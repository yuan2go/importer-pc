using System.Collections.Concurrent;
using System.Text.Json;
using Ipc.Drivers.Hikrobot;
using Ipc.Drivers.Hikrobot.Mvs;
using Ipc.Platform;

internal static class HardeningVerification
{
    static int checks;
    static void Check(bool ok, string reason)
    { if (!ok) throw new InvalidOperationException(reason); checks++; Console.WriteLine("PASS HARDENING: " + reason); }
    static async Task RefuseStart(Func<Task> call)
    {
        try { await call().WaitAsync(TimeSpan.FromSeconds(2)); }
        catch (ObjectDisposedException) { return; }
        throw new InvalidOperationException("Start not fenced by pending disposal");
    }
    static async Task CleanupFailure(Func<Task> call)
    {
        try { await call().WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (IOException) { return; }
        throw new InvalidOperationException("Cleanup error was not reported");
    }
    static MvsAcquisition Mvs(MvsVerification.Fake api, MvsVerification.Lease lease) => new(
        new("hardening.mvs", "left", new Dictionary<string, string> { ["device.ip"] = "192.0.2.1" }), api, _ => lease);
    static HikrobotReader Reader(Verification.FakeApi api, Verification.TestLease lease) => new(
        new("hardening.reader", "left", new Dictionary<string, string> { ["device.ip"] = "192.0.2.1", ["entity.mode"] = "trigger" }), api, _ => lease);
    internal static async Task Run()
    {
        var mvs = new MvsVerification.Fake(); var mvsLease = new MvsVerification.Lease();
        using (var entered = new ManualResetEventSlim())
        using (var release = new ManualResetEventSlim())
        {
            var source = Mvs(mvs, mvsLease);
            mvs.Acquiring = () => { entered.Set(); if (!release.Wait(5000)) throw new TimeoutException(); };
            await source.StartControlledAsync("blocked-acquire", _ => throw new Exception("No fabricated barcode"), _ => { }, default);
            try
            {
                Check(entered.Wait(3000), "MVS native call surrogate is blocked before dispose");
                var first = source.DisposeAsync().AsTask(); var second = source.DisposeAsync().AsTask();
                await RefuseStart(() => source.StartControlledAsync("forbidden", _ => true, _ => { }, default));
                Check(!first.IsCompleted && !second.IsCompleted && mvsLease.Releases == 0, "both disposers retain blocked native owner while Start is already refused");
                release.Set(); await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5)); await source.DisposeAsync();
                Check(mvsLease.Releases == 1 && mvs.Calls.Count(c => c == "stop") == 1 && mvs.Calls.Count(c => c == "destroy") == 1,
                    "concurrent and repeated MVS disposal performs one successful teardown");
            }
            finally { release.Set(); await source.DisposeAsync(); }
        }
        var readerApi = new Verification.FakeApi(); var readerLease = new Verification.TestLease();
        using (var entered = new ManualResetEventSlim())
        using (var release = new ManualResetEventSlim())
        {
            var source = Reader(readerApi, readerLease); var health = new ConcurrentQueue<DeviceHealth>(); int received = 0;
            readerApi.Starting = () => { entered.Set(); if (!release.Wait(5000)) throw new TimeoutException(); };
            var starting = source.StartControlledAsync("starting", _ => { received++; return true; }, h => health.Enqueue(h.Health), default);
            try
            {
                Check(entered.Wait(3000), "reader native Start surrogate is blocked before dispose");
                var first = source.DisposeAsync().AsTask(); var second = source.DisposeAsync().AsTask();
                readerApi.Emit(1, 1, "SYNTHETIC-DURING-DISPOSE");
                await RefuseStart(() => source.StartControlledAsync("forbidden", _ => true, _ => { }, default));
                Check(!first.IsCompleted && readerLease.Releases == 0, "reader disposal retains in-flight Start and refuses new registration");
                release.Set(); await starting; await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5)); await source.DisposeAsync();
                Check(received == 0 && !health.Contains(DeviceHealth.Ready) && readerLease.Releases == 1 && readerApi.Calls.Count(c => c == "destroy") == 1,
                    "dispose during Start neither publishes Ready nor delivers callbacks and frees once");
            }
            finally { release.Set(); await starting; await source.DisposeAsync(); }
        }
        var failingMvs = new MvsVerification.Fake { FailStage = "destroy" }; var heldMvs = new MvsVerification.Lease();
        var retryMvs = Mvs(failingMvs, heldMvs);
        await retryMvs.StartControlledAsync("failed-dispose", _ => true, _ => { }, default);
        await CleanupFailure(() => retryMvs.DisposeAsync().AsTask());
        await RefuseStart(() => retryMvs.StartControlledAsync("forbidden", _ => true, _ => { }, default));
        Check(heldMvs.Releases == 0 && !failingMvs.Calls.Contains("finalize") && retryMvs.Failure.Contains("destroy"), "failed MVS Dispose keeps original native owner, diagnostic failure and Start fence");
        failingMvs.FailStage = ""; await retryMvs.DisposeAsync(); await retryMvs.DisposeAsync();
        Check(heldMvs.Releases == 1 && failingMvs.Calls.Count(c => c == "stop") == 1, "retry Dispose resumes the failed stage without repeating completed native Stop");

        var failingReader = new Verification.FakeApi { FailStop = true }; var heldReader = new Verification.TestLease();
        var retryReader = Reader(failingReader, heldReader);
        await retryReader.StartControlledAsync("failed-dispose", _ => true, _ => { }, default);
        await CleanupFailure(() => retryReader.DisposeAsync().AsTask());
        await RefuseStart(() => retryReader.StartControlledAsync("forbidden", _ => true, _ => { }, default));
        Check(heldReader.Releases == 0 && !failingReader.Calls.Contains("destroy") && retryReader.LastFailure?.Contains("stop failure") == true, "reader Dispose failure retains callbacks, diagnostic failure and physical owner");
        failingReader.FailStop = false; await retryReader.DisposeAsync(); await retryReader.DisposeAsync();
        Check(heldReader.Releases == 1 && failingReader.Calls.Count(c => c == "destroy") == 1, "reader Dispose can retry and completes once");

        var identity = new MvsVerification.Fake(); var identityLease = new MvsVerification.Lease();
        await using (var source = Mvs(identity, identityLease))
        {
            await source.StartControlledAsync("identity-one", _ => true, _ => { }, default); await source.StopAsync(default);
            identity.Serial = "SYNTHETIC-REPLACEMENT";
            bool refused = false;
            try { await source.StartControlledAsync("identity-two", _ => true, _ => { }, default); }
            catch (InvalidDataException ex) when (ex.Message.Contains("physical serial changed")) { refused = true; }
            Check(refused && identity.Calls.Count(c => c == "open") == 1 && identityLease.Releases == 1,
                "MVS optional diagnostic identity remains pinned across Stop/Start before opening a replacement");
        }
        foreach (var notification in new[] { DeviceHealth.Stopping, DeviceHealth.Stopped })
        {
            var api = new Verification.FakeApi(); var lease = new Verification.TestLease(); var source = Reader(api, lease);
            await source.StartControlledAsync("notification", _ => true, h => { if (h.Health == notification) throw new IOException("Synthetic notification failure"); }, default);
            await CleanupFailure(() => source.StopAsync(default)); await source.DisposeAsync();
            Check(lease.Releases == 1 && api.Calls.Count(c => c == "destroy") == 1, notification + " notification failure cannot prevent cleanup or double-release ownership");
        }
        var original = Wire.Encode(new { ParcelCode = "SYNTHETIC-CODE", Codes = new[] { "SYNTHETIC-CODES" },
            Candidates = new[] { new { Text = "SYNTHETIC-CANDIDATE", Characters = 19 } }, Selection = new { SelectedCode = "SYNTHETIC-SELECTED", Matches = 1 },
            Body = Wire.Encode(new { data = new[] { new { @params = new { loadCode = "SYNTHETIC-LOAD", taskId = "kept-task" } } }, token = "SYNTHETIC-TOKEN" }),
            PartialBody = "{\"loadCode\":\"SYNTHETIC-PARTIAL WITH SPACES",
            PartialCandidates = "{\"Candidates\":[\"SYNTHETIC-ONE\",\"SYNTHETIC-TWO",
            StatusCode = 200, ErrorCode = "KEPT-ERROR", CandidateCount = 1 });
        var frozenHash = Wire.Hash(original); var protectedText = DiagnosticText.Protect(original);
        Check(!new[] { "SYNTHETIC-CODE", "SYNTHETIC-CODES", "SYNTHETIC-CANDIDATE", "SYNTHETIC-SELECTED", "SYNTHETIC-LOAD", "SYNTHETIC-TOKEN", "SYNTHETIC-PARTIAL", "WITH SPACES", "SYNTHETIC-ONE", "SYNTHETIC-TWO" }.Any(protectedText.Contains),
            "diagnostic copy masks scalar, array, selected, fragment and nested-JSON barcode/credential values");
        using (var json = JsonDocument.Parse(protectedText))
            Check(json.RootElement.GetProperty("StatusCode").GetInt32() == 200 && json.RootElement.GetProperty("CandidateCount").GetInt32() == 1 &&
                protectedText.Contains("KEPT-ERROR") && protectedText.Contains("kept-task") && Wire.Hash(original) == frozenHash,
                "masking preserves diagnostic status/counts and does not alter original request bytes");
        using (var json = JsonDocument.Parse(protectedText))
        using (var body = JsonDocument.Parse(json.RootElement.GetProperty("Body").GetString()!))
            Check(body.RootElement.GetProperty("data")[0].GetProperty("params").GetProperty("taskId").GetString() == "kept-task",
                "complete nested JSON stays parseable after masking while partial scalar/array fragments fail closed");
        Console.WriteLine("HARDENING_SELF_TEST_PASS checks=" + checks + "; synthetic native ports; real SDK/hardware NOT_RUN");
    }
}

using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Ipc.Drivers.Hikrobot.Mvs;
using Ipc.Platform;

internal static partial class MvsVerification
{
    static int checks;
    static void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException("MVS FAIL: " + text); checks++; Console.WriteLine("PASS MVS: " + text); }
    static async Task Until(Func<bool> condition) { for (var i = 0; i < 500; i++) { if (condition()) return; await Task.Delay(10); } throw new TimeoutException("MVS condition did not occur"); }
    static MvsAcquisition Source(Fake api, Lease lease, Dictionary<string, string>? parameters = null) => new(new("synthetic.mvs", api.Ip, parameters ?? new() { ["device.ip"] = api.Ip }), api, _ => lease);
    internal static async Task Run()
    {
        await VerifyPreview();
        await VerifyAcquisitionDiagnostics();
        await VerifyInspectionShutdown();
        Check(Marshal.SizeOf<MvsNative.FrameInfo>() == 256 && MvsNative.FrameOutBytes == 328 && Marshal.OffsetOf<MvsNative.FrameInfo>("TriggerIndex").ToInt32() == 76 &&
            Marshal.OffsetOf<MvsNative.FrameInfo>("DeviceTimestampHigh").ToInt32() == 12 && Marshal.OffsetOf<MvsNative.FrameInfo>("DeviceTimestampLow").ToInt32() == 16 &&
            Marshal.OffsetOf<MvsNative.FrameInfo>("HostTimestamp").ToInt32() == 24 &&
            Marshal.OffsetOf<MvsNative.FrameInfo>("ExtendedLength").ToInt32() == 120 && MvsNative.DescriptorBytes == 636, "x64 layouts match installed vendor header and official managed wrapper cross-check");
        var api = new Fake(); var lease = new Lease(); var health = new ConcurrentQueue<DeviceHealthConfirmation>(); int observations = 0;
        var stoppedApi = new Fake { StopResult = unchecked((int)0x80000003), CloseResult = unchecked((int)0x80000003), FailStage = "destroy" };
        var stoppedLease = new Lease();
        await using (var source = Source(stoppedApi, stoppedLease))
        {
            await source.StartControlledAsync("already-stopped", _ => true, _ => { }, default);
            try { await source.StopAsync(default); throw new Exception("Destroy failure lost"); }
            catch (IOException) { Check(stoppedLease.Releases == 0 && !stoppedApi.Calls.Contains("finalize"), "call-order stop/close continues teardown but failed destroy retains the original lease and SDK owner"); }
            stoppedApi.FailStage = "";
            await source.StopAsync(default);
            Check(source.Phase == "stopped" && stoppedLease.Releases == 1 && stoppedApi.Calls.Last() == "finalize", "retry completes real destroy/finalize after an already-stopped stream; application can exit");
        }
        await using (var source = Source(api, lease))
        {
            await source.StartControlledAsync("mvs-one", _ => { observations++; return true; }, health.Enqueue, default);
            api.Frames.Enqueue(Frame(1)); api.Frames.Enqueue(Frame(2)); await Until(() => source.Frames == 2 && api.Freed == 2);
            Check(observations == 0 && health.All(h => h.Health != DeviceHealth.Ready && h.ValidUntil is null) && source.ConfirmationSource.Contains("decoder=Unavailable"),
                "actual acquisition contract cannot manufacture a barcode, observation or business Ready when component is absent");
            await source.StopAsync(default);
            Check(lease.Releases == 1 && api.Calls.TakeLast(4).SequenceEqual(["stop", "close", "destroy", "finalize"]) && !api.HasFrame,
                "native loans are returned before stop/close/destroy/finalize and lease release");
            await source.StartControlledAsync("mvs-two", _ => true, health.Enqueue, default);
            api.Frames.Enqueue(Frame(1)); await Until(() => source.Frames == 1);
            Check(source.Epoch == "mvs-two" && source.Failure.Length == 0, "explicit restart resets capture accounting without creating a new parcel");
        }
        var absentApi = new Fake { Discovered = false }; var absentLease = new Lease();
        var absentHealth = new ConcurrentQueue<DeviceHealthConfirmation>();
        await using (var source = Source(absentApi, absentLease))
        {
            try { await source.StartControlledAsync("absent", _ => true, absentHealth.Enqueue, default); throw new Exception("Missing camera started"); }
            catch (ObservationConnectionException) { }
            Check(source.Phase == "disconnected" && absentHealth.Last().Health == DeviceHealth.Disconnected &&
                absentHealth.Last().ValidUntil is null && !absentApi.Calls.Contains("create") && absentApi.Calls.Last() == "finalize" && absentLease.Releases == 0,
                "missing configured camera is offline only after cleanup; no native handle, physical lease or Ready is invented");
            absentApi.Discovered = true;
            await source.StartControlledAsync("returned", _ => true, absentHealth.Enqueue, default);
            Check(source.Epoch == "returned" && source.Failure.Length == 0 && absentApi.Calls.Contains("start"), "explicit reconnect can discover and start the same configured camera after it returns");
            await source.StopAsync(default);
        }
        var failedCleanupApi = new Fake { Discovered = false, FailStage = "finalize" };
        var wrongIdentityApi = new Fake(); var wrongIdentityLease = new Lease();
        await using (var source = Source(wrongIdentityApi, wrongIdentityLease, new()
            { ["device.ip"] = wrongIdentityApi.Ip, ["device.serial"] = "SYNTH-OTHER" }))
        {
            try { await source.StartControlledAsync("wrong-identity", _ => true, _ => { }, default); throw new Exception("Wrong physical camera opened"); }
            catch (ObservationConnectionException) { }
            Check(source.Phase == "disconnected" && !wrongIdentityApi.Calls.Contains("create") && wrongIdentityLease.Releases == 0,
                "a device at the configured IP with a different serial remains offline and is never opened as a fallback");
        }
        await using (var source = Source(failedCleanupApi, new Lease()))
        {
            try { await source.StartControlledAsync("failed-cleanup", _ => true, _ => { }, default); throw new Exception("Failed cleanup was accepted"); }
            catch (AggregateException) { Check(true, "discovery failure with uncompleted native cleanup remains fatal and retains its owner"); }
            failedCleanupApi.FailStage = ""; await source.StopAsync(default);
        }
        var hostFailedApi = new Fake(); var hostFailedLease = new Lease();
        await using (var source = Source(hostFailedApi, hostFailedLease))
        {
            await source.StartControlledAsync("host-failure", _ => true, h =>
            {
                if (h.Health is DeviceHealth.Stopping or DeviceHealth.Stopped) throw new InvalidOperationException("Synthetic host failure");
            }, default);
            await Reject(() => source.StopAsync(default));
            Check(source.Phase == "stopped" && source.Failure.Contains("health receiver failed") && hostFailedLease.Releases == 1 &&
                hostFailedApi.Calls.TakeLast(4).SequenceEqual(["stop", "close", "destroy", "finalize"]),
                "host health exception is explicit but cannot prevent successful native teardown");
            await source.StopAsync(default);
            Check(hostFailedLease.Releases == 1 && hostFailedApi.Calls.Count(c => c == "destroy") == 1,
                "retry after host reporting failure never releases the original native owner twice");
        }
        var retainedApi = new Fake { FailFree = true }; var retainedLease = new Lease();
        await using (var source = Source(retainedApi, retainedLease))
        {
            await source.StartControlledAsync("free-failure", _ => true, _ => { }, default); retainedApi.Frames.Enqueue(Frame(1));
            await Until(() => source.Failure.Length > 0);
            await Reject(() => source.StopAsync(default));
            Check(retainedApi.HasFrame && retainedLease.Releases == 0 && !retainedApi.Calls.Contains("destroy"), "FreeImageBuffer failure retains original loan, handle and physical owner");
            await Reject(() => source.StartControlledAsync("unsafe", _ => true, _ => { }, default));
            retainedApi.FailFree = false; await source.StopAsync(default);
            Check(!retainedApi.HasFrame && retainedLease.Releases == 1 && retainedApi.Freed == 1, "explicit retry returns original frame once then frees original owner");
        }
        var blockedApi = new Fake(); var blockedLease = new Lease();
        using (var entered = new ManualResetEventSlim())
        using (var release = new ManualResetEventSlim())
        await using (var source = Source(blockedApi, blockedLease))
        {
            blockedApi.Acquiring = () => { entered.Set(); release.Wait(); };
            await source.StartControlledAsync("blocked-native", _ => true, _ => { }, default);
            Check(entered.Wait(2000), "native acquisition surrogate entered");
            try
            {
                using var cancelled = new CancellationTokenSource(30);
                try { await source.StopAsync(cancelled.Token); throw new Exception("Falsely stopped"); } catch (OperationCanceledException) { }
                Check(blockedLease.Releases == 0 && !blockedApi.Calls.Contains("destroy"), "cancelled stop wait retains blocked native acquisition owner");
                var retry = source.StopAsync(default); Check(!retry.IsCompleted, "repeat stop joins the actual outstanding acquisition task");
                release.Set(); await retry.WaitAsync(TimeSpan.FromSeconds(3));
                Check(blockedLease.Releases == 1 && blockedApi.Calls.Count(c => c == "destroy") == 1, "actual native return permits one teardown");
            }
            finally { release.Set(); }
        }
        foreach (var stage in new[] { "start", "stop", "close", "destroy", "finalize" })
        {
            var failed = new Fake { FailStage = stage }; var held = new Lease();
            await using var source = Source(failed, held);
            if (stage == "start")
            {
                await Reject(() => source.StartControlledAsync("start-failure", _ => true, _ => { }, default));
                Check(held.Releases == 1 && failed.Calls.Contains("destroy"), "native Start failure cleans returned resources without declaring acquisition success");
            }
            else
            {
                await source.StartControlledAsync(stage, _ => true, _ => { }, default); await Reject(() => source.StopAsync(default));
                Check(held.Releases == 0, stage + " failure retains physical ownership");
                failed.FailStage = ""; await source.StopAsync(default); Check(held.Releases == 1, stage + " retry completes original teardown");
            }
        }
        var left = new Fake(); var right = new Fake { Ip = "192.0.2.2", Serial = "SYNTH-RIGHT" };
        await using (var a = Source(left, new()))
        await using (var b = Source(right, new()))
        {
            await a.StartControlledAsync("left", _ => true, _ => { }, default); await b.StartControlledAsync("right", _ => true, _ => { }, default);
            left.Frames.Enqueue(Frame(1)); left.Frames.Enqueue(Frame(3)); right.Frames.Enqueue(Frame(1)); right.Frames.Enqueue(Frame(2));
            await Until(() => a.Failure.Length > 0 && b.Frames == 2);
            Check(a.Failure.Contains("gap/reset") && b.Failure.Length == 0 && right.Freed == 2, "overload/frame gap faults one acquisition while the other camera remains isolated");
        }
        var noTrigger = new Fake();
        var disconnectedHealth = new System.Collections.Concurrent.ConcurrentQueue<DeviceHealth>();
        await using (var source = Source(noTrigger, new()))
        {
            await source.StartControlledAsync("no-trigger", _ => true, h => disconnectedHealth.Enqueue(h.Health), default); await Task.Delay(1100);
            Check(source.Failure.Length == 0 && source.Frames == 0, "ordinary no-trigger/no-frame is not treated as a disconnection");
            noTrigger.Online = false; await Until(() => source.Failure.Length > 0);
            await Until(() => disconnectedHealth.Contains(DeviceHealth.Disconnected));
            Check(source.Failure.Contains(nameof(ObservationConnectionException)) && disconnectedHealth.Last() == DeviceHealth.Disconnected,
                "actual connection failure fences acquisition and publishes transport disconnection for application-owned recovery");
        }
        bool rejected = false;
        try { _ = Source(new(), new(), new() { ["device.ip"] = "192.0.2.1", ["entity.mode"] = "trigger" }); } catch (InvalidDataException ex) when (ex.Message.Contains("DecoderUnavailable")) { rejected = true; }
        Check(rejected, "formal parcel mode is refused without a real recognition component");
        var invalid = new Fake(); var invalidLease = new Lease();
        await using (var source = Source(invalid, invalidLease))
        {
            await source.StartControlledAsync("invalid", _ => true, _ => { }, default); invalid.Frames.Enqueue(Frame(1) with { LostPackets = 1 });
            await Until(() => source.Failure.Length > 0);
            Check(invalid.Freed == 1 && source.Frames == 0, "invalid/incomplete frame is returned and never counted as a valid image or decoded barcode");
        }
        await SoftwareBoundaries();
        await SamplingTimings();
        Console.WriteLine("MVS_SELF_TEST_PASS checks=" + checks + "; synthetic native ports; real target frames/barcodes NOT_RUN");
    }
    static async Task Reject(Func<Task> call)
    { try { await call(); } catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or AggregateException) { return; } throw new Exception("Expected retained/refused MVS result"); }
    static MvsFrame Frame(uint number) => new(1, 3072, 2048, MvsNative.Mono8, 6291456, number, 0, 0);
    internal sealed class Lease : IDisposable { internal int Releases; public void Dispose() => Releases++; }
    internal sealed class Fake : IMvsApi
    {
        internal string Ip = "192.0.2.1", Serial = "SYNTH-LEFT", FailStage = "";
        internal bool FailFree, Online = true, Discovered = true;
        internal int StopResult, CloseResult;
        internal int Freed;
        internal Action? Acquiring, Enumerating;
        internal ConcurrentQueue<MvsFrame> Frames = new();
        internal ConcurrentQueue<string> Calls = new();
        public bool HasFrame { get; private set; }
        void Call(string stage) { Calls.Enqueue(stage); if (FailStage == stage) throw new IOException("Synthetic MVS " + stage + " failure"); }
        public void Initialize() => Call("initialize");
        public MvsDevice[] Enumerate() { Enumerating?.Invoke(); return Discovered ? [new(Ip, Serial, "SYNTHETIC", "SYNTHETIC", [])] : []; }
        public void Create(MvsDevice device) => Call("create");
        public void Open() => Call("open");
        public uint EnumValue(string key) => key == "PixelFormat" ? MvsNative.Mono8 : key == "AcquisitionMode" ? 2u : 0;
        public long PayloadSize() => 6291456;
        public void ConfigureBuffers(uint nodes) => Call("buffers");
        public void Start() => Call("start");
        public void ClearBuffers() { if (HasFrame) throw new InvalidOperationException("Cannot clear a borrowed frame"); Frames.Clear(); Call("clear"); }
        public MvsFrame? Acquire() { Acquiring?.Invoke(); if (HasFrame) throw new InvalidOperationException("Loan still held"); if (Frames.TryDequeue(out var f)) { HasFrame = true; return f; } Thread.Sleep(5); return null; }
        public void Release() { if (FailFree) throw new IOException("Synthetic FreeImageBuffer failure"); if (HasFrame) { Freed++; HasFrame = false; } }
        public bool Connected() => Online;
        public void Stop() { Call("stop"); MvsNative.CheckTeardownTransition(StopResult, "StopGrabbing"); }
        public void Close() { Call("close"); MvsNative.CheckTeardownTransition(CloseResult, "CloseDevice"); }
        public void Destroy() => Call("destroy");
        public void Shutdown() => Call("finalize");
    }
}

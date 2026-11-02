using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Platform;

static class IndustrialDiagnostics
{
    public static async Task Run(string workspace, string root)
    {
        var checks = 0;
        void Check(bool value, string message)
        {
            if (!value) throw new InvalidDataException("FAIL IE01-DIAGNOSTICS " + message);
            checks++; Console.WriteLine("PASS IE01-DIAGNOSTICS " + message);
        }
        async Task Reject<T>(Func<Task> action, string message) where T : Exception
        {
            try { await action(); } catch (T) { Check(true, message); return; }
            throw new InvalidDataException("Expected " + typeof(T).Name + ": " + message);
        }
        bool NoPartials() => !Directory.EnumerateFiles(root, "*.partial-*").Any();
        await using var h = new ManualSupplyHarness(workspace, Path.Combine(root, "live"), deferred: true);
        await h.StartAsync();
        var api = (IDiagnosticPackageApplication)h.App;
        var denied = Path.Combine(root, "denied.zip");
        await Reject<UnauthorizedAccessException>(() => api.ExportDiagnosticPackageReportAsync(denied, default), "ordinary user cannot read detailed sources");
        Check(!File.Exists(denied) && NoPartials(), "unauthorized export creates no output");
        await ManagementModeFixture.Authorize(h.App);
        var snapshot = await h.App.SnapshotAsync();
        var canary = "CANARY-" + Guid.NewGuid().ToString("N");
        var hostile = snapshot with { ApplicationId = canary, State = canary, BlockingReasons = [canary],
            Health = [new(canary, true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(1), canary)],
            Capacity = snapshot.Capacity! with { Policy = canary },
            Configuration = snapshot.Configuration with { Status = canary, Failure = canary,
                Effective = snapshot.Configuration.Effective with { StationId = canary, StationCode = canary } } };
        var projection = new StationDiagnosticProjection();
        Check(!Wire.Encode(projection.Station(hostile)).Contains(canary), "health/configuration/free-text values cannot bypass allowlist");
        var hostileEntry = new SoftwareLogEntry(new(canary, 1, canary, canary), canary, DateTimeOffset.UtcNow, canary,
            Wire.Encode(new { PaSsWoRd = canary, Detail = new { RequestId = canary, Result = new { Secret = canary } } }))
            { OperationId = canary, EventCode = canary };
        var projected = Wire.Encode(projection.Event(hostileEntry));
        Check(!projected.Contains(canary) && projected.Contains("operation-"), "nested/unknown fields excluded and arbitrary identity aliased");
        Check(Wire.Encode(projection.Event(hostileEntry)) == projected, "references remain consistent inside one package");

        var logs = Path.Combine(h.Launch.DataRoot, h.Config.Mode.ToString(), h.Config.StationId, h.Config.ApplicationId, "diagnostics");
        for (var i = 0; i < 2005; i++)
            RuntimeJournal.AppendMetadata(logs, new { At = DateTimeOffset.UtcNow.AddSeconds(-1), Source = "RCS结果", Level = "信息",
                Event = canary, Detail = Wire.Encode(new { OperationId = Wire.Id(), RequestId = canary, PositionId = canary,
                    Result = new { password = canary }, NextAction = canary, Authorization = "Bearer " + canary,
                    Url = "https://example.invalid/?token=" + canary }) });
        var requests = h.Peer.Requests.Count;
        var target = Path.Combine(root, "diagnostics.ZIP");
        var receipt = await api.ExportDiagnosticPackageReportAsync(target, default);
        Check(receipt.Path == target && receipt.Status == DiagnosticPackageStatus.Partial, "receipt explicitly reports retained-window partial package");
        Check(receipt.Bytes == new FileInfo(target).Length && receipt.Sha256 == Wire.FileHash(target), "receipt identifies actual committed ZIP bytes");
        Check(receipt.Cutoff - receipt.From == TimeSpan.FromHours(24), "one fixed 24-hour cutoff for all log pages");
        using (var archive = ZipFile.OpenRead(target))
        {
            Check(archive.Entries.Count == 6 && archive.Entries.All(e => e.FullName.EndsWith(".json", StringComparison.Ordinal)), "only six bounded JSON artifacts published");
            using var manifest = JsonDocument.Parse(archive.GetEntry("manifest.json")!.Open());
            Check(manifest.RootElement.GetProperty("Schema").GetInt32() == 2 && manifest.RootElement.GetProperty("Status").GetString() == "Partial", "manifest records privacy version and partial status");
            foreach (var file in manifest.RootElement.GetProperty("Files").EnumerateObject())
            {
                using var input = archive.GetEntry(file.Name)!.Open();
                Check(Convert.ToHexString(SHA256.HashData(input)) == file.Value.GetString(), "actual artifact hash: " + file.Name);
            }
            foreach (var entry in archive.Entries)
            {
                using var reader = new StreamReader(entry.Open());
                var text = await reader.ReadToEndAsync();
                Check(!text.Contains(canary) && !text.Contains(h.Peer.Endpoint) && !text.Contains(h.Launch.DataRoot), "no canary, endpoint or local identity path: " + entry.FullName);
            }
            using var index = JsonDocument.Parse(archive.GetEntry("event-index.json")!.Open());
            Check(index.RootElement.GetProperty("Truncated").GetBoolean() && index.RootElement.GetProperty("Events").GetArrayLength() == 2000, "log limit reports exact truncation instead of complete success");
        }
        Check(h.Peer.Requests.Count == requests, "export causes no business RCS calls");
        await Reject<IOException>(() => api.ExportDiagnosticPackageReportAsync(target, default), "existing ZIP cannot be overwritten");
        Check(Wire.FileHash(target) == receipt.Sha256, "existing committed ZIP bytes unchanged");
        await Reject<IOException>(() => api.ExportDiagnosticPackageReportAsync(Path.Combine(h.Launch.DataRoot, "inside.zip"), default), "managed data root is not an export destination");
        await Reject<IOException>(() => api.ExportDiagnosticPackageReportAsync("relative.zip", default), "relative paths rejected");

        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            await Reject<OperationCanceledException>(() => api.ExportDiagnosticPackageReportAsync(Path.Combine(root, "cancelled.zip"), cancelled.Token), "cancel before source read honored");
        }
        var damaged = Path.Combine(root, "damaged.zip");
        h.App.Boundary = phase =>
        {
            if (phase == "AfterDiagnosticPackageWrite")
                using (var stream = new FileStream(Directory.GetFiles(root, "damaged.zip.partial-*").Single(), FileMode.Open, FileAccess.Write)) stream.SetLength(0);
        };
        try { await Reject<InvalidDataException>(() => api.ExportDiagnosticPackageReportAsync(damaged, default), "closed ZIP is verified before atomic publication"); }
        finally { h.App.Boundary = null; }
        Check(!File.Exists(damaged) && NoPartials(), "failed validation removes temporary output");
        var revoked = Path.Combine(root, "revoked.zip");
        h.App.Boundary = phase => { if (phase == "BeforeDiagnosticPackagePublish") LocalManagementMode.ForRoot(h.Launch.DataRoot).Exit(); };
        try
        {
            try { await api.ExportDiagnosticPackageReportAsync(revoked, default); throw new Exception("revoked export published"); }
            catch (Exception error) when (error is OperationCanceledException or UnauthorizedAccessException) { Check(true, "authority rechecked at actual publication"); }
        }
        finally { h.App.Boundary = null; }
        Check(!File.Exists(revoked) && NoPartials(), "revoked export releases source custody and temporary file");
        await ManagementModeFixture.Authorize(h.App);
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        h.App.Boundary = phase => { if (phase == "BeforeDiagnosticPackagePublish") { reached.TrySetResult(); if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("fixture publish gate"); } };
        var first = api.ExportDiagnosticPackageReportAsync(Path.Combine(root, "single.zip"), default);
        try
        {
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await Reject<InvalidOperationException>(() => api.ExportDiagnosticPackageReportAsync(Path.Combine(root, "parallel.zip"), default), "parallel export rejected without waiting queue");
        }
        finally { release.Set(); await first; h.App.Boundary = null; }
        Check(!File.Exists(Path.Combine(root, "parallel.zip")) && NoPartials(), "single owner releases after actual completion");
        Console.WriteLine("IE01-DIAGNOSTICS checks=" + checks + "; actual ZIP/SQLite/loopback; native/system/real devices NOT_RUN");
    }
}

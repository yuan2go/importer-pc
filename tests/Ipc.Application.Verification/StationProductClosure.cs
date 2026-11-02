using System.IO.Compression;
using System.Security.Cryptography;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Platform;
using Microsoft.Data.Sqlite;

static class StationProductClosure
{
    static void Check(bool value, string message)
    { if (!value) throw new InvalidDataException(message); Console.WriteLine("PASS PC21 " + message); }
    static DateTimeOffset Minute(DateTimeOffset value) => new(value.UtcTicks - value.UtcTicks % TimeSpan.TicksPerMinute, TimeSpan.Zero);
    public static async Task Run(string workspace, string root)
    {
        var policy = new StationOperationsPolicy("Singapore Standard Time", [new("早班", 360, 840), new("中班", 840, 1320), new("夜班", 1320, 360)]);
        Check(StationShiftSchedule.Validate(policy).Length == 0, "three adjacent shifts cover a full day including overnight");
        var midnight = new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.FromHours(8));
        var night = StationShiftSchedule.Current(policy, midnight);
        Check(night.Name == "夜班" && night.From == midnight.AddHours(-2) && night.ToExclusive == midnight.AddHours(6), "overnight shift belongs to its original start date");
        Check(StationShiftSchedule.Current(policy, night.ToExclusive).Name == "早班", "exact shift boundary belongs only to following shift");
        Check(StationShiftSchedule.Validate(policy with { Shifts = [new("a", 0, 800), new("b", 700, 1440)] }).Length != 0, "overlap cannot silently double count");
        Check(StationShiftSchedule.Validate(policy with { Shifts = [new("a", 0, 600), new("b", 700, 1440)] }).Length != 0, "gap is rejected rather than losing a production interval");
        Check(StationShiftSchedule.Validate(policy with { TimeZoneId = "not-a-zone" }).Length != 0, "invalid station timezone rejected");
        var dst = new StationOperationsPolicy("Eastern Standard Time", [new("自然日", 0, 1440)]);
        var spring = StationShiftSchedule.Current(dst, DateTimeOffset.Parse("2026-03-08T12:00:00Z"));
        var fall = StationShiftSchedule.Current(dst, DateTimeOffset.Parse("2026-11-01T12:00:00Z"));
        Check(spring.ToExclusive - spring.From == TimeSpan.FromHours(23) && fall.ToExclusive - fall.From == TimeSpan.FromHours(25), "DST calendar shifts retain 23/25 real hours");

        await using var h = new ManualSupplyHarness(workspace, Path.Combine(root, "live"), deferred: true);
        ExtensionStore.AtomicWrite(Path.Combine(h.Launch.ConfigurationDirectory, "business.json"), h.Config with { Retention = (h.Config.Retention ?? new()) with { HistoryDays = 7 } });
        await h.StartAsync(); var api = (IStationOperations)h.App; var from = Minute(DateTimeOffset.UtcNow.AddMinutes(-1));
        for (var i = 0; i < 2; i++)
        {
            var admitted = await h.App.SubmitParcelBarcodeAsync(await h.Command("left", "SAME-SYNTHETIC-CODE"), default);
            Check(admitted.Status == "Admitted", "independent same-code input admitted " + i);
            await CameraRoundHarness.Until(async () => (await h.App.OperationAsync(admitted.OperationId!))?.Wcs?.State == StageState.Accepted);
        }
        var stats = await api.ReadStatisticsAsync(from, Minute(DateTimeOffset.UtcNow).AddMinutes(1), default);
        Check(stats.Buckets.Sum(x => x.Requests) == 2 && stats.Buckets.Sum(x => x.Assigned) == 2 && stats.Buckets.Sum(x => x.ManualRequests) == 2 && stats.Buckets.Sum(x => x.Recognitions) == 0,
            "read-only production statistics count two requests without fabricating camera recognition or unique physical parcels");
        try { await api.ReadStatisticsAsync(from, from.AddDays(32), default); throw new Exception("range accepted"); } catch (ArgumentException) { }
        Check(true, "statistics range budget is enforced before executing query");
        ParcelOperation original; string originalRequest;
        try { await api.ExportDiagnosticPackageAsync(Path.Combine(root, "unauthorized.zip"), default); throw new Exception("export authorized"); } catch (UnauthorizedAccessException) { }
        Check(!File.Exists(Path.Combine(root, "unauthorized.zip")), "diagnostic package requires management authority before file creation");
        await ManagementModeFixture.Authorize(h.App);
        original = (await h.App.SnapshotAsync()).Operations.First(); originalRequest = Wire.Encode(original.Wcs!.Request);
        var logPage = await h.App.QueryLogsAsync(new(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddMinutes(1), Source: "RCS结果"), null, 0, default);
        try { Check(!(await h.App.DiagnosticSettingsAsync()).DebugEnabled && logPage.Entries.Count(e => e.Level == "信息" && e.Text.Contains("RequestId")) >= 2, "ordinary RCS result summaries are queryable with debug mode disabled"); }
        finally { h.App.ReleaseLogSelection(logPage.Selection); }

        var before = await h.App.SnapshotAsync(); var rules = before.Configuration.Effective with { BarcodeFilter = new("^NEW", RejectMultipleCodes: true) };
        var applied = await h.App.StageConfigurationAsync(before.Configuration.Revision, rules, "Synthetic live rule verification");
        Check(applied.Status == "Effective", "resource-independent barcode rules activate through the live barrier");
        var inspection = await h.App.InspectConfigurationSubmissionAsync(before.RuntimeId, before.Configuration.Revision, rules);
        Check(inspection.Status == "Effective" && (await h.App.SnapshotAsync()).Configuration.Pending is null, "save outcome inspection confirms live activation without a restart candidate");
        Check(Wire.Encode((await h.App.OperationAsync(original.Id))!.Wcs!.Request) == originalRequest && Wire.Encode((await h.App.OperationAsync(original.Id))!.FrozenConfiguration) == Wire.Encode(original.FrozenConfiguration),
            "previous accepted operation keeps frozen configuration and exact original request bytes");
        var stale = await h.App.StageConfigurationAsync(before.Configuration.Revision, rules with { BarcodeFilter = new("^OTHER") }, "Synthetic stale editor");
        Check(stale.Status == "Rejected", "stale live rule CAS is rejected");
        var posts = h.Peer.Requests.Count;
        var blocked = await h.App.SubmitParcelBarcodeAsync(await h.Command("left", "OLD-NO-MATCH"), default);
        Check(blocked.Status != "Admitted" && h.Peer.Requests.Count == posts, "new inputs use newly activated rules before any RCS call");
        var zip = Path.Combine(root, "diagnostics.zip"); await api.ExportDiagnosticPackageAsync(zip, default);
        using (var archive = ZipFile.OpenRead(zip))
        {
            Check(archive.Entries.Count == 3 && archive.GetEntry("manifest.json") is not null, "diagnostic package publishes its bounded entries and integrity manifest");
            using (var manifest = System.Text.Json.JsonDocument.Parse(archive.GetEntry("manifest.json")!.Open()))
                foreach (var file in manifest.RootElement.GetProperty("Files").EnumerateObject())
                { using var bytes = archive.GetEntry(file.Name)!.Open(); Check(Convert.ToHexString(SHA256.HashData(bytes)) == file.Value.GetString(), "diagnostic manifest matches actual ZIP bytes: " + file.Name); }

            foreach (var entry in archive.Entries)
            {
                using var reader = new StreamReader(entry.Open()); var value = reader.ReadToEnd();
                Check(!value.Contains("SAME-SYNTHETIC-CODE") && !value.Contains(h.Peer.Endpoint), "diagnostic allowlist excludes raw barcode and RCS endpoint: " + entry.FullName);
            }
        }
        var zipHash = Wire.FileHash(zip);
        try { await api.ExportDiagnosticPackageAsync(zip, default); throw new Exception("overwrite allowed"); } catch (IOException) { }
        Check(Wire.FileHash(zip) == zipHash, "existing diagnostic package cannot be overwritten");
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            try { await api.ExportDiagnosticPackageAsync(Path.Combine(root, "cancelled.zip"), cancelled.Token); throw new Exception("cancel ignored"); } catch (OperationCanceledException) { }
        }
        Check(!File.Exists(Path.Combine(root, "cancelled.zip")) && !Directory.EnumerateFiles(root, "*.partial-*").Any(), "cancelled export publishes no completed file and releases temporary output");
        var revokedPath = Path.Combine(root, "revoked.zip");
        h.App.Boundary = phase => { if (phase == "BeforeDiagnosticPackagePublish") LocalManagementMode.ForRoot(h.Launch.DataRoot).Exit(); };
        try { await api.ExportDiagnosticPackageAsync(revokedPath, default); throw new Exception("revoked export published"); }
        catch (Exception error) when (error is OperationCanceledException or UnauthorizedAccessException) { }
        finally { h.App.Boundary = null; }
        Check(!File.Exists(revokedPath) && !Directory.EnumerateFiles(root, "*.partial-*").Any(), "revocation immediately before publication removes the partial file and releases log custody");
        await ManagementModeFixture.Authorize(h.App);
        var snapshot = await h.App.SnapshotAsync();
        var saved = await api.SaveOperationsPolicyAsync(snapshot.Configuration.Revision, policy with { Shifts = (await api.ReadOperationsPolicyAsync()).Shifts, AutomaticHistoryCleanup = true, CleanupHour = 0 });
        Check(saved.Status == "PendingRestart", "timezone and maintenance policy uses complete-candidate restart activation: " + saved.Status + " " + saved.Reason);
        await h.App.DisposeAsync(); await h.StartAsync(); api = h.App;
        Check((await api.ReadOperationsPolicyAsync()).TimeZoneId == policy.TimeZoneId && (await api.ReadCleanupStatusAsync()).Enabled, "configured timezone and automatic cleanup survive confirmed restart; legacy schedule fields remain compatible");
        stats = await api.ReadStatisticsAsync(from, Minute(DateTimeOffset.UtcNow).AddMinutes(1), default);
        Check(stats.Buckets.Sum(x => x.Requests) == 2, "restart does not recount or lose original request statistics");

        var maintenanceNow = DateTimeOffset.UtcNow.AddDays(9);
        h.App.CleanupClockForVerification = new FixedClock(maintenanceNow);
        var releaseReads = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.App.BeforeRead = cancellation => releaseReads.Task.WaitAsync(cancellation);
        var occupied = Enumerable.Range(0, 4).Select(_ => api.ReadStatisticsAsync(from, Minute(DateTimeOffset.UtcNow).AddMinutes(1), default)).ToArray();
        try
        {
            await h.App.CheckAutomaticCleanupAsync(maintenanceNow);
            Check((await api.ReadCleanupStatusAsync()).State == "等待后台读取完成", "full read capacity defers automatic maintenance instead of failing the business worker");
        }
        finally { releaseReads.TrySetResult(); h.App.BeforeRead = null; await Task.WhenAll(occupied); }
        maintenanceNow = maintenanceNow.AddMinutes(2); h.App.CleanupClockForVerification = new FixedClock(maintenanceNow);
        await h.App.CheckAutomaticCleanupAsync(maintenanceNow);
        var cleaned = await api.ReadCleanupStatusAsync();
        Check(cleaned.State == "本轮完成", "automatic maintenance completes its full backup and bounded retention pass: " + cleaned.State + " " + cleaned.Detail);
        stats = await api.ReadStatisticsAsync(from, Minute(DateTimeOffset.UtcNow).AddMinutes(1), default);
        Check(stats.Buckets.Sum(x => x.Requests) == 2, "automatic maintenance retains statistics after removing expired details");
        Check((await h.App.SnapshotAsync()).Configuration.Effective.BarcodeFilter?.Pattern == "^NEW", "automatic maintenance releases its fence and preserves effective rules");
        await h.App.CheckAutomaticCleanupAsync(maintenanceNow.AddDays(1));
        Check((await api.ReadCleanupStatusAsync()).State == "时钟待核对", "unexpected wall-clock jump fences automatic deletion without replay or rollback");
        Retention(root, Directory.EnumerateFiles(Path.Combine(h.Launch.DataRoot, "maintenance-backups"), "*.db", SearchOption.AllDirectories).Single(), original.FrozenConfiguration);
        await h.App.DisposeAsync();
        var stateFile = Path.Combine(h.Launch.DataRoot, h.Config.Mode.ToString(), h.Config.StationId, "sportmaster", "automatic-cleanup.json");
        File.WriteAllText(stateFile, "synthetic invalid maintenance state"); var stateHash = Wire.FileHash(stateFile);
        await h.StartAsync(); await ManagementModeFixture.Authorize(h.App); api = h.App;
        var retryNow = DateTimeOffset.UtcNow.AddDays(10); h.App.CleanupClockForVerification = new FixedClock(retryNow);
        await h.App.CheckAutomaticCleanupAsync(retryNow);
        Check((await api.ReadCleanupStatusAsync()).State == "维护状态待核对" && Wire.FileHash(stateFile) == stateHash && (await h.App.SnapshotAsync()).Positions!.All(p => p.BarcodeInput?.Manual == true),
            "corrupt optional maintenance state disables deletion, preserves exact bytes and keeps eligible manual business available");
        Console.WriteLine("PC21 ROOT=" + root);
    }
    sealed class FixedClock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
    static void Retention(string root, string completeBackup, ParcelConfiguration configuration)
    {
        var directory = Path.Combine(root, "retention", configuration.Mode.ToString(), configuration.StationId, "sportmaster"); var now = DateTimeOffset.UtcNow;
        ParcelRecovery.Restore(completeBackup, directory);
        var path = Path.Combine(directory, "sportmaster.db");
        using (var repo = new ParcelRepository(directory, "sportmaster"))
        {
            repo.Commit(null, "SyntheticOfflinePause", "isolated full custody backup", paused: true);
            var from = Minute(now.AddDays(-1)); var to = Minute(now.AddDays(1));
            Check(StationStatisticsReader.Read(path, from, to, default).Sum(x => x.Requests) == 2, "full-custody archived requests counted before detail retention");
            var result = repo.ExpireHistoryCore(new SportmasterFlow(), 7, now.AddDays(9), new string('A', 64), () => { });
            Check(result.Operations == 2 && StationStatisticsReader.Read(path, from, to, default).Sum(x => x.Requests) == 2, "detail deletion and retained metric buckets commit atomically without changing total");
            Check(repo.ExpireHistoryCore(new SportmasterFlow(), 7, now.AddDays(9), new string('B', 64), () => { }).Operations == 0 && StationStatisticsReader.Read(path, from, to, default).Sum(x => x.Requests) == 2, "repeated retention never repeats request statistics");
        }
        var old = Path.Combine(root, "unsupported10"); Directory.CreateDirectory(old); File.Copy(path, Path.Combine(old, "sportmaster.db"));
        using (var db = new SqliteConnection("Data Source=" + Path.Combine(old, "sportmaster.db") + ";Pooling=False"))
        { db.Open(); using var c = db.CreateCommand(); c.CommandText = "UPDATE platform_migrations SET version=10"; c.ExecuteNonQuery(); }
        var hash = Wire.FileHash(Path.Combine(old, "sportmaster.db"));
        try { using var rejected = new ParcelRepository(old, "sportmaster"); throw new Exception("DB10 admitted"); } catch (InvalidDataException) { }
        Check(Wire.FileHash(Path.Combine(old, "sportmaster.db")) == hash, "noncurrent DB10 is refused read-only without modifying original bytes");
    }
}

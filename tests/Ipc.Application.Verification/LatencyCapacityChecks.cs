using System.Diagnostics;
using System.Reflection;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Platform;
using Ipc.Platform.Storage;
using Microsoft.Data.Sqlite;

static class LatencyCapacityChecks
{
    sealed class BackupFactory(BusinessLaunch launch) : IParcelCapabilityFactory
    {
        readonly CommercialProductization.SyntheticFactory inner = new(launch);
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly ManualResetEventSlim Release = new(false);
        public ParcelConfiguration Normalize(ParcelConfiguration c) => inner.Normalize(c);
        public void Validate(ParcelConfiguration c) => inner.Validate(c);
        public void ValidateStructure(ParcelConfiguration c) => inner.ValidateStructure(c);
        public ParcelPorts Create(ParcelConfiguration c) => inner.Create(c);
        public void RegisterBackup(string file, ParcelConfiguration c) { Entered.TrySetResult(); Release.Wait(); }
        public ValueTask DisposeAsync() { Release.Dispose(); return inner.DisposeAsync(); }
    }
    public static async Task Run(string workspace, string root)
    {
        var checks = new List<object>(); var failed = new List<string>();
        void Check(bool value, string name, object evidence)
        { checks.Add(new { Name = name, Passed = value, Evidence = evidence }); Console.WriteLine((value ? "PASS " : "FAIL ") + name); if (!value) failed.Add(name); }
        var config = Path.Combine(root, "config"); Directory.CreateDirectory(config);
        var c = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace, "config/sportmaster/business.json"))) with { BackupSchedule = null };
        File.WriteAllText(Path.Combine(config, "business.json"), Wire.Encode(c));
        var launch = new BusinessLaunch(config, Path.Combine(root, "data"), null, null);
        var factory = new BackupFactory(launch);
        await using var runtime = new ParcelRuntime(launch, new SportmasterFlow(), factory) { RuntimeIdentityPreflight = () => { } };
        await runtime.StartAsync(default); await ManagementModeFixture.Authorize(runtime);
        var snapshot = await runtime.SnapshotAsync(); await runtime.CommandAsync(new(Wire.Id(), snapshot.Revision, "Pause"));
        const BindingFlags hidden = BindingFlags.NonPublic | BindingFlags.Instance;
        var repo = (ParcelRepository)typeof(ParcelRuntime).GetField("repository", hidden)!.GetValue(runtime)!;
        var gate = (SemaphoreSlim)typeof(ParcelRuntime).GetField("gate", hidden)!.GetValue(runtime)!;
        await gate.WaitAsync();
        try
        {
            var db = (SqliteConnection)typeof(ParcelRepository).GetField("db", hidden)!.GetValue(repo)!;
            using var fill = db.CreateCommand();
            fill.CommandText = "WITH RECURSIVE n(x) AS (SELECT 1 UNION ALL SELECT x+1 FROM n WHERE x<128) INSERT INTO parcel_audit(at,event,owner,detail) SELECT '2026-10-26T00:00:00Z','SyntheticSlowStorage','performance-only',replace(hex(zeroblob(262144)),'00','R') FROM n";
            fill.ExecuteNonQuery();
        }
        finally { gate.Release(); }
        var sourceBytes = repo.StorageBytes;
        var copying = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delayCalls = new List<long>();
        var observer = typeof(Database).GetProperty("BackupCopyBytesForVerification", BindingFlags.Static | BindingFlags.NonPublic)!;
        observer.SetValue(null, (Action<long>)(bytes => { delayCalls.Add(bytes); copying.TrySetResult(); Thread.Sleep(TimeSpan.FromSeconds(bytes / (8d * 1024 * 1024))); }));
        var backupPath = Path.Combine(root, "cancelled-backup.db");
        var backup = runtime.BackupAsync(backupPath);
        await factory.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var reading = Task.Run(runtime.SnapshotAsync);
        await Task.Delay(100);
        var readCompletedDuringIo = reading.IsCompleted;
        factory.Release.Set();
        await copying.Task.WaitAsync(TimeSpan.FromSeconds(15));
        var stopClock = Stopwatch.StartNew(); var stop = runtime.StopAsync(default);
        try { try { await backup; } catch (OperationCanceledException) { } await reading; await stop; }
        finally { observer.SetValue(null, null); }
        Check(readCompletedDuringIo, "ST03 snapshot gate is released across backup storage work", new { SnapshotCompletedBeforeBarrierRelease = readCompletedDuringIo });
        var partialBytes = File.Exists(backupPath) ? new FileInfo(backupPath).Length : 0;
        Check(partialBytes < sourceBytes, "ST03 stop cancellation interrupts the copying budget", new { SourceBytes = sourceBytes, PartialBytes = partialBytes, StopMs = stopClock.Elapsed.TotalMilliseconds, ThrottledBytesPerSecond = 8 * 1024 * 1024, DelayCalls = delayCalls, Scope = "Controlled managed storage pacing; not a physical slow-volume test", CompletedMarker = File.Exists(backupPath + ".recovery/COMPLETED.json") });

        var launch2 = launch with { DataRoot = Path.Combine(root, "other-data") };
        await using var other = new ParcelRuntime(launch2, new SportmasterFlow(), new CommercialProductization.SyntheticFactory(launch2)) { RuntimeIdentityPreflight = () => { } };
        await other.StartAsync(default);
        var reserve = c.Retention?.MinimumFreeBytes ?? 67108864;
        var capacityGate = (SemaphoreSlim)typeof(ParcelRuntime).GetField("gate", hidden)!.GetValue(other)!;
        await capacityGate.WaitAsync();
        try
        {
            typeof(ParcelRuntime).GetField("storageChecked", hidden)!.SetValue(other, DateTimeOffset.MinValue);
            other.StorageAvailableBytesForVerification = () => reserve * 3;
            typeof(ParcelRuntime).GetMethod("RefreshStorageCapacity", hidden)!.Invoke(other, null);
        }
        finally { capacityGate.Release(); }
        var early = await other.SnapshotAsync();
        Check(early.AuxiliaryReasons!.Any(x => x.Contains("StorageGrowthWarning", StringComparison.Ordinal)), "ST10 early capacity warning precedes the existing 2x reserve warning", new { FreeBytes = reserve * 3, Reserve = reserve, early.AuxiliaryReasons });

        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        other.BeforeRead = token => { entered.TrySetResult(); return release.Task.WaitAsync(token); };
        var selection = await other.SelectHistoryAsync(new());
        var first = other.ExportHistoryAsync(selection, Path.Combine(root, "export-one.csv"), default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = other.ExportHistoryAsync(selection, Path.Combine(root, "export-two.csv"), default);
        await Task.Delay(100);
        var rejected = second.IsFaulted;
        release.TrySetResult();
        await first;
        try { await second; } catch (InvalidOperationException) { }
        other.BeforeRead = null;
        Check(rejected, "ST11 at most one export owns a read transaction", new { SecondExportRejected = rejected });
        var wal = (await other.SnapshotAsync()).AuxiliaryReasons!.Any(x => x.Contains("WalCheckpoint", StringComparison.Ordinal));
        Check(wal, "ST11 periodic passive checkpoint observations are visible", new { ObservationPresent = wal });
        var scheduledRoot = Path.Combine(root, "scheduled"); var scheduledConfig = Path.Combine(scheduledRoot, "config");
        var scheduledTarget = Path.Combine(scheduledRoot, "backups"); Directory.CreateDirectory(scheduledConfig); Directory.CreateDirectory(scheduledTarget);
        var scheduled = c with { BackupSchedule = new(scheduledTarget, 60, 1) };
        File.WriteAllText(Path.Combine(scheduledConfig, "business.json"), Wire.Encode(scheduled));
        var scheduledLaunch = new BusinessLaunch(scheduledConfig, Path.Combine(scheduledRoot, "data"), null, null);
        await using var scheduledRuntime = new ParcelRuntime(scheduledLaunch, new SportmasterFlow(), new CommercialProductization.SyntheticFactory(scheduledLaunch)) { RuntimeIdentityPreflight = () => { } };
        await scheduledRuntime.StartAsync(default); await ManagementModeFixture.Authorize(scheduledRuntime);
        var scheduledSnapshot = await scheduledRuntime.SnapshotAsync();
        await scheduledRuntime.CommandAsync(new(Wire.Id(), scheduledSnapshot.Revision, "Pause"));
        var scheduleAt = DateTimeOffset.UtcNow;
        await scheduledRuntime.CheckScheduledBackupAsync(scheduleAt);
        await scheduledRuntime.CheckScheduledBackupAsync(scheduleAt.AddMinutes(61));
        var complete = Directory.EnumerateFiles(scheduledTarget, "COMPLETED.json", SearchOption.AllDirectories).ToArray();
        Check(complete.Length == 1, "ST03 scheduled backup publishes a complete recovery set through the owned cancellable path", new { CompletedSets = complete.Length });
        await scheduledRuntime.CheckScheduledBackupAsync(scheduleAt.AddMinutes(122));
        Check(Directory.EnumerateFiles(scheduledTarget, "COMPLETED.json", SearchOption.AllDirectories).Count() == 1,
            "ST03 scheduled backup keeps the configured set limit and never deletes an earlier set", new { MaximumSets = 1 });
        ExtensionStore.AtomicWrite(Path.Combine(root, "LATENCY-CAPACITY-CHECKS.json"), new { Checks = checks, Failed = failed });
        if (failed.Count != 0) throw new InvalidOperationException("Measured latency/capacity regressions: " + string.Join("; ", failed));
    }
}

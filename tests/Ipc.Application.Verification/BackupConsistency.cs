using System.Diagnostics;
using System.Text.Json;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Platform;
using Ipc.Platform.Storage;
using Microsoft.Data.Sqlite;
using System.Reflection;

internal static class BackupConsistency
{
    static void Check(bool ok, string detail) { if (!ok) throw new InvalidOperationException(detail); Console.WriteLine("PASS BACKUP " + detail); }
    static DateTimeOffset Progress(ParcelRuntime app)
    {
        using var json = JsonDocument.Parse(Wire.Encode(app.RuntimeProgress()));
        return json.RootElement.GetProperty("Background").EnumerateArray().Single(b => b.GetProperty("Name").GetString() == "maintenance").GetProperty("LastProgress").GetDateTimeOffset();
    }
    static async Task Command(ParcelRuntime app, string kind)
    { var s = await app.SnapshotAsync(); Check((await app.CommandAsync(new(Wire.Id(), s.Revision, kind))).Status == "Persisted", kind + " uses actual application command"); }
    public static async Task Run(string workspace, string root)
    {
        foreach (var outcome in new[] { "complete", "cancel", "copy-failure" }) await Interleaving(workspace, Path.Combine(root, outcome), outcome == "cancel", outcome == "copy-failure");
        foreach (var mode in new[] { "corrupt", "write-failure", "disabled" }) await ScheduleFault(workspace, Path.Combine(root, mode), mode);
        await ScheduleOutcomes(workspace, Path.Combine(root, "outcomes"));
        foreach (var mode in new[] { "full", "io", "custody-destination" }) await CopyFailureIsolation(workspace, Path.Combine(root, "copy-" + mode), mode);
        await CustodySourceFailure(workspace, Path.Combine(root, "custody-source"));
        CopySourceFailure(Path.Combine(root, "copy-source"));
    }
    static async Task Interleaving(string workspace, string root, bool cancel, bool fail)
    {
        await using var h = new CameraRoundHarness(workspace, root);
        h.Peer.ReplySlot = "";
        await h.StartAsync(rcsImplementationFingerprint: new string('A', 64)); await h.WaitForCameraAdmissionAsync();
        h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-BACKUP"), 1);
        ParcelOperation? original = null;
        await CameraRoundHarness.Until(async () => (original = (await h.App.SnapshotAsync()).Operations.SingleOrDefault())?.Wcs?.State == StageState.Unknown);
        await h.App.DisposeAsync();
        await h.StartAsync(waitForRcs: true, rcsImplementationFingerprint: new string('B', 64));
        await Command(h.App, "Pause");
        var directory = Path.Combine(h.Launch.DataRoot, h.Config.Mode.ToString(), h.Config.StationId, h.Config.ApplicationId);
        var files = Directory.GetFiles(Path.Combine(directory, "receipts"), "*.json").ToDictionary(p => p, p => Wire.FileHash(p));
        Check(files.Count > 0, "real SQLite request has original receipt files before backup");
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim(); using var cancellation = new CancellationTokenSource();
        h.App.BackupBoundary = phase =>
        {
            if (phase != (cancel || fail ? "DatabaseCopied" : "Inventory")) return;
            entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("Backup interleaving not released");
        };
        var backup = Path.Combine(root, "external.db"); Task<BusinessReceipt> work = null!;
        await LifecycleOptimization.AfterMonitorDrain(h.App, async () =>
        {
            work = h.App.BackupAsync(backup, cancellation.Token);
            Check(await Task.Run(() => entered.Wait(TimeSpan.FromSeconds(10))), "backup entered custody boundary after real monitor reservation drained");
            return true;
        });
        try
        {
            Check(await Task.Run(() => entered.Wait(TimeSpan.FromSeconds(10))), "backup reached deterministic " + (cancel || fail ? "snapshot" : "inventory") + " boundary");
            // Real driver/decoder empty-frame completion, not decode-start counts.
            var first = DateTimeOffset.UtcNow;
            for (uint frame = 2; frame <= 4; frame++)
            {
                h.Api(h.Left).Enqueue(CameraRoundHarness.EmptyTray, frame);
                await CameraRoundHarness.Until(() => Task.FromResult(h.Factory.Sources[h.Left].Timings.Snapshot().RecentFrames.Any(f => f.Number == frame)));
                if (frame < 4) await Task.Delay(420);
            }
            Check(h.Factory.Trays[h.Left].Observe(TrayPresence.Absent), "qualified observation retires normally at the backup ingress fence");
            Check(DateTimeOffset.UtcNow - first >= TimeSpan.FromMilliseconds(800), "three delivered no-code frames are followed by an independent qualified removal fact");
            var before = Progress(h.App);
            await CameraRoundHarness.Until(() => Task.FromResult(Progress(h.App) > before));
            Check(await h.App.OperationAsync(original!.Id) is not null && files.All(f => File.Exists(f.Key) && Wire.FileHash(f.Key) == f.Value),
                "maintenance advances without gate starvation and retains original DB/file dependencies throughout backup");
            if (cancel) cancellation.Cancel();
            if (fail) File.WriteAllText(backup + ".recovery/configuration.json", "synthetic destination collision");
        }
        finally { release.Set(); }
        if (cancel || fail)
        {
            try { await work; throw new Exception("Incomplete backup returned success"); }
            catch (OperationCanceledException) when (cancel) { }
            catch (IOException) when (fail) { }
            Check(!File.Exists(backup + ".recovery/COMPLETED.json"), (cancel ? "cancellation" : "real custody-copy failure") + " does not publish a complete recovery set");
        }
        else
        {
            Check((await work).Status == "BackupCreated", "interleaved backup returns only actual completion");
            Check(ParcelRecovery.InspectAnalysisBackup(backup, new SportmasterFlow()).DatabaseVersion == ParcelRepository.CurrentStorageVersion,
                "complete recovery set passes production custody and DB schema verifier");
            var target = Path.Combine(root, "restored", h.Config.Mode.ToString(), h.Config.StationId, h.Config.ApplicationId);
            ParcelRecovery.Restore(backup, target);
            using var repo = new ParcelRepository(target, h.Config.ApplicationId);
            Check(repo.ReadOperation(original!.Id)?.Wcs?.Request.Id == original.Wcs!.Request.Id && files.All(f => File.Exists(Path.Combine(target, "receipts", Path.GetFileName(f.Key)))),
                "isolated restore retains exact original request and receipt collection");
        }
        h.App.BackupBoundary = null;
        // Restart restored an uncertain current round. Qualified absence does
        // not replace its required click; the backup fence must close first.
        await SmRoundFixture.NextVehicleAsync(h.App, "left");
        await CameraRoundHarness.Until(async () => await h.App.OperationAsync(original!.Id) is null);
        Check(files.Keys.All(f => !File.Exists(f)) && h.Peer.Requests.Count == 1, "success/cancellation/failure releases the fence; targeted removal then permits obsolete cleanup without replay");
    }
    static async Task ScheduleFault(string workspace, string root, string mode)
    {
        await using var h = new ManualSupplyHarness(workspace, root, deferred: true, mode: RunMode.Simulation);
        var policy = mode == "disabled" ? null : new ParcelBackupSchedule(Path.Combine(root, "backups"), 15, 5);
        ExtensionStore.AtomicWrite(Path.Combine(h.Launch.ConfigurationDirectory, "business.json"), h.Config with { BackupSchedule = policy });
        var directory = Path.Combine(h.Launch.DataRoot, h.Config.Mode.ToString(), h.Config.StationId, h.Config.ApplicationId);
        Directory.CreateDirectory(directory); var state = Path.Combine(directory, "backup-schedule.json");
        if (mode == "write-failure") Directory.CreateDirectory(state); else File.WriteAllText(state, "{broken-original");
        await h.StartAsync(); await ManagementModeFixture.Authorize(h.App);
        await h.App.CheckScheduledBackupAsync(DateTimeOffset.UtcNow);
        var s = await h.App.SnapshotAsync();
        Check(s.BackupScheduleStatus?.Outcome == (mode == "disabled" ? "Disabled" : "ScheduleStateFault") &&
            (mode == "write-failure" ? Directory.Exists(state) : File.ReadAllText(state) == "{broken-original"), "auxiliary " + mode + " preserves original and reports plan state accurately");
        var progress = Progress(h.App);
        await CameraRoundHarness.Until(() => Task.FromResult(Progress(h.App) > progress));
        var next = await h.App.SubmitParcelBarcodeAsync(await h.Command("right", "SYNTH-AFTER-STATE-FAULT"), default);
        await CameraRoundHarness.Until(() => Task.FromResult(h.Peer.Requests.Count == 1));
        Check(next.Status == "Admitted" && h.Peer.Requests.Count == 1, "auxiliary " + mode + " leaves maintenance and legitimate new supply running");
        // Actual volume availability is an independent guard even with the plan fault.
        h.App.StorageAvailableBytesForVerification = () => throw new IOException("synthetic business volume unavailable");
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Capacity?.AdmissionBlock == ParcelInputBlockReason.StorageUnavailable);
        Check((await h.App.SubmitParcelBarcodeAsync(await h.Command("left", "SYNTH-STORAGE-BLOCK"), default)).Status != "Admitted" && h.Peer.Requests.Count == 1,
            "business storage failure still blocks supply after auxiliary state isolation");
    }
    // Native SQLite setup seam, before backup_init owns the target connection.
    // FULL is a real backup_step failure after the application capacity preflight.
    static readonly PropertyInfo TargetOpened = typeof(Database).GetProperty("BackupTargetOpenedForVerification", BindingFlags.Static | BindingFlags.NonPublic)!;
    static async Task CopyFailureIsolation(string workspace, string root, string mode)
    {
        await using var h = new ManualSupplyHarness(workspace, root, deferred: true, mode: RunMode.Simulation);
        var target = Path.Combine(root, "backups"); Directory.CreateDirectory(target);
        ExtensionStore.AtomicWrite(Path.Combine(h.Launch.ConfigurationDirectory, "business.json"), h.Config with { BackupSchedule = new(target, 15, 10) });
        await h.StartAsync(); await ManagementModeFixture.Authorize(h.App); await Command(h.App, "Pause");
        var now = DateTimeOffset.UtcNow;
        await LifecycleOptimization.AfterMonitorDrain(h.App, async () =>
        {
            await h.App.CheckScheduledBackupAsync(now);
            await h.App.CheckScheduledBackupAsync(now.AddMinutes(16));
            return true;
        });
        var success = (await h.App.SnapshotAsync()).BackupScheduleStatus!;
        Check(success.Outcome == "BackupCreated", "copy " + mode + " starts with a verified earlier successful schedule");
        var originalSuccessHash = Wire.FileHash(success.LastSuccessfulBackup!);
        Action<SqliteConnection> failTarget = db =>
        {
            if (mode == "io") throw new SqliteException("Synthetic destination I/O failure at the native backup boundary", SQLitePCL.raw.SQLITE_IOERR);
            using var limit = db.CreateCommand(); limit.CommandText = "PRAGMA max_page_count=1"; limit.ExecuteScalar();
        };
        if (mode == "custody-destination") RecoveryFiles.CopyClosedForVerification = (_, destination) => File.WriteAllText(destination, "synthetic damaged destination");
        else TargetOpened.SetValue(null, failTarget);
        try
        {
            var manualDestination = Path.Combine(root, "manual-failed.db");
            await LifecycleOptimization.AfterMonitorDrain(h.App, async () =>
            {
                try { await h.App.BackupAsync(manualDestination); throw new Exception("Target copy fault returned success"); }
                catch (BackupCopyFailure error)
                {
                    Check(error.SourceFailure is null && error.SourceCheckCompleted && error.InnerException is SqliteException sql &&
                        sql.SqliteErrorCode == (mode == "full" ? SQLitePCL.raw.SQLITE_FULL : SQLitePCL.raw.SQLITE_IOERR),
                        "manual " + mode + " retains the SQLite copy failure and independently verifies the healthy source");
                }
                catch (RecoveryFileFailure error) when (mode == "custody-destination")
                {
                    Check(error is { Origin: RecoveryFailureOrigin.Destination, Phase: "DestinationChecksum", SourceBytesVerified: true } && error.InnerException is InvalidDataException,
                        "actual custody copy verifies the original source stream and attributes damaged copied bytes to destination verification");
                }
                // Releasing the manual backup requests a real membership refresh.
                // Await its applied response before exercising another backup;
                // do not mistake a legitimate heartbeat guard for the copy fault.
                await CameraRoundHarness.Until(async () => await h.App.CheckRcsHeartbeatAsync(default) == RcsHeartbeatResult.Received);
                await h.App.CheckScheduledBackupAsync(success.NextDue!.Value.AddMinutes(1));
                return true;
            });
            var failed = (await h.App.SnapshotAsync()).BackupScheduleStatus!;
            Check(failed.Outcome == (mode == "custody-destination" ? "Failed:RecoveryFileFailure" : "Failed:BackupCopyFailure") && failed.LastSuccessfulBackup == success.LastSuccessfulBackup &&
                failed.LastSuccessfulAt == success.LastSuccessfulAt && Wire.FileHash(success.LastSuccessfulBackup!) == originalSuccessHash &&
                !File.Exists(manualDestination + ".recovery/COMPLETED.json") && Directory.EnumerateFiles(target, "COMPLETED.json", SearchOption.AllDirectories).Count() == 1,
                "scheduled " + mode + " retains the actual failed receipt and previous success, never publishing a complete failed set; " + Wire.Encode(failed));
            var progress = Progress(h.App);
            await CameraRoundHarness.Until(() => Task.FromResult(Progress(h.App) > progress));
            Check(!h.App.FatalFailure.IsCompleted, "target " + mode + " cannot complete business FatalFailure or terminate maintenance");
        }
        finally { TargetOpened.SetValue(null, null); RecoveryFiles.CopyClosedForVerification = null; }
        await Command(h.App, "Resume");
        var next = await h.App.SubmitParcelBarcodeAsync(await h.Command("right", "SYNTH-AFTER-COPY-FAILURE"), default);
        await CameraRoundHarness.Until(() => Task.FromResult(h.Peer.Requests.Count == 1));
        Check(next.Status == "Admitted" && h.Peer.Requests.Count == 1 && !h.App.FatalFailure.IsCompleted,
            "legitimate supply resumes after " + mode + " without restarting the station or rewriting an original request");
    }
    static async Task CustodySourceFailure(string workspace, string root)
    {
        await using var h = new ManualSupplyHarness(workspace, root, deferred: true, mode: RunMode.Simulation);
        await h.StartAsync(); await ManagementModeFixture.Authorize(h.App); await Command(h.App, "Pause");
        var original = Path.Combine(h.Launch.DataRoot, h.Config.Mode.ToString(), h.Config.StationId, h.Config.ApplicationId, "configuration.json");
        var destination = Path.Combine(root, "failed.db");
        h.App.BackupBoundary = phase => { if (phase == "DatabaseCopied") File.WriteAllText(original, "synthetic damaged source ledger"); };
        try
        {
            try { await h.App.BackupAsync(destination); throw new InvalidOperationException("Damaged source accepted"); }
            catch (RecoveryFileFailure error)
            {
                Check(error is { Origin: RecoveryFailureOrigin.Source, Phase: "SourceChecksum", SourceBytesVerified: false } && h.App.FatalFailure.IsCompleted,
                    "actual custody source mismatch reaches the production fatal source-storage guard");
            }
            Check(!File.Exists(destination + ".recovery/COMPLETED.json") && File.ReadAllText(original) == "synthetic damaged source ledger" &&
                (await h.App.SubmitParcelBarcodeAsync(await h.Command("right", "SYNTH-DAMAGED-SOURCE"), default)).Status != "Admitted" && h.Peer.Requests.IsEmpty,
                "damaged original and partial set remain; no completion marker or legitimate-looking supply bypasses source protection");
        }
        finally { h.App.BackupBoundary = null; }
    }
    static void CopySourceFailure(string root)
    {
        Directory.CreateDirectory(root); var file = Path.Combine(root, "source.db");
        using var source = new SqliteConnection("Data Source=" + file + ";Pooling=False"); source.Open();
        using (var create = source.CreateCommand()) { create.CommandText = "CREATE TABLE payload(body TEXT); INSERT INTO payload VALUES('synthetic source')"; create.ExecuteNonQuery(); }
        string? corruptedHash = null;
        Action<SqliteConnection> sourceDamage = _ =>
        {
            using var damage = new SqliteConnection("Data Source=" + file + ";Pooling=False"); damage.Open();
            using var command = damage.CreateCommand(); command.CommandText = "PRAGMA writable_schema=ON; UPDATE sqlite_schema SET rootpage=2147483647 WHERE name='payload'; PRAGMA writable_schema=OFF";
            command.ExecuteNonQuery(); damage.Close();
            using (var bytes = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                corruptedHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
            throw new SqliteException("Synthetic ambiguous copy I/O failure with an actually damaged source", SQLitePCL.raw.SQLITE_IOERR);
        };
        TargetOpened.SetValue(null, sourceDamage);
        try
        {
            try { Database.Backup(source, Path.Combine(root, "partial.db"), CancellationToken.None); throw new Exception("Damaged source accepted"); }
            catch (BackupCopyFailure error)
            {
                source.Close(); // Strict original-byte hashing follows the real native handle release.
                Check(error.SourceCheckCompleted && error.SourceFailure is SqliteException or InvalidDataException &&
                    error.InnerException is AggregateException && corruptedHash == Wire.FileHash(file),
                    "actual source integrity failure remains distinct from destination failure, retaining both errors and damaged originals");
            }
        }
        finally { TargetOpened.SetValue(null, null); }
    }
    static async Task ScheduleOutcomes(string workspace, string root)
    {
        await using var h = new ManualSupplyHarness(workspace, root, deferred: true, mode: RunMode.Simulation);
        var target = Path.Combine(root, "backups"); Directory.CreateDirectory(target);
        ExtensionStore.AtomicWrite(Path.Combine(h.Launch.ConfigurationDirectory, "business.json"), h.Config with { BackupSchedule = new(target, 15, 10) });
        await h.StartAsync(); await ManagementModeFixture.Authorize(h.App); await Command(h.App, "Pause");
        var now = DateTimeOffset.UtcNow; await h.App.CheckScheduledBackupAsync(now);
        for (var i = 0; i < 10 && (await h.App.SnapshotAsync()).BackupScheduleStatus!.Outcome != "BackupCreated"; i++)
            await h.App.CheckScheduledBackupAsync(now.AddMinutes(16 + i));
        var success = (await h.App.SnapshotAsync()).BackupScheduleStatus!;
        Check(success.Outcome == "BackupCreated" && success.LastSuccessfulBackup is not null, "scheduled success updates only the real complete recovery set");
        var due = success.NextDue!.Value.AddMinutes(1);
        // A concurrent actual backup owns the fence when the schedule tries to run.
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        h.App.BackupBoundary = phase => { if (phase == "Inventory") { entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException(); } };
        await LifecycleOptimization.AfterMonitorDrain(h.App, async () =>
        {
            var manual = h.App.BackupAsync(Path.Combine(root, "manual.db"));
            try
            {
                Check(await Task.Run(() => entered.Wait(TimeSpan.FromSeconds(5))), "manual backup holds custody fence while schedule is due");
                await h.App.CheckScheduledBackupAsync(due);
            }
            finally { release.Set(); }
            await manual;
            return true;
        });
        h.App.BackupBoundary = null;
        var rejected = (await h.App.SnapshotAsync()).BackupScheduleStatus!;
        var state = Path.Combine(h.Launch.DataRoot, h.Config.Mode.ToString(), h.Config.StationId, h.Config.ApplicationId, "backup-schedule.json");
        using var json = JsonDocument.Parse(File.ReadAllText(state));
        Check(rejected.Outcome == "Rejected" && !rejected.Detail!.Contains("完整恢复集已创建") && rejected.LastSuccessfulAt == success.LastSuccessfulAt &&
            rejected.LastSuccessfulBackup == success.LastSuccessfulBackup && json.RootElement.GetProperty("Receipt").GetProperty("Status").GetString() == "Rejected",
            "rejected backup preserves full real receipt and earlier success in projection and durable state");
        // A write failure after a known successful state must preserve that success.
        File.Move(state, state + ".retained"); Directory.CreateDirectory(state);
        await h.App.CheckScheduledBackupAsync(due.AddMinutes(1));
        var fault = (await h.App.SnapshotAsync()).BackupScheduleStatus!;
        Check(fault.Outcome == "ScheduleStateFault" && fault.LastSuccessfulBackup == success.LastSuccessfulBackup && File.Exists(state + ".retained"),
            "state write failure stops only the plan and retains last successful information and original file");
        await h.App.DisposeAsync();
        Directory.Delete(state); File.Move(state + ".retained", state);
        var interrupted = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(state))!;
        interrupted["Outcome"] = "Running: owned backup";
        File.WriteAllText(state, interrupted.ToJsonString()); var retainedHash = Wire.FileHash(state);
        await h.StartAsync(ready: false); await ManagementModeFixture.Authorize(h.App);
        await h.App.CheckScheduledBackupAsync(due.AddMinutes(5));
        var uncertain = (await h.App.SnapshotAsync()).BackupScheduleStatus!;
        Check(uncertain.Outcome == "ResultUnconfirmed" && uncertain.NextDue is null && uncertain.LastSuccessfulBackup == success.LastSuccessfulBackup && Wire.FileHash(state) == retainedHash,
            "interrupted scheduled execution retains its original target/success and stops repetition until maintenance verification");
    }
}

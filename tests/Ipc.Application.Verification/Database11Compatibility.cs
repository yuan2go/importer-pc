using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Assembly;
using Ipc.Platform;
using Ipc.Platform.Storage;
using Microsoft.Data.Sqlite;

static class Database11Compatibility
{
    const string LegacyHash = "ECC9F99AFD48F914765248A6B39064A49F0DCBF3DDD2F208C87C75F8E07FEB23";
    static void Check(bool value, string reason)
    { if (!value) throw new InvalidDataException(reason); Console.WriteLine("PASS DB22 " + reason); }
    static SqliteConnection Open(string file, bool readOnly = false)
    {
        var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString());
        db.Open(); return db;
    }
    static long Count(string file, string sql)
    { using var db = Open(file, true); using var c = db.CreateCommand(); c.CommandText = sql; return Convert.ToInt64(c.ExecuteScalar()); }
    static void Change(string file, string sql)
    { using var db = Open(file); Database.Execute(db, null, sql); }
    sealed class Fixture
    {
        internal readonly BusinessLaunch Launch;
        internal readonly ParcelConfiguration Config;
        internal readonly string Directory, Database;
        internal readonly ParcelStartupMaintenance Service;
        internal readonly string Root;
        internal Fixture(string root)
        {
            Root = root; System.IO.Directory.CreateDirectory(root);
            Config = new SportmasterFlow().InitialConfiguration() with { Mode = RunMode.Simulation };
            var config = Path.Combine(root, "config"); System.IO.Directory.CreateDirectory(config);
            ExtensionStore.AtomicWrite(Path.Combine(config, "business.json"), Config);
            Launch = new(config, Path.Combine(root, "data"), null, null);
            Directory = Path.Combine(Launch.DataRoot, Config.Mode.ToString(), Config.StationId, Config.ApplicationId);
            System.IO.Directory.CreateDirectory(Directory); Database = Path.Combine(Directory, "sportmaster.db");
            using (var db = Open(Database)) Ipc.Platform.Storage.Database.CreateBaseline(db, "sportmaster", 10, ParcelRepository.Storage10Baseline);
            Change(Database, "UPDATE parcel_meta SET paused=1; INSERT INTO parcel_audit(at,event,owner,detail) VALUES('2026-11-02','SyntheticOriginal','station','retain original audit bytes');");
            ExtensionStore.AtomicWrite(Path.Combine(Directory, "configuration.json"), CurrentConfigurationFixture.Create<ParcelConfiguration>(3, Config, null, "Effective", null));
            Service = new(Launch, new SportmasterFlow(), () => new ParcelCapabilityFactory(Launch), () => { }, () => "synthetic-maintainer")
            { MaintenanceWorkspace = () => System.IO.Directory.CreateDirectory(Path.Combine(root, "scratch-" + Wire.Id())).FullName };
        }
        internal ParcelOperation Prepared(string id = "operation-original")
        {
            var hash = Wire.Hash(Wire.Encode(Config)); var payload = Wire.Encode(new ParcelPayload("SYNTHETIC-ONLY", "admission", null, null, null));
            var request = new FrozenRequest("request-original", "sportmaster", id, "admission", "Wcs", Config.Wcs!, "synthetic-implementation", hash, payload, Wire.Hash(payload), DateTimeOffset.UtcNow);
            var op = new ParcelOperation(id, "admission", "SYNTHETIC-ONLY", "observation", DateTimeOffset.UtcNow,
                hash, Config, new() { [Config.Wcs!.InstanceId] = "synthetic-implementation" }, Config.Weight, null, ParcelState.ResolvingWcs, null,
                new(request, StageState.Prepared, 0, []), null, null, PositionId: "left");
            using var db = Open(Database); using var c = db.CreateCommand();
            c.CommandText = "INSERT INTO parcel_operations VALUES($id,$admission,$observation,$body)";
            c.Parameters.AddWithValue("$id", op.Id); c.Parameters.AddWithValue("$admission", op.AdmissionId); c.Parameters.AddWithValue("$observation", op.ObservationId); c.Parameters.AddWithValue("$body", Wire.Encode(op)); c.ExecuteNonQuery();
            return op;
        }
        internal string Backup(string name) => Path.Combine(Root, name + ".db");
    }
    static string Facts(string file)
    {
        using var db = Open(file, true); using var names = db.CreateCommand();
        names.CommandText = "SELECT name FROM sqlite_schema WHERE type='table' AND name NOT IN ('platform_migrations','parcel_audit','parcel_meta','station_metric_buckets','sqlite_sequence') ORDER BY name";
        var tables = new List<string>(); using (var r = names.ExecuteReader()) while (r.Read()) tables.Add(r.GetString(0));
        var facts = new List<object>();
        foreach (var table in tables)
        {
            using var c = db.CreateCommand(); c.CommandText = "SELECT * FROM \"" + table.Replace("\"", "\"\"") + "\" ORDER BY rowid";
            using var r = c.ExecuteReader(); var rows = new List<string>();
            while (r.Read()) { var values = new object[r.FieldCount]; r.GetValues(values); rows.Add(Wire.Encode(values)); }
            facts.Add(new { Table = table, Rows = rows });
        }
        return Wire.Hash(Wire.Encode(facts));
    }
    static async Task Reject<T>(Func<Task> action, string reason) where T : Exception
    {
        try { await action(); } catch (T) { Check(true, reason); return; }
        throw new InvalidDataException("Expected refusal: " + reason);
    }
    static void Upgrade(Fixture fixture, string backup, Action<string>? boundary = null, MaintenanceExecution? execution = null) =>
        ParcelRepository.UpgradeStorage10(fixture.Database, "sportmaster", backup, Wire.FileHash(backup), Wire.FileHash(fixture.Database),
            File.Exists(fixture.Database + "-wal") ? Wire.FileHash(fixture.Database + "-wal") : null, "synthetic-maintainer", () => { }, execution, boundary);
    public static async Task Run(string workspace, string root)
    {
        var initial = new Fixture(Path.Combine(root, "identity"));
        Check(Database.BaselineHash(ParcelRepository.Storage10Baseline) == LegacyHash, "approved predecessor specification equals the frozen DB10 hash");
        var originalHash = Wire.FileHash(initial.Database);
        Check(ParcelRepository.ReadMaintenanceStorageVersion(initial.Database, "sportmaster") == 10, "explicit offline inspection recognizes exact predecessor");
        await Reject<InvalidDataException>(() => Task.Run(() => ParcelRepository.ReadStorageVersion(initial.Database, "sportmaster")), "normal runtime cannot silently write or open DB10 as DB11");
        Check(Wire.FileHash(initial.Database) == originalHash, "format refusal preserves original database bytes");
        Change(initial.Database, "ANALYZE;");
        Check(ParcelRepository.ReadMaintenanceStorageVersion(initial.Database, "sportmaster") == 10, "SQLite-owned optimizer statistics do not masquerade as an unsupported application schema");
        Change(initial.Database, "CREATE TABLE unexpected_payload(id TEXT);");
        var changed = Wire.FileHash(initial.Database);
        await Reject<InvalidDataException>(() => Task.Run(() => ParcelRepository.ReadMaintenanceStorageVersion(initial.Database, "sportmaster")), "matching marker cannot hide an unexpected actual schema");
        Check(Wire.FileHash(initial.Database) == changed, "schema refusal does not rewrite the unknown database");
        var lookalike = new Fixture(Path.Combine(root, "sqlite-prefix-lookalike"));
        Change(lookalike.Database, "CREATE TABLE sqliteX_payload(id TEXT);");
        var lookalikeHash = Wire.FileHash(lookalike.Database);
        await Reject<InvalidDataException>(() => Task.Run(() => ParcelRepository.ReadMaintenanceStorageVersion(lookalike.Database, "sportmaster")), "a user object resembling the SQLite prefix cannot hide from exact schema validation");
        Check(Wire.FileHash(lookalike.Database) == lookalikeHash, "lookalike schema refusal preserves the original bytes");
        foreach (var version in new[] { 9, 12 })
        {
            var unknown = new Fixture(Path.Combine(root, "unsupported-" + version)); Change(unknown.Database, "UPDATE platform_migrations SET version=" + version);
            var hash = Wire.FileHash(unknown.Database);
            await Reject<InvalidDataException>(() => unknown.Service.PlanChangeAsync("Upgrade", unknown.Backup("never")), "unsupported format " + version + " is not guessed or converted");
            Check(hash == Wire.FileHash(unknown.Database), "unsupported format retains bytes " + version);
        }
        var successful = new Fixture(Path.Combine(root, "upgrade"));
        var backup = successful.Backup("complete-original"); var configHash = Wire.FileHash(Path.Combine(successful.Directory, "configuration.json"));
        var seedHash = Wire.FileHash(Path.Combine(successful.Launch.ConfigurationDirectory, "business.json")); var facts = Facts(successful.Database);
        var plan = await successful.Service.PlanChangeAsync("Upgrade", backup);
        Check(plan.Allowed && plan.Summary.Contains("事务升级至11"), "installer review explicitly plans full backup and additive format transition");
        Check(!File.Exists(backup) && ParcelRepository.ReadMaintenanceStorageVersion(successful.Database, "sportmaster") == 10, "planning neither publishes a backup nor upgrades the original");
        var deploymentCalls = 0;
        await successful.Service.ExecuteChangeAsync(plan.Identity, "Upgrade", backup, () =>
        {
            Check(ParcelRepository.ReadStorageVersion(successful.Database, "sportmaster") == 11, "schema commit precedes deployment pointer switch");
            Check(File.Exists(backup + ".recovery/COMPLETED.json"), "deployment cannot precede a complete original recovery set");
            deploymentCalls++; return Task.CompletedTask;
        });
        Check(deploymentCalls == 1 && ParcelRepository.ReadMaintenanceStorageVersion(backup, "sportmaster") == 10, "old recovery set stays DB10 while the live original becomes DB11");
        Check(Facts(successful.Database) == facts && Wire.FileHash(Path.Combine(successful.Directory, "configuration.json")) == configHash &&
            Wire.FileHash(Path.Combine(successful.Launch.ConfigurationDirectory, "business.json")) == seedHash, "all existing fact tables, effective configuration and seed bytes survive transition");
        Check(Count(successful.Database, "SELECT count(*) FROM parcel_audit WHERE event='StorageFormatUpgraded'") == 1 && Count(successful.Database, "SELECT paused FROM parcel_meta WHERE id=1") == 1,
            "one durable upgrade audit is atomic with format identity and software pause");
        using (var current = new ParcelRepository(successful.Directory, "sportmaster")) Check(current.StorageVersion == 11, "upgraded database opens through the unmodified current runtime gate");
        var after = Facts(successful.Database);
        await Reject<InvalidDataException>(() => Task.Run(() => Upgrade(successful, backup)), "duplicate format transition refuses rather than recopying history");
        Check(Facts(successful.Database) == after, "duplicate transition preserves original facts");
        var interrupted = new Fixture(Path.Combine(root, "interrupted")); var rollback = interrupted.Backup("rollback-original");
        await interrupted.Service.BackupAsync(rollback); var rollbackFacts = Facts(interrupted.Database);
        await Reject<IOException>(() => Task.Run(() => Upgrade(interrupted, rollback, p => { if (p == "BeforeStorageTransactionCommit") throw new IOException("Synthetic transaction failure"); })), "failure after additive schema and marker writes rolls back the entire SQLite transaction");
        Check(ParcelRepository.ReadMaintenanceStorageVersion(interrupted.Database, "sportmaster") == 10 && Facts(interrupted.Database) == rollbackFacts &&
            Count(interrupted.Database, "SELECT count(*) FROM sqlite_schema WHERE name='station_metric_buckets'") == 0 && Count(interrupted.Database, "SELECT count(*) FROM parcel_audit WHERE event='StorageFormatUpgraded'") == 0,
            "failed transaction leaves the complete old identity without partial analytics or a false audit");
        Upgrade(interrupted, rollback);
        Check(ParcelRepository.ReadStorageVersion(interrupted.Database, "sportmaster") == 11, "same verified backup supports explicit retry after transaction failure");
        var cancelled = new Fixture(Path.Combine(root, "cancelled")); var cancelBackup = cancelled.Backup("cancel-original"); await cancelled.Service.BackupAsync(cancelBackup);
        var cancelHash = Wire.FileHash(cancelled.Database); using var stop = new CancellationTokenSource(); using var execution = new MaintenanceExecution(token: stop.Token);
        stop.Cancel();
        await Reject<OperationCanceledException>(() => Task.Run(() => Upgrade(cancelled, cancelBackup, execution: execution)), "pre-commit cancellation refuses before any writable original connection");
        Check(Wire.FileHash(cancelled.Database) == cancelHash && !File.Exists(cancelled.Database + "-wal"), "cancelled transition preserves original bytes and creates no WAL");
        var corruptBackup = new Fixture(Path.Combine(root, "backup-tamper")); var tamper = corruptBackup.Backup("tamper"); await corruptBackup.Service.BackupAsync(tamper);
        File.AppendAllText(Path.Combine(tamper + ".recovery", "configuration.json"), " "); var intact = Wire.FileHash(corruptBackup.Database);
        await Reject<InvalidDataException>(() => Task.Run(() => Upgrade(corruptBackup, tamper)), "changed custody attachment blocks schema commit");
        Check(Wire.FileHash(corruptBackup.Database) == intact, "invalid backup never changes the original");
        await Closure(root);
        await WalAndDeploymentFailure(root);
        await ActualRequest(workspace, root);
        Console.WriteLine("DB22_EVIDENCE=" + root);
    }
    static async Task WalAndDeploymentFailure(string root)
    {
        var writerFixture = new Fixture(Path.Combine(root, "wal-writer")); var wal = new Fixture(Path.Combine(root, "crash-wal"));
        using (var writer = Open(writerFixture.Database))
        {
            Database.Execute(writer, null, "PRAGMA journal_mode=WAL; PRAGMA wal_autocheckpoint=0; INSERT INTO parcel_audit(at,event,owner,detail) VALUES('2026-11-02','CommittedOnlyInWal','station','retain committed WAL fact');");
            File.Copy(writerFixture.Database, wal.Database, true); File.Copy(writerFixture.Database + "-wal", wal.Database + "-wal");
        }
        var dbHash = Wire.FileHash(wal.Database); var walHash = Wire.FileHash(wal.Database + "-wal"); var backup = wal.Backup("wal-original");
        var plan = await wal.Service.PlanChangeAsync("Upgrade", backup);
        Check(plan.Allowed && Wire.FileHash(wal.Database) == dbHash && Wire.FileHash(wal.Database + "-wal") == walHash && !File.Exists(wal.Database + "-shm"),
            "planning a crashed WAL original reads only a private copy and preserves DB/WAL/SHM bytes");
        await wal.Service.ExecuteChangeAsync(plan.Identity, "Upgrade", backup, () => Task.CompletedTask);
        Check(Count(wal.Database, "SELECT count(*) FROM parcel_audit WHERE event='CommittedOnlyInWal'") == 1 && Count(backup, "SELECT count(*) FROM parcel_audit WHERE event='CommittedOnlyInWal'") == 1,
            "complete backup and schema upgrade both retain facts committed only in a crash WAL");
        var interrupted = new Fixture(Path.Combine(root, "deployment-failure")); var original = interrupted.Backup("before-failed-deployment");
        var before = Facts(interrupted.Database); var first = await interrupted.Service.PlanChangeAsync("Upgrade", original);
        await Reject<IOException>(() => interrupted.Service.ExecuteChangeAsync(first.Identity, "Upgrade", original, () => throw new IOException("Synthetic deployment pointer failure")),
            "deployment failure after storage commit is reported instead of silently rolling back business data");
        Check(ParcelRepository.ReadStorageVersion(interrupted.Database, "sportmaster") == 11 && Facts(interrupted.Database) == before && File.Exists(original + ".recovery/COMPLETED.json"),
            "failed deployment retains current schema, original fact tables and the complete old recovery set");
        var retryBackup = interrupted.Backup("before-retry"); var retry = await interrupted.Service.PlanChangeAsync("Upgrade", retryBackup); var commits = 0;
        await interrupted.Service.ExecuteChangeAsync(retry.Identity, "Upgrade", retryBackup, () => { commits++; return Task.CompletedTask; });
        Check(commits == 1 && Count(interrupted.Database, "SELECT count(*) FROM parcel_audit WHERE event='StorageFormatUpgraded'") == 1,
            "deployment retry advances from committed DB11 without reapplying a schema transition");
        var stale = new Fixture(Path.Combine(root, "changed-plan")); var staleBackup = stale.Backup("never"); var stalePlan = await stale.Service.PlanChangeAsync("Upgrade", staleBackup);
        Change(stale.Database, "INSERT INTO parcel_audit(at,event,owner,detail) VALUES('2026-11-02','ConcurrentMaintenance','station','new fact after review')");
        var latest = Wire.FileHash(stale.Database);
        await Reject<InvalidOperationException>(() => stale.Service.ExecuteChangeAsync(stalePlan.Identity, "Upgrade", staleBackup, () => Task.CompletedTask),
            "a changed original invalidates the exact reviewed installation plan");
        Check(!File.Exists(staleBackup) && Wire.FileHash(stale.Database) == latest, "stale plan never overwrites concurrent maintenance facts");
    }
    static async Task Closure(string root)
    {
        var f = new Fixture(Path.Combine(root, "closure")); var op = f.Prepared("00000000000000000000000000000001"); var hash = Wire.Hash(Wire.Encode(op)); var backup = f.Backup("before-close");
        var blocked = await f.Service.PlanChangeAsync("Upgrade", f.Backup("upgrade"));
        Check(!blocked.Allowed, "unsent original still blocks installation until explicit entity inspection; compatibility never weakens the guard");
        await Reject<InvalidOperationException>(() => f.Service.CloseNeverAttemptedAsync(op.Id, new string('A', 64), "synthetic inspected test remnant", backup), "stale original body hash cannot close another version");
        var result = await f.Service.CloseNeverAttemptedAsync(op.Id, hash, "synthetic inspected test remnant", backup);
        using (var db = Open(f.Database, true))
        {
            using var q = db.CreateCommand(); q.CommandText = "SELECT body FROM parcel_operations WHERE id=$id"; q.Parameters.AddWithValue("$id", op.Id);
            var closed = Wire.Decode<ParcelOperation>((string)q.ExecuteScalar()!);
            Check(result.Contains("UnsubmittedIntentClosed") && closed.State == ParcelState.Abandoned && closed.Wcs!.State == StageState.Cancelled && closed.Wcs.Attempts == 0,
                "explicit offline inspection closes only a never-attempted intent without claiming acceptance or physical completion");
            Check(Wire.Encode(closed.Wcs!.Request) == Wire.Encode(op.Wcs!.Request) && Wire.Encode(closed.FrozenConfiguration) == Wire.Encode(op.FrozenConfiguration), "closure retains exact frozen request and configuration bytes");
        }
        Check(Count(f.Database, "SELECT count(*) FROM parcel_operations") == 1 && Count(f.Database, "SELECT count(*) FROM parcel_audit WHERE event='UnsubmittedIntentClosedOffline'") == 1 &&
            ParcelRepository.ReadMaintenanceStorageVersion(backup, "sportmaster") == 10, "closure retains the original row, complete before-image recovery set and durable reason audit");
        await Reject<InvalidOperationException>(() => f.Service.CloseNeverAttemptedAsync(op.Id, hash, "synthetic inspected test remnant", f.Backup("duplicate")), "lost-result retry must re-inspect the changed original instead of closing it twice");
        Check((await f.Service.PlanChangeAsync("Upgrade", f.Backup("upgrade"))).Allowed, "settled local cancellation admits the existing program-upgrade guard");
        var sent = new Fixture(Path.Combine(root, "sent-refusal")); var attempted = sent.Prepared();
        Change(sent.Database, "UPDATE parcel_operations SET body=json_set(body,'$.Wcs.Attempts',1,'$.Wcs.State','Unknown','$.State','WcsUnknown')");
        using var sentDb = Open(sent.Database, true); using var read = sentDb.CreateCommand(); read.CommandText = "SELECT body FROM parcel_operations"; var raw = (string)read.ExecuteScalar()!; sentDb.Close();
        await Reject<InvalidDataException>(() => sent.Service.CloseNeverAttemptedAsync(attempted.Id, Wire.Hash(raw), "not legitimate for sent original", sent.Backup("never")), "sent or unknown request cannot use local unsubmitted closure");
        Check(!File.Exists(sent.Backup("never")), "invalid closure refuses before publishing any maintenance effect");
    }
    static async Task ActualRequest(string workspace, string root)
    {
        await using var h = new ManualSupplyHarness(workspace, Path.Combine(root, "actual-loopback"), deferred: true);
        await h.StartAsync(); var admitted = await h.App.SubmitParcelBarcodeAsync(await h.Command("left", "DB22-SYNTHETIC"), default);
        await CameraRoundHarness.Until(async () => (await h.App.OperationAsync(admitted.OperationId!))?.Wcs?.State == StageState.Accepted);
        await h.App.StopAsync(default); await h.App.DisposeAsync(); h.App = null!;
        var directory = Path.Combine(h.Launch.DataRoot, h.Config.Mode.ToString(), h.Config.StationId, "sportmaster"); var database = Path.Combine(directory, "sportmaster.db");
        Change(database, "DROP TABLE station_metric_buckets; DROP INDEX station_operation_time; DROP INDEX station_archive_time; DROP INDEX station_recognition_time; UPDATE platform_migrations SET version=10,sha='" + LegacyHash + "'; UPDATE parcel_meta SET paused=1;");
        var before = Facts(database); var posts = h.Peer.Requests.Count;
        var service = new ParcelStartupMaintenance(h.Launch, new SportmasterFlow(), () => new ManualSupplyHarness.ManualFactory(h.Launch), () => { })
        { MaintenanceWorkspace = () => Directory.CreateDirectory(Path.Combine(root, "loopback-scratch-" + Wire.Id())).FullName };
        var backup = Path.Combine(root, "actual-original.db"); var plan = await service.PlanChangeAsync("Upgrade", backup);
        Check(plan.Allowed, "actual accepted loopback request and original raw custody admit offline additive upgrade");
        await service.ExecuteChangeAsync(plan.Identity, "Upgrade", backup, () => Task.CompletedTask);
        Check(Facts(database) == before && h.Peer.Requests.Count == posts, "actual original request, numeric identity, raw receipt and audit ownership remain unchanged with zero extra RCS calls");
        using (var reopened = new ParcelRepository(directory, "sportmaster"))
        {
            reopened.ValidateRawInventory();
            Check(reopened.Load().Length == 1 && reopened.StorageVersion == 11, "migrated actual RCS custody passes current original-history and raw-inventory validation");
        }
        await h.StartAsync(ready: false);
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Health.Any(x => x.InstanceId == h.Config.Wcs!.InstanceId && x.Ready));
        await Task.Delay(1300);
        var snapshot = await h.App.SnapshotAsync();
        Check(snapshot.Paused && !snapshot.Availability!.Resume && snapshot.BlockingReasons.Any(r => r.Contains("数据库已升级")) && h.Peer.Requests.Count == posts,
            "actual restarted runtime stays paused after healthy connections and background cycles; no automatic old request replay");
        var ordinary = await h.App.CommandAsync(new(Wire.Id(), snapshot.Revision, "Resume"));
        Check(ordinary.Status == "Rejected" && (await h.App.SnapshotAsync()).Paused, "ordinary mode cannot acknowledge post-upgrade review");
        await ManagementModeFixture.Authorize(h.App); snapshot = await h.App.SnapshotAsync();
        var resumed = await h.App.CommandAsync(new(Wire.Id(), snapshot.Revision, "Resume"));
        Check(resumed.Status != "Rejected" && !(await h.App.SnapshotAsync()).Paused && h.Peer.Requests.Count == posts,
            "explicit authorized resume records review without sending an old or synthetic business request");
        await h.App.StopAsync(default); await h.App.DisposeAsync(); h.App = null!;
        Check(Count(database, "SELECT count(*) FROM parcel_audit WHERE event='StorageUpgradeReviewed'") == 1, "post-upgrade review survives process stop as a durable audited fact");
    }
}

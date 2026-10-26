using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Platform;
using Microsoft.Data.Sqlite;

static class StabilityAuditFixes
{
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); Console.WriteLine("PASS AUDIT-FIX02 " + message); }
    static void Invoke(ParcelRepository repository, string method)
    {
        try { typeof(ParcelRepository).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(repository, null); }
        catch (TargetInvocationException e) when (e.InnerException is not null) { ExceptionDispatchInfo.Capture(e.InnerException).Throw(); }
    }
    public static async Task Run(string workspace, string root)
    {
        History(workspace, root);
        await Maintenance(root);
    }
    public static void History(string workspace, string root)
    {
        var configuration = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace, "config/sportmaster/business.json")));
        var fixture = Path.Combine(root, "request-originals");
        // Repository-only T1 originals and local unsent dispositions, no peer accepts,
        // raw receipts, physical outcomes, real entities or elapsed days are invented.
        ParcelOperation Operation(int i)
        {
            var id = "op-" + i; var admission = "entity-" + i; var observation = "observation-" + i;
            var payload = Wire.Encode(new ParcelPayload("SYNTH-" + i, admission, null, null, null));
            var request = new FrozenRequest("request-" + i, "sportmaster", id, admission, "Wcs", configuration.Wcs!,
                new string('A', 64), Wire.Hash(Wire.Encode(configuration)), payload, Wire.Hash(payload), DateTimeOffset.UtcNow,
                new(i, "application/json", payload, Wire.Hash(payload)));
            return new(id, admission, "SYNTH-" + i, observation, DateTimeOffset.UtcNow, request.ConfigurationHash, configuration,
                new(), configuration.Weight, null, ParcelState.Abandoned, null, new(request, StageState.Cancelled, 0, []), null, "unsubmitted test disposition", PositionId: "left");
        }
        var baseline = Path.Combine(root, "originals.db");
        using (var repository = new ParcelRepository(fixture, "sportmaster"))
        {
            repository.Commit(Operation(1), "SyntheticUnsubmittedDisposition", "{}");
            repository.Commit(Operation(2), "SyntheticUnsubmittedDisposition", "{}");
            repository.Archive(["op-1"], "repository fixture; no remote submission");
            repository.Backup(baseline);
        }
        var cases = new (string Name, string Sql)[]
        {
            ("duplicate-numeric", "UPDATE parcel_operations SET body=json_set(body,'$.Wcs.Request.IntegrationRequest.RequestId',1) WHERE id='op-2'"),
            ("duplicate-request", "UPDATE parcel_operations SET body=json_set(body,'$.Wcs.Request.Id','request-1') WHERE id='op-2'"),
            ("backfill-duplicate-numeric", "DELETE FROM parcel_integration_id_owners; UPDATE parcel_operations SET body=json_set(body,'$.Wcs.Request.IntegrationRequest.RequestId',1) WHERE id='op-2'"),
            ("backfill-duplicate-request", "DELETE FROM parcel_integration_id_owners; UPDATE parcel_operations SET body=json_set(body,'$.Wcs.Request.Id','request-1') WHERE id='op-2'"),
            ("changed-original", "UPDATE parcel_operations SET body=json_set(body,'$.Wcs.Request.Payload','changed') WHERE id='op-2'"),
            ("zero-numeric", "UPDATE parcel_operations SET body=json_set(body,'$.Wcs.Request.IntegrationRequest.RequestId',0) WHERE id='op-2'"),
            ("string-numeric", "UPDATE parcel_operations SET body=json_set(body,'$.Wcs.Request.IntegrationRequest.RequestId','2') WHERE id='op-2'"),
            ("fraction-numeric", "UPDATE parcel_operations SET body=json_set(body,'$.Wcs.Request.IntegrationRequest.RequestId',2.5) WHERE id='op-2'"),
            ("missing-request", "UPDATE parcel_operations SET body=json_set(body,'$.Wcs.Request.Id',NULL) WHERE id='op-2'"),
            ("archive-hash", "UPDATE parcel_archives SET body=body||' ' WHERE id='op-1'"),
            ("row-owner", "UPDATE parcel_operations SET body=json_set(body,'$.Id','op-1') WHERE id='op-2'"),
            ("backfill-row-owner", "DELETE FROM parcel_integration_id_owners; UPDATE parcel_operations SET body=json_set(body,'$.Id','foreign-op') WHERE id='op-2'"),
            ("backfill-row-admission", "DELETE FROM parcel_integration_id_owners; UPDATE parcel_operations SET body=json_set(body,'$.AdmissionId','foreign-entity') WHERE id='op-2'"),
            ("backfill-row-observation", "DELETE FROM parcel_integration_id_owners; UPDATE parcel_operations SET body=json_set(body,'$.ObservationId','foreign-observation') WHERE id='op-2'"),
            ("partial-backfill", "DELETE FROM parcel_integration_id_owners; UPDATE parcel_archives SET body=body||' ' WHERE id='op-1'")
        };
        foreach (var test in cases)
        {
            var directory = Path.Combine(root, test.Name); Directory.CreateDirectory(directory);
            var database = Path.Combine(directory, "sportmaster.db"); File.Copy(baseline, database);
            using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Pooling = false }.ToString()); db.Open();
            using var sql = db.CreateCommand(); sql.CommandText = test.Sql; sql.ExecuteNonQuery();
            sql.CommandText = "SELECT count(*) FROM parcel_integration_id_owners"; var before = (long)sql.ExecuteScalar()!;
            var rejected = false;
            try { using var invalid = new ParcelRepository(directory, "sportmaster"); }
            catch (Exception e) when (e is InvalidDataException or SqliteException or System.Text.Json.JsonException) { rejected = true; }
            Check(rejected, test.Name + " refuses corrupted original history");
            Check((long)sql.ExecuteScalar()! == before, test.Name + " does not partially publish owner backfill");
        }
        using (var repository = new ParcelRepository(fixture, "sportmaster"))
        {
            Check(repository.IntegrationBudget(new(1, 100)).PermanentHighWater == 2, "resident and archived numeric originals retain the allocation floor");
            repository.Backup(Path.Combine(root, "verified-originals.db"));
        }
        // Paired measurement of the exact old validators plus the same canonical
        // owner pass versus the new entry. The fixture remains repository-only.
        var scale = Path.Combine(root, "scale-originals"); const int count = 2000;
        using (var repository = new ParcelRepository(scale, "sportmaster"))
        {
            for (var i = 1; i <= count; i++) repository.Commit(Operation(i), "SyntheticUnsubmittedDisposition", "{}");
            List<object> samples = [];
            foreach (var order in new[] { new[] { "old", "new" }, new[] { "new", "old" }, new[] { "old", "new" } })
                foreach (var path in order)
                {
                    var watch = Stopwatch.StartNew();
                    if (path == "old") { Invoke(repository, "ValidateIntegrationRequestHistory"); Invoke(repository, "BackfillIntegrationRequestOwners"); }
                    else Invoke(repository, "InitializeIntegrationRequestHistory");
                    samples.Add(new { Path = path, Milliseconds = watch.Elapsed.TotalMilliseconds });
                }
            ExtensionStore.AtomicWrite(Path.Combine(root, "HISTORY-PAIRED.json"), new { Rows = count, Samples = samples,
                Scope = "Repository-only generated T1 originals; same owner/hash checks; not runtime/physical/24h qualification", RuntimeSha256 = Wire.FileHash(typeof(ParcelRepository).Assembly.Location) });
            Check(repository.IntegrationBudget(new(1, count + 100)).PermanentHighWater == count, "paired validation preserves the permanent numeric floor");
        }
    }
    public static async Task Maintenance(string root)
    {
        var config = Path.Combine(root, "unused-config"); Directory.CreateDirectory(config);
        var draft = new SportmasterFlow().InitialConfiguration(); File.WriteAllText(Path.Combine(config, "business.json"), Wire.Encode(draft));
        var data = Path.Combine(root, "unused-data");
        var service = new ParcelStartupMaintenance(new(config, data, null, null), new SportmasterFlow(), () => throw new Exception("Unexpected port construction"), () => { });
        var originalHash = Wire.FileHash(Path.Combine(config, "business.json"));
        var backup = Path.Combine(root, "unused-backup.db");
        var plan = await service.PlanChangeAsync("Upgrade", backup);
        Check(plan.Allowed, "unconfigured original seed enters a no-database maintenance plan");
        var directoryRoot = Path.Combine(data, draft.Mode.ToString(), draft.StationId, draft.ApplicationId); Directory.CreateDirectory(directoryRoot);
        foreach (var marker in new[] { "sportmaster.db-wal", "sportmaster.db-shm", "RESTORE-INCOMPLETE.json", "RESTORED-ISOLATED.json", "overload.json" })
        {
            var path = Path.Combine(directoryRoot, marker); File.WriteAllText(path, "synthetic orphan responsibility");
            var rejected = false;
            try { await service.PlanChangeAsync("Upgrade", Path.Combine(root, "must-not-backup.db")); } catch (InvalidDataException) { rejected = true; }
            Check(rejected && File.Exists(path), marker + " blocks treating original responsibility as an unused installation"); File.Delete(path);
        }
        var lateMarker = Path.Combine(directoryRoot, "RESTORE-INCOMPLETE.json"); File.WriteAllText(lateMarker, "synthetic responsibility after preview");
        var deploymentCalled = false; var lateRejected = false;
        try { await service.ExecuteChangeAsync(plan.Identity, "Upgrade", backup, () => { deploymentCalled = true; return Task.CompletedTask; }); }
        catch (InvalidDataException) { lateRejected = true; }
        Check(lateRejected && !deploymentCalled && File.Exists(lateMarker), "responsibility appearing after preview blocks the real deployment callback");
        Check(Wire.FileHash(Path.Combine(config, "business.json")) == originalHash && !File.Exists(Path.Combine(directoryRoot, "sportmaster.db")), "maintenance preserves the original draft and never fabricates a database");
    }
}

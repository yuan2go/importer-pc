using System.Diagnostics;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Platform;
using Microsoft.Data.Sqlite;

static class StabilityAuditFixes
{
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); Console.WriteLine("PASS AUDIT-FIX02 " + message); }
    public static async Task Run(string workspace, string root)
    {
        History(workspace, root);
        await Maintenance(root);
    }
    public static void History(string workspace, string root)
    {
        var configuration = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace, "tests/Ipc.Application.Verification/sportmaster.synthetic.json")));
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
            ("empty-request", "DELETE FROM parcel_integration_id_owners; UPDATE parcel_operations SET body=json_set(body,'$.Wcs.Request.Id','') WHERE id='op-2'"),
            ("whitespace-request", "DELETE FROM parcel_integration_id_owners; UPDATE parcel_operations SET body=json_set(body,'$.Wcs.Request.Id','   ') WHERE id='op-2'"),
            ("missing-request", "UPDATE parcel_operations SET body=json_set(body,'$.Wcs.Request.Id',NULL) WHERE id='op-2'"),
            ("archive-hash", "UPDATE parcel_archives SET body=body||' ' WHERE id='op-1'"),
            ("row-owner", "UPDATE parcel_operations SET body=json_set(body,'$.Id','op-1') WHERE id='op-2'"),
            ("backfill-row-owner", "DELETE FROM parcel_integration_id_owners; UPDATE parcel_operations SET body=json_set(body,'$.Id','foreign-op') WHERE id='op-2'"),
            ("backfill-row-admission", "DELETE FROM parcel_integration_id_owners; UPDATE parcel_operations SET body=json_set(body,'$.AdmissionId','foreign-entity') WHERE id='op-2'"),
            ("backfill-row-observation", "DELETE FROM parcel_integration_id_owners; UPDATE parcel_operations SET body=json_set(body,'$.ObservationId','foreign-observation') WHERE id='op-2'"),
            ("missing-owner", "DELETE FROM parcel_integration_id_owners WHERE id=1"),
            ("owner-hash", "UPDATE parcel_integration_id_owners SET request_hash='changed' WHERE id=1"),
            ("partial-backfill", "DELETE FROM parcel_integration_id_owners; UPDATE parcel_archives SET body=body||' ' WHERE id='op-1'")
        };
        foreach (var test in cases)
        {
            var directory = Path.Combine(root, test.Name); Directory.CreateDirectory(directory);
            var database = Path.Combine(directory, "sportmaster.db"); File.Copy(baseline, database);
            using (var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Pooling = false }.ToString()))
            {
                db.Open();using var sql=db.CreateCommand();sql.CommandText="PRAGMA journal_mode=DELETE";sql.ExecuteScalar();
                sql.CommandText=test.Sql;sql.ExecuteNonQuery();
            }
            var owners=HistoryOpenSafety.Owners(database);var bytes=HistoryOpenSafety.Snapshot(directory);var rejected=false;
            try { using var invalid = new ParcelRepository(directory, "sportmaster"); }
            catch (Exception e) when (e is InvalidDataException or SqliteException or System.Text.Json.JsonException) { rejected = true; }
            Check(rejected, test.Name + " refuses corrupted original history");
            Check(HistoryOpenSafety.Owners(database)==owners, test.Name + " does not partially publish owner backfill");
            Check(HistoryOpenSafety.Snapshot(directory)==bytes,test.Name+" leaves closed original database, sidecars and directory unchanged");
        }
        using (var repository = new ParcelRepository(fixture, "sportmaster"))
        {
            Check(repository.IntegrationBudget(new(1, 100)).PermanentHighWater == 2, "resident and archived numeric originals retain the allocation floor");
            repository.Backup(Path.Combine(root, "verified-originals.db"));
        }
        SelfConsistentBlankOwners(baseline,root);
        HistoryOpenSafety.ChangedArchiveRequest(baseline,root);
        var scale=Path.Combine(root,"scale-originals");HistoryOpenBenchmark.Populate(scale,configuration);
        var facts=HistoryOpenBenchmark.ReadFacts(scale);List<double> samples=[];
        for(var i=0;i<3;i++)samples.Add(HistoryOpenBenchmark.Measure(scale,facts));
        Check(facts.Resident==1000&&facts.Archived==1000&&facts.MaximumArchived==2000&&facts.Floor==2000,
            "mixed public-open smoke retains 2000 permanent owners and highest archived allocation");
        HistoryOpenSafety.Cancellation(scale,root);
        ExtensionStore.AtomicWrite(Path.Combine(root,"HISTORY-CURRENT.json"),new{Rows=2000,Facts=facts,Samples=samples,
            Scope="Current public-constructor smoke only; paired parent/candidate comparison is separate; no runtime/physical/24h qualification",
            RuntimeSha256=Wire.FileHash(typeof(ParcelRepository).Assembly.Location)});
    }

    static void SelfConsistentBlankOwners(string baseline,string root)
    {
        List<(string Name,bool Refused,bool BytesUnchanged,bool OwnersUnchanged,bool DirectoryUnchanged)> outcomes=[];
        foreach(var (name,id) in new[]{("empty",""),("spaces","   "),("tab","\t")})
        {
            var directory=Path.Combine(root,"consistent-owner-"+name);Directory.CreateDirectory(directory);
            var file=Path.Combine(directory,"sportmaster.db");File.Copy(baseline,file);
            string Owners()
            {
                using var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=file,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString());db.Open();
                using var q=db.CreateCommand();q.CommandText="SELECT id,operation,request,request_hash FROM parcel_integration_id_owners ORDER BY id";
                using var r=q.ExecuteReader();List<object> rows=[];while(r.Read())rows.Add(new{Id=r.GetInt64(0),Operation=r.GetString(1),Request=r.GetString(2),Hash=r.GetString(3)});
                return Wire.Hash(Wire.Encode(rows));
            }
            using(var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=file,Pooling=false}.ToString()))
            {
                db.Open();using var q=db.CreateCommand();q.CommandText="PRAGMA journal_mode=DELETE";q.ExecuteScalar();
                q.CommandText="SELECT body FROM parcel_archives WHERE id='op-1'";var original=Wire.Decode<ParcelOperation>((string)q.ExecuteScalar()!);
                var request=original.Wcs!.Request with{Id=id};var changed=original with{Wcs=original.Wcs with{Request=request}};var body=Wire.Encode(changed);
                using var tx=db.BeginTransaction();q.Transaction=tx;
                q.CommandText="UPDATE parcel_archives SET body=$body,body_hash=$hash WHERE id='op-1'";q.Parameters.AddWithValue("$body",body);q.Parameters.AddWithValue("$hash",Wire.Hash(body));q.ExecuteNonQuery();
                q.Parameters.Clear();q.CommandText="UPDATE parcel_integration_id_owners SET request=$request,request_hash=$hash WHERE id=1";
                q.Parameters.AddWithValue("$request",id);q.Parameters.AddWithValue("$hash",Wire.Hash(Wire.Encode(request)));q.ExecuteNonQuery();tx.Commit();
            }
            var bytes=Wire.FileHash(file);var owners=Owners();var snapshot=HistoryOpenSafety.Snapshot(directory);var refused=false;
            try{using var invalid=new ParcelRepository(directory,"sportmaster");}catch(InvalidDataException){refused=true;}
            var outcome=(Name:name,Refused:refused,BytesUnchanged:Wire.FileHash(file)==bytes,OwnersUnchanged:Owners()==owners,DirectoryUnchanged:HistoryOpenSafety.Snapshot(directory)==snapshot);outcomes.Add(outcome);
            Console.WriteLine((refused?"PASS":"FAIL")+" WT054-IDENTITY "+System.Text.Json.JsonSerializer.Serialize(new{outcome.Name,outcome.Refused,outcome.BytesUnchanged,outcome.OwnersUnchanged,outcome.DirectoryUnchanged}));
        }
        Check(outcomes.All(x=>x.Refused&&x.BytesUnchanged&&x.OwnersUnchanged&&x.DirectoryUnchanged),"self-consistent blank archived requests must refuse without rewriting original bytes or owners");
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

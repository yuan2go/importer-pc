using Ipc.Adapters;
using Ipc.Platform;
using Ipc.Supply;
using Microsoft.Data.Sqlite;

static class AlignmentDataVerification
{
    public static async Task Run(string root, string url, string workspace)
    {
        await LegacyMigration(root, workspace);
        Console.WriteLine("PASS A01 schema1 migration, frozen payload, restore isolation and future format rejection");
        var cfg = Rig.Config(url, demand: 1); var directory = Path.Combine(root, "alignment-data");
        string sessionId, operationId, originalPayload, originalHash; ContextSnapshot context; TaskSnapshot task;
        await using (var h = await Rig.Open(directory, cfg))
        {
            await h.OpenBox(); var session = (await h.App.SnapshotAsync()).State.Session!; sessionId = session.Id;
            context = session.Context ?? throw new InvalidOperationException("Context semantic receipt was not projected");
            var op = await h.Enter("left"); operationId = op.Id;
            Assert(op.BoxBarcode == "000001" && op.BoxWave == context.BoxWave, "Admission freezes original box barcode and box wave");
            await h.Event("left", "Read", op.AdmissionId, ["000123"], 1); await h.Drain();
            op = await h.Op("left"); task = op.Task ?? throw new InvalidOperationException("Validation task semantic receipt was not projected");
            Assert(task.Sku is not null && task.TaskId is not null && task.Destination is not null && task.Unit == "件", "Task fields supplied by independent fixture");
            Assert(task.ItemWave != context.BoxWave && task.ItemWave!.StartsWith("SIM-ITEM-WAVE-") && context.BoxWave!.StartsWith("SIM-BOX-WAVE-"), "Item wave and box wave have separate identities");
            Assert(task.Provenance.Source == "Independent synthetic task fixture" && task.Provenance.Version == "SYNTHETIC-SEMANTICS-v1", "Synthetic provenance remains explicit");
            var validation = (await h.App.DetailsAsync(op.Id)).Single(x => x.Request.Kind == RequestKind.Validate);
            Assert(validation.Responses.Single().Task == task && validation.Responses.Single().EvidenceId == task.Provenance.EvidenceId, "Projection is bound to persisted semantic source evidence");
            var repeated = await h.Port.ExecuteAsync(validation.Request, CancellationToken.None);
            Assert(repeated == validation.Responses.Single(), "Independent receiver repeats exact receipt including acquisition time");
            await h.Command("Submit", op.Id); await h.Drain();
            var submit = (await h.App.DetailsAsync(op.Id)).Single(x => x.Request.Kind == RequestKind.Submit);
            originalPayload = submit.Request.Payload; originalHash = submit.Request.PayloadHash;
            var returnedTask = submit.Responses.Single().Task!;
            Assert(returnedTask.Sku == task.Sku && returnedTask.ItemWave == task.ItemWave && returnedTask.TaskId == task.TaskId && returnedTask.Destination == task.Destination && returnedTask.Unit == task.Unit, "Original entity preserves semantic fields across separate request identities");
            await h.App.CompleteAsync(submit.Request, null, submit.Responses.Single());
            Assert((await h.App.SnapshotAsync()).AcceptedQuantity == 1 && h.Store.AcceptedQuantity(sessionId) == 1, "Unique accepted ledger projects once after repeated evidence");
            await h.Event("left", "Depart", op.AdmissionId); await h.Event("left", "Reset", op.AdmissionId);
            await h.Command("CheckClose"); await h.Drain(); var closing = (await h.App.SnapshotAsync()).State.Session!;
            await h.Command("ConfirmClose", closing.CloseCheckRequest, "CONFIRM"); await h.Drain();
            await h.Command("OpenContext", value: "000002"); await h.Drain();
            Assert((await h.App.SnapshotAsync()).State.Session!.Context!.BoxWave != context.BoxWave, "New box has its own semantic context");
            var historical = (await h.App.HistoryAsync()).Single(x => x.Id == operationId);
            Assert(historical.BoxBarcode == "000001" && historical.BoxWave == context.BoxWave && historical.Task == task, "History is not rewritten from current box or submit provenance");
        }
        await using (var h = await Rig.Open(directory, cfg))
        {
            var historical = (await h.App.HistoryAsync()).Single(x => x.Id == operationId);
            Assert(historical.BoxBarcode == "000001" && historical.BoxWave == context.BoxWave && historical.Task == task && historical.Barcode == "000123" && historical.Supply == 1, "Complete task chain survives restart");
            Assert(h.Store.Session(sessionId)!.Context == context, "Old session context survives replacement and restart");
            var submit = (await h.App.DetailsAsync(operationId)).Single(x => x.Request.Kind == RequestKind.Submit);
            Assert(submit.Request.Payload == originalPayload && submit.Request.PayloadHash == originalHash && Wire.Hash(originalPayload) == originalHash, "Original request bytes/hash survive new semantic snapshots and restart");
            Assert(h.Store.AcceptedQuantity(sessionId) == 1, "Restart does not recount unique acceptance");
        }
        Console.WriteLine("PASS A01 independent semantic fixture, task/request/restart/history consistency and unique acceptance");
    }

    static Task LegacyMigration(string root, string workspace)
    {
        var fixture = Path.Combine(workspace, "tests/Ipc.Verification/fixtures"); var schema = File.ReadAllText(Path.Combine(fixture, "schema1.sql"));
        const string baselineHash = "D4CED2B811DFF4D3D66DC66C431B9DCC8C75F491CA30F10A2E5507AAD741C981";
        Assert(Wire.Hash(schema) == baselineHash, "Schema1 fixture independently pinned to main 2b8b35b");
        var original = Wire.Decode<BusinessRequest>(File.ReadAllText(Path.Combine(fixture, "simulation-v1-request.json")));
        var oldResponseText = File.ReadAllText(Path.Combine(fixture, "simulation-v1-response.json")).TrimEnd();
        var oldResponse = Wire.Decode<IntegrationResponse>(oldResponseText);
        Assert(oldResponse.Context is null && oldResponse.Task is null && Wire.Encode(oldResponse) == oldResponseText, "Old receipt optional fields remain absent and bytes unchanged");
        var legacy = Path.Combine(root, "legacy-schema1.db");
        using (var db = Open(legacy))
        {
            Exec(db, schema); Exec(db, "CREATE TABLE schema_history(id INTEGER PRIMARY KEY,checksum TEXT NOT NULL,applied TEXT NOT NULL)");
            Exec(db, "INSERT INTO schema_history VALUES(1,$a,$b)", baselineHash, DateTimeOffset.UtcNow.ToString("O"));
            var sessionText = "{\"Id\":\"" + original.SessionId + "\",\"Barcode\":\"000001\",\"Status\":\"Active\"}";
            Exec(db, "INSERT INTO sessions VALUES($a,$b)", original.SessionId, sessionText);
            Exec(db, "INSERT INTO operations VALUES('legacy-operation',$a,'left',$b,$c)", original.SessionId, DateTimeOffset.UtcNow.ToString("O"), "{\"Id\":\"legacy-operation\",\"SessionId\":\"" + original.SessionId + "\",\"Lane\":\"left\",\"Barcode\":\"000123\"}");
            Exec(db, "INSERT INTO requests VALUES($a,$b,$c,$d,$e,$f,$g)", original.Id, original.OwnerId, original.SessionId, original.Kind.ToString(), original.IntentKey, original.Status.ToString(), Wire.Encode(original));
        }
        var restoredLegacy = Path.Combine(root, "legacy-restore", "station.db"); SupplyStore.RestoreVerified(legacy, restoredLegacy);
        using (var restored = new SupplyStore(restoredLegacy)) Assert(restored.Load().Quarantined && File.Exists(Path.Combine(root, "legacy-restore", "restore-quarantine")), "Legacy backup migration remains isolated");
        using (var store = new SupplyStore(legacy))
        {
            Assert(store.Session(original.SessionId)!.Context is null && store.History(null, 0, 100).Single().Task is null && store.History(null, 0, 100).Single().BoxWave is null, "Legacy missing fields remain unknown");
            Assert(store.Request(original.Id)!.Payload == original.Payload && store.Request(original.Id)!.PayloadHash == original.PayloadHash, "Migration does not reserialize frozen request");
        }
        using (var db = Open(legacy))
        {
            using var c = db.CreateCommand(); c.CommandText = "SELECT checksum FROM schema_history WHERE id=1";
            Assert((string?)c.ExecuteScalar() == baselineHash, "Original migration checksum remains unchanged");
            c.CommandText = "SELECT count(*) FROM schema_history WHERE id<>1";
            Assert(Convert.ToInt32(c.ExecuteScalar()) == 1, "Original reader's id<>1 guard rejects format2");
        }
        var backup = Path.Combine(root, "format2-backup.db"); using (var store = new SupplyStore(legacy)) store.Backup(backup);
        var restoredCurrent = Path.Combine(root, "format2-restore", "station.db"); SupplyStore.RestoreVerified(backup, restoredCurrent);
        using (var store = new SupplyStore(restoredCurrent)) Assert(store.Load().Quarantined, "Matching format2 backup restores only into quarantine");
        using (var db = Open(backup)) Exec(db, "UPDATE storage_format SET version=3,minimum_reader=3");
        Throws(() => { using var rejected = new SupplyStore(backup); });
        var rejectedPath = Path.Combine(root, "future-restore", "station.db"); Throws(() => SupplyStore.RestoreVerified(backup, rejectedPath));
        Assert(!File.Exists(rejectedPath) && !Directory.Exists(Path.GetDirectoryName(rejectedPath)), "Future format rejected before restore output");
        using (var db = Open(legacy)) Exec(db, "INSERT INTO schema_history VALUES(3,'future',$a)", DateTimeOffset.UtcNow.ToString("O"));
        Throws(() => { using var rejected = new SupplyStore(legacy); });
        return Task.CompletedTask;
    }
    static SqliteConnection Open(string path) { var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()); db.Open(); return db; }
    static void Exec(SqliteConnection db, string sql, params object?[] values)
    {
        using var c = db.CreateCommand(); c.CommandText = sql;
        for (var i = 0; i < values.Length; i++) c.Parameters.AddWithValue("$" + (char)('a' + i), values[i] ?? DBNull.Value);
        c.ExecuteNonQuery();
    }
    static void Throws(Action action) { try { action(); } catch (InvalidDataException) { return; } throw new InvalidOperationException("Incompatible storage unexpectedly admitted"); }
    static void Assert(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
}

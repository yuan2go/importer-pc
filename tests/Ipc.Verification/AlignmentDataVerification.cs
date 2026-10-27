using Ipc.Adapters;
using Ipc.Platform;
using Ipc.Supply;
using Microsoft.Data.Sqlite;

static class AlignmentDataVerification
{
    public static async Task Run(string root, string url, string workspace)
    {
        await LegacyMigration(root, workspace);
        Console.WriteLine("PASS A01 old formats refuse mutation; read-only custody/backup retain frozen originals and current restore isolation");
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

    internal const string Schema1Checksum = "D4CED2B811DFF4D3D66DC66C431B9DCC8C75F491CA30F10A2E5507AAD741C981";
    internal const string Format2Checksum = "4C1BBC06BAF48D95D464867131CD63BFD7048DAC64515B07916652497B60A897";
    internal const string Format3Checksum = "AF01C0A2D5AE4FEB3481E4E703C0BC544CC746FD044736CD8A5B3D86B661FD25";
    const string Format2Migration = "CREATE TABLE storage_format(id INTEGER PRIMARY KEY CHECK(id=1),version INTEGER NOT NULL,minimum_reader INTEGER NOT NULL);\nINSERT INTO storage_format VALUES(1,2,2);";

    public static Task LegacyMigration(string root, string workspace)
    {
        var fixtures = Path.Combine(workspace, "tests/Ipc.Verification/fixtures");
        // Git checkout line endings are not part of the pinned historical SQL fixture.
        var schema = File.ReadAllText(Path.Combine(fixtures, "schema1.sql")).Replace("\r\n", "\n");
        Assert(Wire.Hash(schema) == Schema1Checksum && Wire.Hash(Format2Migration) == Format2Checksum, "Historical schema1 and format2 SQL independently pinned");
        var original = Wire.Decode<BusinessRequest>(File.ReadAllText(Path.Combine(fixtures, "simulation-v1-request.json")));
        var responseText = File.ReadAllText(Path.Combine(fixtures, "simulation-v1-response.json")).TrimEnd();
        var oldResponse = Wire.Decode<IntegrationResponse>(responseText);
        Assert(oldResponse.Context is null && oldResponse.Task is null && Wire.Encode(oldResponse) == responseText, "Old receipt optional fields remain absent and bytes unchanged");
        var release = new MaintenanceReleaseIdentity(Schema1Checksum, Schema1Checksum, Format2Checksum, new() { ["synthetic-config.json"] = Format2Checksum });
        var selected = release with { ProgramAssemblySha256 = Format3Checksum };
        string Legacy(string name, int format = 1, bool active = false)
        {
            var path = Path.Combine(root, name + ".db");
            using var db = Open(path);
            Exec(db, schema); Exec(db, "CREATE TABLE schema_history(id INTEGER PRIMARY KEY,checksum TEXT NOT NULL,applied TEXT NOT NULL)");
            Exec(db, "INSERT INTO schema_history VALUES(1,$a,$b)", Schema1Checksum, DateTimeOffset.UtcNow.ToString("O"));
            if (format >= 2)
            {
                Exec(db, Format2Migration); Exec(db, "INSERT INTO schema_history VALUES(2,$a,$b)", Format2Checksum, DateTimeOffset.UtcNow.ToString("O"));
            }
            if (format >= 3)
            {
                Exec(db, "UPDATE storage_format SET version=3,minimum_reader=3 WHERE id=1;");
                Exec(db, "INSERT INTO schema_history VALUES(3,$a,$b)", Format3Checksum, DateTimeOffset.UtcNow.ToString("O"));
            }
            if (format >= 4)
            {
                Exec(db, "CREATE TABLE response_conflicts(id TEXT PRIMARY KEY,request TEXT NOT NULL REFERENCES requests(id),json TEXT NOT NULL); CREATE INDEX response_conflicts_request ON response_conflicts(request); UPDATE storage_format SET version=4,minimum_reader=4 WHERE id=1;");
                Exec(db, "INSERT INTO schema_history VALUES(4,$a,$b)", ConformanceVerification.Format4Checksum, DateTimeOffset.UtcNow.ToString("O"));
            }
            var sessionText = "{\"Id\":\"" + original.SessionId + "\",\"Barcode\":\"000001\",\"Status\":\"" + (active ? "Active" : "Closed") + "\"}";
            Exec(db, "INSERT INTO sessions VALUES($a,$b)", original.SessionId, sessionText);
            Exec(db, "INSERT INTO operations VALUES($a,$b,'left',$c,$d)", original.OwnerId, original.SessionId, DateTimeOffset.UtcNow.ToString("O"),
                "{\"Id\":\"" + original.OwnerId + "\",\"SessionId\":\"" + original.SessionId + "\",\"Lane\":\"left\",\"Barcode\":\"000123\"}");
            Exec(db, "INSERT INTO requests VALUES($a,$b,$c,$d,$e,$f,$g)", original.Id, original.OwnerId, original.SessionId, original.Kind.ToString(), original.IntentKey, original.Status.ToString(), Wire.Encode(original));
            return path;
        }
        foreach (var format in new[] { 1, 2, 3, 4 })
        {
            var source = Legacy("legacy-format" + format, format); var before = Wire.FileHash(source);
            var inspection = SupplyStore.InspectReadOnly(source);
            Assert(inspection.FormatVersion == format && inspection.MinimumReader == format && !inspection.SelectedReaderCompatible && !inspection.ExplicitUpgradeSupported, "Read-only inspection identifies format" + format);
            Throws(() => { using var denied = new SupplyStore(source); });
            Assert(Wire.FileHash(source) == before, "Inspection and default startup refusal do not mutate original format" + format);
            var backup = Path.Combine(root, "original-format" + format + "-backup.db");
            var backupReceipt = SupplyStore.BackupReadOnly(source, backup);
            Assert(backupReceipt.Source.FormatVersion == format && backupReceipt.Backup.FormatVersion == format && backupReceipt.Sha256 == Wire.FileHash(backup), "Backup receipt binds original format and physical SHA");
            Assert(Wire.FileHash(source) == before && SupplyStore.InspectReadOnly(backup).FormatVersion == format, "Backup preserves source and is original-reader compatible");
            using (var reader = Open(backup))
            {
                using var command = reader.CreateCommand(); command.CommandText = "SELECT count(*) FROM schema_history WHERE id>" + format;
                Assert(Convert.ToInt32(command.ExecuteScalar()) == 0, "Selected historical reader recognizes its own original-format backup");
            }
            var restore = Path.Combine(root, "legacy-format" + format + "-restore", "station.db");
            Throws(()=>SupplyStore.RestoreVerified(backup,restore));
            Assert(!File.Exists(restore) && !Directory.Exists(Path.GetDirectoryName(restore)),"old backup restore refuses before creating destination");
            var receiptPath=Path.Combine(root,"format"+format+"-upgrade.json");var upgradeBackup=Path.Combine(root,"format"+format+"-pre-upgrade.db");
            Throws(()=>SupplyStore.UpgradeVerified(source,upgradeBackup,receiptPath,release,selected));
            Assert(Wire.FileHash(source)==before && !File.Exists(receiptPath) && !File.Exists(upgradeBackup),"old upgrade refuses without source writes, conversion backup or success receipt");
            using(var reader=Open(backup))
            { using var command=reader.CreateCommand();command.CommandText="SELECT json FROM requests WHERE id=$id";command.Parameters.AddWithValue("$id",original.Id);
              Assert((string)command.ExecuteScalar()! == Wire.Encode(original),"read-only original backup retains exact request identity/payload/contract/target"); }
        }
        var blocked=Legacy("legacy-active",active:true);var blockedHash=Wire.FileHash(blocked);
        Throws(()=>SupplyStore.UpgradeVerified(blocked,blocked+".backup",blocked+".receipt",release,selected));
        Assert(Wire.FileHash(blocked)==blockedHash && !File.Exists(blocked+".backup") && !File.Exists(blocked+".receipt"),"unresolved legacy responsibility cannot acquire a conversion or success receipt");
        foreach (var damage in new[] { "future-format", "future-migration", "wrong-checksum" })
        {
            var future = Legacy(damage, 2);
            using (var db = Open(future)) Exec(db, damage switch
            {
                "future-format" => "UPDATE storage_format SET version=4,minimum_reader=4",
                "future-migration" => "INSERT INTO schema_history VALUES(4,'future','2099-01-01T00:00:00Z')",
                _ => "UPDATE schema_history SET checksum='tampered' WHERE id=2"
            });
            var hash = Wire.FileHash(future); var destination = Path.Combine(root, damage + "-restore", "station.db");
            Throws(() => SupplyStore.InspectReadOnly(future)); Throws(() => { using var rejected = new SupplyStore(future); });
            Throws(() => SupplyStore.RestoreVerified(future, destination));
            Assert(Wire.FileHash(future) == hash && !File.Exists(destination) && !Directory.Exists(Path.GetDirectoryName(destination)), "Future/unknown/checksum evidence refused before restore output and source write: " + damage);
        }
        return Task.CompletedTask;
    }
    static void Expect<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected maintenance failure " + typeof(T).Name); }
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

using Ipc.Adapters;
using Ipc.Platform;
using Ipc.Supply;
using Microsoft.Data.Sqlite;

static class AlignmentDataVerification
{
    public static async Task Run(string root, string url, string workspace)
    {
        await CurrentFormatGate(root);
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

    public static Task CurrentFormatGate(string root)
    {
        var directory = Path.Combine(root, "current-storage"); Directory.CreateDirectory(directory);
        var source = Path.Combine(directory, "station.db"); using (var store = new SupplyStore(source)) { }
        Assert(SupplyStore.InspectReadOnly(source).FormatVersion == 6, "Fresh Supply baseline is format6");
        var backup = Path.Combine(root, "current-backup.db"); var receipt = SupplyStore.BackupReadOnly(source, backup);
        Assert(receipt.Sha256 == Wire.FileHash(backup) && receipt.Backup.FormatVersion == 6, "Current backup binds exact bytes and format");
        foreach (var format in Enumerable.Range(1,5).Append(7))
        {
            var file = Path.Combine(root,"noncurrent-"+format+".db"); File.Copy(source,file);
            using (var db = Open(file)) Exec(db,"UPDATE schema_history SET id=$a; UPDATE storage_format SET version=$a,minimum_reader=$a",format);
            var before = Wire.FileHash(file); var destination = Path.Combine(root,"rejected-"+format,"station.db");
            Throws(() => SupplyStore.InspectReadOnly(file)); Throws(() => { using var denied = new SupplyStore(file); });
            Throws(() => SupplyStore.BackupReadOnly(file,destination)); Throws(() => SupplyStore.RestoreVerified(file,destination));
            Assert(Wire.FileHash(file)==before && !Directory.Exists(Path.GetDirectoryName(destination)), "Non-current format refuses before changing bytes or creating destination");
        }
        foreach (var sql in new[]{"UPDATE schema_history SET checksum='changed'", "INSERT INTO schema_history VALUES(7,'extra','synthetic')", "UPDATE storage_format SET minimum_reader=99"})
        {
            var file=Path.Combine(root,Wire.Id()+".db"); File.Copy(source,file); using(var db=Open(file))Exec(db,sql);
            var before=Wire.FileHash(file); Throws(()=>SupplyStore.InspectReadOnly(file)); Throws(()=>{using var denied=new SupplyStore(file);});
            Assert(Wire.FileHash(file)==before,"Corrupt baseline identity refuses without writes");
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

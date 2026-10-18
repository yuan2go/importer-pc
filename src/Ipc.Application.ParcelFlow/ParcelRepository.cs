using Ipc.Application.Contracts;
using Ipc.Platform;
using Ipc.Platform.Storage;
using Microsoft.Data.Sqlite;
namespace Ipc.Application.ParcelFlow;

// Application owns SQL, aggregate semantics and the T1/T2/T3 transaction boundaries.
public sealed class ParcelRepository : IDisposable
{
    readonly SqliteConnection db;
    readonly string rawRoot;
    static readonly DatabaseMigration[] migrations = [new(1, """
        CREATE TABLE parcel_meta(id INTEGER PRIMARY KEY CHECK(id=1),revision INTEGER NOT NULL,paused INTEGER NOT NULL);
        INSERT INTO parcel_meta VALUES(1,0,0);
        CREATE TABLE parcel_operations(id TEXT PRIMARY KEY,admission TEXT NOT NULL UNIQUE,observation TEXT NOT NULL UNIQUE,body TEXT NOT NULL);
        CREATE TABLE parcel_audit(seq INTEGER PRIMARY KEY AUTOINCREMENT,at TEXT NOT NULL,event TEXT NOT NULL,owner TEXT NOT NULL,detail TEXT NOT NULL);
        CREATE TABLE parcel_commands(id TEXT PRIMARY KEY,hash TEXT NOT NULL,receipt TEXT NOT NULL);
        CREATE TABLE parcel_receipts(id TEXT PRIMARY KEY,operation TEXT NOT NULL,stage TEXT NOT NULL,request TEXT NOT NULL,source TEXT NOT NULL,state TEXT NOT NULL,raw_hash TEXT);
        CREATE TABLE parcel_measurements(id TEXT PRIMARY KEY,admission TEXT NOT NULL,body TEXT NOT NULL);
        """)];
    public long Revision { get; private set; }
    public bool Paused { get; private set; }
    public ParcelRepository(string directory, string application)
    {
        Directory.CreateDirectory(directory); rawRoot = Path.Combine(directory, "receipts"); Directory.CreateDirectory(rawRoot);
        db = Database.Open(Path.Combine(directory, application + ".db"));
        try
        {
            Database.Migrate(db, application, migrations);
            using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT revision,paused FROM parcel_meta WHERE id=1";
            using var r = cmd.ExecuteReader(); if (!r.Read()) throw new InvalidDataException("Missing parcel metadata");
            Revision = r.GetInt64(0); Paused = r.GetBoolean(1);
        }
        catch { db.Dispose(); throw; }
    }
    public ParcelOperation[] Load()
    {
        using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT body FROM parcel_operations ORDER BY rowid";
        using var r = cmd.ExecuteReader(); List<ParcelOperation> result = [];
        while (r.Read()) result.Add(Wire.Decode<ParcelOperation>(r.GetString(0))); return result.ToArray();
    }
    public void Commit(ParcelOperation? operation, string eventName, string detail, bool? paused = null, string? occurrence = null, BusinessCommand? command = null)
    {
        using var tx = db.BeginTransaction();
        if (operation is not null)
        {
            using var save = db.CreateCommand(); save.Transaction = tx;
            save.CommandText = "INSERT INTO parcel_operations VALUES($id,$admission,$observation,$body) ON CONFLICT(id) DO UPDATE SET body=excluded.body";
            save.Parameters.AddWithValue("$id", operation.Id); save.Parameters.AddWithValue("$admission", operation.AdmissionId);
            save.Parameters.AddWithValue("$observation", operation.ObservationId); save.Parameters.AddWithValue("$body", Wire.Encode(operation)); save.ExecuteNonQuery();
        }
        using (var meta = db.CreateCommand())
        {
            meta.Transaction = tx; meta.CommandText = "UPDATE parcel_meta SET revision=revision+1,paused=$paused WHERE id=1";
            meta.Parameters.AddWithValue("$paused", paused ?? Paused); meta.ExecuteNonQuery();
        }
        using (var audit = db.CreateCommand())
        {
            audit.Transaction = tx; audit.CommandText = "INSERT INTO parcel_audit(at,event,owner,detail) VALUES($at,$event,$owner,$detail)";
            audit.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O")); audit.Parameters.AddWithValue("$event", eventName);
            audit.Parameters.AddWithValue("$owner", operation?.Id ?? "runtime"); audit.Parameters.AddWithValue("$detail", detail); audit.ExecuteNonQuery();
        }
        if (occurrence is not null)
        {
            using var receipt = db.CreateCommand(); receipt.Transaction = tx; receipt.CommandText = "UPDATE parcel_receipts SET state='Committed' WHERE id=$id AND state='RawDurable'";
            receipt.Parameters.AddWithValue("$id", occurrence); if (receipt.ExecuteNonQuery() != 1) throw new InvalidDataException("Receipt occurrence is not raw-durable");
        }
        if (command is not null)
        {
            using var commandReceipt = db.CreateCommand(); commandReceipt.Transaction = tx;
            commandReceipt.CommandText = "INSERT INTO parcel_commands VALUES($id,$hash,$receipt)";
            commandReceipt.Parameters.AddWithValue("$id", command.Id); commandReceipt.Parameters.AddWithValue("$hash", Wire.Hash(Wire.Encode(command)));
            commandReceipt.Parameters.AddWithValue("$receipt", Wire.Encode(new BusinessReceipt("Persisted", "Application command committed", Revision + 1))); commandReceipt.ExecuteNonQuery();
        }
        tx.Commit(); Revision++; if (paused is not null) Paused = paused.Value;
    }
    public string BeginReceipt(ParcelOperation op, StageFact stage, string source)
    {
        var id = Wire.Id(); using var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT INTO parcel_receipts VALUES($id,$op,$stage,$request,$source,'Awaiting',NULL)";
        cmd.Parameters.AddWithValue("$id", id); cmd.Parameters.AddWithValue("$op", op.Id); cmd.Parameters.AddWithValue("$stage", stage.Request.Stage);
        cmd.Parameters.AddWithValue("$request", stage.Request.Id); cmd.Parameters.AddWithValue("$source", source); cmd.ExecuteNonQuery(); return id;
    }
    public void StoreRaw(string occurrence, StageReply reply)
    {
        ExtensionStore.AtomicWrite(Path.Combine(rawRoot, occurrence + ".json"), reply);
        using var cmd = db.CreateCommand(); cmd.CommandText = "UPDATE parcel_receipts SET state='RawDurable',raw_hash=$hash WHERE id=$id AND state='Awaiting'";
        cmd.Parameters.AddWithValue("$id", occurrence); cmd.Parameters.AddWithValue("$hash", Wire.Hash(Wire.Encode(reply)));
        if (cmd.ExecuteNonQuery() != 1) throw new InvalidDataException("Receipt occurrence cannot be reused");
    }
    public void NoReply(string occurrence)
    {
        using var cmd = db.CreateCommand(); cmd.CommandText = "UPDATE parcel_receipts SET state='NoReply' WHERE id=$id AND state='Awaiting'";
        cmd.Parameters.AddWithValue("$id", occurrence); cmd.ExecuteNonQuery();
    }
    public void ValidateRawInventory()
    {
        using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT id,state,raw_hash FROM parcel_receipts";
        using var r = cmd.ExecuteReader(); Dictionary<string, (string State, string? Hash)> known = [];
        while (r.Read()) known.Add(r.GetString(0), (r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2)));
        foreach (var file in Directory.EnumerateFiles(rawRoot))
        {
            var id = Path.GetFileNameWithoutExtension(file);
            if (Path.GetExtension(file) != ".json" || !Guid.TryParseExact(id, "N", out _) || !known.TryGetValue(id, out var entry))
                throw new InvalidDataException("Unknown/interrupted original raw custody file; preserve and investigate");
            if (entry.Hash is not null && Wire.Hash(File.ReadAllText(file)) != entry.Hash) throw new InvalidDataException("Original raw receipt hash differs");
        }
        foreach (var entry in known.Where(x => x.Value.State is "Committed" or "RawDurable"))
            if (!File.Exists(Path.Combine(rawRoot, entry.Key + ".json"))) throw new InvalidDataException("Durable original raw receipt is missing");
    }
    public (string Id, string Operation, string Stage, string Request, StageReply? Reply)[] PendingReceipts()
    {
        using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT id,operation,stage,request,raw_hash FROM parcel_receipts WHERE state IN ('Awaiting','RawDurable')";
        using var r = cmd.ExecuteReader(); List<(string, string, string, string, StageReply?)> result = [];
        while (r.Read())
        {
            var id = r.GetString(0); var file = Path.Combine(rawRoot, id + ".json"); StageReply? reply = null;
            if (File.Exists(file))
            {
                reply = Wire.Decode<StageReply>(File.ReadAllText(file));
                if (!r.IsDBNull(4) && Wire.Hash(Wire.Encode(reply)) != r.GetString(4)) throw new InvalidDataException("Original receipt bytes changed");
            }
            result.Add((id, r.GetString(1), r.GetString(2), r.GetString(3), reply));
        }
        return result.ToArray();
    }
    public void RecoverRaw(string id, StageReply reply)
    {
        using var cmd = db.CreateCommand(); cmd.CommandText = "UPDATE parcel_receipts SET state='RawDurable',raw_hash=$hash WHERE id=$id AND state IN ('Awaiting','RawDurable')";
        cmd.Parameters.AddWithValue("$id", id); cmd.Parameters.AddWithValue("$hash", Wire.Hash(Wire.Encode(reply))); cmd.ExecuteNonQuery();
    }
    public bool RecordMeasurement(WeightMeasurement m)
    {
        using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT body FROM parcel_measurements WHERE id=$id"; cmd.Parameters.AddWithValue("$id", m.Id);
        if (cmd.ExecuteScalar() is string prior)
        { if (prior != Wire.Encode(m)) throw new InvalidDataException("Measurement identity reused with different content"); return false; }
        cmd.CommandText = "INSERT INTO parcel_measurements VALUES($id,$admission,$body)";
        cmd.Parameters.AddWithValue("$admission", m.AdmissionId); cmd.Parameters.AddWithValue("$body", Wire.Encode(m)); cmd.ExecuteNonQuery(); return true;
    }
    public BusinessReceipt? PriorCommand(BusinessCommand c)
    {
        using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT hash,receipt FROM parcel_commands WHERE id=$id"; cmd.Parameters.AddWithValue("$id", c.Id);
        using var r = cmd.ExecuteReader();
        return !r.Read() ? null : r.GetString(0) != Wire.Hash(Wire.Encode(c)) ? new("Rejected", "Command ID reused with changed content", Revision) : Wire.Decode<BusinessReceipt>(r.GetString(1));
    }
    public void SaveCommand(BusinessCommand c, BusinessReceipt result)
    {
        using var cmd = db.CreateCommand(); cmd.CommandText = "INSERT INTO parcel_commands VALUES($id,$hash,$receipt)";
        cmd.Parameters.AddWithValue("$id", c.Id); cmd.Parameters.AddWithValue("$hash", Wire.Hash(Wire.Encode(c))); cmd.Parameters.AddWithValue("$receipt", Wire.Encode(result)); cmd.ExecuteNonQuery();
    }
    public void Backup(string destination) => Database.Backup(db, destination);
    public void Dispose() => db.Dispose();
}

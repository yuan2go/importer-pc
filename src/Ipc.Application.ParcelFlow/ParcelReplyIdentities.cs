using Ipc.Application.Contracts;
using Ipc.Platform;
using Microsoft.Data.Sqlite;
namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRepository
{
    void RegisterReplyIdentity(string request, StageReply reply, SqliteTransaction tx)
    {
        if (StorageVersion < 4) return;
        using var identity = db.CreateCommand(); identity.Transaction = tx;
        // Keep the first durable bytes. ApplyReply compares a later duplicate against
        // this authority and records Conflict rather than replacing its identity.
        identity.CommandText = "INSERT INTO parcel_reply_identities VALUES($request,$id,$hash) ON CONFLICT(request,id) DO NOTHING";
        identity.Parameters.AddWithValue("$request", request); identity.Parameters.AddWithValue("$id", reply.Id);
        identity.Parameters.AddWithValue("$hash", Wire.Hash(Wire.Encode(reply))); identity.ExecuteNonQuery();
    }
    string OccurrenceRequest(string occurrence, SqliteTransaction tx)
    {
        using var command = db.CreateCommand(); command.Transaction = tx;
        command.CommandText = "SELECT request FROM parcel_receipts WHERE id=$id"; command.Parameters.AddWithValue("$id", occurrence);
        return command.ExecuteScalar() as string ?? throw new InvalidDataException("Original receipt occurrence unavailable");
    }
    void ValidateDurableReplyIdentities()
    {
        if (StorageVersion < 4) return;
        // DB4 previously could commit recovered bytes without this index. Stream all
        // original occurrences, including archived BLOBs, in their durable order.
        // No business facts or original response bytes are rewritten by this repair.
        using var tx = db.BeginTransaction();
        using var rows = db.CreateCommand(); rows.Transaction = tx;
        rows.CommandText = "SELECT id,request,operation,stage,state,raw_hash FROM parcel_receipts WHERE state IN ('Committed','RawDurable') ORDER BY rowid";
        using var reader = rows.ExecuteReader();
        while (reader.Read())
        {
            maintenanceCancellation.ThrowIfCancellationRequested();
            var id = reader.GetString(0); var path = Path.Combine(rawRoot, id + ".json"); byte[] bytes;
            if (File.Exists(path)) bytes = File.ReadAllBytes(path);
            else
            {
                using var archived = db.CreateCommand(); archived.Transaction = tx;
                archived.CommandText = "SELECT body FROM parcel_archived_receipts WHERE id=$id"; archived.Parameters.AddWithValue("$id", id);
                bytes = archived.ExecuteScalar() as byte[] ?? throw new InvalidDataException("Original reply unavailable during identity verification: " + id);
            }
            var reply = Wire.Decode<StageReply>(System.Text.Encoding.UTF8.GetString(bytes));
            if (Wire.Hash(Wire.Encode(reply)) != reader.GetString(5)) throw new InvalidDataException("Original reply hash differs: " + id);
            RegisterReplyIdentity(reader.GetString(1), reply, tx);
            if (reader.GetString(4) != "Committed" || reply.RequestId == reader.GetString(1) && OriginalReplyHash(reader.GetString(1), reply.Id) == Wire.Hash(Wire.Encode(reply))) continue;
            var operation = ReadOperation(reader.GetString(2)) ?? throw new InvalidDataException("Original reply operation unavailable");
            var stage = reader.GetString(3) switch
            {
                "image-capture" => operation.Capture, "image-upload" => operation.Upload,
                "wcs-slot" => operation.Wcs, "plc-submit" => operation.Plc,
                _ => throw new InvalidDataException("Unknown original reply stage")
            };
            if (stage?.Request.Id != reader.GetString(1) || stage.State != StageState.Conflict)
                throw new InvalidDataException("OriginalReplyIdentityConflict: committed facts lack the original contradiction responsibility; preserve all raw bytes and use controlled investigation: " + id);
        }
        Boundary?.Invoke("BeforeReplyIdentityRepairCommit"); tx.Commit();
    }
}

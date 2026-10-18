using Ipc.Application.Contracts;
using Ipc.Platform;
using System.Security.Cryptography;
namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRepository
{
    public string? OriginalReplyHash(string request, string reply)
    { using var c = db.CreateCommand(); c.CommandText = "SELECT hash FROM parcel_reply_identities WHERE request=$request AND id=$id"; c.Parameters.AddWithValue("$request", request); c.Parameters.AddWithValue("$id", reply); return c.ExecuteScalar() as string; }
    public long ArchivedCount
    { get { using var c = db.CreateCommand(); c.CommandText = "SELECT count(*) FROM parcel_archives"; return (long)c.ExecuteScalar()!; } }
    public bool HasOverloadAcknowledgement(string identity)
    { using var c = db.CreateCommand(); c.CommandText = "SELECT count(*) FROM parcel_overload_ack WHERE identity=$id"; c.Parameters.AddWithValue("$id", identity); return (long)c.ExecuteScalar()! == 1; }
    public ParcelOperation[] FindIdentities(string admission, string observation)
    {
        using var c = db.CreateCommand(); c.CommandText = "SELECT id FROM parcel_history_index WHERE admission=$admission OR observation=$observation ORDER BY seq LIMIT 2";
        c.Parameters.AddWithValue("$admission", admission); c.Parameters.AddWithValue("$observation", observation);
        List<string> ids = []; using (var r = c.ExecuteReader()) while (r.Read()) ids.Add(r.GetString(0));
        return ids.Select(id => ReadOperation(id) ?? throw new InvalidDataException("Identity original missing")).ToArray();
    }
    public ParcelOperation? ReadOperation(string id)
    {
        using var c = db.CreateCommand(); c.CommandText = "SELECT body,NULL FROM parcel_operations WHERE id=$id UNION ALL SELECT body,body_hash FROM parcel_archives WHERE id=$id";
        c.Parameters.AddWithValue("$id", id); using var r = c.ExecuteReader();
        if (!r.Read()) return null;
        var body = r.GetString(0); if (!r.IsDBNull(1) && Wire.Hash(body) != r.GetString(1)) throw new InvalidDataException("Archived original checksum differs");
        return Wire.Decode<ParcelOperation>(body);
    }
    public ParcelOperation? Latest(string position)
    {
        using var c = db.CreateCommand(); c.CommandText = "SELECT id FROM parcel_history_index WHERE position=$position ORDER BY seq DESC LIMIT 1";
        c.Parameters.AddWithValue("$position", position); var id = c.ExecuteScalar() as string; return id is null ? null : ReadOperation(id);
    }
    public ParcelHistoryPage History(long before, int limit)
    {
        if (before < 1 || limit is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(limit), "History page limit is 1..200; cursor must be positive");
        List<(long Sequence, string Id, bool Archived)> index = [];
        using (var c = db.CreateCommand())
        {
            c.CommandText = "SELECT h.seq,h.id,a.id IS NOT NULL FROM parcel_history_index h LEFT JOIN parcel_archives a ON a.id=h.id WHERE h.seq<$before ORDER BY h.seq DESC LIMIT $limit";
            c.Parameters.AddWithValue("$before", before); c.Parameters.AddWithValue("$limit", limit + 1);
            using var r = c.ExecuteReader(); while (r.Read()) index.Add((r.GetInt64(0), r.GetString(1), r.GetBoolean(2)));
        }
        var rows = index.Take(limit).Select(x => new ParcelHistoryItem(x.Sequence, x.Archived, ReadOperation(x.Id) ?? throw new InvalidDataException("History original missing"))).ToArray();
        return new(rows, index.Count > limit ? rows[^1].Sequence : null);
    }
    public void Archive(string[] ids, string reason)
    {
        if (ids.Length == 0) return;
        using var tx = db.BeginTransaction();
        foreach (var id in ids)
        {
            using var read = db.CreateCommand(); read.Transaction = tx; read.CommandText = "SELECT body FROM parcel_operations WHERE id=$id AND NOT EXISTS(SELECT 1 FROM parcel_receipts WHERE operation=$id AND state IN ('Awaiting','RawDurable'))";
            read.Parameters.AddWithValue("$id", id); var body = read.ExecuteScalar() as string ?? throw new InvalidOperationException("Archive candidate changed or has original receipt responsibility");
            ArchiveRaw(id, tx);
            using var archive = db.CreateCommand(); archive.Transaction = tx;
            archive.CommandText = "INSERT INTO parcel_archives SELECT id,admission,observation,body,$hash,$at FROM parcel_operations WHERE id=$id; DELETE FROM parcel_operations WHERE id=$id";
            archive.Parameters.AddWithValue("$id", id); archive.Parameters.AddWithValue("$hash", Wire.Hash(body)); archive.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O")); archive.ExecuteNonQuery();
        }
        using var audit = db.CreateCommand(); audit.Transaction = tx;
        audit.CommandText = "INSERT INTO parcel_audit(at,event,owner,detail) VALUES($at,'HistoryArchived','runtime',$detail); UPDATE parcel_meta SET revision=revision+1 WHERE id=1";
        audit.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O")); audit.Parameters.AddWithValue("$detail", Wire.Encode(new { Ids = ids, Reason = reason, Policy = "Terminal originals retained in same-database immutable archive; no expiry deletion" })); audit.ExecuteNonQuery();
        Boundary?.Invoke("BeforeArchiveCommit"); tx.Commit(); Revision++; CleanupArchivedRawFiles();
    }
    void ArchiveRaw(string operation, Microsoft.Data.Sqlite.SqliteTransaction tx)
    {
        List<(string Id, string Hash)> raw = [];
        using (var c = db.CreateCommand())
        {
            c.Transaction = tx; c.CommandText = "SELECT id,raw_hash FROM parcel_receipts WHERE operation=$op AND state='Committed'"; c.Parameters.AddWithValue("$op", operation);
            using var r = c.ExecuteReader(); while (r.Read()) raw.Add((r.GetString(0), r.GetString(1)));
        }
        foreach (var item in raw)
        {
            var path = Path.Combine(rawRoot, item.Id + ".json"); var bytes = File.ReadAllBytes(path);
            if (Convert.ToHexString(SHA256.HashData(bytes)) != item.Hash) throw new InvalidDataException("Original raw changed before archive");
            using var c = db.CreateCommand(); c.Transaction = tx; c.CommandText = "INSERT INTO parcel_archived_receipts VALUES($id,$body,$hash)";
            c.Parameters.AddWithValue("$id", item.Id); c.Parameters.AddWithValue("$body", bytes); c.Parameters.AddWithValue("$hash", item.Hash); c.ExecuteNonQuery();
        }
    }
    bool HasArchivedRaw(string id, string hash)
    {
        using var c = db.CreateCommand(); c.CommandText = "SELECT body,raw_hash FROM parcel_archived_receipts WHERE id=$id"; c.Parameters.AddWithValue("$id", id);
        using var r = c.ExecuteReader(); return r.Read() && r.GetString(1) == hash && Convert.ToHexString(SHA256.HashData((byte[])r.GetValue(0))) == hash;
    }
    public string? ArchiveCleanupFailure { get; private set; }
    void CleanupArchivedRawFiles()
    {
        ArchiveCleanupFailure = null;
        // DB custody commits before removing the redundant spool file; interrupted cleanup is idempotent.
        foreach (var path in Directory.EnumerateFiles(rawRoot, "*.json"))
        {
            var id = Path.GetFileNameWithoutExtension(path); var hash = Wire.FileHash(path);
            if (HasArchivedRaw(id, hash))
                try { File.Delete(path); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { ArchiveCleanupFailure = "Archived raw custody committed; redundant spool cleanup pending: " + id + ": " + ex.GetType().Name; }
        }
    }

}

public sealed partial class ParcelRuntime
{
    bool ArchiveEligible(ParcelOperation op) => IsSettled(op) && !activeStages.Any(x => x.StartsWith(op.Id + ":", StringComparison.Ordinal)) &&
        !repository!.PendingReceipts().Any(x => x.Operation == op.Id) && op.Upload?.State is not (StageState.Prepared or StageState.Dispatching or StageState.Unknown or StageState.Conflict);
    bool CapacityAvailable => storageCapacityFailure is null && operations.Values.Count(x => !ArchiveEligible(x)) < Effective.MaximumOperations;
    ParcelCapacity Capacity() => new(operations.Values.Count(x => !ArchiveEligible(x)), operations.Count, repository!.ArchivedCount,
        Effective.MaximumOperations, CapacityAvailable, "Terminal originals transactionally archived; all historical identities/receipts/audit retained; no automatic disk deletion", freeStorageBytes, Retention.MinimumFreeBytes);
    void ArchiveSettled(bool requested = false, string? reason = null)
    {
        if (!requested && operations.Count < Math.Min(Retention.ResidentHistory, Effective.MaximumOperations)) return;
        var candidates = operations.Values.Where(ArchiveEligible).OrderBy(x => x.ObservedAt).Take(256).Select(x => x.Id).ToArray();
        if (candidates.Length == 0) return;
        repository!.Archive(candidates, reason ?? "Resident working-set high-water policy");
        foreach (var id in candidates) { admissionPositions.TryRemove(operations[id].AdmissionId, out _); operations.Remove(id); }
    }
    public async Task<ParcelHistoryPage> HistoryAsync(long beforeSequence = long.MaxValue, int limit = 50)
    { await gate.WaitAsync().ConfigureAwait(false); try { return repository!.History(beforeSequence, limit); } finally { gate.Release(); } }
    public async Task<ParcelOperation?> OperationAsync(string id)
    { await gate.WaitAsync().ConfigureAwait(false); try { return repository!.ReadOperation(id); } finally { gate.Release(); } }
}

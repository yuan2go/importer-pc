using Ipc.Application.Contracts;
using Ipc.Platform;
using System.Security.Cryptography;
namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRepository
{
    long lastIntegrationRequestId;
    // Rebuild a scalar from canonical resident/archived T1 facts. It is not a
    // second allocation journal and does not grow memory with historical volume.
    void InitializeIntegrationRequestHistory()
    {
        ValidateIntegrationRequestHistory();
        BackfillIntegrationRequestOwners();
    }
    void ValidateIntegrationRequestHistory()
    {
        var history="SELECT id,json_extract(body,'$.Wcs.Request.IntegrationRequest.RequestId') AS wire FROM parcel_operations" +
            (StorageVersion>=4?" UNION ALL SELECT id,json_extract(body,'$.Wcs.Request.IntegrationRequest.RequestId') AS wire FROM parcel_archives":"");
        using var invalid=db.CreateCommand();
        invalid.CommandText="SELECT wire FROM ("+history+") WHERE wire IS NOT NULL GROUP BY wire HAVING typeof(wire)!='integer' OR wire<=0 OR count(DISTINCT id)>1 LIMIT 1";
        if(invalid.ExecuteScalar() is not null)throw new InvalidDataException("Ambiguous original numeric request ID; preserve original owners and investigate before admission");
        using var maximum=db.CreateCommand();maximum.CommandText="SELECT coalesce(max(wire),0) FROM ("+history+")";
        lastIntegrationRequestId=Convert.ToInt64(maximum.ExecuteScalar());
        using var requests = db.CreateCommand();
        requests.CommandText = "SELECT request FROM (SELECT id,json_extract(body,'$.Wcs.Request.Id') request FROM parcel_operations WHERE json_extract(body,'$.Wcs.Request.IntegrationRequest') IS NOT NULL" +
            (StorageVersion >= 4 ? " UNION ALL SELECT id,json_extract(body,'$.Wcs.Request.Id') request FROM parcel_archives WHERE json_extract(body,'$.Wcs.Request.IntegrationRequest') IS NOT NULL" : "") + ") GROUP BY request HAVING request IS NULL OR count(DISTINCT id)>1 LIMIT 1";
        if (requests.ExecuteScalar() is not null) throw new InvalidDataException("Ambiguous original request owner; preserve original database before numeric ownership migration");
    }
    internal void RequireNewIntegrationRequestId(long id)
    {
        var floor = IntegrationRequestIdFloor();
        if(id<=floor)throw new InvalidDataException($"External numeric request ID {id} must exceed durable original allocation/retired range {floor}");
    }
    void ValidateIntegrationIntentCommit(ParcelOperation operation,Microsoft.Data.Sqlite.SqliteTransaction transaction)
    {
        if(operation.Wcs?.Request.IntegrationRequest is not {} wire)return;
        using var prior=db.CreateCommand();prior.Transaction=transaction;
        prior.CommandText="SELECT json_extract(body,'$.Wcs.Request') FROM parcel_operations WHERE id=$id"+
            (StorageVersion>=4?" UNION ALL SELECT json_extract(body,'$.Wcs.Request') FROM parcel_archives WHERE id=$id":"");
        prior.Parameters.AddWithValue("$id",operation.Id);
        var original=prior.ExecuteScalar();
        if(original is string json)
        {
            if(Wire.Encode(Wire.Decode<FrozenRequest>(json))!=Wire.Encode(operation.Wcs.Request))
                throw new InvalidDataException("Original external intent cannot be replaced by a new allocation");
        }
        else RequireNewIntegrationRequestId(wire.RequestId);
        RegisterIntegrationRequestOwner(operation, transaction);
    }
    public long OperationSequence(string id)
    {
        using var c = db.CreateCommand(); c.CommandText = "SELECT seq FROM parcel_history_index WHERE id=$id";
        c.Parameters.AddWithValue("$id", id);
        return c.ExecuteScalar() is long sequence ? sequence : throw new InvalidDataException("Persist admission before preparing an external request");
    }
    public string? OriginalReplyHash(string request, string reply)
    {
        if (StorageVersion < 4) return LegacyOriginalReplyHash(request, reply);
        using var c = db.CreateCommand(); c.CommandText = "SELECT hash FROM parcel_reply_identities WHERE request=$request AND id=$id";
        c.Parameters.AddWithValue("$request", request); c.Parameters.AddWithValue("$id", reply); return c.ExecuteScalar() as string;
    }
    string? LegacyOriginalReplyHash(string request, string reply)
    {
        // Old readers have no index. Read their existing occurrence authority in
        // durable order; never let the bounded projection redefine an original ID.
        using var c = db.CreateCommand();
        c.CommandText = "SELECT id,raw_hash FROM parcel_receipts WHERE request=$request ORDER BY rowid";
        c.Parameters.AddWithValue("$request", request); using var rows = c.ExecuteReader();
        while (rows.Read())
        {
            var file = Path.Combine(rawRoot, rows.GetString(0) + ".json");
            if (!File.Exists(file)) continue; // Missing Awaiting raw is independently retained.
            var bytes = File.ReadAllBytes(file); var hash = Convert.ToHexString(SHA256.HashData(bytes));
            if (!rows.IsDBNull(1) && rows.GetString(1) != hash) throw new InvalidDataException("Original reply hash differs: " + rows.GetString(0));
            var original = Wire.Decode<StageReply>(System.Text.Encoding.UTF8.GetString(bytes));
            if (original.Id == reply) return Wire.Hash(Wire.Encode(original));
        }
        return null;
    }
    public long ArchivedCount
    { get { using var c = db.CreateCommand(); c.CommandText = "SELECT count(*) FROM parcel_archives"; return (long)c.ExecuteScalar()!; } }
    long archiveRevision;
    internal long ArchiveRevision => Interlocked.Read(ref archiveRevision);
    internal long ReadArchivedCount(CancellationToken token)
    {
        Boundary?.Invoke("ArchiveCountReading");
        using var reader = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        { DataSource = db.DataSource, Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly, Pooling = false, DefaultTimeout = 5 }.ToString());
        token.ThrowIfCancellationRequested(); reader.Open();
        SQLitePCL.raw.sqlite3_progress_handler(reader.Handle, 1000, _ => token.IsCancellationRequested ? 1 : 0, null);
        using var command = reader.CreateCommand(); command.CommandText = "SELECT count(*) FROM parcel_archives";
        var count = (long)command.ExecuteScalar()!; token.ThrowIfCancellationRequested(); return count;
    }
    public bool HasOverloadAcknowledgement(string identity)
    { using var c = db.CreateCommand(); c.CommandText = "SELECT count(*) FROM parcel_overload_ack WHERE identity=$id"; c.Parameters.AddWithValue("$id", identity); return (long)c.ExecuteScalar()! == 1; }
    public ParcelOperation[] FindIdentities(string admission, string observation)
    {
        using var c = db.CreateCommand(); c.CommandText = "SELECT id FROM parcel_history_index WHERE (admission=$admission OR observation=$observation)" +
            (StorageVersion >= 9 ? " AND NOT EXISTS(SELECT 1 FROM parcel_retired_operations t WHERE t.id=parcel_history_index.id)" : "") + " ORDER BY seq LIMIT 2";
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
        using var c = db.CreateCommand(); c.CommandText = StorageVersion >= 9 ? "SELECT h.id FROM parcel_history_index h JOIN parcel_history_projection p ON p.id=h.id WHERE h.position=$position ORDER BY h.seq DESC LIMIT 1" : "SELECT id FROM parcel_history_index WHERE position=$position ORDER BY seq DESC LIMIT 1";
        c.Parameters.AddWithValue("$position", position); var id = c.ExecuteScalar() as string; return id is null ? null : ReadOperation(id);
    }
    public ParcelHistoryPage History(long before, int limit)
    {
        if (before < 1 || limit is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(limit), "History page limit is 1..200; cursor must be positive");
        List<(long Sequence, string Id, bool Archived)> index = [];
        using (var c = db.CreateCommand())
        {
            c.CommandText = "SELECT h.seq,h.id,a.id IS NOT NULL FROM parcel_history_index h LEFT JOIN parcel_archives a ON a.id=h.id WHERE h.seq<$before" +
                (StorageVersion >= 9 ? " AND NOT EXISTS(SELECT 1 FROM parcel_retired_operations t WHERE t.id=h.id)" : "") + " ORDER BY h.seq DESC LIMIT $limit";
            c.Parameters.AddWithValue("$before", before); c.Parameters.AddWithValue("$limit", limit + 1);
            using var r = c.ExecuteReader(); while (r.Read()) index.Add((r.GetInt64(0), r.GetString(1), r.GetBoolean(2)));
        }
        var rows = index.Take(limit).Select(x => new ParcelHistoryItem(x.Sequence, x.Archived, ReadOperation(x.Id) ?? throw new InvalidDataException("History original missing"))).ToArray();
        return new(rows, index.Count > limit ? rows[^1].Sequence : null);
    }
    public void Archive(string[] ids, string reason, BusinessCommand? command = null)
    {
        if (ids.Length == 0)
        { if (command is not null) Commit(null, "HistoryArchiveChecked", reason, command: command); return; }
        using var tx = db.BeginTransaction();
        foreach (var id in ids)
        {
            maintenanceCancellation.ThrowIfCancellationRequested();
            using var read = db.CreateCommand(); read.Transaction = tx; read.CommandText = "SELECT body FROM parcel_operations WHERE id=$id AND NOT EXISTS(SELECT 1 FROM parcel_receipts WHERE operation=$id AND state IN ('Awaiting','RawDurable'))";
            read.Parameters.AddWithValue("$id", id); var body = read.ExecuteScalar() as string ?? throw new InvalidOperationException("Archive candidate changed or has original receipt responsibility");
            ArchiveRaw(id, tx);
            using var archive = db.CreateCommand(); archive.Transaction = tx;
            archive.CommandText = "INSERT INTO parcel_archives SELECT id,admission,observation,body,$hash,$at FROM parcel_operations WHERE id=$id; DELETE FROM parcel_operations WHERE id=$id";
            archive.Parameters.AddWithValue("$id", id); archive.Parameters.AddWithValue("$hash", Wire.Hash(body)); archive.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O")); archive.ExecuteNonQuery();
        }
        using var audit = db.CreateCommand(); audit.Transaction = tx;
        audit.CommandText = "INSERT INTO parcel_audit(at,event,owner,detail) VALUES($at,'HistoryArchived','runtime',$detail); UPDATE parcel_meta SET revision=revision+1 WHERE id=1";
        audit.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O")); audit.Parameters.AddWithValue("$detail", Wire.Encode(new { Ids = ids, Reason = reason, Policy = "Archive retains terminal originals in the same database; expiry deletion requires separate authorized retention maintenance" })); audit.ExecuteNonQuery();
        if (command is not null) WriteCommand(tx, command, new("Persisted", "Application command committed", Revision + 1));
        Boundary?.Invoke("BeforeArchiveCommit"); tx.Commit(); Revision++; Interlocked.Increment(ref archiveRevision); Boundary?.Invoke("AfterArchiveCommit"); CleanupArchivedRawFiles();
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
            maintenanceCancellation.ThrowIfCancellationRequested();
            // Reactivated custody owners can already have immutable raw in the archive.
            // Verify and reuse it; cleanup may legitimately have removed its spool file.
            if (HasArchivedRaw(item.Id, item.Hash)) continue;
            var path = Path.Combine(rawRoot, item.Id + ".json"); var bytes = File.ReadAllBytes(path);
            if (Convert.ToHexString(SHA256.HashData(bytes)) != item.Hash) throw new InvalidDataException("Original raw changed before archive");
            using var c = db.CreateCommand(); c.Transaction = tx; c.CommandText = "INSERT INTO parcel_archived_receipts VALUES($id,$body,$hash)";
            c.Parameters.AddWithValue("$id", item.Id); c.Parameters.AddWithValue("$body", bytes); c.Parameters.AddWithValue("$hash", item.Hash); c.ExecuteNonQuery();
        }
    }
    bool HasArchivedRaw(string id, string hash)
    {
        if (StorageVersion < 4) return false;
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
            var id = Path.GetFileNameWithoutExtension(path); var hash = Wire.FileHash(path, maintenanceCancellation);
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
    int? projectedCapacity;
    int UnresolvedCapacity => projectedCapacity ?? operations.Values.Count(x => !ArchiveEligible(x));
    bool CapacityAvailable => storageCapacityFailure is null && UnresolvedCapacity < Effective.MaximumOperations;
    ParcelCapacity Capacity() => new(UnresolvedCapacity, operations.Count, archiveCount?.Count ?? 0,
        Effective.MaximumOperations, CapacityAvailable, "Archive retains originals; configured expiry requires stopped authorized maintenance and a complete backup; unresolved responsibility and permanent identity/numeric fences remain", freeStorageBytes, Retention.MinimumFreeBytes);
    void ArchiveSettled(bool requested = false, string? reason = null, BusinessCommand? command = null)
    {
        if (!requested && operations.Count < Math.Min(Retention.ResidentHistory, Effective.MaximumOperations)) return;
        var candidates = operations.Values.Where(ArchiveEligible).OrderBy(x => x.ObservedAt).Take(256).Select(x => x.Id).ToArray();
        if (candidates.Length == 0 && command is null) return;
        repository!.Archive(candidates, reason ?? "Resident working-set high-water policy", command);
        if (archiveCount is { } count && count.Revision + 1 == repository.ArchiveRevision)
            Volatile.Write(ref archiveCount, new(repository.ArchiveRevision, count.Count + candidates.Length));
        foreach (var id in candidates) { admissionPositions.TryRemove(operations[id].AdmissionId, out _); operations.Remove(id); }
    }
    public async Task<ParcelHistoryPage> HistoryAsync(long beforeSequence = long.MaxValue, int limit = 50)
    { await gate.WaitAsync().ConfigureAwait(false); try { return OperatorHistory(repository!.History(beforeSequence, limit)); } finally { gate.Release(); } }
    public async Task<ParcelOperation?> OperationAsync(string id)
    { await gate.WaitAsync().ConfigureAwait(false); try { var op = repository!.ReadOperation(id); return op is null || Management.Status().Authorized ? op : OperatorOperation(op); } finally { gate.Release(); } }
}

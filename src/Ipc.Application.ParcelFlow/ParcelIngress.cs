using System.Collections.Concurrent;
using Ipc.Application.Contracts;
using Ipc.Platform;
using Ipc.Platform.Storage;
using Microsoft.Data.Sqlite;

namespace Ipc.Application.ParcelFlow;

public sealed record ParcelWorkerProgress(string Position, string State, long Received, long Processed, long Interrupted,
    int QueueDepth, DateTimeOffset? OldestWaiting, DateTimeOffset? LastProgress, string? Failure, DateTimeOffset? ProcessingSince);

public sealed partial class ParcelRuntime
{
    sealed record IngressItem(string Id, object Value, DateTimeOffset At, long ReceivedTicks);
    sealed class IngressLane(string database) : IDisposable
    {
        public readonly object Sync = new();
        public readonly SqliteConnection Db = Database.Open(database, create: false);
        public readonly ConcurrentDictionary<string, DateTimeOffset> Waiting = new();
        public string State = "Starting";
        public string? Failure;
        public long Received, Processed, Interrupted, ProgressTicks, ProcessingTicks;
        public void Dispose() => Db.Dispose();
    }
    readonly Dictionary<string, IngressLane> ingressLanes = [];
    readonly Dictionary<string, Task> positionWorkers = [];
    readonly ConcurrentDictionary<string, byte> interruptedPositions = new();
    readonly AsyncLocal<string?> activeIngress = new();
    public ParcelWorkerProgress[] WorkerProgress() => ingressLanes.Select(pair =>
    {
        var lane = pair.Value;
        var waiting = lane.Waiting.Values.ToArray(); var ticks = Interlocked.Read(ref lane.ProgressTicks);
        return new ParcelWorkerProgress(pair.Key, Volatile.Read(ref lane.State), Interlocked.Read(ref lane.Received),
            Interlocked.Read(ref lane.Processed), Interlocked.Read(ref lane.Interrupted), waiting.Length,
            waiting.Length == 0 ? null : waiting.Min(), ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero), Volatile.Read(ref lane.Failure),
            Interlocked.Read(ref lane.ProcessingTicks) is var active && active != 0 ? new DateTimeOffset(active, TimeSpan.Zero) : null);
    }).ToArray();
    void InitializeIngress()
    {
        foreach (var position in positions.Keys) ingressLanes.Add(position, new(Path.Combine(directory!, Id + ".db")));
    }
    static void WriteIngress(SqliteConnection db, string id, string position, object item, DateTimeOffset at, string session, Action? beforeBegin = null)
    {
        var body = Wire.Encode(item); var hash = Wire.Hash(body);
        AtomicStorageWrite.Execute(db, tx =>
        {
        using var c = db.CreateCommand(); c.Transaction = tx;
        c.CommandText = "INSERT INTO parcel_ingress(id,position,received_at,state,body,body_hash,session) VALUES($id,$position,$at,'Pending',$body,$hash,$session)";
        c.Parameters.AddWithValue("$id", id); c.Parameters.AddWithValue("$position", position); c.Parameters.AddWithValue("$at", at.ToString("O"));
        c.Parameters.AddWithValue("$body", body); c.Parameters.AddWithValue("$hash", hash); c.Parameters.AddWithValue("$session", session); c.ExecuteNonQuery();
        }, beforeBegin);
    }
    static void FinishIngress(SqliteConnection db, string id, string state, string detail)
    {
        using var c = db.CreateCommand();
        c.CommandText = "UPDATE parcel_ingress SET state=$state,detail=$detail,completed_at=$at WHERE id=$id AND state='Pending'";
        c.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
        c.Parameters.AddWithValue("$id", id); c.Parameters.AddWithValue("$state", state); c.Parameters.AddWithValue("$detail", detail);
        if (c.ExecuteNonQuery() != 1) throw new InvalidDataException("Original ingress responsibility changed");
    }
    void FinishIngress(string id, string state, string detail, double? processingMs = null) => repository!.FinishIngress(id, state, detail, processingMs);
    void ReleaseIngressItem(object value)
    {
        if (value is not ParcelObservation o) return;
        try { ReleaseIngressAcquisition(o.Observation); }
        finally { Interlocked.Decrement(ref pendingObservations); }
    }
    BusinessReceipt InspectInterruptedIngress(BusinessCommand command)
    {
        if (!CanConfigure || command.Owner is null || string.IsNullOrWhiteSpace(command.Value) || command.Value.Length > 1024 ||
            ingressLanes.Values.Any(l => !l.Waiting.IsEmpty) || externalWaits != 0)
            return new("Rejected", "Pause, drain and enter management mode; inspect original input and entity", repository!.Revision);
        var position = repository!.IngressPosition(command.Owner);
        if (position is null) return new("Rejected", "No interrupted input with this identity", repository.Revision);
        repository.Commit(null, "InterruptedIngressInspected", Wire.Encode(new { Input = command.Owner, Reason = command.Value, Actor = ManagementEvidence,
            Policy = "Receipt inspection only; original operation/request/raw/Unknown retained; never replay input" }), command: command, inspectedIngress: command.Owner);
        if (!repository.HasInterruptedIngress(position)) interruptedPositions.TryRemove(position, out _);
        return repository.PriorCommand(command)!;
    }
}

public sealed partial class ParcelRepository
{
    internal void BeginIngress(string id, double queueMs)
    {
        using var c = db.CreateCommand(); c.CommandText = "UPDATE parcel_ingress SET started_at=$at,queue_ms=$queue WHERE id=$id AND state='Pending'";
        c.Parameters.AddWithValue("$id", id); c.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O")); c.Parameters.AddWithValue("$queue", queueMs);
        if (c.ExecuteNonQuery() != 1) throw new InvalidDataException("Original ingress responsibility changed before processing");
    }
    internal void FinishIngress(string id, string state, string detail, double? processingMs)
    {
        using var c = db.CreateCommand();
        c.CommandText = "UPDATE parcel_ingress SET state=$state,detail=$detail,completed_at=$at,processing_ms=$ms WHERE id=$id AND state='Pending'";
        c.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O")); c.Parameters.AddWithValue("$ms", (object?)processingMs ?? DBNull.Value);
        c.Parameters.AddWithValue("$id", id); c.Parameters.AddWithValue("$state", state); c.Parameters.AddWithValue("$detail", detail);
        if (c.ExecuteNonQuery() != 1) throw new InvalidDataException("Original ingress responsibility changed");
    }
    internal string[] RecoverIngress()
    {
        using var c = db.CreateCommand();
        c.CommandText = "SELECT body,body_hash FROM parcel_ingress WHERE state IN ('Pending','Interrupted')";
        using (var originals = c.ExecuteReader())
            while (originals.Read()) if (Wire.Hash(originals.GetString(0)) != originals.GetString(1))
                throw new InvalidDataException("Unsettled ingress original checksum differs; preserve original database and restore its complete custody set");
        c.CommandText = "UPDATE parcel_ingress SET state='Interrupted',detail='Previous owner ended before receipt disposition; inspect original entity and business facts; no replay' WHERE state='Pending'";
        c.ExecuteNonQuery();
        c.CommandText = "SELECT DISTINCT position FROM parcel_ingress WHERE state='Interrupted'";
        using var r = c.ExecuteReader(); List<string> positions = []; while (r.Read()) positions.Add(r.GetString(0));
        r.Close(); if (positions.Count != 0) Commit(null, "IngressRecoveryRequired", "Interrupted inputs retain original responsibility; no automatic replay", paused: true);
        return positions.ToArray();
    }
    internal bool HasInterruptedIngress(string position)
    {
        using var c = db.CreateCommand(); c.CommandText = "SELECT EXISTS(SELECT 1 FROM parcel_ingress WHERE state IN ('Pending','Interrupted') AND position=$p)";
        c.Parameters.AddWithValue("$p", position); return (long)c.ExecuteScalar()! != 0;
    }
    internal string? IngressPosition(string id)
    {
        using var c = db.CreateCommand(); c.CommandText = "SELECT position FROM parcel_ingress WHERE id=$id AND state='Interrupted'"; c.Parameters.AddWithValue("$id", id);
        return c.ExecuteScalar() as string;
    }
    public object[] IngressResponsibilities(int limit = 200)
    {
        if (limit is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(limit));
        using var c = db.CreateCommand(); c.CommandText = "SELECT id,position,received_at,state,body,body_hash,detail FROM parcel_ingress WHERE state IN ('Pending','Interrupted') ORDER BY received_at,id LIMIT $limit";
        c.Parameters.AddWithValue("$limit", limit); using var r = c.ExecuteReader(); List<object> rows = []; int characters = 0;
        while (r.Read())
        {
            var body = r.GetString(4); if (Wire.Hash(body) != r.GetString(5)) throw new InvalidDataException("Ingress original checksum differs");
            var preview = body.Length <= 65536 ? body : body[..65536];
            if (characters + preview.Length > 524288 && rows.Count != 0) break;
            characters += preview.Length;
            rows.Add(new { Id = r.GetString(0), Position = r.GetString(1), At = r.GetString(2), State = r.GetString(3), Body = preview,
                OriginalSha256 = r.GetString(5), OriginalCharacters = body.Length, PreviewTruncated = preview.Length != body.Length,
                Detail = r.IsDBNull(6) ? null : r.GetString(6) });
        }
        return rows.ToArray();
    }
}

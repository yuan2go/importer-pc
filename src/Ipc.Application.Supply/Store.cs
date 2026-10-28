using Ipc.Platform;
using Microsoft.Data.Sqlite;

namespace Ipc.Supply;

// Single writer is owned by StationApplication. Reads also use that coordinator, never UI connections.
public sealed partial class SupplyStore : IDisposable
{
    readonly SqliteConnection db;
    SqliteTransaction? current;
    public string Path { get; }
    public Action<object>? Journal { get; set; }
    public const int CurrentFormatVersion = 6;
    const string Schema = """
        CREATE TABLE acceptance(request TEXT PRIMARY KEY REFERENCES requests(id),evidence TEXT NOT NULL UNIQUE,operation TEXT NOT NULL REFERENCES operations(id),quantity INTEGER NOT NULL CHECK(quantity>0));
        CREATE TABLE admissions(id TEXT PRIMARY KEY,operation TEXT NOT NULL REFERENCES operations(id));
        CREATE TABLE alarms(id TEXT PRIMARY KEY,code TEXT NOT NULL,scope TEXT NOT NULL,active INTEGER NOT NULL,json TEXT NOT NULL);
        CREATE TABLE attempts(id TEXT PRIMARY KEY,request TEXT NOT NULL REFERENCES requests(id),ordinal INTEGER NOT NULL,json TEXT NOT NULL,UNIQUE(request,ordinal));
        CREATE TABLE audit(id TEXT PRIMARY KEY,owner TEXT NOT NULL,at TEXT NOT NULL,json TEXT NOT NULL);
        CREATE TABLE commands(id TEXT PRIMARY KEY,hash TEXT NOT NULL,receipt TEXT NOT NULL);
        CREATE TABLE configs(id TEXT PRIMARY KEY,status TEXT NOT NULL,json TEXT NOT NULL);
        CREATE TABLE observations(id TEXT PRIMARY KEY,device TEXT NOT NULL,epoch TEXT NOT NULL,source TEXT NOT NULL,operation TEXT REFERENCES operations(id),json TEXT NOT NULL,UNIQUE(device,epoch,source));
        CREATE TABLE operations(id TEXT PRIMARY KEY,session TEXT NOT NULL REFERENCES sessions(id),lane TEXT NOT NULL,created TEXT NOT NULL,json TEXT NOT NULL);
        CREATE TABLE outbox(request TEXT PRIMARY KEY REFERENCES requests(id),state TEXT NOT NULL,claimed TEXT);
        CREATE TABLE receipt_occurrences(id TEXT PRIMARY KEY,request TEXT NOT NULL REFERENCES requests(id),status TEXT NOT NULL,json TEXT NOT NULL);
        CREATE TABLE requests(id TEXT PRIMARY KEY,owner TEXT NOT NULL,session TEXT NOT NULL REFERENCES sessions(id),kind TEXT NOT NULL,intent TEXT NOT NULL UNIQUE,status TEXT NOT NULL,json TEXT NOT NULL);
        CREATE TABLE response_conflicts(id TEXT PRIMARY KEY,request TEXT NOT NULL REFERENCES requests(id),json TEXT NOT NULL);
        CREATE TABLE responses(id TEXT PRIMARY KEY,request TEXT NOT NULL REFERENCES requests(id),json TEXT NOT NULL);
        CREATE TABLE sessions(id TEXT PRIMARY KEY,json TEXT NOT NULL);
        CREATE TABLE state(id INTEGER PRIMARY KEY CHECK(id=1), json TEXT NOT NULL);
        CREATE TABLE storage_format(id INTEGER PRIMARY KEY CHECK(id=1),version INTEGER NOT NULL,minimum_reader INTEGER NOT NULL);
        CREATE INDEX alarms_active ON alarms(active,scope);
        CREATE INDEX audit_owner ON audit(owner,at);
        CREATE INDEX operations_history ON operations(session,lane,created);
        CREATE INDEX outbox_state ON outbox(state);
        CREATE INDEX receipt_occurrences_request ON receipt_occurrences(request,status);
        CREATE INDEX requests_owner ON requests(owner);
        CREATE INDEX requests_status ON requests(status);
        CREATE INDEX response_conflicts_request ON response_conflicts(request);
        INSERT INTO storage_format VALUES(1,6,6);
        """;
    public SupplyStore(string path, IRequestContract? recoveryContract = null)
    {
        Path = System.IO.Path.GetFullPath(path);
        // Validate the complete receipt set before any writer, journal change or new database.
        var existing = ValidateBeforeWrite(Path, recoveryContract);
        if (!existing) Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        db = Ipc.Platform.Storage.Database.Open(Path, create: !existing);
        try
        {
            if (!existing)
            {
                using var tx = db.BeginTransaction();
                Exec("CREATE TABLE schema_history(id INTEGER PRIMARY KEY,checksum TEXT NOT NULL,applied TEXT NOT NULL);", tx);
                Exec(Schema, tx);
                Exec("INSERT INTO schema_history VALUES(6,$a,$b)", tx, Ipc.Platform.Storage.Database.BaselineHash(Schema), DateTimeOffset.UtcNow.ToString("O"));
                tx.Commit();
            }
            _ = ReadFormat(db);
            Exec("PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; PRAGMA busy_timeout=5000; PRAGMA wal_autocheckpoint=1000;");
        }
        catch { db.Dispose(); throw; }
    }
    SqliteCommand Sql(string sql, SqliteTransaction? tx, params object?[] values)
    {
        var c = db.CreateCommand(); c.CommandText = sql; c.Transaction = tx ?? current;
        for (int i = 0; i < values.Length; i++) c.Parameters.AddWithValue("$" + (char)('a' + i), values[i] ?? DBNull.Value);
        return c;
    }
    internal void Exec(string sql, SqliteTransaction? tx = null, params object?[] values) { using var c = Sql(sql, tx, values); c.ExecuteNonQuery(); }
    string? Scalar(string sql, params object?[] values) { using var c = Sql(sql, null, values); return c.ExecuteScalar()?.ToString(); }
    T[] Read<T>(string sql, params object?[] values)
    {
        using var c = Sql(sql, null, values); using var r = c.ExecuteReader(); var list = new List<T>();
        while (r.Read()) list.Add(Wire.Decode<T>(r.GetString(0))); return list.ToArray();
    }
    public StationState Load() => Read<StationState>("SELECT json FROM state WHERE id=1").SingleOrDefault() ?? new();
    public StationSession? Session(string id) => Read<StationSession>("SELECT json FROM sessions WHERE id=$a", id).SingleOrDefault();
    public StationSession[] Sessions(int offset = 0, int limit = 100) => Read<StationSession>("SELECT json FROM sessions ORDER BY rowid DESC LIMIT $a OFFSET $b", Math.Clamp(limit, 1, 200), Math.Max(0, offset));
    public BusinessRequest[] Requests(string? owner = null) => Read<BusinessRequest>(owner is null ? "SELECT json FROM requests ORDER BY rowid" : "SELECT json FROM requests WHERE owner=$a ORDER BY rowid", owner is null ? [] : [owner]);
    public BusinessRequest[] PendingRequests() => Read<BusinessRequest>("SELECT json FROM requests WHERE status IN ('Prepared','Dispatching','Unknown') OR id IN (SELECT request FROM response_conflicts) OR id IN (SELECT request FROM receipt_occurrences WHERE status NOT IN ('Committed','NoResponse')) OR id IN (SELECT json_extract(b.value,'$.Source') FROM state,json_each(state.json,'$.Blocks') b WHERE json_extract(b.value,'$.Code')='ReceiptPersistenceUncertain') ORDER BY rowid");
    public BusinessRequest[] RequestsForSession(string session) => Read<BusinessRequest>("SELECT json FROM requests WHERE session=$a ORDER BY rowid", session);
    public BusinessRequest? Request(string id) => Read<BusinessRequest>("SELECT json FROM requests WHERE id=$a", id).SingleOrDefault();
    public SupplyOperation[] History(string? filter, int offset, int limit) => Read<SupplyOperation>("SELECT json FROM operations WHERE $a IS NULL OR json_extract(json,'$.Barcode')=$a OR json_extract(json,'$.BoxBarcode')=$a OR json_extract(json,'$.Task.Sku')=$a OR json_extract(json,'$.Task.TaskId')=$a ORDER BY created DESC,id LIMIT $b OFFSET $c", filter, Math.Clamp(limit, 1, 200), Math.Max(0, offset));
    public SendAttempt[] Attempts(string id) => Read<SendAttempt>("SELECT json FROM attempts WHERE request=$a ORDER BY ordinal", id);
    public IntegrationResponse? ResponseByEvidence(string id) => Read<IntegrationResponse>("SELECT json FROM responses WHERE id=$a", id).SingleOrDefault();
    public IntegrationResponse[] Responses(string id) => Read<IntegrationResponse>("SELECT json FROM responses WHERE request=$a ORDER BY rowid", id);
    public Audit[] Audits(string id) => Read<Audit>("SELECT json FROM audit WHERE owner=$a ORDER BY rowid", id);
    public Alarm[] ActiveAlarms() => Read<Alarm>("SELECT json FROM alarms WHERE active=1 ORDER BY rowid DESC");
    public int ActiveAlarmCount() => int.Parse(Scalar("SELECT count(*) FROM alarms WHERE active=1")!);
    public Alarm? Alarm(string id) => Read<Alarm>("SELECT json FROM alarms WHERE id=$a", id).SingleOrDefault();
    public Alarm[] AlarmHistory(int offset, int limit) => Read<Alarm>("SELECT json FROM alarms WHERE active=0 ORDER BY rowid DESC LIMIT $a OFFSET $b", Math.Clamp(limit, 1, 200), Math.Max(0, offset));
    public ConfigRevision[] Configs() => Read<ConfigRevision>("SELECT json FROM configs ORDER BY rowid DESC LIMIT 100");
    // Startup authority must not depend on the UI's bounded recent-revision list.
    public ConfigRevision? CurrentConfig(string status) => Read<ConfigRevision>("""
        SELECT c.json FROM configs c WHERE c.status=$a
        ORDER BY coalesce((SELECT max(a.rowid) FROM audit a WHERE a.owner=c.id
          AND json_extract(a.json,'$.Kind')='ConfigActivation'),0) DESC,c.rowid DESC LIMIT 1
        """, status).SingleOrDefault();
    public bool ConfigVersionExists(string version, string? exceptId = null) => int.Parse(Scalar(
        "SELECT count(*) FROM configs WHERE json_extract(json,'$.Content.Version')=$a AND ($b IS NULL OR id<>$b)", version, exceptId)!) != 0;
    public CommandReceipt? Receipt(Command command)
    {
        using var c = Sql("SELECT hash,receipt FROM commands WHERE id=$a", null, command.Id); using var r = c.ExecuteReader();
        if (!r.Read()) return null;
        if (r.GetString(0) != Wire.Hash(Wire.Encode(command))) return new("Conflict", "Command identity reused with different content", Load().Revision);
        return Wire.Decode<CommandReceipt>(r.GetString(1));
    }
    public bool HasObservation(Observation x) => Scalar("SELECT id FROM observations WHERE device=$a AND epoch=$b AND source=$c", x.DeviceId, x.Epoch, x.SourceEventId) is not null;
    public bool HasAdmission(string id) => Scalar("SELECT id FROM admissions WHERE id=$a", id) is not null;
    public int AcceptedQuantity(string session) => int.Parse(Scalar("SELECT coalesce(sum(a.quantity),0) FROM acceptance a JOIN requests r ON r.id=a.request WHERE r.session=$a", session) ?? "0");
    public int PendingCount() => PendingRequests().Length;
    public ResponseConflict[] Conflicts(string request) => Read<ResponseConflict>("SELECT json FROM response_conflicts WHERE request=$a ORDER BY rowid", request);
    public bool RecoveryRequired(string request) => HasConflict(request) || Receipts(request).Any(ReceiptResponsibilityRules.IsUnresolved) || Load().Blocks.Any(x => x.Code == "ReceiptPersistenceUncertain" && x.Source == request);
    public bool HasConflict(string request) => Scalar("SELECT id FROM response_conflicts WHERE request=$a LIMIT 1", request) is not null;
    public ReceiptResponsibility[] Receipts(string request) => Read<ReceiptResponsibility>("SELECT json FROM receipt_occurrences WHERE request=$a ORDER BY rowid", request);
    public ReceiptResponsibility[] IncompleteReceipts() => Read<ReceiptResponsibility>("SELECT json FROM receipt_occurrences WHERE " + ReceiptResponsibilityRules.UnresolvedSql("status") + " ORDER BY rowid");
    public ReceiptResponsibility? ReceiptOccurrence(string id) => Read<ReceiptResponsibility>("SELECT json FROM receipt_occurrences WHERE id=$a", id).SingleOrDefault();
    public void Commit(StationState state, string kind, string owner, string actor, string evidence = "", Command? command = null, CommandReceipt? receipt = null,
        BusinessRequest? request = null, Observation? observation = null, SupplyOperation? operation = null,
        SendAttempt? attempt = null, IntegrationResponse? response = null, bool count = false, ConfigRevision? config = null, Alarm? alarm = null, ResponseConflict? conflict = null, ReceiptResponsibility? occurrence = null)
    {
        using var tx = db.BeginTransaction();
        current = tx;
        try
        {
        Exec("INSERT INTO state VALUES(1,$a) ON CONFLICT(id) DO UPDATE SET json=excluded.json", tx, Wire.Encode(state));
        if (state.Session is not null) Exec("INSERT INTO sessions VALUES($a,$b) ON CONFLICT(id) DO UPDATE SET json=excluded.json", tx, state.Session.Id, Wire.Encode(state.Session));
        foreach (var op in state.Lanes.Values.Concat(operation is null ? [] : new[] { operation }).DistinctBy(x => x.Id))
            Exec("INSERT INTO operations VALUES($a,$b,$c,$d,$e) ON CONFLICT(id) DO UPDATE SET json=excluded.json", tx, op.Id, op.SessionId, op.Lane, op.CreatedAt.ToString("O"), Wire.Encode(op));
        if (observation is not null)
        {
            Exec("INSERT INTO observations VALUES($a,$b,$c,$d,$e,$f)", tx, observation.Id, observation.DeviceId, observation.Epoch, observation.SourceEventId, operation?.Id, Wire.Encode(observation));
            if (observation.Kind == "Enter" && operation is not null) Exec("INSERT INTO admissions VALUES($a,$b)", tx, observation.AdmissionId, operation.Id);
        }
        if (request is not null)
        {
            // Identity fields never change. Every update verifies the immutable original.
            var original = Request(request.Id);
            if (original is not null && original with { Status = request.Status } != request) throw new InvalidDataException("Immutable request changed");
            Exec("INSERT INTO requests VALUES($a,$b,$c,$d,$e,$f,$g) ON CONFLICT(id) DO UPDATE SET status=excluded.status,json=excluded.json", tx,
                request.Id, request.OwnerId, request.SessionId, request.Kind.ToString(), request.IntentKey, request.Status.ToString(), Wire.Encode(request));
            Exec("INSERT INTO outbox VALUES($a,$b,$c) ON CONFLICT(request) DO UPDATE SET state=excluded.state,claimed=excluded.claimed", tx, request.Id, request.Status.ToString(), request.Status == RequestStatus.Dispatching ? Environment.ProcessId.ToString() : null);
        }
        if (attempt is not null) Exec("INSERT INTO attempts VALUES($a,$b,$c,$d) ON CONFLICT(id) DO UPDATE SET json=excluded.json", tx, attempt.Id, attempt.RequestId, attempt.Ordinal, Wire.Encode(attempt));
        if (response is not null)
        {
            var old = Read<IntegrationResponse>("SELECT json FROM responses WHERE id=$a", response.EvidenceId).SingleOrDefault();
            if (old is not null && old != response) throw new InvalidDataException("Conflicting evidence identity");
            Exec("INSERT OR IGNORE INTO responses VALUES($a,$b,$c)", tx, response.EvidenceId, response.RequestId, Wire.Encode(response));
            if (count) Exec("INSERT OR IGNORE INTO acceptance VALUES($a,$b,$c,$d)", tx, response.RequestId, response.EvidenceId, request!.OwnerId, response.Quantity);
        }
        if (conflict is not null)
            Exec("INSERT OR IGNORE INTO response_conflicts VALUES($a,$b,$c)", tx, conflict.Id, conflict.RequestId, Wire.Encode(conflict));
        if (occurrence is not null)
        {
            var oldOccurrence = ReceiptOccurrence(occurrence.Id);
            if (oldOccurrence is not null && (oldOccurrence.RequestId != occurrence.RequestId || oldOccurrence.AttemptId != occurrence.AttemptId ||
                oldOccurrence.Origin != occurrence.Origin || oldOccurrence.StartedAt != occurrence.StartedAt || ReceiptResponsibilityRules.IsFinal(oldOccurrence.Status) && oldOccurrence != occurrence))
                throw new InvalidDataException("Immutable receipt occurrence changed");
            Exec("INSERT INTO receipt_occurrences VALUES($a,$b,$c,$d) ON CONFLICT(id) DO UPDATE SET status=excluded.status,json=excluded.json", tx,
                occurrence.Id, occurrence.RequestId, occurrence.Status, Wire.Encode(occurrence));
        }
        if (config is not null)
        {
            // Exactly one current Effective after promotion. Prior activation facts and
            // content remain in audit/config history; the prior revision is now superseded.
            if (config.Status == "Effective") Exec("UPDATE configs SET status='Superseded',json=json_set(json,'$.Status','Superseded') WHERE status='Effective' AND id<>$a", tx, config.Id);
            Exec("INSERT INTO configs VALUES($a,$b,$c) ON CONFLICT(id) DO UPDATE SET status=excluded.status,json=excluded.json", tx, config.Id, config.Status, Wire.Encode(config));
        }
        if (alarm is not null) SaveAlarm(alarm);
        if (command is not null) Exec("INSERT INTO commands VALUES($a,$b,$c)", tx, command.Id, Wire.Hash(Wire.Encode(command)), Wire.Encode(receipt));
        var a = new Audit(Wire.Id(), owner, actor, command?.Id ?? "system", kind, evidence, DateTimeOffset.UtcNow);
        Exec("INSERT INTO audit VALUES($a,$b,$c,$d)", tx, a.Id, owner, a.At.ToString("O"), Wire.Encode(a));
        tx.Commit();
        Journal?.Invoke(new { At = a.At, Event = kind, SessionId = state.Session?.Id, OwnerId = owner, RequestId = request?.Id, AttemptId = attempt?.Id, CommandId = command?.Id, Revision = state.Revision });
        }
        finally { current = null; }
    }
    public void SaveAlarm(Alarm alarm) => Exec("INSERT INTO alarms VALUES($a,$b,$c,$d,$e) ON CONFLICT(id) DO UPDATE SET active=excluded.active,json=excluded.json", null,
        alarm.Id, alarm.Code, alarm.Scope, alarm.ClearedAt is null ? 1 : 0, Wire.Encode(alarm));
    public static void RestoreVerified(string source, string destination)
    {
        _ = InspectReadOnly(source);
        if (File.Exists(destination) || File.Exists(destination + "-wal") || File.Exists(destination + "-shm") ||
            System.IO.Path.GetFullPath(source) == System.IO.Path.GetFullPath(destination))
            throw new IOException("Restore requires a new destination; original retained");
        // Persist isolation BEFORE exposing restored bytes. Source/backup bytes are never
        // changed: an explicitly requested current-format copy remains quarantined.
        var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(destination))!; Directory.CreateDirectory(directory);
        using (var marker = new FileStream(System.IO.Path.Combine(directory, "restore-quarantine"), FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            marker.Write(System.Text.Encoding.UTF8.GetBytes("Restored snapshot may lag external effects; reconcile before admission")); marker.Flush(true);
        }
        BackupReadOnly(source, destination);
        using var restored = new SupplyStore(destination); var state = restored.Load(); state.Quarantined = true; state.Revision++;
        restored.Commit(state, "RestoreIsolation", state.Session?.Id ?? "station", "offline-restore", "External ledger/inventory reconciliation required; source backup retained in original format");
    }
    public Action<string>? RegisterRecoveryCopy { get; set; }
    public void Backup(string destination)
    { RegisterRecoveryCopy?.Invoke(destination); BackupSnapshot(Path, destination, true); }
    public int PruneClosedSessions(DateTimeOffset before, string actor, string evidence)
    {
        var eligible = Read<StationSession>("SELECT json FROM sessions").Where(x => x.Status == SessionStatus.Closed && x.ClosedAt < before)
            .Where(x => !PendingRequests().Any(r => r.SessionId == x.Id)).ToArray();
        using var tx = db.BeginTransaction();
        foreach (var session in eligible)
        {
            // Delete only an entire closed aggregate; retained requests/configs/evidence cannot
            // lose their dependencies. Command receipts and the retention audit are tombstones.
            Exec("DELETE FROM receipt_occurrences WHERE request IN (SELECT id FROM requests WHERE session=$a)", tx, session.Id);
            Exec("DELETE FROM response_conflicts WHERE request IN (SELECT id FROM requests WHERE session=$a)", tx, session.Id);
            Exec("DELETE FROM acceptance WHERE request IN (SELECT id FROM requests WHERE session=$a)", tx, session.Id);
            Exec("DELETE FROM responses WHERE request IN (SELECT id FROM requests WHERE session=$a)", tx, session.Id);
            Exec("DELETE FROM attempts WHERE request IN (SELECT id FROM requests WHERE session=$a)", tx, session.Id);
            Exec("DELETE FROM outbox WHERE request IN (SELECT id FROM requests WHERE session=$a)", tx, session.Id);
            Exec("DELETE FROM requests WHERE session=$a", tx, session.Id);
            Exec("DELETE FROM observations WHERE operation IN (SELECT id FROM operations WHERE session=$a)", tx, session.Id);
            Exec("DELETE FROM admissions WHERE operation IN (SELECT id FROM operations WHERE session=$a)", tx, session.Id);
            Exec("DELETE FROM audit WHERE owner=$a OR owner IN (SELECT id FROM operations WHERE session=$a)", tx, session.Id);
            Exec("DELETE FROM alarms WHERE active=0 AND (json_extract(json,'$.OwnerId')=$a OR json_extract(json,'$.OwnerId') IN (SELECT id FROM operations WHERE session=$a))", tx, session.Id);
            Exec("DELETE FROM operations WHERE session=$a", tx, session.Id);
            Exec("DELETE FROM sessions WHERE id=$a", tx, session.Id);
        }
        var audit = new Audit(Wire.Id(), "retention", actor, "maintenance", "PruneClosedSessions", $"before={before:O}; sessions={string.Join(',', eligible.Select(x => x.Id))}; {evidence}", DateTimeOffset.UtcNow);
        Exec("INSERT INTO audit VALUES($a,$b,$c,$d)", tx, audit.Id, audit.OwnerId, audit.At.ToString("O"), Wire.Encode(audit)); tx.Commit(); return eligible.Length;
    }
    public void Dispose() => db.Dispose();
}

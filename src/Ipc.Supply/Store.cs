using Ipc.Platform;
using Microsoft.Data.Sqlite;

namespace Ipc.Supply;

// Single writer is owned by StationApplication. Reads also use that coordinator, never UI connections.
public sealed class SupplyStore : IDisposable
{
    readonly SqliteConnection db;
    SqliteTransaction? current;
    public string Path { get; }
    public Action<object>? Journal { get; set; }
    const string Schema = """
        CREATE TABLE state(id INTEGER PRIMARY KEY CHECK(id=1), json TEXT NOT NULL);
        CREATE TABLE sessions(id TEXT PRIMARY KEY,json TEXT NOT NULL);
        CREATE TABLE operations(id TEXT PRIMARY KEY,session TEXT NOT NULL REFERENCES sessions(id),lane TEXT NOT NULL,created TEXT NOT NULL,json TEXT NOT NULL);
        CREATE INDEX operations_history ON operations(session,lane,created);
        CREATE TABLE observations(id TEXT PRIMARY KEY,device TEXT NOT NULL,epoch TEXT NOT NULL,source TEXT NOT NULL,operation TEXT REFERENCES operations(id),json TEXT NOT NULL,UNIQUE(device,epoch,source));
        CREATE TABLE admissions(id TEXT PRIMARY KEY,operation TEXT NOT NULL REFERENCES operations(id));
        CREATE TABLE requests(id TEXT PRIMARY KEY,owner TEXT NOT NULL,session TEXT NOT NULL REFERENCES sessions(id),kind TEXT NOT NULL,intent TEXT NOT NULL UNIQUE,status TEXT NOT NULL,json TEXT NOT NULL);
        CREATE TABLE outbox(request TEXT PRIMARY KEY REFERENCES requests(id),state TEXT NOT NULL,claimed TEXT);
        CREATE INDEX outbox_state ON outbox(state);
        CREATE INDEX requests_owner ON requests(owner);
        CREATE INDEX requests_status ON requests(status);
        CREATE TABLE attempts(id TEXT PRIMARY KEY,request TEXT NOT NULL REFERENCES requests(id),ordinal INTEGER NOT NULL,json TEXT NOT NULL,UNIQUE(request,ordinal));
        CREATE TABLE responses(id TEXT PRIMARY KEY,request TEXT NOT NULL REFERENCES requests(id),json TEXT NOT NULL);
        CREATE TABLE acceptance(request TEXT PRIMARY KEY REFERENCES requests(id),evidence TEXT NOT NULL UNIQUE,operation TEXT NOT NULL REFERENCES operations(id),quantity INTEGER NOT NULL CHECK(quantity>0));
        CREATE TABLE alarms(id TEXT PRIMARY KEY,code TEXT NOT NULL,scope TEXT NOT NULL,active INTEGER NOT NULL,json TEXT NOT NULL);
        CREATE INDEX alarms_active ON alarms(active,scope);
        CREATE TABLE audit(id TEXT PRIMARY KEY,owner TEXT NOT NULL,at TEXT NOT NULL,json TEXT NOT NULL);
        CREATE INDEX audit_owner ON audit(owner,at);
        CREATE TABLE commands(id TEXT PRIMARY KEY,hash TEXT NOT NULL,receipt TEXT NOT NULL);
        CREATE TABLE configs(id TEXT PRIMARY KEY,status TEXT NOT NULL,json TEXT NOT NULL);
        """;
    public SupplyStore(string path)
    {
        Path = System.IO.Path.GetFullPath(path);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false, DefaultTimeout = 5, ForeignKeys = true }.ToString());
        try
        {
            db.Open(); Exec("PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; PRAGMA busy_timeout=5000; PRAGMA wal_autocheckpoint=1000;");
            if (Scalar("PRAGMA integrity_check") != "ok") throw new InvalidDataException("SQLite integrity check failed; retained original database");
            if (Scalar("SELECT name FROM sqlite_master WHERE type='table' AND name='schema_history'") is null &&
                Scalar("SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' LIMIT 1") is not null)
                throw new InvalidDataException("Existing non-station SQLite database; refusing implicit initialization");
            if (Scalar("PRAGMA foreign_key_check") is not null) throw new InvalidDataException("SQLite foreign key check failed");
            Exec("CREATE TABLE IF NOT EXISTS schema_history(id INTEGER PRIMARY KEY,checksum TEXT NOT NULL,applied TEXT NOT NULL);");
            var hash = Scalar("SELECT checksum FROM schema_history WHERE id=1");
            if (hash is null)
            {
                using var tx = db.BeginTransaction(); Exec(Schema, tx); Exec("INSERT INTO schema_history VALUES(1,$a,$b)", tx, Wire.Hash(Schema), DateTimeOffset.UtcNow.ToString("O")); tx.Commit();
            }
            else if (hash != Wire.Hash(Schema)) throw new InvalidDataException("Schema checksum mismatch");
            if (Scalar("SELECT id FROM schema_history WHERE id<>1") is not null) throw new InvalidDataException("Newer schema: rollback requires matching product");
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
    public BusinessRequest[] Requests(string? owner = null) => Read<BusinessRequest>(owner is null ? "SELECT json FROM requests ORDER BY rowid" : "SELECT json FROM requests WHERE owner=$a ORDER BY rowid", owner is null ? [] : [owner]);
    public BusinessRequest[] PendingRequests() => Read<BusinessRequest>("SELECT json FROM requests WHERE status IN ('Prepared','Dispatching','Unknown') ORDER BY rowid");
    public BusinessRequest[] RequestsForSession(string session) => Read<BusinessRequest>("SELECT json FROM requests WHERE session=$a ORDER BY rowid", session);
    public BusinessRequest? Request(string id) => Read<BusinessRequest>("SELECT json FROM requests WHERE id=$a", id).SingleOrDefault();
    public SupplyOperation[] History(string? filter, int offset, int limit) => Read<SupplyOperation>("SELECT json FROM operations WHERE $a IS NULL OR instr(json,$a)>0 ORDER BY created DESC,id LIMIT $b OFFSET $c", filter, Math.Clamp(limit, 1, 200), Math.Max(0, offset));
    public SendAttempt[] Attempts(string id) => Read<SendAttempt>("SELECT json FROM attempts WHERE request=$a ORDER BY ordinal", id);
    public IntegrationResponse[] Responses(string id) => Read<IntegrationResponse>("SELECT json FROM responses WHERE request=$a ORDER BY rowid", id);
    public Audit[] Audits(string id) => Read<Audit>("SELECT json FROM audit WHERE owner=$a ORDER BY rowid", id);
    public Alarm[] ActiveAlarms() => Read<Alarm>("SELECT json FROM alarms WHERE active=1 ORDER BY rowid DESC");
    public int ActiveAlarmCount() => int.Parse(Scalar("SELECT count(*) FROM alarms WHERE active=1")!);
    public Alarm? Alarm(string id) => Read<Alarm>("SELECT json FROM alarms WHERE id=$a", id).SingleOrDefault();
    public Alarm[] AlarmHistory(int offset, int limit) => Read<Alarm>("SELECT json FROM alarms WHERE active=0 ORDER BY rowid DESC LIMIT $a OFFSET $b", Math.Clamp(limit, 1, 200), Math.Max(0, offset));
    public ConfigRevision[] Configs() => Read<ConfigRevision>("SELECT json FROM configs ORDER BY rowid DESC LIMIT 100");
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
    public int PendingCount() => int.Parse(Scalar("SELECT count(*) FROM requests WHERE status IN ('Prepared','Dispatching','Unknown')")!);
    public void Commit(StationState state, string kind, string owner, string actor, string evidence = "", Command? command = null, CommandReceipt? receipt = null,
        BusinessRequest? request = null, Observation? observation = null, SupplyOperation? operation = null,
        SendAttempt? attempt = null, IntegrationResponse? response = null, bool count = false, ConfigRevision? config = null, Alarm? alarm = null)
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
        if (config is not null) Exec("INSERT INTO configs VALUES($a,$b,$c) ON CONFLICT(id) DO UPDATE SET status=excluded.status,json=excluded.json", tx, config.Id, config.Status, Wire.Encode(config));
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
        if (!File.Exists(source) || File.GetAttributes(source).HasFlag(FileAttributes.ReparsePoint)) throw new FileNotFoundException("Existing regular backup required", source);
        if (File.Exists(destination) || System.IO.Path.GetFullPath(source) == System.IO.Path.GetFullPath(destination)) throw new IOException("Restore requires a new destination");
        using var backup = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = source, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()); backup.Open();
        using (var c = backup.CreateCommand())
        {
            c.CommandText = "PRAGMA integrity_check"; if ((string?)c.ExecuteScalar() != "ok") throw new InvalidDataException("Backup integrity failed");
            c.CommandText = "PRAGMA foreign_key_check"; if (c.ExecuteScalar() is not null) throw new InvalidDataException("Backup foreign keys invalid");
            c.CommandText = "SELECT checksum FROM schema_history WHERE id=1"; if ((string?)c.ExecuteScalar() != Wire.Hash(Schema)) throw new InvalidDataException("Backup schema incompatible with selected product");
            c.CommandText = "SELECT count(*) FROM schema_history WHERE id<>1"; if (Convert.ToInt32(c.ExecuteScalar()) != 0) throw new InvalidDataException("Backup contains newer migration");
        }
        // Persist isolation BEFORE exposing restored bytes. A process death between copying
        // and updating the aggregate must not create an unquarantined restored station.
        var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(destination))!; Directory.CreateDirectory(directory);
        using (var marker = new FileStream(System.IO.Path.Combine(directory, "restore-quarantine"), FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            marker.Write(System.Text.Encoding.UTF8.GetBytes("Restored snapshot may lag external effects; reconcile before admission")); marker.Flush(true);
        }
        using (var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = destination, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString()))
        { target.Open(); backup.BackupDatabase(target); }
        using var restored = new SupplyStore(destination); var state = restored.Load(); state.Quarantined = true; state.Revision++;
        restored.Commit(state, "RestoreIsolation", state.Session?.Id ?? "station", "offline-restore", "External ledger/inventory reconciliation required");
    }
    public void Backup(string destination)
    {
        if (System.IO.Path.GetFullPath(destination) == Path || File.Exists(destination)) throw new IOException("Backup destination must be new");
        using var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = destination, Pooling = false }.ToString()); target.Open(); db.BackupDatabase(target);
        using var check = target.CreateCommand(); check.CommandText = "PRAGMA integrity_check";
        if ((string?)check.ExecuteScalar() != "ok") throw new InvalidDataException("Backup integrity failed");
    }
    public int PruneClosedSessions(DateTimeOffset before, string actor, string evidence)
    {
        var eligible = Read<StationSession>("SELECT json FROM sessions").Where(x => x.Status == SessionStatus.Closed && x.ClosedAt < before)
            .Where(x => !Requests().Any(r => r.SessionId == x.Id && r.Status is RequestStatus.Prepared or RequestStatus.Dispatching or RequestStatus.Unknown)).ToArray();
        using var tx = db.BeginTransaction();
        foreach (var session in eligible)
        {
            // Delete only an entire closed aggregate; retained requests/configs/evidence cannot
            // lose their dependencies. Command receipts and the retention audit are tombstones.
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

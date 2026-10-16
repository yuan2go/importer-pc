using Ipc.Adapters;
using Ipc.Platform;
using Ipc.Supply;
using Microsoft.Data.Sqlite;

namespace Ipc.Fixture;

// Independent receiving ledger: never opens the client's database. A transaction serializes
// arbitration across both lanes and persists acceptance before any deliberately lost response.
public sealed class Receiver : IDisposable
{
    readonly SqliteConnection db;
    readonly object gate = new();
    public Receiver(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        db = new(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false, ForeignKeys = true }.ToString()); db.Open();
        Exec("PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; PRAGMA busy_timeout=5000;");
        using (var check = db.CreateCommand()) { check.CommandText = "PRAGMA integrity_check"; if ((string?)check.ExecuteScalar() != "ok") throw new InvalidDataException("Receiver corrupt"); }
        Exec("""
            CREATE TABLE IF NOT EXISTS contexts(id TEXT PRIMARY KEY,station TEXT NOT NULL,demand INTEGER NOT NULL,accepted INTEGER NOT NULL DEFAULT 0,closed INTEGER NOT NULL DEFAULT 0,revision INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE IF NOT EXISTS receipts(id TEXT PRIMARY KEY,hash TEXT NOT NULL,payload TEXT NOT NULL,json TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS checks(token TEXT PRIMARY KEY,session TEXT NOT NULL,revision INTEGER NOT NULL,remaining INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS network_calls(id TEXT NOT NULL,at TEXT NOT NULL);
            """);
    }
    void Exec(string sql, SqliteTransaction? tx = null, params object?[] values)
    { using var c = Command(sql, tx, values); c.ExecuteNonQuery(); }
    SqliteCommand Command(string sql, SqliteTransaction? tx, params object?[] values)
    {
        var c = db.CreateCommand(); c.CommandText = sql; c.Transaction = tx;
        for (int i = 0; i < values.Length; i++) c.Parameters.AddWithValue("$" + (char)('a' + i), values[i] ?? DBNull.Value);
        return c;
    }
    public IntegrationResponse Process(RequestEnvelope envelope)
    {
        lock (gate)
        {
            if (envelope.Contract != "SIMULATION-v1" || Wire.Hash(envelope.Payload) != envelope.PayloadHash || !Guid.TryParseExact(envelope.RequestId, "N", out _)) throw new InvalidDataException("Invalid fixture identity/hash/contract");
            var p = Wire.Decode<FixturePayload>(envelope.Payload);
            if (p.Policy != "SYNTHETIC-KEEP-v1" || !p.Actor.Id.StartsWith("SIM-", StringComparison.Ordinal) || p.InitialDemand is < 1 or > 100000) throw new InvalidDataException("Synthetic policy/identity required");
            using var tx = db.BeginTransaction();
            Exec("INSERT INTO network_calls VALUES($a,$b)", tx, envelope.RequestId, DateTimeOffset.UtcNow.ToString("O"));
            IntegrationResponse? prior = null;
            using (var c = Command("SELECT hash,json FROM receipts WHERE id=$a", tx, envelope.RequestId))
            using (var r = c.ExecuteReader())
                if (r.Read()) { if (r.GetString(0) != envelope.PayloadHash) throw new InvalidDataException("Identity reused with different payload"); prior = Wire.Decode<IntegrationResponse>(r.GetString(1)); }
            if (prior is not null) { tx.Commit(); return prior; }
            bool exists = false, closed = false; int demand = 0, accepted = 0, revision = 0; string station = "";
            using (var c = Command("SELECT station,demand,accepted,closed,revision FROM contexts WHERE id=$a", tx, p.SessionId))
            using (var r = c.ExecuteReader()) if (r.Read()) { exists = true; station = r.GetString(0); demand = r.GetInt32(1); accepted = r.GetInt32(2); closed = r.GetInt32(3) != 0; revision = r.GetInt32(4); }
            bool ok = true; string reason = "SIMULATION accepted"; int? quantity = null, remaining = exists ? demand - accepted : null; string? checkToken = null;
            if (p.Fault == "Reject") { ok = false; reason = "SIMULATION business rejection"; }
            else if (p.Kind == RequestKind.Context)
            {
                if (exists) { ok = false; reason = "Context already opened"; }
                else
                {
                    using var c = Command("SELECT count(*) FROM contexts WHERE station=$a AND closed=0", tx, p.StationId);
                    if (Convert.ToInt32(c.ExecuteScalar()) != 0) { ok = false; reason = "Station already has an open context"; }
                    else { Exec("INSERT INTO contexts(id,station,demand) VALUES($a,$b,$c)", tx, p.SessionId, p.StationId, p.InitialDemand); remaining = p.InitialDemand; }
                }
            }
            else if (!exists || station != p.StationId || closed) { ok = false; reason = "Context missing/closed/station mismatch"; }
            else switch (p.Kind)
            {
                case RequestKind.Validate:
                    if (p.Package is null or < 1 || p.Package > 10000 || !p.Barcode.All(char.IsAsciiDigit) || p.Barcode.Length is < 1 or > 128) { ok = false; reason = "Invalid synthetic item"; }
                    else { quantity = Math.Min(p.Package.Value, demand - accepted); if (quantity == 0) { ok = false; reason = "No remaining demand"; } }
                    break;
                case RequestKind.Submit:
                    if (p.Quantity is null or < 1 || p.Quantity > p.Package || p.Quantity > demand - accepted) { ok = false; reason = "Atomic demand conflict/invalid quantity"; }
                    else { quantity = p.Quantity; accepted += quantity.Value; remaining = demand - accepted; Exec("UPDATE contexts SET accepted=$a,revision=revision+1 WHERE id=$b", tx, accepted, p.SessionId); }
                    break;
                case RequestKind.CheckClose:
                    checkToken = Wire.Id(); Exec("INSERT INTO checks VALUES($a,$b,$c,$d)", tx, checkToken, p.SessionId, revision, remaining); break;
                case RequestKind.Close:
                    using (var c = Command("SELECT count(*) FROM checks WHERE token=$a AND session=$b AND revision=$c AND remaining=$d", tx, p.CheckToken, p.SessionId, revision, remaining))
                        if (Convert.ToInt32(c.ExecuteScalar()) != 1) { ok = false; reason = "Stale close check"; }
                    if (ok) Exec("UPDATE contexts SET closed=1,revision=revision+1 WHERE id=$a", tx, p.SessionId); break;
                default: ok = false; reason = "Unsupported logical intent"; break;
            }
            var evidenceId = Wire.Id(); var acquiredAt = DateTimeOffset.UtcNow;
            var response = new IntegrationResponse(envelope.RequestId, envelope.PayloadHash, evidenceId, ok ? "Accepted" : "Rejected", reason, remaining, quantity, checkToken,
                ok && p.Kind == RequestKind.Context ? SyntheticSemanticFixture.Context(p, evidenceId, acquiredAt) : null,
                ok && p.Kind is RequestKind.Validate or RequestKind.Submit ? SyntheticSemanticFixture.Item(p, evidenceId, acquiredAt) : null);
            Exec("INSERT INTO receipts VALUES($a,$b,$c,$d)", tx, envelope.RequestId, envelope.PayloadHash, envelope.Payload, Wire.Encode(response)); tx.Commit(); return response;
        }
    }
    public (IntegrationResponse? Response, string? Fault) Query(string id)
    {
        lock (gate)
        {
            using var c = Command("SELECT json,payload FROM receipts WHERE id=$a", null, id); using var r = c.ExecuteReader();
            if (!r.Read()) return (null, null);
            return (Wire.Decode<IntegrationResponse>(r.GetString(0)), Wire.Decode<FixturePayload>(r.GetString(1)).Fault);
        }
    }
    public object Statistics()
    {
        lock (gate)
        {
            int Count(string sql) { using var c = Command(sql, null); return Convert.ToInt32(c.ExecuteScalar()); }
            return new { Mode = "SIMULATION", Receipts = Count("SELECT count(*) FROM receipts"), Calls = Count("SELECT count(*) FROM network_calls"), Accepted = Count("SELECT coalesce(sum(accepted),0) FROM contexts"), Closed = Count("SELECT count(*) FROM contexts WHERE closed=1") };
        }
    }
    public void Dispose() => db.Dispose();
}

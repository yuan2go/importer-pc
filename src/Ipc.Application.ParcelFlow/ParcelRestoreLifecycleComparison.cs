using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRepository
{
    internal static void RequireHandoffSourceEquivalent(string source, string backup)
    {
        var lifecycle = new HashSet<string>(StringComparer.Ordinal) { "parcel_audit", "parcel_meta", "station_run_shifts", "sqlite_sequence" };
        if (HandoffDatabaseIdentity(source, lifecycle) != HandoffDatabaseIdentity(backup, lifecycle))
            throw new InvalidDataException("备份后存在新增业务、原请求、回执、编号或其他事实变化；保留原根，不能用该备份退役较新的来源。");
        using var current = OpenHandoffSnapshot(source); using var original = OpenHandoffSnapshot(backup);
        long Number(SqliteConnection db, string sql)
        { using var q = db.CreateCommand(); q.CommandText = sql; return Convert.ToInt64(q.ExecuteScalar()); }
        string Rows(SqliteConnection db, string sql, string? parameter = null)
        {
            using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using var q = db.CreateCommand(); q.CommandText = sql;
            if (parameter is not null) q.Parameters.AddWithValue("$value", parameter);
            using var rows = q.ExecuteReader();
            while (rows.Read())
                for (var i = 0; i < rows.FieldCount; i++)
                {
                    var value = rows.IsDBNull(i) ? "null" : rows.GetValue(i) switch
                    { byte[] bytes => "bytes:" + Convert.ToBase64String(bytes), string text => "text:" + text, object v => v.GetType().Name + ":" + Convert.ToString(v, CultureInfo.InvariantCulture) };
                    var encoded = Encoding.UTF8.GetBytes(value); digest.AppendData(BitConverter.GetBytes(encoded.Length)); digest.AppendData(encoded);
                }
            return Convert.ToHexString(digest.GetHashAndReset());
        }
        void Require(bool valid)
        { if (!valid) throw new InvalidDataException("备份后的变化不能确认为同一班次正常关闭；原件保留，不忽略新增业务或外部事实。"); }
        var floor = Number(original, "SELECT coalesce(max(seq),0) FROM sqlite_sequence WHERE name='parcel_audit'");
        Require(Rows(current, "SELECT * FROM parcel_audit WHERE seq<=$value ORDER BY seq", floor.ToString(CultureInfo.InvariantCulture)) == Rows(original, "SELECT * FROM parcel_audit ORDER BY seq"));
        var tails = Number(current, "SELECT count(*) FROM parcel_audit WHERE seq>" + floor.ToString(CultureInfo.InvariantCulture));
        Require(tails is 0 or 1);
        Require(Rows(current, "SELECT * FROM sqlite_sequence WHERE name<>'parcel_audit' ORDER BY name") == Rows(original, "SELECT * FROM sqlite_sequence WHERE name<>'parcel_audit' ORDER BY name"));
        var originalRevision = Number(original, "SELECT revision FROM parcel_meta WHERE id=1");
        Require(Number(original, "SELECT count(*) FROM parcel_meta") == 1 && Number(current, "SELECT count(*) FROM parcel_meta") == 1 &&
            Number(current, "SELECT revision FROM parcel_meta WHERE id=1") == checked(originalRevision + tails) &&
            Number(current, "SELECT paused FROM parcel_meta WHERE id=1") == Number(original, "SELECT paused FROM parcel_meta WHERE id=1"));
        Require(Number(current, "SELECT coalesce(max(seq),0) FROM sqlite_sequence WHERE name='parcel_audit'") == checked(floor + tails));
        if (tails == 0)
        {
            Require(Rows(current, "SELECT * FROM station_run_shifts ORDER BY id") == Rows(original, "SELECT * FROM station_run_shifts ORDER BY id"));
            return;
        }
        using var tail = current.CreateCommand(); tail.CommandText = "SELECT seq,at,event,owner,detail FROM parcel_audit WHERE seq>$floor"; tail.Parameters.AddWithValue("$floor", floor);
        string shift; double closedElapsed;
        using (var row = tail.ExecuteReader())
        {
            Require(row.Read() && row.GetInt64(0) == floor + 1 && DateTimeOffset.TryParse(row.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.None, out _) && row.GetString(2) == "StationShiftClosed");
            shift = row.GetString(3);
            using var detail = JsonDocument.Parse(row.GetString(4));
            Require(detail.RootElement.GetProperty("Completed").GetBoolean());
            closedElapsed = detail.RootElement.GetProperty("ElapsedMilliseconds").GetDouble(); Require(double.IsFinite(closedElapsed));
        }
        Require(Number(original, "SELECT count(*) FROM station_run_shifts WHERE status='Open'") == 1);
        Require(Rows(current, "SELECT * FROM station_run_shifts WHERE id<>$value ORDER BY id", shift) == Rows(original, "SELECT * FROM station_run_shifts WHERE id<>$value ORDER BY id", shift));
        using var oldShift = original.CreateCommand(); oldShift.CommandText = "SELECT opened_at,status,closed_at,detected_at,elapsed_ms,clock_adjusted,last_seen_at FROM station_run_shifts WHERE id=$id"; oldShift.Parameters.AddWithValue("$id", shift);
        using var newShift = current.CreateCommand(); newShift.CommandText = oldShift.CommandText; newShift.Parameters.AddWithValue("$id", shift);
        using var before = oldShift.ExecuteReader(); using var after = newShift.ExecuteReader();
        Require(before.Read() && after.Read());
        Require(before.GetString(0) == after.GetString(0) && before.GetString(1) == "Open" && after.GetString(1) == "Closed" && before.IsDBNull(2) && before.IsDBNull(3) &&
            !after.IsDBNull(2) && !after.IsDBNull(3) && DateTimeOffset.TryParse(after.GetString(2), out _) && DateTimeOffset.TryParse(after.GetString(3), out _) &&
            double.IsFinite(after.GetDouble(4)) && after.GetDouble(4) >= before.GetDouble(4) && closedElapsed >= after.GetDouble(4) &&
            after.GetInt64(5) is 0 or 1 && after.GetInt64(5) >= before.GetInt64(5) && DateTimeOffset.TryParse(after.GetString(6), out _));
    }
    internal static SqliteConnection OpenHandoffSnapshot(string database)
    {
        // Callers hold a stable private maintenance DB/WAL copy, or a verified
        // standalone backup. Immutable omits committed WAL facts, so use normal
        // read-only recovery when a WAL exists. Any SHM belongs only to the copy;
        // a standalone backup must not acquire side files during inspection.
        database = Path.GetFullPath(database);
        var source = File.Exists(database + "-wal") ? database : new Uri(database).AbsoluteUri + "?immutable=1";
        var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = source, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        try { db.Open(); return db; } catch { db.Dispose(); throw; }
    }
}

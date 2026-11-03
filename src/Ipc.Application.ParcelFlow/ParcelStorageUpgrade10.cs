using System.Reflection;
using Ipc.Platform;
using Ipc.Platform.Storage;
using Microsoft.Data.Sqlite;

namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRepository
{
    internal bool StorageUpgradeNeedsReview()
    {
        using var command = db.CreateCommand();
        command.CommandText = "SELECT event FROM parcel_audit WHERE event IN ('StorageFormatUpgraded','StorageUpgradeReviewed') ORDER BY seq DESC LIMIT 1";
        return command.ExecuteScalar() as string == "StorageFormatUpgraded";
    }
    const string Storage10Hash = "ECC9F99AFD48F914765248A6B39064A49F0DCBF3DDD2F208C87C75F8E07FEB23";
    static readonly string[] StatisticsSchema = Baseline11.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')
        .Where(line => line.StartsWith("CREATE TABLE station_metric_buckets(", StringComparison.Ordinal) ||
            line.StartsWith("CREATE INDEX station_operation_time ", StringComparison.Ordinal) ||
            line.StartsWith("CREATE INDEX station_archive_time ", StringComparison.Ordinal) ||
            line.StartsWith("CREATE INDEX station_recognition_time ", StringComparison.Ordinal)).ToArray();
    internal static string Storage10Baseline
    {
        get
        {
            var legacy = string.Join('\n', Baseline11.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Where(line => !StatisticsSchema.Contains(line, StringComparer.Ordinal)));
            if (StatisticsSchema.Length != 4 || Database.BaselineHash(legacy) != Storage10Hash)
                throw new InvalidDataException("已知格式10规格不匹配；拒绝推测升级");
            return legacy;
        }
    }
    static string[] StorageSchema(SqliteConnection db, SqliteTransaction? tx = null)
    {
        using var command = db.CreateCommand(); command.Transaction = tx;
        // Only the engine's two optimizer tables are optional. A LIKE prefix
        // would treat '_' as a wildcard and conceal user-defined lookalikes.
        command.CommandText = "SELECT type,name,tbl_name,sql FROM sqlite_schema WHERE sql IS NOT NULL AND NOT(type='table' AND name IN ('sqlite_stat1','sqlite_stat4') AND tbl_name=name) ORDER BY type,name";
        using var rows = command.ExecuteReader(); var result = new List<string>();
        while (rows.Read()) result.Add(Wire.Encode(new[] { rows.GetString(0), rows.GetString(1), rows.GetString(2), rows.GetString(3).Replace("\r\n", "\n", StringComparison.Ordinal) }));
        return result.ToArray();
    }
    static void RequireSchema(SqliteConnection db, string application, int version, SqliteTransaction? tx = null)
    {
        using var expected = new SqliteConnection("Data Source=:memory:;Pooling=False"); expected.Open();
        Database.CreateBaseline(expected, application, version, version == 10 ? Storage10Baseline : version == 11 ? Baseline11 : Baseline);
        if (!StorageSchema(db, tx).SequenceEqual(StorageSchema(expected), StringComparer.Ordinal))
            throw new InvalidDataException("数据库实际结构与获准格式不同；原文件保留，拒绝仅改版本号");
    }
    static bool IsStorage10(SqliteConnection db, string application, SqliteTransaction? tx = null)
    {
        using var command = db.CreateCommand(); command.Transaction = tx;
        command.CommandText = "SELECT application,version,sha FROM platform_migrations";
        using var row = command.ExecuteReader();
        if (!row.Read() || row.GetString(0) != application || row.GetInt64(1) != 10 || row.GetString(2) != Storage10Hash) return false;
        if (row.Read()) throw new InvalidDataException("格式10身份不唯一；保留原件");
        row.Close();
        RequireSchema(db, application, 10, tx);
        return true;
    }
    static bool IsStorage11(SqliteConnection db, string application, SqliteTransaction? tx = null)
    {
        using var c = db.CreateCommand(); c.Transaction = tx; c.CommandText = "SELECT application,version,sha FROM platform_migrations";
        using var r = c.ExecuteReader();
        if (!r.Read() || r.GetString(0) != application || r.GetInt64(1) != 11 || r.GetString(2) != Database.BaselineHash(Baseline11)) return false;
        if (r.Read()) throw new InvalidDataException("格式11身份不唯一"); r.Close(); RequireSchema(db, application, 11, tx); return true;
    }
    internal static void UpgradeStorage10(string database, string application, string backupPath, string backupHash,
        string expectedDatabaseHash, string? expectedWalHash, string actor, Action authority, MaintenanceExecution? execution = null,
        Action<string>? boundary = null)
    {
        authority(); execution?.Checkpoint();
        RecoveryFiles.RequireOrdinaryAncestors(database);
        if (Wire.FileHash(database) != expectedDatabaseHash || (File.Exists(database + "-wal") ? Wire.FileHash(database + "-wal") : null) != expectedWalHash)
            throw new IOException("原DB/WAL在备份后变化；拒绝升级，重新预检");
        var manifest = ParcelRecovery.VerifyBackup(backupPath, authority, execution?.Token ?? default, allowPreviousStorage: true);
        var predecessor = ReadMaintenanceStorageVersion(backupPath, application);
        if (manifest.Application != application || manifest.DatabaseHash != backupHash || predecessor is not (10 or 11))
            throw new InvalidDataException("升级必须有本次完整且可核验的格式10/11恢复集");
        // Integrity inspection is over the complete consistent backup. Even opening
        // an original WAL database read-write can checkpoint it on close.
        using (var verified = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = backupPath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
        {
            verified.Open();
            var token = execution?.Token ?? default;
            SQLitePCL.raw.sqlite3_progress_handler(verified.Handle, 1000, _ => token.IsCancellationRequested ? 1 : 0, null);
            try
            {
                using var integrity = verified.CreateCommand(); integrity.CommandText = "PRAGMA integrity_check";
                using var rows = integrity.ExecuteReader();
                if (!rows.Read() || rows.GetString(0) != "ok" || rows.Read()) throw new InvalidDataException("原库完整性检查失败；保留完整备份和原件");
            }
            catch (SqliteException error) when (error.SqliteErrorCode == 9 && token.IsCancellationRequested)
            { throw new OperationCanceledException("升级预检已取消；原库未写入", error, token); }
        }
        authority(); execution?.Checkpoint(); boundary?.Invoke("BeforeStorageCommit");
        if (execution is { Committing: false }) execution.BeginCommit(MaintenanceStage.InstallingMachine);
        // No cancellation after this boundary. The durable SQLite transaction either
        // retains the exact predecessor or publishes the complete format12 and audit.
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Mode = SqliteOpenMode.ReadWrite, Pooling = false, DefaultTimeout = 5 }.ToString());
        db.Open();
        Database.Execute(db, null, "PRAGMA synchronous=FULL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;");
        using var tx = db.BeginTransaction();
        if (predecessor == 10 ? !IsStorage10(db, application, tx) : !IsStorage11(db, application, tx)) throw new InvalidDataException("原格式在提交前变化");
        if (predecessor == 10) foreach (var sql in StatisticsSchema) Database.Execute(db, tx, sql);
        Database.Execute(db, tx, "ALTER TABLE station_metric_buckets RENAME TO station_metric_buckets_11;");
        Database.Execute(db, tx, Metric12Sql);
        Database.Execute(db, tx, StationSessionSchema);
        Database.Execute(db, tx, "INSERT INTO station_metric_buckets SELECT '',minute,position,recognitions,requests,assigned,rejected,unknown_count,manual_requests FROM station_metric_buckets_11; INSERT INTO station_shift_totals SELECT '',position,sum(recognitions),sum(requests),sum(assigned),sum(rejected),sum(unknown_count),sum(manual_requests) FROM station_metric_buckets_11 GROUP BY position; DROP TABLE station_metric_buckets_11;");
        BackfillStationFacts(db, tx, application);
        boundary?.Invoke("AfterStatisticsSchema");
        using var identity = db.CreateCommand(); identity.Transaction = tx;
        identity.CommandText = "UPDATE platform_migrations SET version=$version,sha=$sha WHERE application=$application AND version=$previous AND sha=$old";
        identity.Parameters.AddWithValue("$version", CurrentStorageVersion); identity.Parameters.AddWithValue("$sha", Database.BaselineHash(Baseline));
        identity.Parameters.AddWithValue("$application", application); identity.Parameters.AddWithValue("$old", predecessor == 10 ? Storage10Hash : Database.BaselineHash(Baseline11)); identity.Parameters.AddWithValue("$previous", predecessor);
        if (identity.ExecuteNonQuery() != 1) throw new InvalidDataException("原格式身份CAS失败");
        Database.Execute(db, tx, "UPDATE parcel_meta SET paused=1,revision=revision+1 WHERE id=1;");
        using var audit = db.CreateCommand(); audit.Transaction = tx;
        audit.CommandText = "INSERT INTO parcel_audit(at,event,owner,detail) VALUES($at,'StorageFormatUpgraded',$owner,$detail)";
        audit.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O")); audit.Parameters.AddWithValue("$owner", application);
        audit.Parameters.AddWithValue("$detail", Wire.Encode(new
        {
            From = predecessor,
            To = CurrentStorageVersion,
            Actor = actor,
            Backup = backupPath,
            BackupHash = backupHash,
            OriginalDatabaseHash = expectedDatabaseHash,
            OriginalWalHash = expectedWalHash,
            Source = typeof(ParcelRepository).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            Policy = "Derived metric keys and transactional shift/fact projections; original requests, receipts, configuration and identities unchanged; software paused; no replay"
        }));
        audit.ExecuteNonQuery();
        RequireCurrentStorage(db, application, tx); RequireSchema(db, application, CurrentStorageVersion, tx);
        boundary?.Invoke("BeforeStorageTransactionCommit");
        tx.Commit();
    }
}

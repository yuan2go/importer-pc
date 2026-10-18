using Microsoft.Data.Sqlite;
namespace Ipc.Platform.Storage;

public sealed record DatabaseMigration(int Version, string Sql);
// The caller owns SQL, transaction boundaries and recovery semantics. Infrastructure never
// interprets a business state or silently recreates a corrupt/unsupported database.
public static class Database
{
    public static SqliteConnection Open(string path, bool create = true)
    {
        var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path,
            Mode = create ? SqliteOpenMode.ReadWriteCreate : SqliteOpenMode.ReadWrite,
            Pooling = false, DefaultTimeout = 5, ForeignKeys = true }.ToString());
        try
        {
            db.Open(); Execute(db, null, "PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; PRAGMA busy_timeout=5000; PRAGMA foreign_keys=ON;");
            return db;
        }
        catch { db.Dispose(); throw; }
    }
    public static void Migrate(SqliteConnection db, string application, IReadOnlyList<DatabaseMigration> migrations)
    {
        if (migrations.Count == 0 || !migrations.Select(x => x.Version).SequenceEqual(Enumerable.Range(1, migrations.Count)))
            throw new InvalidDataException("Migrations must be an ordered immutable sequence");
        using var tx = db.BeginTransaction();
        Execute(db, tx, "CREATE TABLE IF NOT EXISTS platform_migrations(application TEXT NOT NULL,version INTEGER NOT NULL,sha TEXT NOT NULL,PRIMARY KEY(application,version));");
        using (var cmd = db.CreateCommand())
        {
            cmd.Transaction = tx; cmd.CommandText = "SELECT application,version,sha FROM platform_migrations";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var version = r.GetInt32(1);
                if (r.GetString(0) != application || version < 1 || version > migrations.Count || r.GetString(2) != Wire.Hash(migrations[version - 1].Sql))
                    throw new InvalidDataException("Foreign/future/changed application migration; preserve the original database");
            }
        }
        foreach (var m in migrations)
        {
            using var check = db.CreateCommand(); check.Transaction = tx;
            check.CommandText = "SELECT count(*) FROM platform_migrations WHERE application=$app AND version=$v";
            check.Parameters.AddWithValue("$app", application); check.Parameters.AddWithValue("$v", m.Version);
            if (Convert.ToInt64(check.ExecuteScalar()) != 0) continue;
            Execute(db, tx, m.Sql);
            using var insert = db.CreateCommand(); insert.Transaction = tx;
            insert.CommandText = "INSERT INTO platform_migrations VALUES($app,$v,$sha)";
            insert.Parameters.AddWithValue("$app", application); insert.Parameters.AddWithValue("$v", m.Version); insert.Parameters.AddWithValue("$sha", Wire.Hash(m.Sql)); insert.ExecuteNonQuery();
        }
        tx.Commit();
    }
    public static void Execute(SqliteConnection db, SqliteTransaction? tx, string sql)
    { using var command = db.CreateCommand(); command.Transaction = tx; command.CommandText = sql; command.ExecuteNonQuery(); }
    public static void Backup(SqliteConnection source, string destination)
    {
        using var reserve = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        reserve.Dispose();
        using var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = destination, Pooling = false }.ToString());
        target.Open(); source.BackupDatabase(target);
    }
}

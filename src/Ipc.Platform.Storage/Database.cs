using Microsoft.Data.Sqlite;
namespace Ipc.Platform.Storage;

public sealed record DatabaseMigration(int Version, string Sql);
// The caller owns SQL, transaction boundaries and recovery semantics. Infrastructure never
// interprets a business state or silently recreates a corrupt/unsupported database.
public static class Database
{
    internal static Action<long>? BackupCopyBytesForVerification { get; set; }
    public static SqliteConnection Open(string path, bool create = true) => Open(path, create, CancellationToken.None);
    public static SqliteConnection Open(string path, bool create, CancellationToken cancellationToken)
    {
        var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path,
            Mode = create ? SqliteOpenMode.ReadWriteCreate : SqliteOpenMode.ReadWrite,
            Pooling = false, DefaultTimeout = 5, ForeignKeys = true }.ToString());
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            db.Open();
            if (cancellationToken.CanBeCanceled)
                SQLitePCL.raw.sqlite3_progress_handler(db.Handle, 1000, _ => cancellationToken.IsCancellationRequested ? 1 : 0, null);
            Execute(db, null, "PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; PRAGMA busy_timeout=5000; PRAGMA foreign_keys=ON;");
            cancellationToken.ThrowIfCancellationRequested();
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
        target.Open();
        if (BackupCopyBytesForVerification is { } observe)
        {
            using var size = source.CreateCommand(); size.CommandText = "PRAGMA page_count"; var pages = (long)size.ExecuteScalar()!;
            size.CommandText = "PRAGMA page_size"; observe(pages * (long)size.ExecuteScalar()!);
        }
        source.BackupDatabase(target);
    }
    // Online backup has no VM progress callbacks. Bound each copy step so the same
    // maintenance deadline also covers the database copy. Finish rolls back a partial
    // target; callers publish custody COMPLETED only after all hashes are verified.
    public static void Backup(SqliteConnection source, string destination, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using (var reserve = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
        using var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = destination, Pooling = false, DefaultTimeout = 5 }.ToString());
        target.Open();
        using var copy = SQLitePCL.raw.sqlite3_backup_init(target.Handle, "main", source.Handle, "main");
        if (copy is null) throw new SqliteException("Cannot initialize complete maintenance backup", SQLitePCL.raw.sqlite3_errcode(target.Handle));
        int status;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = SQLitePCL.raw.sqlite3_backup_remaining(copy);
            status = SQLitePCL.raw.sqlite3_backup_step(copy, 256);
            if (BackupCopyBytesForVerification is { } observe)
            {
                var after = SQLitePCL.raw.sqlite3_backup_remaining(copy);
                var total = SQLitePCL.raw.sqlite3_backup_pagecount(copy);
                using var size = source.CreateCommand(); size.CommandText = "PRAGMA page_size";
                observe((remaining == 0 ? total - after : remaining - after) * (long)size.ExecuteScalar()!);
            }
            if (status is not (SQLitePCL.raw.SQLITE_OK or SQLitePCL.raw.SQLITE_DONE))
                throw new SqliteException("Complete maintenance backup failed; incomplete target retained", status);
        } while (status != SQLitePCL.raw.SQLITE_DONE);
        cancellationToken.ThrowIfCancellationRequested();
    }
}

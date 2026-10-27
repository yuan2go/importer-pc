using Microsoft.Data.Sqlite;
namespace Ipc.Platform.Storage;

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
    public static string BaselineHash(string sql) => Wire.Hash(sql.Replace("\r\n", "\n", StringComparison.Ordinal));
    // Called only for a new database after the owner has rejected existing non-current formats.
    public static void CreateBaseline(SqliteConnection db, string application, int version, string sql)
    {
        using var tx = db.BeginTransaction();
        Execute(db, tx, "CREATE TABLE platform_migrations(application TEXT NOT NULL,version INTEGER NOT NULL,sha TEXT NOT NULL,PRIMARY KEY(application,version));");
        Execute(db, tx, sql);
        using var command = db.CreateCommand(); command.Transaction = tx;
        command.CommandText = "INSERT INTO platform_migrations VALUES($app,$version,$sha)";
        command.Parameters.AddWithValue("$app", application); command.Parameters.AddWithValue("$version", version);
        command.Parameters.AddWithValue("$sha", BaselineHash(sql)); command.ExecuteNonQuery();
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

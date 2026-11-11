using Microsoft.Data.Sqlite;
namespace Ipc.Platform.Storage;

// The caller owns SQL, transaction boundaries and recovery semantics. Infrastructure never
// interprets a business state or silently recreates a corrupt/unsupported database.
public sealed class BackupCopyFailure(Exception copyFailure, Exception? sourceFailure, bool sourceCheckCompleted)
    : IOException("SQLite backup copy failed: " + copyFailure.Message + "; source check=" +
        (sourceFailure is not null ? "failed: " + sourceFailure.Message : sourceCheckCompleted ? "passed" : "cancelled; source not confirmed"),
        sourceFailure is null ? copyFailure : new AggregateException(copyFailure, sourceFailure))
{
    public Exception? SourceFailure { get; } = sourceFailure;
    public bool SourceCheckCompleted { get; } = sourceCheckCompleted;
}

public static class Database
{
    internal static Action<long>? BackupCopyBytesForVerification { get; set; }
    internal static Action<SqliteConnection>? BackupTargetOpenedForVerification { get; set; }
    public static SqliteConnection Open(string path, bool create = true) => Open(path, create, CancellationToken.None);
    public static SqliteConnection Open(string path, bool create, CancellationToken cancellationToken)
    {
        var db = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = create ? SqliteOpenMode.ReadWriteCreate : SqliteOpenMode.ReadWrite,
            Pooling = false,
            DefaultTimeout = 5,
            ForeignKeys = true
        }.ToString());
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
    public static void Backup(SqliteConnection source, string destination) => Backup(source, destination, CancellationToken.None);
    // Online backup has no VM progress callbacks. Bound each copy step so the same
    // maintenance deadline also covers the database copy. Finish rolls back a partial
    // target; callers publish custody COMPLETED only after all hashes are verified.
    public static void Backup(SqliteConnection source, string destination, CancellationToken cancellationToken)
        => Backup(source, destination, cancellationToken, null);
    public static void Backup(SqliteConnection source, string destination, CancellationToken cancellationToken, Action<long, long>? progress)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Source SQL remains outside the copy boundary: these failures are business
        // store failures. The backup API itself reports destination and source errors
        // through the same status, so its exception alone cannot fence a healthy source.
        long pageBytes;
        try
        {
            using var pageSizeCommand = source.CreateCommand(); pageSizeCommand.CommandText = "PRAGMA page_size";
            pageBytes = (long)pageSizeCommand.ExecuteScalar()!;
        }
        catch (SqliteException error) { throw new BackupCopyFailure(error, error, true); }
        try
        {
            using (var reserve = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
            using var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = destination, Pooling = false, DefaultTimeout = 5 }.ToString());
            target.Open(); BackupTargetOpenedForVerification?.Invoke(target);
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
                    observe((remaining == 0 ? total - after : remaining - after) * pageBytes);
                }
                if (status is not (SQLitePCL.raw.SQLITE_OK or SQLitePCL.raw.SQLITE_DONE))
                    throw new SqliteException("Complete maintenance backup failed; incomplete target retained", status);
                progress?.Invoke((SQLitePCL.raw.sqlite3_backup_pagecount(copy) - SQLitePCL.raw.sqlite3_backup_remaining(copy)) * pageBytes,
                    SQLitePCL.raw.sqlite3_backup_pagecount(copy) * pageBytes);
            } while (status != SQLitePCL.raw.SQLITE_DONE);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (Exception copyError) when (copyError is SqliteException or IOException or UnauthorizedAccessException)
        {
            // Finish/dispose the failed destination before touching either connection.
            // Only a separate source read/integrity failure proves that the source
            // must be fenced. No retry, write, or repair is performed by this check.
            Exception? sourceFailure = null; var checkedSource = false;
            using var check = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            check.CancelAfter(TimeSpan.FromMinutes(2));
            try
            {
                check.Token.ThrowIfCancellationRequested();
                using var probe = new SqliteConnection(new SqliteConnectionStringBuilder
                { DataSource = source.DataSource, Mode = SqliteOpenMode.ReadOnly, Pooling = false, DefaultTimeout = 5 }.ToString());
                probe.Open();
                SQLitePCL.raw.sqlite3_progress_handler(probe.Handle, 1000, _ => check.IsCancellationRequested ? 1 : 0, null);
                using var command = probe.CreateCommand(); command.CommandText = "PRAGMA quick_check";
                using var rows = command.ExecuteReader();
                if (!rows.Read() || rows.GetString(0) != "ok" || rows.Read()) throw new InvalidDataException("Backup source integrity check failed; originals retained");
                checkedSource = true;
            }
            catch (OperationCanceledException) when (check.IsCancellationRequested) { }
            catch (SqliteException error) when (error.SqliteErrorCode == 9 && check.IsCancellationRequested) { }
            catch (Exception error) when (error is SqliteException or IOException or InvalidDataException or UnauthorizedAccessException)
            { sourceFailure = error; checkedSource = true; }
            throw new BackupCopyFailure(copyError, sourceFailure, checkedSource);
        }
    }
}

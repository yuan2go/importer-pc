using System.Diagnostics;
using Microsoft.Data.Sqlite;

namespace Ipc.Platform.Storage;

public enum StorageFailureKind { RetryBudgetExceeded, DurableOutcomeUncertain, Unrecoverable }
public sealed class StorageWriteFailure(StorageFailureKind kind, int attempts, double elapsedMs, Exception inner)
    : IOException("Durable storage write failed: " + kind + "; attempts=" + attempts, inner)
{
    public StorageFailureKind Kind { get; } = kind;
    public int Attempts { get; } = attempts;
    public double ElapsedMs { get; } = elapsedMs;
}

public static partial class AtomicStorageWrite
{
    internal static Action<string>? PhaseForVerification { get; set; }
    public static bool IsUnrecoverable(Exception error) => error is InvalidDataException or UnauthorizedAccessException ||
        error is SqliteException sql && (sql.SqliteErrorCode is 1 or 3 or 8 or 11 or 13 or 17 or 19 or 23 or 26 || sql.SqliteExtendedErrorCode is 8202 or 8458) ||
        error is IOException io && (io.HResult & 0xffff) is 39 or 112 or 1392 or 1393;
    static bool Transient(Exception error) => !IsUnrecoverable(error) &&
        (error is SqliteException sql && sql.SqliteErrorCode is 5 or 6 or 10 || error is IOException and not StorageWriteFailure);
    static bool Busy(Exception error) => error is SqliteException sql && sql.SqliteErrorCode is 5 or 6;

    // The callback must contain local SQL only. A retry is allowed only before
    // COMMIT and after rollback is confirmed. Never repeat a business/provider call.
    public static void Execute(SqliteConnection db, Action<SqliteTransaction> write, Action? beforeBegin = null)
    {
        var started = Stopwatch.GetTimestamp(); var budget = TimeSpan.FromSeconds(5); var attempts = 0;
        var priorTimeout = db.DefaultTimeout; db.DefaultTimeout = 1;
        SQLitePCL.raw.sqlite3_busy_timeout(db.Handle, 100);
        try
        {
            while (true)
            {
                SqliteTransaction? tx = null; var commitStarted = false;
                try
                {
                    attempts++; beforeBegin?.Invoke();
                    tx = db.BeginTransaction(deferred: false);
                    write(tx); PhaseForVerification?.Invoke("BeforeCommit"); commitStarted = true;
                    while (true)
                    {
                        try { tx.Commit(); break; }
                        catch (SqliteException error) when (Busy(error) && SQLitePCL.raw.sqlite3_get_autocommit(db.Handle) == 0 &&
                            attempts < 32 && Stopwatch.GetElapsedTime(started) < budget)
                        { attempts++; Delay(started, budget, attempts); } // Retry the same COMMIT, never the SQL callback.
                    }
                    PhaseForVerification?.Invoke("AfterCommit"); return;
                }
                catch (Exception error)
                {
                    if (commitStarted)
                        throw new StorageWriteFailure(IsUnrecoverable(error) ? StorageFailureKind.Unrecoverable : StorageFailureKind.DurableOutcomeUncertain,
                            attempts, Stopwatch.GetElapsedTime(started).TotalMilliseconds, error);
                    try
                    {
                        if (tx is not null && SQLitePCL.raw.sqlite3_get_autocommit(db.Handle) == 0) tx.Rollback();
                        if (SQLitePCL.raw.sqlite3_get_autocommit(db.Handle) == 0) throw new InvalidDataException("Rollback not confirmed");
                    }
                    catch (Exception rollback)
                    { throw new StorageWriteFailure(IsUnrecoverable(error) || IsUnrecoverable(rollback) ? StorageFailureKind.Unrecoverable : StorageFailureKind.DurableOutcomeUncertain,
                        attempts, Stopwatch.GetElapsedTime(started).TotalMilliseconds, new AggregateException(error, rollback)); }
                    if (!Transient(error)) throw new StorageWriteFailure(IsUnrecoverable(error) ? StorageFailureKind.Unrecoverable : StorageFailureKind.DurableOutcomeUncertain,
                        attempts, Stopwatch.GetElapsedTime(started).TotalMilliseconds, error);
                    if (attempts >= 32 || Stopwatch.GetElapsedTime(started) >= budget)
                        throw new StorageWriteFailure(StorageFailureKind.RetryBudgetExceeded, attempts, Stopwatch.GetElapsedTime(started).TotalMilliseconds, error);
                    Delay(started, budget, attempts);
                }
                finally { tx?.Dispose(); }
            }
        }
        finally { db.DefaultTimeout = priorTimeout; SQLitePCL.raw.sqlite3_busy_timeout(db.Handle, 5000); }
    }
    static void Delay(long started, TimeSpan budget, int attempt)
    {
        var remaining = budget - Stopwatch.GetElapsedTime(started);
        if (remaining > TimeSpan.Zero) Thread.Sleep(TimeSpan.FromMilliseconds(Math.Min(remaining.TotalMilliseconds, Math.Min(200, 25 << Math.Min(attempt - 1, 3)))));
    }
}

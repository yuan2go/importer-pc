using System.Diagnostics;
using Ipc.Platform;
using Ipc.Platform.Storage;
using Microsoft.Data.Sqlite;

static class AtomicStorageChecks
{
    public static async Task Run(string root)
    {
        void Check(bool ok, string message) { if (!ok) throw new Exception("FAIL ST08 " + message); Console.WriteLine("PASS ST08 " + message); }
        var file = Path.Combine(root, "standalone-atomic-fixture.db");
        using var db = Database.Open(file); Database.Execute(db, null, "CREATE TABLE atomic_fixture(id TEXT PRIMARY KEY, value TEXT NOT NULL)");
        void Write(SqliteTransaction tx, string id)
        { using var c = db.CreateCommand(); c.Transaction = tx; c.CommandText = "INSERT INTO atomic_fixture VALUES($id,'synthetic')"; c.Parameters.AddWithValue("$id", id); c.ExecuteNonQuery(); }
        long Count(string id)
        { using var c = db.CreateCommand(); c.CommandText = "SELECT count(*) FROM atomic_fixture WHERE id=$id"; c.Parameters.AddWithValue("$id", id); return (long)c.ExecuteScalar()!; }
        using (var locker = Database.Open(file, false))
        {
            using var held = locker.BeginTransaction(deferred: false); var watch = Stopwatch.StartNew();
            var pending = Task.Run(() => AtomicStorageWrite.Execute(db, tx => Write(tx, "busy")));
            await Task.Delay(150); held.Commit(); await pending;
            Check(Count("busy") == 1, "a real SQLite writer lock released inside budget yields exactly one durable row");
            Console.WriteLine("MEASURE sqlite_busy_recovery_ms=" + watch.Elapsed.TotalMilliseconds);
        }
        void Hook(Action<string>? action) => typeof(AtomicStorageWrite).GetProperty("PhaseForVerification", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.SetValue(null, action);
        try
        {
            var failures = 0; var writes = 0;
            Hook(phase => { if (phase == "BeforeCommit" && ++failures <= 2) throw new IOException("Synthetic rollback-safe I/O jitter"); });
            AtomicStorageWrite.Execute(db, tx => { writes++; Write(tx, "rolled-back"); });
            Check(writes == 3 && Count("rolled-back") == 1, "failed pre-COMMIT attempts roll back, preserve one identity, and retry successfully");
            writes = 0;
            Hook(phase => { if (phase == "AfterCommit") throw new IOException("Synthetic lost COMMIT result"); });
            StorageWriteFailure? uncertain = null;
            try { AtomicStorageWrite.Execute(db, tx => { writes++; Write(tx, "uncertain"); }); }
            catch (StorageWriteFailure error) { uncertain = error; }
            Check(uncertain?.Kind == StorageFailureKind.DurableOutcomeUncertain && writes == 1 && Count("uncertain") == 1,
                "an actually committed write with a lost result is not repeated or represented as success");
            writes = 0;
            Hook(phase => { if (phase == "BeforeCommit") throw new SqliteException("Synthetic SQLITE_FULL", 13); });
            StorageWriteFailure? full = null;
            try { AtomicStorageWrite.Execute(db, tx => { writes++; Write(tx, "full"); }); }
            catch (StorageWriteFailure error) { full = error; }
            Check(full?.Kind == StorageFailureKind.Unrecoverable && writes == 1 && Count("full") == 0, "SQLITE_FULL is permanent, not a retry loop");
            Hook(null);
            using (var quota = db.CreateCommand())
            {
                quota.CommandText = "PRAGMA max_page_count"; var originalMaximum = Convert.ToInt64(quota.ExecuteScalar());
                quota.CommandText = "PRAGMA page_count"; var pages = Convert.ToInt64(quota.ExecuteScalar());
                quota.CommandText = "PRAGMA max_page_count=" + pages; quota.ExecuteScalar();
                StorageWriteFailure? nativeFull = null;
                try
                {
                    AtomicStorageWrite.Execute(db, tx =>
                    {
                        using var insert = db.CreateCommand(); insert.Transaction = tx; insert.CommandText = "INSERT INTO atomic_fixture VALUES('native-full',$value)";
                        insert.Parameters.AddWithValue("$value", new string('X', 1048576)); insert.ExecuteNonQuery();
                    });
                }
                catch (StorageWriteFailure error) { nativeFull = error; }
                finally { quota.CommandText = "PRAGMA max_page_count=" + originalMaximum; quota.ExecuteScalar(); }
                Check(nativeFull?.Kind == StorageFailureKind.Unrecoverable && nativeFull.Attempts == 1 && Count("native-full") == 0,
                    "native SQLite page exhaustion auto-rolls back and retains permanent classification");
            }
            Hook(phase => { if (phase == "BeforeCommit") throw new IOException("Synthetic persistent pre-COMMIT I/O failure"); });
            StorageWriteFailure? exhausted = null; var budget = Stopwatch.StartNew();
            try { AtomicStorageWrite.Execute(db, tx => Write(tx, "exhausted")); }
            catch (StorageWriteFailure error) { exhausted = error; }
            Check(exhausted?.Kind == StorageFailureKind.RetryBudgetExceeded && exhausted.Attempts <= 32 && Count("exhausted") == 0,
                "persistent rollback-safe failure exhausts a bounded monotonic budget with no row committed");
            using var pragma = db.CreateCommand(); pragma.CommandText = "PRAGMA synchronous";
            Check(Convert.ToInt64(pragma.ExecuteScalar()) == 2, "FULL durability is preserved");
            pragma.CommandText = "PRAGMA journal_mode"; Check((string)pragma.ExecuteScalar()! == "wal", "WAL mode is preserved");
            ExtensionStore.AtomicWrite(Path.Combine(root, "ATOMIC-STORAGE-CHECKS.json"), new { Scope = "Standalone synthetic table; no product schema change or physical I/O fault", BudgetMilliseconds = 5000,
                ExhaustedElapsedMs = budget.Elapsed.TotalMilliseconds, exhausted!.Attempts, Exhausted = exhausted.Kind, Uncertain = uncertain!.Kind, Permanent = full!.Kind });
        }
        finally { Hook(null); }
    }
}

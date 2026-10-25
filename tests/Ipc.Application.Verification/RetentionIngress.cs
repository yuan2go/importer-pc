using System.Diagnostics;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Platform;
using Ipc.Platform.Storage;
using Microsoft.Data.Sqlite;

// Component/transaction qualification only. Full operation/raw fixtures remain in
// RetentionQualification; these deliberately small envelopes exercise SQL semantics.
static class RetentionIngress
{
    static readonly DateTimeOffset Now = new(2026, 10, 25, 0, 0, 0, TimeSpan.Zero);
    static void Check(bool value, string message)
    { if (!value) throw new InvalidDataException(message); Console.WriteLine("PASS RETENTION-INGRESS: " + message); }
    static void Exec(SqliteConnection db, string sql) { using var c = db.CreateCommand(); c.CommandText = sql; c.ExecuteNonQuery(); }
    static long Scalar(SqliteConnection db, string sql) { using var c = db.CreateCommand(); c.CommandText = sql; return Convert.ToInt64(c.ExecuteScalar()); }
    static string Id(int n) => n.ToString("x32");
    static SqliteConnection Open(string path) { var db = new SqliteConnection($"Data Source={path};Pooling=False"); db.Open(); return db; }
    static string Create(string directory, int count)
    {
        using (var repo = new ParcelRepository(directory, "sportmaster")) repo.Commit(null, "Fixture", "Synthetic query/transaction component", paused: true);
        var path = Path.Combine(directory, "sportmaster.db");
        using var db = Open(path); using var tx = db.BeginTransaction(); using var insert = db.CreateCommand(); insert.Transaction = tx;
        insert.CommandText = "INSERT INTO parcel_ingress(rowid,id,position,received_at,state,body,body_hash,session,completed_at) VALUES($row,$id,'left','2026-10-01',$state,$body,$hash,'component',$at)";
        foreach (var p in new[] { "$row", "$id", "$state", "$body", "$hash", "$at" }) insert.Parameters.AddWithValue(p, "");
        insert.Prepare();
        for (var n = 1; n <= count; n++)
        {
            // Sparse rowids, mixed completion state and two envelope branches.
            var body = n % 2 == 0 ? "{\"Measurement\":{\"AdmissionId\":\"retired\"}}" : "{\"Observation\":{\"AdmissionId\":\"retired\"}}";
            insert.Parameters["$row"].Value = n * 3L; insert.Parameters["$id"].Value = Id(n);
            insert.Parameters["$state"].Value = n % 2 == 0 ? "Inspected" : "Processed";
            insert.Parameters["$body"].Value = body; insert.Parameters["$hash"].Value = Wire.Hash(body); insert.Parameters["$at"].Value = "2026-10-01";
            insert.ExecuteNonQuery();
        }
        tx.Commit();
        Exec(db, """
            INSERT INTO parcel_history_index(id,admission,observation,position) VALUES('retired-owner','retired','old','left'),('retained-owner','retained','current','left');
            INSERT INTO parcel_retired_operations VALUES('retired-owner','original-hash','2026-10-01','component');
            INSERT INTO parcel_ingress(id,position,received_at,state,body,body_hash,session,completed_at) VALUES
            ('pending','left','2026-10-01','Pending','{}','p','component','2026-10-01'),
            ('interrupted','left','2026-10-01','Interrupted','{}','i','component','2026-10-01'),
            ('retained-observation','left','2026-10-01','Processed','{"Observation":{"AdmissionId":"retained"}}','o','component','2026-10-01'),
            ('retained-measurement','left','2026-10-01','Inspected','{"Measurement":{"AdmissionId":"retained"}}','m','component','2026-10-01'),
            ('coalesce-first','left','2026-10-01','Processed','{"Observation":{"AdmissionId":"retained"},"Measurement":{"AdmissionId":"retired"}}','c','component','2026-10-01'),
            ('recent','left','2026-10-01','Processed','{}','r','component','2026-10-24'),
            ('cutoff-equal','left','2026-10-01','Processed','{}','e','component','2026-10-18'),
            ('null-date','left','2026-10-01','Processed','{}','n','component',NULL),
            ('invalid-date','left','2026-10-01','Processed','{}','d','component','invalid');
            INSERT INTO parcel_audit(at,event,owner,detail) SELECT '2026-10-01','ComponentInput',id,'{}' FROM parcel_ingress;
            INSERT INTO parcel_ingress_audits SELECT owner,seq FROM parcel_audit WHERE event='ComponentInput';
            """);
        return path;
    }
    static List<(long Row, string Id)> Page(SqliteConnection db, string sql, long cursor)
    {
        using var c = db.CreateCommand(); c.CommandText = sql; c.Parameters.AddWithValue("$cursor", cursor); c.Parameters.AddWithValue("$cutoff", Now.AddDays(-7).ToString("O"));
        using var r = c.ExecuteReader(); List<(long, string)> result = []; while (r.Read()) result.Add((r.GetInt64(0), r.GetString(1))); return result;
    }
    static object Plan(SqliteConnection db, string sql, bool optimized)
    {
        using var c = db.CreateCommand(); c.CommandText = "EXPLAIN QUERY PLAN " + sql;
        c.Parameters.AddWithValue("$cursor", 0); c.Parameters.AddWithValue("$cutoff", Now.AddDays(-7).ToString("O"));
        using var r = c.ExecuteReader(); List<string> lines = []; while (r.Read()) lines.Add(r.GetString(3));
        Console.WriteLine(Wire.Encode(new { Optimized = optimized, Plan = lines }));
        if (optimized) Check(lines.Any(x => x.Contains("INTEGER PRIMARY KEY (rowid>?)")) && !lines.Any(x => x.Contains("TEMP B-TREE")) &&
            lines.Any(x => x.Contains("SEARCH h USING INDEX") && x.Contains("(admission=?)")) && lines.Any(x => x.Contains("SEARCH t USING COVERING INDEX") && x.Contains("(id=?)")), "rowid seek, no sort, indexed retained-owner protection");
        return lines;
    }
    static long Instructions(SqliteConnection db, string sql)
    {
        long callbacks = 0; SQLitePCL.raw.sqlite3_progress_handler(db.Handle, 1000, _ => { callbacks++; return 0; }, null);
        try { Page(db, sql, 0); } finally { SQLitePCL.raw.sqlite3_progress_handler(db.Handle, 0, null, null); }
        return callbacks * 1000;
    }
    public static void Run(string workspace, string root, bool large)
    {
        var reports = new List<object>();
        foreach (var count in large ? new[] { 1000, 10000, 100000, 604800 } : new[] { 1000, 10000 })
        {
            var path = Create(Path.Combine(root, "scale-" + count), count); using var db = Open(path);
            var original = ParcelRepository.ExpiredInputPageSql.Replace(" i NOT INDEXED ", " i ");
            var oldPlan = Plan(db, original, false); var plan = Plan(db, ParcelRepository.ExpiredInputPageSql, true);
            foreach (var cursor in new[] { 0L, count / 2 * 3L, (count - 100) * 3L, count * 3L })
            {
                var expected = Enumerable.Range((int)(cursor / 3) + 1, Math.Min(256, count - (int)(cursor / 3))).Select(Id);
                var actual = Page(db, ParcelRepository.ExpiredInputPageSql, cursor);
                Check(actual.Select(x => x.Id).SequenceEqual(expected) && actual.SequenceEqual(Page(db, original, cursor)), count + ": exact oracle at cursor " + cursor);
            }
            var beforeVm = Instructions(db, original); var afterVm = Instructions(db, ParcelRepository.ExpiredInputPageSql);
            Check(afterVm < 20000, "first page VM work stays bounded independently of eligible tail");
            long cursorAll = 0, total = 0; var timer = Stopwatch.StartNew();
            while (true)
            {
                var rows = Page(db, ParcelRepository.ExpiredInputPageSql, cursorAll); if (rows.Count == 0) break;
                CheckSequence(rows, total); total += rows.Count; cursorAll = rows[^1].Row;
            }
            Check(total == count, "all pages visit each eligible input once across rowid gaps; protected tail retained");
            var selectSeconds = timer.Elapsed.TotalSeconds;
            db.Close(); var commits = 0; timer.Restart();
            using (var repo = new ParcelRepository(Path.GetDirectoryName(path)!, "sportmaster"))
            {
                repo.Boundary = p => { if (p == "AfterRetentionInputCommit") commits++; };
                var result = repo.ExpireHistoryCore(new SportmasterFlow(), 7, Now, new string('A', 64), () => { });
                Check(result.Inputs == count && result.Operations == 0 && commits == (count + 255) / 256, "bounded 256-input transactions commit exact count");
            }
            var deleteSeconds = timer.Elapsed.TotalSeconds; db.Open();
            Check(Scalar(db, "SELECT count(*) FROM parcel_ingress") == 9 && Scalar(db, "SELECT count(*) FROM parcel_ingress_audits") == 9 &&
                Scalar(db, "SELECT count(*) FROM parcel_audit WHERE event='ComponentInput'") == 9, "inputs and only their associated audits are retired atomically");
            Check(Scalar(db, "SELECT count(*) FROM parcel_retired_inputs WHERE body_hash NOT IN ('" + Wire.Hash("{\"Measurement\":{\"AdmissionId\":\"retired\"}}") + "','" + Wire.Hash("{\"Observation\":{\"AdmissionId\":\"retired\"}}") + "')") == 0, "every retired input hash retained");
            using var integrity = db.CreateCommand(); integrity.CommandText = "PRAGMA integrity_check"; Check(integrity.ExecuteScalar() as string == "ok", "post-delete integrity");
            reports.Add(new { Count = count, OriginalPlan = oldPlan, Plan = plan, BeforeVm = beforeVm, AfterVm = afterVm, SelectSeconds = selectSeconds, DeleteSeconds = deleteSeconds, Commits = commits });
        }
        Faults(root);
        Budgets(workspace, root);
        ExtensionStore.AtomicWrite(Path.Combine(root, "RETENTION-INGRESS.json"), new { Scope = "Query/transaction component and cancellation; not full custody CLI or field qualification", Samples = reports, ProductSha256 = Wire.FileHash(typeof(ParcelRepository).Assembly.Location) });
    }
    static void CheckSequence(List<(long Row, string Id)> rows, long total)
    { if (!rows.Select(x => x.Id).SequenceEqual(Enumerable.Range((int)total + 1, rows.Count).Select(Id))) throw new InvalidDataException("Paging duplicated/skipped an identity"); }
    static void Faults(string root)
    {
        foreach (var point in new[] { "AfterRetentionInputDelete", "BeforeRetentionInputCommit", "AfterRetentionInputCommit" })
        {
            var path = Create(Path.Combine(root, point), 600); var deletes = 0;
            using (var repo = new ParcelRepository(Path.GetDirectoryName(path)!, "sportmaster"))
            {
                repo.Boundary = p => { if (p == point && (point != "AfterRetentionInputDelete" || ++deletes == 257)) throw new IOException("Injected " + point); };
                try { repo.ExpireHistoryCore(new SportmasterFlow(), 7, Now, new string('A', 64), () => { }); throw new Exception("Fault missing"); } catch (IOException) { }
            }
            using (var db = Open(path))
            {
                var committed = point == "BeforeRetentionInputCommit" ? 0 : 256;
                Check(Scalar(db, "SELECT count(*) FROM parcel_retired_inputs") == committed && Scalar(db, "SELECT count(*) FROM parcel_ingress") == 609 - committed &&
                    Scalar(db, "SELECT count(*) FROM parcel_ingress_audits") == 609 - committed && Scalar(db, "SELECT count(*) FROM parcel_audit WHERE event='ComponentInput'") == 609 - committed,
                    point + ": reopen preserves exact committed prefix and rolls back the whole unfinished page");
                Check(Scalar(db, "SELECT count(*) FROM parcel_ingress i JOIN parcel_retired_inputs t ON t.id=i.id") == 0, "retained/retired identities never overlap");
            }
            using var resumed = new ParcelRepository(Path.GetDirectoryName(path)!, "sportmaster");
            resumed.ExpireHistoryCore(new SportmasterFlow(), 7, Now, new string('B', 64), () => { });
            Check(resumed.ExpireHistoryCore(new SportmasterFlow(), 7, Now, new string('C', 64), () => { }).Inputs == 0, "resume scans fresh rowids without resurrecting/repeating retired inputs");
        }
    }
    static void Budgets(string workspace, string root)
    {
        Check(ParcelRepository.RetentionMaintenanceBudget == TimeSpan.FromHours(2), "production maintenance budget remains two hours");
        foreach (var phase in new[] { "InspectOriginal", "InitializeRepository", "ValidateOriginal", "CompleteBackup", "ExpireDetails", "ValidateResult", "Compact", "Disposed" })
        {
            var directory = Path.Combine(root, "budget-" + phase); var path = Create(directory, 10);
            var c = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace, "config/sportmaster/business.json"))) with { Retention = new(200, 67108864, HistoryDays: 7) };
            ExtensionStore.AtomicWrite(Path.Combine(directory, "configuration.json"), new ConfigurationRevision<ParcelConfiguration>(1, c, null, "Active", null));
            var backup = Path.Combine(root, "budget-" + phase + ".db"); var before = Wire.FileHash(path); using var cancellation = new CancellationTokenSource();
            try
            {
                ParcelRepository.ExpireHistoryAuthorized(path, before, backup, new SportmasterFlow(), (_, _) => { }, true, () => { }, cancellation.Token,
                    reached => { if (reached == phase) cancellation.Cancel(); });
                throw new Exception("Cancellation missing at " + phase);
            }
            catch (OperationCanceledException) { }
            if (phase is "InspectOriginal" or "InitializeRepository") Check(Wire.FileHash(path) == before, "pre-initialization cancellation leaves original bytes unchanged: " + phase);
            if (phase is "InspectOriginal" or "InitializeRepository" or "ValidateOriginal" or "CompleteBackup")
                Check(!File.Exists(backup), "no backup before prerequisite phase: " + phase);
            else Check(File.Exists(backup + ".recovery/COMPLETED.json"), "complete backup survives later cancellation: " + phase);
            using var lease = new ResourceLease(directory, ["application-owner"]);
            Check(true, "cancellation at " + phase + " returns failure only after owner lease is released");
        }
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        var source = Path.Combine(root, "scale-1000", "sportmaster.db");
        try { Wire.FileHash(source, cancelled.Token); throw new Exception("Hash ignored cancellation"); } catch (OperationCanceledException) { Check(true, "hash observes cancellation"); }
        using var sourceDb = Open(source); var target = Path.Combine(root, "cancelled-backup.db");
        try { Database.Backup(sourceDb, target, cancelled.Token); throw new Exception("Backup ignored cancellation"); } catch (OperationCanceledException) { Check(!File.Exists(target), "cancelled backup creates no target"); }
        using (var interrupt = new CancellationTokenSource())
        using (var db = Database.Open(Path.Combine(root, "sql-cancel.db"), true, interrupt.Token))
        {
            interrupt.CancelAfter(20);
            try { Exec(db, "WITH RECURSIVE n(x) AS (VALUES(0) UNION ALL SELECT x+1 FROM n WHERE x<100000000) SELECT sum(x) FROM n"); throw new Exception("SQL ignored budget"); }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 9) { Check(interrupt.IsCancellationRequested, "connection progress handler interrupts an executing query"); }
        }
        // The backup API never invokes a VM progress handler. Cancel after the
        // destination has begun receiving pages, then prove cleanup releases handles.
        var bulk = Path.Combine(root, "backup-cancel-source.db");
        using (var db = Database.Open(bulk)) Exec(db, "CREATE TABLE payload(body BLOB); INSERT INTO payload VALUES(zeroblob(67108864)); PRAGMA wal_checkpoint(TRUNCATE)");
        var bulkHash = Wire.FileHash(bulk); var partial = Path.Combine(root, "backup-cancel-partial.db");
        using var duringCopy = new CancellationTokenSource(); using var started = new ManualResetEventSlim();
        var observer = Task.Factory.StartNew(() =>
        {
            started.Set(); var elapsed = Stopwatch.StartNew();
            while (elapsed.Elapsed < TimeSpan.FromSeconds(20))
            {
                if (File.Exists(partial) && new FileInfo(partial).Length >= 1048576) { duringCopy.Cancel(); return; }
                Thread.Sleep(1);
            }
            throw new TimeoutException("Backup did not start copying pages");
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        started.Wait();
        using (var db = Open(bulk))
        {
            try { Database.Backup(db, partial, duringCopy.Token); throw new Exception("Executing backup ignored cancellation"); }
            catch (OperationCanceledException) { Check(true, "paged backup cancels after copying begins"); }
        }
        observer.GetAwaiter().GetResult();
        using (var exclusive = new FileStream(partial, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        Check(Wire.FileHash(bulk) == bulkHash && !File.Exists(partial + ".recovery/COMPLETED.json"), "interrupted backup preserves source bytes and cannot publish complete custody");
    }
}

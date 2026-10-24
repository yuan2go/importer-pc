using System.Diagnostics;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Platform;
using Microsoft.Data.Sqlite;

// Projection/index scale only. It intentionally does not fabricate millions of
// accepted business originals, raw replies, physical operations or elapsed hours.
static class StabilityHistoryScale
{
    public static void Run(string evidence)
    {
        const int count = 2592000; var database = Path.Combine(evidence, "projection-scale.db");
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Pooling = false }.ToString()); db.Open();
        using var budget = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        SQLitePCL.raw.sqlite3_progress_handler(db.Handle, 1000, _ => budget.IsCancellationRequested ? 1 : 0, null);
        using var command = db.CreateCommand(); command.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE parcel_history_index(seq INTEGER PRIMARY KEY,id TEXT UNIQUE,admission TEXT UNIQUE,observation TEXT UNIQUE,position TEXT);
            CREATE TABLE parcel_history_projection(id TEXT PRIMARY KEY,barcode TEXT,state TEXT,observed REAL,wcs TEXT,plc TEXT,upload TEXT,capture TEXT,accepted INTEGER,unknown INTEGER,physical INTEGER,attempts INTEGER);
            CREATE INDEX parcel_history_barcode ON parcel_history_projection(barcode,id);
            CREATE INDEX parcel_history_state ON parcel_history_projection(state,id);
            CREATE INDEX parcel_history_observed ON parcel_history_projection(observed,id);
            CREATE INDEX parcel_history_position ON parcel_history_index(position,seq);
            """; command.ExecuteNonQuery(); var watch = Stopwatch.StartNew();
        for (var first = 1; first <= count; first += 10000)
        {
            command.CommandText = """
                WITH RECURSIVE n(i) AS (SELECT $first UNION ALL SELECT i+1 FROM n WHERE i<$last)
                INSERT INTO parcel_history_index SELECT i,printf('%032x',i),'a-'||i,'o-'||i,CASE i%2 WHEN 0 THEN 'left' ELSE 'right' END FROM n;
                INSERT INTO parcel_history_projection SELECT id,'SCALE-'||seq,'SlotResolved',julianday('2026-09-24T00:00:00Z')+seq/86400.0,'r-'||seq,NULL,NULL,NULL,1,0,0,1 FROM parcel_history_index WHERE seq BETWEEN $first AND $last;
                """;
            command.Parameters.Clear(); command.Parameters.AddWithValue("$first", first); command.Parameters.AddWithValue("$last", Math.Min(first + 9999, count));
            using var tx = db.BeginTransaction(); command.Transaction = tx; command.ExecuteNonQuery(); tx.Commit(); command.Transaction = null;
        }
        var prefillMs = watch.Elapsed.TotalMilliseconds; List<object> results = [];
        foreach (var item in new[] { ("all", new ParcelHistoryFilter(), (long)count), ("left", new ParcelHistoryFilter(Position: "left"), count / 2L), ("exact-barcode", new ParcelHistoryFilter(Barcode: "SCALE-2592000"), 1L) })
        {
            List<double> times = [];
            for (var sample = 0; sample < 5; sample++)
            {
                watch.Restart(); using var reader = new ParcelHistoryReader(database, budget.Token);
                var metrics = reader.Metrics(new(item.Item2, count)); times.Add(watch.Elapsed.TotalMilliseconds);
                if (metrics.Operations != item.Item3) throw new InvalidDataException("Scale query count differs");
            }
            results.Add(new { Query = item.Item1, SamplesMs = times, MaximumMs = times.Max(), Scope = "Five diagnostic samples; not sustained-load P95/P99 qualification" });
        }
        command.CommandText = "PRAGMA integrity_check"; command.Parameters.Clear(); if (command.ExecuteScalar() as string != "ok") throw new InvalidDataException("Projection scale integrity failed");
        command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)"; command.ExecuteNonQuery();
        using var process = Process.GetCurrentProcess();
        ExtensionStore.AtomicWrite(Path.Combine(evidence, "SCALE-PROJECTION.json"), new { Status = "PASS_INDEX_SCALE_ONLY", Rows = count, PrefillMs = prefillMs, DatabaseBytes = new FileInfo(database).Length,
            ProcessPeakWorkingSet = process.PeakWorkingSet64, Queries = results, RuntimeSha256 = Wire.FileHash(typeof(ParcelRuntime).Assembly.Location),
            FullCustodyScale = "NOT_RUN", Target8GiB = "NOT_RUN", DurationQualification = "NOT_RUN" });
        Console.WriteLine("PASS INDEX SCALE ONLY rows=" + count + "; full custody/8GiB/24h NOT_RUN");
    }
}

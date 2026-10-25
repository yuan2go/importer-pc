#if LATENCY_MEASUREMENT
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster;
using Ipc.Platform;
using Microsoft.Data.Sqlite;

static partial class Program
{
    // Performance-only, synthetic offline import under the actual coordinator.
    // This is not a full raw/ingress/retirement qualification dataset.
    static async Task VerifyLatencyCapacity(string root, List<string> lines, string[] args)
    {
        string Option(string name, string fallback) { var i = Array.IndexOf(args, name); return i < 0 ? fallback : args[i + 1]; }
        var output = Path.GetFullPath(Option("--latency-output", Path.Combine(Path.GetTempPath(), "latency-" + Wire.Id())));
        if (Directory.Exists(output)) throw new IOException("Fresh latency evidence root required");
        Directory.CreateDirectory(output);
        var rows = int.Parse(Option("--latency-rows", "600000"));
        var seconds = int.Parse(Option("--latency-seconds", "12"));
        if (rows is < 0 or > 600000 || seconds is < 2 or > 60) throw new InvalidDataException("Bounded fixture only");
        var free = new DriveInfo(Path.GetPathRoot(output)!).AvailableFreeSpace;
        if (free < 20L * 1024 * 1024 * 1024) throw new IOException("20GiB fixture/backup reserve required");
        var config = Path.Combine(output, "config"); Directory.CreateDirectory(config);
        var c = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(root, "config/sportmaster/business.json"))) with
        { MaximumOperations = 2000, Retention = new(800, 67108864, HistoryDays: 7), BackupSchedule = null };
        File.WriteAllText(Path.Combine(config, "business.json"), Wire.Encode(c));
        var launch = new BusinessLaunch(config, Path.Combine(output, "data"), null, null);
        await using var runtime = new ParcelRuntime(launch, new SportmasterFlow(), new VisualFactory(launch, false)) { RuntimeIdentityPreflight = () => { } };
        await runtime.StartAsync(default); await ManagementModeFixture.Authorize(runtime);
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        var gate = (SemaphoreSlim)typeof(ParcelRuntime).GetField("gate", hidden)!.GetValue(runtime)!;
        var repo = (ParcelRepository)typeof(ParcelRuntime).GetField("repository", hidden)!.GetValue(runtime)!;
        var db = (SqliteConnection)typeof(ParcelRepository).GetField("db", hidden)!.GetValue(repo)!;
        var operations = (Dictionary<string, ParcelOperation>)typeof(ParcelRuntime).GetField("operations", hidden)!.GetValue(runtime)!;
        var at = DateTimeOffset.UtcNow;
        ParcelOperation Empty(int n) => new("synthetic-latency-" + n, "synthetic-admission-" + n, "SYNTHETIC-" + n,
            "synthetic-observation-" + n, at.AddMilliseconds(n), Wire.Hash(Wire.Encode(c)), c, [], c.Weight, null,
            ParcelState.Abandoned, null, null, null, "Synthetic offline performance-only unsent operation", PositionId: n % 2 == 0 ? "left" : "right");
        var prefill = Stopwatch.StartNew();
        await Task.Run(async () =>
        {
            await gate.WaitAsync();
            try
            {
                repo.Commit(null, "SyntheticFixturePaused", "Isolated performance-only offline import", paused: true);
                for (var first = 0; first < rows; first += 1000)
                {
                    if (new DriveInfo(Path.GetPathRoot(output)!).AvailableFreeSpace < 12L * 1024 * 1024 * 1024) throw new IOException("Fixture reserve reached; partial data retained");
                    using var tx = db.BeginTransaction();
                    using var archive = db.CreateCommand(); archive.Transaction = tx;
                    archive.CommandText = "INSERT INTO parcel_archives VALUES($id,$admission,$observation,$body,$hash,$at);" +
                        "INSERT INTO parcel_history_index(id,admission,observation,position) VALUES($id,$admission,$observation,json_extract($body,'$.PositionId'))";
                    foreach (var p in new[] { "$id", "$admission", "$observation", "$body", "$hash", "$at" }) archive.Parameters.AddWithValue(p, "");
                    archive.Prepare();
                    for (var n = first; n < Math.Min(first + 1000, rows); n++)
                    {
                        var op = Empty(n); var body = Wire.Encode(op);
                        archive.Parameters["$id"].Value = op.Id; archive.Parameters["$admission"].Value = op.AdmissionId; archive.Parameters["$observation"].Value = op.ObservationId;
                        archive.Parameters["$body"].Value = body; archive.Parameters["$hash"].Value = Wire.Hash(body); archive.Parameters["$at"].Value = at.ToString("O"); archive.ExecuteNonQuery();
                    }
                    tx.Commit();
                    if (first % 50000 == 0) Console.WriteLine("LATENCY PREFILL " + first + "/" + rows);
                }
                using var checkpoint = db.CreateCommand(); checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)"; checkpoint.ExecuteNonQuery();
                // The synthetic import bypassed repository Archive; notify only an
                // existing in-memory revision, never modify the DB schema or production API.
                var revision = typeof(ParcelRepository).GetField("archiveRevision", hidden);
                if (revision is not null) revision.SetValue(repo, (long)revision.GetValue(repo)! + 1);
            }
            finally { gate.Release(); }
        });
        prefill.Stop();
        var countSamples = new List<object>();
        await gate.WaitAsync();
        try
        {
            for (var i = 0; i < 5; i++)
            {
                var watch = Stopwatch.StartNew();
                SQLitePCL.raw.sqlite3_prepare_v2(db.Handle, "SELECT count(*) FROM parcel_archives", out var statement);
                using (statement)
                {
                    if (SQLitePCL.raw.sqlite3_step(statement) != SQLitePCL.raw.SQLITE_ROW) throw new InvalidDataException("Count failed");
                    var count = SQLitePCL.raw.sqlite3_column_int64(statement, 0);
                    var steps = SQLitePCL.raw.sqlite3_stmt_status(statement, SQLitePCL.raw.SQLITE_STMTSTATUS_VM_STEP, 0);
                    if (count != rows) throw new InvalidDataException("Synthetic archive count mismatch");
                    countSamples.Add(new { Rows = count, Ms = watch.Elapsed.TotalMilliseconds, VmSteps = steps });
                }
            }
        }
        finally { gate.Release(); }
        var gateTimes = new List<double>(); long entered = 0; var encodes = 0;
        runtime.Boundary = point =>
        {
            if (point == "SnapshotGateAcquired") entered = Stopwatch.GetTimestamp();
            else if (point == "SnapshotEncoding") Interlocked.Increment(ref encodes);
            else if (point == "SnapshotGateReleasing") lock (gateTimes) gateTimes.Add(Stopwatch.GetElapsedTime(entered).TotalMilliseconds);
        };
        var cases = new List<object>();
        foreach (var resident in new[] { 200, 800 })
        {
            ParcelOperation rich;
            await gate.WaitAsync();
            try
            {
                operations.Clear();
                for (var n = 0; n < resident; n++)
                {
                    var op = Empty(rows + n + 1) with { State = ParcelState.WcsUnknown, Reason = "Synthetic unresolved responsibility; never resend" };
                    var request = new FrozenRequest(Wire.Id(), c.ApplicationId, op.Id, op.AdmissionId, "wcs-slot", c.Wcs!, "synthetic-only", op.ConfigurationHash, "{}", Wire.Hash("{}"), at);
                    StageReply[] replies = [];
                    if (n == 0)
                    {
                        var raw = new string('R', 1024 * 1024);
                        var destination = new WcsDestinationResult(WcsDestinationOutcome.UnpackRequired, "SYNTHETIC", "Not real upstream data", null, "synthetic-task", "synthetic-sku", null, null, null, null,
                            new("synthetic-code", "synthetic-sku", 1, Enumerable.Range(0, 3000).Select(x => new WcsUnpackChute("synthetic-chute-" + x, "synthetic-order-" + x, x, "synthetic-phase")).ToArray()));
                        replies = [new(Wire.Id(), request.Id, request.PayloadHash, true, null, null, false, null, raw, at, Destination: destination)];
                    }
                    op = op with { Wcs = new(request, StageState.Unknown, 1, replies) };
                    operations.Add(op.Id, op);
                }
                rich = operations.Values.First();
                repo.Commit(null, "SyntheticResidentImported", "Synthetic in-memory unresolved performance set; no external execution");
            }
            finally { gate.Release(); }
            foreach (var count in new[] { 1, 2, 4 })
            {
                var snapshots = new List<double>(); var details = 0;
                var setup = Stopwatch.StartNew();
                var windows = new List<Window>(); var models = new List<ParcelViewModel>();
                for (var i = 0; i < count; i++)
                {
                    var vm = new ParcelViewModel(runtime, "Synthetic performance", "sportmaster");
                    typeof(ParcelViewModel).GetProperty("DetailMeasurement", hidden)!.SetValue(vm, (Action)(() => Interlocked.Increment(ref details)));
                    var window = new Window { Title = "Synthetic isolated latency " + i, ShowActivated = false, ShowInTaskbar = false, Width = 900, Height = 650, Content = new HistoryPage { DataContext = vm } };
                    windows.Add(window); models.Add(vm); window.Show();
                    await vm.RefreshAsync(); vm.Selected = vm.Operations.Single(x => x.Id == rich.Id);
                }
                await Task.WhenAll(models.Select(vm => vm.DrainAsync()));
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                setup.Stop();
                gateTimes.Clear(); encodes = 0; details = 0;
                var gaps = new List<double>(); using var probeEnd = new CancellationTokenSource();
                var probe = Task.Run(async () =>
                {
                    while (!probeEnd.IsCancellationRequested)
                    {
                        var started = Stopwatch.GetTimestamp();
                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => gaps.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds), DispatcherPriority.Background);
                        try { await Task.Delay(20, probeEnd.Token); } catch (OperationCanceledException) { break; }
                    }
                });
                var allocated = GC.GetTotalAllocatedBytes(true); var gen2 = GC.CollectionCount(2); var startedAt = DateTimeOffset.UtcNow; var duration = Stopwatch.StartNew();
                while (duration.Elapsed < TimeSpan.FromSeconds(seconds))
                {
                    var watch = Stopwatch.StartNew(); await Task.WhenAll(models.Select(vm => vm.RefreshAsync()));
                    snapshots.Add(watch.Elapsed.TotalMilliseconds);
                    await Task.Delay(500);
                }
                probeEnd.Cancel(); await probe;
                cases.Add(new { Resident = resident, Windows = count, WindowSetupMs = setup.Elapsed.TotalMilliseconds, Started = startedAt, Ended = DateTimeOffset.UtcNow, WallMs = duration.Elapsed.TotalMilliseconds,
                    RefreshBatches = Stats(snapshots), GateHeld = Stats(gateTimes), SnapshotEncodes = encodes, DetailEvaluations = details, DispatcherWait = Stats(gaps),
                    AllocationBytes = GC.GetTotalAllocatedBytes(true) - allocated, Gen2Collections = GC.CollectionCount(2) - gen2 });
                foreach (var window in windows) window.Close();
                ExtensionStore.AtomicWrite(Path.Combine(output, "MEASUREMENTS.json"), new { Rows = rows, PrefillSeconds = prefill.Elapsed.TotalSeconds, FreeBytesBefore = free, Count = countSamples, Cases = cases });
                Console.WriteLine("LATENCY CASE resident=" + resident + " windows=" + count + " completed");
            }
        }
        runtime.Boundary = null;
        if (args.Contains("--latency-assert"))
        {
            using var measured = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "MEASUREMENTS.json")));
            if (measured.RootElement.GetProperty("Cases").EnumerateArray().Any(x => x.GetProperty("DetailEvaluations").GetInt32() != 0 ||
                x.GetProperty("SnapshotEncodes").GetInt32() > x.GetProperty("RefreshBatches").GetProperty("Count").GetInt32()))
                throw new InvalidOperationException("Warm revision refreshed full details or serialized once per window");
        }
        ExtensionStore.AtomicWrite(Path.Combine(output, "IDENTITY.json"), new { Scope = "Actual runtime and HistoryPage/ViewModel, synthetic archives and unresolved resident records only; no full custody/retention/site qualification",
            Main = "a8cd431456f73e83040f75a0c5e3cc1423440d1b", RuntimeSha256 = Wire.FileHash(typeof(ParcelRuntime).Assembly.Location), UiSha256 = Wire.FileHash(typeof(ParcelViewModel).Assembly.Location), FixtureSha256 = Wire.FileHash(typeof(Program).Assembly.Location),
            OS = Environment.OSVersion.ToString(), Cores = Environment.ProcessorCount, DatabaseBytes = new FileInfo(Path.Combine(launch.DataRoot, c.Mode.ToString(), c.StationId, c.ApplicationId, c.ApplicationId + ".db")).Length });
        lines.Add("PASS isolated latency measurements completed; no absolute performance qualification");
        static object Stats(List<double> source)
        {
            var ordered = source.Order().ToArray();
            double P(double p) => ordered.Length == 0 ? 0 : ordered[Math.Min(ordered.Length - 1, Math.Max(0, (int)Math.Ceiling(ordered.Length * p) - 1))];
            return new { Count = ordered.Length, P50Ms = P(.5), P95Ms = P(.95), P99Ms = P(.99), MaxMs = P(1) };
        }
    }
}
#endif

using System.Diagnostics;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Assembly;
using Ipc.Platform;
using Microsoft.Data.Sqlite;

static partial class ReliabilityHardening
{
    static async Task HandoffStop(string workspace, string root)
    {
        var c = Config(workspace); var launch = Launch(root, c);
        await using var app = new ParcelRuntime(launch, new SportmasterFlow(), new ParcelCapabilityFactory(launch));
        await app.StartAsync(default);
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        app.Boundary = point => { if (point == "AfterIngressDurable") { entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException(); } };
        var handoff = Task.Run(() => app.EmitReferenceAt("left", "overlap", "123", "overlap-event"));
        Check(entered.Wait(TimeSpan.FromSeconds(5)), "slow handoff reached actual durable SQLite boundary");
        bool retained; using var budget = new CancellationTokenSource(TimeSpan.FromMilliseconds(150)); var watch = Stopwatch.StartNew();
        try { await app.StopAsync(budget.Token); retained = false; }
        catch (OperationCanceledException) { retained = !handoff.IsCompleted && app.HasUnreleasedResources; }
        finally { release.Set(); }
        var accepted = await handoff.WaitAsync(TimeSpan.FromSeconds(5));
        await app.StopAsync(default).WaitAsync(TimeSpan.FromSeconds(5));
        var db = Path.Combine(Data(launch, c), "sportmaster.db");
        Check(retained && accepted && Scalar(db, "SELECT count(*) FROM parcel_ingress WHERE state='Interrupted'") == 1 && Scalar(db, "SELECT count(*) FROM parcel_operations") == 0,
            "Stop cancellation retains the in-flight durable handoff; successful handoff is drained to Interrupted without executing business");
        await app.DisposeAsync();
        Check(!app.HasActiveWork && !app.HasUnreleasedResources, "real handoff/drain termination precedes connection and lease release");
        Console.WriteLine($"MEASURE H02 handoff-stop wait_and_drain_ms={watch.Elapsed.TotalMilliseconds:F2}; custody=Interrupted; external=0");
    }
    static async Task Storage(string workspace, string root)
    {
        var c = Config(workspace); var launch = Launch(root, c);
        await using var app = new ParcelRuntime(launch, new SportmasterFlow(), new ParcelCapabilityFactory(launch));
        await app.StartAsync(default);
        var selection = await app.SelectHistoryAsync(new());
        var database = Path.Combine(Data(launch, c), "sportmaster.db");
        using var blocker = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Pooling = false }.ToString()); blocker.Open();
        using var transaction = blocker.BeginTransaction(deferred: false);
        int writers = 0; using var entered = new ManualResetEventSlim();
        app.Boundary = point => { if (point == "BeforeIngressDurable" && Interlocked.Increment(ref writers) == 2) entered.Set(); };
        var watch = Stopwatch.StartNew();
        var left = Task.Run(() => app.EmitReferenceAt("left", "locked-left", "111", "locked-left-event"));
        var right = Task.Run(() => app.EmitReferenceAt("right", "locked-right", "222", "locked-right-event"));
        Check(entered.Wait(TimeSpan.FromSeconds(3)), "both positions reached SQLite while an independent real write transaction owns the lock");
        var queryWatch = Stopwatch.StartNew(); var page = await app.QueryHistoryAsync(selection).WaitAsync(TimeSpan.FromSeconds(2));
        Check(page.Items.Length == 0 && !left.IsCompleted && !right.IsCompleted, "WAL history reader completes while both durable handoffs await the shared writer");
        var queryMs = queryWatch.Elapsed.TotalMilliseconds;
        var outcomes = await Task.WhenAll(left, right).WaitAsync(TimeSpan.FromSeconds(12));
        var waitMs = watch.Elapsed.TotalMilliseconds;
        Check(outcomes.All(x => !x) && Scalar(database, "SELECT count(*) FROM parcel_ingress") == 0,
            "bounded write-lock failure returns no handoff success and creates no fictitious custody");
        transaction.Rollback();
        Check(!app.EmitReferenceAt("right", "after-fault", "333", "after-fault-event"), "shared storage fault fences both positions, unlike a single camera connection failure");
        await app.StopAsync(default).WaitAsync(TimeSpan.FromSeconds(8)); await app.DisposeAsync();
        await using var recovered = new ParcelRuntime(launch, new SportmasterFlow(), new ParcelCapabilityFactory(launch));
        await recovered.StartAsync(default);
        Check(recovered.EmitReferenceAt("right", "rescan", "444", "rescan-event"), "after lock release and explicit restart a new source handoff is durable");
        await Until(() => recovered.WorkerProgress().Single(p => p.Position == "right").Processed == 1);
        Check(Scalar(database, "SELECT count(*) FROM parcel_ingress WHERE state='Processed'") == 1,
            "recovery has only the explicit new input; rejected source inputs are not silently replayed");
        Console.WriteLine($"MEASURE H02 sqlite_lock_wait_ms={waitMs:F2}; history_query_ms={queryMs:F2}; rejected=2; new_processed=1; durability=WAL/FULL unchanged");
    }
}

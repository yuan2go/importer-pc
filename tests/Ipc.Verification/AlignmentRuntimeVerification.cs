using System.Diagnostics;
using Ipc.Adapters;
using Ipc.Platform;
using Ipc.Product;
using Ipc.Supply;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

static class AlignmentRuntimeVerification
{
    public static async Task Run(string root, string url, string workspace)
    {
        var cfg = Rig.Config(url); var directory = Path.Combine(root, "alignment-background");
        using var store = new SupplyStore(Path.Combine(directory, "station.db"));
        var faults = new FaultInjection(); await using var app = new StationApplication(store, cfg, new SimulationRequestContract(cfg), faults);
        foreach (var device in cfg.Devices) await app.SetDeviceAsync(device.DeviceId, Wire.Id(), DeviceHealth.Ready);
        await using var healthPort = new LoopbackIntegration(cfg);
        await app.SetWcsHealthAsync(await healthPort.HealthAsync(CancellationToken.None));
        try { await app.BackupAsync(Path.Combine(root, "unauthorized-backup.db")); throw new InvalidOperationException("Unauthenticated backup was allowed"); }
        catch (GuardException) { Assert(!File.Exists(Path.Combine(root, "unauthorized-backup.db")), "Backend protects full database backup without UI guard"); }
        var first = await app.SnapshotAsync();
        await app.ExecuteAsync(new(Wire.Id(), first.State.Revision, "Login", Value: "maintenance"));
        var snapshot = await app.SnapshotAsync();
        var blocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        
        faults.Hook = point => { if (point == "T1") { blocked.TrySetResult(); release.Task.GetAwaiter().GetResult(); } };
        var sw = Stopwatch.StartNew();
        var pending = app.ExecuteAsync(new(Wire.Id(), snapshot.State.Revision, "OpenContext", Value: "000001"));
        Assert(sw.Elapsed < TimeSpan.FromMilliseconds(300), "Slow store cannot execute inline on UI/caller");
        await blocked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        int heartbeats = 0; using var timer = new Timer(_ => Interlocked.Increment(ref heartbeats), null, 0, 10);
        var queuedRead = app.SnapshotAsync(); await Task.Delay(120);
        Assert(!pending.IsCompleted && !queuedRead.IsCompleted && heartbeats >= 3, "Caller remains responsive while one background owner holds storage");
        release.SetResult(); Assert((await pending).Status == "Persisted", "Slow accepted command is not dropped");
        Assert((await queuedRead).State.Session?.Barcode == "000001", "Read waits behind write and observes committed fact");
        faults.Hook = null;
        await using (var port = new LoopbackIntegration(cfg))
        {
            var claim = (await app.ClaimAsync())!.Value;
            Assert(await app.BeforeSendAsync(claim.Request, claim.Attempt), "Initial context handoff authorized");
            await app.CompleteAsync(claim.Request, claim.Attempt, await port.ExecuteAsync(claim.Request, CancellationToken.None));
        }
        // A real independent SQLite writer holds the lock. Neither caller nor observation
        // callback waits synchronously for SQLite; queued business evidence must drain later.
        var beforeLock = await app.SnapshotAsync();
        var observed = new Observation(Wire.Id(), cfg.Devices[0].DeviceId, beforeLock.State.Devices["left"].Epoch, Wire.Id(), 1,
            Wire.Id(), "Enter", [], null, DateTimeOffset.UtcNow);
        using (var lockedDb = new SqliteConnection($"Data Source={store.Path};Pooling=False"))
        {
            lockedDb.Open(); using var transaction = lockedDb.BeginTransaction();
            sw.Restart(); var lockedWrite = app.ExecuteAsync(new(Wire.Id(), beforeLock.State.Revision, "Login", Value: "maintenance"));
            Assert(sw.Elapsed < TimeSpan.FromMilliseconds(300), "Actual SQLite lock cannot execute on caller");
            await Task.Delay(100); var observation = app.ReceiveAsync(observed);
            Assert(!lockedWrite.IsCompleted && !observation.IsCompleted, "Single owner serializes event behind locked storage");
            transaction.Rollback();
            Assert((await lockedWrite).Status == "Persisted", "Actual SQLite writer resumes after lock release"); await observation;
        }
        Assert(store.HasObservation(observed) && (await app.SnapshotAsync()).State.Lanes["left"].AdmissionId == observed.AdmissionId, "Admitted observation retained after slow real SQLite write");
        // Clone isolation and monotonically versioned projection; no live mutable state escapes.
        var a = await app.SnapshotAsync(); a.State.Session!.Barcode = "UI-MUTATION";
        var b = await app.SnapshotAsync();
        Assert(b.State.Session!.Barcode == "000001" && b.ProjectionVersion > a.ProjectionVersion && b.GeneratedAt >= a.GeneratedAt, "Read-only projection is detached/versioned/fresh");
        // Install a failing alarm-write trigger. Repeated snapshots MUST remain reads.
        using (var db = new SqliteConnection($"Data Source={store.Path};Pooling=False"))
        {
            db.Open(); using var sql = db.CreateCommand();
            sql.CommandText = "CREATE TRIGGER forbid_alarm_insert BEFORE INSERT ON alarms BEGIN SELECT RAISE(ABORT,'Snapshot attempted alarm write'); END;"; sql.ExecuteNonQuery();
            app.Latch("ReadOnlyProjectionProbe", "synthetic diagnostic without a read-driven write");
            for (int i = 0; i < 10; i++) Assert((await app.SnapshotAsync()).RuntimeFaults.Any(x => x.StartsWith("ReadOnlyProjectionProbe")), "Fault projections do not require alarm writes");
            sql.CommandText = "DROP TRIGGER forbid_alarm_insert"; sql.ExecuteNonQuery();
        }
        Console.WriteLine("PASS A01 slow storage background ownership, detached versioned projections and read-only snapshots");

        var install = Path.GetDirectoryName(typeof(ProductRuntime).Assembly.Location)!;
        var manifest = new ProductManifest(1, "ipc-reference", RunMode.Simulation, "net10.0", "any", "supply", ReferenceProduct.Definition.Views,
            ReferenceProduct.Definition.Modules.Select(x => new ModuleSpec(x.Id, x.Kind, x.Version, x.ContractMajor, x.Dependencies, x.File, Wire.FileHash(Path.Combine(install, x.File)))).ToArray());
        var configDirectory = Path.Combine(root, "alignment-view-config"); Directory.CreateDirectory(configDirectory);
        File.WriteAllText(Path.Combine(configDirectory, "product.json"), Wire.Encode(manifest));
        File.WriteAllText(Path.Combine(configDirectory, "station.json"), Wire.Encode(cfg with { StationId = "VIEW-" + Wire.Id() }));
        File.WriteAllText(Path.Combine(configDirectory, "presentation.json"), Wire.Encode(new PresentationSettings()));
        var boot = new BootOptions(Path.Combine(configDirectory, "product.json"), Path.Combine(configDirectory, "station.json"), Path.Combine(configDirectory, "presentation.json"), Path.Combine(root, "alignment-view-data"), install, Path.Combine(root,"view-leases"), View: "history", Layout: "compact");
        using var host = ProductHost.Build(boot); await host.StartAsync(); var runtime = host.Services.GetRequiredService<ProductRuntime>();
        Assert(runtime.Application is not null && runtime.Diagnostics.Count == 0 && runtime.Presentation.View == "history" && runtime.Presentation.Layout == "compact", "Startup View/Layout arguments select installed presentation");
        var business = runtime.Application; var id = runtime.RuntimeId;
        for (int i = 0; i < 100 && (await business!.SnapshotAsync()).State.Blocks.Any(x => x.Source == "transport"); i++) await Task.Delay(50);
        Assert(!(await business!.SnapshotAsync()).State.Blocks.Any(x => x.Source == "transport"), "Fixture transport is ready before reviewing the two-view command revision");
        await runtime.SavePresentationAsync(runtime.Presentation with { Layout = "standard" });
        Assert(runtime.Presentation.View == "history" && ReferenceEquals(business, runtime.Application) && id == runtime.RuntimeId, "Layout persistence preserves page and shared instance");
        var l = await business!.SnapshotAsync(); var login = new Command(Wire.Id(), l.State.Revision, "Login", Value: "maintenance");
        var twoViews = await Task.WhenAll(runtime.CommandAsync(login), runtime.CommandAsync(login));
        Assert(twoViews.All(x => x.Status == "Persisted") && twoViews[0] == twoViews[1], "Two view facades replay the same command receipt without a second effect");
        var ready = await business.SnapshotAsync();
        var open = new Command(Wire.Id(), ready.State.Revision, "OpenContext", Value: "000001");
        var receipts = await Task.WhenAll(runtime.CommandAsync(open), runtime.CommandAsync(open));
        Assert(receipts[0].RequestId is not null && receipts[0] == receipts[1], "Two views create one immutable external request");
        for (int i = 0; i < 100 && (await business.SnapshotAsync()).State.Session?.Status != SessionStatus.Active; i++) await Task.Delay(50);
        var current = await business.SnapshotAsync(); var details = await business.DetailsAsync(current.State.Session!.Id);
        Assert(details.Length == 1 && details[0].Attempts.Length == 1 && details[0].Responses.Length == 1, "Shared runtime has one sender/attempt/response");
        using (var stopDb = new SqliteConnection($"Data Source={Path.Combine(runtime.DataDirectory!, "station.db")};Pooling=False"))
        {
            stopDb.Open(); using var trigger = stopDb.CreateCommand();
            trigger.CommandText = "CREATE TRIGGER fail_stop BEFORE INSERT ON audit WHEN json_extract(NEW.json,'$.Kind')='StoppingBarrier' BEGIN SELECT RAISE(ABORT,'SIMULATION stop persistence failure'); END;"; trigger.ExecuteNonQuery();
            try
            {
                AggregateException? original = null;
                try { await host.StopAsync(); } catch (AggregateException failure) { original = failure; }
                Assert(original?.ToString().Contains("SIMULATION stop persistence failure") == true, "Original injected SQLite stop failure remains reported");
                var stopped = await business.SnapshotAsync();
                Assert(stopped.State.Stopping && stopped.RuntimeFaults.Any(x => x.StartsWith("StorageFailure")), "Failed durable stop still retains memory stop barrier");
                Assert(runtime.ModuleStates.Values.All(x => x == ModuleState.Stopped), "Sender and sources stop even when stop persistence fails");
                Assert(await ((StationApplication)business).ClaimAsync() is null, "No new handoff after failed durable stop");
            }
            finally
            {
                // End the injected disk fault after observing its original exception and
                // resource boundary. Leaving it installed would deliberately fail the
                // later IDisposable cleanup again, obscuring the scenario's result.
                trigger.CommandText = "DROP TRIGGER fail_stop"; trigger.ExecuteNonQuery();
            }
            await runtime.StopAsync(CancellationToken.None);
            trigger.CommandText = "SELECT count(*) FROM audit WHERE json_extract(json,'$.Kind')='StoppingBarrier'";
            Assert(Convert.ToInt32(trigger.ExecuteScalar()) == 1 && runtime.ModuleStates.Values.All(x => x == ModuleState.Stopped), "After fault removal one retried durable stop succeeds without restarting modules");
        }
        Console.WriteLine("PASS A01 startup view/layout, shared command deduplication/one sender, failed-stop resource shutdown and backup authority");
        await QueueAndHandoff(root, url);
    }
    static async Task QueueAndHandoff(string root, string url)
    {
        var cfg = Rig.Config(url) with { CommandCapacity = 1 };
        using (var store = new SupplyStore(Path.Combine(root, "queue-burst", "station.db")))
        {
            var injection = new FaultInjection(); await using var app = new StationApplication(store, cfg, new SimulationRequestContract(cfg), injection);
            var snap = await app.SnapshotAsync(); await app.ExecuteAsync(new(Wire.Id(), snap.State.Revision, "Login", Value: "maintenance")); snap = await app.SnapshotAsync();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            injection.Hook = p => { if (p == "T1") { entered.TrySetResult(); release.Task.GetAwaiter().GetResult(); } };
            var held = app.ExecuteAsync(new(Wire.Id(), snap.State.Revision, "Login", Value: "maintenance")); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var queued = app.ExecuteAsync(new(Wire.Id(), snap.State.Revision, "Login", Value: "maintenance"));
            var watch = Stopwatch.StartNew();
            var rejected = Enumerable.Range(0, 200).Select(_ => app.ExecuteAsync(new(Wire.Id(), snap.State.Revision, "Login", Value: "operator"))).ToArray();
            var controls = Enumerable.Range(0, 32).Select(_ => app.ExecuteAsync(new(Wire.Id(), snap.State.Revision, "Pause"))).ToArray();
            Assert(watch.Elapsed < TimeSpan.FromSeconds(1), "Burst rejection never executes marker/SQLite work inline");
            release.TrySetResult(); await held; await queued; await Task.WhenAll(rejected.Concat(controls)); injection.Hook = null;
            var after = await app.SnapshotAsync();
            Assert(after.OverloadVersion >= 200 && !after.Admit.Allowed && File.Exists(Path.Combine(root, "queue-burst", "overload-marker")), "Bounded rejection keeps every overload generation and durable evidence with saturated control capacity");
            Assert(store.Requests().Length == 0, "Rejected queue burst creates no external effect");
            await app.DisposeAsync();
            Assert((await app.ExecuteAsync(new(Wire.Id(), after.State.Revision, "Login", Value: "maintenance")).WaitAsync(TimeSpan.FromSeconds(1))).Status == "NotReady", "Late command after disposal completes with explicit rejection");
        }
        await using var rig = await Rig.New(root,url); await rig.OpenBox(); var op = await rig.Enter("left");
        await rig.Event("left","Read",op.AdmissionId,["000123"],1); await rig.Drain(); await rig.Command("Submit",op.Id);
        var claim=(await rig.App.ClaimAsync())!.Value;
        using(var db=new SqliteConnection($"Data Source={rig.Store.Path};Pooling=False"))
        {
            db.Open(); using var hold=db.BeginTransaction();
            var handoff=rig.App.BeforeSendAsync(claim.Request,claim.Attempt); await Task.Delay(100);
            Assert(!handoff.IsCompleted,"Real SQLite lock holds durable handoff transaction");
            var watch=Stopwatch.StartNew(); rig.App.Latch("Overload","SIMULATION overflow while handoff waits on SQLite");
            Assert(watch.Elapsed<TimeSpan.FromMilliseconds(500),"Device overload does not wait on SQLite decision lock");
            hold.Rollback(); Assert(!await handoff,"Fault during slow durable handoff suppresses network");
        }
        Assert(rig.Store.Request(claim.Request.Id)!.Status==RequestStatus.Unknown && rig.Store.Attempts(claim.Request.Id).Single().Disposition=="Unknown", "Committed handoff is conservatively Unknown, never Prepared or locally cancellable");
        Assert(await rig.Port.QueryAsync(claim.Request,CancellationToken.None) is null,"Suppressed request absent from independent receiving ledger");
        await rig.Command("CancelPrepared",claim.Request.Id,"SIMULATION transport handoff cannot prove non-delivery",expect:"RejectedByGuard");
        Console.WriteLine("PASS A01 bounded command/control saturation and concurrent overload during real SQLite handoff");
    }
    static void Assert(bool condition, string reason) { if (!condition) throw new InvalidOperationException("ASSERT A01: " + reason); }
}

using Ipc.Platform;
using Ipc.Product;
using Ipc.Supply;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

static class ProductOwnershipVerification
{
    public static async Task Run(string root, string url)
    {
        var evidence = new List<object>();
        foreach (var failure in new[] { "stop-throws", "stop-pending", "dispose-pending" })
            evidence.Add(await Exercise(root, url, failure));
        File.WriteAllText(Path.Combine(root, "review-product-ownership.json"), Wire.Encode(new { Mode = "SIMULATION", Cases = evidence, Result = "PASS", Windows = "NOT_RUN", RealSdk = "BLOCKED" }));
    }
    static async Task<object> Exercise(string root, string url, string failure)
    {
        var directory = Path.Combine(root, "product-ownership-" + failure);
        var configDirectory = Path.Combine(directory, "config"); Directory.CreateDirectory(configDirectory);
        var cfg = Rig.Config(url); var install = Path.GetDirectoryName(typeof(ProductRuntime).Assembly.Location)!;
        var d = ReferenceProduct.Definition;
        var manifest = new ProductManifest(1, d.ProductId, cfg.Mode, d.Runtime, "any", d.BusinessModule, d.Views,
            d.Modules.Select(m => new ModuleSpec(m.Id, m.Kind, m.Version, m.ContractMajor, m.Dependencies, m.File, Wire.FileHash(Path.Combine(install, m.File)))).ToArray());
        BootOptions Boot(string name, StationConfig station)
        {
            var config = Path.Combine(configDirectory, name); Directory.CreateDirectory(config);
            File.WriteAllText(Path.Combine(config, "product.json"), Wire.Encode(manifest));
            File.WriteAllText(Path.Combine(config, "station.json"), Wire.Encode(station));
            File.WriteAllText(Path.Combine(config, "presentation.json"), Wire.Encode(new PresentationSettings()));
            return new(Path.Combine(config, "product.json"), Path.Combine(config, "station.json"), Path.Combine(config, "presentation.json"), Path.Combine(directory, name + "-data"), install, Path.Combine(directory, "leases"));
        }
        var boot = Boot("owner", cfg);
        var held = new ProductProbeSource(cfg.Devices[0].DeviceId, failure);
        var independent = new ProductProbeSource(cfg.Devices[1].DeviceId, "none");
        var runtime = new ProductRuntime(boot, NullLogger<ProductRuntime>.Instance, (binding, _) => binding.Role == "left" ? held : independent);
        Exception? originalFailure = null; BusinessRequest? original = null;
        try
        {
            await runtime.StartAsync(CancellationToken.None);
            Assert(runtime.Diagnostics.Count == 0 && runtime.SourceStartAttempts == 2, "Actual Product owns both controlled source registrations");
            var app = runtime.Application!;
            await Wait(async () => (await app.SnapshotAsync()).State.IntegrationServiceHealth?.IsValid(DateTimeOffset.UtcNow) == true);
            async Task Command(string kind, string? value = null)
            {
                var snapshot = await app.SnapshotAsync();
                var receipt = await runtime.CommandAsync(new(Wire.Id(), snapshot.State.Revision, kind, Value: value));
                Assert(receipt.Status == "Persisted", kind + ": " + receipt.Status + ": " + receipt.Reason);
            }
            await Command("Login", "maintenance"); await Command("SetFixtureFault", "Drop"); await Command("OpenContext", "000001");
            await Wait(async () => (await app.SnapshotAsync()).Requests.Any(x => x.Status == RequestStatus.Unknown));
            original = (await app.SnapshotAsync()).Requests.Single();
            Assert(original.Status == RequestStatus.Unknown, "Real independent receiver accepted context but dropped its response");

            originalFailure = await Capture(() => runtime.DisposeAsync().AsTask());
            Assert(originalFailure.ToString().Contains(failure == "stop-throws" ? "SIMULATION Product source stop failure" : "TimeoutException"), "Original source exception/timeout survives Product aggregate");
            Assert(runtime.Diagnostics.Any(x => x.Contains("retained")), "Product identifies retained dependencies instead of announcing disposal success");
            Assert(independent.StopCount == 1 && independent.DisposeCount == 1 && held.DisposeCount == (failure == "dispose-pending" ? 1 : 0), "Independent cleanup completes once; source disposal cannot race a live Stop");
            Assert(held.Active || failure == "dispose-pending" && !held.Disposed, "Injected source task still owns its lifecycle after bounded disposal returns");
            if (failure != "stop-throws") Assert(held.StopCount == 1, "Pending stop task is reused, not duplicated by repeated cleanup layers");
            var resources = ProductRuntime.ConfigurationResources(cfg);
            Assert(runtime.HeldResources.SequenceEqual(resources), "Actual Product retains the entire selected device/station lease set");
            DeniedLease(runtime.DataDirectory!, ["database"]);
            foreach (var key in resources) DeniedLease(boot.LeaseRoot!, [key]);
            var leaseFiles = Directory.GetFiles(boot.LeaseRoot!, "*.lease").Select(Path.GetFileName).Order().ToArray();

            int competingFactories = 0;
            await using (var duplicate = new ProductRuntime(boot, NullLogger<ProductRuntime>.Instance, (_, _) => { competingFactories++; throw new Exception("Unreachable source factory"); }))
            {
                await duplicate.StartAsync(CancellationToken.None);
                Assert(duplicate.Application is null && duplicate.Diagnostics.Count > 0 && competingFactories == 0, "Same-data second Product cannot pass the retained data lease");
            }
            await using (var rival = new ProductRuntime(Boot("rival", cfg with { StationId = cfg.StationId + "-rival" }), NullLogger<ProductRuntime>.Instance, (_, _) => { competingFactories++; throw new Exception("Unreachable source factory"); }))
            {
                await rival.StartAsync(CancellationToken.None);
                Assert(rival.Application is null && rival.Diagnostics.Count > 0 && competingFactories == 0, "Different-data/station Product cannot pass retained device leases or create sources");
            }
            Assert(!held.EmitAfterStop(), "Live source callback is sealed at observation ingress during retained cleanup");
            // This is a legitimate late authoritative result while stopping, through the real
            // Product reconciler. It needs the retained application, live SQLite and journal.
            await runtime.ReconcileAsync(original.Id);
            var resolved = (await app.DetailsAsync(original.Id)).Single();
            Assert(resolved.Request == original with { Status = RequestStatus.Accepted } && resolved.Attempts.Length == 1 && resolved.Responses.Length == 1, "Retained store processes original late response without changing bytes/binding/attempt identity");
            var state = await app.SnapshotAsync();
            Assert(state.State.Stopping && !state.Admit.Allowed && !state.RuntimeFaults.Any(x => x.StartsWith("DiagnosticLogFailure")), "Stop barrier remains while retained app/store/journal process late evidence");
            var log = Path.Combine(runtime.DataDirectory!, "logs", "runtime.jsonl");
            await Wait(() => Task.FromResult(File.Exists(log) && File.ReadAllText(log).Contains("ReconciliationQuery") && File.ReadAllText(log).Split('\n').Any(x => x.Contains("T3Response") && x.Contains(original.Id))));
            Assert(File.ReadAllText(log).Contains("ReconciliationQuery"), "Retained journal appended post-disposal-attempt evidence instead of dropping it into a completed queue");
            using (var db = new SqliteConnection($"Data Source={Path.Combine(runtime.DataDirectory!, "station.db")};Mode=ReadOnly;Pooling=False"))
            {
                db.Open(); using var query = db.CreateCommand(); query.CommandText = "SELECT status FROM requests WHERE id=$id"; query.Parameters.AddWithValue("$id", original.Id);
                Assert((string?)query.ExecuteScalar() == "Accepted", "Original late result is actually durable in owned SQLite");
            }
            held.Release(); await runtime.DisposeAsync();
            Assert(held.DisposeCount == 1 && held.Disposed && !held.Active && independent.DisposeCount == 1 && runtime.HeldResources.Length == 0, "Late completion reaches exactly-once terminal source disposal and releases Product resource lease");
            using (var data = new ResourceLease(runtime.DataDirectory!, ["database"])) { }
            foreach (var key in resources) { using var device = new ResourceLease(boot.LeaseRoot!, [key]); }
            Assert(Directory.GetFiles(boot.LeaseRoot!, "*.lease").Select(Path.GetFileName).Order().SequenceEqual(leaseFiles), "Cleanup retains lock path identities instead of deleting/replacing files");
            _ = await Capture(async () => { await app.SnapshotAsync(); });
            await using (var restarted = new ProductRuntime(boot, NullLogger<ProductRuntime>.Instance))
            {
                await restarted.StartAsync(CancellationToken.None);
                var restored = await restarted.Application!.SnapshotAsync();
                Assert(restarted.Diagnostics.Count == 0 && restarted.SourceStartAttempts == 2 && restored.State.Actor is null && restored.Requests.Single().Id == original.Id && restored.Requests.Single().Status == RequestStatus.Accepted, "Public registered factory path restarts same store after terminal cleanup without resending original");
            }
            using var receiver = new SqliteConnection($"Data Source={Path.Combine(root, "receiver/receiver.db")};Mode=ReadOnly;Pooling=False"); receiver.Open();
            using var calls = receiver.CreateCommand(); calls.CommandText = "SELECT count(*) FROM network_calls WHERE id=$id"; calls.Parameters.AddWithValue("$id", original.Id);
            Assert(Convert.ToInt32(calls.ExecuteScalar()) == 1, "Original lost-response request observed exactly once remotely across retained cleanup/restart");
            Console.WriteLine("PASS E11 Product " + failure + ": live store/journal + data/station/device leases retained; independent cleanup; original query; late terminal release/restart");
            return new { Failure = failure, runtime.RuntimeId, cfg.StationId, DataDirectory = runtime.DataDirectory, Resources = resources, OriginalRequest = original, Error = originalFailure.ToString(), HeldStopCount = held.StopCount, HeldDisposeCount = held.DisposeCount, IndependentStopCount = independent.StopCount, IndependentDisposeCount = independent.DisposeCount, OriginalReceiverSendCount = 1, ResolvedDetail = resolved, FinalResources = runtime.HeldResources, Result = "PASS" };
        }
        finally { held.Release(); await runtime.DisposeAsync(); }
    }
    static void DeniedLease(string root, string[] resources)
    {
        try { using var lease = new ResourceLease(root, resources); }
        catch (IOException) { return; }
        throw new InvalidOperationException("Retained lease was prematurely released: " + string.Join(",", resources));
    }
    static async Task Wait(Func<Task<bool>> condition)
    {
        for (var i = 0; i < 200; i++) { if (await condition()) return; await Task.Delay(25); }
        throw new TimeoutException("Product ownership verification did not reach expected state");
    }
    static async Task<Exception> Capture(Func<Task> action)
    { try { await action(); } catch (Exception ex) { return ex; } throw new InvalidOperationException("Expected lifecycle failure did not occur"); }
    static void Assert(bool value, string reason) { if (!value) throw new InvalidOperationException("E11 Product: " + reason); }
}

sealed class ProductProbeSource(string deviceId, string failure) : IControlledObservationSource
{
    readonly TaskCompletionSource stopRelease = new(TaskCreationOptions.RunContinuationsAsynchronously), disposeRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
    Func<Observation, bool>? receive;
    bool released;
    public string DeviceId => deviceId;
    public string Epoch { get; private set; } = "not-started";
    public string ConfirmationSource => "SIMULATION:Product-ownership-probe";
    public int StopCount { get; private set; }
    public int DisposeCount { get; private set; }
    public bool Active { get; private set; }
    public bool Disposed { get; private set; }
    public Task StartControlledAsync(string epoch, Func<Observation, bool> callback, Action<DeviceHealthConfirmation> health, CancellationToken token)
    {
        Epoch = epoch; receive = callback; Active = true; var now = DateTimeOffset.UtcNow;
        health(new(DeviceId, Epoch, DeviceHealth.Ready, now, now.AddSeconds(5), ConfirmationSource)); return Task.CompletedTask;
    }
    public async Task StopAsync(CancellationToken token)
    {
        StopCount++;
        if (failure == "stop-throws" && !released) throw new InvalidOperationException("SIMULATION Product source stop failure " + deviceId);
        if (failure == "stop-pending") await stopRelease.Task;
        Active = false;
    }
    public async ValueTask DisposeAsync()
    {
        DisposeCount++; if (failure == "dispose-pending") await disposeRelease.Task;
        Disposed = true;
    }
    public bool EmitAfterStop() => receive!(new(Wire.Id(), DeviceId, Epoch, Wire.Id(), 1, Wire.Id(), "Enter", [], null, DateTimeOffset.UtcNow));
    public void Release() { released = true; stopRelease.TrySetResult(); disposeRelease.TrySetResult(); }
}

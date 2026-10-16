using Ipc.Adapters;
using Ipc.Platform;
using Ipc.Product;
using Ipc.Supply;
using Microsoft.Extensions.Logging.Abstractions;

static class FollowupVerification
{
    public static async Task Run(string root, string url, string workspace)
    {
        await HistoryIdentityMatrix(root, url);
        await EffectiveOwnership(root, url);
        await ReferenceJourneyVerification.Run(root, url);
        Console.WriteLine("PASS FOLLOWUP-08 all history identities and final configuration ownership");
    }
    static async Task HistoryIdentityMatrix(string root, string url)
    {
        var cfg = Rig.Config(url); var clock = new TestClock();
        using var store = new SupplyStore(Path.Combine(root, "followup-identity", "station.db"));
        var session = new StationSession { Barcode = "000-historical", Status = SessionStatus.Closed, ClosedAt = clock.GetUtcNow() };
        var operation = new SupplyOperation { SessionId = session.Id, Barcode = "000-item", Lane = "left", Status = OperationStatus.Cancelled, Occupancy = Occupancy.Clear };
        var alarm = new Alarm(Wire.Id(), "SyntheticHistorical", "station", "fixture", "one", null, clock.GetUtcNow(), null, null, clock.GetUtcNow());
        store.Commit(new() { Session = session }, "SyntheticHistorySeed", session.Id, "fixture", operation: operation, alarm: alarm);
        await using var app = new StationApplication(store, cfg, new SimulationRequestContract(cfg), clock: clock);
        var reads = new (string Name, Func<Task> Read)[]
        {
            ("legacy-operation", async () => { _ = await app.HistoryAsync(); }),
            ("legacy-alarm", async () => { _ = await app.AlarmHistoryAsync(); }),
            ("request-details", async () => { _ = await app.DetailsAsync(operation.Id); }),
            ("typed-session-history", async () => { _ = await app.QueryHistoryAsync(new()); }),
            ("typed-operation-history", async () => { _ = await app.QueryHistoryAsync(new(Entity: HistoryEntity.Operations)); }),
            ("session-details", async () => { _ = await app.SessionDetailsAsync(session.Id); }),
            ("config-history", async () => { _ = await app.ConfigsAsync(); }),
            ("unknown-request-history", async () => { _ = await app.UnknownRequestsAsync(); })
        };
        async Task Matrix(string identity, bool allowed)
        {
            foreach (var read in reads)
            {
                if (allowed) await read.Read();
                else await Denied(read.Read, identity + "/" + read.Name);
            }
            var current = await app.SnapshotAsync();
            Assert(current.State.Session?.Barcode == session.Barcode, "Current diagnostics remain discoverable without historical read authority");
            Console.WriteLine($"PASS FOLLOWUP-08 history identity={identity} entries={reads.Length} allowed={allowed}");
        }
        async Task Command(string kind, string? value = null)
        {
            var snapshot = await app.SnapshotAsync();
            var receipt = await app.ExecuteAsync(new(Wire.Id(), snapshot.State.Revision, kind, Value: value));
            Assert(receipt.Status == "Persisted", kind + ": " + receipt.Reason);
        }
        await Matrix("never-logged-in", false);
        await Command("Login", "operator"); await Matrix("valid-operator", true);
        Assert((await app.HistoryAsync()).Single().Barcode == operation.Barcode && (await app.AlarmHistoryAsync()).Single().Id == alarm.Id, "Valid legacy readers retain real records");
        await Command("Logout"); await Matrix("logged-out", false);
        await Command("Login", "maintenance"); await Matrix("valid-maintenance", true);
        clock.Advance(TimeSpan.FromHours(9)); await Matrix("expired", false);
    }
    static async Task EffectiveOwnership(string root, string url)
    {
        var directory = Path.Combine(root, "followup-ownership"); Directory.CreateDirectory(directory);
        var install = Path.GetDirectoryName(typeof(ProductRuntime).Assembly.Location)!;
        var manifest = new ProductManifest(1, "ipc-reference", RunMode.Simulation, "net10.0", "any", "supply", ReferenceProduct.Definition.Views,
            ReferenceProduct.Definition.Modules.Select(m => new ModuleSpec(m.Id, m.Kind, m.Version, m.ContractMajor, m.Dependencies, m.File, Wire.FileHash(Path.Combine(install, m.File)))).ToArray());
        var leases = Path.Combine(directory, "leases");
        BootOptions Boot(string name, StationConfig seed, StationConfig? effective = null, ConfigRevision? pending = null)
        {
            var config = Path.Combine(directory, name, "config"); Directory.CreateDirectory(config);
            File.WriteAllText(Path.Combine(config, "product.json"), Wire.Encode(manifest));
            File.WriteAllText(Path.Combine(config, "station.json"), Wire.Encode(seed));
            File.WriteAllText(Path.Combine(config, "presentation.json"), Wire.Encode(new PresentationSettings()));
            var data = Path.Combine(directory, name, "data");
            if (effective is not null || pending is not null)
            {
                using var store = new SupplyStore(Path.Combine(data, seed.Mode.ToString(), seed.StationId, "station.db"));
                if (effective is not null) store.Commit(new(), "SeedEffective", seed.StationId, "fixture", config: new(Wire.Id(), effective.Version, effective, "fixture", "Effective", "synthetic persisted configuration", DateTimeOffset.UtcNow));
                if (pending is not null) store.Commit(store.Load(), "SeedPending", seed.StationId, "fixture", config: pending);
            }
            return new(Path.Combine(config, "product.json"), Path.Combine(config, "station.json"), Path.Combine(config, "presentation.json"), data, install, leases);
        }
        ProductRuntime Runtime(BootOptions options) => new(options, NullLogger<ProductRuntime>.Instance);
        ConfigRevision Pending(StationConfig from, StationConfig to) => new(Wire.Id(), from.Version, to, "fixture", "PendingActivation", "synthetic staged change", DateTimeOffset.UtcNow);
        var final = Rig.Config(url); var seed = final with { Version = "redeployed-seed", Devices = final.Devices.Select(x => x with { DeviceId = "seed-" + x.DeviceId }).ToArray() };
        var effectiveBoot = Boot("effective-a-seed-b", seed, final);
        using (var manyDrafts = new SupplyStore(Path.Combine(effectiveBoot.DataRoot, seed.Mode.ToString(), seed.StationId, "station.db")))
            for (var i = 0; i < 105; i++) manyDrafts.Commit(manyDrafts.Load(), "SyntheticRejectedDraft", seed.StationId, "fixture", config: new(Wire.Id(), final.Version, seed, "fixture", "Rejected", "not authoritative", DateTimeOffset.UtcNow));
        await using (var active = Runtime(effectiveBoot))
        {
            await active.StartAsync(CancellationToken.None);
            Assert(active.Diagnostics.Count == 0 && active.Application is not null && active.Configuration?.Version == final.Version && active.SourceStartAttempts == 2, "Persisted Effective selected before source startup");
            Assert(active.HeldResources.SequenceEqual(ProductRuntime.ConfigurationResources(final)), "Actual final device and station lease set");
            Assert((await active.Application!.SnapshotAsync()).State.Devices.Values.Select(x => x.DeviceId).Order().SequenceEqual(final.Devices.Select(x => x.DeviceId).Order()), "Only final device sources registered");
            using var unused = new ResourceLease(leases, seed.Devices.Select(x => $"{seed.Mode}:device:{x.DeviceId}"));
            var other = Rig.Config(url) with { Devices = final.Devices };
            await using var competing = Runtime(Boot("different-station-competes-a", other));
            await competing.StartAsync(CancellationToken.None);
            Assert(competing.SourceStartAttempts == 0 && competing.Application is null && competing.ModuleStates.Count == 0 && competing.HeldResources.Length == 0 && competing.Diagnostics.Count > 0, "Different station/data root cannot start any source owning A");
            // Root sorts the station after devices. Whichever key sorts first, no partial
            // acquisition survives construction failure and no source is constructed.
            using var stationProbe = new ResourceLease(leases, [$"{other.Mode}:station:{other.StationId}"]);
        }
        using (var released = new ResourceLease(leases, ProductRuntime.ConfigurationResources(final))) { }
        var changed = final with { Version = "changed-devices", Devices = seed.Devices };
        await using (var activated = Runtime(Boot("pending-new-device-set", final, final, Pending(final, changed))))
        {
            await activated.StartAsync(CancellationToken.None);
            Assert(activated.Diagnostics.Count == 0 && activated.Configuration?.Version == changed.Version && activated.HeldResources.SequenceEqual(ProductRuntime.ConfigurationResources(changed)), "Pending activates only under its final device leases");
        }
        var invalid = changed with { Version = "invalid", StationId = "other-station" };
        await using (var fallback = Runtime(Boot("pending-invalid-fallback", seed, final, Pending(final, invalid))))
        {
            await fallback.StartAsync(CancellationToken.None);
            Assert(fallback.Diagnostics.Count == 0 && fallback.Configuration?.Version == final.Version && fallback.HeldResources.SequenceEqual(ProductRuntime.ConfigurationResources(final)), "Invalid pending falls back to previous Effective and leases its exact resources");
        }
        var failedBoot = Boot("pending-source-failure", seed, final, Pending(final, changed with { Devices = changed.Devices.Select(x => x with { Driver = "replay", ReplayFile = "missing.jsonl" }).ToArray() }));
        await using (var failed = Runtime(failedBoot))
        {
            await failed.StartAsync(CancellationToken.None);
            Assert(failed.SourceStartAttempts == 0 && failed.Diagnostics.Count > 0 && failed.ModuleStates.Count == 0, "Partial source creation failure starts no source");
        }
        await using (var retry = Runtime(failedBoot))
        {
            await retry.StartAsync(CancellationToken.None);
            Assert(retry.Diagnostics.Count == 0 && retry.Configuration?.Version == final.Version && retry.HeldResources.SequenceEqual(ProductRuntime.ConfigurationResources(final)), "Failed activation restart retains last Effective with matching leases");
        }
        foreach (var wrong in new[] { final with { StationId = "foreign" }, final with { Mode = RunMode.Production } })
        {
            await using var refused = Runtime(Boot("restored-identity-" + Wire.Id(), seed, wrong));
            await refused.StartAsync(CancellationToken.None);
            Assert(refused.SourceStartAttempts == 0 && refused.Diagnostics.Count > 0 && refused.Application is null && refused.ModuleStates.Count == 0 && refused.HeldResources.Length == 0, "Restored Effective station/mode mismatch refused before sources or device leases");
        }
        // Verify partial lease acquisition independently under a sorted held second key.
        using (var second = new ResourceLease(leases, ["partial-b"]))
        {
            try { using var rejected = new ResourceLease(leases, ["partial-a", "partial-b"]); throw new Exception("Lease contention admitted"); }
            catch (IOException) { using var first = new ResourceLease(leases, ["partial-a"]); }
        }
        Console.WriteLine("PASS FOLLOWUP-08 Effective/seed/pending/fallback/restored identity/competition/partial resource terminal states");
    }
    static async Task Denied(Func<Task> action, string reason)
    {
        try { await action(); } catch (GuardException ex) when (ex.Message.Contains("identity", StringComparison.OrdinalIgnoreCase)) { return; }
        throw new InvalidOperationException("Historical read escaped identity guard: " + reason);
    }
    static void Assert(bool value, string reason) { if (!value) throw new InvalidOperationException("FOLLOWUP-08: " + reason); }
}

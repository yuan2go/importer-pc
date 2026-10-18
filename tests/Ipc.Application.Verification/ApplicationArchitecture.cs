using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Application.DirectPlc;
using Ipc.Application.WcsToPlc;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Assembly;
using Ipc.Platform;
using Microsoft.Data.Sqlite;

static class ApplicationArchitecture
{
    static void Check(bool pass, string reason) { if (!pass) throw new Exception("FAIL AA22: " + reason); Console.WriteLine("PASS AA22: " + reason); }
    static async Task<ParcelSnapshot> Wait(ParcelRuntime app, Func<ParcelSnapshot, bool> predicate)
    {
        for (int i = 0; i < 250; i++) { var s = await app.SnapshotAsync().WaitAsync(TimeSpan.FromSeconds(1)); if (predicate(s)) return s; await Task.Delay(20); }
        throw new Exception("AA22 timeout: " + Wire.Encode(await app.SnapshotAsync()));
    }
    public static async Task Run(string workspace, string root)
    {
        ParcelConfiguration Config(string id) => Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace, "config", id, "business.json")));
        FlowDefinition Flow(string id) => id switch { "sportmaster" => new SportmasterFlow(), "direct-plc" => new DirectPlcFlow(), _ => new WcsToPlcFlow() };
        async Task<ParcelRuntime> Open(ParcelConfiguration c, string run, Func<ParcelCapabilityFactory, IParcelCapabilityFactory>? wrap = null)
        {
            var path = Path.Combine(root, run); Directory.CreateDirectory(path); ExtensionStore.AtomicWrite(Path.Combine(path, "business.json"), c);
            var launch = new BusinessLaunch(path, Path.Combine(path, "data"), null, null); var factory = new ParcelCapabilityFactory(launch);
            var app = new ParcelRuntime(launch, Flow(c.ApplicationId), wrap?.Invoke(factory) ?? factory);
            try { await app.StartAsync(CancellationToken.None); return app; } catch { await app.DisposeAsync(); throw; }
        }
        async Task<BusinessReceipt> Command(ParcelRuntime app, string kind, string? owner = null, string? value = null)
        { var s = await app.SnapshotAsync(); return await app.CommandAsync(new(Wire.Id(), s.Revision, kind, owner, value)); }
        CapabilityBinding B(string kind) => new(kind, "reference-" + kind, "1.0.0", kind + ".22", "reference://" + kind + "/22", "PARCEL-REFERENCE-v1", kind == "login" ? new() { ["subjects"] = "alice,bob" } : []);
        var sport = Config("sportmaster"); var topology = sport.Topology!;
        foreach (var bad in new[] {
            sport with { Topology = topology with { Readers = [topology.Readers[0], topology.Readers[0]] } },
            sport with { Topology = topology with { Positions = [topology.Positions[0], topology.Positions[1] with { ReaderDeviceId = topology.Readers[0].DeviceId }] } },
            sport with { Topology = topology with { Positions = [topology.Positions[0]] } },
            sport with { Reader = sport.Reader with { DeviceId = "wrong.anchor" } },
            sport with { Topology = topology with { Positions = [topology.Positions[0] with { CameraInstanceId = "missing" }, topology.Positions[1]] } } })
            Check(Flow("sportmaster").Validate(bad).Length > 0, "missing/duplicate/shared/misbound topology rejected before resources");
        BlockingFactory? blocked = null;
        await using (var app = await Open(sport, "dual", inner => blocked = new(inner)))
        {
            app.EmitReferenceAt("left", "left-one", "000123", "left-source"); await blocked!.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Check(app.EmitReferenceAt("right", "right-one", "000123", "right-source"), "right input enters while left WCS is slow");
            var s = await Wait(app, x => x.Operations.Any(o => o.PositionId == "right" && o.State == ParcelState.SlotResolved));
            var left = s.Operations.Single(o => o.PositionId == "left"); var right = s.Operations.Single(o => o.PositionId == "right");
            Check(left.Wcs?.State == StageState.Dispatching && right.Plc is null && right.Wcs?.Attempts == 1 && right.ReaderBinding!.DeviceId == topology.Readers[1].DeviceId,
                "real dual consumers interleave; same barcode has two distinct entities and reader bindings");
            var payload = Wire.Decode<ParcelPayload>(right.Wcs!.Request.Payload);
            Check(payload.PositionId == "right" && payload.ReaderBinding == right.ReaderBinding && payload.Acquisition == right.Acquisition, "T1 freezes original position/device/acquisition in delivered semantics");
            app.EmitReferenceAt("right", "right-one", "000123", "right-source"); await Task.Delay(80);
            Check((await app.SnapshotAsync()).Operations.Length == 2, "duplicate original does not create another request");
            await Command(app, "Pause"); await app.AuthorizeMaintenanceAsync("maintenance", "SIMULATION_ONLY");
            s = await app.SnapshotAsync(); var rightEpoch = s.Devices!.Single(x => x.DeviceId == topology.Readers[1].DeviceId).Epoch;
            Check((await app.ReconnectDeviceAsync(topology.Readers[0].DeviceId)).Status == "Reconnected", "single-side reconnect while other original remains owned");
            s = await app.SnapshotAsync(); Check(s.Devices!.Single(x => x.DeviceId == topology.Readers[1].DeviceId).Epoch == rightEpoch, "other reader epoch unchanged by single-side reconnect");
            var oldLeft = blocked.Readers![topology.Readers[0].DeviceId];
            Check(!oldLeft.EmitOld("late", "000123"), "old registration callback is fenced after reconnect");
            blocked.Release.TrySetResult(); await Wait(app, x => x.Operations.All(o => o.State == ParcelState.SlotResolved));
            Check((await Command(app, "Resume")).Status == "Persisted", "resume does not resend original stages");
            blocked.Readers[topology.Readers[0].DeviceId].Disconnect();
            s = await app.SnapshotAsync(); Check(s.Positions!.Single(p => p.Id == "right").Admit && !s.Positions!.Single(p => p.Id == "left").Admit, "reader disconnect blocks only its position");
            app.EmitReferenceAt("right", "right-two", "000123", "right-two-source"); await Wait(app, x => x.Operations.Length == 3 && x.Operations.First().State == ParcelState.SlotResolved);
            blocked.Unavailable = true; await Wait(app, x => x.Health.Any(h => h.InstanceId == sport.Wcs!.InstanceId && !h.Ready));
            Check((await app.SnapshotAsync()).Positions!.All(p => !p.Admit), "shared WCS health failure blocks both positions without rewriting originals");
            blocked.Unavailable = false; await Wait(app, x => x.Health.Any(h => h.InstanceId == sport.Wcs!.InstanceId && h.Ready));
            await Command(app, "Pause"); s = await app.SnapshotAsync();
            Check((await app.StageConfigurationAsync(s.Configuration.Revision, s.Configuration.Effective with { SchemaVersion = 2, Topology = null })).Status == "Rejected", "application API rejects schema3 flattening, beyond UI protection");
            var candidate = s.Configuration.Effective with { TimeoutMs = 1700 };
            Check((await app.StageConfigurationAsync(s.Configuration.Revision, candidate)).Status == "PendingRestart", "complete topology staged using original configuration CAS");
            Check((await app.StageConfigurationAsync(s.Configuration.Revision, candidate)).Status == "Rejected", "stale configuration CAS rejected");
            s = await app.SnapshotAsync(); Check(s.Configuration.Pending!.Topology!.Readers.Length == 2 && s.Configuration.Effective.TimeoutMs != 1700, "Pending/Effective preserve both bindings and separate activation");
            var vm = new ParcelViewModel(app, "22", "22"); await vm.RefreshAsync(); var before = Wire.Encode((await app.SnapshotAsync()).Configuration);
            await vm.LoadCapabilitiesAsync(); await vm.SaveCapabilitiesAsync();
            Check(vm.Message.Contains("拒绝保存") && Wire.Encode((await app.SnapshotAsync()).Configuration) == before, "real legacy ViewModel rejects flattening schema3, authority unchanged");
            var backup = Path.Combine(root, "dual.backup.db"); Check((await app.BackupAsync(backup)).Status == "BackupCreated", "multi-device original set backed up through actual application");
            ParcelRecovery.Restore(backup, Path.Combine(root, "dual-restored/data/Simulation", sport.StationId, sport.ApplicationId));
        }
        await using (var app = await Open(sport, "dual"))
        {
            var s = await app.SnapshotAsync(); Check(s.Configuration.Status == "Effective" && s.Configuration.Effective.TimeoutMs == 1700 && s.Operations.All(o => o.FrozenConfiguration.TimeoutMs == 1500), "restart activates full revision while old parcels retain original configuration");
        }
        await using (var app = await Open(sport, "dual-restored"))
        {
            var s = await app.SnapshotAsync();
            Check(s.Paused && s.Operations.Length == 3 && s.Operations.Select(o => o.PositionId).Distinct().Count() == 2 && s.Operations.All(o => o.Wcs?.Attempts == 1), "restored complete multi-device backup starts isolated with original position/request responsibilities");
        }
        var legacy = sport with { SchemaVersion = 2, Topology = null };
        await using (var app = await Open(legacy, "configuration-upgrade"))
        {
            await Command(app, "Pause"); await app.AuthorizeMaintenanceAsync("maintenance", "SIMULATION_ONLY"); var s = await app.SnapshotAsync();
            Check((await app.StageConfigurationAsync(s.Configuration.Revision, sport)).Status == "PendingRestart" && (await app.SnapshotAsync()).Configuration.Effective.Topology is null,
                "schema2 config is never guessed into two positions; full explicit schema3 candidate staged");
        }
        await using (var app = await Open(legacy, "configuration-upgrade"))
            Check((await app.SnapshotAsync()).Configuration.Effective.Topology?.Readers.Length == 2, "controlled restart explicitly activates schema2 to complete schema3 topology");
        foreach (var id in new[] { "direct-plc", "wcs-to-plc" })
        {
            await using var app = await Open(Config(id), "sequential-" + id);
            app.EmitReferenceAt("main", "first", "0001", "one"); var s = await Wait(app, x => x.Operations.FirstOrDefault()?.State == ParcelState.PlcAccepted); var op = s.Operations.Single();
            app.EmitReferenceAt("main", "too-early", "0001", "early"); await Task.Delay(80);
            Check((await app.SnapshotAsync()).Operations.Length == 1 && !s.Positions!.Single().Admit, id + " sequential position cannot admit next parcel on PLC acceptance alone");
            await app.ReconcileAsync(op.Id); app.EmitReferenceAt("main", "second", "0001", "two"); s = await Wait(app, x => x.Operations.Length == 2 && x.Operations.First().State == ParcelState.PlcAccepted);
            Check(s.Operations.All(x => (x.Wcs is not null) == (id == "wcs-to-plc")) && s.Operations.All(x => x.Plc?.Attempts == 1), id + " preserves own main chain/delivery object without WCS leakage or repeats");
        }
        var captureOnly = Config("direct-plc") with { Login = B("login"), Weight = new(WeightSource.Measured, WeightUnit.Kg, 3, WaitTimeoutMs: 2000), Scale = B("weight"),
            Images = new(B("camera") with { Target = Config("direct-plc").Reader.DeviceId }, null) };
        ScaleFactory? scales = null;
        await using (var app = await Open(captureOnly, "capture-only", f => scales = new(f)))
        {
            Check(!app.EmitReference("unauthenticated", "001", "none"), "configured login refuses unauthenticated admission");
            await app.LoginAsync("alice", "SIMULATION_ONLY"); app.EmitReference("image-only", "001", "acquisition-only");
            var s = await Wait(app, x => x.Operations.FirstOrDefault()?.Capture?.State == StageState.Accepted); var op = s.Operations.Single();
            Check(op.Upload is null && !op.ImageReleased && op.Image!.Acquisition == op.Acquisition && op.Plc is null, "same physical reader reuses controlled original capture; capture-only waits for this weight without uploader");
            await Command(app, "Pause"); await app.AuthorizeMaintenanceAsync("maintenance", "SIMULATION_ONLY");
            Check((await app.ReconnectDeviceAsync(captureOnly.Scale!.InstanceId)).Status == "Reconnected", "scale re-registers its own measurement epoch without changing parcel acquisition");
            Check(!scales!.Probe!.EmitOld(new(Wire.Id(), op.AdmissionId, captureOnly.Scale.Target, 2.345m, WeightUnit.Kg, 3, MeasurementQuality.Valid, true, DateTimeOffset.UtcNow)), "late old-generation scale callback cannot supply current weight");
            await Command(app, "Resume");
            Check((await app.LogoutAsync()).Status == "Rejected" && (await app.LoginAsync("bob", "SIMULATION_ONLY")).Status == "Rejected", "inherited session guard protects original unfinished operator");
            app.EmitReferenceWeight(new(Wire.Id(), op.AdmissionId, captureOnly.Scale!.Target, 0, WeightUnit.Kg, 3, MeasurementQuality.Valid, true, DateTimeOffset.UtcNow)); await Task.Delay(50);
            Check((await app.SnapshotAsync()).Operations.Single().Weight is null, "enabled scale rejects zero instead of missing-weight fallback");
            app.EmitReferenceWeight(new(Wire.Id(), op.AdmissionId, captureOnly.Scale.Target, 2.345m, WeightUnit.Kg, 3, MeasurementQuality.Valid, true, DateTimeOffset.UtcNow));
            s = await Wait(app, x => x.Operations.Single().Weight is not null);
            Check(s.Operations.Single().Operator?.Subject == "alice" && s.CurrentOperator?.Subject == "alice", "account switch cannot change admitted identity or dispatch old parcel");
            await Command(app, "ContinuePrepared", op.Id);
            s = await Wait(app, x => x.Operations.Single().State == ParcelState.PlcAccepted);
            Check(!s.Operations.Single().ImageReleased, "capture-only original retained until main completion");
            await app.ReconcileAsync(op.Id); s = await Wait(app, x => x.Operations.Single().ImageReleased);
            Check(!File.Exists(Path.Combine(root, "capture-only/data/Simulation", captureOnly.StationId, captureOnly.ApplicationId, s.Operations.Single().Image!.RelativeFile)), "capture-only terminal release is durable then deletes temporary original");
        }
        // Scope and ordering of image upload remain actual stage facts in the new topology.
        foreach (var strategy in Enum.GetValues<UploadStrategy>())
        {
            var c = Config("wcs-to-plc") with { Images = new(B("camera"), B("upload") with { Parameters = new() { ["reply-mode"] = "lose-after-commit" } }, Strategy: strategy) };
            await using var app = await Open(c, "upload-" + strategy); app.EmitReference("image", "001", "image");
            var s = await Wait(app, x => x.Operations.FirstOrDefault()?.Upload?.State == StageState.Unknown); var op = s.Operations.Single();
            Check(!op.ImageReleased && (strategy == UploadStrategy.Attached || op.Wcs is null), strategy + " unknown retains original and correct main-flow ordering");
            if (strategy == UploadStrategy.Attached) await Wait(app, x => x.Operations.Single().Plc is not null);
            await app.ReconcileAsync(op.Id); s = await Wait(app, x => x.Operations.Single().ImageReleased);
            Check(s.Operations.Single().Upload?.Attempts == 1, strategy + " original upload query never blindly resends");
        }

        var lost = sport with { Wcs = sport.Wcs! with { Parameters = new(sport.Wcs.Parameters) { ["reply-mode"] = "lose-after-commit" } } };
        await using (var app = await Open(lost, "local-unknown"))
        {
            app.EmitReferenceAt("left", "left-unknown", "001", "left"); await Wait(app, x => x.Operations.FirstOrDefault()?.State == ParcelState.WcsUnknown);
            app.EmitReferenceAt("right", "right-unknown", "001", "right"); var s = await Wait(app, x => x.Operations.Length == 2 && x.Operations.All(o => o.State == ParcelState.WcsUnknown));
            await app.ReconcileAsync(s.Operations.Single(o => o.PositionId == "right").Id); s = await app.SnapshotAsync();
            Check(s.Positions!.Single(p => p.Id == "right").Admit && !s.Positions!.Single(p => p.Id == "left").Admit, "left Unknown original does not block independently reconciled right position");
        }
        BlockingFactory? overloaded = null;
        var overloadApp = await Open(sport with { QueueCapacity = 1 }, "dual-overload", f => overloaded = new(f));
        overloadApp.EmitReferenceAt("left", "left-one", "001", "left"); await overloaded!.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Check(overloadApp.EmitReferenceAt("left", "queued", "002", "queued") && !overloadApp.EmitReferenceAt("left", "overflow", "003", "overflow"), "position queue bounds and visibly rejects overload");
        Check(overloadApp.EmitReferenceAt("right", "right", "001", "right"), "left overload does not consume right queue"); await Wait(overloadApp, x => x.Operations.Any(o => o.PositionId == "right" && o.State == ParcelState.SlotResolved));
        using (var budget = new CancellationTokenSource(50))
        {
            try { await overloadApp.StopAsync(budget.Token); throw new Exception("premature Stop"); }
            catch (OperationCanceledException) { Check(overloadApp.HasUnreleasedResources, "Stop timeout retains database/device/package ownership until both workers drain"); }
        }
        overloaded.Release.TrySetResult(); await overloadApp.StopAsync(CancellationToken.None); await overloadApp.DisposeAsync();
        Check(!overloadApp.HasUnreleasedResources, "Stop retry drains all positions before final resource release");
        await using (var app = await Open(sport, "dual-overload"))
        {
            var s = await app.SnapshotAsync(); Check(s.Positions!.Single(p => p.Id == "right").Admit && !s.Positions!.Single(p => p.Id == "left").Admit, "restart preserves durable scoped overload and original uncertainty");
        }
        BlockingFactory? partial = null;
        var failedPath = Path.Combine(root, "partial-start"); Directory.CreateDirectory(failedPath); ExtensionStore.AtomicWrite(Path.Combine(failedPath, "business.json"), sport);
        var failedLaunch = new BusinessLaunch(failedPath, Path.Combine(failedPath, "data"), null, null);
        var failedApp = new ParcelRuntime(failedLaunch, new SportmasterFlow(), partial = new(new(failedLaunch), true));
        try { await failedApp.StartAsync(CancellationToken.None); throw new Exception("second source startup unexpectedly passed"); }
        catch (IOException) { Check(failedApp.HasUnreleasedResources && partial.Readers!.Values.First().Starts == 1, "second reader startup failure retains already-started first reader and downstream resources"); }
        await failedApp.DisposeAsync(); Check(partial.Readers!.Values.All(r => r.Disposed && r.Stops > 0), "partial startup disposes every returned source before factory/database release");
        await using (var app = await Open(sport, "partial-start")) Check((await app.SnapshotAsync()).Devices!.Length == 2, "failed candidate releases leases only after cleanup; fresh owner can start");
        var constructionPath = Path.Combine(root, "partial-construction"); Directory.CreateDirectory(constructionPath); ExtensionStore.AtomicWrite(Path.Combine(constructionPath, "business.json"), sport);
        var constructionLaunch = new BusinessLaunch(constructionPath, Path.Combine(constructionPath, "data"), null, null);
        var partialFactory = new ParcelCapabilityFactory(constructionLaunch) { Boundary = point => { if (point.EndsWith(topology.Readers[1].DeviceId, StringComparison.Ordinal)) throw new IOException("Injected failure after second reader construction"); } };
        var partialApp = new ParcelRuntime(constructionLaunch, new SportmasterFlow(), partialFactory);
        try { await partialApp.StartAsync(CancellationToken.None); throw new Exception("partial construction unexpectedly succeeded"); }
        catch (IOException) { Check(partialApp.HasUnreleasedResources, "real factory partial construction retains every acquired device lease"); }
        await partialApp.DisposeAsync();
        await using (var app = await Open(sport, "partial-construction")) Check((await app.SnapshotAsync()).Devices!.Length == 2, "actual factory releases partially constructed readers before shared device lease release");
        var optional = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace, "config/optional/sportmaster/business.json")));
        await using (var app = await Open(optional, "dual-optional"))
        {
            await app.LoginAsync("alice", "SIMULATION_ONLY"); app.EmitReferenceAt("left", "left-weight", "001", "left-image"); app.EmitReferenceAt("right", "right-weight", "001", "right-image");
            var s = await Wait(app, x => x.Operations.Length == 2 && x.Operations.All(o => o.Capture?.State == StageState.Accepted));
            var left = s.Operations.Single(o => o.PositionId == "left"); var right = s.Operations.Single(o => o.PositionId == "right");
            app.EmitReferenceWeight(new(Wire.Id(), left.AdmissionId, ParcelBindings.Scale(optional, "left")!.Target, 2.345m, WeightUnit.Kg, 3, MeasurementQuality.Valid, true, DateTimeOffset.UtcNow));
            s = await Wait(app, x => x.Operations.Single(o => o.Id == left.Id).State == ParcelState.SlotResolved);
            Check(s.Operations.Single(o => o.Id == right.Id).Weight is null && s.Operations.Single(o => o.Id == right.Id).Wcs is null, "shipped dual optional config binds each original capture/measurement independently");
            app.EmitReferenceWeight(new(Wire.Id(), right.AdmissionId, ParcelBindings.Scale(optional, "right")!.Target, 3.456m, WeightUnit.Kg, 3, MeasurementQuality.Valid, true, DateTimeOffset.UtcNow));
            s = await Wait(app, x => x.Operations.All(o => o.State == ParcelState.SlotResolved && o.ImageReleased));
            Check(s.Operations.All(o => o.Upload?.Attempts == 1 && o.Image?.Acquisition == o.Acquisition && o.Operator?.Subject == "alice"), "both optional positions complete real capture/scale/upload/WCS paths with original identity");
        }
        await using (var app = await Open(captureOnly, "capture-cancel"))
        {
            await app.LoginAsync("alice", "SIMULATION_ONLY"); app.EmitReference("cancel", "001", "cancel-source"); var s = await Wait(app, x => x.Operations.FirstOrDefault()?.Capture?.State == StageState.Accepted);
            await Command(app, "Pause"); Check((await Command(app, "AbandonUnsubmitted", s.Operations.Single().Id, "SYNTHETIC inspected removal")).Status == "Persisted", "capture-only unsubmitted parcel safely cancelled through actual command guard");
            s = await Wait(app, x => x.Operations.Single().ImageReleased); Check(s.Operations.Single().Upload is null && s.Operations.Single().Plc is null, "safe cancellation releases capture-only bytes without inventing upload or main execution");
        }
        await VerifyMigration(root);


        Console.WriteLine("PASS AA22 COMPLETE");
    }
    static async Task VerifyMigration(string root)
    {
        var path = Path.Combine(root, "migration"); Directory.CreateDirectory(path);
        using (var repo = new ParcelRepository(path, "sportmaster", 2)) { }
        using (var db = new SqliteConnection("Data Source=" + Path.Combine(path, "sportmaster.db")))
        {
            db.Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "INSERT INTO parcel_receipts VALUES('pending','original','wcs-slot','request','Submit','Awaiting',NULL)"; cmd.ExecuteNonQuery();
        }
        try { using var rejected = new ParcelRepository(path, "sportmaster"); throw new Exception("migration accepted unresolved original"); }
        catch (InvalidDataException e) { Check(e.Message.Contains("LegacyUnsettled"), "database2 unresolved originals block before migration3"); }
        using (var db = new SqliteConnection("Data Source=" + Path.Combine(path, "sportmaster.db")))
        {
            db.Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT max(version) FROM platform_migrations";
            Check((long)cmd.ExecuteScalar()! == 2, "blocked upgrade preserves SQL2 identity and checksum history");
            cmd.CommandText = "UPDATE parcel_receipts SET state='NoReply'"; cmd.ExecuteNonQuery();
        }
        using (var repo = new ParcelRepository(path, "sportmaster")) { }
        using (var db = new SqliteConnection("Data Source=" + Path.Combine(path, "sportmaster.db")))
        {
            db.Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT max(version) FROM platform_migrations";
            Check((long)cmd.ExecuteScalar()! == 3, "settled database2 upgrades with appended SQL3");
        }
        await Task.CompletedTask;
    }
    sealed class BlockingFactory(ParcelCapabilityFactory inner, bool failSecondStart = false) : IParcelCapabilityFactory
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Unavailable;
        public Dictionary<string, ReaderProbe>? Readers { get; private set; }
        public ParcelConfiguration Normalize(ParcelConfiguration c) => inner.Normalize(c);
        public void Validate(ParcelConfiguration c) => inner.Validate(c);
        public ParcelPorts Create(ParcelConfiguration c)
        {
            var p = inner.Create(c); Readers = p.Readers!.ToDictionary(x => x.Key, x => new ReaderProbe(x.Value, failSecondStart && x.Key != c.Reader.DeviceId));
            return p with { Reader = Readers[c.Reader.DeviceId], Readers = Readers.ToDictionary(x => x.Key, x => (IControlledObservationSource)x.Value), Wcs = new LeftSlow(p.Wcs!, Entered, Release, () => Unavailable) };
        }
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
    sealed class LeftSlow(IWcsPort inner, TaskCompletionSource entered, TaskCompletionSource release, Func<bool> unavailable) : IWcsPort
    {
        public string InstanceId => inner.InstanceId;
        public Task StartAsync(CancellationToken t) => inner.StartAsync(t);
        public Task<CapabilityHealth> HealthAsync(CancellationToken t) => unavailable() ? Task.FromResult(new CapabilityHealth(InstanceId, false, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "TEST shared unavailable")) : inner.HealthAsync(t);
        public Task ReconnectAsync(CancellationToken t) => inner.ReconnectAsync(t);
        public async Task<StageReply> ResolveSlotAsync(FrozenRequest r, CancellationToken t)
        { if (r.AdmissionId == "left-one") { entered.TrySetResult(); await release.Task; } return await inner.ResolveSlotAsync(r, t); }
        public Task<StageReply?> QueryAsync(FrozenRequest r, CancellationToken t) => inner.QueryAsync(r, t);
        public Task StopAsync(CancellationToken t) => inner.StopAsync(t);
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
    sealed class ScaleFactory(ParcelCapabilityFactory inner) : IParcelCapabilityFactory
    {
        public WeightProbe? Probe;
        public ParcelConfiguration Normalize(ParcelConfiguration c) => inner.Normalize(c);
        public void Validate(ParcelConfiguration c) => inner.Validate(c);
        public CapabilityRequirements Requirements(CapabilityBinding b) => inner.Requirements(b);
        public ParcelPorts Create(ParcelConfiguration c)
        {
            var p = inner.Create(c); Probe = new((IControlledWeightSource)p.Weight!);
            return p with { Weight = Probe, Devices = p.Devices!.ToDictionary(x => x.Key, x => x.Key == Probe.InstanceId ? Probe : x.Value) };
        }
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
    sealed class WeightProbe(IControlledWeightSource inner) : IControlledWeightSource, ISyntheticWeightInput
    {
        Func<WeightObservation, bool>? callback, old;
        string? epoch, oldEpoch;
        public string InstanceId => inner.InstanceId;
        public Task StartAsync(CancellationToken t) => inner.StartAsync(t);
        public Task<CapabilityHealth> HealthAsync(CancellationToken t) => inner.HealthAsync(t);
        public Task ReconnectAsync(CancellationToken t) => inner.ReconnectAsync(t);
        public Task StartMeasurementsAsync(Func<WeightMeasurement, bool> r, CancellationToken t) => inner.StartMeasurementsAsync(r, t);
        public Task StartControlledMeasurementsAsync(string e, Func<WeightObservation, bool> r, CancellationToken t)
        { callback = r; epoch = e; return inner.StartControlledMeasurementsAsync(e, r, t); }
        public bool Emit(WeightMeasurement m) => ((ISyntheticWeightInput)inner).Emit(m);
        public bool EmitOld(WeightMeasurement m) => old?.Invoke(new(oldEpoch!, 999, m)) == true;
        public Task StopAsync(CancellationToken t) { old = callback; oldEpoch = epoch; return inner.StopAsync(t); }
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
    sealed class ReaderProbe(IControlledObservationSource inner, bool failStart = false) : IControlledObservationSource, ISyntheticObservationInput
    {
        Func<Observation, bool>? receive, old;
        Action<DeviceHealthConfirmation>? health;
        string? oldEpoch;
        public int Starts, Stops;
        public bool Disposed;
        public string DeviceId => inner.DeviceId;
        public string Epoch => inner.Epoch;
        public string ConfirmationSource => inner.ConfirmationSource;
        public Task StartAsync(Func<Observation, bool> r, Action<string, string, DeviceHealth> h, CancellationToken t) => throw new NotSupportedException();
        public Task StartControlledAsync(string e, Func<Observation, bool> r, Action<DeviceHealthConfirmation> h, CancellationToken t)
        { receive = r; health = h; Starts++; if (failStart) throw new IOException("Injected second-reader start failure"); return inner.StartControlledAsync(e, r, h, t); }
        public bool Emit(string kind, string admissionId, string[]? codes = null, int? quantity = null, string? sourceId = null) => ((ISyntheticObservationInput)inner).Emit(kind, admissionId, codes ?? [], quantity, sourceId ?? Wire.Id());
        public bool EmitOld(string admission, string code) => old?.Invoke(new(Wire.Id(), DeviceId, oldEpoch!, Wire.Id(), 999, admission, "Read", [code], null, DateTimeOffset.UtcNow)) == true;
        public void Disconnect() => health!(new(DeviceId, Epoch, DeviceHealth.Disconnected, DateTimeOffset.UtcNow, null, "TEST:disconnect"));
        public async Task StopAsync(CancellationToken t) { Stops++; old = receive; oldEpoch = Epoch; await inner.StopAsync(t); }
        public ValueTask DisposeAsync() { Disposed = true; return inner.DisposeAsync(); }
    }
}

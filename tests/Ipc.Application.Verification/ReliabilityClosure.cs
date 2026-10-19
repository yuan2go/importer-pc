using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Application.DirectPlc;
using Ipc.Application.WcsToPlc;
using Ipc.Assembly;
using Ipc.Platform;

static class ReliabilityClosure
{
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); Console.WriteLine("PASS AC20: " + message); }
    static async Task<ParcelSnapshot> Wait(ParcelRuntime app, Func<ParcelSnapshot, bool> predicate)
    {
        for (var i = 0; i < 200; i++) { var s = await app.SnapshotAsync().WaitAsync(TimeSpan.FromSeconds(1)); if (predicate(s)) return s; await Task.Delay(20); }
        throw new Exception("Timeout: " + Wire.Encode(await app.SnapshotAsync()));
    }
    public static async Task Run(string workspace, string root)
    {
        List<string> failures = [];
        async Task Case(string name, Func<Task> run) { try { await run(); } catch (Exception e) { failures.Add(name + ": " + e.Message); Console.WriteLine("FAIL AC20 " + name + ": " + e.Message); } }
        ParcelConfiguration Config(string id) => Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace, "config", id, "business.json"))) with { SchemaVersion = 2, Topology = null };
        CapabilityBinding B(string kind, string decision = "accept", string mode = "normal") => new(kind, "reference-" + kind, "1.0.0", kind + ".one", "reference://" + kind + "/reliability20", "PARCEL-REFERENCE-v1", kind is "camera" or "upload" ? new() { ["decision"] = decision, ["reply-mode"] = mode } : []);
        async Task<(ParcelRuntime App, TestFactory Factory)> Open(ParcelConfiguration c, string name, bool blockCamera = false, bool blockWcs = false)
        {
            var path = Path.Combine(root, name); Directory.CreateDirectory(path); ExtensionStore.AtomicWrite(Path.Combine(path, "business.json"), c);
            var launch = new BusinessLaunch(path, Path.Combine(path, "data"), null, null);
            var f = new TestFactory(new(launch), blockCamera, blockWcs);
            var app = c.ApplicationId switch { "sportmaster" => SportmasterApplication.Create(launch, f), "direct-plc" => DirectPlcApplication.Create(launch, f), _ => WcsToPlcApplication.Create(launch, f) };
            try { await app.StartAsync(CancellationToken.None); return (app, f); } catch { await app.DisposeAsync(); throw; }
        }
        async Task<BusinessReceipt> Command(ParcelRuntime app, string kind, string? owner = null)
        { var s = await app.SnapshotAsync(); return await app.CommandAsync(new(Wire.Id(), s.Revision, kind, owner, "SIMULATION inspected and removed")); }
        foreach (var id in new[] { "sportmaster", "direct-plc", "wcs-to-plc" })
        {
            await Case(id + " capture unknown deadline", async () =>
            {
                var c = Config(id) with { Weight = new(WeightSource.Measured, WeightUnit.Kg, 3, WaitTimeoutMs: 200), Scale = B("weight"), Images = new(B("camera", mode: "lose-after-commit"), B("upload"), Strategy: UploadStrategy.Attached) };
                var (app, _) = await Open(c, id + "-unknown"); await using var owned = app;
                app.EmitReference("original", "001", Wire.Id()); var s = await Wait(app, x => x.Operations.FirstOrDefault()?.Capture?.State == StageState.Unknown);
                Check(!s.Availability!.Admit, id + " capture unknown cannot hide waiting weight or admit next parcel");
                s = await Wait(app, x => x.Operations.Single().State == ParcelState.WeightTimedOut);
                await app.ReconcileAsync(s.Operations.Single().Id); s = await app.SnapshotAsync();
                Check(s.Operations.Single().State == ParcelState.WeightTimedOut && !s.Availability!.Resume, id + " capture query cannot revive expired weight");
            });
            foreach (var source in new[] { WeightSource.Disabled, WeightSource.Measured })
                await Case(id + " rejected capture " + source, async () =>
                {
                    var c = Config(id) with { Weight = new(source, WeightUnit.Kg, 3), Scale = source == WeightSource.Measured ? B("weight") : null, Images = new(B("camera", "reject"), B("upload"), Strategy: UploadStrategy.Attached) };
                    var (app, _) = await Open(c, id + "-rejected-" + source); await using var owned = app;
                    app.EmitReference("rejected", "002", Wire.Id()); await Wait(app, x => x.Operations.FirstOrDefault()?.Capture?.State == StageState.Rejected);
                    if (c.Scale is not null) app.EmitReferenceWeight(new(Wire.Id(), "rejected", c.Scale.Target, 1.234m, WeightUnit.Kg, 3, MeasurementQuality.Valid, true, DateTimeOffset.UtcNow));
                    await Task.Delay(120); var op = (await app.SnapshotAsync()).Operations.Single();
                    Check(op.State == ParcelState.ImageRejected && op.Wcs is null && op.Plc is null && op.Weight is null, id + " rejected capture remains terminal for both disabled and measured weight");
                });
        }
        await Case("cancel no image and restart", async () =>
        {
            var c = Config("sportmaster") with { Images = new(B("camera"), B("upload"), MaximumPending: 1) };
            var (app, f) = await Open(c, "cancel-empty", true);
            try
            {
                f.Camera!.Block = true; app.EmitReference("cancelled", "003", Wire.Id()); await f.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
                await Command(app, "Pause"); f.Release.TrySetResult();
                var s = await Wait(app, x => x.Operations.FirstOrDefault()?.Capture is { State: StageState.Prepared, Attempts: 0 } && x.Availability!.AbandonUnsubmitted.Contains(x.Operations.Single().Id));
                Check(s.Availability!.Resume, "full current image reservation does not deadlock Resume needed to finish existing work");
                await Command(app, "AbandonUnsubmitted", s.Operations.Single().Id);
                s = await Wait(app, x => x.Operations.Single().ImageReleased);
                Check(s.Operations.Single().Image is null && s.Operations.Single().Capture!.State == StageState.Cancelled, "no-image safe cancellation durably releases reservation");
            }
            finally { f.Release.TrySetResult(); await app.DisposeAsync(); }
            var (restarted, _) = await Open(c, "cancel-empty"); await using var ownRestart = restarted;
            Check((await Command(restarted, "Resume")).Status == "Persisted", "cancelled empty capture quota remains released after restart");
            restarted.EmitReference("new-parcel", "003", Wire.Id()); await Wait(restarted, x => x.Operations.Length == 2);
        });
        await Case("actual ViewModel slow reconciliation", async () =>
        {
            var c = Config("wcs-to-plc"); c = c with { Wcs = c.Wcs! with { Parameters = new(c.Wcs.Parameters) { ["reply-mode"] = "lose-after-commit" } } };
            var (app, f) = await Open(c, "viewmodel", blockWcs: true); await using var owned = app;
            app.EmitReference("vm-original", "004", Wire.Id()); await Wait(app, x => x.Operations.FirstOrDefault()?.Wcs?.State == StageState.Unknown);
            var vm = new Ipc.Application.ParcelFlow.Desktop.ParcelViewModel(app, "test", "test"); await vm.RefreshAsync();
            f.Wcs!.BlockQuery = true;
            var slow = vm.RunAsync(vm.ReconcileAsync); await f.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            try
            {
                Check(vm.Busy, "actual ViewModel enters busy during original query");
                var ordinaryRan = false; await vm.RunAsync(() => { ordinaryRan = true; return Task.CompletedTask; });
                Check(!ordinaryRan, "ordinary ViewModel writes remain mutually exclusive");
                await vm.PauseAsync().WaitAsync(TimeSpan.FromMilliseconds(500)); await vm.RefreshAsync().WaitAsync(TimeSpan.FromMilliseconds(500));
                Check(vm.Snapshot!.Paused && vm.Busy && !slow.IsCompleted, "software Pause and read refresh complete while actual ViewModel query remains busy");
            }
            finally { f.Release.TrySetResult(); await slow; }
            await vm.AuthorizeMaintenanceAsync("SIMULATION_ONLY"); await vm.RefreshAsync(); await vm.LoadCapabilitiesAsync();
            var reader = vm.CapabilityDrafts.Single(x => x.Kind == "observation");
            Check(reader.IsTargetReadOnly, "stable reader target is explicitly read-only in the actual draft");
            reader.Target = "changed-device";
            try { await vm.SaveCapabilitiesAsync(); throw new Exception("Reader identity silently accepted"); }
            catch (InvalidOperationException ex) { Check(ex.Message.Contains("DeviceId"), "tampered reader target is explicitly rejected instead of silently discarded: " + ex.Message); }
            reader.Target = c.Reader.DeviceId; vm.ConfigurationReason = "Synthetic parameter maintenance after original reconciliation"; vm.DraftWaitMs += 1; await vm.SaveCapabilitiesAsync(); await vm.RefreshAsync();
            Check(vm.Snapshot!.Configuration.Status == "PendingRestart" && vm.Snapshot.Configuration.Effective.Weight.WaitTimeoutMs == c.Weight.WaitTimeoutMs, "actual ViewModel parameter save preserves Effective and creates Pending");
        });
        foreach (var validity in new[] { WeightValidity.UntilDispatch, WeightValidity.AtAssociation })
            await Case("weight original validity " + validity, async () =>
            {
                var c = Config("wcs-to-plc") with { SchemaVersion = validity == WeightValidity.UntilDispatch ? 1 : 2, WeightValidity = validity == WeightValidity.UntilDispatch ? null : validity, Weight = new(WeightSource.Measured, WeightUnit.Kg, 3, MaximumAgeMs: 100), Scale = B("weight") };
                var (app, f) = await Open(c, "validity-" + validity, blockWcs: true); await using var owned = app;
                f.Wcs!.BlockSubmit = true; app.EmitReference("original", "005", Wire.Id()); await Wait(app, x => x.Operations.Length == 1);
                app.EmitReferenceWeight(new(Wire.Id(), "original", c.Scale!.Target, 1.234m, WeightUnit.Kg, 3, MeasurementQuality.Valid, true, DateTimeOffset.UtcNow));
                await f.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
                try { await Command(app, "Pause"); await Task.Delay(180); } finally { f.Release.TrySetResult(); }
                var s = await Wait(app, x => x.Operations.Single().Plc is { State: StageState.Prepared, Attempts: 0 }); var op = s.Operations.Single();
                Check(op.Wcs!.State == StageState.Accepted && op.Slot is not null, "original WCS acceptance and downstream intent remain atomic while paused");
                if (validity == WeightValidity.UntilDispatch)
                {
                    Check(!s.Availability!.Resume && s.Availability.AbandonUnsubmitted.Contains(op.Id), "continuous freshness expiration exposes legal inspected disposition");
                    await Command(app, "AbandonUnsubmitted", op.Id); s = await app.SnapshotAsync(); var disposed = s.Operations.Single();
                    Check(disposed.State == ParcelState.Abandoned && disposed.Plc!.State == StageState.Cancelled && disposed.Plc.Attempts == 0 &&
                        Wire.Encode(disposed.Wcs) == Wire.Encode(op.Wcs) && Wire.Encode(disposed.Plc.Request) == Wire.Encode(op.Plc!.Request), "disposition preserves accepted WCS, original slot/payload and cancels only unsent PLC");
                }
                Check((await Command(app, "Resume")).Status == "Persisted", "resolved or association-valid original permits explicit Resume");
                if (validity == WeightValidity.AtAssociation)
                {
                    await Command(app, "ContinuePrepared", op.Id); s = await Wait(app, x => x.Operations.Single().State == ParcelState.PlcAccepted);
                    Check(s.Operations.Single().Weight!.MeasurementId == op.Weight!.MeasurementId && s.Operations.Single().Plc!.Attempts == 1, "reference association-valid policy continues immutable original measurement without reweighing");
                }
            });
        await Case("legacy upgrade preserves unresolved originals", async () =>
        {
            var c = Config("direct-plc") with { SchemaVersion = 1, WeightValidity = null, Weight = new(WeightSource.Measured, WeightUnit.Kg, 3), Scale = B("weight") };
            var (app, _) = await Open(c, "legacy-upgrade");
            app.EmitReference("legacy", "006", Wire.Id()); await Wait(app, x => x.Operations.Length == 1); await app.DisposeAsync();
            var file = Path.Combine(root, "legacy-upgrade/data/Simulation", c.StationId, "direct-plc/direct-plc.db");
            string originalBody, originalChecksum;
            using (var db = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + file))
            {
                db.Open(); using var command = db.CreateCommand(); command.CommandText = "DROP TABLE IF EXISTS parcel_commercial_semantics; DROP TABLE parcel_history_index; DROP TABLE parcel_archives; DROP TABLE parcel_reply_identities; DROP TABLE parcel_archived_receipts; DROP TABLE parcel_overload_ack; DROP TABLE parcel_identity_conflicts; DROP TABLE parcel_configuration_audit; DROP INDEX parcel_receipts_pending; DROP INDEX parcel_receipts_operation; DROP TABLE parcel_topology_semantics; DROP TABLE parcel_semantics; DELETE FROM platform_migrations WHERE version>=2"; command.ExecuteNonQuery();
                command.CommandText = "SELECT body FROM parcel_operations"; originalBody = (string)command.ExecuteScalar()!;
                command.CommandText = "SELECT sha FROM platform_migrations WHERE version=1"; originalChecksum = (string)command.ExecuteScalar()!;
            }
            try { var (unexpected, _) = await Open(c, "legacy-upgrade"); await unexpected.DisposeAsync(); throw new Exception("Unsettled legacy root was upgraded"); }
            catch (InvalidDataException ex) { Check(ex.Message.Contains("LegacyUnsettled"), "unsettled schema1 refuses upgrade before losing access by the matching old host"); }
            using (var db = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + file))
            {
                db.Open(); using var command = db.CreateCommand(); command.CommandText = "SELECT max(version) FROM platform_migrations";
                Check((long)command.ExecuteScalar()! == 1, "rejected upgrade preserves original schema1 reader access");
                command.CommandText = "SELECT body FROM parcel_operations"; Check((string)command.ExecuteScalar()! == originalBody, "rejected upgrade leaves original operation bytes intact");
                command.CommandText = "SELECT sha FROM platform_migrations WHERE version=1"; Check((string)command.ExecuteScalar()! == originalChecksum, "original SQL1 checksum remains intact");
                command.CommandText = "DELETE FROM parcel_operations"; command.ExecuteNonQuery(); // Synthetic fixture only: represent an empty legacy root, never a recovery procedure.
            }
            using var upgraded = new ParcelRepository(Path.GetDirectoryName(file)!, "direct-plc");
            Check(upgraded.Load().Length == 0, "settled legacy root accepts additive migration2/3/4 without changing migration1");
        });
        await Case("Application delivery capability compatibility", () =>
        {
            var c = Config("direct-plc");
            foreach (var requirement in new[] { new CapabilityRequirements(Query: true, PhysicalConfirmation: false), new CapabilityRequirements(PhysicalConfirmation: true), new CapabilityRequirements(Query: true, PhysicalConfirmation: true, BusinessAcceptance: false) })
            {
                try { ParcelCapabilityFactory.ValidateRequirements(c, requirement, "plc"); throw new Exception("Unfinishable delivery accepted"); }
                catch (InvalidDataException) { Check(true, "PLC delivery rejects missing business acceptance/physical completion/query path"); }
            }
            ParcelCapabilityFactory.ValidateRequirements(c, new(PhysicalConfirmation: true, CompletesOnSubmit: true), "plc");
            Check(true, "physical completion on submit may satisfy delivery without inventing query support");
            return Task.CompletedTask;
        });
        await Case("qualification signatures and prerequisites", () => { QualificationVerification.Run(root); return Task.CompletedTask; });
        if (failures.Count > 0) throw new Exception("AC20 failures: " + string.Join("\n", failures));
    }
    sealed class TestFactory(ParcelCapabilityFactory inner, bool blockCamera, bool blockWcs) : IParcelCapabilityFactory
    {
        public CameraBlock? Camera;
        public WcsBlock? Wcs;
        public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously), Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ParcelConfiguration Normalize(ParcelConfiguration c) => inner.Normalize(c);
        public void Validate(ParcelConfiguration c) => inner.Validate(c);
        public CapabilityCatalog Catalog() => inner.Catalog();
        public CapabilityRequirements Requirements(CapabilityBinding b) => inner.Requirements(b);
        public ParcelPorts Create(ParcelConfiguration c)
        { var p = inner.Create(c); if (blockCamera) p = p with { Camera = Camera = new(p.Camera!, Entered, Release) }; if (blockWcs) p = p with { Wcs = Wcs = new(p.Wcs!, Entered, Release) }; return p; }
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
    sealed class WcsBlock(IWcsPort inner, TaskCompletionSource entered, TaskCompletionSource release) : IWcsPort
    {
        public bool BlockSubmit, BlockQuery;
        public string InstanceId => inner.InstanceId;
        public Task StartAsync(CancellationToken t) => inner.StartAsync(t);
        public Task<CapabilityHealth> HealthAsync(CancellationToken t) => inner.HealthAsync(t);
        public Task ReconnectAsync(CancellationToken t) => inner.ReconnectAsync(t);
        public async Task<StageReply> ResolveSlotAsync(FrozenRequest r, CancellationToken t) { if (BlockSubmit) { entered.TrySetResult(); await release.Task; } return await inner.ResolveSlotAsync(r, t); }
        public async Task<StageReply?> QueryAsync(FrozenRequest r, CancellationToken t) { if (BlockQuery) { entered.TrySetResult(); await release.Task; } return await inner.QueryAsync(r, t); }
        public Task StopAsync(CancellationToken t) => inner.StopAsync(t);
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
    sealed class CameraBlock(IImageCamera inner, TaskCompletionSource entered, TaskCompletionSource release) : IImageCamera
    {
        public bool Block;
        public string InstanceId => inner.InstanceId;
        public Task StartAsync(CancellationToken t) => inner.StartAsync(t);
        public async Task<CapabilityHealth> HealthAsync(CancellationToken t) { if (Block) { entered.TrySetResult(); await release.Task; } return await inner.HealthAsync(t); }
        public Task ReconnectAsync(CancellationToken t) => inner.ReconnectAsync(t);
        public Task<StageReply> CaptureAsync(FrozenRequest r, CancellationToken t) => inner.CaptureAsync(r, t);
        public Task<StageReply?> QueryAsync(FrozenRequest r, CancellationToken t) => inner.QueryAsync(r, t);
        public Task StopAsync(CancellationToken t) => inner.StopAsync(t);
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}

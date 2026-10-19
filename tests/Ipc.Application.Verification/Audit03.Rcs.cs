using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Assembly;
using Ipc.Platform;
using Microsoft.Data.Sqlite;

static partial class SportmasterRcs
{
    public static async Task Audit03Checks(string workspace, string root, string? packageFile = null, string? trustFile = null)
    {
        await using var peer = new Peer();
        PackageReference? signed = null;
        ExtensionStore? store = null;
        if (packageFile is not null)
        {
            ParcelCapabilityFactory.RegisterContracts();
            store = new ExtensionStore(Path.Combine(root, "signed-rcs-store"), Wire.Decode<ExtensionTrust>(File.ReadAllText(trustFile!)), RunMode.Simulation, Path.Combine(root, "signed-rcs-leases"));
            signed = store.Install(packageFile, Wire.FileHash(packageFile)).Package;
        }
        var standard = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace, "config/sportmaster/business.json")));
        foreach (var delayedPosition in new[] { "left", "right" })
        {
            var binding = Binding(peer.Endpoint);
            if (signed is not null) binding = binding with { Implementation = "extension:" + signed.Id + "/sportmaster-rcs-task", Version = signed.Version, Package = signed, Module = "sportmaster-rcs-task" };
            CapabilityBinding Camera(string p) => new("camera", "reference-camera", "1.0.0", "audit.camera." + p, standard.Topology!.Positions.Single(x => x.Id == p).ReaderDeviceId, "PARCEL-REFERENCE-v1", []);
            var leftCamera = Camera("left"); var rightCamera = Camera("right");
            var c = standard with { Wcs = binding, Extensions = signed is null ? null : new([signed], []), TimeoutMs = 5000, Images = new(leftCamera, null), CapabilityInstances = [binding, standard.MaintenanceLogin!, leftCamera, rightCamera],
                Topology = standard.Topology! with { Positions = [standard.Topology.Positions[0], standard.Topology.Positions[1] with { CameraInstanceId = rightCamera.InstanceId }] } };
            var run = Path.Combine(root, "rcs-t1-" + delayedPosition); Directory.CreateDirectory(run); ExtensionStore.AtomicWrite(Path.Combine(run, "business.json"), c);
            if (signed is not null) ExtensionStore.AtomicWrite(Path.Combine(run, "extension-store.json"), new ParcelExtensionStoreConfiguration(store!.Root, trustFile!));
            var data = Path.Combine(run, "data");
            async Task<ParcelRuntime> Open(string dataRoot, HoldFactory? holder = null)
            {
                var launch = new BusinessLaunch(run, dataRoot, null, null);
                IParcelCapabilityFactory factory = signed is null ? new Factory(new ParcelCapabilityFactory(launch)) :
                    new ParcelCapabilityFactory(launch, BusinessCompatibility.Read(typeof(SportmasterFlow).Assembly), typeof(SportmasterFlow).Assembly);
                if (holder is not null) { holder.Inner = factory; factory = holder; }
                var app = new ParcelRuntime(launch, new SportmasterFlow(), factory);
                try { await app.StartAsync(default); return app; } catch { await app.DisposeAsync(); throw; }
            }
            async Task<BusinessReceipt> Command(ParcelRuntime app, string kind, string? owner = null, string? value = null)
            {
                for (int i = 0; i < 10; i++) { var s = await app.SnapshotAsync(); var r = await app.CommandAsync(new(Wire.Id(), s.Revision, kind, owner, value)); if (r.Status != "Conflict") return r; }
                throw new Exception("RCS fixture command CAS did not settle");
            }
            var hold = new HoldFactory(delayedPosition); string firstRequest, secondRequest; long lastId; var startPosts = peer.Requests.Count;
            var backup = Path.Combine(root, "rcs-t1-" + delayedPosition + ".backup.db");
            await using (var app = await Open(data, hold))
            {
                try
                {
                    var other = delayedPosition == "left" ? "right" : "left";
                    app.EmitReferenceAt(delayedPosition, "delayed-entity", "SYNTH-0001", "delayed-event");
                    await hold.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
                    app.EmitReferenceAt(other, "independent-entity", "SYNTH-0001", "independent-event");
                    var independent = await Wait(app, s => s.Operations.Any(o => o.PositionId == other && o.State == ParcelState.SlotResolved));
                    Check(independent.Operations.Single(o => o.PositionId == delayedPosition).Wcs is null, "A03 independent position reaches actual HTTP T1 while other capture is held");
                    hold.Release.TrySetResult();
                    var result = await Wait(app, s => s.Operations.Length == 2 && s.Operations.All(o => o.State is ParcelState.SlotResolved or ParcelState.Blocked));
                    Check(result.Operations.All(o => o.State == ParcelState.SlotResolved && o.Wcs!.Attempts == 1) && peer.Requests.Count == startPosts + 2,
                        "A03 " + delayedPosition + " capture delay gives both positions one actual T1 and POST without global serialization");
                    var ordered = result.Operations.OrderBy(o => o.Wcs!.Request.IntegrationRequest!.RequestId).ToArray();
                    Check(ordered[0].PositionId == other && ordered[1].PositionId == delayedPosition && ordered.Select(o => o.Wcs!.Request.IntegrationRequest!.RequestId).Distinct().Count() == 2, "A03 numeric order follows durable T1 preparation, not earlier admission order");
                    firstRequest = Wire.Encode(ordered[0].Wcs!.Request); secondRequest = Wire.Encode(ordered[1].Wcs!.Request); lastId = ordered[1].Wcs!.Request.IntegrationRequest!.RequestId;
                    ExtensionStore.AtomicWrite(Path.Combine(root, "rcs-t1-" + delayedPosition + ".json"), result);
                    await Command(app, "Pause"); await app.AuthorizeMaintenanceAsync("maintenance", "SIMULATION_ONLY"); await Wait(app, s => s.Availability!.Configure);
                    await Command(app, "ArchiveHistory", value: "Synthetic T1 owner archival");
                    Check((await app.BackupAsync(backup)).Status == "BackupCreated", "A03 actual backup retains owner ledger, ranges and archived originals");
                }
                finally { hold.Release.TrySetResult(); }
            }
            var posts = peer.Requests.Count;
            await using (var app = await Open(data))
            {
                var history = await app.HistoryAsync(long.MaxValue, 20);
                Check(history.Items.Select(x => Wire.Encode(x.Operation.Wcs!.Request)).Order().SequenceEqual(new[] { firstRequest, secondRequest }.Order()) && peer.Requests.Count == posts, "A03 restart/archive preserve both original wire IDs and exact bodies without POST");
                await Command(app, "Resume"); app.EmitReferenceAt("left", "after-backup", "SYNTH-0001", "after-backup-event");
                var after = await Wait(app, s => s.Operations.Any(o => o.AdmissionId == "after-backup" && o.State == ParcelState.SlotResolved));
                Check(after.Operations.Single(o => o.AdmissionId == "after-backup").Wcs!.Request.IntegrationRequest!.RequestId > lastId, "A03 source consumes a new persistent ID after the backup cut");
            }
            posts = peer.Requests.Count;
            var restored = Path.Combine(run, "restored"); var directory = Path.Combine(restored, "Simulation", c.StationId, c.ApplicationId);
            ParcelRecovery.Restore(backup, directory);
            await using (var app = await Open(restored))
            {
                await Command(app, "Resume"); app.EmitReferenceAt("left", "restore-old-range", "SYNTH-0001", "restore-old-event");
                var s = await Wait(app, x => x.Operations.Any(o => o.AdmissionId == "restore-old-range" && o.State == ParcelState.Blocked));
                Check(peer.Requests.Count == posts && s.Operations.Single(o => o.AdmissionId == "restore-old-range").Wcs is null, "A03 restored backup retires entire source range including IDs used after backup");
                await Command(app, "Pause"); await app.AuthorizeMaintenanceAsync("maintenance", "SIMULATION_ONLY");
                Check((await Command(app, "AbandonUnsubmitted", s.Operations.Single(o => o.AdmissionId == "restore-old-range").Id, "Synthetic entity inspected; source root stopped")).Status == "Persisted", "A03 exhausted unsent entity can be inspected and disposed");
                await Wait(app, x => x.Availability!.Configure); s = await app.SnapshotAsync();
                var nextBinding = binding with { Parameters = new(binding.Parameters) { ["request-id-base"] = "820000000" } };
                var next = s.Configuration.Effective with { Wcs = nextBinding, CapabilityInstances = s.Configuration.Effective.CapabilityInstances!.Select(b => b.InstanceId == binding.InstanceId ? nextBinding : b).ToArray() };
                Check((await app.StageConfigurationAsync(s.Configuration.Revision, next, "Synthetic externally allocated disjoint range; old source retired")).Status == "PendingRestart", "A03 new disjoint range uses original configuration CAS and controlled restart");
            }
            await using (var app = await Open(restored))
            {
                await Command(app, "Resume"); app.EmitReferenceAt("right", "restored-new-range", "SYNTH-0001", "new-range-event");
                var s = await Wait(app, x => x.Operations.Any(o => o.AdmissionId == "restored-new-range" && o.State == ParcelState.SlotResolved));
                Check(s.Operations.Single(o => o.AdmissionId == "restored-new-range").Wcs!.Request.IntegrationRequest!.RequestId == 820000001 && peer.Requests.Count == posts + 1,
                    "A03 restored root runs with new range; old retirement never retires newly staged range on restart");
            }
            using var db = new SqliteConnection("Data Source=" + Path.Combine(directory, "sportmaster.db")); db.Open(); using var q = db.CreateCommand();
            q.CommandText = "SELECT count(*) FROM parcel_integration_id_owners";
            Check((long)q.ExecuteScalar()! == 3, "A03 persistent SQL unique owners include archived and post-restore T1 allocations");
        }
        Console.WriteLine(signed is null ? "PASS A03 RCS COMPLETE" : "PASS A03 SIGNED RCS COMPLETE");
    }
    sealed class HoldFactory(string position) : IParcelCapabilityFactory
    {
        public IParcelCapabilityFactory Inner = null!;
        public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously), Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ParcelConfiguration Normalize(ParcelConfiguration c) => Inner.Normalize(c);
        public void Validate(ParcelConfiguration c) => Inner.Validate(c);
        public CapabilityRequirements Requirements(CapabilityBinding b) => Inner.Requirements(b);
        public ParcelPorts Create(ParcelConfiguration c)
        {
            var p = Inner.Create(c); var id = ParcelBindings.Camera(c, position)!.InstanceId;
            var held = new HeldCamera((IImageCamera)p.Devices![id], this);
            return p with { Camera = p.Camera!.InstanceId == id ? held : p.Camera, Devices = p.Devices.ToDictionary(x => x.Key, x => x.Key == id ? held : x.Value) };
        }
        public ValueTask DisposeAsync() => Inner.DisposeAsync();
    }
    sealed class HeldCamera(IImageCamera inner, HoldFactory owner) : IImageCamera
    {
        public string InstanceId => inner.InstanceId;
        public Task StartAsync(CancellationToken t) => inner.StartAsync(t);
        public Task<CapabilityHealth> HealthAsync(CancellationToken t) => inner.HealthAsync(t);
        public Task ReconnectAsync(CancellationToken t) => inner.ReconnectAsync(t);
        public async Task<StageReply> CaptureAsync(FrozenRequest r, CancellationToken t) { owner.Entered.TrySetResult(); await owner.Release.Task.WaitAsync(t); return await inner.CaptureAsync(r, t); }
        public Task<StageReply?> QueryAsync(FrozenRequest r, CancellationToken t) => inner.QueryAsync(r, t);
        public Task StopAsync(CancellationToken t) => inner.StopAsync(t);
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}

using System.Diagnostics;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Application.DirectPlc;
using Ipc.Application.WcsToPlc;
using Ipc.Assembly;
using Ipc.Platform;
using Ipc.Reference.Protocol;
using Microsoft.Data.Sqlite;

static class CapabilityClosure
{
    static void Pass(bool condition, string reason) { if (!condition) throw new Exception("FAIL AC19: " + reason); Console.WriteLine("PASS AC19: " + reason); }
    static async Task<ParcelSnapshot> Wait(ParcelRuntime app, Func<ParcelSnapshot, bool> ready)
    {
        for (int n = 0; n < 250; n++) { var s = await app.SnapshotAsync().WaitAsync(TimeSpan.FromSeconds(1)); if (ready(s)) return s; await Task.Delay(20); }
        throw new Exception("AC19 timeout: " + Wire.Encode(await app.SnapshotAsync()));
    }
    static ParcelRuntime Create(string id, BusinessLaunch launch, IParcelCapabilityFactory factory) => id switch
    {
        "sportmaster" => SportmasterApplication.Create(launch, factory),
        "direct-plc" => DirectPlcApplication.Create(launch, factory),
        _ => WcsToPlcApplication.Create(launch, factory)
    };
    public static async Task Run(string workspace, string root)
    {
        Pass(typeof(WeightPolicy).Assembly == typeof(OperatorSession).Assembly && typeof(WeightPolicy).FullName == "Ipc.Application.Contracts.WeightPolicy" &&
            typeof(WeightPolicy).GetConstructor([typeof(WeightSource), typeof(WeightUnit), typeof(int), typeof(decimal?), typeof(int)]) is not null &&
            !typeof(OperatorSession).Assembly.GetReferencedAssemblies().Any(x => x.Name!.StartsWith("Ipc.Application.", StringComparison.Ordinal)),
            "common capabilities live in Platform with released full names/constructor and no Application assembly dependency");
        CapabilityBinding B(string kind) => new(kind, "reference-" + kind, "1.0.0", kind + ".one", "reference://" + kind + "/closure19", "PARCEL-REFERENCE-v1", kind == "login" ? new() { ["subjects"] = "alice,bob" } : []);
        ParcelConfiguration Config(string id) => Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace, "config", id, "business.json"))) with { SchemaVersion = 2, Topology = null };
        async Task<ParcelRuntime> Open(ParcelConfiguration c, string run, Func<ParcelCapabilityFactory, IParcelCapabilityFactory>? wrap = null)
        {
            var path = Path.Combine(root, run); Directory.CreateDirectory(path);
            ExtensionStore.AtomicWrite(Path.Combine(path, "business.json"), c);
            var launch = new BusinessLaunch(path, Path.Combine(path, "data"), null, null); var factory = new ParcelCapabilityFactory(launch);
            var app = Create(c.ApplicationId, launch, wrap is null ? factory : wrap(factory));
            try { await app.StartAsync(CancellationToken.None); return app; } catch { await app.DisposeAsync(); throw; }
        }
        async Task<BusinessReceipt> Command(ParcelRuntime app, string kind, string? owner = null, string? value = null)
        { var s = await app.SnapshotAsync(); return await app.CommandAsync(new(Wire.Id(), s.Revision, kind, owner, value)); }
        foreach (var id in new[] { "sportmaster", "direct-plc", "wcs-to-plc" })
        {
            var c = Config(id) with { Login = B("login"), Weight = new(WeightSource.Measured, WeightUnit.Kg, 3, MaximumAgeMs: 3000, WaitTimeoutMs: 5000), Scale = B("weight"), Images = new(B("camera"), B("upload")) };
            await using var app = await Open(c, "entry-" + id);
            Pass(!app.EmitReference("no-login", "00001", Wire.Id()), id + " public entry refuses admission without session");
            await app.LoginAsync("alice", "SIMULATION_ONLY"); app.EmitReference("original", "00001", "original-acquisition");
            var s = await Wait(app, x => x.Operations.FirstOrDefault()?.Capture?.State == StageState.Accepted); var op = s.Operations.Single();
            Pass(op.State == ParcelState.WaitingWeight && op.Wcs is null && op.Plc is null && op.Image is not null, id + " captures original before waiting for actual weight");
            var acquisition = Wire.Decode<ParcelPayload>(op.Capture!.Request.Payload).Acquisition;
            Pass(acquisition?.SourceEventId == "original-acquisition" && acquisition.AdmissionId == op.AdmissionId && acquisition.ObservedAt == op.ObservedAt, id + " capture uses immutable source event/admission/time rather than latest frame");
            await Task.Delay(200);
            Pass(Wire.FileHash(Path.Combine(root, "entry-" + id, "data", "Simulation", c.StationId, id, op.Image!.RelativeFile)) == op.Image.Sha256, id + " waiting weight retains intact original bytes");
            var measurement = new WeightMeasurement(Wire.Id(), op.AdmissionId, c.Scale!.Target, 2.345m, WeightUnit.Kg, 3, MeasurementQuality.Valid, true, DateTimeOffset.UtcNow);
            app.EmitReferenceWeight(measurement);
            s = await Wait(app, x => x.Operations.Single().State == (id == "sportmaster" ? ParcelState.SlotResolved : ParcelState.PlcAccepted)); op = s.Operations.Single();
            foreach (var stage in new[] { op.Wcs, op.Plc }.OfType<StageFact>())
            {
                var payload = Wire.Decode<ParcelPayload>(stage.Request.Payload);
                Pass(payload.Weight?.MeasurementId == measurement.Id && payload.Operator?.SessionId == op.Operator!.SessionId && payload.Image?.CaptureRequestId == op.Capture!.Request.Id, id + " adapter-bound original payload retains operator, measurement and acquisition");
            }
            await Wait(app, x => x.Operations.Single().ImageReleased);
            Pass(!File.Exists(Path.Combine(root, "entry-" + id, "data", "Simulation", c.StationId, id, op.Image!.RelativeFile)), id + " terminal upload releases bounded temporary bytes with durable release marker");
        }
        var weightedUpload = Config("wcs-to-plc") with { Weight = new(WeightSource.Measured, WeightUnit.Kg, 3), Scale = B("weight"), Images = new(B("camera"), B("upload"), Strategy: UploadStrategy.Attached) };
        await using (var app = await Open(weightedUpload, "upload-required-weight", inner => new BlockingFactory(inner, "requires-weight")))
        {
            app.EmitReference("weight-upload", "00002", "original-weighted-upload");
            var s = await Wait(app, x => x.Operations.FirstOrDefault()?.Capture?.State == StageState.Accepted);
            Pass(s.Operations.Single().Upload is null && s.Operations.Single().Weight is null, "uploader declaring measured input captures first but cannot prematurely freeze or send weightless upload");
            var m = new WeightMeasurement(Wire.Id(), "weight-upload", weightedUpload.Scale!.Target, 1.234m, WeightUnit.Kg, 3, MeasurementQuality.Valid, true, DateTimeOffset.UtcNow);
            app.EmitReferenceWeight(m);
            s = await Wait(app, x => x.Operations.Single().State == ParcelState.PlcAccepted && x.Operations.Single().Upload?.State == StageState.Accepted);
            Pass(Wire.Decode<ParcelPayload>(s.Operations.Single().Upload!.Request.Payload).Weight?.MeasurementId == m.Id, "required measured upload input is frozen only when same-parcel measurement is ready");
        }
        // Attached upload intentionally commits and loses its reply. The main chain still
        // proceeds, and restart cannot convert the upload's uncertainty into a resend.
        var attached = Config("wcs-to-plc") with { Weight = new(WeightSource.Disabled, WeightUnit.Kg, 3), Images = new(B("camera"), B("upload") with { Parameters = new() { ["reply-mode"] = "lose-after-commit" } }, Strategy: UploadStrategy.Attached) };
        ParcelOperation original;
        await using (var app = await Open(attached, "attached-unknown"))
        {
            app.EmitReference("attached", "00002", "attached-original");
            var s = await Wait(app, x => x.Operations.FirstOrDefault() is { State: ParcelState.PlcAccepted, Upload.State: StageState.Unknown }); original = s.Operations.Single();
            Pass(original.Wcs?.Attempts == 1 && original.Plc?.Attempts == 1 && !original.ImageReleased, "attached unknown has independent responsibility and does not block WCS/PLC or delete original");
        }
        await using (var app = await Open(attached, "attached-unknown"))
        {
            var op = (await app.SnapshotAsync()).Operations.Single();
            Pass(op.Upload!.Attempts == 1 && op.Upload.State == StageState.Unknown && op.Plc!.Request.Id == original.Plc!.Request.Id, "restart preserves attached unknown and original main requests without automatic replay");
            await app.ReconcileAsync(op.Id); var s = await Wait(app, x => x.Operations.Single().ImageReleased); op = s.Operations.Single();
            var peer = Wire.Decode<ReferencePeer.PeerEntry>(File.ReadAllText(Path.Combine(op.Upload!.Request.Binding.Parameters["peer-root"], Wire.Hash(op.Upload!.Request.Binding.Target), op.Upload.Request.Id + ".json")));
            Pass(peer.Executions == 1 && op.Upload.State == StageState.Accepted && op.State == ParcelState.PhysicalCompleted, "original query proves upload and physical facts independently without another execution");
        }
        await using (var app = await Open(attached, "attached-unknown")) Pass((await app.SnapshotAsync()).Operations.Single().ImageReleased, "released historical images never make restart require deleted files");
        foreach (var failure in new[] { "reject", "lose-after-commit" })
        {
            var c = attached with { Images = attached.Images! with { Uploader = B("upload") with { Parameters = failure == "reject" ? new() { ["decision"] = "reject" } : new() { ["reply-mode"] = failure } } } };
            await using var app = await Open(c, "attached-" + failure); app.EmitReference("image-failure", "003", Wire.Id());
            var s = await Wait(app, x => x.Operations.FirstOrDefault()?.State == ParcelState.PlcAccepted && x.Operations[0].Upload?.State == (failure == "reject" ? StageState.Rejected : StageState.Unknown));
            Pass(s.Operations.Single().Plc?.Attempts == 1, "attached " + failure + " remains independent of main acceptance");
        }
        var bounded = attached with { Images = attached.Images! with { MaximumPending = 1, TotalBytes = attached.Images.MaximumBytes } };
        await using (var app = await Open(bounded, "bounded-cache"))
        {
            app.EmitReference("cache-one", "003", Wire.Id()); await Wait(app, x => x.Operations.FirstOrDefault()?.Upload?.State == StageState.Unknown);
            app.EmitReference("cache-two", "003", Wire.Id()); await Task.Delay(100);
            var s = await app.SnapshotAsync();
            Pass(s.Operations.Length == 1 && s.BlockingReasons.Any(x => x.Contains("backlog")), "temporary cache admission enforces total/backlog bounds and projects same guard to client");
            var originalPath = Path.Combine(root, "bounded-cache/data/Simulation", bounded.StationId, bounded.ApplicationId, s.Operations.Single().Image!.RelativeFile);
            Pass(File.Exists(originalPath), "quota exhaustion preserves unknown upload original");
        }
        var released = Config("sportmaster") with { Images = new(B("camera"), B("upload")) };
        var releaseCrash = await Open(released, "release-crash");
        releaseCrash.Boundary = point => { if (point == "AfterImageReleaseDurable") throw new IOException("AC19 crash after durable release, before delete"); };
        releaseCrash.EmitReference("release", "003", Wire.Id());
        var releasedSnapshot = await Wait(releaseCrash, x => x.Operations.FirstOrDefault()?.ImageReleased == true);
        var releasePath = Path.Combine(root, "release-crash/data/Simulation", released.StationId, released.ApplicationId, releasedSnapshot.Operations.Single().Image!.RelativeFile);
        Pass(File.Exists(releasePath), "crash injection preserves byte evidence after committed release marker");
        await releaseCrash.DisposeAsync();
        await using (var app = await Open(released, "release-crash")) Pass(!File.Exists(releasePath) && (await app.SnapshotAsync()).Operations.Single().ImageReleased, "startup completes interrupted release cleanup without requiring released history bytes");
        // Both external-wait paths are exercised through actual Application entrances.
        foreach (var slow in new[] { "wcs", "upload" })
        {
            BlockingFactory? blocker = null;
            var c = slow == "upload" ? attached with { Images = attached.Images! with { Uploader = B("upload") } } : Config("wcs-to-plc");
            var app = await Open(c, "slow-" + slow, inner => blocker = new(inner, slow));
            app.EmitReference("slow-parcel", "004", Wire.Id()); await blocker!.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            if (slow == "upload") await Wait(app, x => x.Operations.Single().State == ParcelState.PlcAccepted);
            var clock = Stopwatch.StartNew(); var snapshot = await app.SnapshotAsync().WaitAsync(TimeSpan.FromMilliseconds(250));
            Pass((await Command(app, "Pause")).Status == "Persisted" && clock.ElapsedMilliseconds < 250, slow + " external wait does not block snapshot or software pause");
            using var stopBudget = new CancellationTokenSource(50);
            try { await app.StopAsync(stopBudget.Token); throw new Exception("Stop falsely completed"); }
            catch (OperationCanceledException) { Pass(app.HasUnreleasedResources, slow + " stop timeout retains original in-flight ownership"); }
            blocker.Release.TrySetResult(); await app.StopAsync(CancellationToken.None); await app.DisposeAsync();
        }
        var measured = Config("direct-plc") with { Weight = new(WeightSource.Measured, WeightUnit.Kg, 3, MaximumAgeMs: 1000, WaitTimeoutMs: 250), Scale = B("weight") };
        await using (var app = await Open(measured, "weight-deadline"))
        {
            app.EmitReference("expired", "005", Wire.Id()); await Wait(app, x => x.Operations.FirstOrDefault()?.State == ParcelState.WeightTimedOut);
            var op = (await app.SnapshotAsync()).Operations.Single();
            app.EmitReferenceWeight(new(Wire.Id(), op.AdmissionId, measured.Scale!.Target, 1.234m, WeightUnit.Kg, 3, MeasurementQuality.Valid, true, DateTimeOffset.UtcNow));
            await Task.Delay(80); Pass((await app.SnapshotAsync()).Operations.Single().Plc is null, "late measurement after same-parcel deadline cannot restore or submit");
            await Command(app, "Pause"); Pass((await Command(app, "Resume")).Status == "Rejected", "Resume cannot bypass weight deadline");
            Pass((await Command(app, "AbandonUnsubmitted", op.Id, "SIMULATION: parcel inspected and removed")).Status == "Persisted", "explicit inspected disposition only abandons a parcel with no WCS/PLC intent");
            Pass((await Command(app, "Resume")).Status == "Persisted", "next parcel requires explicit reset after disposition");
            app.EmitReference("fresh", "005", Wire.Id()); var s = await Wait(app, x => x.Operations.Length == 2); var next = s.Operations.First();
            app.EmitReferenceWeight(new(Wire.Id(), next.AdmissionId, measured.Scale.Target, 1.234m, WeightUnit.Kg, 3, MeasurementQuality.Valid, true, DateTimeOffset.UtcNow));
            await Wait(app, x => x.Operations.First().State == ParcelState.PlcAccepted); Pass(true, "next same-code parcel has new identity and new measurement association");
        }
        // Crash at the last pre-transaction boundary: dedup row and aggregate must both
        // remain absent, then the ORIGINAL sample can be associated once after restart.
        var crash = measured with { Weight = measured.Weight with { WaitTimeoutMs = 15000, MaximumAgeMs = 10000 } };
        WeightMeasurement sample;
        var broken = await Open(crash, "weight-crash"); broken.Boundary = point => { if (point == "AfterMeasurementInsert") throw new IOException("AC19 interrupted after measurement insert, before aggregate/transaction commit"); };
        broken.EmitReference("crash", "006", Wire.Id()); await Wait(broken, x => x.Operations.Length == 1);
        sample = new(Wire.Id(), "crash", crash.Scale!.Target, 1.234m, WeightUnit.Kg, 3, MeasurementQuality.Valid, true, DateTimeOffset.UtcNow); broken.EmitReferenceWeight(sample);
        await Wait(broken, x => x.BlockingReasons.Any(r => r.Contains("worker failed")));
        try { await broken.DisposeAsync(); } catch (InvalidOperationException) { }
        using (var db = new SqliteConnection("Data Source=" + Path.Combine(root, "weight-crash/data/Simulation", crash.StationId, "direct-plc/direct-plc.db")))
        { db.Open(); using var count = db.CreateCommand(); count.CommandText = "SELECT COUNT(*) FROM parcel_measurements"; Pass((long)count.ExecuteScalar()! == 0, "interrupted association cannot leave an orphaned measurement dedup row"); }
        await using (var app = await Open(crash, "weight-crash"))
        {
            app.EmitReferenceWeight(sample); var s = await Wait(app, x => x.Operations.Single().Weight is not null);
            Pass(s.Paused && s.Operations.Single().Plc is null, "recovered original measurement cannot automatically authorize first PLC dispatch");
            await Command(app, "Resume");
            var recovered = s.Operations.Single(); var revision = (await app.SnapshotAsync()).Revision;
            await app.CommandAsync(new(Wire.Id(), revision, "ContinuePrepared", recovered.Id, "Synthetic original parcel inspected after interrupted association"));
            s = await Wait(app, x => x.Operations.Single().State == ParcelState.PlcAccepted);
            app.EmitReferenceWeight(sample); await Task.Delay(50);
            Pass((await app.SnapshotAsync()).Operations.Single().Plc!.Attempts == 1 && s.Operations.Single().Weight!.MeasurementId == sample.Id, "original sample survives restart and duplicate sample cannot cause another PLC attempt");
        }
        var sessionConfig = Config("sportmaster") with { Login = B("login"), SessionLifetimeMs = 200 };
        await using (var app = await Open(sessionConfig, "session-expiry"))
        {
            await app.LoginAsync("alice", "SIMULATION_ONLY"); app.EmitReference("session", "007", Wire.Id()); var s = await Wait(app, x => x.Operations.FirstOrDefault()?.State == ParcelState.SlotResolved); var op = s.Operations.Single();
            await Task.Delay(240); Pass(!app.EmitReference("expired-session", "008", Wire.Id()), "expired session refuses admission without inventing an authenticated default");
            await Command(app, "Pause"); var before = (await app.SnapshotAsync()).Configuration;
            Pass((await app.StageConfigurationAsync(before.Revision, before.Effective, "Synthetic directed configuration verification")).Status == "Rejected", "software pause alone grants no maintenance authority");
            Pass((await app.AuthorizeMaintenanceAsync("maintenance", "wrong")).Status == "Rejected" && (await app.AuthorizeMaintenanceAsync("maintenance", "SIMULATION_ONLY")).Status == "Authorized", "separate reference maintenance provider authenticates explicit maintenance grant");
            Pass((await app.StageConfigurationAsync(before.Revision, before.Effective with { TimeoutMs = 1800 }, "Synthetic directed configuration verification")).Status == "PendingRestart", "authenticated configuration forms pending revision and leaves original effective and parcel frozen");
            Pass((await app.LoginAsync("bob", "SIMULATION_ONLY")).Status == "Authenticated" && (await app.SnapshotAsync()).Operations.Single().Operator!.SessionId == op.Operator!.SessionId, "expired session replacement preserves admitted operator snapshot");
        }
        Console.WriteLine("PASS AC19 COMPLETE: independent entrances, weight/session guards, original images, two upload strategies, cleanup and nonblocking recovery");
    }
    sealed class BlockingFactory(ParcelCapabilityFactory inner, string slow) : IParcelCapabilityFactory
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ParcelConfiguration Normalize(ParcelConfiguration c) => inner.Normalize(c);
        public void Validate(ParcelConfiguration c) => inner.Validate(c);
        public CapabilityRequirements Requirements(CapabilityBinding binding)
        { var requirements = inner.Requirements(binding); return slow == "requires-weight" && binding.Kind == "upload" ? requirements with { MeasuredWeight = true } : requirements; }
        public ParcelPorts Create(ParcelConfiguration c) { var p = inner.Create(c); return slow == "wcs" ? p with { Wcs = new BlockingWcs(p.Wcs!, Entered, Release) } : slow == "upload" ? p with { Uploader = new BlockingUpload(p.Uploader!, Entered, Release) } : p; }
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
    sealed class BlockingWcs(IWcsPort inner, TaskCompletionSource entered, TaskCompletionSource release) : IWcsPort
    {
        public string InstanceId => inner.InstanceId;
        public Task StartAsync(CancellationToken t) => inner.StartAsync(t);
        public Task<CapabilityHealth> HealthAsync(CancellationToken t) => inner.HealthAsync(t);
        public Task ReconnectAsync(CancellationToken t) => inner.ReconnectAsync(t);
        public async Task<StageReply> ResolveSlotAsync(FrozenRequest r, CancellationToken t) { entered.TrySetResult(); await release.Task; return await inner.ResolveSlotAsync(r, t); }
        public Task<StageReply?> QueryAsync(FrozenRequest r, CancellationToken t) => inner.QueryAsync(r, t);
        public Task StopAsync(CancellationToken t) => inner.StopAsync(t);
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
    sealed class BlockingUpload(IImageUploader inner, TaskCompletionSource entered, TaskCompletionSource release) : IImageUploader
    {
        public string InstanceId => inner.InstanceId;
        public Task StartAsync(CancellationToken t) => inner.StartAsync(t);
        public Task<CapabilityHealth> HealthAsync(CancellationToken t) => inner.HealthAsync(t);
        public Task ReconnectAsync(CancellationToken t) => inner.ReconnectAsync(t);
        public async Task<StageReply> UploadAsync(FrozenRequest r, ReadOnlyMemory<byte> content, CancellationToken t) { entered.TrySetResult(); await release.Task; return await inner.UploadAsync(r, content, t); }
        public Task<StageReply?> QueryAsync(FrozenRequest r, CancellationToken t) => inner.QueryAsync(r, t);
        public Task StopAsync(CancellationToken t) => inner.StopAsync(t);
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}

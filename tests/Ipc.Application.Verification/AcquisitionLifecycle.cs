using Ipc.Adapters;
using Ipc.Application.Contracts;
using Ipc.Platform;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Assembly;

static class AcquisitionLifecycle
{
    static void Check(bool ok, string detail) { if (!ok) throw new Exception("FAIL P2: " + detail); Console.WriteLine("PASS P2: " + detail); }
    static AcquisitionIdentity Identity(Observation o) => new(o.DeviceId, o.Epoch, o.SourceEventId, o.AdmissionId, o.ObservedAt);
    static FrozenRequest Request(Observation o, CapabilityBinding binding)
    {
        var payload = Wire.Encode(new ParcelPayload("0001", o.AdmissionId, null, null, null, Acquisition: Identity(o)));
        return new(Wire.Id(), "sportmaster", Wire.Id(), o.AdmissionId, "image-capture", binding, Wire.Hash("implementation"), Wire.Hash("configuration"), payload, Wire.Hash(payload), DateTimeOffset.UtcNow);
    }
    static async Task Missing(IImageCamera camera, FrozenRequest request)
    {
        try { await camera.CaptureAsync(request, CancellationToken.None); }
        catch (IOException ex) when (ex.Message.Contains("Original acquisition unavailable")) { return; }
        throw new Exception("FAIL P2: released/foreign original was replaced");
    }
    public static async Task Run(string workspace, string root)
    {
        await using var source = new ManualObservationSource("reader.p2");
        var binding = new CapabilityBinding("camera", "reference-camera", "1.0.0", "camera.p2", source.DeviceId, "PARCEL-REFERENCE-v1", []);
        await using var camera = source.BindCapture(binding, root);
        await camera.StartAsync(CancellationToken.None);
        Observation? accepted = null;
        await source.StartControlledAsync(Wire.Id(), o => { if (accepted is not null) return false; accepted = o; return true; }, _ => { }, CancellationToken.None);
        source.Emit("Read", "original", ["0001"], sourceId: "original");
        for (int i = 0; i < 5000; i++) source.Emit("Read", "rejected-" + i, ["0001"]);
        var a = accepted!;
        var acquisition = Identity(a);
        var request = Request(a, binding);
        var reply = await camera.CaptureAsync(request, CancellationToken.None);
        if (reply.BusinessAccepted != true || reply.Image?.Acquisition != acquisition) throw new Exception("FAIL P2: accepted original acquisition lost after rejected flood");
        Check(source.RetainedAcquisitionCount == 1, "accepted original survives 5000 rejected emissions; rejected records leave no owners");
        source.RetainAcquisition(acquisition, "operation:original");
        source.ReleaseAcquisition(acquisition, "observation:" + a.Id);
        source.ReleaseAcquisition(acquisition, "observation:" + a.Id);
        Check(source.RetainedAcquisitionCount == 1, "idempotent ingress release cannot remove the operation owner");
        await source.StopAsync(CancellationToken.None);
        var arrivals = new List<Observation>();
        await source.StartControlledAsync(Wire.Id(), o => { arrivals.Add(o); return true; }, _ => { }, CancellationToken.None);
        source.Emit("Read", a.AdmissionId, ["0001"], sourceId: a.SourceEventId);
        var newer = arrivals.Single();
        Check(newer.Epoch != a.Epoch && (await camera.CaptureAsync(request, CancellationToken.None)).Image?.Acquisition == acquisition,
            "reconnect and reused source event retain exact original epoch while new acquisition is distinct");
        await Missing(camera, Request(a with { AdmissionId = "wrong" }, binding));
        await Missing(camera, Request(a with { DeviceId = "foreign" }, binding));
        await Missing(camera, Request(a with { ObservedAt = a.ObservedAt.AddTicks(1) }, binding));
        Check(true, "foreign device, admission and timestamp cannot substitute original acquisition");
        source.ReleaseAcquisition(acquisition, "operation:original");
        await Missing(camera, request);
        Check((await camera.QueryAsync(request, CancellationToken.None))?.Image?.Acquisition == acquisition,
            "released frame cannot be recaptured; durable original request remains queryable");
        for (int i = 0; i < 4200; i++) source.Emit("Read", "accepted-" + i, ["0001"]);
        Check(source.RetainedAcquisitionCount == arrivals.Count, "more than 4096 accepted in-flight acquisitions retain all owners");
        Check((await camera.CaptureAsync(Request(newer, binding), CancellationToken.None)).Image?.Acquisition == Identity(newer), "earliest accepted original remains available after accepted flood");
        foreach (var o in arrivals) source.ReleaseAcquisition(Identity(o), "observation:" + o.Id);
        Check(source.RetainedAcquisitionCount == 0, "all consumed observations are released without a count or time eviction policy");
        await source.StopAsync(CancellationToken.None);
        await source.StartControlledAsync(Wire.Id(), _ => throw new IOException("callback fault"), _ => { }, CancellationToken.None);
        try { source.Emit("Read", "throw", ["0001"]); throw new Exception("callback failure swallowed"); }
        catch (IOException ex) when (ex.Message == "callback fault") { }
        Check(source.RetainedAcquisitionCount == 0, "callback exception is propagated and provisional owner is removed");
        await ActiveCapture(root);
        await RuntimeCases(workspace, root);
    }

    static async Task ActiveCapture(string root)
    {
        // A real asynchronous I/O boundary inside the production capture facade.
        var address = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        address.Start(); var port = ((System.Net.IPEndPoint)address.LocalEndpoint).Port; address.Stop();
        using var server = new System.Net.HttpListener();
        var endpoint = "http://127.0.0.1:" + port + "/"; server.Prefixes.Add(endpoint); server.Start();
        await using var source = new ManualObservationSource("reader.active");
        var binding = new CapabilityBinding("camera", "reference-camera", "1.0.0", "camera.active", source.DeviceId, "PARCEL-REFERENCE-v1", new() { ["endpoint"] = endpoint });
        await using var camera = source.BindCapture(binding, root);
        await camera.StartAsync(CancellationToken.None);
        Observation? observation = null;
        await source.StartControlledAsync(Wire.Id(), o => { observation = o; return true; }, _ => { }, CancellationToken.None);
        source.Emit("Read", "active", ["0001"]);
        var capture = camera.CaptureAsync(Request(observation!, binding), CancellationToken.None);
        var incoming = await server.GetContextAsync().WaitAsync(TimeSpan.FromSeconds(2));
        try
        {
            source.ReleaseAcquisition(Identity(observation!), "observation:" + observation!.Id);
            Check(source.RetainedAcquisitionCount == 1, "active capture pins original even after its last observation owner releases");
            try { await source.DisposeAsync(); throw new Exception("disposed active capture"); }
            catch (InvalidOperationException ex) when (ex.Message.Contains("captures still active")) { }
            Check(!capture.IsCompleted && source.RetainedAcquisitionCount == 1, "source disposal refuses to erase active original and permits cleanup retry");
        }
        finally { incoming.Response.StatusCode = 503; incoming.Response.Close(); }
        try { await capture.WaitAsync(TimeSpan.FromSeconds(2)); throw new Exception("HTTP error swallowed"); }
        catch (IOException) { }
        Check(source.RetainedAcquisitionCount == 0, "failed asynchronous capture propagates error and releases only its completed pin");
        await source.DisposeAsync();
    }

    static async Task<ParcelSnapshot> Wait(ParcelRuntime app, Func<ParcelSnapshot, bool> predicate)
    {
        for (int i = 0; i < 300; i++) { var s = await app.SnapshotAsync().WaitAsync(TimeSpan.FromSeconds(2)); if (predicate(s)) return s; await Task.Delay(10); }
        throw new Exception("P2 timeout: " + Wire.Encode(await app.SnapshotAsync()));
    }
    static async Task<BusinessReceipt> Command(ParcelRuntime app, string kind, string? owner = null, string? value = null)
    { var s = await app.SnapshotAsync(); return await app.CommandAsync(new(Wire.Id(), s.Revision, kind, owner, value)); }
    static async Task RuntimeCases(string workspace, string root)
    {
        var c = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace, "config/optional/sportmaster/business.json")));
        c = c with { Login = null, Scale = null, Weight = new(WeightSource.Disabled, WeightUnit.Kg, 3), TimeoutMs = 10000, QueueCapacity = 2,
            Images = c.Images! with { Uploader = null }, Topology = c.Topology! with { Positions = c.Topology.Positions.Select(x => x with { ScaleInstanceId = null }).ToArray() } };
        var path = Path.Combine(root, "runtime"); Directory.CreateDirectory(path); ExtensionStore.AtomicWrite(Path.Combine(path, "business.json"), c);
        var launch = new BusinessLaunch(path, Path.Combine(path, "data"), null, null);
        var factory = new SlowFactory(new ParcelCapabilityFactory(launch));
        await using var app = new ParcelRuntime(launch, new SportmasterFlow(), factory);
        await app.StartAsync(CancellationToken.None);
        var left = factory.Readers![c.Topology.Readers[0].DeviceId]; var right = factory.Readers[c.Topology.Readers[1].DeviceId];
        try
        {
            Check(app.EmitReferenceAt("left", "slow", "0001", "slow-original"), "slow left original accepted");
            await factory.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Check(app.EmitReferenceAt("left", "queued-one", "0001", "queue-1") && app.EmitReferenceAt("left", "queued-two", "0001", "queue-2"), "bounded left queue holds two original acquisitions while capture is in flight");
            Check(!app.EmitReferenceAt("left", "overload", "0001", "overload"), "left queue overload explicitly rejects input");
            Parallel.For(0, 5000, i => { if (app.EmitReferenceAt("left", "reject-" + i, "0001", "reject-" + i)) throw new Exception("overload admitted input"); });
            Check(left.RetainedAcquisitionCount == 3, "concurrent rejected flood retains exactly accepted left acquisitions");
            Check(app.EmitReferenceAt("right", "independent", "0001", "right-original"), "right accepts while left capture and queue are blocked");
            await Wait(app, s => s.Operations.Any(o => o.PositionId == "right" && o.ImageReleased));
            Check(right.RetainedAcquisitionCount == 0 && left.RetainedAcquisitionCount == 3, "right completes and releases independently of overloaded left");
            await Command(app, "Pause"); await app.AuthorizeMaintenanceAsync("maintenance", "SIMULATION_ONLY");
            var before = await app.SnapshotAsync();
            var reconnect = app.ReconnectDeviceAsync(left.DeviceId);
            await Task.Delay(100);
            var during = await app.SnapshotAsync().WaitAsync(TimeSpan.FromSeconds(2));
            Check(!reconnect.IsCompleted && left.Epoch == before.Devices!.Single(x => x.DeviceId == left.DeviceId).Epoch, "left reconnect waits for in-flight capture while snapshots remain readable");
            factory.Release.TrySetResult();
            Check((await reconnect).Status == "Reconnected", "left reader reconnect completes only after the original capture releases its lifecycle");
            var after = await app.SnapshotAsync();
            Check(after.Devices!.Single(x => x.DeviceId == right.DeviceId).Epoch == before.Devices!.Single(x => x.DeviceId == right.DeviceId).Epoch, "left reconnect does not change right epoch");
            var s = await Wait(app, s => s.Operations.Any(o => o.AdmissionId == "slow" && o.Capture?.State == StageState.Accepted) && left.RetainedAcquisitionCount == 1);
            var op = s.Operations.Single(o => o.AdmissionId == "slow");
            Check(op.Image?.Acquisition == op.Acquisition && op.Acquisition!.Epoch != left.Epoch && op.Capture?.Attempts == 1, "slow capture resolves exact pre-reconnect acquisition once; blocked queued observations release");
            var durableBeforeRelease = false;
            app.Boundary = point => { if (point == "AfterImageReleaseDurable") durableBeforeRelease = left.RetainedAcquisitionCount == 1; };
            await Command(app, "AcknowledgeOverload"); await Command(app, "Resume");
            await Command(app, "ContinuePrepared", op.Id);
            await Wait(app, x => x.Operations.Single(o => o.Id == op.Id).ImageReleased);
            Check(durableBeforeRelease && left.RetainedAcquisitionCount == 0, "durable main completion precedes release of the final operation acquisition");
            app.Boundary = null;

            await Wait(app, x => x.Positions!.Single(p => p.Id == "left").Admit);
            factory.LoseNextReply = true;
            app.EmitReferenceAt("left", "unknown", "0001", "unknown-source");
            s = await Wait(app, x => x.Operations.Any(o => o.AdmissionId == "unknown" && o.Capture?.State == StageState.Unknown));
            op = s.Operations.Single(o => o.AdmissionId == "unknown");
            Check(left.RetainedAcquisitionCount == 1 && !op.ImageReleased, "lost capture reply preserves unknown acquisition ownership");
            await app.ReconcileAsync(op.Id);
            await Command(app, "ContinuePrepared", op.Id);
            await Wait(app, x => x.Operations.Single(o => o.Id == op.Id).ImageReleased);
            Check(left.RetainedAcquisitionCount == 0 && (await app.SnapshotAsync()).Operations.Single(o => o.Id == op.Id).Capture!.Attempts == 1, "original query recovers capture without resend and terminal cleanup releases owner");
            // The overload journey already covers accepted-but-business-blocked ingress.
            await Command(app, "Pause");
            Check(app.EmitReferenceAt("left", "paused", "0001", "paused"), "paused observation can enter queue for explicit business rejection");
            await Wait(app, _ => left.RetainedAcquisitionCount == 0);
            Check((await app.SnapshotAsync()).Operations.All(o => o.AdmissionId != "paused"), "business rejection releases ingress without inventing an operation");
            await Command(app, "Resume"); factory.LoseNextReply = true;
            app.EmitReferenceAt("left", "restart", "0001", "restart-source");
            s = await Wait(app, x => x.Operations.Any(o => o.AdmissionId == "restart" && o.Capture?.State == StageState.Unknown));
            var original = s.Operations.Single(o => o.AdmissionId == "restart");
            await app.DisposeAsync();
            Check(left.RetainedAcquisitionCount == 0 && right.RetainedAcquisitionCount == 0, "fully drained disposal terminates process-local acquisition resources");
            var reopenedFactory = new SlowFactory(new ParcelCapabilityFactory(launch));
            await using var reopened = new ParcelRuntime(launch, new SportmasterFlow(), reopenedFactory);
            await reopened.StartAsync(CancellationToken.None);
            await reopened.ReconcileAsync(original.Id);
            var recovered = (await reopened.SnapshotAsync()).Operations.Single(o => o.Id == original.Id);
            Check(Wire.Encode(recovered.Capture!.Request) == Wire.Encode(original.Capture!.Request) && recovered.Capture.Attempts == 1 && recovered.Image?.Acquisition == original.Acquisition,
                "restart queries durable original capture with identical request and acquisition, without recreating frame or resending");
        }
        finally { factory.Release.TrySetResult(); }
    }
    sealed class SlowFactory(ParcelCapabilityFactory inner) : IParcelCapabilityFactory
    {
        public Dictionary<string, ManualObservationSource>? Readers;
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously), Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool LoseNextReply;
        public ParcelConfiguration Normalize(ParcelConfiguration c) => inner.Normalize(c);
        public void Validate(ParcelConfiguration c) => inner.Validate(c);
        public CapabilityRequirements Requirements(CapabilityBinding b) => inner.Requirements(b);
        public ParcelPorts Create(ParcelConfiguration c)
        {
            var p = inner.Create(c); Readers = p.Readers!.ToDictionary(x => x.Key, x => (ManualObservationSource)x.Value);
            var first = ParcelBindings.Camera(c, "left")!.InstanceId;
            var devices = p.Devices!.ToDictionary(x => x.Key, x => x.Value);
            var camera = new SlowCamera((IImageCamera)devices[first], this); devices[first] = camera;
            return p with { Camera = camera, Devices = devices };
        }
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
    sealed class SlowCamera(IImageCamera inner, SlowFactory owner) : IImageCamera
    {
        public string InstanceId => inner.InstanceId;
        public Task StartAsync(CancellationToken t) => inner.StartAsync(t);
        public Task<CapabilityHealth> HealthAsync(CancellationToken t) => inner.HealthAsync(t);
        public Task ReconnectAsync(CancellationToken t) => inner.ReconnectAsync(t);
        public async Task<StageReply> CaptureAsync(FrozenRequest r, CancellationToken t)
        {
            if (r.AdmissionId == "slow") { owner.Entered.TrySetResult(); await owner.Release.Task; }
            var reply = await inner.CaptureAsync(r, t);
            if (owner.LoseNextReply) { owner.LoseNextReply = false; throw new IOException("Injected reply loss after original capture"); }
            return reply;
        }
        public Task<StageReply?> QueryAsync(FrozenRequest r, CancellationToken t) => inner.QueryAsync(r, t);
        public Task StopAsync(CancellationToken t) => inner.StopAsync(t);
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}

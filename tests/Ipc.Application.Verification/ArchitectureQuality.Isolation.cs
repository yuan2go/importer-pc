using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Assembly;
using Ipc.Platform;

static partial class ArchitectureQuality
{
    static CapabilityBinding B(string kind) => new(kind, "reference-" + kind, "1.0.0", "quality." + kind, "reference://quality/" + kind, "PARCEL-REFERENCE-v1", []);
    static async Task IsolationChecks()
    {
        FaultFactory? faults = null;
        await using (var app = await Open(Config("sportmaster"), "health-identity", f => faults = new(f) { WrongHealth = true }))
        {
            var s = await app.SnapshotAsync(); var vm = new ParcelViewModel(app, "Quality", "Sportmaster"); await vm.RefreshAsync();
            Check(s.Health.Single(h => h.InstanceId == s.Configuration.Effective.Wcs!.InstanceId).Ready == false && s.Positions!.All(p => !p.Admit) && vm.Health.Contains(s.Configuration.Effective.Wcs!.InstanceId + " 未就绪"), "AQ06 wrong health instance cannot assert readiness for configured WCS");
        }
        await using (var app = await Open(Config("sportmaster"), "health-expiry", f => new FaultFactory(f) { ExpireHealth = true }))
        {
            await Task.Delay(160); var s = await app.SnapshotAsync(); var h = s.Health.Single(x => x.InstanceId == s.Configuration.Effective.Wcs!.InstanceId);
            var vm = new ParcelViewModel(app, "Quality", "Sportmaster"); await vm.RefreshAsync();
            Check(!h.Ready && h.ValidUntil <= DateTimeOffset.UtcNow && vm.Health.Contains(h.InstanceId + " 未就绪"), "AQ06 expired source confirmation keeps timestamps but projects unavailable in actual ViewModel");
        }
        await using (var app = await Open(Config("sportmaster"), "ui-target"))
        {
            app.EmitReferenceAt("left", "visible-left", "001", "visible-left-source"); await Wait(app, s => s.Operations.Any(x => x.State == ParcelState.SlotResolved));
            app.EmitReferenceAt("right", "visible-right", "002", "visible-right-source"); var s = await Wait(app, s => s.Operations.Length == 2 && s.Operations.All(x => x.State == ParcelState.SlotResolved));
            var vm = new ParcelViewModel(app, "Quality", "Sportmaster"); await vm.RefreshAsync(); vm.SelectedPositionId = "right"; vm.Selected = s.Operations.Single(o => o.PositionId == "left");
            Check(vm.Current?.AdmissionId == "visible-right" && vm.Selected.AdmissionId == "visible-left" && vm.CurrentCommandTarget.Contains(vm.Current.Id) && vm.HistoryCommandTarget.Contains(vm.Selected.Id), "actual ViewModel separates displayed position/current command owner from selected history detail");
            await vm.RefreshAsync(); Check(vm.Current?.PositionId == "right" && vm.Selected?.PositionId == "left" && vm.PositionRows.Length == 2, "refresh preserves independent window targets and projects both Sportmaster results");
        }
        var c = Config("direct-plc") with { Images = new(B("camera") with { Target = Config("direct-plc").Reader.DeviceId }, B("upload") with { Parameters = new() { ["reply-mode"] = "lose-after-commit" } }, Strategy: UploadStrategy.Attached) };
        string originalId; ParcelOperation before;
        await using (var app = await Open(c, "attached-isolation", f => new FaultFactory(f) { EmptyUploadQuery = true }))
        {
            app.EmitReference("attached-original", "001", "attached-source");
            var s = await Wait(app, s => s.Operations.FirstOrDefault() is { State: ParcelState.PlcAccepted, Upload.State: StageState.Unknown }); before = s.Operations.Single(); originalId = before.Id;
            Check(before.Reason is null && before.Upload!.Failure is not null && before.Plc!.State == StageState.Accepted, "attached transport loss owns upload Failure without contaminating accepted main reason or PLC fact");
            await app.ReconcileAsync(originalId); s = await app.SnapshotAsync(); var op = s.Operations.Single();
            Check(op.State == ParcelState.PhysicalCompleted && op.Reason is null && op.Upload!.State == StageState.Unknown && op.Upload.Failure is not null && !op.ImageReleased && op.Plc!.Attempts == 1, "independent main query reaches authoritative physical completion while attached query remains unknown and retains image");
        }
        // A separate original Awaiting upload occurrence, not another query's bytes, must retain responsibility.
        using (var db = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + Path.Combine(DirectoryFor("attached-isolation", c), "direct-plc.db")))
        {
            db.Open(); using var q = db.CreateCommand(); q.CommandText = "INSERT INTO parcel_receipts VALUES($id,$op,'image-upload',$request,'Query','Awaiting',NULL)";
            q.Parameters.AddWithValue("$id", Wire.Id()); q.Parameters.AddWithValue("$op", originalId); q.Parameters.AddWithValue("$request", before.Upload!.Request.Id); q.ExecuteNonQuery();
        }
        await using (var app = await Open(c, "attached-isolation"))
        {
            var s = await app.SnapshotAsync(); var op = s.Operations.Single();
            Check(op.State == ParcelState.PhysicalCompleted && op.Reason is null && op.Upload!.Failure!.StartsWith("RawUnavailable:") && !op.ImageReleased, "restart assigns missing raw upload occurrence to attached responsibility, preserving completed main facts");
        }
        await QueryHistoryChecks();
        await BoundaryOwnershipChecks();
        await DeviceChecks();
        await HttpChecks();
    }
    static async Task BoundaryOwnershipChecks()
    {
        FaultFactory? producer = null;
        await using (var app = await Open(Config("sportmaster"), "observation-copy", f => producer = new(f) { MutateObservation = true, HoldFirstObservation = true }))
        {
            app.EmitReferenceAt("left", "hold-array", "001", "first-array-source"); await producer!.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            app.EmitReferenceAt("left", "array-original", "002", "second-array-source"); producer.Release.TrySetResult();
            var s = await Wait(app, s => s.Operations.Any(o => o.AdmissionId == "array-original" && o.State == ParcelState.SlotResolved));
            Check(s.Operations.Single(o => o.AdmissionId == "array-original").ParcelCode == "002", "driver buffer reuse after callback cannot change the queued admitted barcode");
        }
        await using (var app = await Open(Config("sportmaster"), "request-copy", f => new FaultFactory(f) { MutateRequest = true }))
        {
            app.EmitReferenceAt("left", "immutable-request", "003", "immutable-request-source");
            var op = (await Wait(app, s => s.Operations.FirstOrDefault()?.State == ParcelState.SlotResolved)).Operations.Single();
            Check(!op.Wcs!.Request.Binding.Parameters.ContainsKey("provider-annotation") && !op.FrozenConfiguration.Wcs!.Parameters.ContainsKey("provider-annotation") && Wire.Hash(Wire.Encode(op.FrozenConfiguration)) == op.ConfigurationHash,
                "adapter mutation of detached request parameters cannot alter original intent or frozen configuration");
        }
    }
    static async Task DeviceChecks()
    {
        var c = Config("direct-plc") with { Images = new(B("camera") with { Target = Config("direct-plc").Reader.DeviceId }, null) };
        FaultFactory? f = null;
        await using var app = await Open(c, "device-serialization", inner => f = new(inner) { BlockCapture = true });
        app.EmitReference("capture-inflight", "003", "original-acquisition"); await f!.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Maintenance(app); var before = await app.SnapshotAsync(); var reconnect = app.ReconnectDeviceAsync(c.Reader.DeviceId);
        await Task.Delay(100); var during = await app.SnapshotAsync().WaitAsync(TimeSpan.FromMilliseconds(300));
        Check(!reconnect.IsCompleted && !f.ReaderStopped && during.Paused, "same-device reconnect waits for in-flight capture while snapshot and software pause remain responsive");
        f.Release.TrySetResult(); var result = await reconnect.WaitAsync(TimeSpan.FromSeconds(2));
        var after = await Wait(app, s => s.Operations.Single().Capture?.State == StageState.Accepted);
        Check(result.Status == "Reconnected" && after.Devices!.Single().Epoch != before.Devices!.Single().Epoch && after.Operations.Single().Image!.Acquisition == before.Operations.Single().Acquisition, "capture returns original acquisition before underlying reader session is replaced with new epoch");
    }
    static async Task QueryHistoryChecks()
    {
        await using var app = await Open(Config("direct-plc"), "query-history", f => new DelayedPlcFactory(f));
        app.EmitReference("long-physical-wait", "00012", "long-physical-source"); var op = (await Wait(app, s => s.Operations.FirstOrDefault()?.State == ParcelState.PlcAccepted)).Operations.Single();
        for (int i = 0; i < 72; i++) await app.ReconcileAsync(op.Id);
        var final = (await app.SnapshotAsync()).Operations.Single();
        using var db = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + Path.Combine(DirectoryFor("query-history", Config("direct-plc")), "direct-plc.db")); db.Open();
        using var c = db.CreateCommand(); c.CommandText = "SELECT count(*) FROM parcel_receipts WHERE state='Committed'";
        Check(final.State == ParcelState.PhysicalCompleted && final.Plc!.Attempts == 1 && final.Plc.Replies.Length <= 64 && (long)c.ExecuteScalar()! == 72, "more than 64 authoritative queries retain every raw receipt, bound stage projection and reach completion without another submit");
    }
    sealed class DelayedPlcFactory(ParcelCapabilityFactory inner) : IParcelCapabilityFactory
    {
        public ParcelConfiguration Normalize(ParcelConfiguration c) => inner.Normalize(c);
        public void Validate(ParcelConfiguration c) => inner.Validate(c);
        public CapabilityRequirements Requirements(CapabilityBinding b) => inner.Requirements(b);
        public ParcelPorts Create(ParcelConfiguration c) { var p = inner.Create(c); return p with { Plc = new DelayedPlc(p.Plc!) }; }
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
    sealed class DelayedPlc(IPlcPort inner) : IPlcPort
    {
        int confirmations;
        public string InstanceId => inner.InstanceId;
        public Task StartAsync(CancellationToken t) => inner.StartAsync(t);
        public Task<CapabilityHealth> HealthAsync(CancellationToken t) => inner.HealthAsync(t);
        public Task ReconnectAsync(CancellationToken t) => inner.ReconnectAsync(t);
        public Task<StageReply> SubmitAsync(FrozenRequest r, CancellationToken t) => inner.SubmitAsync(r, t);
        public async Task<StageReply?> QueryAsync(FrozenRequest r, CancellationToken t)
        {
            var reply = await inner.QueryAsync(r, t); var count = Interlocked.Increment(ref confirmations); r.Binding.Parameters["provider-query-annotation"] = "adapter-owned";
            return reply is null ? null : reply with { Id = Wire.Id(), PhysicalCompleted = count > 70, At = DateTimeOffset.UtcNow, Evidence = "Synthetic delayed authority confirmation " + count };
        }
        public Task StopAsync(CancellationToken t) => inner.StopAsync(t);
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
    sealed class FaultFactory(ParcelCapabilityFactory inner) : IParcelCapabilityFactory
    {
        public bool WrongHealth, ExpireHealth, EmptyUploadQuery, BlockCapture, ReaderStopped, MutateObservation, HoldFirstObservation, MutateRequest;
        public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously), Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ParcelConfiguration Normalize(ParcelConfiguration c) => inner.Normalize(c);
        public void Validate(ParcelConfiguration c) => inner.Validate(c);
        public CapabilityRequirements Requirements(CapabilityBinding b) => inner.Requirements(b);
        public ParcelPorts Create(ParcelConfiguration c)
        {
            var p = inner.Create(c);
            if (p.Wcs is { } w) p = p with { Wcs = new Wcs(w, this) };
            if (p.Uploader is { } u) p = p with { Uploader = new Upload(u, this) };
            if (MutateObservation)
            {
                var wrapped = p.Readers!.ToDictionary(x => x.Key, x => (IControlledObservationSource)new Reader(x.Value, this));
                p = p with { Reader = wrapped[p.Reader.DeviceId], Readers = wrapped };
            }
            if (BlockCapture)
            {
                var reader = new Reader(p.Reader, this); var camera = new Camera(p.Camera!, this);
                p = p with { Reader = reader, Readers = new Dictionary<string, IControlledObservationSource> { [reader.DeviceId] = reader }, Camera = camera,
                    Devices = p.Devices!.ToDictionary(x => x.Key, x => x.Value.InstanceId == camera.InstanceId ? camera : x.Value) };
            }
            return p;
        }
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
    sealed class Wcs(IWcsPort inner, FaultFactory f) : IWcsPort
    {
        int calls;
        public string InstanceId => inner.InstanceId;
        public Task StartAsync(CancellationToken t) => inner.StartAsync(t);
        public async Task<CapabilityHealth> HealthAsync(CancellationToken t)
        {
            if (f.ExpireHealth && Interlocked.Increment(ref calls) > 1) await Task.Delay(Timeout.Infinite, t);
            var h = await inner.HealthAsync(t); return h with { InstanceId = f.WrongHealth ? "wrong.port" : h.InstanceId, ValidUntil = f.ExpireHealth ? h.ConfirmedAt.AddMilliseconds(50) : h.ValidUntil };
        }
        public Task ReconnectAsync(CancellationToken t) => inner.ReconnectAsync(t);
        public async Task<StageReply> ResolveSlotAsync(FrozenRequest r, CancellationToken t)
        {
            if (f.HoldFirstObservation && r.AdmissionId == "hold-array") { f.Entered.TrySetResult(); await f.Release.Task; }
            var reply = await inner.ResolveSlotAsync(r, t); if (f.MutateRequest) r.Binding.Parameters["provider-annotation"] = "adapter-owned"; return reply;
        }
        public Task<StageReply?> QueryAsync(FrozenRequest r, CancellationToken t) => inner.QueryAsync(r, t);
        public Task StopAsync(CancellationToken t) => inner.StopAsync(t);
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
    sealed class Upload(IImageUploader inner, FaultFactory f) : IImageUploader
    {
        public string InstanceId => inner.InstanceId;
        public Task StartAsync(CancellationToken t) => inner.StartAsync(t);
        public Task<CapabilityHealth> HealthAsync(CancellationToken t) => inner.HealthAsync(t);
        public Task ReconnectAsync(CancellationToken t) => inner.ReconnectAsync(t);
        public Task<StageReply> UploadAsync(FrozenRequest r, ReadOnlyMemory<byte> b, CancellationToken t) => inner.UploadAsync(r, b, t);
        public Task<StageReply?> QueryAsync(FrozenRequest r, CancellationToken t) => f.EmptyUploadQuery ? Task.FromResult<StageReply?>(null) : inner.QueryAsync(r, t);
        public Task StopAsync(CancellationToken t) => inner.StopAsync(t);
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
    sealed class Camera(IImageCamera inner, FaultFactory f) : IImageCamera
    {
        public string InstanceId => inner.InstanceId;
        public Task StartAsync(CancellationToken t) => inner.StartAsync(t);
        public Task<CapabilityHealth> HealthAsync(CancellationToken t) => inner.HealthAsync(t);
        public Task ReconnectAsync(CancellationToken t) => inner.ReconnectAsync(t);
        public async Task<StageReply> CaptureAsync(FrozenRequest r, CancellationToken t) { f.Entered.TrySetResult(); await f.Release.Task; return await inner.CaptureAsync(r, t); }
        public Task<StageReply?> QueryAsync(FrozenRequest r, CancellationToken t) => inner.QueryAsync(r, t);
        public Task StopAsync(CancellationToken t) => inner.StopAsync(t);
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
    sealed class Reader(IControlledObservationSource inner, FaultFactory f) : IControlledObservationSource, IReaderImageCapture, ISyntheticObservationInput
    {
        public string DeviceId => inner.DeviceId;
        public string Epoch => inner.Epoch;
        public string ConfirmationSource => inner.ConfirmationSource;
        public Task StartAsync(Func<Observation, bool> r, Action<string, string, DeviceHealth> h, CancellationToken t) => inner.StartAsync(r, h, t);
        public Task StartControlledAsync(string e, Func<Observation, bool> r, Action<DeviceHealthConfirmation> h, CancellationToken t) => inner.StartControlledAsync(e, r, h, t);
        public bool Emit(string k, string a, string[] c, int? n, string s)
        { var accepted = ((ISyntheticObservationInput)inner).Emit(k, a, c, n, s); if (f.MutateObservation && a == "array-original") c[0] = "999"; return accepted; }
        public Task StopAsync(CancellationToken t) { f.ReaderStopped = true; return inner.StopAsync(t); }
        public ValueTask DisposeAsync() => inner.DisposeAsync();
        public IImageCamera BindCapture(CapabilityBinding b, string d) => ((IReaderImageCapture)inner).BindCapture(b, d);
        public void RetainAcquisition(AcquisitionIdentity a, string owner) => ((IReaderImageCapture)inner).RetainAcquisition(a, owner);
        public void ReleaseAcquisition(AcquisitionIdentity a, string owner) => ((IReaderImageCapture)inner).ReleaseAcquisition(a, owner);
    }
}

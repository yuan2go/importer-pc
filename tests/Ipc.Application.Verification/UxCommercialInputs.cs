using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Assembly;
using Ipc.Platform;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster.Desktop;

static class UxCommercialInputs
{
    static void Check(bool condition, string message)
    { if (!condition) throw new Exception("FAIL UXC INPUT: " + message); Console.WriteLine("PASS UXC INPUT: " + message); }
    static async Task<ParcelSnapshot> Wait(ParcelRuntime app, Func<ParcelSnapshot, bool> predicate)
    {
        for (int i = 0; i < 200; i++) { var s = await app.SnapshotAsync(); if (predicate(s)) return s; await Task.Delay(15); }
        throw new Exception("UXC input timeout: " + Wire.Encode(await app.SnapshotAsync()));
    }
    public static async Task Run(string workspace, string root)
    {
        var config = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace, "config/sportmaster/business.json")));
        var directory = Path.Combine(root, "config"); Directory.CreateDirectory(directory);
        ExtensionStore.AtomicWrite(Path.Combine(directory, "business.json"), config);
        var launch = new BusinessLaunch(directory, Path.Combine(root, "data"), null, null);
        var factory = new InputFactory(new ParcelCapabilityFactory(launch));
        await using var app = new ParcelRuntime(launch, new SportmasterFlow(), factory);
        await app.StartAsync(default);
        Check(app.EmitReferenceAt("left", "original-left", "LEFT", "left-read") && app.EmitReferenceAt("right", "original-right", "RIGHT", "right-read"), "two positions enqueue independently");
        var before = await Wait(app, s => s.Operations.Length == 2 && s.Operations.All(o => o.State == ParcelState.SlotResolved));
        var original = before.Operations.Single(o => o.PositionId == "left");
        Check(factory.Emit(config.Topology!.Readers[0].DeviceId, "NoRead", "unread-left", [], "left-noread"), "trusted NoRead enqueues");
        var after = await Wait(app, s => s.Revision > before.Revision);
        Check(after.Positions!.Single(p => p.Id == "left").CurrentOperation is null, "latest failed input cannot present prior terminal slot as current");
        Check(after.Operations.Length == 2 && Wire.Encode(after.Operations.Single(o => o.Id == original.Id)) == Wire.Encode(original), "failed input creates no operation or delivery and preserves original result");
        var classic = new ParcelViewModel(app, "Sportmaster", "camera → WCS"); var alternate = new SupplyStationViewModel(app);
        await classic.RefreshAsync(); await alternate.RefreshDisplayAsync();
        Check(classic.Current is null && classic.CurrentSlot.Contains("未取得") && classic.CurrentInputFailed && alternate.Left.Operation is null,
            "both actual desktop viewmodels consume failed input without the original slot");
        Check(after.Inputs!.Single(i => i.PositionId == "left") is { Outcome: ParcelInputOutcome.Rejected, Observation.Kind: "NoRead", OperationId: null }, "typed failed source evidence is available without inventing an entity");
        var right = Wire.Encode(before.Operations.Single(o => o.PositionId == "right"));
        foreach (var item in new[] { ("Read", new[] { "A", "B" }, "MultipleCodes"), ("Read", new[] { "BAD\nCODE" }, "InvalidCode"), ("Read", Array.Empty<string>(), "NoCode") })
        {
            var source = Wire.Id(); factory.Emit(config.Topology.Readers[0].DeviceId, item.Item1, "failed-input", item.Item2, source);
            after = await Wait(app, s => s.Inputs!.Any(i => i.Observation.SourceEventId == source));
            Check(after.Inputs!.Single(i => i.PositionId == "left").Reason.StartsWith(item.Item3) && after.Operations.Length == 2 &&
                after.Positions!.Single(p => p.Id == "right").CurrentOperation == before.Positions!.Single(p => p.Id == "right").CurrentOperation && Wire.Encode(after.Operations.Single(o => o.PositionId == "right")) == right,
                item.Item3 + " persists raw candidates and never changes independent right result");
        }
        var observed = after.Inputs!.Single(i => i.PositionId == "left"); var inputSource = factory.Readers[config.Topology.Readers[0].DeviceId];
        Check(!inputSource.Raw(observed.Observation with { Epoch = "obsolete-epoch", Sequence = 99999 }), "old epoch callback is fenced before ingress");
        var revision = after.Revision; inputSource.Raw(observed.Observation with { Id = Wire.Id(), SourceEventId = "old-sequence", Codes = ["STALE"], Kind = "Read" });
        after = await Wait(app, s => s.Revision > revision);
        Check(Wire.Encode(after.Inputs!.Single(i => i.PositionId == "left")) == Wire.Encode(observed), "replayed sequence cannot replace failed current input");
        revision = after.Revision;
        inputSource.Raw(observed.Observation with { Id = Wire.Id(), Sequence = 500, SourceEventId = "stale-time", ObservedAt = DateTimeOffset.UtcNow.AddHours(-1), Codes = ["OLD"], Kind = "Read" });
        after = await Wait(app, s => s.Revision > revision);
        Check(Wire.Encode(after.Inputs!.Single(i => i.PositionId == "left")) == Wire.Encode(observed), "stale source timestamp is audit-only");
        factory.Emit(config.Topology.Readers[0].DeviceId, "Read", "fresh-after-stale", [], "fresh-after-stale");
        after = await Wait(app, s => s.Inputs!.Any(i => i.Observation.SourceEventId == "fresh-after-stale"));
        observed = after.Inputs!.Single(i => i.PositionId == "left");
        Check(observed.Observation.Sequence < 500 && observed.Outcome == ParcelInputOutcome.Rejected, "rejected stale input cannot poison the accepted source sequence");
        ExtensionStore.AtomicWrite(Path.Combine(root, "input-before-after.json"), new { Before = before, After = after });
        await app.DisposeAsync();
        var nextFactory = new InputFactory(new ParcelCapabilityFactory(launch));
        await using var restarted = new ParcelRuntime(launch, new SportmasterFlow(), nextFactory); await restarted.StartAsync(default);
        var recovered = await restarted.SnapshotAsync();
        Check(Wire.Encode(recovered.Inputs!.Single(i => i.PositionId == "left")) == Wire.Encode(observed) && recovered.Positions!.Single(p => p.Id == "left").CurrentOperation is null,
            "restart rebuilds latest input from original audit without resurrecting prior slot");
        Check((await restarted.HistoryAsync()).Items.Any(i => i.Operation.Id == original.Id), "original operation remains queryable history after failed input and restart");
        Check(restarted.EmitReferenceAt("left", "next-left", "LEFT", "same-code-new-entity"), "same code distinct entity is accepted after trusted new input");
        recovered = await Wait(restarted, s => s.Operations.Any(o => o.AdmissionId == "next-left" && o.State == ParcelState.SlotResolved));
        var newest = recovered.Operations.Single(o => o.AdmissionId == "next-left");
        restarted.EmitReferenceAt("left", "original-left", "LEFT", "duplicate-old-entity");
        recovered = await Wait(restarted, s => s.Inputs!.Any(i => i.Outcome == ParcelInputOutcome.Duplicate));
        Check(recovered.Positions!.Single(p => p.Id == "left").CurrentOperation == newest.Id && recovered.Operations.Length == 3,
            "late duplicate of older admission neither regresses current result nor creates delivery");
        await restarted.DisposeAsync(); // Release exact synthetic readers before the next independent fixture.
        await UnknownOriginal(workspace, root);
        await ReaderRebinding(workspace, root, true);
        await ReaderRebinding(workspace, root, false);
        AtomicAdmission(root, original);
        Console.WriteLine("PASS UXC INPUT COMPLETE");
    }
    static void AtomicAdmission(string root, ParcelOperation source)
    {
        using var repository = new ParcelRepository(Path.Combine(root, "atomic"), "sportmaster");
        var op = source with { Id = Wire.Id(), AdmissionId = "atomic-new", ObservationId = Wire.Id(), State = ParcelState.Prepared, Slot = null, Wcs = null, Plc = null };
        var o = new Observation(Wire.Id(), source.ReaderBinding!.DeviceId, "epoch", "atomic-source", 1, op.AdmissionId, "Read", [op.ParcelCode], null, DateTimeOffset.UtcNow);
        var fact = new ParcelInputSnapshot("left", o, ParcelInputOutcome.Admitted, "Synthetic", op.Id, DateTimeOffset.UtcNow, source.ReaderBinding);
        repository.Boundary = p => { if (p == "AfterInputObservedInsert") throw new IOException("Synthetic transaction cut after both inserts"); };
        try { repository.Commit(op, "ParcelObserved", "Synthetic", input: fact); throw new Exception("Expected transaction cut"); } catch (IOException) { }
        Check(repository.Revision == 0 && !repository.Load().Any() && repository.LatestInputs(new Dictionary<string, DeviceBinding> { ["left"] = source.ReaderBinding }).Length == 0,
            "transaction cut cannot persist input without its operation or revision");
    }
    static async Task ReaderRebinding(string workspace, string root, bool changeDevice)
    {
        var c = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace, "config/sportmaster/business.json")));
        var directory = Path.Combine(root, "rebind-" + changeDevice, "config"); Directory.CreateDirectory(directory); ExtensionStore.AtomicWrite(Path.Combine(directory, "business.json"), c);
        var launch = new BusinessLaunch(directory, Path.Combine(root, "rebind-" + changeDevice, "data"), null, null); string original;
        await using (var app = new ParcelRuntime(launch, new SportmasterFlow(), new InputFactory(new ParcelCapabilityFactory(launch))))
        {
            await app.StartAsync(default); app.EmitReferenceAt("left", "before-rebind", "OLD", "before-rebind");
            var s = await Wait(app, x => x.Operations.FirstOrDefault()?.State == ParcelState.SlotResolved); original = s.Operations.Single().Id;
            var r = await app.CommandAsync(new(Wire.Id(), s.Revision, "Pause")); Check(r.Status == "Persisted", "rebind pauses original owner");
            await AdministratorFixture.Authorize(app); s = await Wait(app, x => x.Availability!.Configure);
            var reader = c.Reader with { DeviceId = changeDevice ? "sportmaster.camera.replaced" : c.Reader.DeviceId, Revision = "2" };
            var candidate = s.Configuration.Effective with { Reader = reader, Topology = new([reader, c.Topology!.Readers[1]], [c.Topology.Positions[0] with { ReaderDeviceId = reader.DeviceId }, c.Topology.Positions[1]]) };
            Check((await app.StageConfigurationAsync(s.Configuration.Revision, candidate, "Synthetic controlled reader replacement")).Status == "PendingRestart", "exact reader replacement stages through existing CAS");
        }
        await using var next = new ParcelRuntime(launch, new SportmasterFlow(), new InputFactory(new ParcelCapabilityFactory(launch))); await next.StartAsync(default);
        var recovered = await next.SnapshotAsync();
        Check(recovered.Positions!.Single(p => p.Id == "left").CurrentOperation is null && recovered.Inputs!.All(i => i.PositionId != "left") && (await next.HistoryAsync()).Items.Any(i => i.Operation.Id == original),
            (changeDevice ? "new DeviceId" : "same DeviceId new Revision") + " cannot borrow the prior reader result; original stays in history");
    }
    static async Task UnknownOriginal(string workspace, string root)
    {
        var config = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace, "config/sportmaster/business.json")));
        var directory = Path.Combine(root, "unknown", "config"); Directory.CreateDirectory(directory); ExtensionStore.AtomicWrite(Path.Combine(directory, "business.json"), config);
        var launch = new BusinessLaunch(directory, Path.Combine(root, "unknown", "data"), null, null);
        var factory = new InputFactory(new ParcelCapabilityFactory(launch), true);
        await using var app = new ParcelRuntime(launch, new SportmasterFlow(), factory); await app.StartAsync(default);
        app.EmitReferenceAt("left", "unknown-original", "UNKNOWN", "unknown-read");
        var before = await Wait(app, s => s.Operations.FirstOrDefault()?.State == ParcelState.WcsUnknown); var original = before.Operations.Single();
        factory.Emit(config.Topology!.Readers[0].DeviceId, "NoRead", "failed-next", [], "unknown-noread");
        var after = await Wait(app, s => s.Inputs!.Any(i => i.Observation.SourceEventId == "unknown-noread"));
        Check(after.Positions!.Single(p => p.Id == "left").CurrentOperation == original.Id && !after.Positions!.Single(p => p.Id == "left").Admit && Wire.Encode(after.Operations.Single()) == Wire.Encode(original),
            "failed observation preserves original Unknown request/hash/attempt and admission block");
        app.EmitReferenceAt("left", "blocked-next", "BLOCKED", "blocked-read");
        after = await Wait(app, s => s.Inputs!.Any(i => i.Observation.SourceEventId == "blocked-read"));
        Check(after.Inputs!.Single(i => i.PositionId == "left").Outcome == ParcelInputOutcome.Blocked && after.Operations.Length == 1 && after.Operations.Single().Wcs!.Attempts == 1,
            "blocked next entity records input without resending Unknown");
        ExtensionStore.AtomicWrite(Path.Combine(root, "unknown-input.json"), new { Before = before, After = after });
    }
    internal sealed class InputFactory(ParcelCapabilityFactory inner, bool loseReply = false) : IParcelCapabilityFactory
    {
        public Dictionary<string, InputSource> Readers { get; } = [];
        public bool Emit(string device, string kind, string admission, string[] codes, string source) => Readers[device].Emit(kind, admission, codes, null, source);
        public CapabilityCatalog Catalog() => inner.Catalog();
        public ParcelConfiguration Normalize(ParcelConfiguration c) => inner.Normalize(c);
        public void Validate(ParcelConfiguration c) => inner.Validate(c);
        public CapabilityRequirements Requirements(CapabilityBinding b) => inner.Requirements(b);
        public ParcelPorts Create(ParcelConfiguration c)
        {
            var p = inner.Create(c); foreach (var r in p.Readers!.Values) Readers.Add(r.DeviceId, new(r));
            return p with { Reader = Readers[p.Reader.DeviceId], Readers = Readers.ToDictionary(x => x.Key, x => (IControlledObservationSource)x.Value), Wcs = loseReply ? new LostReply(p.Wcs!) : p.Wcs };
        }
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
    internal sealed class InputSource(IControlledObservationSource inner) : IControlledObservationSource, ISyntheticObservationInput
    {
        Func<Observation, bool>? receive;
        public string DeviceId => inner.DeviceId;
        public string Epoch => inner.Epoch;
        public string ConfirmationSource => inner.ConfirmationSource;
        public bool Raw(Observation o) => receive!(o);
        public bool Emit(string kind, string admission, string[] codes, int? quantity, string source) => ((ISyntheticObservationInput)inner).Emit(kind, admission, codes, quantity, source);
        public Task StartControlledAsync(string epoch, Func<Observation, bool> callback, Action<DeviceHealthConfirmation> health, CancellationToken token)
        { receive = callback; return inner.StartControlledAsync(epoch, callback, health, token); }
        public Task StartAsync(Func<Observation, bool> callback, Action<string, string, DeviceHealth> health, CancellationToken token) => inner.StartAsync(callback, health, token);
        public Task StopAsync(CancellationToken token) => inner.StopAsync(token);
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
    sealed class LostReply(IWcsPort inner) : IWcsPort
    {
        public string InstanceId => inner.InstanceId;
        public Task StartAsync(CancellationToken token) => inner.StartAsync(token);
        public Task<CapabilityHealth> HealthAsync(CancellationToken token) => inner.HealthAsync(token);
        public Task ReconnectAsync(CancellationToken token) => inner.ReconnectAsync(token);
        public async Task<StageReply> ResolveSlotAsync(FrozenRequest request, CancellationToken token)
        { await inner.ResolveSlotAsync(request, token); throw new IOException("Synthetic lost reply after original peer commit"); }
        public Task<StageReply?> QueryAsync(FrozenRequest original, CancellationToken token) => inner.QueryAsync(original, token);
        public Task StopAsync(CancellationToken token) => inner.StopAsync(token);
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}

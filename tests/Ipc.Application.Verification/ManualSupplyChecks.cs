using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Assembly;
using Ipc.Platform;

internal sealed class ManualSupplyHarness : IAsyncDisposable
{
    public readonly CameraRoundHarness.RoundPeer Peer = new();
    public readonly BusinessLaunch Launch;
    public readonly ParcelConfiguration Config;
    public ManualFactory Factory = null!;
    public ParcelRuntime App = null!;
    public ManualSupplyHarness(string workspace, string folder, bool deferred, bool rightCode = true)
    {
        var seed = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace, "tests/Ipc.Application.Verification/sportmaster.synthetic.json")));
        var readers = seed.Topology!.Readers.Select((r, i) => r with { Driver = "hikrobot-mvs-reader", Parameters = deferred ? new() : new()
            { ["device.ip"] = "192.0.2." + (21 + i), ["device.serial"] = "SYNTH-OFFLINE-" + i, ["entity.mode"] = "read-result", ["acquisition.trigger"] = "off", ["code.encoding"] = "utf-8" } }).ToArray();
        var wcs = new CapabilityBinding("integration", "sportmaster-rcs", "1.0.0", seed.Wcs!.InstanceId, Peer.Endpoint, "SPORTMASTER-RCS-SORTER-v1", new()
        { ["zone-id"] = "71", ["project-id"] = "83", ["specified-chute"] = "false", ["request-id-base"] = "830000000", ["request-id-count"] = "100000",
          ["heartbeat-interval-seconds"] = "3600", ["left-station-code"] = "MANUAL-STATION", ["right-station-code"] = "MANUAL-STATION",
          ["left-bcr-code"] = "MANUAL-LEFT", ["right-bcr-code"] = rightCode ? "MANUAL-RIGHT" : "" });
        Config = seed with { Mode = RunMode.Production, Reader = readers[0], Topology = seed.Topology with { Readers = readers }, Wcs = wcs,
            Login = null, DeferredConnections = deferred ? readers.Select(r => r.DeviceId).ToArray() : [] };
        var config = Path.Combine(folder, "config"); Directory.CreateDirectory(config);
        ExtensionStore.AtomicWrite(Path.Combine(config, "business.json"), Config);
        Launch = new(config, Path.Combine(folder, "data"), null, null);
    }
    public async Task StartAsync(bool ready = true)
    {
        Factory = new(Launch);
        App = new(Launch, new SportmasterFlow(), Factory) { RuntimeIdentityPreflight = () => { } };
        await App.StartAsync(default);
        if (ready) await CameraRoundHarness.Until(async () => (await App.SnapshotAsync()).Positions!.All(p => p.BarcodeInput?.Manual == true));
    }
    public async Task<SubmitParcelBarcode> Command(string position, string code, ParcelBarcodeInputKind kind = ParcelBarcodeInputKind.Keyboard)
    {
        var s = await App.SnapshotAsync();
        return new(Wire.Id(), s.RuntimeId, s.Configuration.Revision, position, code, kind,
            s.Inputs?.SingleOrDefault(i => i.PositionId == position)?.Observation.Id, s.Positions!.Single(p => p.Id == position).CurrentOperation);
    }
    public async ValueTask DisposeAsync() { if (App is not null) await App.DisposeAsync(); await Peer.DisposeAsync(); }

    public sealed class ManualFactory(BusinessLaunch launch) : IParcelCapabilityFactory
    {
        readonly ParcelCapabilityFactory validation = new(launch);
        public bool RcsOffline;
        public readonly Dictionary<string, SilentCamera> Cameras = [];
        public ParcelConfiguration Normalize(ParcelConfiguration c) => validation.Normalize(c);
        public void Validate(ParcelConfiguration c) => validation.ValidateStructure(c);
        public void ValidateStructure(ParcelConfiguration c) => validation.ValidateStructure(c);
        public CapabilityCatalog Catalog() => validation.Catalog();
        public CapabilityRequirements Requirements(CapabilityBinding b) => validation.Requirements(b);
        public ParcelPorts Create(ParcelConfiguration c)
        {
            var readers = new Dictionary<string, IControlledObservationSource>();
            foreach (var reader in ParcelBindings.Readers(c))
            {
                if (ParcelConnectionSetup.IsDeferred(c, reader.DeviceId)) readers.Add(reader.DeviceId, new DeferredObservationSource(reader.DeviceId));
                else { var source = new SilentCamera(reader.DeviceId); readers.Add(reader.DeviceId, source); Cameras.Add(reader.DeviceId, source); }
            }
            var wcs = new Ipc.Integrations.SportmasterRcs.SportmasterRcsTaskService(c.Wcs!); wcs.ConfigureRuntime(c);
            return new(readers[c.Reader.DeviceId], null, new RcsHealth(wcs, this), null,
                new Dictionary<string, string> { [wcs.InstanceId] = "synthetic-manual-rcs-implementation" }, Readers: readers);
        }
        public ValueTask DisposeAsync() => validation.DisposeAsync();
    }
    sealed class RcsHealth(Ipc.Integrations.SportmasterRcs.SportmasterRcsTaskService inner, ManualFactory owner) : IWcsPort, IWcsRequestPreparer
    {
        public string InstanceId => inner.InstanceId;
        public Task StartAsync(CancellationToken t) => inner.StartAsync(t);
        public async Task<CapabilityHealth> HealthAsync(CancellationToken t) { var h = await inner.HealthAsync(t); return owner.RcsOffline ? h with { Ready = false } : h; }
        public Task ReconnectAsync(CancellationToken t) => inner.ReconnectAsync(t);
        public Task<StageReply> ResolveSlotAsync(FrozenRequest r, CancellationToken t) => inner.ResolveSlotAsync(r, t);
        public Task<StageReply?> QueryAsync(FrozenRequest r, CancellationToken t) => inner.QueryAsync(r, t);
        public IntegrationRequestIdRange? GetRequestIdRange(CapabilityBinding b) => inner.GetRequestIdRange(b);
        public PreparedIntegrationRequest PrepareRequest(FrozenRequest r, long s) => inner.PrepareRequest(r, s);
        public Task StopAsync(CancellationToken t) => inner.StopAsync(t);
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
    public sealed class SilentCamera(string device) : IControlledObservationSource, IOriginalFrameSource
    {
        public string DeviceId => device;
        public string Epoch => epoch;
        public string ConfirmationSource => "synthetic-disconnected";
        string epoch = ""; Func<Observation, bool>? receive;
        public Task StartControlledAsync(string registeredEpoch, Func<Observation, bool> accept, Action<DeviceHealthConfirmation> health, CancellationToken t)
        { epoch = registeredEpoch; receive = accept; health(new(device, epoch, DeviceHealth.Disconnected, DateTimeOffset.UtcNow, null, "synthetic disconnected camera")); return Task.CompletedTask; }
        public void EnableOriginalFrames() { }
        public IOriginalFrameLease RetainOriginalFrame(AcquisitionIdentity identity) => throw new InvalidOperationException("Disconnected synthetic camera has no frame");
        public void ReleaseOriginalFrame(AcquisitionIdentity identity) { }
        public void Emit() => receive!(new(Wire.Id(), device, epoch, Wire.Id(), 1, "camera." + Wire.Id(), "Read", ["CAMERA-OFFLINE"], null, DateTimeOffset.UtcNow));
        public Task StopAsync(CancellationToken t) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

internal static class ManualSupplyChecks
{
    static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); Console.WriteLine("PASS MANUAL " + message); }
    public static async Task Run(string workspace, string root)
    {
        Console.WriteLine("EvidenceRoot=" + root);
        Console.WriteLine("Production configuration, explicit synthetic disconnected/deferred camera ports, real SQLite and built-in RCS over loopback; real SDK/site NOT_RUN.");
        await using (var partial = new ManualSupplyHarness(workspace, Path.Combine(root, "missing-code"), deferred: true, rightCode: false))
        {
            await partial.StartAsync(ready: false);
            await CameraRoundHarness.Until(async () => (await partial.App.SnapshotAsync()).Positions!.Single(p => p.Id == "left").Admit);
            var status = await partial.App.SnapshotAsync();
            var denied = await partial.App.SubmitParcelBarcodeAsync(await partial.Command("right", "NO-SOURCE-CODE"), default);
            Check(status.Positions!.Single(p => p.Id == "left").Admit && !status.Positions!.Single(p => p.Id == "right").Admit &&
                denied.Status == "Rejected" && denied.Detail.Contains("条码来源编码未配置") && !denied.Detail.Contains("相机") && partial.Peer.Requests.IsEmpty,
                "missing logical source code blocks only that position with a precise reason, without requiring camera parameters");
        }
        foreach (var deferred in new[] { true, false })
        {
            await using var h = new ManualSupplyHarness(workspace, Path.Combine(root, deferred ? "deferred" : "offline"), deferred);
            await h.StartAsync();
            var s = await h.App.SnapshotAsync();
            Check(s.Positions!.All(p => p.Admit && p.BarcodeInput is { Camera: false, Manual: true }) && s.Devices!.All(d => !d.IsValid(DateTimeOffset.UtcNow)),
                "manual availability is independent of both cameras; deferred=" + deferred);
            var command = await h.Command("left", "000123456"); var receipt = await h.App.SubmitParcelBarcodeAsync(command, default);
            s = await h.App.SnapshotAsync(); var original = s.Operations.Single(o => o.Id == receipt.OperationId);
            Check(receipt.Status == "Admitted" && original.Wcs is { State: StageState.Accepted, Attempts: 1 } && original.Slot == "SYNTH-CHUTE" && h.Peer.Requests.Count == 1,
                "manual input reaches the actual RCS adapter and accepted stage with no camera; deferred=" + deferred);
            await ManagementModeFixture.Authorize(h.App);
            original = (await h.App.ReadCameraOperationDetailAsync(original.Id, default))!.Original;
            var payload = Wire.Decode<ParcelPayload>(original.Wcs!.Request.Payload);
            await h.App.ExitManagementModeAsync();
            Check(original.Acquisition is null && payload.Acquisition is null && payload.OperatorInput?.CommandId == command.CommandId && payload.OperatorInput.Kind == "Keyboard" &&
                payload.ParcelCode == "000123456" && payload.ReaderBinding!.DeviceId == s.Positions!.Single(p => p.Id == "left").ReaderDeviceId,
                "frozen original has real operator evidence/leading zeroes and no fabricated camera acquisition");
            Check((await h.App.SubmitParcelBarcodeAsync(command, default)).OperationId == original.Id && h.Peer.Requests.Count == 1, "same command returns original without HTTP replay");
            var pda = await h.Command("right", "000123457", ParcelBarcodeInputKind.PdaKeyboard);
            Check((await h.App.SubmitParcelBarcodeAsync(pda, default)).Status == "Admitted" && h.Peer.Requests.Count == 2, "PDA source independently submits at the other position");
            h.Factory.RcsOffline = true;
            await CameraRoundHarness.Until(async () => !(await h.App.SnapshotAsync()).Health.Single(x => x.InstanceId == h.Config.Wcs!.InstanceId).Ready);
            var offline = await h.App.SubmitParcelBarcodeAsync(await h.Command("right", "OFFLINE"), default);
            Check(offline.Status == "Rejected" && offline.Detail.Contains("RCS 服务离线") && !offline.Detail.Contains("相机") && h.Peer.Requests.Count == 2, "RCS offline has a precise rejection and no request");
            h.Factory.RcsOffline = false;
            await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Positions!.Any(p => p.BarcodeInput?.Manual == true));
            await ManagementModeFixture.Authorize(h.App); s = await h.App.SnapshotAsync();
            await h.App.CommandAsync(new(Wire.Id(), s.Revision, "Pause"));
            Check((await h.App.SubmitParcelBarcodeAsync(await h.Command("right", "PAUSED"), default)).Detail.Contains("供包已暂停") && h.Peer.Requests.Count == 2, "software pause still blocks manual input");
            s = await h.App.SnapshotAsync(); Check(s.Availability!.Resume, "manual availability allows resume without either camera");
            await h.App.CommandAsync(new(Wire.Id(), s.Revision, "Resume"));
            if (!deferred)
            {
                h.Factory.Cameras.Values.First().Emit();
                await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Inputs!.Any(i => i.Reason.Contains("ReaderConfirmationExpired")));
                Check(h.Peer.Requests.Count == 2, "camera observation still requires a healthy camera; manual guard cannot admit it");
            }
            h.Peer.Mode = "malformed";
            var unknown = await h.App.SubmitParcelBarcodeAsync(await h.Command("left", "UNKNOWN-MANUAL"), default);
            s = await h.App.SnapshotAsync();
            Check(s.Operations.Single(o => o.Id == unknown.OperationId).Wcs is { State: StageState.Unknown, Attempts: 1 }, "invalid RCS response retains original Unknown after one attempt");
            var denied = await h.App.SubmitParcelBarcodeAsync(await h.Command("left", "NO-REPLAY"), default);
            Check(denied.Status == "Rejected" && denied.Detail.Contains("原作业") && h.Peer.Requests.Count == 3, "unresolved original blocks the same position without another HTTP request");
            var frozen = original.Wcs.Request.IntegrationRequest!.Body;
            await h.App.DisposeAsync(); await h.StartAsync(ready: false);
            await ManagementModeFixture.Authorize(h.App); s = await h.App.SnapshotAsync();
            Check(!s.Positions!.Single(p => p.Id == "left").Admit && s.Operations.Single(o => o.Id == unknown.OperationId).Wcs is { State: StageState.Unknown, Attempts: 1 } &&
                s.Operations.Single(o => o.Id == original.Id).Wcs!.Request.IntegrationRequest!.Body == frozen && h.Peer.Requests.Count == 3,
                "restart preserves original frozen bytes/Unknown and does not replay any manual request");
        }
    }
}

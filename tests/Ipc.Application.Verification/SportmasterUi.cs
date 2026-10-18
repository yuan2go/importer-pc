using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Assembly;
using Ipc.Desktop;
using Ipc.Platform;
using Ipc.Reference.Protocol;

static class SportmasterUi
{
    static void Check(bool condition, string reason)
    {
        if (!condition) throw new Exception("FAIL SMUI: " + reason);
        Console.WriteLine("PASS SMUI: " + reason);
    }
    static async Task<ParcelSnapshot> Wait(ParcelRuntime app, Func<ParcelSnapshot, bool> predicate)
    {
        for (var i = 0; i < 200; i++)
        {
            var snapshot = await app.SnapshotAsync().WaitAsync(TimeSpan.FromSeconds(1));
            if (predicate(snapshot)) return snapshot;
            await Task.Delay(20);
        }
        throw new Exception("SMUI timeout: " + Wire.Encode(await app.SnapshotAsync()));
    }
    static ReferencePeer.PeerEntry Peer(StageFact stage) => Wire.Decode<ReferencePeer.PeerEntry>(File.ReadAllText(
        Path.Combine(stage.Request.Binding.Parameters["peer-root"], Wire.Hash(stage.Request.Binding.Target), stage.Request.Id + ".json")));

    public static async Task Run(string workspace, string root)
    {
        Check(DesktopLaunchOptions.Parse([], "sportmaster").Ui == "classic", "omitted --ui preserves classic default");
        var launch = DesktopLaunchOptions.Parse(["--ui", "supply-station", "--config", "config-path", "--data-root", "data-path", "--view", "sportmaster-history", "--layout", "compact"], "sportmaster");
        Check(launch.Ui == "supply-station" && launch.Business == new BusinessLaunch("config-path", "data-path", "sportmaster-history", "compact"), "UI selection preserves configuration, data, page and layout arguments");
        foreach (var application in new[] { "sportmaster", "direct-plc", "wcs-to-plc", "supply" })
            Check(DesktopLaunchOptions.Parse(["--ui", "classic"], application).Ui == "classic", application + " accepts explicit classic");
        foreach (var invalid in new[] {
            new[] { "--ui" }, new[] { "--ui", "" }, new[] { "--ui", "--view" },
            new[] { "--ui", "unknown" }, new[] { "--ui", "Supply-Station" },
            new[] { "--ui", "classic", "--ui", "supply-station" },
            new[] { "--unknown", "value" }, new[] { "--layout" },
            new[] { "--data-root", "one", "--data-root", "two" }, new[] { "orphan" }
        }) Reject(invalid, "sportmaster");
        foreach (var application in new[] { "direct-plc", "wcs-to-plc", "supply" }) Reject(["--ui", "supply-station"], application);
        static void Reject(string[] args, string application)
        {
            try { DesktopLaunchOptions.Parse(args, application); }
            catch (ArgumentException) { Check(true, application + " rejects invalid arguments before runtime construction: " + string.Join(" ", args)); return; }
            throw new Exception("Invalid launch arguments unexpectedly accepted: " + string.Join(" ", args));
        }

        var configuration = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace, "config/sportmaster/business.json")));
        async Task<ParcelRuntime> Open(ParcelConfiguration config, string run, bool loseSecondLeft = false)
        {
            var directory = Path.Combine(root, run); Directory.CreateDirectory(directory);
            ExtensionStore.AtomicWrite(Path.Combine(directory, "business.json"), config);
            var business = new BusinessLaunch(directory, Path.Combine(directory, "data"), null, null);
            var factory = new ParcelCapabilityFactory(business);
            var app = new ParcelRuntime(business, new SportmasterFlow(), loseSecondLeft ? new LostReplyFactory(factory) : factory);
            try { await app.StartAsync(CancellationToken.None); return app; }
            catch { await app.DisposeAsync(); throw; }
        }
        await using (var app = await Open(configuration, "ui-shared"))
        {
            var classic = new ParcelViewModel(app, "Sportmaster", "扫码 → WCS 格口");
            var ui = new SupplyStationViewModel(app); var auxiliary = new SupplyStationViewModel(app);
            await classic.RefreshAsync(); await ui.RefreshDisplayAsync(); await auxiliary.RefreshDisplayAsync();
            var initial = ui.Snapshot!; var leftObject = ui.Left; var rightObject = ui.Right;
            var epochs = initial.Devices!.ToDictionary(x => x.DeviceId, x => x.Epoch);
            Check(ReferenceEquals(classic.Application, app) && ReferenceEquals(ui.Application, app) && ReferenceEquals(auxiliary.Application, app) &&
                classic.Snapshot!.RuntimeId == ui.Snapshot!.RuntimeId && auxiliary.Snapshot!.RuntimeId == initial.RuntimeId,
                "classic, alternate and auxiliary models share the actual application and runtime identity");
            Check(ui.Fresh && !ui.Legacy && ui.Left.PositionId == "left" && ui.Right.PositionId == "right" && ui.Left.CanAdmit && ui.Right.CanAdmit,
                "schema3 projects two independently admissible real positions");
            ui.Left.ParcelCode = ui.Right.ParcelCode = "000000123";
            var leftAdmission = ui.Left.AdmissionId; var rightAdmission = ui.Right.AdmissionId;
            Check(leftAdmission != rightAdmission, "same barcode has independently retained entity drafts");
            await ui.EmitPositionAsync(ui.Left); await ui.EmitPositionAsync(ui.Right);
            var snapshot = await Wait(app, s => s.Operations.Length == 2 && s.Operations.All(o => o.State == ParcelState.SlotResolved));
            await ui.RefreshDisplayAsync(); await classic.RefreshAsync(); await auxiliary.RefreshDisplayAsync();
            var left = snapshot.Operations.Single(o => o.PositionId == "left"); var right = snapshot.Operations.Single(o => o.PositionId == "right");
            Check(left.Id != right.Id && left.AdmissionId == leftAdmission && right.AdmissionId == rightAdmission &&
                ui.Left.Operation!.Id == left.Id && ui.Right.Operation!.Id == right.Id && ui.Left.Barcode == "000000123" && ui.Right.Barcode == "000000123",
                "actual double-position input retains leading zeros, distinct admissions and correctly bound operations");
            Check(ui.Left.Recent.All(o => o.PositionId == "left") && ui.Right.Recent.All(o => o.PositionId == "right") &&
                ui.Left.ReaderId == left.ReaderBinding!.DeviceId && ui.Right.ReaderId == right.ReaderBinding!.DeviceId,
                "position histories and configured reader identities remain isolated");
            Check(snapshot.RuntimeId == initial.RuntimeId && snapshot.Devices!.All(d => epochs[d.DeviceId] == d.Epoch) &&
                snapshot.Operations.All(o => o.Plc is null && o.Wcs is { Attempts: 1 } && Peer(o.Wcs).Executions == 1),
                "second UI creates no runtime, device epoch, PLC phase or duplicate WCS execution");
            foreach (var op in snapshot.Operations)
            {
                var payload = Wire.Decode<ParcelPayload>(Peer(op.Wcs!).Request.Payload);
                Check(payload.PositionId == op.PositionId && payload.AdmissionId == op.AdmissionId && payload.ParcelCode == op.ParcelCode && payload.ReaderBinding == op.ReaderBinding,
                    op.PositionId + " actual receiver keeps original business payload and reader binding");
            }
            await ui.EmitPositionAsync(ui.Left); await ui.EmitPositionAsync(ui.Right); await Task.Delay(80);
            Check((await app.SnapshotAsync()).Operations.Length == 2 && (await app.SnapshotAsync()).Operations.All(o => Peer(o.Wcs!).Executions == 1),
                "repeated reference clicks retain admission identity and cannot repeat original requests");
            ui.Left.NewParcel(); var nextLeft = ui.Left.AdmissionId;
            ui.SelectedPage = "history"; await ui.RefreshDisplayAsync(); ui.SelectedPage = "maintenance"; await ui.RefreshDisplayAsync(); ui.SelectedPage = "workstation";
            Check(ReferenceEquals(ui.Left, leftObject) && ReferenceEquals(ui.Right, rightObject) && nextLeft != leftAdmission && ui.Left.AdmissionId == nextLeft &&
                ui.Right.AdmissionId == rightAdmission && ui.Left.ParcelCode == "000000123" && ui.Right.ParcelCode == "000000123",
                "refresh/navigation preserve stable position objects and each window's explicit drafts");
            Check(auxiliary.Left.AdmissionId != ui.Left.AdmissionId && auxiliary.Right.AdmissionId != ui.Right.AdmissionId,
                "auxiliary window owns drafts while sharing business facts");
            await VerifySnapshotSafety(app, ui, snapshot);
        }
        await using (var app = await Open(configuration, "ui-unknown", loseSecondLeft: true))
        {
            var ui = new SupplyStationViewModel(app); await ui.RefreshDisplayAsync();
            await ui.EmitPositionAsync(ui.Left); await ui.EmitPositionAsync(ui.Right);
            var accepted = await Wait(app, s => s.Operations.Length == 2 && s.Operations.All(o => o.State == ParcelState.SlotResolved));
            await ui.RefreshDisplayAsync(); var old = ui.Left.Operation!; var right = ui.Right.Operation!;
            Check(ui.Left.Slot == old.Slot && old.Slot is not null, "accepted prior left slot is shown before next entity");
            ui.Left.NewParcel(); await ui.EmitPositionAsync(ui.Left);
            var unknown = await Wait(app, s => s.Operations.Length == 3 && s.Operations.Any(o => o.PositionId == "left" && o.State == ParcelState.WcsUnknown));
            await ui.RefreshDisplayAsync(); var current = ui.Left.Operation!;
            Check(current.Id != old.Id && current.State == ParcelState.WcsUnknown && ui.Left.Slot == "未取得" && ui.Left.Status.Contains("待核对") && !ui.Left.CanAdmit &&
                ui.Left.Recent.Any(o => o.Id == old.Id && o.Slot == old.Slot) && ui.Right.Operation!.Id == right.Id && ui.Right.Slot == right.Slot && ui.Right.CanAdmit,
                "latest left Unknown clears displayed old slot, retains historical acceptance and leaves right position independent");
            var originalRequest = Wire.Encode(current.Wcs!.Request);
            Check(current.Wcs.Attempts == 1 && Peer(current.Wcs).Executions == 1, "lost reply follows a real independent receiver commit");
            await ui.ReconcilePositionAsync(ui.Left); await ui.RefreshDisplayAsync();
            Check(ui.Left.Operation!.State == ParcelState.SlotResolved && Wire.Encode(ui.Left.Operation.Wcs!.Request) == originalRequest &&
                ui.Left.Operation.Wcs.Attempts == 1 && Peer(ui.Left.Operation.Wcs).Executions == 1 && (await app.SnapshotAsync()).Operations.Length == unknown.Operations.Length,
                "UI reconciliation queries exact original request without another admission or send");
            Check(accepted.RuntimeId == unknown.RuntimeId, "uncertainty keeps the same owner and runtime");
        }
        var rejected = configuration with { Wcs = configuration.Wcs! with { Parameters = new(configuration.Wcs.Parameters) { ["decision"] = "reject" } } };
        await using (var app = await Open(rejected, "ui-rejected"))
        {
            var ui = new SupplyStationViewModel(app); await ui.RefreshDisplayAsync(); await ui.EmitPositionAsync(ui.Left);
            await Wait(app, s => s.Operations.FirstOrDefault()?.State == ParcelState.WcsRejected); await ui.RefreshDisplayAsync();
            Check(ui.Left.Slot == "未取得" && ui.Left.Status.Contains("明确拒绝") && ui.Left.Operation!.Wcs!.State == StageState.Rejected && ui.Right.Operation is null,
                "real WCS rejection remains distinct from Unknown or slot acceptance");
        }
        await using (var app = await Open(configuration with { SchemaVersion = 2, Topology = null }, "ui-legacy"))
        {
            var ui = new SupplyStationViewModel(app); await ui.RefreshDisplayAsync();
            Check(ui.Legacy && ui.Left.PositionId == "main" && ui.Left.CanAdmit && !ui.Right.CanAdmit && ui.Right.Operation is null && ui.Right.Status == "未启用",
                "schema2 explicitly maps single main to left and disables unsupported right");
            await ui.EmitPositionAsync(ui.Left); await Wait(app, s => s.Operations.FirstOrDefault()?.State == ParcelState.SlotResolved); await ui.RefreshDisplayAsync();
            await ui.EmitPositionAsync(ui.Right); await Task.Delay(40);
            Check(ui.Left.Operation is not null && ui.Right.Operation is null && ui.Right.Recent.Length == 0 && (await app.SnapshotAsync()).Operations.Length == 1,
                "legacy UI never mirrors one operation into a second physical position or admits right");
        }
        ExtensionStore.AtomicWrite(Path.Combine(root, "sportmaster-ui-verification.json"), new
        {
            Status = "PASS", Coverage = "CLI selection; real shared runtime; dual position projection; Unknown/rejection; snapshot failure/order; schema2 compatibility",
            Windows = "NOT_RUN", RealSdk = "BLOCKED", FieldAcceptance = "NOT_RUN"
        });
        Console.WriteLine("PASS SMUI COMPLETE: actual application integration and alternate UI projection; Windows UI NOT_RUN");
    }

    static async Task VerifySnapshotSafety(ParcelRuntime app, SupplyStationViewModel original, ParcelSnapshot prior)
    {
        var probe = new SnapshotProbe(app); var ui = new SupplyStationViewModel(probe); await ui.RefreshDisplayAsync();
        var operation = ui.Left.Operation!.Id; var updated = ui.UpdatedAt;
        ui.MarkReadFailure("synthetic connection loss");
        Check(!ui.Fresh && !ui.Left.CanAdmit && !ui.Right.CanAdmit && !ui.Left.CanReconcile && !ui.Left.CanContinue &&
            ui.Left.Operation!.Id == operation && ui.UpdatedAt == updated && ui.ReadStatus.Contains("synthetic connection loss"),
            "read failure retains original facts/time as stale and disables position actions");
        await ui.EmitPositionAsync(ui.Left); await ui.ReconcilePositionAsync(ui.Left); await ui.ContinuePositionAsync(ui.Left);
        Check(probe.Writes == 0, "stale action methods issue no application command, query or admission");
        await ui.RefreshDisplayAsync();
        Check(ui.Fresh && ui.Left.CanAdmit && ui.Left.CanReconcile, "new successful read recovers freshness without resetting business");
        ui.RefreshFreshness(DateTimeOffset.UtcNow.AddSeconds(4));
        Check(!ui.Fresh && !ui.Left.CanAdmit && !ui.Right.CanReconcile, "snapshot deadline expires even without a thrown read error");
        await ui.EmitPositionAsync(ui.Right); await ui.ReconcilePositionAsync(ui.Right);
        Check(probe.Writes == 0, "expired snapshot cannot dispatch position actions");
        probe.Read = () => Task.FromException<ParcelSnapshot>(new IOException("synthetic failed snapshot"));
        try { await ui.RefreshDisplayAsync(); throw new Exception("Expected snapshot failure"); }
        catch (IOException) { Check(!ui.Fresh && ui.ReadStatus.Contains("synthetic failed snapshot"), "thrown application snapshot failure visibly invalidates last display"); }
        probe.Read = null; await ui.RefreshDisplayAsync();
        var beforeFailure = await app.SnapshotAsync();
        var delayedFailure = new TaskCompletionSource<ParcelSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        probe.Read = () => delayedFailure.Task; var failedOlderRead = ui.RefreshDisplayAsync();
        probe.Read = () => Task.FromResult(beforeFailure); await ui.RefreshDisplayAsync();
        delayedFailure.SetException(new IOException("late obsolete read failure"));
        try { await failedOlderRead; throw new Exception("Expected obsolete read exception"); }
        catch (IOException) { Check(ui.Fresh && !ui.ReadStatus.Contains("late obsolete"), "late obsolete failure cannot invalidate newer successful snapshot"); }
        var delayedCommandFailure = new TaskCompletionSource<ParcelSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        probe.Read = () => delayedCommandFailure.Task; var readBeforeCommand = ui.RefreshDisplayAsync();
        probe.Read = null;
        await ui.RunAsync(() => ui.CommandAsync("Pause"));
        var commandSnapshot = ui.Snapshot!;
        Check(commandSnapshot.Paused && commandSnapshot.Revision > beforeFailure.Revision && ui.Fresh,
            "successful real command refresh updates snapshot through inherited RunAsync");
        delayedCommandFailure.SetException(new IOException("obsolete poll failed after successful command refresh"));
        try { await readBeforeCommand; throw new Exception("Expected pre-command poll failure"); }
        catch (IOException)
        {
            Check(ui.Fresh && ReferenceEquals(ui.Snapshot, commandSnapshot) && !ui.Left.CanAdmit &&
                !ui.ReadStatus.Contains("obsolete poll failed"),
                "old poll failure cannot invalidate newer successful inherited command refresh");
        }
        await ui.RunAsync(() => ui.CommandAsync("Resume"));
        beforeFailure = await app.SnapshotAsync();
        var rightDevice = ui.Right.ReaderId;
        var rightFault = beforeFailure with
        {
            Positions = beforeFailure.Positions!.Select(p => p.Id == "right" ? p with { Admit = false, BlockingReasons = ["synthetic right disconnect"] } : p).ToArray(),
            Devices = beforeFailure.Devices!.Select(d => d.DeviceId == rightDevice ? d with { Health = DeviceHealth.Disconnected } : d).ToArray()
        };
        probe.Read = () => Task.FromResult(rightFault); await ui.RefreshDisplayAsync();
        Check(ui.Left.Operation!.Id == operation && ui.Left.CanAdmit && ui.Left.DeviceStatus.Contains("就绪") &&
            !ui.Right.CanAdmit && ui.Right.DeviceStatus.Contains("Disconnected") && ui.Right.BlockingReasons.Contains("synthetic right disconnect"),
            "right device fault projection cannot overwrite left identity, device readiness or application guard");
        probe.Read = null; await ui.RefreshDisplayAsync();
        var older = await app.SnapshotAsync(); var delayed = new TaskCompletionSource<ParcelSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        probe.Read = () => delayed.Task; var first = ui.RefreshDisplayAsync();
        var receipt = await app.CommandAsync(new(Wire.Id(), older.Revision, "Pause"));
        Check(receipt.Status == "Persisted", "real pause advances revision for refresh ordering test");
        var newer = await app.SnapshotAsync(); probe.Read = () => Task.FromResult(newer); await ui.RefreshDisplayAsync();
        delayed.SetResult(older); await first;
        Check(ui.Snapshot!.Revision == newer.Revision && ui.Snapshot.Paused && !ui.Left.CanAdmit && ui.Snapshot.RuntimeId == prior.RuntimeId,
            "late older refresh cannot overwrite newer actual application revision or guards");
        var baselineRead = ui.Snapshot;
        probe.Read = () => Task.FromResult(older); await ui.RefreshDisplayAsync();
        Check(ReferenceEquals(ui.Snapshot, baselineRead), "lower revision is rejected even when returned by a later refresh");
        var positions = newer.Positions!.Select(p => p.Id == "left" ? p with { CurrentOperation = "missing-original" } : p).ToArray();
        probe.Read = () => Task.FromResult(newer with { Positions = positions }); await ui.RefreshDisplayAsync();
        Check(ui.Left.Operation is null && ui.Left.Slot == "未取得" && ui.Left.Status.Contains("身份未匹配") && ui.Right.Operation is not null,
            "unmatched current operation never falls back to another historical entity or position");
        probe.Read = null;
        var paused = await app.SnapshotAsync(); await app.CommandAsync(new(Wire.Id(), paused.Revision, "Resume")); await original.RefreshDisplayAsync();
    }

    sealed class LostReplyFactory(ParcelCapabilityFactory inner) : IParcelCapabilityFactory
    {
        public ParcelConfiguration Normalize(ParcelConfiguration config) => inner.Normalize(config);
        public void Validate(ParcelConfiguration config) => inner.Validate(config);
        public CapabilityRequirements Requirements(CapabilityBinding binding) => inner.Requirements(binding);
        public ParcelPorts Create(ParcelConfiguration config) { var ports = inner.Create(config); return ports with { Wcs = new LostSecondLeftReply(ports.Wcs!) }; }
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
    sealed class LostSecondLeftReply(IWcsPort inner) : IWcsPort
    {
        int leftCalls;
        public string InstanceId => inner.InstanceId;
        public Task StartAsync(CancellationToken token) => inner.StartAsync(token);
        public Task<CapabilityHealth> HealthAsync(CancellationToken token) => inner.HealthAsync(token);
        public Task ReconnectAsync(CancellationToken token) => inner.ReconnectAsync(token);
        public async Task<StageReply> ResolveSlotAsync(FrozenRequest request, CancellationToken token)
        {
            var reply = await inner.ResolveSlotAsync(request, token);
            if (Wire.Decode<ParcelPayload>(request.Payload).PositionId == "left" && Interlocked.Increment(ref leftCalls) == 2)
                throw new IOException("SMUI synthetic reply loss after independent WCS commit");
            return reply;
        }
        public Task<StageReply?> QueryAsync(FrozenRequest request, CancellationToken token) => inner.QueryAsync(request, token);
        public Task StopAsync(CancellationToken token) => inner.StopAsync(token);
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
    sealed class SnapshotProbe(IParcelApplication inner) : IParcelApplication
    {
        public Func<Task<ParcelSnapshot>>? Read;
        public int Writes;
        public Task<ParcelSnapshot> SnapshotAsync() => Read?.Invoke() ?? inner.SnapshotAsync();
        public Task<BusinessReceipt> CommandAsync(BusinessCommand command) { Writes++; return inner.CommandAsync(command); }
        public Task<BusinessProjection> QueryAsync() => inner.QueryAsync();
        public Task<BusinessReceipt> LoginAsync(string subject, string proof) { Writes++; return inner.LoginAsync(subject, proof); }
        public Task<BusinessReceipt> LogoutAsync() { Writes++; return inner.LogoutAsync(); }
        public Task<BusinessReceipt> AuthorizeMaintenanceAsync(string subject, string proof) { Writes++; return inner.AuthorizeMaintenanceAsync(subject, proof); }
        public Task<CapabilityCatalog> CapabilitiesAsync() => inner.CapabilitiesAsync();
        public Task<BusinessReceipt> InstallCapabilityAsync(string file, string sha256) { Writes++; return inner.InstallCapabilityAsync(file, sha256); }
        public Task<BusinessReceipt> StageConfigurationAsync(long revision, ParcelConfiguration candidate) { Writes++; return inner.StageConfigurationAsync(revision, candidate); }
        public Task<BusinessReceipt> ReconcileAsync(string operationId) { Writes++; return inner.ReconcileAsync(operationId); }
        public Task<BusinessReceipt> ReconnectAsync() { Writes++; return inner.ReconnectAsync(); }
        public Task<BusinessReceipt> BackupAsync(string destination) { Writes++; return inner.BackupAsync(destination); }
        public bool EmitReference(string admission, string code, string source) { Writes++; return inner.EmitReference(admission, code, source); }
        public bool EmitReferenceAt(string position, string admission, string code, string source) { Writes++; return inner.EmitReferenceAt(position, admission, code, source); }
        public bool EmitReferenceWeight(WeightMeasurement measurement) { Writes++; return inner.EmitReferenceWeight(measurement); }
        public Task<BusinessReceipt> ReconnectDeviceAsync(string device) { Writes++; return inner.ReconnectDeviceAsync(device); }
    }
}

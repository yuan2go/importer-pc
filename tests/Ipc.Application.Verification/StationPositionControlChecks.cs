using Ipc.Application.Contracts;
using Ipc.Platform;
using System.Text.Json;

internal static class StationPositionControlChecks
{
    static void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); Console.WriteLine("PASS POSITION-CONTROL " + text); }
    static async Task<SetStationPositionEnabled> Command(IParcelApplication app, string position, bool enabled)
    {
        var s = await app.SnapshotAsync();
        return new(Wire.Id(), s.RuntimeId, position, s.Positions!.Single(p => p.Id == position).SupplyControl!.Revision, enabled);
    }
    internal static async Task Run(string workspace, string root)
    {
        var source = new ManualSupplyHarness.SilentCamera("SYNTH-SESSION");
        var session = new ObservationSession(source); var accepted = 0;
        bool Receive(Observation observation) { if (!session.AcceptSequence(observation)) return false; accepted++; return true; }
        await session.StartAsync(Receive, default); source.Emit();
        session.Retire(); await source.StopAsync(default);
        await session.StartAsync(Receive, default); source.Emit();
        Check(accepted == 2, "retired session resets sequence so first observation of newly enabled camera is accepted");
        await source.DisposeAsync();
        await using (var h = new ManualSupplyHarness(workspace, Path.Combine(root, "offline"), false))
        {
            await h.StartAsync();
            var s = await h.App.SnapshotAsync();
            Check(s.Positions!.All(p => p.SupplyControl?.Mode == StationPositionMode.Enabled && p.BarcodeInput?.Manual == true), "both positions default enabled independently of offline cameras");
            var disable = await Command(h.App, "left", false);
            Check((await h.App.SetPositionEnabledAsync(disable, default)).Status == "Accepted", "ordinary operator can disable idle position without management authorization");
            s = await h.App.SnapshotAsync();
            Check(s.Positions!.Single(p => p.Id == "left") is { SupplyControl.Mode: StationPositionMode.Disabled, BarcodeInput.Manual: false } && s.Positions!.Single(p => p.Id == "right").BarcodeInput?.Manual == true, "idle disable blocks only this position");
            Check((await h.App.SetPositionEnabledAsync(disable, default)).Status == "Accepted" && (await h.App.ReadPositionControlHistoryAsync(long.MaxValue, 100, default)).Length == 1, "same command replays its receipt without a second toggle audit");
            Check((await h.App.SetPositionEnabledAsync(disable with { CommandId = Wire.Id(), Enabled = true }, default)).Status == "Conflict", "stale control revision cannot enable position");
            Check((await h.App.SubmitParcelBarcodeAsync(await h.Command("left", "SYNTH-BLOCKED"), default)).Status != "Admitted" && h.Peer.Requests.IsEmpty, "manual entry cannot bypass disabled application guard");
            var rightControl = await Command(h.App, "right", false);
            var admitted = await h.App.SubmitParcelBarcodeAsync(await h.Command("right", "SYNTH-MANUAL"), default);
            Check(admitted.Status == "Admitted" && h.Peer.Requests.Count == 1, "other offline-camera position still works through manual input and real loopback RCS");
            Check((await h.App.SetPositionEnabledAsync(rightControl, default)).Status == "Accepted", "unrelated observation revisions do not conflict with unchanged position control revision");
            Check((await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "right").SupplyControl?.Mode == StationPositionMode.Disabling, "accepted manual result with no placement-end evidence remains disabling");
            await Task.Delay(450);
            Check((await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "right").SupplyControl?.Mode == StationPositionMode.Disabling, "elapsed release window cannot fabricate physical completion");
            await h.App.DisposeAsync(); await h.StartAsync(false); await Task.Delay(250);
            Check((await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "right").SupplyControl?.Mode == StationPositionMode.Disabling,
                "restart retains the draining placement guard rather than inferring the parcel left");
            Check(h.Factory.Cameras[h.Config.Topology!.Positions.Single(p => p.Id == "left").ReaderDeviceId].Epoch.Length == 0 &&
                (await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "left").CameraConnectionSuspended, "restart never starts persisted disabled camera");
            await h.App.SetPositionEnabledAsync(await Command(h.App, "right", true), default);
            Check((await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "right").SupplyControl?.Mode == StationPositionMode.Enabled && h.Peer.Requests.Count == 1, "enable cancels draining without replaying original request");
            await h.App.DisposeAsync(); await h.StartAsync(false);
            await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Positions?.Length == 2);
            s = await h.App.SnapshotAsync();
            Check(s.Positions!.Single(p => p.Id == "left").SupplyControl?.Mode == StationPositionMode.Disabled && s.Positions!.Single(p => p.Id == "right").SupplyControl?.Mode == StationPositionMode.Enabled, "restart restores durable control facts independently of parcel history");
            var records = await h.App.ReadPositionControlHistoryAsync(long.MaxValue, 100, default);
            Check(records.Length == 3 && records.Any(r => r.State.TaskId == "SYNTH-TASK") && records.All(r => r.State.ChangedAt is not null && r.State.ChangedBy is not null), "audit retains time position actor action and task identity across restart");
            Check((await h.App.ReadPositionControlHistoryAsync(records[0].Sequence, 1, default)).Single().Sequence == records[1].Sequence, "audit cursor pages earlier records without repeating current row");
        }
        await using (var h = new CameraRoundHarness(workspace, Path.Combine(root, "camera-drain")))
        {
            await h.StartAsync(); await h.App.ExitManagementModeAsync(); await h.WaitForCameraAdmissionAsync();
            uint frame = 0;
            async Task Deliver(CameraRoundHarness.RoundPixels pixels)
            {
                var number = ++frame; h.Api(h.Left).Enqueue(pixels, number);
                await CameraRoundHarness.Until(() => Task.FromResult(h.Factory.Sources[h.Left].Timings.Snapshot().RecentFrames.Any(f => f.Number == number)));
            }
            await Deliver(CameraRoundHarness.Barcode("SYNTH-DRAIN"));
            await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Any(o => o.Wcs?.State == StageState.Accepted));
            var original = (await h.App.SnapshotAsync()).Operations.Single();
            await h.App.SetPositionEnabledAsync(await Command(h.App, "left", false), default);
            Check((await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "left").SupplyControl?.Mode == StationPositionMode.Disabling, "confirmed request alone cannot finish disabling while parcel still rests on tray");
            await Deliver(CameraRoundHarness.Barcode("SYNTH-DRAIN"));
            Check(h.Peer.Requests.Count == 1 && (await h.App.SnapshotAsync()).Devices!.All(d => d.IsValid(DateTimeOffset.UtcNow)), "draining keeps cameras connected and suppresses repeated barcode requests");
            await Deliver(CameraRoundHarness.EmptyTray); await Task.Delay(420); await Deliver(CameraRoundHarness.EmptyTray);
            Check((await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "left").SupplyControl?.Mode == StationPositionMode.Disabling, "two empty frames are insufficient placement evidence");
            await Task.Delay(420); await Deliver(CameraRoundHarness.EmptyTray);
            await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "left").SupplyControl?.Mode == StationPositionMode.Disabled);
            Check(h.Peer.Requests.Count == 1 && (await h.App.ReadPositionControlHistoryAsync(long.MaxValue, 100, default))[0].State.ChangedBy == "application", "three changed-scene frames spanning 800ms finish draining and record automatic transition");
            await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "left").CameraConnectionSuspended);
            var rightEpoch = (await h.App.SnapshotAsync()).Devices!.Single(d => d.DeviceId == h.Right).Epoch;
            var opens = h.Api(h.Left).Opens; await Task.Delay(1100);
            Check(h.Api(h.Left).Destroys == 1 && h.Api(h.Left).Finalizes == 1 && h.Api(h.Left).Opens == opens && h.Peer.Requests.Count == 1,
                "disabled position actually releases camera handle and SDK and suppresses automatic reconnect");
            await h.App.SetPositionEnabledAsync(await Command(h.App, "left", true), default);
            await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "left") is { CameraConnectionSuspended: false, BarcodeInput.Camera: true });
            Check(h.Api(h.Left).Opens == opens + 1 && (await h.App.SnapshotAsync()).Devices!.Single(d => d.DeviceId == h.Right).Epoch == rightEpoch,
                "enabling reconnects only the selected camera with a new session");
            await Deliver(CameraRoundHarness.Barcode("SYNTH-REENABLED"));
            await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Count(o => o.Wcs?.State == StageState.Accepted) == 2);
            Check(h.Peer.Requests.Count == 2 && (await h.App.SnapshotAsync()).Operations.Any(o => o.Id == original.Id), "reenabled camera admits next placement and preserves historical original");
        }
        await using (var h = new CameraRoundHarness(workspace, Path.Combine(root, "release-failed")))
        {
            await h.StartAsync(); await h.WaitForCameraAdmissionAsync();
            h.Api(h.Left).DestroyError = unchecked((int)0x80000000);
            await h.App.SetPositionEnabledAsync(await Command(h.App, "left", false), default);
            await CameraRoundHarness.Until(() => Task.FromResult(h.Api(h.Left).Stops > 0));
            await Task.Delay(1200);
            Check(!(await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "left").CameraConnectionSuspended && h.Api(h.Left).Destroys == 0 && h.Api(h.Left).Opens == 1,
                "failed handle destruction retains ownership without claiming disconnected or reconnecting");
            h.Api(h.Left).DestroyError = 0; // Allow actual shutdown to release the original synthetic handle.
        }
        await using (var h = new CameraRoundHarness(workspace, Path.Combine(root, "unknown-drain"), timeoutMs: 250))
        {
            h.Peer.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
            await h.StartAsync(); await h.WaitForCameraAdmissionAsync();
            h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-UNKNOWN-DRAIN"), 1);
            h.Api(h.Right).Enqueue(CameraRoundHarness.Barcode("SYNTH-RIGHT-DRAIN"), 1);
            await CameraRoundHarness.Until(() => Task.FromResult(h.Peer.Requests.Count == 2));
            await h.App.SetPositionEnabledAsync(await Command(h.App, "left", false), default);
            await h.App.SetPositionEnabledAsync(await Command(h.App, "right", false), default);
            await CameraRoundHarness.Until(async () =>
            {
                var state = await h.App.SnapshotAsync();
                return state.Operations.Count(o => o.State == ParcelState.WcsUnknown) == 2 &&
                    state.Health.Single(health => health.InstanceId == h.Config.Wcs!.InstanceId).RemoteConfirmation != RemoteConnectionConfirmation.ResponseReceived;
            });
            var originals = (await h.App.SnapshotAsync()).Operations;
            async Task ClearTray(string position, string camera)
            {
                for (uint number = 2; number <= 4; number++)
                {
                    h.Api(camera).Enqueue(CameraRoundHarness.EmptyTray, number);
                    await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).CameraReads
                        .Any(read => read.PositionId == position && read.Frame.FrameNumber == number));
                    if (number < 4) await Task.Delay(425);
                }
                await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == position) is
                    { SupplyControl.Mode: StationPositionMode.Disabled, CameraConnectionSuspended: true, CurrentOperation: null });
            }
            await ClearTray("left", h.Left);
            Check((await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "right").SupplyControl?.Mode == StationPositionMode.Disabling,
                "only the cleared lane disables despite both original requests being unconfirmed");
            await ClearTray("right", h.Right);
            Check(h.Peer.Requests.Count == 2 && h.Api(h.Left).Destroys == 1 && h.Api(h.Right).Destroys == 1,
                "both lanes finish disabling without heartbeats or extra requests and release their camera handles");
            await h.App.SetPositionEnabledAsync(await Command(h.App, "left", true), default);
            var delayedOriginals = h.Peer.Hold;
            h.Peer.Hold = null;
            await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "left").BarcodeInput?.Camera == true);
            h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-UNKNOWN-DRAIN"), 5);
            await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Any(o => o.Wcs?.State == StageState.Accepted));
            delayedOriginals!.TrySetResult();
            await CameraRoundHarness.Until(() => Task.FromResult(h.Peer.BusinessRepliesAttempted == 3));
            var snapshot = await h.App.SnapshotAsync();
            var history = await h.App.HistoryAsync();
            Check(h.Peer.Requests.Count == 3 && snapshot.Capacity?.HistoricalUnknown == 2 &&
                originals.All(old => snapshot.Operations.All(o => o.Id != old.Id) &&
                    history.Items.Any(item => item.Archived && item.Operation.Id == old.Id && Wire.Encode(item.Operation.Wcs) == Wire.Encode(old.Wcs))) &&
                snapshot.Positions!.Single(p => p.Id == "right").SupplyControl?.Mode == StationPositionMode.Disabled,
                "reenabling starts a new same-code request without changing the detached originals or the other position");
        }
        await using (var h = new ManualSupplyHarness(workspace, Path.Combine(root, "manual-release"), false))
        {
            h.Peer.ReplySlot = "";
            await h.StartAsync();
            var original = await h.App.SubmitParcelBarcodeAsync(await h.Command("left", "SYNTH-MANUAL-RELEASE"), default);
            var snapshot = await h.App.SnapshotAsync();
            var old = snapshot.Operations.Single();
            Check(original.Status == "Admitted" && old.State == ParcelState.WcsUnknown && h.Peer.Requests.Count == 1 &&
                snapshot.Positions!.Single(p => p.Id == "left") is { CanConfirmPlacementRemoval: true, BarcodeInput.Manual: false },
                "without camera placement evidence the operator has an explicit removal action on the blocked lane");
            var removal = new BusinessCommand(Wire.Id(), snapshot.Revision, "ConfirmPlacementRemoval", "left", old.Id);
            Check((await h.App.CommandAsync(removal with { Owner = "right" })).Status == "Rejected",
                "removal confirmation cannot release a parcel from another lane");
            Check((await h.App.CommandAsync(removal)).Status == "Persisted" &&
                (await h.App.CommandAsync(removal)).Status == "Persisted" && h.Peer.Requests.Count == 1,
                "confirming removal twice only returns the committed result and does not replay the original");
            await h.App.DisposeAsync();
            h.Peer.ReplySlot = "SYNTH-NEXT";
            await h.StartAsync();
            snapshot = await h.App.SnapshotAsync();
            Check(!snapshot.Paused && snapshot.Positions!.Single(p => p.Id == "left") is { CurrentOperation: null, BarcodeInput.Manual: true } &&
                snapshot.Capacity?.HistoricalUnknown == 1 && snapshot.Operations.All(o => o.Id != old.Id) &&
                Wire.Encode((await h.App.OperationAsync(old.Id))?.Wcs) == Wire.Encode(old.Wcs),
                "a restart preserves the removal and original request in history without reblocking the lane");
            var next = await h.App.SubmitParcelBarcodeAsync(await h.Command("left", "SYNTH-MANUAL-RELEASE"), default);
            Check(next.Status == "Admitted" && next.OperationId != old.Id && h.Peer.Requests.Count == 2,
                "a new manual same-code placement makes an independent request after removal");
        }
        await UpgradeWithIncompatibleOriginal(workspace, Path.Combine(root, "upgrade-with-original"));
        await HeartbeatSelection(workspace);
    }
    static async Task UpgradeWithIncompatibleOriginal(string workspace, string root)
    {
        await using var h = new ManualSupplyHarness(workspace, root, deferred: true, mode: RunMode.Simulation);
        await h.StartAsync(rcsImplementationFingerprint: new string('A', 64));
        var finished = await h.App.SubmitParcelBarcodeAsync(await h.Command("right", "SYNTH-FINISHED"), default);
        h.Peer.ReplySlot = "";
        var original = await h.App.SubmitParcelBarcodeAsync(await h.Command("left", "SYNTH-UPGRADE"), default);
        var old = (await h.App.SnapshotAsync()).Operations.Single(o => o.Id == original.OperationId);
        Check(old.Wcs is { State: StageState.Unknown, Attempts: 1 } && h.Peer.Requests.Count == 2,
            "old installation leaves a real frozen request with an unconfirmed RCS result");
        await h.App.DisposeAsync();

        h.Peer.ReplySlot = "SYNTH-NEW-SLOT";
        var newFingerprint = new string('B', 64);
        await h.StartAsync(ready: false, rcsImplementationFingerprint: newFingerprint);
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "right").BarcodeInput?.Manual == true);
        var restarted = await h.App.SnapshotAsync();
        Check(!restarted.Paused && h.Peer.Requests.Count == 2 &&
            restarted.Positions!.Single(p => p.Id == "left") is { CanConfirmPlacementRemoval: true, BarcodeInput.Manual: false } &&
            (await h.App.HistoryAsync()).Items.Any(row => row.Operation.Id == finished.OperationId && row.Operation.State == ParcelState.SlotResolved),
            "upgrade starts without replay, keeps the possibly occupied tray guarded, and preserves finished history");
        var repeat = await h.App.SubmitParcelBarcodeAsync(await h.Command("left", "SYNTH-UPGRADE"), default);
        Check(repeat.Status != "Admitted" && h.Peer.Requests.Count == 2,
            "the old barcode on the same placement cannot issue a new request after upgrade");

        restarted = await h.App.SnapshotAsync();
        var removal = new BusinessCommand(Wire.Id(), restarted.Revision, "ConfirmPlacementRemoval", "left", old.Id);
        Check((await h.App.CommandAsync(removal)).Status == "Persisted", "operator can confirm the old parcel was removed without management access");
        await CameraRoundHarness.Until(async () => await h.App.OperationAsync(old.Id) is null);
        Check((await h.App.HistoryAsync()).Items.All(row => row.Operation.Id != old.Id) &&
            (await h.App.SnapshotAsync()).Operations.All(o => o.Id != old.Id) && h.Peer.Requests.Count == 2,
            "after removal, the obsolete request is deleted from live work and history");

        var next = await h.App.SubmitParcelBarcodeAsync(await h.Command("left", "SYNTH-UPGRADE"), default);
        var current = (await h.App.SnapshotAsync()).Operations.Single(o => o.Id == next.OperationId);
        var sent = h.Peer.Requests.ToArray();
        using var oldMessage = JsonDocument.Parse(sent[1]);
        using var newMessage = JsonDocument.Parse(sent[^1]);
        Check(current.Wcs is { State: StageState.Accepted, Attempts: 1 } && current.Id != old.Id &&
            sent.Length == 3 && newMessage.RootElement.GetProperty("requestId").GetInt64() > oldMessage.RootElement.GetProperty("requestId").GetInt64(),
            "replacing the same barcode uses a new request number and sends only once");
        await h.App.DisposeAsync();
        await h.StartAsync(rcsImplementationFingerprint: newFingerprint);
        Check((await h.App.OperationAsync(old.Id)) is null && h.Peer.Requests.Count == 3,
            "the following startup validates retired receipt cleanup without replay or restoring the deleted request");
    }
    static async Task HeartbeatSelection(string workspace)
    {
        await using var peer = new CameraRoundHarness.RoundPeer { ReplyVehicleStates = true };
        var seed = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace, "tests/Ipc.Application.Verification/sportmaster.synthetic.json")));
        var binding = new CapabilityBinding("integration", "sportmaster-rcs", "1.0.0", seed.Wcs!.InstanceId, peer.Endpoint, "SPORTMASTER-RCS-SORTER-v1", new()
        { ["zone-id"] = "71", ["project-id"] = "83", ["left-station-code"] = "SYNTH-STATION", ["right-station-code"] = "SYNTH-STATION", ["left-bcr-code"] = "SYNTH-LEFT", ["right-bcr-code"] = "SYNTH-RIGHT", ["heartbeat-interval-ms"] = "100" });
        var config = seed with { Wcs = binding, StationCode = "SYNTH-STATION" };
        await using var service = new Ipc.Integrations.SportmasterRcs.SportmasterRcsTaskService(binding);
        peer.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously); peer.HoldWhen = _ => true;
        service.ConfigureRuntime(config); service.SetEnabledPositions(["right"]); await service.StartAsync(default);
        await CameraRoundHarness.Until(() => Task.FromResult(peer.HeartbeatRequests.Count > 0));
        using (var json = JsonDocument.Parse(peer.HeartbeatRequests.Last()))
        {
            var data = json.RootElement.GetProperty("data"); var parameters = data[0].GetProperty("params");
            Check(data.GetArrayLength() == 1 && parameters.GetProperty("bcrCode").GetString() == binding.Parameters["right-bcr-code"] && parameters.EnumerateObject().Count() == 3,
                "heartbeat uses enabled position selection with unchanged stationCode bcrCode timestamp protocol");
        }
        service.SetEnabledPositions([]); peer.Hold.TrySetResult(); peer.Hold = null; await Task.Delay(150); var count = peer.HeartbeatRequests.Count; await Task.Delay(250);
        Check(peer.HeartbeatRequests.Count == count && (await service.HealthAsync(default)).RemoteConfirmation == RemoteConnectionConfirmation.Unconfirmed, "both disabled sends no empty heartbeat and cannot claim fresh remote confirmation");
        Check((await service.HealthAsync(default)).StationVehicles.Length == 0, "late response from previous selection cannot restore disabled vehicle state");
        service.SetEnabledPositions(["left"]);
        await CameraRoundHarness.Until(() => Task.FromResult(peer.HeartbeatRequests.Count > count));
        using var last = JsonDocument.Parse(peer.HeartbeatRequests.Last());
        Check(last.RootElement.GetProperty("data")[0].GetProperty("params").GetProperty("bcrCode").GetString() == binding.Parameters["left-bcr-code"], "reenabling resumes only selected position without camera state changes");
    }
}

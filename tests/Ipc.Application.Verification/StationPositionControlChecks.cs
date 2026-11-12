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
            await CameraRoundHarness.Until(async () => (await h.App.OperationAsync(admitted.OperationId!))?.State == ParcelState.SlotResolved);
            await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Capacity!.ManagedExecutions == 0);
            Check(admitted.Status == "Admitted" && h.Peer.Requests.Count == 1, "other offline-camera position still works through manual input and real loopback RCS");
            Check((await h.App.SetPositionEnabledAsync(rightControl, default)).Status == "Accepted", "unrelated observation revisions do not conflict with unchanged position control revision");
            Check((await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "right") is
                { SupplyControl.Mode: StationPositionMode.Disabled, CurrentRoundId: null, AutomaticSuppressed: true },
                "manual successful round disables immediately without camera evidence and retains duplicate suppression");
            Check((await h.App.RoundHistoryAsync(search: (await h.App.OperationAsync(admitted.OperationId!))!.RoundId)).Items.Single().EndReason == "OperatorDisabledPosition",
                "disable records the operator action instead of claiming trusted physical removal");
            await h.App.DisposeAsync(); await h.StartAsync(false); await Task.Delay(250);
            Check((await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "right") is { SupplyControl.Mode: StationPositionMode.Disabled, AutomaticSuppressed: true },
                "restart closes old confirmed display while retaining automatic suppression independently of disabled state");
            Check(h.Factory.Cameras[h.Config.Topology!.Positions.Single(p => p.Id == "left").ReaderDeviceId].Epoch.Length == 0 &&
                (await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "left").CameraConnectionSuspended, "restart never starts persisted disabled camera");
            await h.App.SetPositionEnabledAsync(await Command(h.App, "right", true), default);
            Check((await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "right").SupplyControl?.Mode == StationPositionMode.Enabled && h.Peer.Requests.Count == 1, "enable restores the lane without replaying its original request");
            await h.App.DisposeAsync(); await h.StartAsync(false);
            await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Positions?.Length == 2);
            s = await h.App.SnapshotAsync();
            Check(s.Positions!.Single(p => p.Id == "left").SupplyControl?.Mode == StationPositionMode.Disabled && s.Positions!.Single(p => p.Id == "right").SupplyControl?.Mode == StationPositionMode.Enabled, "restart restores durable control facts independently of parcel history");
            var records = await h.App.ReadPositionControlHistoryAsync(long.MaxValue, 100, default);
            Check(records.Length == 3 && records.Any(r => r.State.TaskId == "SYNTH-TASK") && records.All(r => r.State.ChangedAt is not null && r.State.ChangedBy is not null), "audit retains the three actual operator switches, time and task identity across restart");
            Check((await h.App.ReadPositionControlHistoryAsync(records[0].Sequence, 1, default)).Single().Sequence == records[1].Sequence, "audit cursor pages earlier records without repeating current row");
        }
        await using (var h = new CameraRoundHarness(workspace, Path.Combine(root, "camera-drain")) { StationClock = new TimeStorageFaultChecks.ManualTime() })
        {
            await h.StartAsync(); await h.App.ExitManagementModeAsync(); await h.WaitForCameraAdmissionAsync();
            uint frame = 0;
            async Task Deliver(CameraRoundHarness.RoundPixels pixels)
            {
                var number = ++frame; h.Api(h.Left).Enqueue(pixels, number);
                await CameraRoundHarness.Until(() => Task.FromResult(h.Factory.Sources[h.Left].Timings.Snapshot().RecentFrames.Any(f => f.Number == number)));
            }
            await h.TrayAsync(h.Left, TrayPresence.Present);
            await Deliver(CameraRoundHarness.Barcode("SYNTH-DRAIN"));
            await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Any(o => o.Wcs?.State == StageState.Accepted));
            var original = (await h.App.SnapshotAsync()).Operations.Single();
            await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Capacity!.ManagedExecutions == 0);
            await h.App.SetPositionEnabledAsync(await Command(h.App, "left", false), default);
            Check((await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "left") is
                { SupplyControl.Mode: StationPositionMode.Disabled, CurrentRoundId: null, AutomaticSuppressed: true },
                "occupied camera lane disables immediately without waiting for a single empty frame");
            Check((await h.App.ReadPositionControlHistoryAsync(long.MaxValue, 100, default))[0].State.ChangedBy == "local-workstation",
                "disable remains an operator action without a separate automatic drain transition");
            await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "left").CameraConnectionSuspended);
            var rightEpoch = (await h.App.SnapshotAsync()).Devices!.Single(d => d.DeviceId == h.Right).Epoch;
            var opens = h.Api(h.Left).Opens; await Task.Delay(1100);
            Check(h.Api(h.Left).Destroys == 1 && h.Api(h.Left).Finalizes == 1 && h.Api(h.Left).Opens == opens && h.Peer.Requests.Count == 1,
                "disabled position actually releases camera handle and SDK and suppresses automatic reconnect");
            await h.App.SetPositionEnabledAsync(await Command(h.App, "left", true), default);
            await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "left") is { CameraConnectionSuspended: false, Arrival.Confirmed: true });
            Check(h.Api(h.Left).Opens == opens + 1 && (await h.App.SnapshotAsync()).Devices!.Single(d => d.DeviceId == h.Right).Epoch == rightEpoch,
                "enabling reconnects only the selected camera with a new session");
            await Deliver(CameraRoundHarness.Barcode("SYNTH-DRAIN"));
            Check(h.Peer.Requests.Count == 1 && (await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "left").CurrentRoundId is null,
                "reenabling over the same placement does not manufacture an empty tray or resend the resting parcel");
            await h.TrayAsync(h.Left, TrayPresence.Absent); await SmRoundFixture.NextVehicleAsync(h.App, "left");
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
        await using (var h = new CameraRoundHarness(workspace, Path.Combine(root, "unknown-disable")))
        {
            // Leave a finite window to observe the real in-flight state after
            // both software decoders have delivered, including on a loaded CI host.
            h.Config.Wcs!.Parameters["request-timeout-ms"] = "5000";
            ExtensionStore.AtomicWrite(Path.Combine(h.Launch.ConfigurationDirectory, "business.json"), h.Config);
            h.Peer.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
            await h.StartAsync(); await h.WaitForCameraAdmissionAsync();
            h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-UNKNOWN-DISABLE"), 1);
            h.Api(h.Right).Enqueue(CameraRoundHarness.Barcode("SYNTH-RIGHT-DISABLE"), 1);
            await CameraRoundHarness.Until(() => Task.FromResult(h.Peer.Requests.Count == 2));
            var submitted = (await h.App.SnapshotAsync()).Operations;
            h.Peer.HeartbeatUnavailable = true;
            await h.App.SetPositionEnabledAsync(await Command(h.App, "left", false), default);
            Check((await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "left") is
                { SupplyControl.Mode: StationPositionMode.Disabling, CurrentRoundId: not null } &&
                (await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "right").SupplyControl?.Mode == StationPositionMode.Enabled,
                "a lane with an actual outstanding call drains while the other remains enabled");
            await h.App.SetPositionEnabledAsync(await Command(h.App, "right", false), default);
            Check((await h.App.SnapshotAsync()).Positions!.All(p => p.SupplyControl?.Mode == StationPositionMode.Disabling && p.CurrentRoundId is not null && p.BarcodeInput?.Manual == false),
                "both in-flight lanes reject new input while their original calls and receipts drain");
            await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Capacity?.HistoricalUnknown == 2);
            var originals = (await h.App.HistoryAsync()).Items.Select(row => row.Operation).Where(op => submitted.Any(old => old.Id == op.Id)).ToArray();
            Check(originals.Length == 2 && originals.All(op => op.Wcs is { State: StageState.Unknown, Attempts: 1 } &&
                Wire.Encode(op.Wcs.Request) == Wire.Encode(submitted.Single(old => old.Id == op.Id).Wcs!.Request)),
                "disabled original calls finish with their exact frozen requests and truthful unknown outcomes");
            await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Positions!.All(p => p.CameraConnectionSuspended));
            Check(h.Peer.Requests.Count == 2 && h.Api(h.Left).Destroys == 1 && h.Api(h.Right).Destroys == 1,
                "both disabled cameras release without empty-tray input or extra requests during a real RCS outage");
            var delayedOriginals = h.Peer.Hold;
            h.Peer.Hold = null; h.Peer.HeartbeatUnavailable = false;
            await h.App.SetPositionEnabledAsync(await Command(h.App, "left", true), default);
            await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "left").BarcodeInput?.Camera == true);
            await h.TrayAsync(h.Left, TrayPresence.Absent);
            h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-UNKNOWN-DISABLE"), 2);
            await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Any(o => o.Wcs?.State == StageState.Accepted));
            delayedOriginals!.TrySetResult();
            await CameraRoundHarness.Until(() => Task.FromResult(h.Peer.BusinessRepliesAttempted == 3));
            var snapshot = await h.App.SnapshotAsync(); var history = await h.App.HistoryAsync();
            Check(h.Peer.Requests.Count == 3 && snapshot.Capacity?.HistoricalUnknown == 2 &&
                originals.All(old => snapshot.Operations.All(o => o.Id != old.Id) &&
                    history.Items.Any(item => item.Archived && item.Operation.Id == old.Id && Wire.Encode(item.Operation.Wcs) == Wire.Encode(old.Wcs))) &&
                snapshot.Positions!.Single(p => p.Id == "right").SupplyControl?.Mode == StationPositionMode.Disabled,
                "new same-code placement and late old responses preserve original custody and the other disabled lane");
        }

        await using (var h = new ManualSupplyHarness(workspace, Path.Combine(root, "manual-release"), false))
        {
            h.Peer.ReplySlot = "";
            await h.StartAsync();
            var original = await h.App.SubmitParcelBarcodeAsync(await h.Command("left", "SYNTH-MANUAL-RELEASE"), default);
            await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Capacity!.ManagedExecutions == 0 && (await h.App.OperationAsync(original.OperationId!))?.State == ParcelState.WcsUnknown);
            var snapshot = await h.App.SnapshotAsync();
            var old = snapshot.Operations.Single();
            Check(original.Status == "Admitted" && old.State == ParcelState.WcsUnknown && h.Peer.Requests.Count == 1 &&
                snapshot.Positions!.Single(p => p.Id == "left") is { BarcodeInput.Manual: false } lane && lane.PlacementRemovalOperation!.StartsWith(old.RoundId + ":", StringComparison.Ordinal),
                "without camera placement evidence the operator has an explicit removal action on the blocked lane");
            var removal = new BusinessCommand(Wire.Id(), snapshot.Revision, "ConfirmPlacementRemoval", "left", snapshot.Positions!.Single(p => p.Id == "left").PlacementRemovalOperation);
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
            await CameraRoundHarness.Until(() => Task.FromResult(h.Peer.Requests.Count == 2));
            Check(next.Status == "Admitted" && next.OperationId != old.Id && h.Peer.Requests.Count == 2,
                "a new manual same-code placement makes an independent request after removal");
        }
        await UpgradeWithIncompatibleOriginal(workspace, Path.Combine(root, "upgrade-with-original"));
        await DisableBeforeSend(workspace, Path.Combine(root, "disable-before-send"));
        await DisableAtomicCommit(workspace, Path.Combine(root, "disable-atomic"));
        await HeartbeatWhileDisabled(workspace, Path.Combine(root, "heartbeat-disabled"));
    }
    static async Task DisableBeforeSend(string workspace, string root)
    {
        await using var h = new ManualSupplyHarness(workspace, root, false);
        await h.StartAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.App.BeforeSmAttemptForVerification = _ => { entered.TrySetResult(); return ready.Task; };
        try
        {
            var admitted = await h.App.SubmitParcelBarcodeAsync(await h.Command("left", "SYNTH-UNSENT-DISABLE"), default);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var before = await h.App.SnapshotAsync();
            var removal = before.Positions!.Single(p => p.Id == "left").PlacementRemovalOperation;
            var disable = await Command(h.App, "left", false);
            Check((await h.App.SetPositionEnabledAsync(disable, default)).Status == "Accepted" &&
                (await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "left") is
                    { SupplyControl.Mode: StationPositionMode.Disabled, CurrentRoundId: null, PlacementRemovalOperation: null },
                "disable atomically closes a prepared manual round without any camera or removal action");
            ready.TrySetResult(); h.App.BeforeSmAttemptForVerification = null;
            await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Capacity!.ManagedExecutions == 0);
            Check(h.Peer.Requests.IsEmpty && (await h.App.OperationAsync(admitted.OperationId!)) is
                { State: ParcelState.Abandoned, Wcs.Attempts: 0, Wcs.State: StageState.Cancelled },
                "a prepared request disabled before its first attempt never reaches RCS");
            await h.App.SetPositionEnabledAsync(await Command(h.App, "left", true), default);
            var next = await h.App.SubmitParcelBarcodeAsync(await h.Command("left", "SYNTH-UNSENT-DISABLE"), default);
            await CameraRoundHarness.Until(async () => (await h.App.OperationAsync(next.OperationId!))?.State == ParcelState.SlotResolved);
            var current = (await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "left").CurrentRoundId;
            Check((await h.App.SetPositionEnabledAsync(disable, default)).Status == "Accepted" &&
                (await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "left") is { SupplyControl.Mode: StationPositionMode.Enabled } lane &&
                lane.CurrentRoundId == current && h.Peer.Requests.Count == 1,
                "replayed old disable returns its original receipt without ending a newly enabled parcel");
            var snapshot = await h.App.SnapshotAsync();
            Check((await h.App.CommandAsync(new(Wire.Id(), snapshot.Revision, "ConfirmPlacementRemoval", "left", removal))).Status != "Persisted" &&
                (await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "left").CurrentRoundId == current,
                "an old removal action cannot affect the new round after disable and enable");
        }
        finally { ready.TrySetResult(); h.App.BeforeSmAttemptForVerification = null; }
    }
    static async Task DisableAtomicCommit(string workspace, string root)
    {
        await using var h = new ManualSupplyHarness(workspace, root, false);
        await h.StartAsync();
        var admitted = await h.App.SubmitParcelBarcodeAsync(await h.Command("left", "SYNTH-ATOMIC-DISABLE"), default);
        await CameraRoundHarness.Until(async () => (await h.App.OperationAsync(admitted.OperationId!))?.State == ParcelState.SlotResolved);
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Capacity!.ManagedExecutions == 0);
        var round = (await h.App.OperationAsync(admitted.OperationId!))!.RoundId;
        var disable = await Command(h.App, "left", false);
        h.App.Boundary = point => { if (point == "BeforeBusinessCommit:PositionSupplyChanged") throw new InvalidOperationException("SyntheticDisableCommitInterrupted"); };
        try
        {
            try { await h.App.SetPositionEnabledAsync(disable, default); throw new Exception("Commit interruption was lost"); }
            catch (InvalidOperationException error) when (error.Message == "SyntheticDisableCommitInterrupted") { }
        }
        finally { h.App.Boundary = null; }
        Check((await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "left") is { SupplyControl.Mode: StationPositionMode.Enabled } lane &&
            lane.CurrentRoundId == round && (await h.App.RoundHistoryAsync(search: round)).Items.Single().EndedAt is null &&
            (await h.App.ReadPositionControlHistoryAsync(long.MaxValue, 100, default)).Length == 0,
            "interrupted disable rolls back both the supply switch and ended-round record in the actual database");
        Check((await h.App.SetPositionEnabledAsync(disable, default)).Status == "Accepted" &&
            (await h.App.RoundHistoryAsync(search: round)).Items.Single().EndReason == "OperatorDisabledPosition" && h.Peer.Requests.Count == 1,
            "retrying the uncommitted disable persists one operator transition without changing the original request");
    }
    static async Task UpgradeWithIncompatibleOriginal(string workspace, string root)
    {
        await using var h = new ManualSupplyHarness(workspace, root, deferred: true, mode: RunMode.Simulation);
        await h.StartAsync(rcsImplementationFingerprint: new string('A', 64));
        var finished = await h.App.SubmitParcelBarcodeAsync(await h.Command("right", "SYNTH-FINISHED"), default);
        await CameraRoundHarness.Until(async () => (await h.App.OperationAsync(finished.OperationId!))?.State == ParcelState.SlotResolved);
        h.Peer.ReplySlot = "";
        var original = await h.App.SubmitParcelBarcodeAsync(await h.Command("left", "SYNTH-UPGRADE"), default);
        await CameraRoundHarness.Until(async () => (await h.App.OperationAsync(original.OperationId!))?.Wcs?.State == StageState.Unknown);
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
            restarted.Positions!.Single(p => p.Id == "left") is { BarcodeInput.Manual: false } lane && lane.PlacementRemovalOperation!.StartsWith(old.RoundId + ":", StringComparison.Ordinal) &&
            (await h.App.HistoryAsync()).Items.Any(row => row.Operation.Id == finished.OperationId && row.Operation.State == ParcelState.SlotResolved),
            "upgrade starts without replay, keeps the possibly occupied tray guarded, and preserves finished history");
        var repeat = await h.App.SubmitParcelBarcodeAsync(await h.Command("left", "SYNTH-UPGRADE"), default);
        Check(repeat.Status != "Admitted" && h.Peer.Requests.Count == 2,
            "the old barcode on the same placement cannot issue a new request after upgrade");

        restarted = await h.App.SnapshotAsync();
        var removal = new BusinessCommand(Wire.Id(), restarted.Revision, "ConfirmPlacementRemoval", "left", restarted.Positions!.Single(p => p.Id == "left").PlacementRemovalOperation);
        Check((await h.App.CommandAsync(removal)).Status == "Persisted", "operator can confirm the old parcel was removed without management access");
        await CameraRoundHarness.Until(async () => await h.App.OperationAsync(old.Id) is null);
        Check((await h.App.HistoryAsync()).Items.All(row => row.Operation.Id != old.Id) &&
            (await h.App.SnapshotAsync()).Operations.All(o => o.Id != old.Id) && h.Peer.Requests.Count == 2,
            "after removal, the obsolete request is deleted from live work and history");

        var next = await h.App.SubmitParcelBarcodeAsync(await h.Command("left", "SYNTH-UPGRADE"), default);
        await CameraRoundHarness.Until(async () => (await h.App.OperationAsync(next.OperationId!))?.State == ParcelState.SlotResolved);
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
    static async Task HeartbeatWhileDisabled(string workspace, string root)
    {
        await using var h = new CameraRoundHarness(workspace, root);
        h.Peer.ReplyVehicleStates = true;
        await h.StartAsync(); await h.App.ExitManagementModeAsync(); await h.WaitForCameraAdmissionAsync();
        await h.App.SetPositionEnabledAsync(await Command(h.App, "left", false), default);
        var count = h.Peer.HeartbeatRequests.Count;
        await CameraRoundHarness.Until(() => Task.FromResult(h.Peer.HeartbeatRequests.Count > count));
        using (var json = JsonDocument.Parse(h.Peer.HeartbeatRequests.Last()))
        {
            var data = json.RootElement.GetProperty("data");
            Check(data.GetArrayLength() == 2 && data.EnumerateArray().All(item => item.GetProperty("params").EnumerateObject().Count() == 3) &&
                data.EnumerateArray().Select(item => item.GetProperty("params").GetProperty("bcrCode").GetString()).SequenceEqual(
                    new[] { h.Config.Wcs!.Parameters["left-bcr-code"], h.Config.Wcs.Parameters["right-bcr-code"] }),
                "disabling one lane retains both configured identities and the unchanged heartbeat protocol");
        }
        // Hold a real in-flight response across the second switch. It still
        // confirms RCS connectivity but cannot give a disabled lane a green cue.
        h.Peer.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously); h.Peer.HoldWhen = _ => true;
        count = h.Peer.HeartbeatRequests.Count;
        await CameraRoundHarness.Until(() => Task.FromResult(h.Peer.HeartbeatRequests.Count > count));
        await h.App.SetPositionEnabledAsync(await Command(h.App, "right", false), default);
        h.Peer.Hold.TrySetResult(); h.Peer.Hold = null;
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Positions!.All(p =>
            p.SupplyControl?.Mode == StationPositionMode.Disabled && p.CameraConnectionSuspended));
        count = h.Peer.HeartbeatRequests.Count;
        await CameraRoundHarness.Until(() => Task.FromResult(h.Peer.HeartbeatRequests.Count > count));
        var snapshot = await h.App.SnapshotAsync();
        Check(snapshot.Health.Single(health => health.InstanceId == h.Config.Wcs!.InstanceId).RemoteConfirmation == RemoteConnectionConfirmation.ResponseReceived &&
            snapshot.Positions!.All(p => p.Vehicle is null && p.BarcodeInput?.Manual == false),
            "both disabled cameras keep scheduled RCS connectivity without restoring vehicle cues or input admission");
        count = h.Peer.HeartbeatRequests.Count;
        Check(await h.App.CheckRcsHeartbeatAsync(default) == RcsHeartbeatResult.Received && h.Peer.HeartbeatRequests.Count == count + 1,
            "manual heartbeat also works when both lanes and their cameras are disabled");
        h.Peer.HeartbeatUnavailable = true;
        await CameraRoundHarness.Until(async () => !(await h.App.SnapshotAsync()).Health.Single(health => health.InstanceId == h.Config.Wcs!.InstanceId).Ready);
        h.Peer.HeartbeatUnavailable = false;
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Health.Single(health => health.InstanceId == h.Config.Wcs!.InstanceId).RemoteConfirmation == RemoteConnectionConfirmation.ResponseReceived);
        Check((await h.App.SnapshotAsync()).Positions!.All(p => p.SupplyControl?.Mode == StationPositionMode.Disabled),
            "a real RCS outage recovers automatically while both supply switches remain disabled");
        await h.App.DisposeAsync();
        count = h.Peer.HeartbeatRequests.Count;
        await h.StartAsync(waitForAllCameras: false);
        Check(h.Peer.HeartbeatRequests.Count > count && (await h.App.SnapshotAsync()).Positions!.All(p =>
            p.SupplyControl?.Mode == StationPositionMode.Disabled && p.CameraConnectionSuspended),
            "restart with both durable switches disabled starts heartbeat without opening cameras");
    }
}

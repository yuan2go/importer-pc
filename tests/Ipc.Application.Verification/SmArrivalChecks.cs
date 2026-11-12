using System.Text.Json;
using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Verification;

internal static class SmArrivalChecks
{
    static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); Console.WriteLine("PASS ARRIVAL " + message); }
    static PositionSnapshot Lane(ParcelSnapshot s, string position = "left") => s.Positions!.Single(p => p.Id == position);
    public static async Task Run(string workspace, string root)
    {
        await SmContinuousInductionChecks.Run(workspace, root);
        await SmArrivalRecoveryChecks.Run(workspace, root);
        Console.WriteLine("Synthetic RCS applies target binding separately from response delivery; real SDK/customer/site NOT_RUN");
        await using var h = new ManualSupplyHarness(workspace, Path.Combine(root, "arrivals"), deferred: false, mode: RunMode.Simulation);
        h.Config.Wcs!.Parameters["no-dest-to-exp"] = "false";
        ExtensionStore.AtomicWrite(Path.Combine(h.Launch.ConfigurationDirectory, "business.json"), h.Config);
        await h.StartAsync();
        foreach (var camera in h.Factory.Cameras.Values) camera.ConfirmConnected();
        await CameraRoundHarness.Until(async () => Lane(await h.App.SnapshotAsync()).Arrival?.CameraBoundaryReady == true);
        var cameraLeft = h.Factory.Cameras[h.Config.Topology!.Readers[0].DeviceId];
        async Task Read(string[] codes, TrayPresence? presence = null)
        {
            var before = h.App.WorkerProgress().Single(p => p.Position == "left").Processed;
            Check(cameraLeft.Read(codes, presence is { } state ? new(state, "qualified-synthetic-frame") : null), "camera handoff completes");
            await CameraRoundHarness.Until(() => Task.FromResult(h.App.WorkerProgress().Single(p => p.Position == "left").Processed > before));
        }
        async Task SetEnabled(bool enabled, string position = "left")
        {
            var s = await h.App.SnapshotAsync();
            var receipt = await h.App.SetPositionEnabledAsync(new(Wire.Id(), s.RuntimeId, position, Lane(s, position).SupplyControl!.Revision, enabled), default);
            Check(receipt.Status == "Accepted", "position switch accepted with current control identity");
            await ((IStationRcsHeartbeat)h.App).CheckRcsHeartbeatAsync(default);
        }
        async Task Resolved(string operation) => await CameraRoundHarness.Until(async () =>
            (await h.App.OperationAsync(operation))?.Wcs?.State is StageState.Accepted or StageState.Rejected or StageState.Unknown);
        var first = Lane(await h.App.SnapshotAsync()).Arrival!;
        await Read([], TrayPresence.Absent);
        Check((await h.App.SnapshotAsync()).Rounds.Length == 0, "empty arrived vehicle does not create a parcel or start a timeout");
        await Read(["SAME-001"], TrayPresence.Present);
        await Read(["SAME-001"]);
        var a = (await h.App.SnapshotAsync()).Rounds.Single(r => r.PositionId == "left");
        await Resolved(a.OperationId!);
        Check(h.Peer.Requests.Count == 1 && a.ArrivalGeneration == first.Generation, "first vehicle has exactly one bound production request");
        var repeated = await h.App.SubmitParcelBarcodeAsync(await h.Command("left", "OTHER-002"), default);
        await Read([], TrayPresence.Absent);
        await Read(["OTHER-002"]);
        Check(repeated.Status == "Rejected" && h.Peer.Requests.Count == 1, "same-car manual, changed code and visible absence do not rearm");
        var oldCommand = await h.Command("left", "SAME-001");
        await h.NextVehicleAsync("left", "CAR-B");
        var b = Lane(await h.App.SnapshotAsync()).Arrival!;
        Check(b.Generation != first.Generation && !b.Submitted && (await h.App.SnapshotAsync()).Rounds.Length == 0,
            "A with parcel to empty B resets current round and presence without waiting 800ms");
        var stale = await h.App.SubmitParcelBarcodeAsync(oldCommand, default);
        Check(stale.Status == "Conflict" && h.Peer.Requests.Count == 1, "old queued manual command cannot target B");
        await Read(["SAME-001"]);
        var roundB = (await h.App.SnapshotAsync()).Rounds.Single(r => r.PositionId == "left"); await Resolved(roundB.OperationId!);
        Check(h.Peer.Requests.Count == 2 && roundB.Id != a.Id, "consecutive same barcode on changed vehicle dispatches once");
        await h.NextVehicleAsync("left", first.VehicleNo!);
        await Read(["DIFFERENT-003"]);
        await Resolved((await h.App.SnapshotAsync()).Rounds.Single(r => r.PositionId == "left").OperationId!);
        Check(h.Peer.Requests.Count == 3, "continuous A B A creates three arrivals rather than global vehicle-number deduplication");

        // A prepared claim loses its right to call when the vehicle changes.
        await h.NextVehicleAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.App.BeforeSmAttemptForVerification = _ => { entered.TrySetResult(); return release.Task; };
        try
        {
            var prepared = await h.App.SubmitParcelBarcodeAsync(await h.Command("left", "UNSENT-004"), default);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await h.NextVehicleAsync(); release.TrySetResult();
            await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Capacity!.ManagedExecutions == 0);
            Check((await h.App.OperationAsync(prepared.OperationId!))!.Wcs is { Attempts: 0, State: StageState.Cancelled } && h.Peer.Requests.Count == 3,
                "vehicle change before T2 cancels the prepared intent with zero external calls");
        }
        finally { release.TrySetResult(); h.App.BeforeSmAttemptForVerification = null; }

        // A real business refusal leaves the car: remove via acknowledged membership,
        // never by clearing the claim or repeating its request.
        h.Peer.Mode = "no-destination";
        var refused = await h.App.SubmitParcelBarcodeAsync(await h.Command("left", "REFUSED-005"), default); await Resolved(refused.OperationId!);
        await SetEnabled(false);
        await CameraRoundHarness.Until(() => Task.FromResult(!h.Peer.RemoteMembers.Contains("MANUAL-LEFT")));
        Check(Lane(await h.App.SnapshotAsync()).SupplyControl!.Mode == StationPositionMode.Disabled, "confirmed refusal can withdraw without waiting for empty vision");
        Check(Lane(await h.App.SnapshotAsync()).Arrival!.RecoveryReason is not null, "business refusal without a no-side-effect contract retains remote protection");
        await SmRoundFixture.ReconcileRemoteAsync(h.App, "left", "SYNTHETIC peer has completed REFUSED-005 and applied withdrawal; no pending remote effects");
        h.Peer.Vehicles["MANUAL-LEFT"] = "CAR-AFTER-REFUSAL";
        h.Peer.Mode = "assigned";
        await SetEnabled(true);
        await CameraRoundHarness.Until(async () => Lane(await h.App.SnapshotAsync()).Arrival is { VehicleNo: "CAR-AFTER-REFUSAL", Confirmed: true });
        var resumed = await h.App.SubmitParcelBarcodeAsync(await h.Command("left", "RESUMED-006"), default); await Resolved(resumed.OperationId!);
        Check(h.Peer.Requests.Count == 5, "acknowledged withdrawal and a new vehicle restore production after refusal");

        // Delay actual remote binding, not just a response. A local timeout cannot
        // let an old request encounter a replacement car on this physical lane.
        await h.NextVehicleAsync();
        var bindEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var bindRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Peer.BusinessGate = body => body.Contains("LATE-BIND-007", StringComparison.Ordinal) ? WaitBinding() : Task.CompletedTask;
        async Task WaitBinding() { bindEntered.TrySetResult(); await bindRelease.Task; }
        try
        {
            var late = await h.App.SubmitParcelBarcodeAsync(await h.Command("left", "LATE-BIND-007"), default);
            await bindEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)); await Resolved(late.OperationId!);
            await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Capacity!.ManagedExecutions == 0);
            await SetEnabled(false);
            var disabled = await h.App.SnapshotAsync();
            var unsafeEnable = await h.App.SetPositionEnabledAsync(new(Wire.Id(), disabled.RuntimeId, "left", Lane(disabled).SupplyControl!.Revision, true), default);
            Check(unsafeEnable.Status == "Rejected" && !h.Peer.RemoteMembers.Contains("MANUAL-LEFT"), "timeout then withdrawal blocks unsafe re-dispatch before late target binding");
            var right = await h.App.SubmitParcelBarcodeAsync(await h.Command("right", "RIGHT-008"), default); await Resolved(right.OperationId!);
            Check(right.Status == "Admitted", "remote uncertainty is confined to its physical lane");
            bindRelease.TrySetResult();
            var originalRequestId = JsonDocument.Parse(h.Peer.Requests.Single(x => x.Contains("LATE-BIND-007", StringComparison.Ordinal))).RootElement.GetProperty("requestId").GetInt64();
            await CameraRoundHarness.Until(() => Task.FromResult(h.Peer.RemoteBindings.Any(x => x.Request == originalRequestId)));
            await ManagementModeFixture.Authorize(h.App);
            Check((await h.App.ManagementModeStatusAsync()).Active, "remote recovery requires current management authority");
            var recovery = await h.App.ConfirmRemoteRecoveryAsync(new(Wire.Id(), disabled.RuntimeId, "left", Lane(disabled).Arrival!.Generation,
                "SYNTHETIC peer original binding completed while lane withdrawn; no pending remote execution")
            { ExpectedControlRevision = Lane(disabled).SupplyControl!.Revision, ExpectedTargetOperation = Lane(disabled).Arrival!.TargetOperation, ExpectedRecoveryToken = Lane(disabled).Arrival!.RecoveryToken }, default);
            Check(recovery.Status == "Accepted", "explicit external reconciliation records evidence without changing original Unknown outcome");
            h.Peer.Vehicles["MANUAL-LEFT"] = "CAR-AFTER-RECONCILIATION";
            await SetEnabled(true);
            Check((await h.App.OperationAsync(late.OperationId!))!.Wcs!.Attempts == 1, "recovery never replays the original request");
        }
        finally { bindRelease.TrySetResult(); h.Peer.BusinessGate = null; }

        await SetEnabled(false); await SetEnabled(false, "right");
        Check(h.Peer.HeartbeatRequests.Any(body => JsonDocument.Parse(body).RootElement.GetProperty("data").GetArrayLength() == 0), "both disabled send a real empty heartbeat");
        Check((await h.App.SnapshotAsync()).Health.Any(x => x.RemoteConfirmation == RemoteConnectionConfirmation.ResponseReceived), "legal empty acknowledgement retains confirmed RCS connectivity");
    }
}

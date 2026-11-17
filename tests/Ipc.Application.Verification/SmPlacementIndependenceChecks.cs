using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Platform;

namespace Ipc.Verification;

internal static class SmPlacementIndependenceChecks
{
    static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); Console.WriteLine("PASS PLACEMENT " + message); }
    static PositionSnapshot Lane(ParcelSnapshot snapshot) => snapshot.Positions!.Single(p => p.Id == "left");

    public static async Task Run(string workspace, string root)
    {
        await using var h = new ManualSupplyHarness(workspace, Path.Combine(root, "placement-independence"), deferred: true, mode: RunMode.Simulation);
        h.Config.Wcs!.Parameters["request-timeout-ms"] = "30000";
        ExtensionStore.AtomicWrite(Path.Combine(h.Launch.ConfigurationDirectory, "business.json"), h.Config);
        await h.StartAsync();
        var vehicle = Lane(await h.App.SnapshotAsync()).Arrival!.Generation;

        // Binding must be confirmed by a response, not inferred from the peer's
        // private state or the local physical ending.
        var response = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Peer.ResponseGate = body => body.Contains("SYNTH-OLD-HELD", StringComparison.Ordinal) ? response.Task : Task.CompletedTask;
        try
        {
            var oldCommand = await h.Command("left", "SYNTH-OLD-HELD");
            var old = await h.App.SubmitParcelBarcodeAsync(oldCommand, default);
            await CameraRoundHarness.Until(() => Task.FromResult(h.Peer.Requests.Count == 1));
            var before = await h.App.SnapshotAsync();
            var oldRemoval = new BusinessCommand(Wire.Id(), before.Revision, "ConfirmPlacementRemoval", "left", Lane(before).PlacementRemovalOperation);
            Check((await h.App.CommandAsync(oldRemoval)).Status == "Persisted" && Lane(await h.App.SnapshotAsync()) is { CurrentRoundId: null, Arrival.Submitted: false } &&
                Lane(await h.App.SnapshotAsync()).Arrival!.TargetOperation == old.OperationId,
                "explicit removal ends the parcel while preserving its unconfirmed remote binding fence");
            var blocked = await h.App.SubmitParcelBarcodeAsync(await h.Command("left", "SYNTH-NEXT-READY"), default);
            Check(blocked.Status == "Rejected" && h.Peer.Requests.Count == 1 &&
                (await h.App.OperationAsync(old.OperationId!))!.Wcs?.State == StageState.Dispatching &&
                Lane(await h.App.SnapshotAsync()).Arrival!.Generation == vehicle,
                "the next parcel cannot dispatch before the old response establishes binding");
            response.TrySetResult();
            await CameraRoundHarness.Until(async () => (await h.App.OperationAsync(old.OperationId!))?.Wcs?.State == StageState.Accepted);
            var next = await h.App.SubmitParcelBarcodeAsync(await h.Command("left", "SYNTH-NEXT-READY"), default);
            await CameraRoundHarness.Until(async () => (await h.App.OperationAsync(next.OperationId!))?.Wcs?.State == StageState.Accepted);
            var nextRound = Lane(await h.App.SnapshotAsync()).CurrentRoundId;
            var after = await h.App.SnapshotAsync();
            Check(Lane(after).CurrentRoundId == nextRound && Lane(after).Arrival is { RecoveryReason: null, TargetOperation: null } &&
                after.Rounds.Single(r => r.Id == nextRound).OperationId == next.OperationId &&
                (await h.App.SubmitParcelBarcodeAsync(oldCommand, default)).OperationId == old.OperationId && h.Peer.Requests.Count == 2,
                "late response and replayed old command remain attached to their original task and cannot overwrite or resubmit the next parcel");
            var staleRemoval = oldRemoval with { Id = Wire.Id() };
            Check((await h.App.CommandAsync(staleRemoval)).Status != "Persisted" && Lane(await h.App.SnapshotAsync()).CurrentRoundId == nextRound,
                "stale removal cannot end the next parcel");
        }
        finally { response.TrySetResult(); h.Peer.ResponseGate = null; }

        await SmRoundFixture.ConfirmRemovedAsync(h.App, "left");
        h.Peer.Mode = "reject";
        var refused = await h.App.SubmitParcelBarcodeAsync(await h.Command("left", "SYNTH-REPEATED"), default);
        await CameraRoundHarness.Until(async () => (await h.App.OperationAsync(refused.OperationId!))?.Wcs?.State == StageState.Rejected);
        await SmRoundFixture.ConfirmRemovedAsync(h.App, "left");
        h.Peer.Mode = "assigned"; h.Peer.ReplySlot = "";
        var unknown = await h.App.SubmitParcelBarcodeAsync(await h.Command("left", "SYNTH-REPEATED"), default);
        await CameraRoundHarness.Until(async () => (await h.App.OperationAsync(unknown.OperationId!))?.Wcs?.State == StageState.Unknown);
        var original = (await h.App.OperationAsync(unknown.OperationId!))!.Wcs!;
        await SmRoundFixture.ConfirmRemovedAsync(h.App, "left");
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Capacity!.HistoricalUnknown == 1);
        Check(Lane(await h.App.SnapshotAsync()) is { CurrentRoundId: null, BarcodeInput.Manual: false } && unknown.OperationId != refused.OperationId,
            "refusal permits the next parcel, while a removed Unknown retains only the independent remote fence");
        await h.App.DisposeAsync();

        // Restart must retain remote uncertainty even after the local ending.
        var directory = Path.Combine(h.Launch.DataRoot, h.Config.Mode.ToString(), h.Config.StationId, "sportmaster");
        using (var repo = new ParcelRepository(directory, "sportmaster"))
        {
            var cycle = repo.Cycle("left")!;
            repo.Commit(null, "SyntheticEndedPlacementFence", "Reproduce deployed current-format ended placement", cycle: cycle with
            { SubmittedOperation = unknown.OperationId, SubmissionAttempted = true, TargetOperation = unknown.OperationId, RecoveryCause = SmRemoteRecoveryCause.RequestMayBind });
        }
        h.Peer.ReplySlot = "SYNTH-NEXT-SLOT";
        await h.StartAsync();
        Check(Lane(await h.App.SnapshotAsync()) is { CurrentRoundId: null, BarcodeInput.Manual: false, Arrival.Submitted: false } &&
            Lane(await h.App.SnapshotAsync()).Arrival!.TargetOperation == unknown.OperationId && h.Peer.Requests.Count == 4 &&
            Wire.Encode((await h.App.OperationAsync(unknown.OperationId!))!.Wcs) == Wire.Encode(original),
            "restart preserves the remote fence and exact archived Unknown evidence without reviving the ended parcel");
        await SmRoundFixture.ReconcileRemoteAsync(h.App, "left", "SYNTHETIC original task has settled, no delayed binding remains, and withdrawal is confirmed");
        var disabled = await h.App.SnapshotAsync();
        await h.App.SetPositionEnabledAsync(new(Wire.Id(), disabled.RuntimeId, "left", Lane(disabled).SupplyControl!.Revision, true), default);
        await h.App.CheckRcsHeartbeatAsync(default);
        var restarted = await h.App.SubmitParcelBarcodeAsync(await h.Command("left", "SYNTH-REPEATED"), default);
        await CameraRoundHarness.Until(async () => (await h.App.OperationAsync(restarted.OperationId!))?.Wcs?.State == StageState.Accepted);
        Check(h.Peer.Requests.Count == 5 && restarted.OperationId != unknown.OperationId && Lane(await h.App.SnapshotAsync()).Arrival!.Generation == vehicle,
            "confirmed external reconciliation admits one new same-code task without replaying the original request");
    }
}

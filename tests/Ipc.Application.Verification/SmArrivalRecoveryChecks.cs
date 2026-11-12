using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Platform;
namespace Ipc.Verification;

internal static class SmArrivalRecoveryChecks
{
    static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); Console.WriteLine("PASS ARRIVALRECOVERY " + message); }
    static PositionSnapshot Lane(ParcelSnapshot s, string position = "left") => s.Positions!.Single(p => p.Id == position);
    public static async Task Run(string workspace, string root)
    {
        await SameVehicle(workspace, root);
        await LateMembership(workspace, root);
        await BackupHeartbeat(workspace, root);
        await SharedTarget(workspace, root);
    }
    static async Task SameVehicle(string workspace, string root)
    {
        var clock = new TimeStorageFaultChecks.ManualTime();
        await using var h = new ManualSupplyHarness(workspace, Path.Combine(root, "same-car-recovery"), deferred: false, mode: RunMode.Simulation) { StationClock = clock };
        ExtensionStore.AtomicWrite(Path.Combine(h.Launch.ConfigurationDirectory, "business.json"), h.Config with { BarcodeFilter = new("", "BOTTOM-[0-9]+"), ReadTimeoutMs = 1000 });
        await h.StartAsync();
        foreach (var camera in h.Factory.Cameras.Values) camera.ConfirmConnected();
        await CameraRoundHarness.Until(async () => Lane(await h.App.SnapshotAsync()).Arrival?.CameraBoundaryReady == true);
        var left = h.Factory.Cameras[h.Config.Topology!.Readers[0].DeviceId];
        async Task Observe(string[] codes, TrayPresenceEvidence? presence = null)
        {
            var processed = h.App.WorkerProgress().Single(p => p.Position == "left").Processed;
            Check(left.Read(codes, presence), "source delivers current-generation evidence");
            await CameraRoundHarness.Until(() => Task.FromResult(h.App.WorkerProgress().Single(p => p.Position == "left").Processed > processed));
        }
        await Observe(["BOTTOM-001"]);
        Check((await h.App.SnapshotAsync()).Rounds.Length == 0 && h.Peer.Requests.IsEmpty, "excluded bottom code on an empty vehicle creates no parcel");
        await Observe([], new(TrayPresence.Present, "qualified-synthetic-placement"));
        var initial = await h.App.SnapshotAsync(); var generation = Lane(initial).Arrival!.Generation; var round = initial.Rounds.Single().Id;
        clock.Elapse(TimeSpan.FromMilliseconds(1001));
        Check((await h.App.SnapshotAsync()).Rounds.Single().AutoExpired, "original placement read window expires without being renewed by heartbeats");
        h.Peer.HeartbeatTransform = _ => "{\"invalid\":true}";
        Check(await ((IStationRcsHeartbeat)h.App).CheckRcsHeartbeatAsync(default) == RcsHeartbeatResult.Failed, "HTTP 200 with malformed protocol is not an acknowledgement");
        Check(!Lane(await h.App.SnapshotAsync()).Arrival!.Confirmed, "invalid protocol immediately withdraws valid vehicle confirmation");
        h.Peer.HeartbeatTransform = null;
        Check(await ((IStationRcsHeartbeat)h.App).CheckRcsHeartbeatAsync(default) == RcsHeartbeatResult.Received, "same vehicle can regain actual confirmation");
        Check(Lane(await h.App.SnapshotAsync()).Arrival!.Generation == generation && (await h.App.SnapshotAsync()).Rounds.Single().Id == round,
            "same unsubmitted vehicle recovery retains arrival and parcel identity");
        await Observe(["CAMERA-LATE-001"]);
        Check(h.Peer.Requests.IsEmpty, "restored confirmation does not renew expired automatic read rights");
        try { await h.App.DisposeAsync(); } catch (Exception error) { Console.WriteLine("SameVehicleStopFailure=" + error); throw; }
        await h.StartAsync(ready: false);
        await CameraRoundHarness.Until(async () => Lane(await h.App.SnapshotAsync()).Arrival?.Confirmed == true);
        var restored = (await h.App.SnapshotAsync()).Rounds.Single();
        Check(restored.Id == round && restored.AutoExpired && Lane(await h.App.SnapshotAsync()).Arrival!.Generation == generation,
            "restart restores same unsubmitted round for manual supplementation without a new generation");
        var command = await h.Command("left", "PDA-RECOVERY-002", ParcelBarcodeInputKind.PdaKeyboard);
        var admitted = await h.App.SubmitParcelBarcodeAsync(command, default);
        Check(admitted.Status == "Admitted", "camera disconnected after restart does not cancel valid same-car manual ability");
        await CameraRoundHarness.Until(async () => (await h.App.OperationAsync(admitted.OperationId!))?.Wcs?.State == StageState.Accepted);
        var replay = await h.App.SubmitParcelBarcodeAsync(command, default);
        Check(replay.OperationId == admitted.OperationId && h.Peer.Requests.Count == 1, "same command returns its original receipt without replay");
        try { await h.App.DisposeAsync(); } catch (Exception error) { Console.WriteLine("SameVehicleStopFailure=" + error); throw; }
        await h.StartAsync(ready: false);
        await CameraRoundHarness.Until(async () => Lane(await h.App.SnapshotAsync()).Arrival?.Confirmed == true);
        var duplicate = await h.App.SubmitParcelBarcodeAsync(await h.Command("left", "PDA-RECOVERY-002"), default);
        Check(duplicate.Status == "Rejected" && h.Peer.Requests.Count == 1, "same submitted vehicle remains suppressed across restart");
        await h.NextVehicleAsync();
        var next = await h.App.SubmitParcelBarcodeAsync(await h.Command("left", "PDA-RECOVERY-002"), default);
        Check(next.Status == "Admitted" && next.OperationId != admitted.OperationId, "fresh changed vehicle restores legitimate same-code manual submission");
    }
    static async Task LateMembership(string workspace, string root)
    {
        await using var h = new ManualSupplyHarness(workspace, Path.Combine(root, "late-membership"), deferred: true);
        await h.StartAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var once = 0;
        h.Peer.HeartbeatGate = async _ =>
        {
            if (Interlocked.Increment(ref once) != 1) return;
            entered.TrySetResult(); await release.Task;
        };
        try
        {
            var old = ((IStationRcsHeartbeat)h.App).CheckRcsHeartbeatAsync(default);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var s = await h.App.SnapshotAsync();
            Check((await h.App.SetPositionEnabledAsync(new(Wire.Id(), s.RuntimeId, "left", Lane(s).SupplyControl!.Revision, false), default)).Status == "Accepted",
                "local disable can proceed while the old heartbeat is still awaiting remote application");
            Check(await old == RcsHeartbeatResult.Failed, "old heartbeat can time out before remote member application");
            await CameraRoundHarness.Until(() => Task.FromResult(!h.Peer.RemoteMembers.Contains("MANUAL-LEFT")));
            Check(Lane(await h.App.SnapshotAsync()).Arrival?.RecoveryReason is not null, "member refresh is coalesced after busy manual heartbeat and preserves old uncertainty");
            release.TrySetResult();
            await CameraRoundHarness.Until(() => Task.FromResult(h.Peer.RemoteMembers.Contains("MANUAL-LEFT")));
            s = await h.App.SnapshotAsync();
            var unsafeEnable = await h.App.SetPositionEnabledAsync(new(Wire.Id(), s.RuntimeId, "left", Lane(s).SupplyControl!.Revision, true), default);
            Check(unsafeEnable.Status == "Rejected", "a physically late old membership update cannot be mistaken for safe re-enable");
            Check(await ((IStationRcsHeartbeat)h.App).CheckRcsHeartbeatAsync(default) == RcsHeartbeatResult.Received && !h.Peer.RemoteMembers.Contains("MANUAL-LEFT"),
                "fresh removal is sent, but cannot prove that old remote work was cancelled");
            var right = await h.App.SubmitParcelBarcodeAsync(await h.Command("right", "RIGHT-AFTER-LATE-003"), default);
            Check(right.Status == "Admitted" && Lane(await h.App.SnapshotAsync(), "right").Arrival?.RecoveryReason is null,
                "unchanged right membership and valid vehicle remain independent");
        }
        finally { release.TrySetResult(); h.Peer.HeartbeatGate = null; }
    }
    static async Task BackupHeartbeat(string workspace, string root)
    {
        await using var h = new ManualSupplyHarness(workspace, Path.Combine(root, "backup-heartbeat"), deferred: true);
        await h.StartAsync(); await ManagementModeFixture.Authorize(h.App);
        var s = await h.App.SnapshotAsync();
        await h.App.CommandAsync(new(Wire.Id(), s.Revision, "Pause"));
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        h.App.BackupBoundary = phase => { if (phase == "Inventory") { entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException(); } };
        Task<BusinessReceipt> backup = null!;
        var path = Path.Combine(root, "heartbeat-consistent.db");
        await LifecycleOptimization.AfterMonitorDrain(h.App, async () =>
        {
            backup = h.App.BackupAsync(path, default);
            Check(await Task.Run(() => entered.Wait(TimeSpan.FromSeconds(8))), "backup freezes the heartbeat membership lease before its source snapshot");
            return true;
        });
        try
        {
            var revision = (await h.App.SnapshotAsync()).Revision;
            for (var i = 0; i < 2; i++)
                Check(await ((IStationRcsHeartbeat)h.App).CheckRcsHeartbeatAsync(default) == RcsHeartbeatResult.Received,
                    "actual heartbeat continues during frozen backup without changing the business snapshot");
            Check((await h.App.SnapshotAsync()).Revision == revision, "heartbeat lease avoids concurrent business writes during backup");
        }
        finally { release.Set(); h.App.BackupBoundary = null; }
        Check((await backup).Status == "BackupCreated" && ParcelRecovery.InspectAnalysisBackup(path, new Ipc.Application.Sportmaster.SportmasterFlow()).DatabaseVersion == ParcelRepository.CurrentStorageVersion,
            "current-format complete backup retains heartbeat uncertainty and original recovery boundaries");
    }
    static async Task SharedTarget(string workspace, string root)
    {
        await using var h = new ManualSupplyHarness(workspace, Path.Combine(root, "shared-target"), deferred: true, leftCode: false);
        await h.StartAsync();
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Peer.Hold = held;
        try
        {
            var left = await h.App.SubmitParcelBarcodeAsync(await h.Command("left", "NO-BCR-REAL-001"), default);
            await CameraRoundHarness.Until(() => Task.FromResult(h.Peer.Requests.Count == 1));
            var right = await h.App.SubmitParcelBarcodeAsync(await h.Command("right", "SAME-PHYSICAL-002"), default);
            Check(left.Status == "Admitted" && right.Status == "Rejected" && h.Peer.Requests.Count == 1,
                "no-bcr manual remains legal but its real target is coordinated with the same station's coded lane");
            await ManagementModeFixture.Authorize(h.App);
            var s = await h.App.SnapshotAsync();
            var debug = await h.App.PrepareManualRcsRequestAsync(new(Wire.Id(), s.RuntimeId, s.Configuration.Revision, "right", "DEBUG-SAME-TARGET", null)
            { ExpectedArrivalGeneration = Lane(s, "right").Arrival!.Generation }, default);
            Check(!debug.CanSend && h.Peer.Requests.Count == 1,
                "independent debug preparation cannot bypass real target ownership");
            try { await h.App.SendCameraRoundAsync(new(Wire.Id(), s.RuntimeId, debug.Id!, debug.RequestHash!), default); throw new Exception("unsafe debug send accepted"); }
            catch (InvalidOperationException) { Check(h.Peer.Requests.Count == 1, "actual debug send rechecks shared physical responsibility before HTTP"); }
            held.TrySetResult(); h.Peer.Hold = null;
            await CameraRoundHarness.Until(async () => (await h.App.OperationAsync(left.OperationId!))?.Wcs?.State == StageState.Accepted);
            await h.NextVehicleAsync("right");
            var changed = await h.App.SnapshotAsync();
            Check(changed.Rounds.All(r => r.PositionId != "left") && !Lane(changed, "right").Arrival!.Submitted,
                "trusted target vehicle change immediately ends the no-bcr source's old presentation and releases the new arrival");
            var next = await h.App.SubmitParcelBarcodeAsync(await h.Command("right", "NEXT-TARGET-003"), default);
            Check(next.Status == "Admitted", "new confirmed target permits production while the old unsent debug intention remains independent");
        }
        finally { held.TrySetResult(); h.Peer.Hold = null; }
    }
}

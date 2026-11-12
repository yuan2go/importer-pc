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
        await DebugTarget(workspace, root);
        await UnconfiguredLane(workspace, root);
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
        var rightGeneration = Lane(await h.App.SnapshotAsync(), "right").Arrival!.Generation;
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
            // A valid response settles membership before the capability monitor
            // necessarily replaces its cached timeout health. Wait for the same
            // manual admission the workstation uses; never retry a submitted input.
            await CameraRoundHarness.Until(async () => Lane(await h.App.SnapshotAsync(), "right").BarcodeInput?.Manual == true);
            var right = await h.App.SubmitParcelBarcodeAsync(await h.Command("right", "RIGHT-AFTER-LATE-003"), default);
            var rightLane = Lane(await h.App.SnapshotAsync(), "right");
            Check(right.Status == "Admitted" && rightLane.Arrival is { RecoveryReason: null } arrival && arrival.Generation == rightGeneration,
                "unchanged right membership and valid vehicle remain independent: " + Wire.Encode(right));
        }
        finally { release.TrySetResult(); h.Peer.HeartbeatGate = null; }
    }
    static async Task BackupHeartbeat(string workspace, string root)
    {
        await using var h = new ManualSupplyHarness(workspace, Path.Combine(root, "backup-heartbeat"), deferred: false, mode: RunMode.Simulation);
        await h.StartAsync();
        foreach (var source in h.Factory.Cameras.Values) source.ConfirmConnected();
        await CameraRoundHarness.Until(async () => Lane(await h.App.SnapshotAsync()).Arrival?.CameraBoundaryReady == true);
        await ManagementModeFixture.Authorize(h.App);
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
            var source = h.Factory.Cameras[h.Config.Topology!.Readers[0].DeviceId];
            Check(source.Read([], new(TrayPresence.Present, "backup-preview-only")), "physical preview during backup retires normally without a durable camera write");
            for (var i = 0; i < 2; i++)
                Check(await ((IStationRcsHeartbeat)h.App).CheckRcsHeartbeatAsync(default) == RcsHeartbeatResult.Received,
                    "actual heartbeat continues during frozen backup without changing the business snapshot");
            Check((await h.App.SnapshotAsync()).Revision == revision, "heartbeat lease avoids concurrent business writes during backup");
        }
        finally { release.Set(); h.App.BackupBoundary = null; }
        Check((await backup).Status == "BackupCreated" && ParcelRecovery.InspectAnalysisBackup(path, new Ipc.Application.Sportmaster.SportmasterFlow()).DatabaseVersion == ParcelRepository.CurrentStorageVersion,
            "current-format complete backup retains heartbeat uncertainty and original recovery boundaries");
        await h.App.DisposeAsync();
        var sourceDatabase = Path.Combine(h.Launch.DataRoot, h.Config.Mode.ToString(), h.Config.StationId, "sportmaster", "sportmaster.db");
        ParcelRepository.RequireHandoffSourceEquivalent(sourceDatabase, path);
        Check(true, "normal close and unchanged-member heartbeat custody can finish after backup without making that complete restore source unusable");
    }
    static async Task DebugTarget(string workspace, string root)
    {
        await using var h = new ManualSupplyHarness(workspace, Path.Combine(root, "debug-target"), deferred: true);
        await h.StartAsync();
        var first = await h.App.SubmitParcelBarcodeAsync(await h.Command("left", "PRODUCTION-CLAIM"), default);
        await CameraRoundHarness.Until(async () => (await h.App.OperationAsync(first.OperationId!))?.Wcs?.State == StageState.Accepted);
        await ManagementModeFixture.Authorize(h.App);
        var s = await h.App.SnapshotAsync();
        var command = new PrepareManualRcsRequest(Wire.Id(), s.RuntimeId, s.Configuration.Revision, "left", "DEBUG-SAME-CAR", null)
        { ExpectedArrivalGeneration = Lane(s).Arrival!.Generation };
        var prepared = await h.App.PrepareManualRcsRequestAsync(command, default);
        Check(!prepared.CanSend, "independent debug preparation cannot take a second real submission on the same physical arrival");
        var blocked = await h.App.SendCameraRoundAsync(new(Wire.Id(), s.RuntimeId, prepared.Id!, prepared.RequestHash!), default);
        Check(blocked.SendAttempts == 0 && !blocked.CanSend && h.Peer.Requests.Count == 1, "actual debug send rechecks same-position ownership before HTTP");
        await h.NextVehicleAsync();
        s = await h.App.SnapshotAsync();
        var next = await h.App.PrepareManualRcsRequestAsync(command with { CommandId = Wire.Id(), ExpectedRoundId = prepared.Id, ExpectedArrivalGeneration = Lane(s).Arrival!.Generation }, default);
        await h.App.SendCameraRoundAsync(new(Wire.Id(), s.RuntimeId, next.Id!, next.RequestHash!), default);
        var competing = await h.App.SubmitParcelBarcodeAsync(await h.Command("left", "PRODUCTION-AFTER-DEBUG"), default);
        Check(competing.Status == "Rejected" && h.Peer.Requests.Count == 2, "real debug takes this arrival's claim and production cannot bypass it");
        var right = await h.App.SubmitParcelBarcodeAsync(await h.Command("right", "RIGHT-INDEPENDENT"), default);
        Check(right.Status == "Admitted", "mandatory distinct camera identities preserve the other physical lane's independent input");
    }
    static async Task UnconfiguredLane(string workspace, string root)
    {
        await using var h = new ManualSupplyHarness(workspace, Path.Combine(root, "unconfigured-lane"), deferred: false, mode: RunMode.Simulation);
        var blank = h.Config with { Wcs = h.Config.Wcs! with { Parameters = new(h.Config.Wcs.Parameters) { ["left-bcr-code"] = "" } } };
        ExtensionStore.AtomicWrite(Path.Combine(h.Launch.ConfigurationDirectory, "business.json"), blank);
        await h.StartAsync(); var s = await h.App.SnapshotAsync();
        Check(Lane(s) is { SupplyControl.Mode: StationPositionMode.Disabled, Arrival.Configured: false } && h.Factory.Cameras[h.Config.Topology!.Readers[0].DeviceId].Epoch.Length == 0,
            "empty camera code disables that lane without starting even its configured camera");
        var enabled = await h.App.SetPositionEnabledAsync(new(Wire.Id(), s.RuntimeId, "left", Lane(s).SupplyControl!.Revision, true), default);
        var input = await h.App.SubmitParcelBarcodeAsync(await h.Command("left", "NO-CODE-NO-SEND"), default);
        Check(enabled.Status == "Rejected" && input.Status == "Rejected" && h.Peer.Requests.IsEmpty,
            "unconfigured lane has no manual fallback or enable shortcut");
        Check(h.Peer.HeartbeatRequests.All(body => !body.Contains("MANUAL-LEFT", StringComparison.Ordinal)) && Lane(s, "right").Arrival?.Confirmed == true,
            "only configured lane participates in actual heartbeat and remains independent");
        await ManagementModeFixture.Authorize(h.App);
        var filled = blank with { Wcs = blank.Wcs! with { Parameters = new(blank.Wcs.Parameters) { ["left-bcr-code"] = "FILLED-LEFT" } } };
        var saved = await h.App.StageConfigurationAsync(s.Configuration.Revision, filled, "Configure unused lane");
        Check(saved.Status is "Effective" or "PendingRestart", "filling an unused lane uses the existing guarded configuration flow");
        if (saved.Status == "PendingRestart") { await h.App.DisposeAsync(); await h.StartAsync(); }
        await ((IStationRcsHeartbeat)h.App).CheckRcsHeartbeatAsync(default);
        s = await h.App.SnapshotAsync();
        enabled = await h.App.SetPositionEnabledAsync(new(Wire.Id(), s.RuntimeId, "left", Lane(s).SupplyControl!.Revision, true), default);
        Check(enabled.Status == "Accepted", "configured code permits deliberate enable while keeping its old disabled state until then");
        await ((IStationRcsHeartbeat)h.App).CheckRcsHeartbeatAsync(default);
        await CameraRoundHarness.Until(async () => Lane(await h.App.SnapshotAsync()).Arrival?.Confirmed == true);
        var admitted = await h.App.SubmitParcelBarcodeAsync(await h.Command("left", "FILLED-MANUAL"), default);
        Check(admitted.Status == "Admitted", "configured arrival restores legal manual input even with the physical camera disconnected");
    }
}

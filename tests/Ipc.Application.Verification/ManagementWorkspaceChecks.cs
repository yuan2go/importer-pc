using Ipc.Application.Contracts;
using Ipc.Platform;

static class ManagementWorkspaceChecks
{
    public static async Task Run(string workspace, string root)
    {
        void Check(bool value, string message) { if (!value) throw new Exception(message); Console.WriteLine("PASS MANAGEMENT-WORKSPACE " + message); }
        await RcsOfflineCameraReads.Run(workspace, Path.Combine(root, "rcs-software"), nativeResults: false);
        await using var h = new CameraRoundHarness(workspace, Path.Combine(root, "workspace"));
        await h.StartAsync(); await h.WaitForCameraAdmissionAsync();
        h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-BEFORE-MANAGEMENT"), 0);
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Any(o => o.Wcs?.State == StageState.Accepted));
        var before = await h.App.SnapshotAsync(); var id = Wire.Id();
        await h.App.SetManagementWorkspaceAsync(id, true, default);
        var held = await h.App.SnapshotAsync();
        Check(held.Positions!.All(p => !p.Admit), "opening management fences both production positions");
        Check(held.Paused == before.Paused && held.Positions!.Select(p => p.SupplyControl).SequenceEqual(before.Positions!.Select(p => p.SupplyControl)), "window does not rewrite saved pause or position switches");
        var decoded = h.Factory.Decoders[h.Left].Count;
        // Preview delivery is demand-driven: subscribe before the one synthetic
        // frame can be acquired, rather than racing acquisition on a busy runner.
        h.Factory.Sources[h.Left].ReadPreview();
        h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-HELD"), 1);
        await CameraRoundHarness.Until(() => Task.FromResult(h.Factory.Sources[h.Left].ReadPreview() is { } frame && frame.FrameNumber == 1));
        Check(h.Factory.Decoders[h.Left].Count == decoded && h.Peer.Requests.Count == 1, "live frames do not decode or request a chute while management is open");
        Check((await h.App.SnapshotAsync()).Devices!.All(d => d.IsValid(DateTimeOffset.UtcNow)), "pausing reads retains actual camera health");
        var snapshot = await h.App.SnapshotAsync();
        var scope = await h.App.BeginCameraInspectionAsync(new(Wire.Id(), snapshot.RuntimeId, snapshot.Configuration.Revision,
            CameraInspectionMode.ObserveProduction, "Synthetic explicit management read", new()) { SingleReadRounds = true }, default);
        var round = await h.App.ArmCameraReadAsync(new(Wire.Id(), snapshot.RuntimeId, snapshot.Configuration.Revision, scope.SessionId!, h.Left, null), default);
        h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-DEBUG"), 2);
        await CameraRoundHarness.Until(async () => (await h.App.ReadCameraReadRoundAsync(default)).State == "Locked");
        Check(h.Peer.Requests.Count == 1 && (await h.App.SnapshotAsync()).Operations.Count(o => o.DebugRequest is null) == 1, "explicit debug reads never create an automatic production request");
        var afterDebug = await h.App.SnapshotAsync();
        Check(afterDebug.Inputs!.Single(p => p.PositionId == "left").Observation.Codes.SequenceEqual(new[] { "SYNTH-BEFORE-MANAGEMENT" }),
            "explicit management reads preserve the original production parcel card and input evidence");
        // Force expiry cleanup to queue behind the window close, without timing
        // sleeps: close owns inspectionControl while waiting for the runtime gate.
        var fields = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var runtimeGate = (SemaphoreSlim)h.App.GetType().GetField("gate", fields)!.GetValue(h.App)!;
        var inspectionControl = (SemaphoreSlim)h.App.GetType().GetField("inspectionControl", fields)!.GetValue(h.App)!;
        var auxiliary = (List<Task>)h.App.GetType().GetField("auxiliary", fields)!.GetValue(h.App)!;
        async Task CloseWithQueuedExpiry(string window, string reason)
        {
            var clearsBeforeClose = h.Api(h.Left).Clears;
            Task close;
            Task[] expiryCleanup;
            await runtimeGate.WaitAsync();
            try
            {
                close = h.App.SetManagementWorkspaceAsync(window, false, default);
                await CameraRoundHarness.Until(() => Task.FromResult(inspectionControl.CurrentCount == 0));
                await h.App.ExitManagementModeAsync();
                var previous = auxiliary.Count;
                h.App.GetType().GetMethod("CheckCameraInspectionLease", fields)!.Invoke(h.App, null);
                expiryCleanup = auxiliary.Skip(previous).ToArray();
            }
            finally { runtimeGate.Release(); }
            await close.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.WhenAll(expiryCleanup).WaitAsync(TimeSpan.FromSeconds(5));
            Check(expiryCleanup.Length == 1 && h.Api(h.Left).Clears == clearsBeforeClose + 1, reason);
        }
        await CloseWithQueuedExpiry(id,
            "late expiry cleanup does not reset the camera again after the original window has restored production");
        await h.WaitForCameraAdmissionAsync();
        Check((await h.App.SnapshotAsync()).Paused == before.Paused, "original window can restore production even after password authorization expires");
        await h.TrayAsync(h.Left, TrayPresence.Absent);
        h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-AFTER-CLOSE"), 3);
        await CameraRoundHarness.Until(() => Task.FromResult(h.Peer.Requests.Count == 2));
        Check(h.Peer.Requests.Count == 2, "closing management resumes automatic decoding and one production request");
        await h.TrayAsync(h.Left, TrayPresence.Present);
        await h.AuthorizeAsync();
        var unarmedWindow = Wire.Id();
        await h.App.SetManagementWorkspaceAsync(unarmedWindow, true, default);
        var unarmedSnapshot = await h.App.SnapshotAsync();
        await h.App.BeginCameraInspectionAsync(new(Wire.Id(), unarmedSnapshot.RuntimeId, unarmedSnapshot.Configuration.Revision,
            CameraInspectionMode.ObserveProduction, "Synthetic close before arming", new()) { SingleReadRounds = true }, default);
        await CloseWithQueuedExpiry(unarmedWindow,
            "an inspection not yet armed also retains its management owner and cannot reset restored camera buffers");
        await h.AuthorizeAsync();
        var waitingWindow = Wire.Id();
        await h.App.SetManagementWorkspaceAsync(waitingWindow, true, default);
        var waitingSnapshot = await h.App.SnapshotAsync();
        var waitingScope = await h.App.BeginCameraInspectionAsync(new(Wire.Id(), waitingSnapshot.RuntimeId, waitingSnapshot.Configuration.Revision,
            CameraInspectionMode.ObserveProduction, "Synthetic close while waiting", new()) { SingleReadRounds = true }, default);
        var previousRound = await h.App.ReadCameraReadRoundAsync(default);
        await h.App.ArmCameraReadAsync(new(Wire.Id(), waitingSnapshot.RuntimeId, waitingSnapshot.Configuration.Revision, waitingScope.SessionId!, h.Left, previousRound.Id), default);
        await h.App.SetManagementWorkspaceAsync(waitingWindow, false, default).WaitAsync(TimeSpan.FromSeconds(5));
        Check((await h.App.ReadCameraReadRoundAsync(default)).State == "Closed" && h.Peer.Requests.Count == 2,
            "closing while waiting for a barcode cancels and drains the unsent round without waiting for a scan or sending a request");
        var current = await h.App.SnapshotAsync(); var control = current.Positions!.Single(p => p.Id == "left").SupplyControl!;
        await h.App.SetPositionEnabledAsync(new(Wire.Id(), current.RuntimeId, "left", control.Revision, false), default);
        h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-NEXT-PARCEL"), 4);
        await CameraRoundHarness.Until(() => Task.FromResult(h.Factory.Sources[h.Left].Timings.Snapshot().RecentFrames.Any(f => f.Number == 4)));
        Check((await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "left").SupplyControl?.Mode == StationPositionMode.Disabling,
            "changed barcode alone cannot complete a reliable current round while draining");
        await h.TrayAsync(h.Left, TrayPresence.Absent);
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "left").SupplyControl?.Mode == StationPositionMode.Disabled);
        Check(h.Peer.Requests.Count == 2, "qualified removal completes the original draining round without claiming the next parcel");
        await h.AuthorizeAsync();
        Check((await h.App.ManagementModeStatusAsync()).Authorized, "reopening uses the fixture original configured management password");
        h.Api(h.Right).Discovered = false;
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Devices!.Any(d => d.DeviceId == h.Right && d.Health == DeviceHealth.Disconnected));
        var offlineWindow = Wire.Id();
        await h.App.SetManagementWorkspaceAsync(offlineWindow, true, default).WaitAsync(TimeSpan.FromSeconds(5));
        Check((await h.App.SnapshotAsync()).Positions!.All(p => !p.Admit), "a disconnected camera does not block management entry for repair");
        await h.App.SetManagementWorkspaceAsync(offlineWindow, false, default).WaitAsync(TimeSpan.FromSeconds(5));
        DeviceState? restoredOffline = null;
        await CameraRoundHarness.Until(async () =>
        {
            restoredOffline = (await h.App.SnapshotAsync()).Devices!.SingleOrDefault(d => d.DeviceId == h.Right);
            return restoredOffline?.Health == DeviceHealth.Disconnected;
        });
        Check(restoredOffline!.Health == DeviceHealth.Disconnected,
            "closing management does not invent camera connectivity when it is disconnected");
        await using var failed = new CameraRoundHarness(workspace, Path.Combine(root, "failed-sdk-boundary"));
        await failed.StartAsync(); await failed.WaitForCameraAdmissionAsync();
        failed.Api(failed.Left).FailNextClear = true;
        var warning = await failed.App.SetManagementWorkspaceAsync(Wire.Id(), true, default);
        Check(warning.Status == "PausedWithCameraWarning" && (await failed.App.SnapshotAsync()).Positions!.All(p => !p.Admit),
            "an SDK cutover failure reports the pending boundary but keeps production fenced and management available for repair");


    }
}

using Ipc.Application.Contracts;
using Ipc.Platform;

static class ManagementWorkspaceChecks
{
    public static async Task Run(string workspace, string root)
    {
        void Check(bool value, string message) { if (!value) throw new Exception(message); Console.WriteLine("PASS MANAGEMENT-WORKSPACE " + message); }
        await using var h = new CameraRoundHarness(workspace, Path.Combine(root, "workspace"));
        await h.StartAsync(); await h.WaitForCameraAdmissionAsync();
        var before = await h.App.SnapshotAsync(); var id = Wire.Id();
        await h.App.SetManagementWorkspaceAsync(id, true, default);
        var held = await h.App.SnapshotAsync();
        Check(held.Positions!.All(p => !p.Admit), "opening management fences both production positions");
        Check(held.Paused == before.Paused && held.Positions!.Select(p => p.SupplyControl).SequenceEqual(before.Positions!.Select(p => p.SupplyControl)), "window does not rewrite saved pause or position switches");
        var decoded = h.Factory.Decoders[h.Left].Count;
        h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-HELD"), 1);
        await CameraRoundHarness.Until(() => Task.FromResult(h.Factory.Sources[h.Left].ReadPreview() is { } frame && frame.FrameNumber == 1));
        Check(h.Factory.Decoders[h.Left].Count == decoded && h.Peer.Requests.Count == 0, "live frames do not decode or request a chute while management is open");
        Check((await h.App.SnapshotAsync()).Devices!.All(d => d.IsValid(DateTimeOffset.UtcNow)), "pausing reads retains actual camera health");
        var snapshot = await h.App.SnapshotAsync();
        var scope = await h.App.BeginCameraInspectionAsync(new(Wire.Id(), snapshot.RuntimeId, snapshot.Configuration.Revision,
            CameraInspectionMode.ObserveProduction, "Synthetic explicit management read", new()) { SingleReadRounds = true }, default);
        var round = await h.App.ArmCameraReadAsync(new(Wire.Id(), snapshot.RuntimeId, snapshot.Configuration.Revision, scope.SessionId!, h.Left, null), default);
        h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-DEBUG"), 2);
        await CameraRoundHarness.Until(async () => (await h.App.ReadCameraReadRoundAsync(default)).State == "Locked");
        Check(h.Peer.Requests.Count == 0 && (await h.App.SnapshotAsync()).Operations.All(o => o.DebugRequest is not null), "explicit debug reads never create an automatic production request");
        await h.App.ExitManagementModeAsync();
        await h.App.SetManagementWorkspaceAsync(id, false, default);
        await h.WaitForCameraAdmissionAsync();
        Check((await h.App.SnapshotAsync()).Paused == before.Paused, "original window can restore production even after password authorization expires");
        h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-AFTER-CLOSE"), 3);
        await CameraRoundHarness.Until(() => Task.FromResult(h.Peer.Requests.Count == 1));
        Check(h.Peer.Requests.Count == 1, "closing management resumes automatic decoding and one production request");
        var current = await h.App.SnapshotAsync(); var control = current.Positions!.Single(p => p.Id == "left").SupplyControl!;
        await h.App.SetPositionEnabledAsync(new(Wire.Id(), current.RuntimeId, "left", control.Revision, false), default);
        h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-NEXT-PARCEL"), 4);
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "left").SupplyControl?.Mode == StationPositionMode.Disabled);
        Check(h.Peer.Requests.Count == 1, "a changed barcode completes the original draining placement without claiming the next parcel");
        Check(await h.EnterManagementAsync(), "reopening uses the fixture original configured management password");
        h.Api(h.Right).Discovered = false;
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Devices!.Single(d => d.DeviceId == h.Right).Health == DeviceHealth.Disconnected);
        var offlineWindow = Wire.Id();
        await h.App.SetManagementWorkspaceAsync(offlineWindow, true, default).WaitAsync(TimeSpan.FromSeconds(5));
        Check((await h.App.SnapshotAsync()).Positions!.All(p => !p.Admit), "a disconnected camera does not block management entry for repair");
        await h.App.SetManagementWorkspaceAsync(offlineWindow, false, default).WaitAsync(TimeSpan.FromSeconds(5));
        Check((await h.App.SnapshotAsync()).Devices!.Single(d => d.DeviceId == h.Right).Health != DeviceHealth.Ready,
            "closing management does not invent camera connectivity when it is disconnected");
        await using var failed = new CameraRoundHarness(workspace, Path.Combine(root, "failed-sdk-boundary"));
        await failed.StartAsync(); await failed.WaitForCameraAdmissionAsync();
        failed.Api(failed.Left).FailNextClear = true;
        var warning = await failed.App.SetManagementWorkspaceAsync(Wire.Id(), true, default);
        Check(warning.Status == "PausedWithCameraWarning" && (await failed.App.SnapshotAsync()).Positions!.All(p => !p.Admit),
            "an SDK cutover failure reports the pending boundary but keeps production fenced and management available for repair");


    }
}

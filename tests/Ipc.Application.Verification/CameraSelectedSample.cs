using Ipc.Application.Contracts;
using Ipc.Platform;

internal static partial class CameraRoundChecks
{
    static async Task VerifySelectedSample(string workspace, string root, bool native)
    {
        await using var h = new CameraRoundHarness(workspace, Path.Combine(root, native ? "sample-native" : "sample-software"), nativeResults: native);
        await h.StartAsync(); await h.WaitForCameraAdmissionAsync();
        var window = Wire.Id(); await h.App.SetManagementWorkspaceAsync(window, true, default);
        var snapshot = await h.App.SnapshotAsync();
        BeginCameraInspection Begin(string device) => new(Wire.Id(), snapshot.RuntimeId, snapshot.Configuration.Revision,
            CameraInspectionMode.ObserveProduction, "Synthetic selected camera sample", new(1, 64, 1000)) { DeviceId = device };
        async Task Frame(string device, uint number)
        {
            var pixels = CameraRoundHarness.Barcode("SYNTH-SAMPLE");
            if (native) h.Api(device).EnqueueNative(pixels, number, [new("SYNTH-SAMPLE", "Code128", [new(10, 10), new(50, 10), new(50, 30), new(10, 30)], 0)]);
            else h.Api(device).Enqueue(pixels, number);
            await CameraRoundHarness.Until(() => Task.FromResult(h.Factory.Sources[device].Timings.Snapshot().RecentFrames.Any(f => f.Number == number)));
        }
        var command = Begin(h.Left);
        var rightClears = h.Api(h.Right).Clears;
        var first = await h.App.BeginCameraInspectionAsync(command, default);
        await Frame(h.Left, 1); await Frame(h.Right, 1);
        await CameraRoundHarness.Until(async () => (await h.App.ReadCameraFramesAsync(first.SessionId!, h.Left, 0, null, 20, default)).Rows.Length == 1);
        var ended = await h.App.EndCameraInspectionAsync(new(Wire.Id(), first.SessionId!, false, false, "Synthetic sample stop and flush"), default);
        Check(h.Api(h.Right).Clears == rightClears, "begin and stop do not reset the unselected camera's SDK buffers");
        var rows = (await h.App.ReadCameraFramesAsync(first.SessionId!, h.Left, 0, null, 20, default)).Rows;
        Check(ended.State == "Closed" && rows.Length == 1 && rows[0].ImageAvailable &&
            (await h.App.ReadCameraFramesAsync(first.SessionId!, h.Right, 0, null, 20, default)).Rows.Length == 0,
            $"{native}: production acquisition/store captures only the selected camera, including its original image");
        using (var preview = await h.App.ReadCameraPreviewLeaseAsync(h.Right, default))
            Check(preview.Diagnostics?.Window?.DecodedFrames == 0 && h.Api(h.Right).PreviewAcquires > 0,
                $"{native}: unselected camera stays preview-only without software or native recognition");
        var second = await h.App.BeginCameraInspectionAsync(Begin(h.Right), default);
        await Frame(h.Left, 2); await Frame(h.Right, 2);
        await CameraRoundHarness.Until(async () => (await h.App.ReadCameraFramesAsync(second.SessionId!, h.Right, 0, null, 20, default)).Rows.Length == 1);
        await h.App.EndCameraInspectionAsync(new(Wire.Id(), second.SessionId!, false, false, "Synthetic next sample stop"), default);
        Check((await h.App.ReadCameraFramesAsync(second.SessionId!, h.Left, 0, null, 20, default)).Rows.Length == 0 &&
            (await h.App.ReadCameraFramesAsync(first.SessionId!, h.Left, 0, null, 20, default)).Rows.Length == 1 && h.Peer.Requests.IsEmpty &&
            (await h.App.SnapshotAsync()).Operations.Length == 0,
            $"{native}: stop then select the other camera retains original session evidence without new business requests");
        await Reject(() => h.App.BeginCameraInspectionAsync(Begin("missing-camera"), default), "invalid selected source cannot silently sample all cameras");
        Check((await h.App.CameraInspectionStatusAsync(default)).SessionId == second.SessionId,
            "invalid selection creates no session and preserves previous results");
        await h.App.SetManagementWorkspaceAsync(window, false, default); await h.WaitForCameraAdmissionAsync();
        await Frame(h.Left, 3);
        await CameraRoundHarness.Until(() => Task.FromResult(h.Peer.Requests.Count == 1));
        Check(h.Peer.Requests.Count == 1, "normal production resumes after selected sampling and rejected selection");
    }
}

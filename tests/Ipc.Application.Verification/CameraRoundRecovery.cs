using System.Reflection;
using Ipc.Application.Contracts;
using Ipc.Platform;

internal static partial class CameraRoundChecks
{
    static async Task VerifyNoReadRecovery(string workspace, string root)
    {
        await using var h = new CameraRoundHarness(workspace, Path.Combine(root, "no-read-recovery"), nativeResults: true);
        await h.StartAsync(); await PauseForDirectDebug(h.App);
        var runtime = (await h.App.SnapshotAsync()).RuntimeId;
        await h.BeginAsync(); await h.ArmAsync(h.Left);
        var source = h.Factory.Sources[h.Left];
        var field = source.GetType().GetField("inspection", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var registration = (InspectionRegistration)field.GetValue(source)!;
        var delivered = 0;
        await source.SetInspectionAsync(registration with { Receive = frame =>
        {
            var accepted = registration.Receive(frame); Interlocked.Increment(ref delivered); return accepted;
        } }, default);
        // Full-size empty frames exceed the old 128 MiB queue budget. Wait for
        // actual delivery so latest-frame sampling cannot hide the failure path.
        var empty = new CameraRoundHarness.RoundPixels(new byte[3072 * 2048], 3072, 2048);
        for (uint frame = 1; frame <= 26; frame++)
        {
            var prior = Volatile.Read(ref delivered);
            h.Api(h.Left).EnqueueNative(empty, frame, []);
            await CameraRoundHarness.Until(() => Task.FromResult(Volatile.Read(ref delivered) > prior));
            if (frame is 2 or 3)
            {
                var reading = await h.App.ReadCameraReadRoundAsync(default);
                Check(reading.ConsecutiveNoCodeFrames == frame && reading.Detail.Contains("无条码") == (frame == 3),
                    $"no-code feedback appears after three delivered frames, threshold frame={frame}");
            }
        }
        var status = await h.App.CameraInspectionStatusAsync(default);
        Check(status.Frames == 0 && status.MissingFrames == 0 && (await h.App.ReadCameraReadRoundAsync(default)).State == "Reading" &&
            !h.App.FatalFailure.IsCompleted && h.Peer.Requests.IsEmpty,
            "full-size no-code debug frames keep waiting without archiving empty images, overflowing custody or fencing production");
        // Exercise the real 30-second round budget, with no fake clock or timeout.
        using (var budget = new CancellationTokenSource(TimeSpan.FromSeconds(35)))
            while ((await h.App.ReadCameraReadRoundAsync(budget.Token)).State == "Reading") await Task.Delay(50, budget.Token);
        var expired = await h.App.ReadCameraReadRoundAsync(default);
        Check(expired.State == "Stopped" && expired.Detail.Contains("再次扫码") && !h.App.FatalFailure.IsCompleted && h.Peer.Requests.IsEmpty,
            "empty read timeout leaves readable runtime and an explicit retry instead of a fatal storage failure");
        await h.ArmAsync(h.Left);
        h.Api(h.Left).EnqueueNative(CameraRoundHarness.EmptyTray, 30, [new("RECOVERY-ONE", "Code128", [], 0)]);
        var locked = await h.LockedAsync();
        Check(locked.CanSend && locked.ConsecutiveNoCodeFrames == 0 && locked.Frame?.FrameNumber == 30 && (await h.App.CameraInspectionStatusAsync(default)).Frames == 1,
            "retry after no-read timeout captures only the winning frame and never sends automatically");
        await h.EndAsync(); await h.BeginAsync(); await h.ArmAsync(h.Left);
        registration = (InspectionRegistration)field.GetValue(source)!;
        string? injected = null;
        await source.SetInspectionAsync(registration with { Receive = frame =>
        {
            // Fail only the diagnostic file boundary, leaving SQLite/business
            // writes and the actual driver/decoder path untouched.
            var hash = Wire.Hash(registration.Id + ":" + frame.DeviceId + ":" + frame.Epoch + ":" + frame.FrameNumber).ToLowerInvariant();
            injected = Path.Combine(h.Launch.DataRoot, h.Config.Mode.ToString(), h.Config.StationId, "sportmaster", "camera-inspection", "images", hash + ".gray.br.tmp");
            File.WriteAllText(injected, "synthetic blocked diagnostic file");
            return registration.Receive(frame);
        } }, default);
        h.Api(h.Left).EnqueueNative(CameraRoundHarness.EmptyTray, 31, [new("RECOVERY-PHOTO-FAIL", "Code128", [], 0)]);
        await CameraRoundHarness.Until(async () => (await h.App.ReadCameraReadRoundAsync(default)).State == "Faulted");
        Check(!h.App.FatalFailure.IsCompleted && h.Peer.Requests.IsEmpty && (await h.App.ReadCameraReadRoundAsync(default)).RequestBody is null &&
            (await h.App.SnapshotAsync()).RuntimeId == runtime,
            "diagnostic photo IOException stays local to the scan and never creates a request or kills snapshot reads");
        await h.EndAsync();
        if (injected is not null) File.Delete(injected);
        await h.BeginAsync(); await h.ArmAsync(h.Right);
        h.Api(h.Right).EnqueueNative(CameraRoundHarness.EmptyTray, 1, [new("RECOVERY-RIGHT", "Code128", [], 0)]);
        var retry = await h.LockedAsync();
        Check(retry.DeviceId == h.Right && retry.CanSend && !h.App.FatalFailure.IsCompleted && h.Peer.Requests.IsEmpty,
            "new explicit collection retries diagnostic custody in the same process and the other camera remains usable");
        await h.App.SendCameraRoundAsync(await h.SendCommandAsync(retry), default);
        Check(h.Peer.Requests.Count == 1, "only the explicit send after recovery posts once to RCS");
        await h.EndAsync();
        await CameraRoundHarness.Until(async () =>
        {
            var logs = await h.App.QueryLogsAsync(new(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMinutes(1))
            { KeyEvents = true, Source = "相机调试" }, null, 0, default);
            try
            {
                return logs.Entries.Any(e => e.EventCode == "本轮未识别到条码") && logs.Entries.Any(e => e.Text.Contains("ReadTimeout")) &&
                    logs.Entries.Any(e => e.Text.Contains("CaptureDiagnosticFrame") && e.Text.Contains("ReadFailed") && e.Text.Contains("IOException") && e.Text.Contains("DiagnosticStoreError")) &&
                    logs.Entries.Any(e => e.EventCode == "重新尝试诊断采集");
            }
            finally { h.App.ReleaseLogSelection(logs.Selection); }
        });
        Check(true, "default key logs distinguish no-code threshold, timeout, diagnostic save failure and explicit recovery");
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Platform;

static partial class Program
{
    static async Task VerifyPausedWorkstation(string root, string evidence, List<string> lines)
    {
        void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); lines.Add("PASS PAUSED-CARD " + text); }
        await using var h = new CameraRoundHarness(root, Path.Combine(evidence, "paused-card"), nativeResults: true, timeoutMs: 1000);
        await h.StartAsync(); await h.WaitForCameraAdmissionAsync();
        RecognitionSymbol Symbol(string code, int x) => new(code, "DataMatrix", [new(x, 20), new(x + 30, 20), new(x + 30, 60), new(x, 60)], 0);
        var rejectedPixels = CameraRoundHarness.Barcode("SYNTH-MULTI-IMAGE");
        h.Api(h.Right).EnqueueNative(rejectedPixels, 1, [Symbol("SYNTH-A", 20), Symbol("SYNTH-B", 100)]);
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Inputs!.Any(i => i.PositionId == "right" && i.Outcome == ParcelInputOutcome.Rejected));
        var s = await h.App.SnapshotAsync();
        Check((await h.App.CommandAsync(new(Wire.Id(), s.Revision, "Pause"))).Status == "Persisted", "software pause is confirmed before feeding the blocked barcode");
        var blockedPixels = CameraRoundHarness.Barcode("SYNTH-BLOCKED-IMAGE");
        h.Api(h.Left).EnqueueNative(blockedPixels, 1, [Symbol("SYNTH-PAUSED-CODE", 50)]);
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Inputs!.Any(i => i.PositionId == "left" && i.Outcome == ParcelInputOutcome.Blocked));
        var window = new SupplyStationWindow(h.App, null, "standard"); window.Show();
        var vm = window.ViewModel;
        try
        {
            await SetClient(window, 1366, 768);
            async Task Until(Func<bool> done)
            {
                for (var i = 0; i < 200; i++) { await vm.RefreshAsync(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); if (done()) return; await Task.Delay(25); }
                throw new TimeoutException("Paused work card did not settle");
            }
            await Until(() => vm.Left.CanViewCurrentPicture && vm.Right.CanViewCurrentPicture && vm.CanResumeInViewport);
            Check(vm.Left.Barcode == "SYNTH-PAUSED-CODE" && vm.Left.DisplayStatus.Contains("已读到条码") && vm.Left.DisplayAttentionReason.Contains("未请求格口"),
                "left blocked scan explains that decoding succeeded but no chute request was made");
            Check(vm.Left.DisplayPresentation.Tone == ParcelPresentationTone.Neutral && vm.Right.DisplayPresentation.Tone == ParcelPresentationTone.Rejected &&
                vm.Right.DisplayStatus.Contains("多个") && vm.Right.DisplayNextStep.Contains("移出托盘"), "software pause preserves the right multi-code refusal and its next step");
            var original = await h.App.ReadParcelPictureAsync(vm.Left.CurrentPictureQuery!, default);
            Check(original.Image?.Gray8.SequenceEqual(blockedPixels.Data) == true && vm.Right.CurrentPictureQuery!.ObservationId != vm.Left.CurrentPictureQuery!.ObservationId,
                "left image matches the blocked acquisition and cannot point at the right rejection");
            var resume = (Button)window.FindName("ResumeSupply");
            Check(resume.IsVisible && resume.IsEnabled && h.Peer.Requests.Count == 0, "pause has an available guarded resume action and picture reads create no requests");
            CaptureInternal(evidence, "paused-blocked-left-rejected-right-1366", (Grid)window.FindName("ClientArea"), lines);
            await SetClient(window, 800, 420);
            Check(resume.IsVisible, "resume remains discoverable in the compact workstation");
            CaptureInternal(evidence, "paused-blocked-left-rejected-right-800", (Grid)window.FindName("ClientArea"), lines);
            resume.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => vm.Snapshot?.Paused == false);
            Check(!resume.IsVisible && h.Peer.Requests.Count == 0, "resume clears only software pause without replaying blocked input or original requests");
            await h.WaitForCameraAdmissionAsync();
            h.Api(h.Left).EnqueueNative(blockedPixels, 2, [Symbol("SYNTH-PAUSED-CODE", 50)]);
            await Until(() => vm.Left.ShowConfirmedTask);
            Check(h.Peer.Requests.Count == 1, "the next current camera observation is admitted once after resume");
            h.Peer.ReplySlot = "";
            h.Api(h.Right).EnqueueNative(rejectedPixels, 2, [Symbol("SYNTH-UNKNOWN", 150)]);
            await Until(() => vm.Right.HasUncertainOriginal);
            await vm.PauseAsync("SYNTH-UI-PAUSE-REASON"); await Until(() => vm.Snapshot?.Paused == true);
            var triggerRecorded = false;
            await CameraRoundHarness.Until(async () =>
            {
                var page = await h.App.QueryLogsAsync(new(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1)) { KeyEvents = true }, null, 0, default);
                try
                {
                    foreach (var row in page.Entries)
                    {
                        using var json = System.Text.Json.JsonDocument.Parse(row.Text); var entry = json.RootElement;
                        if (entry.TryGetProperty("Event", out var action) && action.GetString() == "软件暂停状态已记录" &&
                            entry.GetProperty("Detail").GetProperty("Evidence").GetProperty("Reason").GetString() == "SYNTH-UI-PAUSE-REASON") triggerRecorded = true;
                    }
                    return triggerRecorded;
                }
                finally { h.App.ReleaseLogSelection(page.Selection); }
            });
            Check(triggerRecorded, "the UI pause call carries its actual trigger into the committed Application log");
            Check(vm.Right.DisplayStatus.Contains("未确认") && vm.Right.DisplayPresentation.Tone == ParcelPresentationTone.Unknown &&
                vm.Right.DisplayNextStep.Contains("供包已暂停"), "software pause preserves an unconfirmed original result instead of replacing its warning");
            await SetClient(window, 1366, 768);
            CaptureInternal(evidence, "paused-unknown-right-1366", (Grid)window.FindName("ClientArea"), lines);
        }
        finally { window.Close(); }
        lines.Add("Native WPF with synthetic camera frames and loopback RCS; physical SDK camera, customer RCS and site acceptance not run.");
    }
}

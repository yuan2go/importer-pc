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
            var leftControl = Descendants(window).OfType<SupplyStationPosition>().Single(c => ReferenceEquals(c.DataContext, vm.Left));
            var pictureButton = (Button)leftControl.FindName("CurrentBarcode");
            Check(vm.Left.Barcode == "SYNTH-PAUSED-CODE" && vm.Left.DisplayStatus.Contains("已读到条码") && vm.Left.DisplayAttentionReason.Contains("未请求格口"),
                "left blocked scan explains that decoding succeeded but no chute request was made");
            Check(vm.Left.DisplayPresentation.Tone == ParcelPresentationTone.Neutral && vm.Right.DisplayPresentation.Tone == ParcelPresentationTone.Rejected &&
                vm.Right.DisplayStatus.Contains("多个") && vm.Right.DisplayNextStep.Contains("移出托盘"), "software pause preserves the right multi-code refusal and its next step");
            var original = await h.App.ReadParcelPictureAsync(vm.Left.CurrentPictureQuery!, default);
            Check(original.Image?.Gray8.SequenceEqual(blockedPixels.Data) == true && vm.Right.CurrentPictureQuery!.ObservationId != vm.Left.CurrentPictureQuery!.ObservationId,
                "left image matches the blocked acquisition and cannot point at the right rejection");
            var resume = (Button)window.FindName("ResumeSupply");
            Check(resume.IsVisible && resume.IsEnabled && h.Peer.Requests.Count == 0, "pause has an available guarded resume action and picture reads create no requests");
            Check(vm.Left.ShowCenteredState && vm.Left.CenteredStateTitle == "供包已暂停" &&
                vm.Left.CenteredStateInstruction.Contains("恢复供包"), "paused neutral card uses the centered disabled-card layout");
            await CameraRoundHarness.Until(() => Task.FromResult(h.App.WorkerProgress().Single(p => p.Position == "left").State == "Waiting"));
            var observed = h.App.WorkerProgress().Single(p => p.Position == "left").ObservedOnly;
            h.Api(h.Left).EnqueueNative(blockedPixels, 2, [Symbol("SYNTH-PAUSED-CODE", 50)]);
            await CameraRoundHarness.Until(() => Task.FromResult(h.App.WorkerProgress().Single(p => p.Position == "left").ObservedOnly > observed));
            await vm.RefreshAsync();
            Check(pictureButton.IsEnabled && vm.Left.CanViewCurrentPicture && vm.Left.CurrentPictureQuery!.ObservationId == original.ObservationId,
                "continuous paused presence feedback keeps the actual picture button enabled without a new durable snapshot");
            Check((await h.App.ReadParcelPictureAsync(vm.Left.CurrentPictureQuery!, default)).Image!.Gray8.SequenceEqual(blockedPixels.Data),
                "presence-only feedback keeps the exact original without substituting the latest video");
            CaptureInternal(evidence, "paused-blocked-left-rejected-right-1366", (Grid)window.FindName("ClientArea"), lines);
            await SetClient(window, 800, 420);
            Check(resume.IsVisible, "resume remains discoverable in the compact workstation");
            CaptureInternal(evidence, "paused-blocked-left-rejected-right-800", (Grid)window.FindName("ClientArea"), lines);
            resume.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => vm.Snapshot?.Paused == false);
            Check(!resume.IsVisible && h.Peer.Requests.Count == 0, "resume clears only software pause without replaying blocked input or original requests");
            await h.WaitForCameraAdmissionAsync();
            h.Api(h.Left).EnqueueNative(blockedPixels, 3, [Symbol("SYNTH-PAUSED-CODE", 50)]);
            await Until(() => vm.Left.ShowConfirmedTask);
            Check(h.Peer.Requests.Count == 1, "the next current camera observation is admitted once after resume");
            h.Peer.ReplySlot = "";
            h.Api(h.Right).EnqueueNative(rejectedPixels, 2, [Symbol("SYNTH-UNKNOWN", 150)]);
            await Until(() => vm.Right.HasUncertainOriginal);
            await vm.PauseAsync("SYNTH-UI-PAUSE-REASON"); await Until(() => vm.Snapshot?.Paused == true);
            var triggerRecorded = false; var pictureButtonRecorded = false; var pictureReadRecorded = false;
            await CameraRoundHarness.Until(async () =>
            {
                var page = await h.App.QueryLogsAsync(new(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1)) { KeyEvents = true }, null, 0, default);
                try
                {
                    foreach (var row in page.Entries)
                    {
                        using var json = System.Text.Json.JsonDocument.Parse(row.Text); var entry = json.RootElement;
                        if (!entry.TryGetProperty("Event", out var action)) continue;
                        if (action.GetString() == "软件暂停状态已记录" &&
                            entry.GetProperty("Detail").GetProperty("Evidence").GetProperty("Reason").GetString() == "SYNTH-UI-PAUSE-REASON") triggerRecorded = true;
                        if (action.GetString() == "图片按钮状态变化" && entry.GetProperty("Detail").GetProperty("Enabled").GetBoolean() &&
                            entry.GetProperty("Detail").GetProperty("Reason").GetString() == "Available") pictureButtonRecorded = true;
                        if (action.GetString() == "包裹图片读取结果" && entry.GetProperty("Detail").GetProperty("Status").GetString() == "Available" &&
                            entry.GetProperty("Detail").GetProperty("ObservationId").GetString() == original.ObservationId) pictureReadRecorded = true;
                    }
                    return triggerRecorded && pictureButtonRecorded && pictureReadRecorded;
                }
                finally { h.App.ReleaseLogSelection(page.Selection); }
            });
            Check(triggerRecorded, "the UI pause call carries its actual trigger into the committed Application log");
            Check(pictureButtonRecorded && pictureReadRecorded, "default key logs contain actual UI picture availability and exact original-read results");
            Check(vm.Right.DisplayStatus.Contains("未确认") && vm.Right.DisplayPresentation.Tone == ParcelPresentationTone.Unknown &&
                vm.Right.DisplayNextStep.Contains("供包已暂停"), "software pause preserves an unconfirmed original result instead of replacing its warning");
            await SetClient(window, 1366, 768);
            CaptureInternal(evidence, "paused-unknown-right-1366", (Grid)window.FindName("ClientArea"), lines);
        }
        finally { window.Close(); }
        await VerifyInterruptedPauseCard(root, evidence, lines);
        lines.Add("Native WPF with synthetic camera frames and loopback RCS; physical SDK camera, customer RCS and site acceptance not run.");
    }
    static async Task VerifyInterruptedPauseCard(string root, string evidence, List<string> lines)
    {
        await using var h = new CameraRoundHarness(root, Path.Combine(evidence, "interrupted-pause"), nativeResults: true);
        await h.StartAsync(); await h.App.DisposeAsync();
        var database = Path.Combine(h.Launch.DataRoot, h.Config.Mode.ToString(), h.Config.StationId, "sportmaster", "sportmaster.db");
        var originalId = Wire.Id();
        using (var db = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + database))
        {
            db.Open();
            using var insert = db.CreateCommand();
            insert.CommandText = "INSERT INTO parcel_ingress(id,position,received_at,state,body,body_hash,session,detail) VALUES($id,$position,$at,'Interrupted',$body,$hash,'synthetic-prior-run','Synthetic interrupted source requires inspection')";
            insert.Parameters.AddWithValue("$id", originalId);
            insert.Parameters.AddWithValue("$position", "left");
            insert.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.AddDays(-1).ToString("O"));
            insert.Parameters.AddWithValue("$body", "{}");
            insert.Parameters.AddWithValue("$hash", Wire.Hash("{}"));
            insert.ExecuteNonQuery();
            insert.Parameters["$id"].Value = Wire.Id();
            insert.Parameters["$position"].Value = "right";
            insert.ExecuteNonQuery();
        }
        await h.StartAsync();
        var window = new SupplyStationWindow(h.App, null, "standard"); window.Show();
        try
        {
            var vm = window.ViewModel;
            await SetClient(window, 1366, 768);
            await CameraRoundHarness.Until(async () => { await vm.RefreshAsync(); return vm.Fresh && vm.Snapshot?.Paused == true; });
            if (!vm.Left.ShowCenteredState || !vm.Left.CenteredStateReason.Contains("中断") ||
                !vm.Left.CenteredStateInstruction.Contains("未处理的扫码") || vm.CanResumeInViewport)
                throw new InvalidOperationException("Interrupted pause must identify the custody barrier and keep resume guarded: " + Wire.Encode(new
                { vm.Left.ShowCenteredState, vm.Left.CenteredStateReason, vm.Left.CenteredStateInstruction, vm.CanResumeInViewport, vm.Snapshot!.BlockingReasons }));
            lines.Add("PASS PAUSED-CARD interrupted pause shows its concrete reason and inspection route with resume disabled");
            CaptureInternal(evidence, "paused-interrupted-1366", (Grid)window.FindName("ClientArea"), lines);
            await SetClient(window, 800, 420);
            foreach (var control in Descendants(window).OfType<SupplyStationPosition>())
            {
                var panel = (Border)control.FindName("CenteredSupplyPanel");
                var content = (StackPanel)control.FindName("CenteredSupplyContent");
                if (content.DesiredSize.Height > panel.ActualHeight - panel.Padding.Top - panel.Padding.Bottom + 1)
                    throw new InvalidOperationException("Compact interrupted pause clips its reason or next step");
            }
            lines.Add("PASS PAUSED-CARD compact interrupted pause fits the title, cause and next step without clipping");
            CaptureInternal(evidence, "paused-interrupted-800", (Grid)window.FindName("ClientArea"), lines);
            var found = false;
            await CameraRoundHarness.Until(async () =>
            {
                var page = await h.App.QueryLogsAsync(new(DateTimeOffset.UtcNow.AddMinutes(-2), DateTimeOffset.UtcNow.AddMinutes(1)) { KeyEvents = true }, null, 0, default);
                try
                {
                    foreach (var row in page.Entries)
                    {
                        using var json = System.Text.Json.JsonDocument.Parse(row.Text);
                        if (!json.RootElement.TryGetProperty("Detail", out var d) || d.ValueKind != System.Text.Json.JsonValueKind.Object ||
                            !d.TryGetProperty("InterruptedIngress", out var custody)) continue;
                        found = custody.GetProperty("Total").GetInt64() == 2 && custody.GetProperty("Omitted").GetInt64() == 0 &&
                            custody.GetProperty("Entries").EnumerateArray().Any(e => e.GetProperty("Id").GetString() == originalId &&
                            e.GetProperty("OriginalSession").GetString() == "synthetic-prior-run");
                        if (found) break;
                    }
                    return found;
                }
                finally { h.App.ReleaseLogSelection(page.Selection); }
            });
            lines.Add("PASS PAUSED-CARD default key logs identify the original interrupted input, prior run, total and omitted count");
            if (h.Peer.Requests.Count != 0) throw new InvalidOperationException("Interrupted pause replayed a source");
        }
        finally { window.Close(); }
    }

}

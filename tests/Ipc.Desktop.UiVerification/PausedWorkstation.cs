using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Platform;
using Ipc.Presentation.Wpf;

static partial class Program
{
    static async Task VerifyPausedWorkstation(string root, string evidence, List<string> lines)
    {
        void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); lines.Add("PASS PAUSED-CARD " + text); }
        await using var h = new CameraRoundHarness(root, Path.Combine(evidence, "paused-card"), nativeResults: true, timeoutMs: 1000)
            { StationClock = new PdaRoundClock() };
        await h.StartAsync(); await h.WaitForCameraAdmissionAsync();
        RecognitionSymbol Symbol(string code, int x) => new(code, "DataMatrix", [new(x, 20), new(x + 30, 20), new(x + 30, 60), new(x, 60)], 0);
        var rejectedPixels = CameraRoundHarness.Barcode("SYNTH-MULTI-IMAGE");
        h.Api(h.Right).EnqueueNative(rejectedPixels, 1, [Symbol("SYNTH-A", 20), Symbol("SYNTH-B", 100)]);
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Rounds.Any(r => r.PositionId == "right" && r.FormalCodes.Length == 2));
        var s = await h.App.SnapshotAsync();
        Check((await h.App.CommandAsync(new(Wire.Id(), s.Revision, "Pause"))).Status == "Persisted", "software pause is confirmed before feeding the blocked barcode");
        var blockedPixels = CameraRoundHarness.Barcode("SYNTH-BLOCKED-IMAGE");
        var processed = h.App.WorkerProgress().Single(p => p.Position == "left").Processed;
        h.Api(h.Left).EnqueueNative(blockedPixels, 1, [Symbol("SYNTH-PAUSED-CODE", 50)]);
        await CameraRoundHarness.Until(() => Task.FromResult(h.App.WorkerProgress().Single(p => p.Position == "left").Processed > processed));
        var rows = await h.App.ReadCameraBusinessAsync(h.Left, 0, null, 100, default);
        var blocked = (await h.App.ReadCameraBusinessDetailAsync(rows.Rows.Last().Sequence, default))!;
        var window = new SupplyStationWindow(h.App, null, "standard"); window.Show();
        var vm = window.ViewModel;
        try
        {
            await SetClient(window, 1366, 768);
            async Task Until(Func<bool> done)
            {
                for (var i = 0; i < 200; i++) { await vm.RefreshAsync(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); if (done()) return; await Task.Delay(25); }
                throw new TimeoutException($"Work card did not settle: {vm.Right.CenteredStateTitle} / {vm.Right.CenteredStateReason}");
            }
            await Until(() => vm.Fresh && vm.Right.CanViewCurrentPicture && vm.CanResumeInViewport);
            var leftControl = Descendants(window).OfType<SupplyStationPosition>().Single(c => ReferenceEquals(c.DataContext, vm.Left));
            var pictureButton = (Button)leftControl.FindName("CurrentBarcode");
            Check(blocked.Outcome == ParcelInputOutcome.Blocked && vm.Left.CurrentRound is null && !pictureButton.IsEnabled && vm.Left.Barcode.Length == 0,
                "a paused observation is retained in diagnostics without inventing current-round authority");
            Check(vm.Right.CurrentRound is { FormalCodes.Length: 2, OperationId: null } && vm.Right.CanViewCurrentPicture && !vm.Right.CenteredRcsRejection,
                "pause retains the existing correctable multi-code round and its exact original, without a request");
            var original = await h.App.ReadParcelPictureAsync(new(ObservationId: blocked.Observation.Id), default);
            Check(original.Image?.Gray8.SequenceEqual(blockedPixels.Data) == true && vm.Right.CurrentPictureQuery!.ObservationId != original.ObservationId,
                "the owned diagnostic image matches the blocked acquisition and cannot borrow the other round's picture");
            var resume = (Button)window.FindName("ResumeSupply");
            Check(resume.IsVisible && resume.IsEnabled && h.Peer.Requests.Count == 0 && vm.Left.CenteredStateTitle == "供包已暂停",
                "pause retains a guarded resume action and picture reads create no requests");
            var observed = h.App.WorkerProgress().Single(p => p.Position == "left").ObservedOnly;
            h.Api(h.Left).EnqueueNative(blockedPixels, 2, [Symbol("SYNTH-PAUSED-CODE", 50)]);
            await CameraRoundHarness.Until(() => Task.FromResult(h.App.WorkerProgress().Single(p => p.Position == "left").ObservedOnly > observed));
            Check((await h.App.ReadParcelPictureAsync(new(ObservationId: blocked.Observation.Id), default)).Image!.Gray8.SequenceEqual(blockedPixels.Data),
                "repeated paused observations retain their exact diagnostic image rather than substitute live video");
            CaptureInternal(evidence, "paused-existing-round-and-diagnostics-1366", (Grid)window.FindName("ClientArea"), lines);
            await SetClient(window, 800, 420);
            Check(resume.IsVisible, "resume remains discoverable in the compact workstation");
            resume.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => vm.Snapshot?.Paused == false);
            Check(!resume.IsVisible && h.Peer.Requests.Count == 0, "resume never replays blocked input or original requests");
            await h.WaitForCameraAdmissionAsync();
            h.Api(h.Left).EnqueueNative(blockedPixels, 3, [Symbol("SYNTH-PAUSED-CODE", 50)]);
            await Until(() => vm.Left.ShowConfirmedTask);
            Check(h.Peer.Requests.Count == 1, "a fresh observation after resume is independently admitted once");
            h.Peer.ReplySlot = "";
            h.Api(h.Right).EnqueueNative(rejectedPixels, 2, [Symbol("SYNTH-UNKNOWN", 150)]);
            await Until(() => vm.Right.HasUncertainOriginal);
            var prior = vm.Right.Operation!; var round = vm.Right.CurrentRound!.Id; var sent = h.Peer.Requests.Count;
            Check(vm.Right.CanConfirmPlacementRemoval && vm.Right.CenteredStateInstruction == "请先取下包裹，再确认",
                "the visual-degraded unresolved round exposes targeted manual removal");
            h.Api(h.Right).EnqueueNative(rejectedPixels, 3, [Symbol("SYNTH-NEXT-A", 20), Symbol("SYNTH-NEXT-B", 100)]);
            await CameraRoundHarness.Until(() => Task.FromResult(h.Factory.Sources[h.Right].Timings.Snapshot().RecentFrames.Any(f => f.Number == 3)));
            await vm.RefreshAsync();
            Check(vm.Right.CurrentRound?.Id == round && vm.Right.Operation?.Id == prior.Id && h.Peer.Requests.Count == sent && vm.Left.ShowConfirmedTask,
                "changed candidates after submission cannot replace the current Unknown or create another call");
            await vm.PauseAsync("SYNTH-UI-PAUSE-REASON"); await Until(() => vm.Snapshot?.Paused == true);
            Check(vm.Right.HasUncertainOriginal && !vm.Right.CenteredRcsRejection && !vm.Right.CanSubmitBarcode,
                "software pause preserves the current unresolved original and submit guard");
            resume.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Until(() => vm.Snapshot?.Paused == false);
            await h.TrayAsync(h.Right, TrayPresence.Absent); await vm.RefreshAsync();
            Check(vm.Right.CurrentRound?.Id == round && vm.Right.CanConfirmPlacementRemoval,
                "later visual absence does not replace the already required manual confirmation");
            var rightControl = Descendants(window).OfType<SupplyStationPosition>().Single(c => ReferenceEquals(c.DataContext, vm.Right));
            var client = (Grid)window.FindName("ClientArea");
            Check(Inside((TextBlock)rightControl.FindName("CenteredInstruction"), client) &&
                Inside(Descendants(rightControl).OfType<Button>().Single(b => Equals(b.Content, "查看详情")), client),
                "compact exception keeps its instructions and details reachable");
            ((Button)rightControl.FindName("ConfirmManualPlacementRemoval")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => vm.Right.CurrentRound is null && vm.Right.CanSubmitBarcode);
            Check(vm.Right.CenteredStateTitle == "等待投包" && !vm.Right.CanViewCurrentPicture && vm.Left.ShowConfirmedTask && h.Peer.Requests.Count == sent,
                "targeted confirmation resets only that round without replay");
            h.Peer.ReplySlot = "B-02";
            h.Api(h.Right).EnqueueNative(rejectedPixels, 4, [Symbol("SYNTH-UNKNOWN", 150)]);
            await Until(() => vm.Right.ShowConfirmedTask && vm.Right.Slot == "B-02");
            await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Capacity!.HistoricalUnknown == 1);
            Check(vm.Right.Operation?.Id != prior.Id && Wire.Encode((await h.App.OperationAsync(prior.Id))?.Wcs) == Wire.Encode(prior.Wcs) &&
                h.Peer.Requests.Count == sent + 1 && vm.Left.ShowConfirmedTask,
                "same-code new round sends once and preserves the departed Unknown's original bytes and response");
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
        }
        finally { window.Close(); }
        await VerifyInterruptedPauseCard(root, evidence, lines);
        lines.Add("Native WPF with synthetic camera frames and loopback RCS; physical SDK camera, customer RCS and site acceptance not run.");
    }
    static string SeedInterruptedCameraInputs(CameraRoundHarness h)
    {
        var folder = Path.Combine(h.Launch.DataRoot, h.Config.Mode.ToString(), h.Config.StationId, "sportmaster");
        var originalId = Wire.Id();
        using (var db = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + Path.Combine(folder, "sportmaster.db")))
        {
            db.Open();
            foreach (var position in new[] { "left", "right" })
            {
                var observation = new Observation(Wire.Id(), position == "left" ? h.Left : h.Right, "synthetic-prior-epoch", "prior.1", 1,
                    "prior." + position, "Read", ["SYNTH-RESTART"], null, DateTimeOffset.UtcNow.AddDays(-1));
                var body = Wire.Encode(new { Observation = observation, Operator = (object?)null, ReceivedAt = observation.ObservedAt });
                using var insert = db.CreateCommand();
                insert.CommandText = "INSERT INTO parcel_ingress(id,position,received_at,state,body,body_hash,session,detail) VALUES($id,$position,$at,'Interrupted',$body,$hash,'synthetic-prior-run','Synthetic old camera receipt')";
                insert.Parameters.AddWithValue("$id", position == "left" ? originalId : Wire.Id());
                insert.Parameters.AddWithValue("$position", position); insert.Parameters.AddWithValue("$at", observation.ObservedAt.ToString("O"));
                insert.Parameters.AddWithValue("$body", body); insert.Parameters.AddWithValue("$hash", Wire.Hash(body)); insert.ExecuteNonQuery();
            }
        }
        using var repo = new Ipc.Application.ParcelFlow.ParcelRepository(folder, "sportmaster", ["left", "right"]);
        repo.Commit(null, "IngressRecoveryRequired", "Synthetic old software pause from interrupted scans", paused: true);
        return originalId;
    }
    static async Task VerifyInterruptedPauseCard(string root, string evidence, List<string> lines)
    {
        await using var h = new CameraRoundHarness(root, Path.Combine(evidence, "interrupted-pause"), nativeResults: true);
        await h.StartAsync(); await h.App.ExitManagementModeAsync(); await h.App.DisposeAsync();
        var originalId = SeedInterruptedCameraInputs(h);
        await h.StartAsync(authorizeManagement: false); await h.WaitForCameraAdmissionAsync();
        var window = new SupplyStationWindow(h.App, null, "standard"); window.Show();
        try
        {
            var vm = window.ViewModel;
            await SetClient(window, 1366, 768);
            await CameraRoundHarness.Until(async () =>
            {
                await vm.RefreshAsync();
                return vm.Fresh && vm.Snapshot is { Paused: false, Positions: { } positions } &&
                    positions.All(position => position.BarcodeInput?.Camera == true);
            });
            if (vm.IsManagementMode || !vm.Left.ShowCenteredState || vm.Left.CenteredStateTitle != "等待投包" ||
                vm.Right.CenteredStateTitle != "等待投包" || vm.ResumeVisibility != Visibility.Collapsed)
                throw new InvalidOperationException("Old camera receipts must recover directly into ordinary work without management or resume: " + Wire.Encode(vm.Snapshot));
            lines.Add("PASS PAUSED-CARD old interrupted scans return both cards to waiting without management or a resume command");
            CaptureInternal(evidence, "automatic-scan-recovery-1366", (Grid)window.FindName("ClientArea"), lines);
            await SetClient(window, 800, 420);
            if (vm.Left.CenteredStateInstruction.Contains("管理中心") || vm.Right.CenteredStateInstruction.Contains("核对"))
                throw new InvalidOperationException("Operator is still directed to an unavailable recovery tool");
            lines.Add("PASS PAUSED-CARD compact recovery has no manual inspection or management instruction");
            CaptureInternal(evidence, "automatic-scan-recovery-800", (Grid)window.FindName("ClientArea"), lines);
            await CameraRoundHarness.Until(() =>
            {
                var file = Path.Combine(h.Launch.DataRoot, h.Config.Mode.ToString(), h.Config.StationId, "sportmaster", "diagnostics", "runtime.jsonl");
                using var reader = new StreamReader(new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
                var log = reader.ReadToEnd();
                return Task.FromResult(log.Contains("已自动跳过上次未完成的扫码") && log.Contains(originalId));
            });
            lines.Add("PASS PAUSED-CARD automatic skip logs retain original input identity without claiming an old request was not sent");
            if (h.Peer.Requests.Count != 0) throw new InvalidOperationException("Startup replayed an old source");
            h.Api(h.Left).EnqueueNative(CameraRoundHarness.Barcode("SYNTH-FRESH-AFTER-RESTART"), 1,
                [new RecognitionSymbol("SYNTH-FRESH-AFTER-RESTART", "Code128", [new(20, 20), new(120, 20), new(120, 60), new(20, 60)], 0)]);
            await CameraRoundHarness.Until(async () => { await vm.RefreshAsync(); return vm.Left.ShowConfirmedTask; });
            if (h.Peer.Requests.Count != 1 || vm.Snapshot!.Paused || vm.IsManagementMode) throw new InvalidOperationException("Fresh parcel failed after automatic recovery");
            lines.Add("PASS PAUSED-CARD a fresh parcel reaches its chute in the ordinary workstation without entering management");
        }
        finally { window.Close(); }
    }
}

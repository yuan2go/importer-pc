using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Platform;

static partial class Program
{
    static async Task VerifyCameraBarcodeCurrent(string root, string evidence, List<string> lines)
    {
        void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); lines.Add("PASS CAMERA-CURRENT " + text); }
        async Task Until(Func<bool> ready, string step)
        {
            for (var i = 0; i < 400; i++)
            { await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); if (ready()) return; await Task.Delay(20); }
            throw new TimeoutException(step);
        }
        await using var h = new CameraRoundHarness(root, Path.Combine(evidence, "camera-current"), 15000);
        h.Config.Wcs!.Parameters["request-timeout-ms"] = "15000";
        ExtensionStore.AtomicWrite(Path.Combine(h.Launch.ConfigurationDirectory, "business.json"), h.Config);
        await h.StartAsync(); await h.WaitForCameraAdmissionAsync();
        var window = new SupplyStationWindow(h.App, null, "standard"); window.Show();
        SupplyStationWindow? second = null;
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var blocked = 0;
        try
        {
            await SetClient(window, 1366, 768); await Until(() => window.ViewModel.Fresh, "initial snapshot");
            var vm = window.ViewModel; var p = vm.Left;
            var control = Descendants(window).OfType<SupplyStationPosition>().Single(c => ReferenceEquals(c.DataContext, p));
            var entry = (TextBox)control.FindName("BarcodeEntry"); var picture = (Button)control.FindName("CurrentBarcode");
            var slot = (TextBox)control.FindName("CurrentSlot"); var recentBarcode = (TextBox)control.FindName("RecentChuteBarcode");
            var tasks = (Grid)control.FindName("TaskFields");
            var edits = typeof(SupplyStationPosition).GetField("editing", BindingFlags.Instance | BindingFlags.NonPublic)!;
            async Task Read(uint frame, string code)
            {
                var prior = p.ReadFeedbackKey;
                h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode(code), frame);
                await Until(() => p.ReadFeedbackKey is { } read && read != prior && p.Barcode == code, "camera feedback " + code);
            }
            async Task Resolved(string code) => await Until(() => p.Operation?.ParcelCode == code && p.ShowConfirmedTask, "resolved " + code);
            string State() => Wire.Encode(new
            {
                Main = entry.Text,
                Model = p.Barcode,
                Read = p.ReadFeedbackKey,
                Decoder = h.Factory.Decoders[h.Left].Last.Select(s => s.Text),
                Input = p.Input?.Observation.Codes,
                Operation = p.Operation?.ParcelCode,
                p.ShowConfirmedTask,
                PictureEnabled = picture.IsEnabled,
                Editing = edits.GetValue(control)
            });

            // The first read has no older Input/Operation to make HasCurrentInput true.
            h.App.Boundary = point => { if (point == "BeforeIngressDurable" && Interlocked.Exchange(ref blocked, 0) == 1) { entered.Set(); release.Wait(); } };
            blocked = 1;
            await Read(1, "SYNTH-FIRST-0001"); await Until(() => entered.IsSet, "first durable boundary");
            lines.Add("FIRST_BEFORE_DURABLE " + State());
            Check(entry.Text == "SYNTH-FIRST-0001" && !picture.IsEnabled && !tasks.IsVisible && h.Peer.Requests.Count == 0 && !(bool)edits.GetValue(control)!,
                "first decoded code reaches main entry before persistence without creating draft, picture, task or RCS request");
            release.Set(); await Resolved("SYNTH-FIRST-0001");
            Check(entry.Text == p.Operation!.ParcelCode && picture.IsEnabled && tasks.IsVisible, "committed first result owns barcode, picture and task fields");
            var first = p.Operation!; var frozen = Wire.Encode(first.Wcs!.Request); var basis = vm.Snapshot!;

            entered.Reset(); release.Reset(); blocked = 1;
            await Read(2, "SYNTH-SECOND-0002"); await Until(() => entered.IsSet, "second durable boundary");
            Check(entry.Text == "SYNTH-SECOND-0002" && !picture.IsEnabled && !tasks.IsVisible && !p.CurrentTaskAvailable && p.WaveNo == "—" &&
                p.Operation!.Id == first.Id && h.Peer.Requests.Count == 1, "new read replaces old main barcode and clears old task values/picture before durable admission");
            await vm.AcceptPublishedSnapshotAsync(basis with { PublishedTicks = Stopwatch.GetTimestamp() });
            Check(entry.Text == "SYNTH-SECOND-0002" && !picture.IsEnabled && !p.ShowConfirmedTask,
                "an older-input snapshot arriving after a newer read cannot restore old barcode, picture or destination");
            await Task.Delay(3200); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Check(entry.Text == "SYNTH-SECOND-0002" && p.CameraReadFeedback.Length == 0 && !picture.IsEnabled,
                "short read cue expiry never revives the old barcode or picture");
            second = new SupplyStationWindow(h.App, null, "compact"); second.Show();
            await Until(() => second.ViewModel.Fresh, "new window during older-than-three-second pending read");
            var secondControl = Descendants(second).OfType<SupplyStationPosition>().Single(c => ReferenceEquals(c.DataContext, second.ViewModel.Left));
            await Until(() => ((TextBox)secondControl.FindName("BarcodeEntry")).Text == "SYNTH-SECOND-0002", "new window gets latest pending read watermark");
            Check(!((Button)secondControl.FindName("CurrentBarcode")).IsEnabled && !second.ViewModel.Left.ShowConfirmedTask,
                "new window receives latest observed code even after transient cue expires, without inheriting old result authority");
            CaptureInternal(evidence, "camera-current-before-durable", (Grid)window.FindName("ClientArea"), lines);
            release.Set(); await Resolved("SYNTH-SECOND-0002"); h.App.Boundary = null;
            second.Close(); second = null;
            Check(Wire.Encode((await h.App.OperationAsync(first.Id))!.Wcs!.Request) == frozen, "immediate presentation never rewrites the first frozen request");

            // Each placement dispatches independently while this position's first response is held.
            h.Peer.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
            h.Peer.HoldWhen = body => body.Contains("SYNTH-RCS-0003", StringComparison.Ordinal);
            await Read(3, "SYNTH-RCS-0003");
            await Until(() => h.Peer.Requests.Count == 3 && vm.Snapshot!.Operations.Any(o => o.ParcelCode == "SYNTH-RCS-0003" && o.Wcs?.State == StageState.Dispatching), "held RCS call reflected in business snapshot");
            var heldOriginal = vm.Snapshot!.Operations.Single(o => o.ParcelCode == "SYNTH-RCS-0003");
            var heldRequest = Wire.Encode(heldOriginal.Wcs!.Request);
            await Read(4, "SYNTH-QUEUED-0004");
            await Resolved("SYNTH-QUEUED-0004");
            await Read(5, "SYNTH-LATEST-0005");
            await Resolved("SYNTH-LATEST-0005");
            Check(entry.Text == "SYNTH-LATEST-0005" && picture.IsEnabled && tasks.IsVisible && h.Peer.Requests.Count == 5 &&
                !h.Peer.Hold.Task.IsCompleted && vm.Snapshot!.Operations.Single(o => o.Id == heldOriginal.Id).Wcs!.State == StageState.Dispatching,
                "later parcels independently receive results while this position's earlier response remains pending");
            var regressions = new List<string>();
            TextChangedEventHandler changed = (_, _) => regressions.Add(entry.Text);
            entry.TextChanged += changed;
            h.Peer.Hold.TrySetResult(); h.Peer.Hold = null; h.Peer.HoldWhen = null;
            await CameraRoundHarness.Until(async () => (await h.App.OperationAsync(heldOriginal.Id))?.Wcs?.State == StageState.Accepted);
            var completedOriginal = (await h.App.OperationAsync(heldOriginal.Id))!;
            var completedRevision = (await h.App.SnapshotAsync()).Revision;
            await Until(() => vm.Snapshot!.Revision >= completedRevision, "late original result has reached the live UI snapshot");
            entry.TextChanged -= changed;
            Check(regressions.All(t => t == "SYNTH-LATEST-0005") && entry.Text == "SYNTH-LATEST-0005" && h.Peer.Requests.Count == 5 &&
                Wire.Encode(completedOriginal.Wcs!.Request) == heldRequest,
                "late original response cannot roll the main barcode back, replace the latest task or rewrite/resend any frozen request");
            Check(picture.IsEnabled && tasks.IsVisible, "latest matching committed observation restores only its own picture and task fields");
            var priorId = p.Operation!.Id;
            await Read(6, "SYNTH-LATEST-0005");
            await Until(() => p.Input?.Observation.SourceEventId == "mvs.frame.6" && p.ShowConfirmedTask, "same placement fully processed");
            Check(picture.IsEnabled && tasks.IsVisible && p.Operation!.Id == priorId && h.Peer.Requests.Count == 5,
                "continuous same-placement reads preserve their current picture and task without another request");
            DateTimeOffset? firstEmpty = null, lastEmpty = null;
            for (uint frame = 7; frame <= 9; frame++)
            {
                h.Api(h.Left).Enqueue(CameraRoundHarness.EmptyTray, frame);
                await Until(() => vm.Snapshot!.CameraReads.Any(r => r.PositionId == "left" && r.Frame.FrameNumber == frame &&
                    r.Frame.Outcome == ObservationReadOutcome.NoCode), "delivered empty tray frame " + frame);
                lastEmpty = vm.Snapshot!.CameraReads.Single(r => r.PositionId == "left").Frame.ObservedAt;
                firstEmpty ??= lastEmpty;
                if (frame == 7)
                {
                    await Until(() => p.ShowRecentChute && recentBarcode.IsVisible, "first changed no-code frame retains the previous result");
                    Check(!p.ShowConfirmedTask && !p.SlotAvailable && slot.Text == "SYNTH-CHUTE" && entry.Text.Length == 0 &&
                        recentBarcode.Text == "上一件条码 SYNTH-LATEST-0005", "changed no-code frame demotes the last chute and separates its barcode from the live entry");
                }
                if (frame < 9) await Task.Delay(425);
            }
            await Until(() => vm.Snapshot!.Positions!.Single(s => s.Id == "left").PlacementClearedAt >= lastEmpty, "empty tray retires current result");
            Check(lastEmpty - firstEmpty >= TimeSpan.FromMilliseconds(800), "three delivered empty frames span the required release window");
            Check(p.ShowRecentChute && !p.ShowConfirmedTask && !picture.IsEnabled && !tasks.IsVisible && !p.CanViewWorkDetails &&
                p.CanSubmitBarcode && !vm.Right.ShowRecentChute && h.Peer.Requests.Count == 5,
                "confirmed removal keeps a read-only result locally while freeing the lane and leaving the other side independent");
            h.Peer.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
            await Read(10, "SYNTH-LATEST-0005");
            await Until(() => h.Peer.Requests.Count == 6 && p.Operation?.Id != priorId && p.Operation?.Wcs?.State == StageState.Dispatching &&
                p.Input?.Observation.SourceEventId == "mvs.frame.10", "same-code independent request and its current input snapshot");
            Check(entry.Text == "SYNTH-LATEST-0005" && !picture.IsEnabled && !tasks.IsVisible && !p.ShowRecentChute && !recentBarcode.IsVisible &&
                !(bool)edits.GetValue(control)!, "same-code new placement immediately retires the historical result without transferring its authority");
            h.Peer.Hold.TrySetResult(); h.Peer.Hold = null;
            await Until(() => p.ShowConfirmedTask && p.Operation!.Id != priorId, "same-code latest confirmed result");

            h.Api(h.Left).Enqueue(CameraRoundHarness.Multiple("SYNTH-MULTI-A", "SYNTH-MULTI-B"), 11);
            await Until(() => p.Input?.Observation.SourceEventId == "mvs.frame.11", "multiple input rejected");
            var placeholder = (TextBlock)control.FindName("BarcodePlaceholder");
            Check(entry.Text.Length == 0 && placeholder.IsVisible && placeholder.Text.Contains("多个条码") && p.Input!.Observation.Codes.Length == 2 &&
                p.RawCandidates.Contains("SYNTH-MULTI-A") && p.RawCandidates.Contains("SYNTH-MULTI-B") && h.Peer.Requests.Count == 6 && picture.IsEnabled &&
                (await h.App.ReadParcelPictureAsync(p.CurrentPictureQuery!, default)).Image?.Metadata.SourceEventId == "mvs.frame.11",
                "multiple candidates keep a clear notice and their own viewable rejection original without requesting a chute");
            await SetClient(window, 800, 420); CaptureInternal(evidence, "camera-current-multiple-compact", (Grid)window.FindName("ClientArea"), lines);
            await Read(12, "SYNTH-AFTER-MULTI"); await Resolved("SYNTH-AFTER-MULTI");
            Check(entry.Text == "SYNTH-AFTER-MULTI" && !placeholder.IsVisible && !(bool)edits.GetValue(control)!,
                "single frame after multiple candidates replaces the notice and never appends older candidate text");

            // Intentional local editing stays separate from camera result projection.
            entry.Focus(); entry.Text = "INTENTIONAL-DRAFT"; entry.Select(3, 4);
            await Read(13, "SYNTH-DURING-DRAFT"); await Resolved("SYNTH-DURING-DRAFT");
            Check(entry.Text == "INTENTIONAL-DRAFT" && entry.SelectionStart == 3 && entry.SelectionLength == 4 && entry.IsKeyboardFocusWithin && !picture.IsEnabled,
                "real manual draft, caret and focus survive camera updates while mismatched picture action stays disabled");
            ((Button)control.FindName("SubmitBarcode")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => ((TextBlock)control.FindName("InputFeedback")).Text.StartsWith("本位状态已变化"), "original draft basis conflict");
            Check(entry.Text == "INTENTIONAL-DRAFT" && h.Peer.Requests.Count == 8, "camera replacement does not rebase or submit a stale manual draft");
            // The other position never shares main entry or observation identity.
            h.Api(h.Right).Enqueue(CameraRoundHarness.Barcode("SYNTH-RIGHT-ONLY"), 1);
            await Until(() => vm.Right.ShowConfirmedTask, "independent right result");
            Check(vm.Right.Barcode == "SYNTH-RIGHT-ONLY" && p.Barcode == "SYNTH-DURING-DRAFT" && entry.Text == "INTENTIONAL-DRAFT",
                "left draft and latest camera facts remain independent from the right position");
            var rightControl = Descendants(window).OfType<SupplyStationPosition>().Single(c => ReferenceEquals(c.DataContext, vm.Right));
            var rightEntry = (TextBox)rightControl.FindName("BarcodeEntry");
            var requestCount = h.Peer.Requests.Count;
            h.Api(h.Right).Enqueue(CameraRoundHarness.Barcode("SYNTH\nINVALID"), 2);
            await Until(() => vm.Right.Input?.Observation.SourceEventId == "mvs.frame.2", "control-bearing candidate rejected");
            Check(rightEntry.Text.Length == 0 && vm.Right.InputFailed && vm.Right.Input!.Observation.Codes.Single().Contains('\n') &&
                h.Peer.Requests.Count == requestCount && ((Button)rightControl.FindName("CurrentBarcode")).IsEnabled &&
                (await h.App.ReadParcelPictureAsync(vm.Right.CurrentPictureQuery!, default)).Image?.Metadata.SourceEventId == "mvs.frame.2",
                "control-bearing candidate stays in raw evidence with its own rejection picture and cannot become editable multiline input");
            var longCode = "SYNTH-" + new string('0', 90);
            h.Api(h.Right).Enqueue(CameraRoundHarness.Barcode(longCode), 3);
            await Until(() => vm.Right.ShowConfirmedTask && vm.Right.Barcode == longCode, "long single-code recovery");
            Check(rightEntry.Text == longCode && rightEntry.LineCount > 1 && !rightEntry.AcceptsReturn &&
                rightEntry.VerticalScrollBarVisibility == ScrollBarVisibility.Auto && !((TextBlock)rightControl.FindName("BarcodePlaceholder")).IsVisible,
                "long valid code remains complete in the wrapping scrollable field without manual newlines or invalid/multiple notices");
            CaptureInternal(evidence, "camera-current-long-compact", (Grid)window.FindName("ClientArea"), lines);

            // Explicit read-only display projections: source order remains stable if the wall clock changes.
            var projection = new SupplyStationViewModel(h.App);
            var current = await h.App.SnapshotAsync(); await projection.AcceptPublishedSnapshotAsync(current);
            var input = projection.Right.Input!.Observation;
            var read = new ParcelReadFeedback(current.RuntimeId, "right", Wire.Id(), input.DeviceId, input.Epoch,
                "SYNTH-CLOCK-NEW", Stopwatch.GetTimestamp(), input.ObservedAt.AddHours(-1))
            { SourceSequence = input.Sequence + 1 };
            var apply = projection.Right.GetType().GetMethod("ApplyReadFeedback", BindingFlags.Instance | BindingFlags.NonPublic)!;
            apply.Invoke(projection.Right, [read]);
            Check(projection.Right.Barcode == "SYNTH-CLOCK-NEW" && !projection.Right.ShowConfirmedTask,
                "same-epoch source sequence prevents wall-clock rollback from restoring an older parcel");
            apply.Invoke(projection.Right, [read with { ObservationId = input.Id, SingleCode = longCode, DetectedTicks = read.DetectedTicks - 1 }]);
            Check(projection.Right.Barcode == "SYNTH-CLOCK-NEW", "late older read notification cannot replace the latest observation watermark");
            await projection.AcceptPublishedSnapshotAsync(current with { PublishedTicks = Stopwatch.GetTimestamp() });
            Check(projection.Right.Barcode == "SYNTH-CLOCK-NEW", "late complete snapshot also respects the pending source-sequence watermark");

            for (uint frame = 4; frame <= 6; frame++)
            {
                h.Api(h.Right).Enqueue(CameraRoundHarness.EmptyTray, frame);
                await Until(() => vm.Snapshot!.CameraReads.Any(r => r.PositionId == "right" && r.Frame.FrameNumber == frame &&
                    r.Frame.Outcome == ObservationReadOutcome.NoCode), "right empty-tray frame " + frame);
                if (frame < 6) await Task.Delay(425);
            }
            await Until(() => vm.Snapshot!.Positions!.Single(s => s.Id == "right").PlacementClearedAt is not null, "right removal confirmation");
            var heldBarcode = (TextBox)rightControl.FindName("RecentChuteBarcode");
            var heldAt = Stopwatch.GetTimestamp();
            await Task.Delay(1400);
            Check(vm.Right.ShowRecentChute && heldBarcode.IsVisible && heldBarcode.Text == "上一件条码 " + longCode &&
                rightEntry.Text.Length == 0 && vm.Left.ShowConfirmedTask, "the confirmed removal keeps the full previous barcode and leaves the other lane unchanged");
            await Until(() => !vm.Right.ShowRecentChute && vm.Right.ShowCenteredState && vm.Right.CenteredStateTitle == "等待投包" && !heldBarcode.IsVisible,
                "previous result expires to waiting through the existing refresh clock");
            Check(Stopwatch.GetElapsedTime(heldAt) is { TotalSeconds: >= 2.5 and < 4.5 } &&
                h.Peer.Requests.Count == requestCount + 1 && vm.Left.ShowConfirmedTask,
                "previous result expires after about three seconds without creating a request");
        }
        finally
        {
            release.Set(); h.App.Boundary = null; h.Peer.Hold?.TrySetResult(); h.Peer.Hold = null;
            second?.Close(); window.Close();
        }
    }
}

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
        await h.StartAsync();
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
            var tasks = (Grid)control.FindName("TaskFields");
            var edits = typeof(SupplyStationPosition).GetField("editing", BindingFlags.Instance | BindingFlags.NonPublic)!;
            async Task Read(uint frame, string code)
            {
                h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode(code), frame);
                await Until(() => h.Factory.Decoders[h.Left].Last is [{ Text: var text }] && text == code && p.ReadCueLabel.EndsWith(code[^Math.Min(4, code.Length)..], StringComparison.Ordinal), "decode/feedback " + code);
            }
            async Task Resolved(string code) => await Until(() => p.Operation?.ParcelCode == code && p.ShowConfirmedTask, "resolved " + code);
            string State() => Wire.Encode(new
            {
                Main = entry.Text,
                Model = p.Barcode,
                Read = p.ReadCueLabel,
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

            // Same position serially processes already-custodied observations while a prior RCS call is held.
            h.Peer.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
            await Read(3, "SYNTH-RCS-0003"); await Until(() => h.Peer.Requests.Count == 3, "held RCS call");
            await Read(4, "SYNTH-QUEUED-0004");
            await Read(5, "SYNTH-LATEST-0005");
            Check(entry.Text == "SYNTH-LATEST-0005" && !picture.IsEnabled && !tasks.IsVisible && h.Peer.Requests.Count == 3,
                "latest decoded parcel replaces two queued predecessors without dropping or dispatching their business responsibility");
            var regressions = new List<string>();
            TextChangedEventHandler changed = (_, _) => regressions.Add(entry.Text);
            entry.TextChanged += changed;
            h.Peer.Hold.TrySetResult(); h.Peer.Hold = null;
            await Resolved("SYNTH-LATEST-0005"); entry.TextChanged -= changed;
            Check(regressions.All(t => t == "SYNTH-LATEST-0005") && h.Peer.Requests.Count == 5,
                "late queued results cannot roll the main barcode back while every decoded input retains its own HTTP request");
            Check(picture.IsEnabled && tasks.IsVisible, "latest matching committed observation restores only its own picture and task fields");
            var priorId = p.Operation!.Id;
            h.Peer.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
            await Read(6, "SYNTH-LATEST-0005");
            await Until(() => h.Peer.Requests.Count == 6, "same-code independent request");
            Check(entry.Text == "SYNTH-LATEST-0005" && !picture.IsEnabled && !tasks.IsVisible && !(bool)edits.GetValue(control)!,
                "same-code new observation still retires old picture/task authority without creating a manual draft");
            h.Peer.Hold.TrySetResult(); h.Peer.Hold = null;
            await Until(() => p.ShowConfirmedTask && p.Operation!.Id != priorId, "same-code latest confirmed result");

            h.Api(h.Left).Enqueue(CameraRoundHarness.Multiple("SYNTH-MULTI-A", "SYNTH-MULTI-B"), 7);
            await Until(() => p.Input?.Observation.SourceEventId == "mvs.frame.7", "multiple input rejected");
            var placeholder = (TextBlock)control.FindName("BarcodePlaceholder");
            Check(entry.Text.Length == 0 && placeholder.IsVisible && placeholder.Text.Contains("多个条码") && p.Input!.Observation.Codes.Length == 2 &&
                p.RawCandidates.Contains("SYNTH-MULTI-A") && p.RawCandidates.Contains("SYNTH-MULTI-B") && h.Peer.Requests.Count == 6 && !picture.IsEnabled,
                "multiple raw candidates stay in original evidence with a clear main-field notice instead of a growing editable list");
            await SetClient(window, 800, 420); CaptureInternal(evidence, "camera-current-multiple-compact", (Grid)window.FindName("ClientArea"), lines);
            await Read(8, "SYNTH-AFTER-MULTI"); await Resolved("SYNTH-AFTER-MULTI");
            Check(entry.Text == "SYNTH-AFTER-MULTI" && !placeholder.IsVisible && !(bool)edits.GetValue(control)!,
                "single frame after multiple candidates replaces the notice and never appends older candidate text");

            // Intentional local editing stays separate from camera result projection.
            entry.Focus(); entry.Text = "INTENTIONAL-DRAFT"; entry.Select(3, 4);
            await Read(9, "SYNTH-DURING-DRAFT"); await Resolved("SYNTH-DURING-DRAFT");
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
                h.Peer.Requests.Count == requestCount && !((Button)rightControl.FindName("CurrentBarcode")).IsEnabled,
                "control-bearing candidate stays in raw evidence but cannot turn the primary entry into multiple editable lines or reuse old picture");
            var longCode = "SYNTH-" + new string('0', 90);
            h.Api(h.Right).Enqueue(CameraRoundHarness.Barcode(longCode), 3);
            await Until(() => vm.Right.ShowConfirmedTask && vm.Right.Barcode == longCode, "long single-code recovery");
            Check(rightEntry.Text == longCode && rightEntry.LineCount == 1 && !((TextBlock)rightControl.FindName("BarcodePlaceholder")).IsVisible,
                "long valid code stays complete on one line and replaces invalid/multiple notices");
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
        }
        finally
        {
            release.Set(); h.App.Boundary = null; h.Peer.Hold?.TrySetResult(); h.Peer.Hold = null;
            second?.Close(); window.Close();
        }
    }
}

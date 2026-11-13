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
        await using var h = new CameraRoundHarness(root, Path.Combine(evidence, "camera-current"), 15000)
            { StationClock = new PdaRoundClock() };
        h.Config.Wcs!.Parameters["request-timeout-ms"] = "15000";
        ExtensionStore.AtomicWrite(Path.Combine(h.Launch.ConfigurationDirectory, "business.json"), h.Config);
        await h.StartAsync(); await h.WaitForCameraAdmissionAsync();
        var window = new SupplyStationWindow(h.App, null, "standard"); window.Show();
        SupplyStationWindow? second = null;
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        try
        {
            var vm = window.ViewModel; var p = vm.Left;
            async Task Until(Func<bool> ready, string step)
            {
                for (var i = 0; i < 400; i++)
                { await vm.RefreshAsync(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); if (ready()) return; await Task.Delay(20); }
                throw new TimeoutException(step);
            }
            await SetClient(window, 1366, 768); await Until(() => vm.Fresh, "initial snapshot");
            var control = Descendants(window).OfType<SupplyStationPosition>().Single(c => ReferenceEquals(c.DataContext, p));
            var entry = (TextBox)control.FindName("BarcodeEntry"); var picture = (Button)control.FindName("CurrentBarcode");
            var tasks = (Grid)control.FindName("TaskFields"); var placeholder = (TextBlock)control.FindName("BarcodePlaceholder");
            uint left = 0, right = 0;
            async Task Frame(string device, CameraRoundHarness.RoundPixels pixels)
            {
                var position = device == h.Left ? "left" : "right";
                var before = h.App.WorkerProgress().Single(p => p.Position == position).Processed;
                var number = device == h.Left ? ++left : ++right;
                h.Api(device).Enqueue(pixels, number);
                // No-code frames publish recognition feedback, not business inputs.
                if (ReferenceEquals(pixels, CameraRoundHarness.EmptyTray))
                    await CameraRoundHarness.Until(() => Task.FromResult(h.Factory.Sources[device].Timings.Snapshot().RecentFrames.Any(f => f.Number == number)));
                else await CameraRoundHarness.Until(() => Task.FromResult(h.App.WorkerProgress().Single(p => p.Position == position).Processed > before));
                await vm.RefreshAsync(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            }
            async Task Next(string device)
            { await SmRoundFixture.NextVehicleAsync(h.App, device == h.Left ? "left" : "right"); await h.TrayAsync(device, TrayPresence.Present); await vm.RefreshAsync(); }
            async Task Resolved(string code) => await Until(() => p.Barcode == code && p.ShowConfirmedTask, "resolved " + code);

            // A decode cue has no authority to create a current business round.
            var blocked = 1;
            h.App.Boundary = point => { if (point == "BeforeIngressDurable" && Interlocked.Exchange(ref blocked, 0) == 1) { entered.Set(); release.Wait(); } };
            h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-FIRST-0001"), ++left);
            await CameraRoundHarness.Until(() => Task.FromResult(entered.IsSet));
            Check(entry.Text.Length == 0 && !picture.IsEnabled && !tasks.IsVisible && h.Peer.Requests.Count == 0,
                "a pre-admission decode cannot invent current-round barcode, image, task or request authority");
            release.Set(); h.App.Boundary = null; await Resolved("SYNTH-FIRST-0001");
            Check(entry.Text == p.Operation!.ParcelCode && picture.IsEnabled && tasks.IsVisible, "accepted current round owns its barcode, exact picture and task");
            var first = p.Operation!; var frozen = Wire.Encode(first.Wcs!.Request); var basis = vm.Snapshot!;
            await h.TrayAsync(h.Left, TrayPresence.Present);
            await Frame(h.Left, CameraRoundHarness.Barcode("SYNTH-CHANGED-OBSERVATION"));
            Check(p.Operation?.Id == first.Id && entry.Text == first.ParcelCode && h.Peer.Requests.Count == 1,
                "changed code alone cannot create a second round or replace the resting parcel result");

            await Next(h.Left); await Frame(h.Left, CameraRoundHarness.Barcode("SYNTH-SECOND-0002")); await Resolved("SYNTH-SECOND-0002");
            await vm.AcceptPublishedSnapshotAsync(basis);
            Check(entry.Text == "SYNTH-SECOND-0002" && p.ShowConfirmedTask && Wire.Encode((await h.App.OperationAsync(first.Id))!.Wcs!.Request) == frozen,
                "a stale old-round snapshot cannot restore its card or rewrite the original frozen request");
            second = new SupplyStationWindow(h.App, null, "compact"); second.Show();
            await CameraRoundHarness.Until(async () =>
            {
                // Loaded can already own the first refresh; an overlapping
                // RefreshAsync call is not evidence that its snapshot arrived.
                await second.ViewModel.RefreshAsync(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                return second.ViewModel.Fresh && second.ViewModel.Left.CurrentRound?.Id == p.CurrentRound?.Id;
            });
            Check(second.ViewModel.Left.CurrentRound?.Id == p.CurrentRound?.Id && second.ViewModel.Left.Operation?.Id == p.Operation?.Id,
                "a second native window projects the same Application-owned current round");
            second.Close(); second = null;

            await Next(h.Left); h.Peer.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
            h.Peer.HoldWhen = body => body.Contains("SYNTH-RCS-0003", StringComparison.Ordinal);
            await Frame(h.Left, CameraRoundHarness.Barcode("SYNTH-RCS-0003"));
            await Until(() => p.Operation?.Wcs?.State == StageState.Dispatching, "held original");
            var held = p.Operation!; var heldRequest = Wire.Encode(held.Wcs!.Request);
            await Next(h.Left); await Frame(h.Left, CameraRoundHarness.Barcode("SYNTH-LATEST-0004")); await Resolved("SYNTH-LATEST-0004");
            var currentId = p.CurrentRound!.Id;
            Check(!h.Peer.Hold.Task.IsCompleted && h.Peer.Requests.Count == 4 && picture.IsEnabled && tasks.IsVisible,
                "next same-position round receives its result while the removed original remains in flight");
            h.Peer.Hold.TrySetResult(); h.Peer.Hold = null; h.Peer.HoldWhen = null;
            await CameraRoundHarness.Until(async () => (await h.App.OperationAsync(held.Id))?.Wcs?.State == StageState.Accepted);
            await vm.RefreshAsync();
            Check(p.CurrentRound?.Id == currentId && entry.Text == "SYNTH-LATEST-0004" && Wire.Encode((await h.App.OperationAsync(held.Id))!.Wcs!.Request) == heldRequest && h.Peer.Requests.Count == 4,
                "late old success only completes its record and leaves the current image, task and barcode intact");
            await Frame(h.Left, CameraRoundHarness.EmptyTray);
            Check(p.CurrentRound?.Id == currentId && p.ShowConfirmedTask, "a no-code frame is not qualified removal");
            await SmRoundFixture.NextVehicleAsync(h.App, "left"); await vm.RefreshAsync();
            Check(p.CurrentRound is null && !p.ShowConfirmedTask && p.ShowRecentChute && !picture.IsEnabled && !tasks.IsVisible && p.CanSubmitBarcode &&
                entry.Text.Length == 0 && p.DisplaySlot == "SYNTH-CHUTE" && p.RecentChuteBarcode.Contains("SYNTH-LATEST-0004") &&
                p.DisplayStatus.Contains("上一件") && p.DisplayNextStep == "当前：可以扫码投包" && h.Peer.Requests.Count == 4,
                "new vehicle immediately permits input while the previous successful chute and task remain explicitly read-only for three seconds");
            CaptureInternal(evidence, "previous-result-ready-1366", (Grid)window.FindName("ClientArea"), lines);
            await SetClient(window, 800, 420); await vm.RefreshAsync();
            CaptureInternal(evidence, "previous-result-ready-800", (Grid)window.FindName("ClientArea"), lines);

            await h.TrayAsync(h.Left, TrayPresence.Present);
            await Frame(h.Left, CameraRoundHarness.Multiple("SYNTH-MULTI-A", "SYNTH-MULTI-B"));
            var correcting = p.CurrentRound!.Id;
            Check(!p.ShowRecentChute && p.CurrentRound.FormalCodes.Length == 2 && entry.Text.Contains("SYNTH-MULTI-A") && entry.Text.Contains("SYNTH-MULTI-B") &&
                p.CenteredStateReason.Contains("多个") && picture.IsEnabled && h.Peer.Requests.Count == 4,
                "multiple formal labels have their exact image and a correctable unsent round");
            await SetClient(window, 800, 420); CaptureInternal(evidence, "camera-round-multiple-compact", (Grid)window.FindName("ClientArea"), lines);
            await Frame(h.Left, CameraRoundHarness.Barcode("SYNTH-CORRECTED")); await Resolved("SYNTH-CORRECTED");
            Check(p.CurrentRound?.Id == correcting && entry.Text == "SYNTH-CORRECTED" && !placeholder.IsVisible && h.Peer.Requests.Count == 5,
                "a corrected unique label submits once within the same round instead of locking permanent rejection");

            await SmRoundFixture.NextVehicleAsync(h.App, "left"); await vm.RefreshAsync();
            Check(p.ShowRecentChute && p.CanSubmitBarcode, "previous result retention does not reserve the next parcel's submission claim");
            var retention = System.Diagnostics.Stopwatch.StartNew();
            while (retention.Elapsed < TimeSpan.FromMilliseconds(3200)) { await vm.RefreshAsync(); await Task.Delay(80); }
            Check(!p.ShowRecentChute && p.CanSubmitBarcode && entry.Text.Length == 0 && h.Peer.Requests.Count == 5,
                "repeated fresh snapshots do not extend previous-result retention or send a request");

            entry.Focus(); entry.Text = "INTENTIONAL-DRAFT"; entry.Select(3, 4);
            await Next(h.Left); await Frame(h.Left, CameraRoundHarness.Barcode("SYNTH-DURING-DRAFT")); await Resolved("SYNTH-DURING-DRAFT");
            Check(entry.Text == "INTENTIONAL-DRAFT" && entry.SelectionStart == 3 && entry.SelectionLength == 4 && !picture.IsEnabled,
                "camera round replacement preserves a manual draft and caret without lending the camera picture");
            ((Button)control.FindName("SubmitBarcode")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(h.Peer.Requests.Count == 6, "a stale draft cannot append a first call in an already submitted new round");
            await h.TrayAsync(h.Right, TrayPresence.Present);
            await Frame(h.Right, CameraRoundHarness.Barcode("SYNTH\nINVALID"));
            var rightControl = Descendants(window).OfType<SupplyStationPosition>().Single(c => ReferenceEquals(c.DataContext, vm.Right));
            var rightEntry = (TextBox)rightControl.FindName("BarcodeEntry");
            Check(rightEntry.Text.Length == 0 && vm.Right.CurrentRound is { FormalCodes.Length: 0, OperationId: null } &&
                vm.Right.RawCandidates.Contains('\n') && vm.Right.CanViewCurrentPicture && h.Peer.Requests.Count == 6,
                "a control-bearing candidate remains raw evidence with its exact image and never becomes an editable multiline barcode or request");
            var longCode = "SYNTH-" + new string('0', 90);
            await Frame(h.Right, CameraRoundHarness.Barcode(longCode)); await Until(() => vm.Right.ShowConfirmedTask, "independent long right result");
            Check(rightEntry.Text == longCode && rightEntry.LineCount > 1 && !rightEntry.AcceptsReturn && rightEntry.VerticalScrollBarVisibility == ScrollBarVisibility.Auto && entry.Text == "INTENTIONAL-DRAFT",
                "the other lane keeps a complete wrapping long code while the left retains its draft");
            var projection = new SupplyStationViewModel(h.App); var snapshot = await h.App.SnapshotAsync(); await projection.AcceptPublishedSnapshotAsync(snapshot);
            var input = projection.Right.Input!.Observation;
            var cue = new ParcelReadFeedback(snapshot.RuntimeId, "right", Wire.Id(), input.DeviceId, input.Epoch, "UNOWNED-CODE", Stopwatch.GetTimestamp(), input.ObservedAt.AddHours(-1)) { SourceSequence = input.Sequence + 1 };
            projection.Right.GetType().GetMethod("ApplyReadFeedback", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(projection.Right, [cue]);
            Check(projection.Right.Barcode == longCode && projection.Right.CurrentRound?.Id == vm.Right.CurrentRound?.Id,
                "a later source cue with a changed clock cannot replace current-round authority");
            CaptureInternal(evidence, "camera-current-rounds", (Grid)window.FindName("ClientArea"), lines);
        }
        finally { release.Set(); h.App.Boundary = null; h.Peer.Hold?.TrySetResult(); second?.Close(); window.Close(); }
    }
}

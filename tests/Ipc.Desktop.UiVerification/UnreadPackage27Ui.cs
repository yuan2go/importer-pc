using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Platform;

static partial class Program
{
    static async Task VerifyUnreadPackage27Ui(string root, string evidence, List<string> lines)
    {
        void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); lines.Add("PASS UNREAD27-NATIVE " + text); }
        await using var h = new CameraRoundHarness(root, Path.Combine(evidence, "unread27-native")); await h.StartAsync();
        var window = new SupplyStationWindow(h.App, null, "standard"); window.Show();
        try
        {
            await SetClient(window, 1366, 768);
            async Task Until(Func<bool> predicate)
            {
                for (var i = 0; i < 300; i++) { await window.ViewModel.RefreshAsync(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); if (predicate()) return; await Task.Delay(20); }
                throw new TimeoutException("Unread27 native surface did not converge");
            }
            var vm = window.ViewModel;
            await Until(() => vm.Fresh && vm.ViewportReady);
            h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-PREVIOUS-27"), 1);
            h.Api(h.Right).Enqueue(CameraRoundHarness.Barcode("SYNTH-RIGHT-27"), 1);
            await Until(() => vm.Left.ShowConfirmedTask && vm.Right.ShowConfirmedTask);
            var blank = new CameraRoundHarness.RoundPixels(Enumerable.Repeat((byte)180, 640 * 240).ToArray(), 640, 240);
            var oldSnapshot = vm.Snapshot!;
            h.Api(h.Left).Enqueue(blank, 2);
            await Until(() => vm.Left.DisplayStatus == "未读到条码");
            Check(!vm.Left.ShowConfirmedTask && !vm.Left.CurrentTaskAvailable && !vm.Left.CanViewCurrentPicture && vm.Left.Barcode == "" && vm.Left.DisplaySlot == "" && vm.Left.TaskId == "—" && vm.Right.ShowConfirmedTask,
                "no-code clears left barcode, slot, task and picture authority while preserving the right current task");
            await vm.AcceptPublishedSnapshotAsync(oldSnapshot, default);
            Check(!vm.Left.ShowConfirmedTask && vm.Left.DisplayStatus == "未读到条码", "an older business snapshot cannot revive a superseded confirmed slot");
            foreach (var (width, height) in new[] { (1366, 768), (800, 420) })
            {
                await SetClient(window, width, height); window.UpdateLayout();
                var left = Descendants(window).OfType<SupplyStationPosition>().Single(p => ReferenceEquals(p.DataContext, vm.Left));
                var reason = (TextBox)left.FindName("AttentionReason");
                Check(reason.IsVisible && reason.ActualHeight >= reason.FontSize * 1.2 && vm.Left.DisplayNextStep.Contains("补录"), width + " exposes a readable no-code reason and rescan/manual recovery");
                CaptureInternal(evidence, "unread27-no-code-" + width, (Grid)window.FindName("ClientArea"), lines);
            }
            await SetClient(window, 1366, 768);
            h.Peer.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-LATE-27"), 3);
                await Until(() => vm.Left.IsProcessing);
                h.Api(h.Left).Enqueue(blank, 4);
                await Until(() => vm.Left.DisplayStatus == "未读到条码");
            }
            finally { h.Peer.Hold.TrySetResult(); h.Peer.Hold = null; }
            await Until(() => vm.Left.Operation?.State == ParcelState.SlotResolved);
            Check(!vm.Left.ShowConfirmedTask && !vm.Left.CanViewCurrentPicture, "a late matching RCS response updates its original operation without reviving the unread frame's slot");
            var snapshot = await h.App.SnapshotAsync(); var input = snapshot.Inputs!.Single(i => i.PositionId == "left");
            var receipt = await h.App.SubmitParcelBarcodeAsync(new(Wire.Id(), snapshot.RuntimeId, snapshot.Configuration.Revision, "left", "SYNTH-MANUAL-27", ParcelBarcodeInputKind.Keyboard,
                input.Observation.Id, snapshot.Positions!.Single(p => p.Id == "left").CurrentOperation), default);
            await Until(() => vm.Left.ShowConfirmedTask && vm.Left.Barcode == "SYNTH-MANUAL-27");
            h.Api(h.Left).Enqueue(blank, 5);
            await Until(() => vm.Snapshot!.CameraReads.Any(f => f.PositionId == "left" && f.Frame.FrameNumber == 5));
            Check(receipt.Status == "Admitted" && vm.Left.ShowConfirmedTask && vm.Left.Barcode == "SYNTH-MANUAL-27", "camera no-code does not invalidate an independent manual barcode and its accepted RCS result");
            h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-NEW-27"), 6);
            await Until(() => vm.Left.ShowConfirmedTask && vm.Left.Barcode == "SYNTH-NEW-27");
            Check(h.Peer.Requests.Count == 5, "next successful camera observation replaces the manual display with one distinct new request; empty frames add none");
            var oldEpoch = h.Factory.Sources[h.Left].Epoch;
            await h.App.ReconnectDeviceAsync(h.Left);
            await Until(() => h.Factory.Sources[h.Left].Epoch != oldEpoch && vm.Snapshot!.Devices!.Single(d => d.DeviceId == h.Left).IsValid(DateTimeOffset.UtcNow));
            Check(!vm.Left.ShowConfirmedTask && !vm.Left.CanViewCurrentPicture && vm.Left.DisplayStatus == "等待重新扫码", "healthy reconnect without a new read cannot revive the prior connection's accepted slot");
            h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-RECONNECTED-27"), 1);
            await Until(() => vm.Left.ShowConfirmedTask && vm.Left.Barcode == "SYNTH-RECONNECTED-27");
            Check(h.Peer.Requests.Count == 6, "verified first read in a new epoch restores its own task with exactly one new request");
        }
        finally { window.Close(); }
    }
}

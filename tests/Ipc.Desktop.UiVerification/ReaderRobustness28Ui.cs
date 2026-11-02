using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Application.Sportmaster.Desktop;

static partial class Program
{
    static async Task VerifyReaderRobustness28Ui(string root, string evidence, List<string> lines)
    {
        void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); lines.Add("PASS READER28-NATIVE " + text); }
        await using var h = new CameraRoundHarness(root, Path.Combine(evidence, "reader28-native")); await h.StartAsync();
        var window = new SupplyStationWindow(h.App, null, "standard"); window.Show();
        var queueField = typeof(SupplyStationWindow).GetField("presentationDispatchQueued", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var queueMethod = typeof(SupplyStationWindow).GetMethod("QueuePresentationDispatch", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var queueGate = typeof(SupplyStationWindow).GetField("presentationUiGate", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var vm = window.ViewModel;
        async Task Until(Func<bool> done)
        {
            for (var i = 0; i < 300; i++) { await vm.RefreshAsync(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); if (done()) return; await Task.Delay(20); }
            throw new TimeoutException("Reader28 native state did not converge");
        }
        try
        {
            await SetClient(window, 1366, 768); await Until(() => vm.Fresh && vm.ViewportReady);
            h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-OLD-28"), 1);
            h.Api(h.Right).Enqueue(CameraRoundHarness.Barcode("SYNTH-RIGHT-28"), 1);
            await Until(() => vm.Left.ShowConfirmedTask && vm.Right.ShowConfirmedTask);
            var prior = vm.Snapshot!;
            h.Api(h.Left).Enqueue(new(Enumerable.Repeat((byte)180, 640 * 240).ToArray(), 640, 240), 2);
            await Until(() => vm.Left.DisplayStatus == "未读到条码");
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            // Simulate a delayed Render callback, not a fake business state. The
            // normal snapshot reader is still running and must remain safe alone.
            lock (queueGate) queueField.SetValue(window, true);
            h.Peer.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
            h.App.Boundary = point => { if (point == "BeforeIngressDurable") { entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("Native snapshot-first gate timed out"); } };
            h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-NEW-28"), 3);
            await Until(() => entered.IsSet && vm.Snapshot!.CameraReads.Any(f => f.PositionId == "left" && f.Frame.FrameNumber == 3));
            Check(!vm.Left.ShowConfirmedTask && !vm.Left.CanViewCurrentPicture && vm.Left.Barcode == "SYNTH-NEW-28" && vm.Left.DisplaySlot != "SYNTH-CHUTE" && vm.Right.ShowConfirmedTask,
                "snapshot-first native path shows the new pending barcode and never the old chute/picture while its stream render is delayed");
            Check(h.Peer.Requests.Count == 2 && vm.Left.Input!.Observation.SourceEventId == "mvs.frame.1", "the assertion runs before the new input is durable or any new HTTP request exists");
            foreach (var (w, height) in new[] { (1366, 768), (800, 420) })
            {
                await SetClient(window, w, height); window.UpdateLayout();
                var left = Descendants(window).OfType<SupplyStationPosition>().Single(p => ReferenceEquals(p.DataContext, vm.Left));
                var text = (TextBlock)left.FindName("NextStep");
                Check(!vm.Left.ShowConfirmedTask && text.IsVisible && text.ActualHeight >= text.FontSize * 1.2 && text.Text.Contains("勿按上一件格口投递"), w + " preserves a readable pending instruction with no retired destination");
                CaptureInternal(evidence, "reader28-snapshot-first-" + w, (Grid)window.FindName("ClientArea"), lines);
            }
            await vm.AcceptPublishedSnapshotAsync(prior, default);
            Check(!vm.Left.ShowConfirmedTask && !vm.Left.CanViewCurrentPicture, "late previous native snapshot cannot grant a retired task again");
            lock (queueGate) { queueField.SetValue(window, false); queueMethod.Invoke(window, null); }
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Check(!vm.Left.ShowConfirmedTask && vm.Left.Barcode == "SYNTH-NEW-28", "late stream rendering agrees with the snapshot watermark instead of changing parcel identity");
            release.Set(); h.App.Boundary = null;
            await Until(() => h.Peer.Requests.Count == 3);
            Check(!vm.Left.ShowConfirmedTask && vm.Left.IsProcessing, "an in-flight original request still has no confirmed chute");
            h.Peer.Hold.TrySetResult(); h.Peer.Hold = null;
            await Until(() => vm.Left.ShowConfirmedTask && vm.Left.Barcode == "SYNTH-NEW-28");
            Check(h.Peer.Requests.Count == 3 && vm.Right.Barcode == "SYNTH-RIGHT-28", "only the matched new response restores the left task and the right task never changes identity");
        }
        finally
        {
            release.Set(); h.App.Boundary = null; h.Peer.Hold?.TrySetResult(); h.Peer.Hold = null;
            lock (queueGate) queueField.SetValue(window, false);
            window.Close();
        }
    }
}

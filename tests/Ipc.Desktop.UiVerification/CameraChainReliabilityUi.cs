using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Application.Sportmaster.Desktop;

static partial class Program
{
    static async Task VerifyCameraChainReliabilityUi(string root, string evidence, List<string> lines)
    {
        void Check(bool ok, string detail) { if (!ok) throw new InvalidOperationException(detail); lines.Add("PASS CHAIN20-NATIVE " + detail); }
        lines.Add("Native SendInput/WPF with explicitly labelled UI-only terminal receipts; camera leg uses actual ZXing/Application/SQLite and synthetic MVS/loopback RCS. Physical devices NOT_RUN.");
        await using (var h = new ManualSupplyHarness(root, Path.Combine(evidence, "terminal"), deferred: true))
        {
            await h.StartAsync(); var service = DispatchProxy.Create<IBarcodeTransportFixture, BarcodeTransportProbe>();
            var transport = (BarcodeTransportProbe)(object)service; transport.Inner = h.App;
            var window = new SupplyStationWindow(service, null, "standard"); window.Show();
            try
            {
                await SetClient(window, 1366, 768);
                for (var i = 0; i < 100 && !window.ViewModel.Left.CanSubmitBarcode; i++) { await window.ViewModel.RefreshAsync(); await Task.Delay(20); }
                var position = Descendants(window).OfType<SupplyStationPosition>().Single(c => ReferenceEquals(c.DataContext, window.ViewModel.Left));
                var entry = (TextBox)position.FindName("BarcodeEntry");
                var editing = typeof(SupplyStationPosition).GetField("editing", BindingFlags.Instance | BindingFlags.NonPublic)!;
                window.Activate(); entry.Focus(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                async Task Send(string text) => await BarcodeNativeKeyboard.SendAsync(window, entry, text.Select(c => (ushort)c).Append((ushort)0x0D).ToArray());
                foreach (var status in new[] { "Rejected", "Blocked", "Duplicate", "Conflict" })
                {
                    transport.NextReply = "Terminal/" + status; await Send("000123450001");
                    for (var i = 0; i < 100 && ((bool)editing.GetValue(position)! || entry.IsReadOnly || window.ViewModel.Busy); i++) await Task.Delay(20);
                    Check(!(bool)editing.GetValue(position)! && entry.Text == "000123450001" && entry.SelectionLength == entry.Text.Length,
                        status + $" terminal receipt retains and selects the result while releasing edit ownership: text={entry.Text}, selection={entry.SelectionLength}, editing={editing.GetValue(position)}, feedback={((TextBlock)position.FindName("InputFeedback")).Text}");
                    transport.NextReply = "Terminal/Rejected"; await Send("000123450002");
                    for (var i = 0; i < 100 && ((bool)editing.GetValue(position)! || entry.IsReadOnly || window.ViewModel.Busy); i++) await Task.Delay(20);
                    Check(transport.Commands.Last().Barcode == "000123450002" && entry.Text == "000123450002" && h.Peer.Requests.IsEmpty,
                        status + " followed by native scanner input replaces A with B, never AB; UI projection emits no HTTP");
                }
            }
            finally { window.Close(); }
        }
        await using (var h = new CameraRoundHarness(root, Path.Combine(evidence, "camera-clear"), 15000))
        {
            await h.StartAsync(); var window = new SupplyStationWindow(h.App, null, "standard"); window.Show();
            try
            {
                await SetClient(window, 1366, 768); var vm = window.ViewModel;
                async Task Until(Func<bool> condition) { for (var i = 0; i < 250; i++) { await vm.RefreshAsync(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); if (condition()) return; await Task.Delay(20); } throw new TimeoutException("Camera clear/recovery did not converge"); }
                await Until(() => vm.Fresh);
                var control = Descendants(window).OfType<SupplyStationPosition>().Single(c => ReferenceEquals(c.DataContext, vm.Left));
                var entry = (TextBox)control.FindName("BarcodeEntry");
                h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-SAME"), 1); await Until(() => vm.Left.ShowConfirmedTask);
                entry.Text = "DRAFT"; entry.Clear();
                h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-SAME"), 2);
                await Until(() => vm.Left.Input?.Observation.SourceEventId == "mvs.frame.2" && vm.Left.ShowConfirmedTask);
                Check(entry.Text == "SYNTH-SAME", "deleting a draft to empty resumes the camera observation for the same placement");
                entry.ContextMenu.Items.OfType<MenuItem>().Single(m => Equals(m.Header, "清空输入")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-SAME"), 3);
                await Until(() => vm.Left.Input?.Observation.SourceEventId == "mvs.frame.3" && vm.Left.ShowConfirmedTask);
                Check(entry.Text == "SYNTH-SAME" && h.Peer.Requests.Count == 1 && ParcelInputReasons.IsSamePlacement(vm.Left.Input!),
                    "clearing text releases edit ownership but does not turn three observations of one placement into three RCS requests");
                uint frameNumber = 4;
                foreach (var size in new[] { (1366, 768), (800, 420) })
                {
                    await SetClient(window, size.Item1, size.Item2); await Until(() => vm.Left.ShowConfirmedTask);
                    var fields = (Grid)control.FindName("TaskFields"); var result = (Border)control.FindName("ResultPanel");
                    var shortFieldHeight = fields.ActualHeight;
                    h.Peer.ReplyWaveNo = size.Item1 == 1366 ? new string('W', 64) : null;
                    var preparation = await h.ReplaceParcelAsync(h.Left, frameNumber); frameNumber = preparation + 1;
                    h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-SAME"), preparation);
                    await Until(() => vm.Left.ShowConfirmedTask && vm.Left.Input?.Observation.SourceEventId == "mvs.frame." + preparation);
                    window.UpdateLayout(); var fieldHeight = fields.ActualHeight; var resultHeight = result.ActualHeight;
                    if (size.Item1 == 1366) Check(fieldHeight > shortFieldHeight + 1, "long confirmed wave wraps into a taller measured task row before the stability check");
                    var restingRequests = h.Peer.Requests.Count;
                    var restingFrame = frameNumber++; h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-SAME"), restingFrame);
                    await Until(() => vm.Left.ShowConfirmedTask && vm.Left.Input?.Observation.SourceEventId == "mvs.frame." + restingFrame);
                    Check(h.Peer.Requests.Count == restingRequests && Math.Abs(fields.ActualHeight - fieldHeight) < 1 && Math.Abs(result.ActualHeight - resultHeight) < 1,
                        $"{size.Item1} another observation of the resting placement preserves the confirmed task geometry without another request");
                    var sends = h.Peer.Requests.Count; h.Peer.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    try
                    {
                        var nextFrame = await h.ReplaceParcelAsync(h.Left, frameNumber); frameNumber = nextFrame + 1;
                        h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-SAME"), nextFrame);
                        await Until(() => h.Peer.Requests.Count == sends + 1 && vm.Left.IsProcessing); window.UpdateLayout();
                        var pendingId = vm.Left.Operation!.Id; var pendingFieldHeight = fields.ActualHeight; var pendingResultHeight = result.ActualHeight;
                        var repeatedFrame = frameNumber++; h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-SAME"), repeatedFrame);
                        await Until(() => vm.Snapshot!.CameraReads.Any(r => r.PositionId == "left" && r.Frame.FrameNumber == repeatedFrame)); window.UpdateLayout();
                        Check(fields.Visibility == Visibility.Hidden && !fields.IsVisible && Math.Abs(fields.ActualHeight - pendingFieldHeight) < 1 && Math.Abs(result.ActualHeight - pendingResultHeight) < 1 && h.Peer.Requests.Count == sends + 1,
                            $"{size.Item1} repeated reads during the new placement's pending request keep its geometry stable without another request");
                        Check(!vm.Left.ShowConfirmedTask && vm.Left.WaveNo == "—" && ((TextBox)control.FindName("CurrentSlot")).Text == "处理中…",
                            $"{size.Item1} stable layout never borrows the old task or assigned chute while the next request is pending");
                        h.Peer.Hold.TrySetResult(); h.Peer.Hold = null;
                        await Until(() => vm.Left.ShowConfirmedTask && vm.Left.Operation?.Id == pendingId && vm.Left.Input?.Observation.SourceEventId == "mvs.frame." + repeatedFrame);
                        Check(fields.IsVisible && h.Peer.Requests.Count == sends + 1, $"{size.Item1} completion restores the matching task with exactly one new HTTP request");
                    }
                    finally { h.Peer.Hold?.TrySetResult(); h.Peer.Hold = null; }
                }
                var soundCount = typeof(SupplyStationPosition).GetProperty("PlayedSuccessSounds", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var right = Descendants(window).OfType<SupplyStationPosition>().Single(c => ReferenceEquals(c.DataContext, vm.Right));
                h.Api(h.Right).Enqueue(CameraRoundHarness.Barcode("SYNTH-RIGHT"), 1);
                await Until(() => vm.Right.ShowConfirmedTask && (int)soundCount.GetValue(right)! == 1);
                Check((int)soundCount.GetValue(right)! == 1, "right position success sound invocation is independent of left cooldown");
                await Until(() => (int)soundCount.GetValue(control)! >= 2);
                Check((int)soundCount.GetValue(control)! >= 2, "still-current deferred left result invokes its sound after cooldown without another read; audible hardware NOT_RUN");
                CaptureInternal(evidence, "chain20-camera-cleared-recovered", (Grid)window.FindName("ClientArea"), lines);
            }
            finally { window.Close(); }
        }
    }
}

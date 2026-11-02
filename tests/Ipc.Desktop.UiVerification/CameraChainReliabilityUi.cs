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
                // Each frame is the next parcel: one placement of a code is one request (SM-PLACEMENT-PRESENCE-29).
                static string Parcel(uint frame) => "SYNTH-NEXT-" + frame;
                h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode(Parcel(1)), 1); await Until(() => vm.Left.ShowConfirmedTask);
                entry.Text = "DRAFT"; entry.Clear();
                h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode(Parcel(2)), 2);
                await Until(() => vm.Left.Input?.Observation.SourceEventId == "mvs.frame.2" && vm.Left.ShowConfirmedTask);
                Check(entry.Text == Parcel(2), "deleting a draft to empty resumes camera projection for the next independent parcel");
                entry.ContextMenu.Items.OfType<MenuItem>().Single(m => Equals(m.Header, "清空输入")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode(Parcel(3)), 3);
                await Until(() => vm.Left.Input?.Observation.SourceEventId == "mvs.frame.3" && vm.Left.ShowConfirmedTask);
                Check(entry.Text == Parcel(3) && h.Peer.Requests.Count == 3, "clear action releases edit ownership and all three independent parcels still reach RCS");
                uint frameNumber = 3;
                foreach (var size in new[] { (1366, 768), (800, 420) })
                {
                    await SetClient(window, size.Item1, size.Item2); await Until(() => vm.Left.ShowConfirmedTask);
                    var fields = (Grid)control.FindName("TaskFields"); var result = (Border)control.FindName("ResultPanel");
                    var shortFieldHeight = fields.ActualHeight;
                    h.Peer.ReplyWaveNo = size.Item1 == 1366 ? new string('W', 64) : null;
                    var preparation = ++frameNumber; h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode(Parcel(preparation)), preparation);
                    await Until(() => vm.Left.ShowConfirmedTask && vm.Left.Input?.Observation.SourceEventId == "mvs.frame." + preparation);
                    window.UpdateLayout(); var fieldHeight = fields.ActualHeight; var resultHeight = result.ActualHeight;
                    if (size.Item1 == 1366) Check(fieldHeight > shortFieldHeight + 1, "long confirmed wave wraps into a taller measured task row before the stability check");
                    var sends = h.Peer.Requests.Count; h.Peer.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    try
                    {
                        var nextFrame = ++frameNumber; h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode(Parcel(nextFrame)), nextFrame);
                        await Until(() => h.Peer.Requests.Count == sends + 1 && vm.Left.IsProcessing); window.UpdateLayout();
                        Check(fields.Visibility == Visibility.Hidden && !fields.IsVisible && Math.Abs(fields.ActualHeight - fieldHeight) < 1 && Math.Abs(result.ActualHeight - resultHeight) < 1,
                            $"{size.Item1} next-parcel request keeps task/result geometry stable without rendering the old task");
                        Check(!vm.Left.ShowConfirmedTask && vm.Left.WaveNo == "—" && ((TextBox)control.FindName("CurrentSlot")).Text == "处理中…",
                            $"{size.Item1} stable layout never borrows the old task or assigned chute while the next request is pending");
                        h.Peer.Hold.TrySetResult(); h.Peer.Hold = null;
                        await Until(() => vm.Left.ShowConfirmedTask && vm.Left.Input?.Observation.SourceEventId == "mvs.frame." + nextFrame);
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

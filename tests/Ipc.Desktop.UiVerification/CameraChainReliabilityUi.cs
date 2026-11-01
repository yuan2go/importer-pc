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
                Check(entry.Text == "SYNTH-SAME", "deleting a draft to empty resumes camera projection even when the next independent frame has the same code");
                entry.ContextMenu.Items.OfType<MenuItem>().Single(m => Equals(m.Header, "清空输入")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-SAME"), 3);
                await Until(() => vm.Left.Input?.Observation.SourceEventId == "mvs.frame.3" && vm.Left.ShowConfirmedTask);
                Check(entry.Text == "SYNTH-SAME" && h.Peer.Requests.Count == 3, "clear action releases edit ownership and all three independent same-code frames still reach RCS");
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

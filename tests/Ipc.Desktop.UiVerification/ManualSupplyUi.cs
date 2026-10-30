using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Application.Sportmaster.Desktop;

static partial class Program
{
    static async Task VerifyManualSupplyUi(string root, string evidence, List<string> lines)
    {
        void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); lines.Add("PASS MANUAL-UI " + message); }
        foreach (var deferred in new[] { true, false })
        {
            await using var h = new ManualSupplyHarness(root, Path.Combine(evidence, deferred ? "manual-deferred" : "manual-offline"), deferred);
            await h.StartAsync();
            var window = new SupplyStationWindow(h.App, null, "standard") { ConfirmDiscardDrafts = _ => true, Title = "人工供包 · 无相机合成验证" };
            window.Show(); var vm = window.ViewModel;
            async Task Flush() { await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout(); }
            async Task Until(Func<bool> ready)
            {
                for (var i = 0; i < 150; i++) { await vm.RefreshAsync(); await Flush(); if (ready()) return; await Task.Delay(25); }
                throw new TimeoutException("Manual supply native UI did not converge");
            }
            try
            {
                await SetClient(window, 1366, 768); await Until(() => vm.Fresh && vm.Left.CanAdmit && vm.Right.CanAdmit);
                Check(!vm.IsManagementMode && vm.OperatorStatus == "可以供包" && !vm.ShowSetupGuidance &&
                    vm.Left.DeviceLabel.Contains(deferred ? "待配置" : "离线") && vm.Left.BlockingReasons.Length == 0,
                    "ordinary mode permits manual supply while preserving camera facts; deferred=" + deferred);
                var positions = Descendants(window).OfType<SupplyStationPosition>().ToArray();
                var left = positions.Single(p => ReferenceEquals(p.DataContext, vm.Left));
                var right = positions.Single(p => ReferenceEquals(p.DataContext, vm.Right));
                ((TextBox)left.FindName("BarcodeEntry")).Text = "000123456";
                ((Button)left.FindName("SubmitBarcode")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Until(() => vm.Left.SlotAvailable && !vm.Busy);
                Check(vm.Left.Barcode == "000123456" && vm.Left.Slot == "SYNTH-CHUTE" && vm.Left.ScanSummary.StartsWith("手动键盘输入") && h.Peer.Requests.Count == 1,
                    "bottom submit sends the complete keyboard barcode through Application and real built-in RCS");
                ((MenuItem)right.FindName("PdaSource")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                var entry = (TextBox)right.FindName("BarcodeEntry"); entry.Text = "000123457";
                entry.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, 0, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                await Until(() => vm.Right.SlotAvailable && !vm.Busy);
                Check(vm.Right.Barcode == "000123457" && vm.Right.ScanSummary.StartsWith("PDA键盘输入") && h.Peer.Requests.Count == 2 &&
                    vm.Right.Operation!.Acquisition is null, "PDA Enter independently supplies the other position without any camera acquisition");
                CaptureInternal(evidence, deferred ? "manual-deferred-native" : "manual-offline-native", (Grid)window.FindName("ClientArea"), lines);
                await SetClient(window, 800, 420); await Flush();
                var submit = (Button)right.FindName("SubmitBarcode"); submit.BringIntoView(); await Flush();
                Check(Inside(submit, (Grid)window.FindName("ClientArea")) && !entry.IsReadOnly, "compact layout retains editable barcode and reachable bottom submit");
            }
            finally { window.Close(); }
        }
    }
}

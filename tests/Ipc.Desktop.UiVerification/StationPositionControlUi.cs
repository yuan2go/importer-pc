using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Platform;
using Ipc.Presentation.Wpf;

static partial class Program
{
    static async Task VerifyStationPositionControlUi(string root, string evidence, List<string> lines)
    {
        void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); lines.Add("PASS POSITION-UI " + text); }
        await using var h = new ManualSupplyHarness(root, Path.Combine(evidence, "position-control"), false);
        await h.StartAsync();
        var window = new SupplyStationWindow(h.App, null, "standard"); window.Show();
        var vm = window.ViewModel;
        async Task Flush() { await vm.RefreshAsync(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout(); }
        async Task Until(Func<bool> ready)
        {
            for (var i = 0; i < 150; i++) { await Flush(); if (ready()) return; await Task.Delay(30); }
            throw new TimeoutException("Position UI did not converge");
        }
        try
        {
            await SetClient(window, 1366, 768); await Until(() => vm.Fresh && vm.Left.CanChangeSupply);
            var positions = Descendants(window).OfType<SupplyStationPosition>().ToArray();
            var left = positions.Single(p => ReferenceEquals(p.DataContext, vm.Left)); var right = positions.Single(p => ReferenceEquals(p.DataContext, vm.Right));
            var leftEntry = (TextBox)left.FindName("BarcodeEntry"); var rightEntry = (TextBox)right.FindName("BarcodeEntry");
            Check(vm.Left.DeviceLabel.Contains("离线") && vm.Left.DisplayPresentation.Tone == Ipc.Application.ParcelFlow.Desktop.ParcelPresentationTone.Neutral && !leftEntry.IsReadOnly && vm.Left.CanSubmitBarcode,
                "offline camera is footer information with neutral work area and usable manual entry");
            foreach (var card in positions)
                Check(((Button)card.FindName("EnableSupply")).ActualHeight >= 44 && ((Button)card.FindName("DisableSupply")).ActualHeight >= 44, "each segmented action retains 44 DIP touch target");
            CaptureInternal(evidence, "positions-enabled-offline", (Grid)window.FindName("ClientArea"), lines);
            leftEntry.Focus(); ((Button)left.FindName("DisableSupply")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => vm.Left.SupplyDisabled && !vm.Busy);
            Check(leftEntry.IsReadOnly && !leftEntry.IsTabStop && !rightEntry.IsReadOnly && ((Border)left.FindName("CenteredSupplyPanel")).IsVisible && vm.Left.CenteredStateTitle == "已停用",
                "idle disable shows grey disabled panel and excludes barcode input");
            Check(vm.OperatorStatus.Contains("仅右投递位") && vm.InputTargetPosition != "left", "single enabled position is named and disabled card loses scanner target");
            ((Button)right.FindName("DisableSupply")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Until(() => vm.Right.SupplyDisabled && !vm.Busy);
            Check(vm.OperatorStatus == "暂停供包 · 左右投递位均已停用" && vm.WorkspaceStatusForeground == OperatorTokens.Warning && vm.InputTargetPosition.Length == 0, "both disabled gives amber top status and clears barcode target");
            CaptureInternal(evidence, "positions-both-disabled", (Grid)window.FindName("ClientArea"), lines);
            ((Button)left.FindName("EnableSupply")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Until(() => vm.Left.SupplyEnabled && !vm.Busy);
            Check(vm.InputTargetPosition == "left", "explicit enable selects this position for the next manual barcode");
            leftEntry.Text = "SYNTH-MANUAL-UI"; ((Button)left.FindName("SubmitBarcode")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => vm.Left.ShowConfirmedTask && !vm.Busy);
            ((Button)left.FindName("DisableSupply")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Until(() => vm.Left.SupplyDisabling && !vm.Busy);
            Check(vm.Left.ShowConfirmedTask && vm.Left.Slot == "SYNTH-CHUTE" && leftEntry.IsReadOnly && ((TextBlock)left.FindName("DisablingNotice")).IsVisible,
                "draining stops new input while preserving confirmed current result and cancellation instruction");
            foreach (var (w, height, name) in new[] { (1366d, 768d, "standard"), (1093d, 614d, "125pct"), (911d, 512d, "150pct") })
            {
                await SetClient(window, w, height); await Flush();
                Check(Inside((Button)left.FindName("DisableSupply"), (Grid)window.FindName("ClientArea")) && Inside((Button)left.FindName("SubmitBarcode"), (Grid)window.FindName("ClientArea")), "enable controls and submission stay reachable at " + name);
                var leftResult = (Border)left.FindName("ResultPanel"); var rightResult = (Border)right.FindName("ResultPanel");
                Check(Math.Abs(leftResult.TranslatePoint(new Point(), window).Y - rightResult.TranslatePoint(new Point(), window).Y) < 2 && Math.Abs(leftResult.ActualHeight - rightResult.ActualHeight) < 2,
                    "different supply states keep result regions aligned at " + name);
                Check(((TextBlock)window.FindName("WorkStatus")).IsVisible, "supply status remains visible in compact layout at " + name);
                CaptureInternal(evidence, "positions-draining-" + name, (Grid)window.FindName("ClientArea"), lines);
            }
            await vm.LoadSupplyHistoryAsync(); Check(vm.SupplyHistory.Length == 4 && vm.SupplyHistory[0].Task == "SYNTH-TASK", "records show operator switches and current task");
            ((Button)left.FindName("EnableSupply")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Until(() => vm.Left.SupplyEnabled && !vm.Busy);
            Check(!leftEntry.IsReadOnly && h.Peer.Requests.Count == 1, "cancelling disable restores input without replaying parcel request");
            typeof(SupplyStationWindow).GetMethod("SetPage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(window, ["history"]);
            await Flush();
            var controls = Descendants(window).OfType<Expander>().Single(e => Equals(e.Header, "投递位启停记录")); controls.IsExpanded = true;
            await Until(() => !vm.Busy && vm.SupplyHistory.Length == 5);
            Check(Descendants(controls).OfType<DataGrid>().Single().Items.Count == 5, "history page renders durable position switch records");
            CaptureInternal(evidence, "position-control-history", (Grid)window.FindName("ClientArea"), lines);
        }
        finally { window.Close(); }
        await using var deferred = new ManualSupplyHarness(root, Path.Combine(evidence, "unconfigured-disabled"), true);
        await deferred.StartAsync();
        var unconfigured = new SupplyStationWindow(deferred.App, null, "standard"); unconfigured.Show();
        try
        {
            var snapshot = await deferred.App.SnapshotAsync();
            var state = snapshot.Positions!.Single(p => p.Id == "right").SupplyControl!;
            await deferred.App.SetPositionEnabledAsync(new(Wire.Id(), snapshot.RuntimeId, "right", state.Revision, false), default);
            for (var n = 0; n < 100 && !unconfigured.ViewModel.Right.SupplyDisabled; n++) { await unconfigured.ViewModel.RefreshAsync(); await Task.Delay(25); }
            Check(unconfigured.ViewModel.Right.SupplyDisabled && unconfigured.ViewModel.Right.DeviceLabel.Contains("待配置") && !unconfigured.ViewModel.Right.DeviceLabel.Contains("断开"),
                "an unconfigured disabled camera stays awaiting configuration without a false disconnect or release message");
            await SetClient(unconfigured, 1366, 768); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            CaptureInternal(evidence, "unconfigured-position-disabled", (Grid)unconfigured.FindName("ClientArea"), lines);
        }
        finally { unconfigured.Close(); }

    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Platform;

static partial class Program
{
    static async Task VerifyWorkstationBarcodeReady(string root, string evidence, List<string> lines)
    {
        void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); lines.Add("PASS BARCODE-READY " + message); }
        lines.Add("SYNTHETIC Simulation sources/peer, actual Windows WPF; real cameras/PDA/customer RCS NOT_RUN");
        var folder = Path.Combine(evidence, "config"); Directory.CreateDirectory(folder);
        var seed = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(root, "tests/Ipc.Application.Verification/sportmaster.synthetic.json")));
        ExtensionStore.AtomicWrite(Path.Combine(folder, "business.json"), seed);
        var launch = new BusinessLaunch(folder, Path.Combine(evidence, "data"), null, null);
        var factory = new CommercialProductization.SyntheticFactory(launch);
        await using var runtime = new ParcelRuntime(launch, new SportmasterFlow(), factory) { RuntimeIdentityPreflight = () => { } };
        await runtime.StartAsync(default);
        var window = new SupplyStationWindow(runtime, null, "standard") { ConfirmDiscardDrafts = _ => true, Title = "就绪条码输入 · 合成端口验证" };
        window.Show(); var vm = window.ViewModel;
        async Task Flush() { await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout(); }
        async Task Until(Func<bool> done)
        { for (var i = 0; i < 100; i++) { await vm.RefreshAsync(); await Flush(); if (done()) return; await Task.Delay(40); } throw new TimeoutException("Ready barcode UI did not converge"); }
        SupplyStationPosition Position(string id) => Descendants(window).OfType<SupplyStationPosition>().Single(p => ((SupplyStationPositionViewModel)p.DataContext).PositionId == id);
        T Part<T>(SupplyStationPosition p, string name) where T : FrameworkElement => (T)p.FindName(name);
        try
        {
            await SetClient(window, 1366, 768); await Until(() => vm.Fresh && vm.Left.CanAdmit && vm.Right.CanAdmit);
            var left = Position("left"); var right = Position("right");
            foreach (var managed in new[] { false, true })
            {
                if (managed) await ManagementModeFixture.Authorize(runtime);
                else await runtime.ExitManagementModeAsync();
                await vm.RefreshManagementModeAsync(); await Flush();
                foreach (var size in new[] { (1366d, 768d), (800d, 420d) })
                {
                    await SetClient(window, size.Item1, size.Item2); await Flush();
                    var items = Descendants(window).OfType<Menu>().SelectMany(m => m.Items.OfType<MenuItem>()).Single(m => Equals(m.Header, "页面")).Items.OfType<MenuItem>().ToArray();
                    foreach (var label in new[] { "设置", "日志", "调试" })
                    {
                        var button = Descendants(window).OfType<Button>().Single(b => Equals(b.Content, label));
                        var item = items.Single(m => Equals(m.Header, label));
                        Check(button.Visibility == (managed ? Visibility.Visible : Visibility.Collapsed) && item.Visibility == button.Visibility,
                            label + " navigation respects management mode in sidebar and compact menu at " + size.Item1);
                    }
                }
            }
            await SetClient(window, 1366, 768); await Flush();
            Click(window, "调试"); await Flush();
            Check(vm.SelectedPage == "debug" && Descendants(window).OfType<UserControl>().Any(c => c.GetType().Name == "StationDebugControl"), "management debug navigation opens the existing round control directly");
            CaptureInternal(evidence, "management-debug-direct", (Grid)window.FindName("ClientArea"), lines);
            await runtime.ExitManagementModeAsync(); await vm.RefreshManagementModeAsync(); await Flush();
            Check(vm.SelectedPage == "workstation", "revoking management leaves debug and returns to work");
            await SetClient(window, 1366, 768); await Flush();
            foreach (var p in new[] { left, right })
            {
                var state = (SupplyStationPositionViewModel)p.DataContext; var entry = Part<TextBox>(p, "BarcodeEntry");
                Check(state.Status == "等待扫码或输入条码" && state.NextStep.Contains("输入完整条码"), state.PositionId + " ready status explains both acquisition and manual input");
                Check(entry.IsVisible && entry.IsEnabled && !entry.IsReadOnly && entry.Focusable, state.PositionId + " ready input is immediately editable without a failed scan");
                Check(Part<Grid>(p, "ScanInformation").IsVisible && Part<Button>(p, "SubmitBarcode").IsVisible, state.PositionId + " empty result retains the single barcode area and bottom submit");
            }
            var leftEntry = Part<TextBox>(left, "BarcodeEntry"); leftEntry.Focus(); leftEntry.Text = "000123450001"; leftEntry.Select(3, 4);
            for (var i = 0; i < 5; i++) { await vm.RefreshAsync(); await Flush(); }
            Check(leftEntry.Text == "000123450001" && leftEntry.SelectionStart == 3 && leftEntry.SelectionLength == 4 && leftEntry.IsKeyboardFocusWithin && factory.Sends == 0,
                "ready snapshots preserve draft, selection and focus and do not auto-submit");
            CaptureInternal(evidence, "ready-entry-standard", (Grid)window.FindName("ClientArea"), lines);
            Part<Button>(left, "SubmitBarcode").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => vm.Left.Operation?.OperatorInput?.Kind == "Keyboard" && vm.Left.SlotAvailable);
            Check(vm.Left.Operation!.ParcelCode == "000123450001" && leftEntry.Text == "000123450001" && factory.Sends == 1,
                "keyboard submit from ready goes through real Application once and preserves leading zeroes");
            Check(Part<Grid>(left, "ScanInformation").IsVisible && Part<Button>(left, "CurrentBarcode").IsEnabled && leftEntry.IsVisible,
                "accepted result retains picture entry and the next manual input remains visible");
            var sentBefore = factory.Sends;
            Part<Button>(left, "SubmitBarcode").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Flush();
            Check(factory.Sends == sentBefore && Part<TextBlock>(left, "InputFeedback").Text.Contains("已读取条码"), "submitting an unedited camera/result display never creates another request");
            var rightEntry = Part<TextBox>(right, "BarcodeEntry"); ((MenuItem)right.FindName("PdaSource")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            rightEntry.Focus(); rightEntry.Text = "000123450002";
            void Enter() => rightEntry.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(rightEntry), Environment.TickCount, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            Enter(); Enter();
            await Until(() => vm.Right.Operation?.OperatorInput?.Kind == "PdaKeyboard" && vm.Right.SlotAvailable);
            Check(vm.Right.Operation!.ParcelCode == "000123450002" && factory.Sends == 2, "PDA Enter from ready uses its own position/source and repeat Enter does not duplicate the request");
            leftEntry.Focus(); leftEntry.Text = "000123450003";
            runtime.EmitReferenceAt("left", "synthetic-camera-next", "000123450004", "synthetic-camera-next");
            await Until(() => vm.Left.Operation?.ParcelCode == "000123450004");
            Check(Part<TextBlock>(left, "InputFeedback").Text.Contains("000123450004"), "draft notice follows the actual task identity when a new camera result arrives");
            var sends = factory.Sends; Part<Button>(left, "SubmitBarcode").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => Part<TextBlock>(left, "InputFeedback").Text.StartsWith("需重新核对"));
            Check(factory.Sends == sends && leftEntry.Text == "000123450003" && vm.Left.Operation!.ParcelCode == "000123450004",
                "camera update during a draft preserves it and rejects stale manual basis without another request");
            foreach (var size in new[] { (1024d, 768d), (800d, 420d) })
            {
                await SetClient(window, size.Item1, size.Item2); await Flush();
                foreach (var p in new[] { left, right })
                {
                    var scroll = Part<ScrollViewer>(p, "PositionBody"); scroll.ScrollToTop(); await Flush();
                    var entry = Part<TextBox>(p, "BarcodeEntry");
                    var slot = Part<TextBox>(p, "CurrentSlot");
                    Check(slot.IsVisible && slot.TranslatePoint(new Point(), scroll).Y + slot.ActualHeight <= scroll.ActualHeight + 1,
                        "current destination stays in the body viewport without scrolling at " + size.Item1 + " slot=" + slot.TranslatePoint(new Point(), scroll).Y + "/" + slot.ActualHeight + " body=" + scroll.ActualHeight);
                    Check(entry.IsVisible && entry.ActualHeight >= 44 && scroll.ScrollableWidth == 0,
                        "ready/manual input remains on the vertical route without horizontal overflow at " + size.Item1);
                }
                CaptureInternal(evidence, "ready-entry-" + (int)size.Item1, (Grid)window.FindName("ClientArea"), lines);
            }
        }
        finally { window.Close(); }
        // Explicit presentation-only fixtures: no command is submitted through projected health.
        var projection = System.Reflection.DispatchProxy.Create<IHistoryProjectionFixture, HierarchyProjectionProbe>();
        var probe = (HierarchyProjectionProbe)(object)projection; probe.Inner = runtime;
        var offline = new SupplyStationWindow(projection, null, "standard") { ConfirmDiscardDrafts = _ => true };
        offline.Show();
        try
        {
            foreach (var missing in new[] { false, true })
            {
                probe.Project = s => s with {
                    Configuration = s.Configuration with { Effective = s.Configuration.Effective with {
                        Mode = RunMode.Production, DeferredConnections = missing ? [.. s.Positions!.Select(p => p.ReaderDeviceId), s.Configuration.Effective.Wcs!.InstanceId] : null } },
                    Devices = s.Devices!.Select(d => d with { Health = DeviceHealth.Disconnected }).ToArray(),
                    Operations = missing ? [] : s.Operations, Inputs = missing ? [] : s.Inputs,
                    Positions = s.Positions!.Select(p => p with { Admit = false, CurrentOperation = missing ? null : p.CurrentOperation, BlockingReasons = [missing ? "连接待配置" : "相机离线"] }).ToArray() };
                foreach (var size in new[] { (1366d, 768d), (800d, 420d) })
                {
                    await SetClient(offline, size.Item1, size.Item2); await offline.ViewModel.RefreshAsync(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); offline.UpdateLayout();
                    var client = (Grid)offline.FindName("ClientArea");
                    foreach (var p in Descendants(offline).OfType<SupplyStationPosition>())
                    {
                        var entry = Part<TextBox>(p, "BarcodeEntry"); entry.Text = "0000009"; entry.Focus(); entry.Select(1, 2);
                        await offline.ViewModel.RefreshAsync(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); offline.UpdateLayout();
                        Check(p.IsVisible && entry.IsVisible && entry.IsEnabled && !entry.IsReadOnly && entry.Text == "0000009" && entry.SelectionLength == 2,
                            "offline/deferred keeps editable barcode and draft at " + size.Item1 + " deferred=" + missing);
                        Check(Descendants(p).OfType<TextBlock>().Any(t => t.IsVisible && t.Text == "任务信息") && Inside(Part<Button>(p, "SubmitBarcode"), client),
                            "offline/deferred task heading and bottom submit remain reachable at " + size.Item1 + " deferred=" + missing);
                        Check(((SupplyStationPositionViewModel)p.DataContext).DevicePresentation.Text.Contains(missing ? "待配置" : "离线"), "projected device status remains explicit without claiming readiness");
                    }
                    CaptureInternal(evidence, (missing ? "deferred" : "offline") + "-single-entry-" + (int)size.Item1, client, lines);
                }
            }
        }
        finally { offline.Close(); }
    }
}

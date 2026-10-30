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
            foreach (var p in new[] { left, right })
            {
                var state = (SupplyStationPositionViewModel)p.DataContext; var entry = Part<TextBox>(p, "BarcodeEntry");
                Check(state.Status == "等待扫码或输入条码" && state.NextStep.Contains("输入完整条码"), state.PositionId + " ready status explains both acquisition and manual input");
                Check(entry.IsVisible && entry.IsEnabled && !entry.IsReadOnly && entry.Focusable, state.PositionId + " ready input is immediately editable without a failed scan");
                Check(!Part<Grid>(p, "ScanInformation").IsVisible && Part<Button>(p, "SubmitBarcode").IsVisible, state.PositionId + " empty result does not push down ready input; submit remains reachable");
            }
            var leftEntry = Part<TextBox>(left, "BarcodeEntry"); leftEntry.Focus(); leftEntry.Text = "000123450001"; leftEntry.Select(3, 4);
            for (var i = 0; i < 5; i++) { await vm.RefreshAsync(); await Flush(); }
            Check(leftEntry.Text == "000123450001" && leftEntry.SelectionStart == 3 && leftEntry.SelectionLength == 4 && leftEntry.IsKeyboardFocusWithin && factory.Sends == 0,
                "ready snapshots preserve draft, selection and focus and do not auto-submit");
            CaptureInternal(evidence, "ready-entry-standard", (Grid)window.FindName("ClientArea"), lines);
            Part<Button>(left, "SubmitBarcode").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => vm.Left.Operation?.OperatorInput?.Kind == "Keyboard" && vm.Left.SlotAvailable);
            Check(vm.Left.Operation!.ParcelCode == "000123450001" && leftEntry.Text.Length == 0 && factory.Sends == 1,
                "keyboard submit from ready goes through real Application once and preserves leading zeroes");
            Check(Part<Grid>(left, "ScanInformation").IsVisible && Part<Button>(left, "CurrentBarcode").IsEnabled && leftEntry.IsVisible,
                "accepted result retains picture entry and the next manual input remains visible");
            var rightEntry = Part<TextBox>(right, "BarcodeEntry"); Part<ComboBox>(right, "InputKind").SelectedIndex = 1;
            rightEntry.Focus(); rightEntry.Text = "000123450002";
            void Enter() => rightEntry.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(rightEntry), Environment.TickCount, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            Enter(); Enter();
            await Until(() => vm.Right.Operation?.OperatorInput?.Kind == "PdaKeyboard" && vm.Right.SlotAvailable);
            Check(vm.Right.Operation!.ParcelCode == "000123450002" && factory.Sends == 2, "PDA Enter from ready uses its own position/source and repeat Enter does not duplicate the request");
            leftEntry.Focus(); leftEntry.Text = "000123450003";
            runtime.EmitReferenceAt("left", "synthetic-camera-next", "000123450004", "synthetic-camera-next");
            await Until(() => vm.Left.Operation?.ParcelCode == "000123450004");
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
                    Check(entry.IsVisible && entry.ActualHeight >= 44 && scroll.ScrollableWidth == 0,
                        "ready/manual input remains on the vertical route without horizontal overflow at " + size.Item1);
                }
                CaptureInternal(evidence, "ready-entry-" + (int)size.Item1, (Grid)window.FindName("ClientArea"), lines);
            }
        }
        finally { window.Close(); }
    }
}

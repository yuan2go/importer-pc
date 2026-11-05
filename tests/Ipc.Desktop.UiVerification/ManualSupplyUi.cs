using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.Reflection;
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
                Check(!vm.IsManagementMode && (deferred ? vm.OperatorStatus == "可以供包" : vm.OperatorStatus.StartsWith("可以供包") && vm.OperatorStatus.Contains("相机离线，可手动输入")) && !vm.ShowSetupGuidance &&
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
                var entry = (TextBox)right.FindName("BarcodeEntry"); entry.Text = "000123457";
                entry.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, 0, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                await Until(() => vm.Right.SlotAvailable && !vm.Busy);
                Check(vm.Right.Barcode == "000123457" && vm.Right.Operation?.OperatorInput?.Kind == "Keyboard" && h.Peer.Requests.Count == 2 &&
                    vm.Right.Operation!.Acquisition is null, "PDA Enter independently supplies the other position without any camera acquisition");
                CaptureInternal(evidence, deferred ? "manual-deferred-native" : "manual-offline-native", (Grid)window.FindName("ClientArea"), lines);
                await SetClient(window, 800, 420); await Flush();
                var submit = (Button)right.FindName("SubmitBarcode"); submit.BringIntoView(); await Flush();
                Check(Inside(submit, (Grid)window.FindName("ClientArea")) && !entry.IsReadOnly, "compact layout retains editable barcode and reachable bottom submit");
            }
            finally { window.Close(); }
        }
        foreach (var argumentError in new[] { false, true })
            await VerifyManualSubmissionFailure(root, evidence, lines, argumentError);
    }

    static async Task VerifyManualSubmissionFailure(string root, string evidence, List<string> lines, bool argumentError)
    {
        void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); lines.Add("PASS MANUAL-FAILURE " + message); }
        var folder = Path.Combine(evidence, "manual-lost-return-" + argumentError);
        await using var h = new ManualSupplyHarness(root, folder, deferred: true);
        await h.StartAsync();
        var application = DispatchProxy.Create<IManualSupplyUiFixture, ManualSubmissionFailureProbe>();
        var probe = (ManualSubmissionFailureProbe)(object)application; probe.Inner = h.App;
        probe.Failure = argumentError ? new ArgumentException("Synthetic SDK InvalidParameter 0x80000214") : new IOException("Synthetic SDK connection lost 0x80000214");
        var window = new SupplyStationWindow(application, null, "standard") { ConfirmDiscardDrafts = _ => true };
        window.Show(); var vm = window.ViewModel;
        async Task Until(Func<bool> ready)
        {
            for (var i = 0; i < 180; i++)
            {
                await vm.RefreshAsync(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
                if (ready()) return; await Task.Delay(25);
            }
            throw new TimeoutException("Manual failure UI did not converge");
        }
        try
        {
            await SetClient(window, 1366, 768); await Until(() => vm.Fresh && vm.Left.CanAdmit);
            var left = Descendants(window).OfType<SupplyStationPosition>().Single(p => ReferenceEquals(p.DataContext, vm.Left));
            var entry = (TextBox)left.FindName("BarcodeEntry"); var submit = (Button)left.FindName("SubmitBarcode");
            var feedback = (TextBlock)left.FindName("InputFeedback");
            entry.Text = "000123456"; submit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => !vm.Busy && Equals(submit.Content, "查看提交结果"));
            Check(entry.IsReadOnly && feedback.Text.Contains("保留包裹") && feedback.Text.Contains("未确认") &&
                !feedback.Text.Contains("SDK") && !feedback.Text.Contains(probe.Commands[0].CommandId) &&
                !vm.WorkFeedback.Contains("SDK"), "lost return is actionable Chinese in both position and shared feedback; argument=" + argumentError);
            Check(h.Peer.Requests.Count == 1, "failure after admission retains the original external effect");
            CaptureInternal(evidence, "manual-failure-" + argumentError, (Grid)window.FindName("ClientArea"), lines);
            await SetClient(window, 800, 600); feedback.BringIntoView();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
            var fullText = new System.Windows.Media.FormattedText(feedback.Text, System.Globalization.CultureInfo.CurrentUICulture,
                feedback.FlowDirection, new System.Windows.Media.Typeface(feedback.FontFamily, feedback.FontStyle, feedback.FontWeight, feedback.FontStretch),
                feedback.FontSize, feedback.Foreground, System.Windows.Media.VisualTreeHelper.GetDpi(feedback).PixelsPerDip)
                { MaxTextWidth = feedback.ActualWidth };
            Check(fullText.Height <= feedback.ActualHeight + 1 && Inside(submit, (Grid)window.FindName("ClientArea")),
                "compact failure copy is fully visible and inspection action remains reachable");
            Check(((Border)left.FindName("ResultPanel")).ActualHeight >= 72 &&
                ((TextBox)left.FindName("CurrentSlot")).ActualHeight > 0,
                "compact failure feedback leaves visible space for the current chute result");
            CaptureInternal(evidence, "manual-failure-compact-" + argumentError, (Grid)window.FindName("ClientArea"), lines);
            submit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => !vm.Busy && Equals(submit.Content, "提交"));
            Check(probe.Commands.Count == 2 && probe.Commands[0] == probe.Commands[1] && h.Peer.Requests.Count == 1,
                "inspection repeats the exact original command and creates no second RCS request");

            var invalid = await h.Command("left", "invalid\nbarcode");
            try { await h.App.SubmitParcelBarcodeAsync(invalid, default); throw new InvalidOperationException("Expected validation exception"); }
            catch (ArgumentException) { }
            string journal = "";
            await Until(() =>
            {
                journal = string.Join("\n", Directory.GetFiles(folder, "runtime.jsonl", SearchOption.AllDirectories).Select(File.ReadAllText));
                return journal.Contains(invalid.CommandId);
            });
            Check(journal.Contains("ArgumentException") && journal.Contains("PositionId") && journal.Contains("left"),
                "Application retains command, position and original exception evidence in diagnostic log");
        }
        finally { window.Close(); }
    }
}

public interface IManualSupplyUiFixture : IHistoryProjectionFixture, IParcelWorkstation { }
public class ManualSubmissionFailureProbe : ProjectionProbe
{
    public Exception? Failure;
    public List<SubmitParcelBarcode> Commands { get; } = [];
    protected override object? Invoke(MethodInfo? method, object?[]? arguments)
    {
        if (method!.Name == nameof(IParcelWorkstation.SubmitParcelBarcodeAsync))
            return Submit((SubmitParcelBarcode)arguments![0]!, (CancellationToken)arguments[1]!);
        return base.Invoke(method, arguments);
    }
    async Task<ParcelBarcodeReceipt> Submit(SubmitParcelBarcode command, CancellationToken token)
    {
        Commands.Add(command);
        var result = await ((IParcelWorkstation)Inner).SubmitParcelBarcodeAsync(command, token);
        if (Failure is { } error) { Failure = null; throw error; }
        return result;
    }
}

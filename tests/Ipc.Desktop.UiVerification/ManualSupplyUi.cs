using System.Text.Json;
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
                Check(!vm.IsManagementMode && vm.OperatorStatus.StartsWith("可以供包") && !vm.ShowSetupGuidance &&
                    vm.Left.DeviceLabel.Contains(deferred ? "待配置" : "离线") && vm.Left.BlockingReasons.Length == 0,
                    "ordinary mode permits manual supply while preserving camera facts; deferred=" + deferred);
                var positions = Descendants(window).OfType<SupplyStationPosition>().ToArray();
                var left = positions.Single(p => ReferenceEquals(p.DataContext, vm.Left));
                var right = positions.Single(p => ReferenceEquals(p.DataContext, vm.Right));
                if (!deferred)
                {
                    h.Factory.Cameras[vm.Left.ReaderId].Emit();
                    await Until(() => vm.Left.Input?.Outcome == ParcelInputOutcome.Blocked);
                    Check(vm.Left.ShowCenteredState && vm.Left.CenteredStateTitle == "相机连接未确认" &&
                        vm.Left.CenteredStateReason == "本次扫码未发送请求" &&
                        ParcelInputReasons.BlockReason(vm.Left.Input!) == ParcelInputBlockReason.ReaderConfirmationExpired &&
                        vm.Left.Input!.OperationId is null && vm.Right.CanAdmit && h.Peer.Requests.Count == 0,
                        "an invalid camera session shows the exact Chinese cause and sends no request; other position remains usable");
                }
                ((TextBox)left.FindName("BarcodeEntry")).Text = "000123456";
                ((Button)left.FindName("SubmitBarcode")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Until(() => vm.Left.SlotAvailable && !vm.Busy);
                Check(vm.Left.Barcode == "000123456" && vm.Left.Slot == "SYNTH-CHUTE" && vm.Left.ScanSummary.StartsWith("手动键盘输入") && h.Peer.Requests.Count == 1,
                    "bottom submit sends the complete keyboard barcode through Application and real built-in RCS");
                var source = (ComboBox)right.FindName("BarcodeSource");
                Check(source.IsVisible && source.SelectedIndex == 0, "each position defaults to manual input");
                source.SelectedIndex = 1;
                var entry = (TextBox)right.FindName("BarcodeEntry"); entry.Text = "000123457";
                entry.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, 0, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                await Until(() => vm.Right.SlotAvailable && !vm.Busy);
                Check(vm.Right.Barcode == "000123457" && vm.Right.Operation?.OperatorInput?.Kind == "PdaKeyboard" && h.Peer.Requests.Count == 2 &&
                    vm.Right.Operation!.Acquisition is null, "PDA Enter independently supplies the other position without any camera acquisition");
                var bodies = h.Peer.Requests.ToArray();
                using var manual = JsonDocument.Parse(bodies[0]);
                using var pda = JsonDocument.Parse(bodies[1]);
                Check(manual.RootElement.GetProperty("source").GetString() == "MANUAL" && pda.RootElement.GetProperty("source").GetString() == "PDA",
                    "ordinary keyboard and explicit PDA selection reach the RCS source field");
                CaptureInternal(evidence, deferred ? "manual-deferred-native" : "manual-offline-native", (Grid)window.FindName("ClientArea"), lines);
                await SetClient(window, 800, 420); await Flush();
                var submit = (Button)right.FindName("SubmitBarcode"); submit.BringIntoView(); await Flush();
                Check(Inside(submit, (Grid)window.FindName("ClientArea")) && !entry.IsReadOnly, "compact layout retains editable barcode and reachable bottom submit");
                if (!deferred)
                {
                    var leftEntry = (TextBox)left.FindName("BarcodeEntry");
                    var leftSubmit = (Button)left.FindName("SubmitBarcode");
                    h.Peer.ReplySlot = "";
                    leftEntry.Text = "000123458";
                    leftSubmit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await Until(() => vm.Left.HasUncertainOriginal && !vm.Busy);
                    var original = vm.Left.Operation!.Id;
                    var removal = (Button)left.FindName("ConfirmPlacementRemoval");
                    Check(removal.IsEnabled && Inside(removal, (Grid)window.FindName("ClientArea")) &&
                        vm.Left.CenteredStateInstruction == "请先取下包裹，再确认", "compact offline-camera card exposes the operator removal action without management");
                    removal.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await Until(() => vm.Left.Operation is null && vm.Left.CanSubmitBarcode && !vm.Busy);
                    Check(!removal.IsVisible && !vm.Left.HasCurrentInput && leftEntry.Text == "" && vm.Right.SlotAvailable && h.Peer.Requests.Count == 3,
                        "the removal action clears only the affected card and sends no request");
                    h.Peer.ReplySlot = "SYNTH-NEXT";
                    leftEntry.Text = "000123458";
                    leftSubmit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await Until(() => vm.Left.Slot == "SYNTH-NEXT" && !vm.Busy);
                    Check(vm.Left.Operation?.Id != original && h.Peer.Requests.Count == 4, "explicitly re-entering the same code sends a fresh manual request");
                    h.Peer.AssignedData = new { chuteCode = "SYNTH-CHUTE-ACCEPTED", height = "-", length = "-", quantity = 1, skuId = "-", taskId = "SYNTH-TASK", waveNo = "-", weight = "-", width = "-" };
                    entry.Text = "000123459";
                    submit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await Until(() => vm.Right.ShowConfirmedTask && vm.Right.Slot == "SYNTH-CHUTE-ACCEPTED" && !vm.Busy);
                    Check(((TextBox)right.FindName("CurrentSlot")).Text == "SYNTH-CHUTE-ACCEPTED" && ((TextBox)right.FindName("QuantityValue")).Text == "1" &&
                        !vm.Right.HasUncertainOriginal && vm.Left.Slot == "SYNTH-NEXT" && h.Peer.Requests.Count == 5,
                        "numeric quantity and dash measurements show the confirmed chute on the receiving position without changing the other side");
                }
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

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
                    await CameraRoundHarness.Until(() => Task.FromResult(h.App.WorkerProgress().Single(p => p.Position == "left").Processed > 0));
                    await vm.RefreshAsync();
                    Check(vm.Left.CurrentRound is null && vm.Left.Barcode.Length == 0 && vm.Left.CanSubmitBarcode &&
                        vm.Right.CanAdmit && h.Peer.Requests.Count == 0,
                        "an unhealthy camera cannot invent a current round or prevent independent manual supply");
                }
                ((TextBox)left.FindName("BarcodeEntry")).Text = "000123456";
                ((Button)left.FindName("SubmitBarcode")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Until(() => vm.Left.SlotAvailable && !vm.Busy);
                Check(vm.Left.Barcode == "000123456" && vm.Left.Slot == "SYNTH-CHUTE" && vm.Left.ScanSummary.StartsWith("手动键盘输入") && h.Peer.Requests.Count == 1,
                    "bottom submit sends the complete keyboard barcode through Application and real built-in RCS");
                var leftOriginal = vm.Left.Operation!.Id;
                Check(!vm.Left.CanConfirmPlacementRemoval && !((Button)left.FindName("ConfirmManualPlacementRemoval")).IsVisible,
                    "a confirmed chute does not require a take-off confirmation in normal operation");
                Check(left.FindName("BarcodeSource") is null && right.FindName("BarcodeSource") is null,
                    "both positions accept input through the same barcode editor with no source picker");
                var entry = (TextBox)right.FindName("BarcodeEntry");
                entry.Focus(); await Flush();
                // Feed the actual WPF key and text paths with controlled key timestamps.
                // Text composition can be delivered later than the native key.
                var keyboard = Keyboard.PrimaryDevice;
                var inputSource = PresentationSource.FromVisual(window)!;
                var timestamp = Environment.TickCount;
                const string rightCode = "000123457";
                for (var i = 0; i < rightCode.Length; i++)
                {
                    var character = rightCode[i];
                    entry.RaiseEvent(new KeyEventArgs(keyboard, inputSource, timestamp, KeyInterop.KeyFromVirtualKey(character))
                    { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                    entry.RaiseEvent(new TextCompositionEventArgs(keyboard, new TextComposition(InputManager.Current, entry, character.ToString()))
                    { RoutedEvent = TextCompositionManager.PreviewTextInputEvent });
                    var caret = entry.SelectionStart + 1;
                    entry.SelectedText = character.ToString(); entry.Select(caret, 0);
                    timestamp += deferred ? i is 0 or 3 ? 110 : 10 : 65;
                }
                entry.RaiseEvent(new KeyEventArgs(keyboard, inputSource, timestamp, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                await Until(() => vm.Right.SlotAvailable && !vm.Busy);
                var rightOriginal = vm.Right.Operation!.Id;
                var expectedKind = "Keyboard";
                Check(vm.Right.Barcode == "000123457" && vm.Right.Operation?.OperatorInput?.Kind == expectedKind && h.Peer.Requests.Count == 2 &&
                    vm.Right.Operation!.Acquisition is null, "synthetic WPF events without native message evidence remain MANUAL; real PDA cadence is covered by continuous-pda-input");
                var bodies = h.Peer.Requests.ToArray();
                using var manual = JsonDocument.Parse(bodies[0]);
                using var pda = JsonDocument.Parse(bodies[1]);
                Check(manual.RootElement.GetProperty("source").GetString() == "MANUAL" && pda.RootElement.GetProperty("source").GetString() == "MANUAL",
                    "the editor preserves the detected source in the actual RCS payload");
                CaptureInternal(evidence, deferred ? "manual-deferred-native" : "manual-offline-native", (Grid)window.FindName("ClientArea"), lines);
                await SetClient(window, 800, 420); await Flush();
                var submit = (Button)right.FindName("SubmitBarcode"); submit.BringIntoView(); await Flush();
                Check(Inside(submit, (Grid)window.FindName("ClientArea")) && !entry.IsReadOnly, "compact layout retains editable barcode and reachable bottom submit");
                var leftEntry = (TextBox)left.FindName("BarcodeEntry");
                var leftSubmit = (Button)left.FindName("SubmitBarcode");
                // Observe the native presentation subscription with no forced refresh or input.
                await CameraRoundHarness.Until(async () =>
                {
                    await Flush();
                    return vm.Left.ShowCenteredState && vm.Left.CenteredStateTitle == "等待投包" &&
                        vm.Right.ShowCenteredState && vm.Right.CenteredStateTitle == "等待投包";
                });
                Check(leftEntry.Text == "" && entry.Text == "" && !vm.Left.HasCurrentInput && !vm.Left.ShowConfirmedTask && vm.Left.CanSubmitBarcode &&
                    vm.Snapshot!.Positions!.All(p => p.PlacementClearedAt is null) &&
                    (await h.App.OperationAsync(leftOriginal))?.State == ParcelState.SlotResolved &&
                    (await h.App.OperationAsync(rightOriginal))?.State == ParcelState.SlotResolved && h.Peer.Requests.Count == 2,
                    "keyboard and PDA results automatically clear from both work cards without a removal or another RCS request");
                CaptureInternal(evidence, deferred ? "manual-deferred-waiting" : "manual-offline-waiting", (Grid)window.FindName("ClientArea"), lines);
                if (!deferred)
                {
                    h.Peer.ReplySlot = "";
                    leftEntry.Text = "000123458";
                    leftSubmit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await Until(() => vm.Left.HasUncertainOriginal && !vm.Busy);
                    var original = vm.Left.Operation!.Id;
                    var removal = (Button)left.FindName("ConfirmManualPlacementRemoval");
                    Check(removal.IsEnabled && Inside(removal, (Grid)window.FindName("ClientArea")) &&
                        vm.Left.CenteredStateInstruction == "请先取下包裹，再确认", "compact offline-camera card exposes the operator removal action without management");
                    removal.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await Until(() => vm.Left.Operation is null && vm.Left.CanSubmitBarcode && !vm.Busy);
                    Check(!removal.IsVisible && !vm.Left.HasCurrentInput && leftEntry.Text == "" && vm.Right.CenteredStateTitle == "等待投包" && h.Peer.Requests.Count == 3,
                        "the removal action clears only the affected card and sends no request");
                    h.Peer.ReplySlot = "SYNTH-NEXT";
                    leftEntry.Text = "000123458";
                    leftSubmit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await Until(() => vm.Left.Slot == "SYNTH-NEXT" && !vm.Busy);
                    Check(vm.Left.Operation?.Id != original && h.Peer.Requests.Count == 4, "explicitly re-entering the same code sends a fresh manual request");
                    h.Peer.AssignedData = new { chuteCode = "SYNTH-CHUTE-ACCEPTED", height = "2.5", length = "-", quantity = 1, skuId = "-", taskId = "SYNTH-TASK", waveNo = "-", weight = "-", width = "-" };
                    entry.Text = "000123459";
                    submit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await Until(() => vm.Right.ShowConfirmedTask && vm.Right.Slot == "SYNTH-CHUTE-ACCEPTED" && !vm.Busy);
                    Check(((TextBox)right.FindName("CurrentSlot")).Text == "SYNTH-CHUTE-ACCEPTED" && ((TextBox)right.FindName("QuantityValue")).Text == "1" &&
                        !vm.Right.HasUncertainOriginal && vm.Left.Slot == "SYNTH-NEXT" && h.Peer.Requests.Count == 5,
                        "numeric quantity and string measurements show the confirmed chute on the receiving position without changing the other side");
                }
            }
            finally { window.Close(); }
        }
        foreach (var argumentError in new[] { false, true })
            await VerifyManualSubmissionFailure(root, evidence, lines, argumentError);
        await VerifyRoundHistoryReasons(root, evidence, lines);
    }

    static async Task VerifyRoundHistoryReasons(string root, string evidence, List<string> lines)
    {
        void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); lines.Add("PASS ROUND-HISTORY " + message); }
        var clock = new PdaRoundClock();
        await using var h = new ManualSupplyHarness(root, Path.Combine(evidence, "round-history"), deferred: true) { StationClock = clock };
        await h.StartAsync();
        var window = new SupplyStationWindow(h.App, null, "standard") { ConfirmDiscardDrafts = _ => true };
        window.Show();
        try
        {
            // A structurally accepted input can still fail barcode legality. It
            // creates a real unsubmitted round without a device or RCS request.
            await h.App.SubmitParcelBarcodeAsync(await h.Command("left", new string('X', 300)), default);
            var snapshot = await h.App.SnapshotAsync();
            var rejected = snapshot.Rounds.Single(r => r.PositionId == "left");
            var removal = await h.App.CommandAsync(new(Ipc.Platform.Wire.Id(), snapshot.Revision, "ConfirmPlacementRemoval", "left",
                snapshot.Positions!.Single(p => p.Id == "left").PlacementRemovalOperation));
            Check(removal.Status == "Persisted" && rejected.OperationId is null && rejected.ReadOutcome == "NoMatchingCode", "invalid unsubmitted round has a real targeted removal record");
            await SetClient(window, 1366, 768);
            typeof(SupplyStationWindow).GetMethod("SetPage", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, ["history"]);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
            var expander = Descendants(window).OfType<Expander>().Single(e => Equals(e.Header, "包裹轮次（含无码与未提交）"));
            expander.IsExpanded = true;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
            var panel = (DockPanel)expander.Content;
            var table = Descendants(panel).OfType<DataGrid>().Single();
            var reason = Descendants(panel).OfType<ComboBox>().Single(c => System.Windows.Automation.AutomationProperties.GetName(c) == "轮次结果、结束原因或识读情况");
            var load = Descendants(panel).OfType<Button>().Single(b => Equals(b.Content, "查询轮次"));
            async Task Query(string value)
            {
                reason.SelectedValue = value; load.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await CameraRoundHarness.Until(async () => { await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout(); return load.IsEnabled; });
                if (table.ItemsSource is null) throw new InvalidOperationException("Native history query did not populate rows: " + window.ViewModel.Message);
            }
            string Cell(object row, string property) => (string)row.GetType().GetProperty(property)!.GetValue(row)!;
            await Query("OperatorConfirmedRemoval");
            var item = table.Items.Cast<object>().Single();
            Check(Cell(item, "Identity") == rejected.Id && Cell(item, "Read") == "无合法条码" && Cell(item, "End") == "人工确认取下" &&
                table.Columns.OfType<DataGridTextColumn>().Any(c => Equals(c.Header, "识读情况") && ((System.Windows.Data.Binding)c.Binding).Path.Path == "Read"),
                "native history query by removal reason displays the original unsubmitted barcode failure");
            await Query("NoMatchingCode");
            Check(Cell(table.Items.Cast<object>().Single(), "Identity") == rejected.Id, "native read-outcome selector queries the same round without a synthetic operation");
            await h.App.SubmitParcelBarcodeAsync(await h.Command("left", new string('Y', 300)), default);
            clock.Advance();
            await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Rounds.Any(r => r.PositionId == "left" && r.AutoExpired));
            await Query("ReadWindowExpired");
            item = table.Items.Cast<object>().Single();
            Check(Cell(item, "Identity") != rejected.Id && Cell(item, "Read") == "自动识读超时，未提交" && Cell(item, "End") == "本轮尚未结束" && h.Peer.Requests.Count == 0,
                "native history distinguishes automatic timeout from invalid code and does not send an RCS request");
        }
        finally { window.Close(); }
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

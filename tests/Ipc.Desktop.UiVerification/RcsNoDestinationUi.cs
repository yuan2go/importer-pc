using System.Windows;
using System.Windows.Automation;
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
    static IDataObject? CaptureClipboardForRestore()
    {
        var original = Clipboard.GetDataObject();
        if (original is null) return null;
        var formats = original.GetFormats(false);
        if (formats.Length == 0) return null;
        if (!formats.All(format => format is "UnicodeText" or "Text" or "OEMText" or "Locale"))
            return original;

        // Copy plain text while the original OLE source still owns it. Asking a
        // live clipboard proxy for its old data during OleFlushClipboard can
        // re-enter the clipboard while Windows already has it open.
        var snapshot = new DataObject();
        foreach (var format in formats)
            if (original.GetData(format, false) is { } value) snapshot.SetData(format, value, false);
        return snapshot;
    }
    static async Task VerifyRcsNoDestinationUi(string root, string evidence, List<string> lines)
    {
        void Check(bool value, string text) { if (!value) throw new InvalidOperationException(text); lines.Add("PASS NO-DEST-UI " + text); }
        async Task Until(Func<bool> ready)
        {
            for (int i = 0; i < 250; i++) { await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); if (ready()) return; await Task.Delay(25); }
            throw new TimeoutException("RCS no-destination UI did not settle.");
        }
        await using (var h = new ManualSupplyHarness(root, Path.Combine(evidence, "no-dest-settings"), deferred: true))
        {
            ExtensionStore.AtomicWrite(Path.Combine(h.Launch.ConfigurationDirectory, "business.json"), h.Config with { StationCode = "MANUAL-STATION" });
            await h.StartAsync(); await ManagementModeFixture.Authorize(h.App);
            var window = new OperationsViewFixture(h.App) { ConfirmDiscardDrafts = _ => true };
            window.Show(); await window.ViewModel.RefreshAsync(); await SetClient(window, 1366, 768);
            var vm = window.ViewModel;
            window.UseFieldParameters(); var center = window;
            window.Navigate("maintenance"); await Until(() => vm.HasLoadedConfiguration && !vm.Busy);
            var editor = Descendants(center).OfType<ParameterSettingsControl>().Single();
            var rcs = editor.Model!.Components.Single(ParameterOwnership.IsRcs);
            var path = rcs.InstanceId + "/no-dest-to-exp";
            Check(editor.NavigateTo(path), "setting is directly reachable on the station/RCS page"); center.UpdateLayout();
            var choice = Descendants(editor).OfType<ComboBox>().Single(c => AutomationProperties.GetAutomationId(c) == path);
            for (var parent = System.Windows.Media.VisualTreeHelper.GetParent(choice); parent is not null; parent = System.Windows.Media.VisualTreeHelper.GetParent(parent))
                if (parent is Border card) { card.BringIntoView(); break; }
            center.UpdateLayout();
            var labels = choice.Items.Cast<object>().Select(i => (string)i.GetType().GetProperty("Label")!.GetValue(i)!).ToArray();
            Check(labels.SequenceEqual(new[] { "送往异常口（默认）", "留在原位，人工处理" }) && Equals(choice.SelectedValue, "true"), "new/omitted setting selects the default exception-chute behavior with clear Chinese options");
            Check(choice.IsVisible && choice.ActualHeight >= 44 && AutomationProperties.GetHelpText(choice).Contains("小车不动"), "setting remains visible outside folded protocol details and explains the stationary vehicle");
            CaptureInternal(evidence, "no-destination-default", (Grid)center.FindName("ClientArea"), lines);
            choice.SelectedValue = "false"; center.UpdateLayout();
            Check(vm.HasUnsavedConfiguration && vm.BuildStructuredConfiguration().Wcs!.Parameters["no-dest-to-exp"] == "false", "selecting manual handling changes the shared configuration draft");
            var change = vm.GetConfigurationChangeItems().Single(c => c.Label == "无可用格口时");
            Check(change.Before.Contains("送往异常口") && change.After.Contains("人工处理"), "save review describes both behaviors without exposing boolean literals");
            CaptureInternal(evidence, "no-destination-manual", (Grid)center.FindName("ClientArea"), lines);
            Check(await vm.PrepareConfigurationReviewAsync(), "full configuration review accepts the selected policy: " + string.Join("; ", editor.Model.Issues.Select(i => i.Path + " " + i.Message)) + " " + vm.Message);
            vm.ConfigurationReason = "合成验证：无可用格口时改为留在原位，人工处理";
            await vm.SaveCapabilitiesAsync();
            await Until(() => (vm.Snapshot!.Configuration.Pending ?? vm.Snapshot.Configuration.Effective).Wcs!.Parameters.GetValueOrDefault("no-dest-to-exp") == "false");
            Check((vm.Snapshot!.Configuration.Pending ?? vm.Snapshot.Configuration.Effective).Wcs!.Parameters.GetValueOrDefault("no-dest-to-exp") == "false", "CAS save retains false in the configuration book: " + vm.Message);
            await SetClient(center, 800, 420); editor.NavigateTo(path); center.UpdateLayout();
            Check(choice.IsVisible && choice.ActualWidth > 100, "small field-settings window retains the editable selector");
            CaptureInternal(evidence, "no-destination-settings-800", (Grid)center.FindName("ClientArea"), lines);
            center.Close(); await Until(() => !center.IsVisible); window.Close(); await Until(() => !window.IsVisible);
        }
        await using (var h = new ManualSupplyHarness(root, Path.Combine(evidence, "no-dest-result"), deferred: true))
        {
            var configuration = h.Config with { Wcs = h.Config.Wcs! with { Parameters = new(h.Config.Wcs!.Parameters) { ["no-dest-to-exp"] = "false" } } };
            ExtensionStore.AtomicWrite(Path.Combine(h.Launch.ConfigurationDirectory, "business.json"), configuration);
            h.Peer.Mode = "no-destination";
            h.Peer.RejectionMessage = "无设备可用";
            await h.StartAsync();
            var window = new SupplyStationWindow(h.App, null, "standard") { ConfirmDiscardDrafts = _ => true };
            window.Show(); await SetClient(window, 1366, 768);
            var receipt = await h.App.SubmitParcelBarcodeAsync(await h.Command("left", "SYNTH-NO-DEST-UI"), default);
            await Until(() => window.ViewModel.Left.Operation?.Id == receipt.OperationId && window.ViewModel.Left.Operation?.State == ParcelState.WcsRejected);
            var position = window.ViewModel.Left;
            window.UpdateLayout();
            CaptureInternal(evidence, "no-destination-workstation", (Grid)window.FindName("ClientArea"), lines);
            var text = Descendants(window).Select(e => e switch { TextBlock t when t.IsVisible => t.Text, TextBox t when t.IsVisible => t.Text, _ => "" });
            Check(text.Any(t => t == "无设备可用") && position.NextStep.Contains("取下包裹后人工判断") && !position.HasUncertainOriginal && !position.SlotAvailable,
                "workstation displays the original msg and manual removal instruction as a confirmed rejection: " + position.NextStep);
            var vm = window.ViewModel;
            var client = (Grid)window.FindName("ClientArea");
            var cards = Descendants(window).OfType<SupplyStationPosition>().ToArray();
            var left = cards.Single(c => ReferenceEquals(c.DataContext, position));
            var right = cards.Single(c => ReferenceEquals(c.DataContext, vm.Right));
            var message = (TextBox)left.FindName("CenteredRcsMessage");
            var subtitle = (TextBlock)left.FindName("CenteredStateReason");
            var divider = (Border)left.FindName("CenteredRcsDivider");
            var step = (TextBlock)left.FindName("CenteredInstruction");
            var waitingTitle = (TextBlock)right.FindName("CenteredStateTitle");
            var entry = (TextBox)left.FindName("BarcodeEntry");
            Check(message.IsVisible && message.Text == "无设备可用" && message.FontSize == waitingTitle.FontSize &&
                subtitle.Text == "本件暂不能分配格口" && step.FontSize < message.FontSize &&
                step.Text == "请取下包裹后人工判断，再确认\n取下后本件结束，可继续处理下一件" && divider.IsVisible && divider.ActualWidth > 0 &&
                ((Button)left.FindName("ConfirmManualPlacementRemoval")).IsVisible &&
                !((Border)right.FindName("CenteredRcsDivider")).IsVisible &&
                entry.Text == "SYNTH-NO-DEST-UI" && !((DockPanel)left.FindName("DetailedStatus")).IsVisible &&
                position.DisplayPresentation.Tone == ParcelPresentationTone.Rejected && position.NeedsAttention &&
                position.CenteredStateForeground == OperatorTokens.Danger && position.ResultBackground == vm.Right.ResultBackground &&
                position.ResultBackground == OperatorTokens.Neutral && vm.Right.CenteredStateTitle == "相机不可用，可人工扫码" && vm.Right.CanSubmitBarcode,
                "confirmed refusal has one original title with waiting-size typography, a quieter manual next step, and an independent ready position");

            var basis = (await h.App.SnapshotAsync()) with { PublishedTicks = System.Diagnostics.Stopwatch.GetTimestamp() };
            var membership = new SupplyStationViewModel(h.App);
            await membership.AcceptPublishedSnapshotAsync(basis with
            {
                Positions = basis.Positions!.Select(p => p.Id == "left" ? p with
                { Arrival = p.Arrival! with { RecoveryReason = "SYNTHETIC outstanding heartbeat membership" } } : p).ToArray()
            });
            Check(membership.Left.CenteredRcsRejection && membership.Left.CenteredStateInstruction.Contains("请取下包裹") &&
                membership.Left.CenteredStateInstruction.Contains("本位心跳成员仍待核对") && !membership.Left.CenteredStateInstruction.Contains("可继续处理下一件"),
                "membership recovery remains an independent blocker after removing a refused parcel");
            var conflict = new SupplyStationViewModel(h.App);
            await conflict.AcceptPublishedSnapshotAsync(basis with { Operations = basis.Operations.Select(o =>
                o.Id == receipt.OperationId ? o with { Wcs = o.Wcs! with { State = StageState.Conflict } } : o).ToArray() });
            Check(conflict.Left.HasUncertainOriginal && !conflict.Left.CenteredRcsRejection && conflict.Left.NeedsAttention,
                "a conflict retaining the same reply is excluded from the confirmed-rejection presentation");

            h.Peer.Mode = "reject";
            async Task Reject(string code, string original, string title)
            {
                await SmRoundFixture.ConfirmRemovedAsync(h.App, "left");
                h.Peer.RejectionMessage = original;
                var request = await h.App.SubmitParcelBarcodeAsync(await h.Command("left", code), default);
                await Until(() => position.Operation?.Id == request.OperationId && position.CenteredRcsRejection && message.IsVisible && message.Text == title);
            }
            await Reject("SYNTH-DIFFERENT-MESSAGE", "该包裹已被上游拦截", "该包裹已被上游拦截");
            await Reject("SYNTH-EMPTY-MESSAGE", " \t ", "RCS 未分配格口");
            Check(position.Operation?.Wcs?.Replies.Last().Destination?.Message is null, "blank business message uses a Chinese fallback without modifying the parsed result");

            var longMessage = "此包裹需要核对。\r\n" + new string('中', 110) + "\r\n" + new string('A', 180) + "\r\n合成回包结束。";
            await Reject("SYNTH-MULTILINE-MESSAGE", longMessage, longMessage);
            foreach (var (width, height) in new[] { (1366d, 768d), (800d, 420d) })
            {
                await SetClient(window, width, height); window.UpdateLayout();
                FrameworkElement Part(SupplyStationPosition card, string name) => name == "查看详情"
                    ? Descendants(card).OfType<Button>().Single(b => Equals(b.Content, name))
                    : (FrameworkElement)card.FindName(name);
                var aligned = new[] { "ResultPanel", "SubmitBarcode", "查看详情", "PositionDeviceFooter" }.All(name =>
                {
                    var l = Part(left, name); var r = Part(right, name);
                    return Inside(l, client) && Inside(r, client) && Math.Abs(l.TranslatePoint(new(), client).Y - r.TranslatePoint(new(), client).Y) <= 1 &&
                        Math.Abs(l.ActualHeight - r.ActualHeight) <= 1;
                });
                double Top(FrameworkElement part) => part.TranslatePoint(new(), client).Y;
                var firstLine = message.GetRectFromCharacterIndex(0);
                Check(aligned && Inside(step, client) && Top(subtitle) >= Top(message) + message.ActualHeight &&
                    Top(divider) >= Top(subtitle) + subtitle.ActualHeight && Top(step) >= Top(divider) + divider.ActualHeight &&
                    message.TextWrapping == TextWrapping.Wrap && !firstLine.IsEmpty && firstLine.Bottom <= message.ActualHeight + .5,
                    $"multiline content wraps while both lanes and controls stay aligned at {width}x{height}");
                CaptureInternal(evidence, "no-destination-multiline-" + width, client, lines);
            }
            window.Activate(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var picture = (Button)left.FindName("CurrentBarcode");
            picture.Focus(); picture.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            Check(message.IsKeyboardFocused, "the rejection can be selected and copied through native keyboard navigation");
            message.SelectAll(); message.ScrollToEnd(); window.UpdateLayout();
            var offset = message.VerticalOffset;
            for (var i = 0; i < 3; i++) await vm.RefreshAsync();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Check(message.ExtentHeight > message.ViewportHeight && offset > 0 && message.VerticalOffset == offset && message.SelectedText == longMessage &&
                message.IsKeyboardFocused && vm.InputTargetPosition == "left", "refresh preserves the focused selection and scroll offset in the compact message area");
            var priorClipboard = CaptureClipboardForRestore();
            try
            {
                ApplicationCommands.Copy.Execute(null, message);
                Check(Clipboard.GetText() == longMessage, "native Copy puts the complete Chinese, English and multiline response on the clipboard");
            }
            finally
            {
                // Windows may still own the clipboard immediately after Copy.
                // Restore the same original data after yielding; keep a finite
                // cleanup budget and fail if ownership never becomes available.
                for (var attempt = 0; ; attempt++)
                {
                    try { if (priorClipboard is null) Clipboard.Clear(); else Clipboard.SetDataObject(priorClipboard, true); break; }
                    catch (System.Runtime.InteropServices.COMException error) when (error.HResult == unchecked((int)0x800401D0) && attempt < 4)
                    { await Task.Delay(100); }
                }
            }
            var requestCount = h.Peer.Requests.Count;
            var scan = new TextCompositionEventArgs(Keyboard.PrimaryDevice, new TextComposition(InputManager.Current, message, "7"))
            { RoutedEvent = TextCompositionManager.PreviewTextInputEvent };
            message.RaiseEvent(scan);
            Check(scan.Handled && entry.IsKeyboardFocused && entry.Text == "7" && message.Text == longMessage && h.Peer.Requests.Count == requestCount,
                "scanner text leaves the read-only message for the selected lane's barcode input without submitting");
            var snapshot = await h.App.SnapshotAsync();
            await h.App.CommandAsync(new(Wire.Id(), snapshot.Revision, "Pause"));
            await Until(() => vm.Snapshot?.Paused == true);
            Check(message.IsVisible && message.Text == longMessage && step.Text == "请取下包裹后人工判断，再确认\n供包仍暂停，处理完成后再上件" &&
                !position.CanSubmitBarcode && h.Peer.Requests.Count == requestCount, "pause retains the manual refusal and its original request while allowing physical removal: " + step.Text);
            window.Close(); await Until(() => !window.IsVisible);
        }
    }
}

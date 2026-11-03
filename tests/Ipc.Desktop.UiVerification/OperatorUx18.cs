using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Platform;
using Ipc.Presentation.Wpf;

static partial class Program
{
    static async Task VerifyOperatorUx18(string root, string evidence, List<string> lines)
    {
        void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); lines.Add("PASS OPERATOR18 " + text); }
        await using var h = new ManualSupplyHarness(root, Path.Combine(evidence, "operator18"), deferred: true);
        await h.StartAsync(); h.Peer.ReplySlot = "L-07"; h.Peer.ReplyWaveNo = "WAVE-01"; h.Peer.ReplyQuantity = "00012";
        var window = new SupplyStationWindow(h.App, null, "standard"); window.Show(); var vm = window.ViewModel;
        async Task Flush() { await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout(); }
        async Task Until(Func<bool> ready)
        { for (var i = 0; i < 200; i++) { await Flush(); if (ready()) return; await Task.Delay(25); } throw new TimeoutException("Operator18 state"); }
        try
        {
            await SetClient(window, 1366, 768); await Until(() => vm.Fresh && vm.Left.CanSubmitBarcode);
            var left = Descendants(window).OfType<SupplyStationPosition>().Single(c => ReferenceEquals(c.DataContext, vm.Left));
            var right = Descendants(window).OfType<SupplyStationPosition>().Single(c => ReferenceEquals(c.DataContext, vm.Right));
            var entry = (TextBox)left.FindName("BarcodeEntry"); var otherEntry = (TextBox)right.FindName("BarcodeEntry");
            var submit = (Button)left.FindName("SubmitBarcode"); var slot = (TextBox)left.FindName("CurrentSlot");
            Check(left.FindName("VisibleInputSource") is null && left.FindName("KeyboardSource") is null && left.FindName("PdaSource") is null && !submit.IsEnabled, "operator has one barcode entry without source selectors; empty draft cannot submit");
            window.Activate(); entry.Focus(); await Flush();
            Check(vm.InputTargetPosition == "left" && vm.InputTargetLabel.Contains("左位") && vm.Left.InputTargetActive && !vm.Right.InputTargetActive, "real left keyboard focus is the sole highlighted target");
            otherEntry.Focus(); await Flush();
            Check(vm.InputTargetPosition == "right" && vm.InputTargetLabel.Contains("右位") && !vm.Left.InputTargetActive && vm.Right.InputTargetActive, "focus transfer moves target and outline together");
            ((TextBlock)window.FindName("AccountIdentity")).Focus(); await Flush();
            Check(vm.InputTargetPosition == "" && !vm.Left.InputTargetActive && !vm.Right.InputTargetActive, "non-barcode focus advertises no selected input target");
            entry.Focus(); await Flush();
            Check(entry.IsKeyboardFocused && vm.InputTargetLabel.Contains("左相机"), "returning focus identifies the left camera position without asking for input device type");
            entry.Text = "000UX180001"; await Flush(); Check(submit.IsEnabled, "complete new draft has an enabled primary action");
            submit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => vm.Left.ShowConfirmedTask && !vm.Busy);
            Check(vm.Left.Operation?.OperatorInput?.Kind == "Keyboard" && h.Peer.Requests.Count == 1, "unified keyboard character input reaches the guarded command without fabricating a camera acquisition");
            Check(vm.Left.DisplayPresentation.Tone == ParcelPresentationTone.Accepted && vm.Left.ResultBackground == OperatorTokens.Success && vm.Left.ResultForeground == OperatorTokens.Surface && slot.Text == "L-07", "current accepted identity maps to solid green with white full destination");
            Check(((Grid)left.FindName("TaskFields")).IsVisible && vm.Left.WaveNo == "WAVE-01" && vm.Left.TaskId == "SYNTH-TASK" && vm.Left.SkuId == "SYNTH-SKU" && vm.Left.ResponseQuantity == "00012", "site-required task fields stay on the work surface with the confirmed chute; returned values retain their exact strings");
            var cues = typeof(SupplyStationPosition).GetProperty("SuccessCueCount", BindingFlags.Instance | BindingFlags.NonPublic)!;
            await Until(() => (int)cues.GetValue(left)! == 1);
            for (var i = 0; i < 5; i++) await vm.RefreshAsync(); await Flush();
            Check((int)cues.GetValue(left)! == 1, "repeated snapshots of one original result do not repeat its success cue");
            await ManagementModeFixture.Authorize(h.App); await vm.RefreshManagementModeAsync(); await vm.RefreshAsync();
            var frozen = Wire.Encode(vm.Left.Operation!.Wcs!.Request);
            foreach (var size in new[] { (1366d, 768d), (800d, 420d) })
            {
                await SetClient(window, size.Item1, size.Item2); await Flush();
                var panel = (Border)left.FindName("ResultPanel");
                var actual = slot.TransformToAncestor(panel).TransformBounds(new Rect(slot.RenderSize));
                Check(actual.Top >= 0 && actual.Bottom <= panel.ActualHeight + 1 && slot.Text == "L-07", "full current destination stays inside fixed result panel at " + size.Item1);
                var transform = slot.TransformToAncestor(panel);
                var scale = (transform.Transform(new Point(0, 1)) - transform.Transform(new Point())).Length;
                CaptureInternal(evidence, "operator18-geometry-" + (int)size.Item1, (Grid)window.FindName("ClientArea"), lines);
                lines.Add("GEOMETRY tight=" + vm.IsTight + " compact=" + vm.IsCompact + " panel=" + panel.ActualHeight + " slot=" + slot.ActualHeight + " scale=" + scale + " guidance=" + vm.Left.DisplayNextStep + " actions=" + ((StackPanel)left.FindName("FixedActions")).ActualHeight);
                Check(slot.FontSize == (vm.IsCompact ? UiTypography.SlotCompact : UiTypography.Slot), "chute uses the balanced 44/36 DIP scale at " + size.Item1);
                foreach (var name in new[] { "WaveValue", "TaskValue", "SkuValue", "QuantityValue" })
                { var value = (TextBox)left.FindName(name); Check(value.IsVisible && value.FontSize == 22 && value.MaxHeight == (vm.IsTight ? 34 : 64) && value.ActualWidth > 50 && Inside(value, (Grid)window.FindName("ClientArea")), "task field stays visible and readable in its expanded area: " + name + " at " + size.Item1); }
                Check(slot.FontSize * scale >= 32, "actually drawn short chute font is at least 32 DIP at " + size.Item1 + "; measured=" + (slot.FontSize * scale).ToString("F1"));
                Check(submit.ActualHeight >= OperatorTokens.TouchHeight && Inside(submit, (Grid)window.FindName("ClientArea")), "primary touch action remains 64 DIP and on screen at " + size.Item1);
                CaptureInternal(evidence, "operator18-success-" + (int)size.Item1, (Grid)window.FindName("ClientArea"), lines);
            }
            await vm.PauseAsync();
            await Until(() => vm.Snapshot?.Paused == true);
            Check(!vm.Left.ShowConfirmedTask && !vm.Left.DisplaySlotVisible && vm.Left.DisplaySlot == "" && vm.Left.DisplayPresentation.Tone == ParcelPresentationTone.Neutral && vm.Left.DisplayNextStep.Contains("暂勿投递"), "pause immediately removes green chute affordance while preserving original facts");
            entry.Text = "000UX180002"; await Flush();
            var calls = h.Peer.Requests.Count; Check(!submit.IsEnabled, "paused position disables new submit");
            entry.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, 0, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            await Flush(); Check(h.Peer.Requests.Count == calls, "Enter shares the availability guard and cannot bypass disabled submit");
            Check(Wire.Encode(vm.Left.Operation!.Wcs!.Request) == frozen, "pause and draft handling preserve the frozen original request");
            var pausedPanel = (Border)left.FindName("ResultPanel"); var instruction = (TextBlock)left.FindName("NextStep");
            Check(instruction.IsVisible && instruction.TransformToAncestor(pausedPanel).TransformBounds(new Rect(instruction.RenderSize)).Bottom <= pausedPanel.ActualHeight + 1, "pause package instruction is fully visible inside the compact result panel");
            CaptureInternal(evidence, "operator18-paused-800", (Grid)window.FindName("ClientArea"), lines);
            vm.DismissGlobalOperationFeedback(); await vm.CommandAsync("Resume"); await vm.RefreshAsync(); await Until(() => !vm.Snapshot!.Paused && vm.Left.CanSubmitBarcode);
            h.Peer.Mode = "reject";
            entry.Clear(); entry.Text = "000UX180003"; submit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => vm.Left.Operation?.State == ParcelState.WcsRejected && !vm.Busy);
            Check(vm.Left.ResultBackground == OperatorTokens.Danger && !vm.Left.DisplaySlotVisible && vm.Left.DisplayNextStep == OperatorCopy.RejectedStep && vm.Left.DisplayPresentation.Icon == "!", "rejection uses red, a symbol and explicit package handling without retaining old chute");
            await Flush();
            var reasonViewport = (ScrollViewer)left.FindName("PositionBody"); var reason = (TextBox)left.FindName("AttentionReason");
            var firstLine = reason.GetRectFromCharacterIndex(0);
            lines.Add("EXCEPTION_GEOMETRY viewport=" + reasonViewport.ViewportHeight + " firstLine=" + firstLine.Height);
            CaptureInternal(evidence, "operator18-reason-geometry-800", (Grid)window.FindName("ClientArea"), lines);
            Check(reasonViewport.ViewportHeight >= firstLine.Height && firstLine.Height > 0, "compact exception leaves one full rendered reason line beneath its status");
            CaptureInternal(evidence, "operator18-rejected-800", (Grid)window.FindName("ClientArea"), lines);
            await SetClient(window, 800, 420); Click(window, "记录"); await Until(() => vm.SelectedPage == "history" && !vm.HistoryLoading);
            await Flush(); var history = Descendants(window).OfType<HistoryPage>().Single();
            var filters = (Expander)history.FindName("AllHistoryFilters"); var rows = (DataGrid)history.FindName("HistoryRows");
            Check(!filters.IsExpanded && rows.ActualHeight >= rows.ColumnHeaderHeight + 3 * rows.RowHeight, "default compact history keeps filters collapsed and room for at least three rows");
            var chute = rows.Columns.OfType<DataGridTextColumn>().Single(c => Equals(c.Header, "格口"));
            Check(chute.Width.IsSizeToCells && chute.MinWidth >= 100, "history chute column sizes to complete values rather than fixed 80-DIP ellipsis");
            CaptureInternal(evidence, "operator18-history-800", (Grid)window.FindName("ClientArea"), lines);
            vm.MarkReadFailure("Synthetic interruption"); Check(!vm.Left.DisplaySlotVisible && vm.Left.DisplayPresentation.Tone == ParcelPresentationTone.Stale, "lost snapshot cannot keep a live green result");
            lines.Add("Native Windows WPF, synthetic SQLite/built-in RCS loopback. Cue invocation and rendered geometry verified; physical audio, gloves, hardware PDA/MVS/customer RCS/site NOT_RUN. Efficiency counts are read-only software facts and do not represent physical parcels.");
        }
        finally { window.Close(); }
    }
}

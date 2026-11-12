using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.Text.Json;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster.Desktop;

static partial class Program
{
    static async Task VerifyRestartBarcodeDetails(string root, string evidence, List<string> lines)
    {
        void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); lines.Add("PASS RESTART-DETAILS " + text); }
        async Task Flush() => await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        await using var h = new ManualSupplyHarness(root, Path.Combine(evidence, "restart-details"), true);
        await h.StartAsync();
        const string returnedTaskId = "00001234567890123456789012345678901234567890";
        h.Peer.ReplyTaskId = returnedTaskId;
        var accepted = await h.App.SubmitParcelBarcodeAsync(await h.Command("left", "SYNTH-RESTART-ACCEPT"), default);
        await CameraRoundHarness.Until(async () => (await h.App.OperationAsync(accepted.OperationId!))?.Wcs?.State == StageState.Accepted);
        h.Peer.Mode = "reject";
        h.Peer.ReplyTaskId = null;
        var rejected = await h.App.SubmitParcelBarcodeAsync(await h.Command("right", "SYNTH-RESTART-REJECT"), default);
        await CameraRoundHarness.Until(async () => (await h.App.OperationAsync(rejected.OperationId!))?.Wcs?.State == StageState.Rejected);
        await SmRoundFixture.ConfirmRemovedAsync(h.App, "right");
        await h.App.DisposeAsync(); await h.StartAsync(ready: false);
        var window = new SupplyStationWindow(h.App, null, "standard") { ConfirmDiscardDrafts = _ => true };
        window.Show();
        async Task Until(Func<bool> done)
        { for (var i = 0; i < 300; i++) { await window.ViewModel.RefreshAsync(); await Flush(); if (done()) return; await Task.Delay(25); } throw new TimeoutException("Restart/details UI"); }
        try
        {
            await SetClient(window, 1600, 900); await Until(() => window.ViewModel.Fresh);
            var vm = window.ViewModel;
            Check(!vm.Left.HasCurrentInput && !vm.Right.HasCurrentInput && vm.Left.Input is null && vm.Right.Input is null,
                "both positions discard accepted/rejected previous-shift barcode inputs");
            Check(Descendants(window).OfType<SupplyStationPosition>().All(p => ((TextBox)p.FindName("BarcodeEntry")).Text.Length == 0),
                "actual work-page barcode editors are empty after restart");
            Click(window, "记录"); await Until(() => !vm.HistoryLoading && !vm.Busy && vm.HistoryItems.Length == 2);
            var history = Descendants(window).OfType<HistoryPage>().Single();
            var panel = (ContentControl)history.FindName("HistoryDetailPanel");
            var rows = (DataGrid)history.FindName("HistoryRows");
            var taskColumn = rows.Columns.OfType<DataGridTextColumn>().Single(c => Equals(c.Header, "任务号"));
            var acceptedRow = vm.HistoryItems.Single(item => item.Operation.Id == accepted.OperationId);
            var rejectedRow = vm.HistoryItems.Single(item => item.Operation.Id == rejected.OperationId);
            using var sentRequest = JsonDocument.Parse(h.Peer.Requests.First());
            var requestTaskId = sentRequest.RootElement.GetProperty("data")[0].GetProperty("params").GetProperty("taskId").GetRawText();
            rows.ScrollIntoView(acceptedRow, taskColumn); await Flush();
            Check(taskColumn.Visibility == Visibility.Visible && taskColumn.GetCellContent(acceptedRow) is TextBlock taskValue &&
                taskValue.Text == returnedTaskId && Equals(taskValue.ToolTip, returnedTaskId) &&
                Equals(taskColumn.OnCopyingCellClipboardContent(acceptedRow), returnedTaskId) &&
                returnedTaskId != requestTaskId,
                "history displays and copies the full RCS-returned task ID after restart, preserving leading zeros and keeping it distinct from the request task ID");
            rows.ScrollIntoView(rejectedRow, taskColumn); await Flush();
            Check(taskColumn.GetCellContent(rejectedRow) is TextBlock missingTask && missingTask.Text == "—",
                "a response without task ID shows no fabricated job or request number");
            var exportFile = Path.Combine(evidence, "returned-task-history.csv");
            await vm.ExportHistoryAsync(exportFile);
            var csv = await File.ReadAllLinesAsync(exportFile);
            Check(csv[0].EndsWith(",rcs_task_id_text", StringComparison.Ordinal) && csv.Length == 3 &&
                csv.Single(line => line.Contains(accepted.OperationId!, StringComparison.Ordinal)).EndsWith("\"'" + returnedTaskId + "\"", StringComparison.Ordinal) &&
                csv.Single(line => line.Contains(rejected.OperationId!, StringComparison.Ordinal)).EndsWith("\"'\"", StringComparison.Ordinal),
                "history export preserves the exact returned task as spreadsheet text and leaves missing responses empty");
            var statusColumn = rows.Columns.Single(c => Equals(c.Header, "状态"));
            Button Status(string id) => Descendants(rows).OfType<Button>().Single(b => b.Name == "HistoryStatusDetailsAction" && b.DataContext is ParcelHistoryItem item && item.Operation.Id == id);
            Check(!panel.IsVisible && Grid.GetColumnSpan(rows) == 2 && !Descendants(history).OfType<Expander>().Any(e => Equals(e.Header, "任务详情")),
                "history keeps both records, has no detail dropdown and initially uses full width");
            foreach (var size in new[] { (1600d, 900d), (800d, 420d) })
            {
                await SetClient(window, size.Item1, size.Item2); await Flush(); window.Activate();
                rows.ScrollIntoView(acceptedRow, statusColumn); await Flush();
                var status = Status(accepted.OperationId!);
                rows.ScrollIntoView(status.DataContext); await Flush();
                await BarcodeNativeKeyboard.ClickAsync(window, status, new Point(status.ActualWidth / 2, status.ActualHeight / 2));
                await Flush();
                Check(panel.IsVisible && vm.Selected?.Id == accepted.OperationId, "native status click opens the exact record at " + size.Item1);
                Check(rows.ActualHeight >= rows.ColumnHeaderHeight + rows.RowHeight + SystemParameters.HorizontalScrollBarHeight,
                    "opening details retains the header, a complete record row and horizontal scrolling at " + size.Item1);
                var width = rows.ActualWidth;
                var detail = (StackPanel)history.FindName("StructuredDetailsPanel");
                await BarcodeNativeKeyboard.ClickAsync(window, detail, new Point(12, 12)); await Flush();
                Check(panel.IsVisible, "clicking inside detail keeps it open at " + size.Item1);
                CaptureInternal(evidence, "details-open-" + (int)size.Item1, (Grid)window.FindName("ClientArea"), lines);
                // The query status stays visible at both supported sizes.
                var outside = (TextBlock)history.FindName("HistoryStatusLine");
                await BarcodeNativeKeyboard.ClickAsync(window, outside, new Point(10, outside.ActualHeight / 2)); await Flush();
                Check(!panel.IsVisible && Grid.GetColumnSpan(rows) == 2 && (vm.IsCompact || rows.ActualWidth > width + 100),
                    "native outside click removes the detail and reclaims its space at " + size.Item1);
                status = Status(accepted.OperationId!);
                await BarcodeNativeKeyboard.ClickAsync(window, status, new Point(status.ActualWidth / 2, status.ActualHeight / 2)); await Flush();
                Check(panel.IsVisible, "same selected record can reopen without changing selection");
                rows.ScrollIntoView(rejectedRow, statusColumn); await Flush();
                var other = Status(rejected.OperationId!);
                await BarcodeNativeKeyboard.ClickAsync(window, other, new Point(other.ActualWidth / 2, other.ActualHeight / 2)); await Flush();
                Check(panel.IsVisible && vm.Selected?.Id == rejected.OperationId, "another status replaces the detail without a blank pane");
                other.Focus();
                var key = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), Environment.TickCount, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                other.RaiseEvent(key); await Flush();
                Check(key.Handled && !panel.IsVisible, "routed Escape closes detail with keyboard focus on its status action");
                CaptureInternal(evidence, "details-closed-" + (int)size.Item1, (Grid)window.FindName("ClientArea"), lines);
            }
            await window.ShowDetailsAsync(accepted.OperationId!); await Flush();
            Check(panel.IsVisible && vm.Selected?.Id == accepted.OperationId, "work-card detail navigation still opens the original job");
            Check(h.Peer.Requests.Count == 2, "restart and every detail interaction preserve history without resending either request");
        }
        finally { window.Close(); await Flush(); }
        lines.Add("Actual Windows WPF HWND with native SendInput mouse and routed Escape; synthetic cameras/loopback RCS. Physical SDK, customer RCS and site acceptance NOT_RUN.");
    }
}

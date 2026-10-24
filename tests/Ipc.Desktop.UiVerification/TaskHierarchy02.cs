using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Platform;

static partial class Program
{
    // Coherent synthetic snapshot, never a production transition. Freeze original
    // request identity/configuration; adjust result, operation and position together.
    static ParcelSnapshot HierarchyState(ParcelSnapshot snapshot, StageState state)
    {
        var target = snapshot.Positions!.Single(p => p.Id == "left").CurrentOperation;
        var rejected = state == StageState.Rejected;
        var reason = rejected ? "合成业务拒绝：格口不可用" : state == StageState.Conflict ? "合成回执冲突：原请求出现矛盾结果" : "合成响应超时：原请求结果未知";
        return snapshot with
        {
            Operations = snapshot.Operations.Select(o => o.Id != target ? o : o with
            {
                State = rejected ? ParcelState.WcsRejected : ParcelState.WcsUnknown, Slot = null, Reason = reason,
                Wcs = o.Wcs! with { State = state, Failure = reason, Replies = rejected
                    ? [o.Wcs.Replies.Last() with { BusinessAccepted = false, Slot = null, Destination = null, Evidence = reason }]
                    : state == StageState.Conflict ? [o.Wcs.Replies.Last(), o.Wcs.Replies.Last() with { Id = Wire.Id(), BusinessAccepted = false, Slot = null, Destination = null, Evidence = reason }] : [] }
            }).ToArray(),
            Positions = snapshot.Positions!.Select(p => p.Id != "left" ? p : p with { Admit = rejected && !snapshot.Paused, MainResultComplete = rejected, BackgroundComplete = rejected, BlockingReasons = rejected ? [] : [reason] }).ToArray()
        };
    }

    static async Task VerifyHierarchy02(Window window, ParcelViewModel vm, HierarchyProjectionProbe probe, Action<string> navigate, string evidence, List<string> lines)
    {
        void Check(bool ok, string reason) { if (!ok) throw new InvalidOperationException(reason); lines.Add("PASS HIERARCHY02 " + reason); }
        async Task Flush() { await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout(); }
        var client = (Grid)window.FindName("ClientArea");
        var host = (ContentControl)window.FindName("PageHost");
        var ui = vm is SupplyStationViewModel ? "supply-station" : "classic";
        async Task RefreshUntil(Func<bool> ready)
        {
            for (var i = 0; i < 100; i++) { await vm.RefreshAsync(); await Flush(); if (ready()) return; await Task.Delay(20); }
            throw new InvalidOperationException("Projected scenario did not reach the current native snapshot within its budget");
        }
        navigate("日志查询"); await Task.Delay(100); await Flush();
        foreach (var outcome in new[] { "Rejected", "Conflict", "Unknown", "Throw" })
        {
            var calls = 0;
            probe.Command = _ => { calls++; return outcome == "Throw" ? Task.FromException<BusinessReceipt>(new System.IO.IOException("合成通信中断")) : Task.FromResult(new BusinessReceipt(outcome, "合成命令回执", vm.Snapshot!.Revision)); };
            await vm.PauseAsync(); await Flush();
            var receipt = (TextBox)window.FindName("GlobalOperationFeedback");
            Check(receipt.IsVisible && receipt.Text.Contains(outcome == "Conflict" ? "连续修订冲突" : outcome == "Throw" ? "待核对" : "合成命令回执"), ui + " log page exposes " + outcome + " pause result");
            Check(calls == (outcome == "Conflict" ? 3 : 1), "pause uses existing bounded conflict policy");
            var currentReceipt = receipt.Text;
            await vm.RunAsync(() => Task.CompletedTask, "读取最新50条"); await Flush();
            Check(receipt.Text == currentReceipt, "page feedback cannot erase current global receipt");
            CaptureInternal(evidence, ui + "-logs-command-" + outcome + "-800", client, lines);
        }
        probe.Command = _ => Task.FromResult(new BusinessReceipt("Rejected", "合成恢复条件不足", vm.Snapshot!.Revision));
        await vm.RunAsync(() => vm.CommandAsync("Resume"), "恢复供包"); await Flush();
        Check(((TextBox)window.FindName("GlobalOperationFeedback")).Text.Contains("合成恢复条件不足"), "resume rejection replaces previous pause result");
        var resumeStarted = new TaskCompletionSource();
        var resumeResult = new TaskCompletionSource<BusinessReceipt>();
        probe.Command = command =>
        {
            if (command.Kind == "Resume") { resumeStarted.TrySetResult(); return resumeResult.Task; }
            return Task.FromResult(new BusinessReceipt("Rejected", "合成独立暂停回执", vm.Snapshot!.Revision));
        };
        var resuming = vm.RunAsync(() => vm.CommandAsync("Resume"), "恢复供包");
        await resumeStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await vm.PauseAsync(); var pauseReceipt = vm.GlobalOperationFeedback;
        resumeResult.SetResult(new BusinessReceipt("Rejected", "较早恢复的迟到回执", vm.Snapshot!.Revision)); await resuming; await Flush();
        Check(pauseReceipt.Contains("合成独立暂停回执") && vm.GlobalOperationFeedback == pauseReceipt, "independent pause remains visible while busy; an older command cannot replace its receipt");
        vm.DismissGlobalOperationFeedback(); await Flush();
        Check(!((TextBox)window.FindName("GlobalOperationFeedback")).IsVisible, "explicit collapse removes acknowledged receipt without changing application state");
        probe.Command = null;
        await vm.RunAsync(() => Task.FromException(new InvalidOperationException("合成页内操作失败")), "页内操作"); await Flush();
        Check(vm.TaskFeedback.Contains("合成页内操作失败"), "previous global receipt cannot suppress a later page failure");
        var lateRead = new TaskCompletionSource();
        var pendingRead = vm.RunAsync(() => lateRead.Task, "合成跨页读取");
        navigate("工作台"); await Flush(); lateRead.SetResult(); await pendingRead; await Flush();
        Check(vm.TaskFeedback == "", "late result from a previous page does not become current page feedback");
        if (vm is not SupplyStationViewModel station) return;

        navigate("工作台"); await Task.Delay(80); await Flush();
        var controls = Descendants(host).OfType<SupplyStationPosition>().ToArray();
        var left = controls.Single(c => ReferenceEquals(c.DataContext, station.Left));
        var right = controls.Single(c => ReferenceEquals(c.DataContext, station.Right));
        Check(!((Grid)right.FindName("TaskFields")).IsVisible && !((TextBox)right.FindName("CurrentBarcode")).IsVisible, "idle position has no empty task matrix or duplicate placeholder barcode");
        Check(((Grid)left.FindName("TaskFields")).IsVisible && station.Left.WaveNo == "回执未包含", "valid receipt shows missing optional fields explicitly");
        var original = station.Left.Operation!;
        probe.Project = s => s with
        {
            Operations = s.Operations.Select(o => o.Id == original.Id ? o with { State = ParcelState.ResolvingWcs, Slot = null, Reason = null, Wcs = o.Wcs! with { State = StageState.Dispatching, Replies = [], Failure = null } } : o).ToArray(),
            Positions = s.Positions!.Select(p => p.Id == "left" ? p with { Admit = false, MainResultComplete = false, BackgroundComplete = false, BlockingReasons = ["Current entity main result is incomplete"] } : p).ToArray()
        };
        await RefreshUntil(() => station.Left.Operation?.State == ParcelState.ResolvingWcs);
        Check(station.Left.Status == "正在取格口" && !((Grid)left.FindName("TaskFields")).IsVisible && station.Left.NextStep.Contains("等待") && station.Right.CanAdmit, "pending request has one waiting result, no empty fields, and leaves right availability intact");
        CaptureInternal(evidence, "supply-station-pending-800", client, lines);
        probe.Project = null; await vm.RefreshAsync();
        var longCode = new string('0', 96) + "123456789";
        var longSlot = "SYNTHETIC-LONG-DESTINATION-" + new string('8', 70);
        probe.Project = s => s with { Operations = s.Operations.Select(o => o.Id == original.Id ? o with { ParcelCode = longCode, Slot = longSlot, Wcs = o.Wcs! with { Replies = o.Wcs.Replies.Select(r => r with { Slot = longSlot }).ToArray() } } : o).ToArray() };
        await RefreshUntil(() => station.Left.Barcode == longCode);
        var barcode = (TextBox)left.FindName("CurrentBarcode"); var slot = (TextBox)left.FindName("CurrentSlot");
        barcode.BringIntoView(); barcode.Focus(); barcode.SelectAll(); await vm.RefreshAsync(); await Flush();
        Check(barcode.Text == longCode && barcode.SelectedText == longCode && barcode.IsKeyboardFocused, "long barcode remains fully selectable across refresh");
        slot.SelectAll(); Check(slot.SelectedText == longSlot, "long slot remains fully selectable");
        var body = (ScrollViewer)left.FindName("PositionBody"); body.ScrollToEnd(); await Flush(); var offset = body.VerticalOffset;
        await vm.RefreshAsync(); await Flush(); Check(offset > 0 && body.VerticalOffset == offset, "position polling preserves scroll position");
        body.ScrollToTop(); barcode.ScrollToHome(); slot.ScrollToHome(); await Flush();
        Check(barcode.TranslatePoint(new Point(), body).Y + barcode.FontSize * 1.4 <= body.ActualHeight, "minimum viewport shows at least one full barcode line even with a long slot");
        CaptureInternal(evidence, "supply-station-long-800", client, lines);
        await SetClient(window, 1366, 768); CaptureInternal(evidence, "supply-station-long-1366", client, lines);
        probe.Project = s => s with { Inputs = s.Inputs!.Select(i => i.PositionId == "left" ? i with { Outcome = ParcelInputOutcome.Blocked, OperationId = null, Reason = "合成新输入未准入", Observation = i.Observation with { Codes = ["NEW-INPUT-02"] } } : i).ToArray() };
        await RefreshUntil(() => station.Left.InputUnadmitted && station.Left.Barcode == "NEW-INPUT-02");
        Check(station.Left.Barcode == "NEW-INPUT-02" && !slot.IsVisible && !((Grid)left.FindName("TaskFields")).IsVisible && station.Left.OriginalResponsibility.Contains(original.Id), "unadmitted input cannot inherit original slot and retains original owner");
        CaptureInternal(evidence, "supply-station-unadmitted-1366", client, lines);
        probe.Project = null; await vm.RefreshAsync(); vm.MarkReadFailure("合成状态读取失败"); await Flush();
        Check(!slot.IsVisible && !station.Left.CanAdmit && !station.Left.CanContinue && station.Left.Status == "状态已失效", "stale snapshot hides actionable destination and preserves command guards");
        CaptureInternal(evidence, "supply-station-stale-1366", client, lines);
        await vm.RefreshAsync();
        navigate("日志查询"); await Task.Delay(80); Click(host, "查询");
        var queryButton = Descendants(host).OfType<Button>().Single(b => Equals(b.Content, "查询"));
        for (var i = 0; i < 100 && !queryButton.IsEnabled; i++) await Task.Delay(20);
        await SetClient(window, 800, 420); await Flush();
        var range = Descendants(host).OfType<TextBox>().Single(b => AutomationProperties.GetName(b) == "日志当前查询范围");
        var advanced = Descendants(host).OfType<Expander>().Single(e => Equals(e.Header, "时间、更多筛选、上下文与导出"));
        Check(range.IsVisible && range.Text.StartsWith("当前查询（本地）") && Inside(range, client) && Inside(advanced, client), "collapsed advanced filters retain actual query range and reachable entry at minimum size");
        var converted = (DateTimeOffset)new LocalTimestampConverter().Convert(new DateTimeOffset(2026, 10, 24, 8, 0, 0, TimeSpan.FromHours(-4)), typeof(DateTimeOffset), null!, System.Globalization.CultureInfo.InvariantCulture);
        Check(converted == new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero) && converted.Offset == TimeZoneInfo.Local.GetUtcOffset(converted), "list conversion preserves instant and uses local offset for nonlocal records");
        CaptureInternal(evidence, "supply-station-log-range-800", client, lines);
    }
}

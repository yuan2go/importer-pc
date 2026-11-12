using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using Ipc.Application.Contracts;
using Ipc.Assembly;
using Ipc.Platform;

namespace Ipc.Desktop;

sealed class MaintenanceHistoryWindow : StartupWorkflowWindow
{
    readonly ListBox operations = new() { MinHeight = 120, MaxHeight = 220 };
    readonly TextBox detail = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 160 };
    readonly Button next, previous, inspect, resume;
    DeploymentHistoryPage? page;
    DeploymentHistoryItem? selected;
    int offset;
    internal Func<int, Task<DeploymentHistoryPage>> ReadHistory { get; set; } = ParcelStartupServices.ReadMaintenanceHistoryAsync;
    internal Func<string, string, Task<DeploymentHistoryItem>> InspectHistory { get; set; } = ParcelStartupServices.InspectMaintenanceHistoryAsync;
    public MaintenanceHistoryWindow() : base("历史维护结果 · 离线维护")
    {
        Width = 860; Height = 700;
        Note("按原操作核对安装、回滚、恢复、卸载和清理；查询不执行续办。原提交、当前部署关系及当前余项分别显示。机器记录读取需要 Windows 管理员授权。");
        Body.Children.Add(operations); Body.Children.Add(detail);
        operations.SelectionChanged += (_, _) => { selected = null; detail.Text = "点击“核对所选结果”读取原部署及实际余项；选择不会执行维护。"; UpdateActions(); };
        previous = Action("上一页", async () => { offset = Math.Max(0, offset - 12); await ReadPage(); }, readOnly: true);
        next = Action("下一页", async () => { offset += 12; await ReadPage(); }, readOnly: true);
        inspect = Action("核对所选结果", InspectSelected, readOnly: true);
        resume = Action("继续原清单清理", async () =>
        {
            await InspectSelected(); var original = selected ?? throw new InvalidOperationException("先选择原操作。");
            if (!original.CanResume || original.Kind is not ("assets" or "prune")) throw new InvalidOperationException("原状态不能续办。");
            if (MessageBox.Show(this, "仅继续此原操作的冻结清单，执行前复核当前引用。新程序、缓存、配置和恢复数据保留。", "继续原清理", MessageBoxButton.OKCancel, MessageBoxImage.None, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
            if (UnknownOutcome) ResolveKnownMaintenanceOutcome();
            if (original.Kind == "assets")
                _ = await RunMaintenance("继续原卸载资产清理：" + original.OperationId, (p, t) => ParcelStartupServices.ResumeHistoricalAssetsAsync(original.OperationId, p, t));
            else _ = await RunMaintenance("继续原版本清理：" + original.OperationId, (p, t) => ParcelStartupServices.InspectVersionPruningAsync(original.OperationId, true, p, t));
            await InspectSelected();
        }, readOnly: true);
        var refresh = Action("刷新列表", ReadPage, readOnly: true);
        var close = Action("关闭", () => { Busy = false; Close(); return Task.CompletedTask; }, readOnly: true); close.IsCancel = true;
        foreach (var (button, id) in new[] { (previous, "Previous"), (next, "Next"), (inspect, "Inspect"), (resume, "Resume"), (refresh, "Refresh"), (close, "Close") })
            AutomationProperties.SetAutomationId(button, "MaintenanceHistory." + id);
        Loaded += async (_, _) => await Run(ReadPage); UpdateActions();
    }
    async Task ReadPage()
    {
        page = await Task.Run(() => ReadHistory(offset)); selected = null;
        operations.ItemsSource = page.Items.Select(p => Kind(p.Kind) + " · 原记录：" + StatusText(p.RecordedStatus) + "\n操作：" + p.OperationId + "\n原部署：" + (p.Deployment?.SourceCommit ?? p.OriginalScope ?? "证据待核对") + "\n记录时间：" + (p.At?.ToString("yyyy-MM-dd HH:mm:ss zzz") ?? "未记录")).ToArray();
        detail.Text = page.Items.Length == 0 ? "没有此页历史记录。" : "选择原操作，核对结果和原部署。";
    }
    async Task InspectSelected()
    {
        if (page is null || operations.SelectedIndex < 0) throw new InvalidOperationException("先选择原操作。");
        var choice = page.Items[operations.SelectedIndex]; selected = null; detail.Clear();
        var item = await Task.Run(() => InspectHistory(choice.Kind, choice.OperationId)); selected = item;
        detail.Text = Kind(item.Kind) + " · " + item.OperationId + "\n原提交记录：" + StatusText(item.RecordedStatus) + "\n当前核对：" + StatusText(item.Status) +
            "\n" + item.Detail + (item.Deployment is { } location ? "\n原部署来源：" + location.SourceCommit + "\n原程序：" + location.ReleaseRoot + "\n原数据：" + location.DataRoot : "") +
            "\n余项（" + item.TotalRemaining + "）：\n" + string.Join("\n", item.Remaining) + "\n保留及原因（" + item.TotalRetained + "）：\n" + string.Join("\n", item.Retained.Select(r => r.Root + "：" + r.Reason)) + "\n完整原记录：" + item.OperationFile;
        if (item.Kind is "migration" or "restore")
            detail.Text += "\n\n恢复责任与业务事实：" + (item.RestoreSummary ?? "未取得原站审计，保持待核实。部署提交不等于责任确认或业务启用。");
        Message(item.Detail);
    }
    internal static string Kind(string value) => value switch { "setup" => "安装", "rollback" => "回滚", "migration" => "恢复接管", "restore" => "恢复复制", "remove" => "卸载", "prune" => "旧版本清理", "assets" => "卸载资产清理", "purge" => "本站数据删除", _ => "未知操作" };
    internal static string StatusText(string value) => value switch
    {
        "Completed" or "CompletedIsolated" => "原操作已完成", "Purged" => "本站数据已删除", "Prepared" or "ReadyToCommit" or "Cleaning" => "原操作未收口",
        "Superseded" or "SupersededByRemoval" => "已被后续操作取代", "CommittedNeedsSystemRepair" or "ProjectionPending" => "已提交，入口待修复",
        "RemovedDataRetained" => "程序已移除，数据保留", "RestartRequired" => "尚有重启待清理项", "Partial" or "PartiallyPurged" or "RemovedWithRetainedFiles" => "尚有余项",
        "NotStarted" or "NotCommitted" or "Failed" => "原操作未提交", "RetainedUnverified" => "余项已保留，未逐项核对", "CompletedWithRetainedFiles" => "原清单完成，未知文件保留",
        "CopyIntentPrepared" => "原复制意图已登记", "AdoptedIsolated" => "接管已完成，业务事实另行核对", _ => "待核实"
    };
    void UpdateActions()
    {
        if (next is null) return;
        previous.Visibility = offset > 0 ? Visibility.Visible : Visibility.Collapsed;
        next.Visibility = page?.HasMore == true ? Visibility.Visible : Visibility.Collapsed;
        resume.Visibility = selected?.CanResume == true && selected.Kind is "assets" or "prune" ? Visibility.Visible : Visibility.Collapsed;
        inspect.IsEnabled = operations.SelectedIndex >= 0;
    }
    protected override void OperationCompleted() => UpdateActions();
}

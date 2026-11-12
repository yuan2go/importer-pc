using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Ipc.Application.Contracts;
using Ipc.Assembly;
using Ipc.Platform;
using Microsoft.Win32;

namespace Ipc.Desktop;

sealed class StartupVersionPruneWindow : StartupWorkflowWindow
{
    readonly IManagementMode authority;
    readonly TextBox destination;
    readonly CheckBox observed = new() { Content = "已确认当前构建正常打开并完成必要观察（不代表现场验收）", MinHeight = 44 };
    readonly TextBox detail = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 160 };
    readonly Button review, execute, inspect, resume, keep;
    StartupVersionPruneReview? plan;
    DeploymentPruneReceipt? receipt;
    string? operation;
    string frozenBackup = "";
    internal Func<string, string, bool, IProgress<MaintenanceProgress>?, CancellationToken, Task<StartupVersionPruneReview>> ReviewPruning { get; set; } = ParcelStartupServices.ReviewVersionPruningAsync;
    internal Func<StartupVersionPruneReview, string, IProgress<MaintenanceProgress>?, CancellationToken, Task<DeploymentPruneReceipt>> ExecutePruning { get; set; } = ParcelStartupServices.PruneVersionsAsync;
    internal Func<string, bool, IProgress<MaintenanceProgress>?, CancellationToken, Task<DeploymentPruneReceipt>> InspectPruning { get; set; } = ParcelStartupServices.InspectVersionPruningAsync;
    internal Func<string, IProgress<MaintenanceProgress>?, CancellationToken, Task<DeploymentPruneReceipt>> KeepRemainder { get; set; } = ParcelStartupServices.KeepVersionPruningRemainderAsync;
    internal Func<string?> LastOperation { get; set; } = () => WindowsDeploymentSetup.LastVersionPruning(ParcelStartupServices.ApplicationId);
    internal Func<string> RegistrationHash { get; set; } = () => Wire.FileHash(WindowsDeploymentSetup.Registration(ParcelStartupServices.ApplicationId));

    public StartupVersionPruneWindow(IManagementMode authority) : base("旧版本清理 · 离线维护")
    {
        this.authority = authority; Width = 840; Height = 680;
        Note("按真实部署关系检查旧构建；保留当前部署、合法回滚候选、业务数据和原操作证据。相同 1.0.0 版本的不同构建也可检查。请先完全关闭供包台。");
        destination = Field("清理前完整备份位置", "", _ => { plan = null; UpdateActions(); });
        var choose = new Button { Content = "选择备份位置…", MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Left };
        choose.Click += (_, _) => { var picker = new SaveFileDialog { FileName = "before-version-cleanup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".db", Filter = "完整备份|*.db" }; if (picker.ShowDialog(this) == true) destination.Text = picker.FileName; };
        Body.Children.Add(choose); Body.Children.Add(observed); Body.Children.Add(detail);
        observed.Checked += (_, _) => { plan = null; UpdateActions(); }; observed.Unchecked += (_, _) => { plan = null; UpdateActions(); };
        review = Action("检查旧版本", async () =>
        {
            await RequireAuthority();
            if (string.IsNullOrWhiteSpace(destination.Text) || observed.IsChecked != true) throw new InvalidOperationException("先选择新的完整备份位置并确认当前构建已正常打开。");
            frozenBackup = destination.Text;
            plan = await RunMaintenance("只读生成清理计划，核对真实部署、库存和业务责任。", (p, t) => ReviewPruning(RegistrationHash(), frozenBackup, true, p, t), readOnly: true);
            receipt = null; operation = null;
            detail.Text = plan.Deployment.Reason + "\n\n将删除：\n" + string.Join("\n", plan.Deployment.Delete.Select(d => d.Root + "（" + (d.ReviewedFileCount ?? d.Files.Count) + " 文件）")) +
                "\n\n保留及原因：\n" + string.Join("\n", plan.Deployment.Retain.Select(r => r.Root + "：" + r.Reason)) + "\n\n业务准入：" + plan.Business?.OperatorSummary;
            HideCompletedMaintenanceProgress(); detail.BringIntoView();
            Message(plan.Deployment.Allowed && plan.Business?.Allowed == true ? "检查通过。核对删除及保留范围后明确清理。" : "尚不能清理，所有原件保留；请查看原因。");
        });
        execute = Action("备份并清理旧版本", async () =>
        {
            await RequireAuthority(); var selected = plan ?? throw new InvalidOperationException("先检查计划。");
            if (frozenBackup != destination.Text || selected.Business?.Allowed != true || !selected.Deployment.Allowed) throw new InvalidOperationException("计划已变化或不允许清理，请重新检查。");
            operation = selected.Deployment.OperationId;
            receipt = await RunMaintenance("创建完整备份后，仅删除已核验的固定旧程序和缓存清单。", (p, t) => ExecutePruning(selected, frozenBackup, p, t)); ShowReceipt();
        });
        inspect = Action("核对原清理", () => Inspect(false), readOnly: true);
        resume = Action("继续原清单清理", () => Inspect(true), readOnly: true);
        keep = Action("保留原余项并结束", async () =>
        {
            await RequireAuthority(); await Inspect(false);
            if (receipt?.Status is not ("Prepared" or "Partial")) return;
            if (MessageBox.Show(this, "不再删除原范围余项，保留库存和回执；未核验项不能视为已删除。", "保留原余项", MessageBoxButton.OKCancel, MessageBoxImage.None, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
            receipt = await RunMaintenance("记录保留原余项，不执行文件删除。", (p, t) => KeepRemainder(operation!, p, t)); ShowReceipt();
        }, readOnly: true);
        var close = Action("关闭", () => { Busy = false; Close(); return Task.CompletedTask; }, readOnly: true); close.IsCancel = true;
        foreach (var (button, id) in new[] { (review, "Review"), (execute, "Execute"), (inspect, "Inspect"), (resume, "Resume"), (keep, "Keep"), (close, "Close") })
            AutomationProperties.SetAutomationId(button, "VersionPrune." + id);
        Loaded += async (_, _) => await Run(async () => { await RequireAuthority(); operation = LastOperation(); if (operation is not null) await Inspect(false); });
        UpdateActions();
    }
    async Task RequireAuthority() { if (!(await authority.ManagementModeStatusAsync()).Authorized) throw new UnauthorizedAccessException("管理授权已结束，请关闭并重新授权，原清单保留。"); }
    async Task Inspect(bool continueOriginal)
    {
        await RequireAuthority();
        if (operation is null) throw new InvalidOperationException("没有原清理操作。");
        var observedReceipt = await InspectPruning(operation, false, null, default);
        ResolveKnownMaintenanceOutcome(); receipt = observedReceipt;
        if (continueOriginal && receipt.Status is "Prepared" or "Partial")
            receipt = await RunMaintenance("复核当前引用后按原冻结清单继续，不扩大范围。", (p, t) => InspectPruning(operation, true, p, t));
        ShowReceipt();
    }
    void ShowReceipt()
    {
        if (receipt is null) return;
        detail.Text = "原操作：" + receipt.OperationId + "\n" + receipt.Detail + "\n回执：" + receipt.OperationFile + "\n余项：\n" + string.Join("\n", receipt.Remaining) +
            (receipt.RemainingTruncated ? "\n仅显示摘要；完整余项 " + receipt.TotalRemaining + " 项，见原回执。" : "");
        Message(receipt.Detail);
        if (!UnknownOutcome) HideCompletedMaintenanceProgress();
    }
    void UpdateActions()
    {
        if (review is null) return;
        var unresolved = operation is not null && (UnknownOutcome || receipt?.Status is "Prepared" or "Partial");
        review.Visibility = !unresolved ? Visibility.Visible : Visibility.Collapsed;
        execute.Visibility = plan is { Deployment.Allowed: true, Business.Allowed: true } && operation is null && !UnknownOutcome ? Visibility.Visible : Visibility.Collapsed;
        inspect.Visibility = operation is not null ? Visibility.Visible : Visibility.Collapsed;
        resume.Visibility = keep.Visibility = receipt?.Status is "Prepared" or "Partial" ? Visibility.Visible : Visibility.Collapsed;
        foreach (var button in new[] { review, execute, inspect, resume, keep }) button.ClearValue(StyleProperty);
        (execute.Visibility == Visibility.Visible ? execute : resume.Visibility == Visibility.Visible ? resume : review).SetResourceReference(StyleProperty, "Primary");
    }
    protected override void OperationCompleted() => UpdateActions();
}

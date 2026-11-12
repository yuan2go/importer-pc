using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using Ipc.Application.Contracts;
using Ipc.Assembly;
using Ipc.Platform;
using Microsoft.Win32;

namespace Ipc.Desktop;

sealed class StartupRollbackWindow : StartupWorkflowWindow
{
    readonly IManagementMode authority;
    readonly TextBox destination;
    readonly TextBox evidence = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 140 };
    readonly Button plan, execute, inspect, repair;
    StartupRollbackReview? reviewed;
    DeploymentRollbackReceipt? receipt;
    string? originalOperation;
    string frozenBackup = "";
    internal Func<string, string, IProgress<MaintenanceProgress>?, CancellationToken, Task<StartupRollbackReview>> ReviewRollback { get; set; } = ParcelStartupServices.ReviewRollbackAsync;
    internal Func<StartupRollbackReview, string, IProgress<MaintenanceProgress>?, CancellationToken, Task<DeploymentRollbackReceipt>> ExecuteRollback { get; set; } = ParcelStartupServices.RollbackAsync;
    internal Func<string, bool, IProgress<MaintenanceProgress>?, CancellationToken, Task<DeploymentRollbackReceipt>> InspectRollback { get; set; } = ParcelStartupServices.InspectRollbackAsync;
    internal Func<string?> LastOperation { get; set; } = () => WindowsDeploymentSetup.RollbackOperationToInspect(ParcelStartupServices.ApplicationId);
    internal Func<string> RegistrationHash { get; set; } = () => Wire.FileHash(WindowsDeploymentSetup.Registration(ParcelStartupServices.ApplicationId));

    public StartupRollbackWindow(IManagementMode authority) : base("上一部署回滚 · 离线维护")
    {
        this.authority = authority;
        Width = 780; Height = 590; MinHeight = 420;
        WorkflowHeading.Text = "回滚到上一部署";
        Note("仅切换程序，当前数据库、已生效及待生效配置、原请求和编号原账保留。设备不会自动启动。目标已清理或不完整时不能回滚。");
        destination = Field("回滚前完整备份位置", "", _ => { reviewed = null; UpdateActions(); });
        AutomationProperties.SetAutomationId(destination, "Rollback.BackupDestination");
        var choose = new Button { Content = "选择备份位置…", MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Left };
        choose.Click += (_, _) =>
        {
            var picker = new SaveFileDialog { Title = "回滚前完整备份", FileName = "before-rollback-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".db", Filter = "完整备份数据库|*.db" };
            if (picker.ShowDialog(this) == true) destination.Text = picker.FileName;
        };
        Body.Children.Add(choose); Body.Children.Add(evidence);
        plan = Action("检查回滚", async () =>
        {
            await RequireAuthority();
            if (string.IsNullOrWhiteSpace(destination.Text)) throw new InvalidOperationException("先选择新的完整备份位置。");
            frozenBackup = destination.Text;
            reviewed = await RunMaintenance("检查上一部署及原运行责任，不切换程序。",
                (progress, token) => ReviewRollback(RegistrationHash(), frozenBackup, progress, token), readOnly: true);
            receipt = null; originalOperation = null;
            var deployment = reviewed.Deployment;
            evidence.Text = "当前：" + deployment.CurrentVersion + "\n" + deployment.Current.SourceCommit +
                "\n上一部署：" + (deployment.TargetVersion ?? "不可用") + "\n" + deployment.Target?.SourceCommit +
                "\n\n程序检查：" + deployment.Reason + "\n运行责任：" + reviewed.Business?.OperatorSummary;
            Message(deployment.Allowed && reviewed.Business?.Allowed == true ? "检查通过。请核对当前和目标构建后明确回滚。" : "尚不能回滚，原程序和数据保留，请查看检查结果。");
        });
        execute = Action("回滚到上一部署", async () =>
        {
            await RequireAuthority();
            var review = reviewed ?? throw new InvalidOperationException("先检查回滚条件。");
            if (!review.Deployment.Allowed || review.Business?.Allowed != true || frozenBackup != destination.Text)
                throw new InvalidOperationException("原检查不允许回滚或备份位置已改变，请重新检查。");
            originalOperation = review.Deployment.OperationId;
            receipt = await RunMaintenance("创建完整备份后切换到已核验的上一部署；不恢复旧数据库。",
                (progress, token) => ExecuteRollback(review, frozenBackup, progress, token));
            ShowReceipt();
        });
        inspect = Action("核对原回滚", async () => await InspectOriginal(false), readOnly: true);
        repair = Action("修复原系统入口", async () => await InspectOriginal(true), readOnly: true);
        var close = Action("关闭", () => { Busy = false; Close(); return Task.CompletedTask; }, readOnly: true);
        close.IsCancel = true;
        foreach (var (button, name) in new[] { (plan, "Review"), (execute, "Execute"), (inspect, "Inspect"), (repair, "Repair"), (close, "Close") })
            AutomationProperties.SetAutomationId(button, "Rollback." + name);
        UpdateActions();
        Loaded += async (_, _) => await Run(async () =>
        {
            await RequireAuthority();
            originalOperation = LastOperation();
            if (originalOperation is not null) await InspectOriginal(false);
            else Message("选择新的备份位置，再检查上一部署及运行责任。");
        });
    }
    async Task RequireAuthority()
    {
        if (!(await authority.ManagementModeStatusAsync()).Authorized) throw new UnauthorizedAccessException("管理授权已结束，请关闭本窗口后重新授权。原回滚记录保留。");
    }
    async Task InspectOriginal(bool repairOriginal)
    {
        await RequireAuthority();
        if (originalOperation is null) throw new InvalidOperationException("没有可核对的原回滚。");
        if (repairOriginal)
        {
            var observed = await InspectRollback(originalOperation, false, null, default);
            if (observed.Status is not ("CommittedNeedsSystemRepair" or "NotCommitted")) throw new InvalidOperationException("原回滚状态已变化，请先核对结果。");
            ResolveKnownMaintenanceOutcome();
        }
        receipt = await RunMaintenance(repairOriginal ? "收口原回滚系统入口，不重复切换程序。" : "只读核对原回滚，不重新执行。",
            (progress, token) => InspectRollback(originalOperation, repairOriginal, progress, token), readOnly: !repairOriginal);
        if (receipt.Status is "Completed" or "NotCommitted" or "CommittedNeedsSystemRepair" or "SupersededByRemoval") ResolveKnownMaintenanceOutcome();
        ShowReceipt();
    }
    void ShowReceipt()
    {
        if (receipt is null) return;
        evidence.Text = (evidence.Text.Length == 0 ? "" : evidence.Text + "\n\n") + "原操作：" + receipt.OperationId + "\n" + receipt.Detail + "\n回执：" + receipt.OperationFile;
        Message(receipt.Detail);
        if (!UnknownOutcome) HideCompletedMaintenanceProgress();
    }
    void UpdateActions()
    {
        if (plan is null) return; // Field callback can run while constructing the form.
        var ready = reviewed is { Deployment.Allowed: true, Business.Allowed: true } && frozenBackup == destination.Text;
        var originalClosed = receipt?.Status is "Completed" or "SupersededByRemoval" || receipt?.ClosedWithoutCommit == true;
        plan.Visibility = (!ready || originalClosed) && !UnknownOutcome && receipt?.Status is not ("CommittedNeedsSystemRepair" or "Unknown") ? Visibility.Visible : Visibility.Collapsed;
        execute.Visibility = ready && originalOperation is null && receipt is null && !UnknownOutcome ? Visibility.Visible : Visibility.Collapsed;
        inspect.Visibility = originalOperation is not null ? Visibility.Visible : Visibility.Collapsed;
        repair.Visibility = receipt?.Status == "CommittedNeedsSystemRepair" || receipt is { Status: "NotCommitted", ClosedWithoutCommit: false } ? Visibility.Visible : Visibility.Collapsed;
        repair.Content = receipt?.Status == "NotCommitted" ? "结束原次回滚" : "修复原系统入口";
        destination.IsReadOnly = UnknownOutcome || receipt?.Status == "CommittedNeedsSystemRepair";
        foreach (var button in new[] { plan, execute, inspect, repair })
        {
            button.SetResourceReference(StyleProperty, button == repair && repair.Visibility == Visibility.Visible || button == execute && execute.Visibility == Visibility.Visible || button == plan && plan.Visibility == Visibility.Visible ? "Primary" : typeof(Button));
            button.IsDefault = false; // No program switch or repair from default Enter.
        }
    }
    protected override void OperationCompleted() => UpdateActions();
}

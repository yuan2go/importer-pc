using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Ipc.Application.Contracts;
using Ipc.Assembly;
using Ipc.Platform;

namespace Ipc.Desktop;

sealed partial class ProductMaintenanceWindow
{
    internal Func<string, string, IProgress<MaintenanceProgress>?, CancellationToken, Task<StartupRollbackReview>> ReviewVersionRollback { get; set; } = ParcelStartupServices.ReviewRollbackAsync;
    internal Func<StartupRollbackReview, string, IProgress<MaintenanceProgress>?, CancellationToken, Task<DeploymentRollbackReceipt>> ExecuteVersionRollback { get; set; } = ParcelStartupServices.RollbackAsync;
    internal Func<string, bool, IProgress<MaintenanceProgress>?, CancellationToken, Task<DeploymentRollbackReceipt>> InspectVersionRollback { get; set; } = ParcelStartupServices.InspectRollbackAsync;
    internal Func<string?> LastVersionRollback { get; set; } = () => WindowsDeploymentSetup.LastRollbackOperation(ParcelStartupServices.ApplicationId);
    internal Func<string, bool>? ConfirmVersionRollback { get; set; }
    StartupRollbackReview? rollbackReview;
    string rollbackBackup = "";
    string? rollbackOperation;
    Button? rollbackExecute, rollbackRepair;
    TextBlock? rollbackStatus;

    void InvalidateRollbackReview()
    {
        rollbackReview = null;
        if (rollbackExecute is not null) rollbackExecute.IsEnabled = false;
        if (rollbackStatus is not null) rollbackStatus.Text = "备份位置已改变，请重新检查回退条件。";
    }

    void AddRollbackSection()
    {
        var body = new StackPanel { Margin = new(0, 8, 0, 8) };
        body.Children.Add(new TextBlock { Text = "更新后无法正常使用时，可检查上一版本。回退必须先完成完整备份；业务数据保持当前状态，不会恢复旧数据库或重发请求。", TextWrapping = TextWrapping.Wrap });
        rollbackStatus = new TextBlock { Text = "尚未检查。没有兼容版本、存在未决请求或程序仍在运行时，不能回退。", TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 8) };
        AutomationProperties.SetName(rollbackStatus, "版本回退说明");
        body.Children.Add(rollbackStatus);
        var buttons = new WrapPanel(); body.Children.Add(buttons);
        Button VersionAction(string name, Func<Task> action, bool readOnly = false)
        {
            var button = Action(name, action, readOnly); Actions.Children.Remove(button); buttons.Children.Add(button); return button;
        }
        VersionAction("检查回退条件", ReviewRollback, readOnly: true);
        rollbackExecute = VersionAction("备份并回退到上一版本…", ExecuteRollback); rollbackExecute.IsEnabled = false;
        VersionAction("核对上次回退", () => InspectRollback(false), readOnly: true);
        rollbackRepair = VersionAction("修复上次回退的入口…", () => InspectRollback(true), readOnly: true); rollbackRepair.IsEnabled = false;
        Body.Children.Add(new Expander { Header = "版本回退", Content = body, Margin = new(0, 16, 0, 8) });
    }

    async Task ReviewRollback()
    {
        if (UnknownOutcome) throw new InvalidOperationException("上次结果尚未核对，请先核对上次回退。");
        rollbackExecute!.IsEnabled = false; rollbackRepair!.IsEnabled = false;
        var current = CurrentDeployment() ?? throw new InvalidDataException("缺少原部署登记。");
        rollbackBackup = Path.Combine(Path.GetFullPath(backupDirectory.Text), current.ApplicationId + "-pre-rollback-" + Wire.Id() + ".db");
        rollbackReview = await RunMaintenance("检查上一版本、当前数据和备份位置", (progress, token) =>
            ReviewVersionRollback(RegistrationIdentity(), rollbackBackup, progress, token), readOnly: true);
        rollbackOperation = rollbackReview.Deployment.OperationId;
        var allowed = rollbackReview.Deployment.Allowed && rollbackReview.Business?.Allowed == true;
        rollbackStatus!.Text = rollbackReview.Deployment.Reason +
            (rollbackReview.Deployment.CurrentVersion is { } version ? "\n当前版本：" + version + "；上一版本：" + rollbackReview.Deployment.TargetVersion : "") +
            (rollbackReview.Business is { } business && !business.Allowed ? "\n" + business.Summary : "") +
            (allowed ? "\n完整备份：" + rollbackBackup : "\n原程序、数据和记录保留。请先处理上述条件。");
        rollbackExecute.IsEnabled = allowed;
    }

    async Task ExecuteRollback()
    {
        var review = rollbackReview;
        if (review?.Deployment.Allowed != true || review.Business?.Allowed != true) throw new InvalidOperationException("请先检查回退条件。");
        var message = "将先创建完整备份，再从 " + review.Deployment.CurrentVersion + " 切换到 " + review.Deployment.TargetVersion +
            "。\n业务数据保持当前状态，不会重发原请求。\n备份：" + rollbackBackup + "\n是否继续？";
        var confirmed = ConfirmVersionRollback?.Invoke(message) ?? MessageBox.Show(this, message, "确认程序回退", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
        if (!confirmed) return;
        rollbackExecute!.IsEnabled = false;
        var receipt = await RunMaintenance("完整备份并切换到上一版本", (progress, token) => ExecuteVersionRollback(review, rollbackBackup, progress, token));
        ShowRollbackReceipt(receipt);
    }

    async Task InspectRollback(bool repair)
    {
        rollbackOperation ??= LastVersionRollback();
        if (rollbackOperation is null) { rollbackStatus!.Text = "没有上次回退记录。"; return; }
        var receipt = await RunMaintenance(repair ? "按原登记修复系统入口；不再次回退" : "只读核对上次回退", (progress, token) =>
            InspectVersionRollback(rollbackOperation, repair, progress, token), readOnly: true);
        ShowRollbackReceipt(receipt);
    }

    void ShowRollbackReceipt(DeploymentRollbackReceipt receipt)
    {
        rollbackOperation = receipt.OperationId;
        rollbackStatus!.Text = receipt.Detail + (receipt.Status == "Completed" ? "\n请关闭本窗口，从开始菜单重新打开软件。" : "");
        rollbackExecute!.IsEnabled = false;
        rollbackRepair!.IsEnabled = receipt.Status is "CommittedNeedsSystemRepair" or "NotCommitted";
        if (receipt.Status is "Completed" or "NotCommitted") { ResolveKnownMaintenanceOutcome(); rollbackReview = null; }
        Message(receipt.Detail);
    }
}

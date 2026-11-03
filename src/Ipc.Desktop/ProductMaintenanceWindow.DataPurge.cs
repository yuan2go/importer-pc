using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Ipc.Application.Contracts;
using Ipc.Assembly;
using Ipc.Platform;

namespace Ipc.Desktop;

// Permanent deletion is offered only after the program was removed with data retained. It is a
// separate, unchecked-by-default task: review first (every request settled), then a typed
// confirmation. A partial result can only be continued with its original list.
sealed partial class ProductMaintenanceWindow
{
    internal Func<string, string?, IProgress<MaintenanceProgress>?, CancellationToken, Task<StartupDataPurgeReview>> ReviewPurge { get; set; } = ParcelStartupServices.ReviewDataPurgeAsync;
    internal Func<StartupDataPurgeReview, string?, IProgress<MaintenanceProgress>?, CancellationToken, Task<DeploymentPurgeReceipt>> ExecutePurge { get; set; } = ParcelStartupServices.PurgeDataAsync;
    internal Func<string, IProgress<MaintenanceProgress>?, CancellationToken, Task<DeploymentPurgeReceipt>> ResumePurge { get; set; } = ParcelStartupServices.ResumeDataPurgeAsync;
    internal Func<string, bool>? ConfirmPurge { get; set; }
    const string PurgeWord = "永久删除";
    readonly CheckBox purgeBackupEnabled = new() { Content = "删除前创建完整备份（推荐）", IsChecked = true, Margin = new(0, 8, 0, 8) };
    readonly TextBox purgeConfirmation = new() { MinHeight = 36, MaxLength = 16, Margin = new(0, 4, 0, 8) };
    StartupDataPurgeReview? purgeReview;
    string? purgeBackup;
    Button? purgeExecute;
    TextBlock? purgeStatus;

    static bool IsPurgeState(string? state) => state is "Purging" or "PartiallyPurged" or "Purged";

    void InvalidatePurgeReview()
    {
        purgeReview = null;
        if (purgeExecute is not null) purgeExecute.IsEnabled = false;
        if (purgeStatus is not null) purgeStatus.Text = "条件已改变，请重新检查。";
    }

    void AddDataPurgeSection()
    {
        var body = new StackPanel();
        body.Children.Add(new TextBlock
        {
            Text = "删除本站的配置、数据库、图片、回包和日志，不可撤销。只有全部请求都已结清时才能删除；有未完成或结果未确认的请求时，请保留数据。" +
                "Windows 部署登记和安装回执会保留，供实施人员核对；组织备份和本机以外的副本不在删除范围内。普通删除不等于安全擦除。",
            TextWrapping = TextWrapping.Wrap
        });
        body.Children.Add(purgeBackupEnabled);
        purgeBackupEnabled.Click += (_, _) => InvalidatePurgeReview();
        purgeStatus = new TextBlock { Text = "尚未检查。", TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 8) };
        AutomationProperties.SetLiveSetting(purgeStatus, AutomationLiveSetting.Polite); body.Children.Add(purgeStatus);
        body.Children.Add(new TextBlock { Text = "确认时请输入“" + PurgeWord + "”：", TextWrapping = TextWrapping.Wrap });
        AutomationProperties.SetName(purgeConfirmation, "输入永久删除以确认"); body.Children.Add(purgeConfirmation);
        var buttons = new WrapPanel(); body.Children.Add(buttons);
        Button PurgeAction(string caption, Func<Task> action, bool readOnly = false)
        { var button = Action(caption, action, readOnly); Actions.Children.Remove(button); buttons.Children.Add(button); return button; }
        PurgeAction("检查能否永久删除", ReviewDataPurge, readOnly: true);
        purgeExecute = PurgeAction("永久删除本站数据…", PurgeStationData); purgeExecute.IsEnabled = false;
        Body.Children.Add(new Expander { Header = "永久删除本站数据（不可撤销）", Content = body, Margin = new(0, 16, 0, 8) });
    }

    async Task ReviewDataPurge()
    {
        if (UnknownOutcome) throw new InvalidOperationException("上次结果尚未核对，请先核对原操作。");
        InvalidatePurgeReview();
        var current = CurrentDeployment() ?? throw new InvalidDataException("找不到安装登记。");
        purgeBackup = purgeBackupEnabled.IsChecked == true ? Path.Combine(Path.GetFullPath(backupDirectory.Text), current.ApplicationId + "-pre-purge-" + Wire.Id() + ".db") : null;
        purgeReview = await RunMaintenance("检查本站记录、数据范围和备份位置", (progress, token) => ReviewPurge(RegistrationIdentity(), purgeBackup, progress, token), readOnly: true);
        var plan = purgeReview.Deployment;
        purgeStatus!.Text = plan.Reason +
            (plan.Allowed ? "\n删除范围：\n" + string.Join("\n", plan.Delete.Select(d => d.Root + " · " + (d.ReviewedFileCount ?? d.Files.Count) + " 个文件")) : "") +
            "\n保留：\n" + string.Join("\n", plan.Retained) + "\n备份：" + (purgeBackup ?? "本次不备份");
        purgeExecute!.IsEnabled = plan.Allowed && purgeReview.Business?.Allowed == true;
    }

    async Task PurgeStationData()
    {
        if (purgeReview is not { Deployment.Allowed: true, Business.Allowed: true } review) throw new InvalidOperationException("请先检查并审阅删除范围。");
        if (purgeConfirmation.Text.Trim() != PurgeWord) { purgeStatus!.Text = "请在确认框中输入“" + PurgeWord + "”后再删除。"; purgeConfirmation.Focus(); return; }
        var message = "将" + (purgeBackup is null ? "不备份，" : "先创建完整备份，再") + "永久删除以下目录中已列出的文件，不可撤销：\n" +
            string.Join("\n", review.Deployment.Delete.Select(d => d.Root)) + "\n是否继续？";
        var confirmed = ConfirmPurge?.Invoke(message) ?? MessageBox.Show(this, message, "确认永久删除", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
        if (!confirmed) return;
        purgeExecute!.IsEnabled = false; purgeConfirmation.Clear();
        ShowPurgeResult(await RunMaintenance("永久删除本站数据", (progress, token) => ExecutePurge(review, purgeBackup, progress, token)));
    }

    void ShowPurgeState(DeploymentLocation current)
    {
        removalCompleted = true; plan = null; registrationHash = null;
        Title = "菜鸟供包台 · 永久删除"; WorkflowHeading.Text = "永久删除";
        Body.Children.Clear(); Actions.Children.Clear(); SessionActions.Children.Clear();
        Message(current.LifecycleState == "Purged" ? "本站数据已永久删除。Windows 登记和安装回执保留，供实施人员核对后移除。"
            : "上一次永久删除尚未完成。本站保持“部分删除”，不能重新安装为空站；关闭占用文件的程序后可继续原次。");
        if (current.LifecycleState != "Purged" && PurgeOperation(current) is { } operation)
            Action("继续原次永久删除…", async () => ShowPurgeResult(await RunMaintenance("继续原次永久删除", (progress, token) => ResumePurge(operation, progress, token), canRequestCancellation: false)));
        CloseAction();
    }

    void ShowPurgeResult(DeploymentPurgeReceipt receipt)
    {
        var current = CurrentDeployment();
        if (current is not null) { ShowPurgeState(current); }
        if (receipt.Remaining.Length > 0)
            Body.Children.Add(new Expander
            {
                Header = "未删除的文件",
                Content = new TextBox { Text = string.Join("\n", receipt.Remaining), IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 220 }
            });
        Message(receipt.Detail);
    }

    static string? PurgeOperation(DeploymentLocation current)
    {
        var name = Path.GetFileNameWithoutExtension(current.MaintenanceOperation ?? "");
        var prefix = current.ApplicationId + ".purge-";
        return name.StartsWith(prefix, StringComparison.Ordinal) ? name[prefix.Length..] : null;
    }
}

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
    internal Func<string, string, bool, IProgress<MaintenanceProgress>?, CancellationToken, Task<StartupVersionPruneReview>> ReviewOldVersions { get; set; } = ParcelStartupServices.ReviewVersionPruningAsync;
    internal Func<StartupVersionPruneReview, string, IProgress<MaintenanceProgress>?, CancellationToken, Task<DeploymentPruneReceipt>> ExecuteOldVersions { get; set; } = ParcelStartupServices.PruneVersionsAsync;
    internal Func<string, bool, IProgress<MaintenanceProgress>?, CancellationToken, Task<DeploymentPruneReceipt>> InspectOldVersions { get; set; } = ParcelStartupServices.InspectVersionPruningAsync;
    internal Func<string?> LastOldVersions { get; set; } = () => WindowsDeploymentSetup.LastVersionPruning(ParcelStartupServices.ApplicationId);
    internal Func<string, IProgress<MaintenanceProgress>?, CancellationToken, Task<DeploymentPruneReceipt>> KeepOldVersionRemainder { get; set; } = ParcelStartupServices.KeepVersionPruningRemainderAsync;
    internal Func<string, bool>? ConfirmVersionPruning { get; set; }
    readonly CheckBox ordinaryStartupConfirmed = new()
    {
        Content = new TextBlock
        {
            Text = "我已用当前版本普通启动，并完成本次更新所需观察。此确认不代表设备或现场验收。",
            TextWrapping = TextWrapping.Wrap
        },
        Margin = new(0, 12, 0, 8)
    };
    StartupVersionPruneReview? pruneReview;
    string pruneBackup = "";
    string? pruneOperation;
    Button? pruneExecute, pruneResume, pruneKeep;
    TextBlock? pruneStatus;

    void InvalidatePruningReview()
    {
        pruneReview = null;
        if (pruneExecute is not null) pruneExecute.IsEnabled = false;
        if (pruneStatus is not null) pruneStatus.Text = "条件或备份位置已改变，请重新检查旧版本。";
    }

    void AddVersionPruningSection()
    {
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = "只清理核验过的旧程序和对应维护缓存。当前版本、上一版本及兼容回退候选保留；未决责任或归属不明时不删除。此操作不会删除业务数据。", TextWrapping = TextWrapping.Wrap });
        body.Children.Add(ordinaryStartupConfirmed);
        ordinaryStartupConfirmed.Click += (_, _) => InvalidatePruningReview();
        AutomationProperties.SetName(ordinaryStartupConfirmed, "确认当前版本已普通启动并完成必要观察");
        pruneStatus = new TextBlock { Text = "尚未检查。请先关闭业务软件。", TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 8) };
        AutomationProperties.SetName(pruneStatus, "旧版本清理说明"); body.Children.Add(pruneStatus);
        var buttons = new WrapPanel(); body.Children.Add(buttons);
        Button VersionAction(string caption, Func<Task> action, bool readOnly = false)
        { var button = Action(caption, action, readOnly); Actions.Children.Remove(button); buttons.Children.Add(button); return button; }
        VersionAction("检查旧版本", ReviewPruneVersions, readOnly: true);
        pruneExecute = VersionAction("备份并清理旧版本…", PruneVersions); pruneExecute.IsEnabled = false;
        VersionAction("核对上次版本清理", () => InspectPruneVersions(false), readOnly: true);
        pruneResume = VersionAction("继续原清单清理…", () => InspectPruneVersions(true), readOnly: true); pruneResume.IsEnabled = false;
        pruneKeep = VersionAction("保留余项，结束清理…", KeepPruneRemainder, readOnly: true); pruneKeep.IsEnabled = false;
        Body.Children.Add(new Expander { Header = "清理旧版本", Content = body, Margin = new(0, 16, 0, 8) });
    }

    async Task ReviewPruneVersions()
    {
        if (UnknownOutcome) throw new InvalidOperationException("上次结果尚未核对，请先核对原操作。");
        InvalidatePruningReview(); pruneResume!.IsEnabled = false; pruneKeep!.IsEnabled = false;
        if (ordinaryStartupConfirmed.IsChecked != true) { pruneStatus!.Text = "须先完成普通启动和必要观察，再明确勾选确认；不会由构建或模拟结果代替。"; return; }
        var current = CurrentDeployment() ?? throw new InvalidDataException("缺少原部署登记。");
        pruneBackup = Path.Combine(Path.GetFullPath(backupDirectory.Text), current.ApplicationId + "-pre-version-prune-" + Wire.Id() + ".db");
        pruneReview = await RunMaintenance("检查旧版本、原责任和备份位置", (progress, token) =>
            ReviewOldVersions(RegistrationIdentity(), pruneBackup, true, progress, token), readOnly: true);
        pruneOperation = pruneReview.Deployment.OperationId;
        var plan = pruneReview.Deployment;
        pruneStatus!.Text = plan.Reason + "\n保留：\n" + string.Join("\n", plan.Retain.Select(r => r.Root + " · " + r.Reason)) +
            "\n拟删除（只限清单内文件）：\n" + string.Join("\n", plan.Delete.Select(d => d.Root + " · " + (d.ReviewedFileCount ?? d.Files.Count) + " 个文件")) +
            (pruneReview.Business is { Allowed: false } business ? "\n" + (business.OperatorSummary ?? "当前业务记录检查未通过，请先按原记录处理。") : "") + "\n完整备份：" + pruneBackup;
        pruneExecute!.IsEnabled = plan.Allowed && pruneReview.Business?.Allowed == true;
    }

    async Task PruneVersions()
    {
        if (pruneReview is not { Deployment.Allowed: true, Business.Allowed: true } review || ordinaryStartupConfirmed.IsChecked != true)
            throw new InvalidOperationException("请先检查并审阅固定旧版本清理范围。");
        var message = "将先创建完整备份，再删除已列出的旧程序及对应维护缓存。业务数据、当前版本和回退候选保留。\n备份：" + pruneBackup + "\n是否继续？";
        var confirmed = ConfirmVersionPruning?.Invoke(message) ?? MessageBox.Show(this, message, "确认旧版本清理", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
        if (!confirmed) return;
        pruneExecute!.IsEnabled = false;
        ShowPruningReceipt(await RunMaintenance("备份并清理已审阅的旧版本", (progress, token) => ExecuteOldVersions(review, pruneBackup, progress, token)));
    }

    async Task InspectPruneVersions(bool resume)
    {
        pruneOperation ??= LastOldVersions();
        if (pruneOperation is null) { pruneStatus!.Text = "没有原版本清理记录。"; return; }
        ShowPruningReceipt(await RunMaintenance(resume ? "继续原版本清单；不扩大删除范围" : "只读核对原版本清理", (progress, token) =>
            InspectOldVersions(pruneOperation, resume, progress, token), readOnly: true, canRequestCancellation: !resume));
    }

    void ShowPruningReceipt(DeploymentPruneReceipt receipt)
    {
        pruneOperation = receipt.OperationId; pruneExecute!.IsEnabled = false;
        pruneResume!.IsEnabled = receipt.Status is "Prepared" or "Partial";
        pruneKeep!.IsEnabled = receipt.Status is "Prepared" or "Partial";
        pruneStatus!.Text = receipt.Detail + "\n保留或待清理项：\n" + string.Join("\n", receipt.Remaining);
        if (receipt.RemainingTruncated) pruneStatus.Text += "\n此处显示部分，共 " + receipt.TotalRemaining + " 项；完整清单保留在：" + receipt.OperationFile;
        if (receipt.Status is "Completed" or "CompletedWithRetainedFiles" or "RetainedUnverified" or "NotStarted" or "Partial" or "Prepared") ResolveKnownMaintenanceOutcome();
        pruneReview = null; Message(receipt.Detail);
    }

    async Task KeepPruneRemainder()
    {
        if (pruneOperation is null || pruneKeep?.IsEnabled != true) throw new InvalidOperationException("请先核对原版本清理。");
        const string message = "将结束原次清理，不再删除余项。原范围的未确认文件保留，并从以后的清理候选中排除；不代表全部清理完成。是否保留余项？";
        var confirmed = ConfirmVersionPruning?.Invoke(message) ?? MessageBox.Show(this, message, "确认保留余项", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
        if (!confirmed) return;
        pruneKeep.IsEnabled = false;
        ShowPruningReceipt(await RunMaintenance("保留原范围余项，只记录处置", (progress, token) => KeepOldVersionRemainder(pruneOperation, progress, token), readOnly: true));
    }
}

using System.IO;
using System.Windows;
using System.Windows.Controls;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Assembly;
using Ipc.Platform;

namespace Ipc.Desktop;

sealed partial class ProductMaintenanceWindow : StartupWorkflowWindow
{
    internal Func<DeploymentLocation?> CurrentDeployment { get; set; } = () => WindowsDeploymentSetup.Current(ParcelStartupServices.ApplicationId);
    internal bool AutomaticRead { get; set; } = true;
    internal Func<string> RegistrationIdentity { get; set; } = () => Wire.FileHash(WindowsDeploymentSetup.Registration(ParcelStartupServices.ApplicationId));
    internal Func<WindowsRemovalReceipt> InspectRemoval { get; set; } = () => WindowsDeploymentSetup.InspectRemoval(ParcelStartupServices.ApplicationId);
    internal Func<DeploymentMedia, Task<WindowsRemovalReceipt>> ContinueRemoval { get; set; } = media =>
        ParcelStartupServices.ResumeRemovalAsync(media, Wire.FileHash(WindowsDeploymentSetup.Registration(ParcelStartupServices.ApplicationId)));
    StartupChangePreview? plan;
    DeploymentLocation? location;
    string? registrationHash;
    readonly System.Windows.Controls.TextBox backupDirectory;
    string backupFile = "";
    readonly CheckBox backupEnabled = new() { Content = "卸载前创建完整备份（推荐）", IsChecked = true, Margin = new(0, 8, 0, 8) };
    bool removalCompleted;
    string operationLabel = "读取卸载记录";
    readonly DeploymentMedia? cleanupMedia;
    internal ProductMaintenanceWindow(WindowsRemovalReceipt completed, string retainedDataRoot) : this()
        => ShowRemovalResult(completed);
    internal ProductMaintenanceWindow(DeploymentMedia media) : this() => cleanupMedia = media;
    public ProductMaintenanceWindow() : base("菜鸟供包台 · 修复与卸载")
    {
        WorkflowHeading.Text = "修复与卸载";
        Body.Children.Add(backupEnabled);
        backupEnabled.Click += (_, _) => { plan = null; };
        var personal = WindowsStoragePaths.UserRoot;
        var defaultBackup = Path.GetPathRoot(personal)?.Equals(@"D:\", StringComparison.OrdinalIgnoreCase) == true ? Path.Combine(personal, "backups") : @"D:\ProgramData\ImporterPc\backups";
        backupDirectory = Folder("完整备份目录（卸载会保留业务数据、配置和设备身份）", defaultBackup);
        backupDirectory.TextChanged += (_, _) =>
        {
            plan = null; backupFile = "";
            InvalidateRollbackReview();
            if (!removalCompleted) Message("备份目录已更改，请重新点击“读取安装信息”。");
        };
        Note("此窗口不会启动供包或设备。修复程序需要使用完整的原安装包；开始菜单和快捷方式可直接修复。卸载不会删除共享驱动和业务数据。");
        Action("读取安装信息", Read, readOnly: true);
        Action("查看上次卸载结果", InspectOriginal, readOnly: true);
        var resume = Action("继续上次卸载清理…", ResumeOriginal, readOnly: true);
        resume.Visibility = CurrentDeployment()?.LifecycleState is { } lifecycle && lifecycle != "Installed" ? Visibility.Visible : Visibility.Collapsed;
        Action("修复开始菜单与快捷方式…", async () =>
        {
            if (removalCompleted) return;
            if (registrationHash is null) throw new InvalidOperationException("请先读取安装信息");
            await RunMaintenance("修复开始菜单与快捷方式\n程序、配置和业务数据不变。",(progress,token)=>ParcelStartupServices.RepairRegistrationAsync(registrationHash,null,progress,token)); Message("开始菜单与快捷方式已修复，配置和数据未改动。");
        });
        Action("完整备份并卸载程序…", async () =>
        {
            if (removalCompleted) return;
            if (plan is null || location is null || registrationHash is null || !plan.Allowed) throw new InvalidOperationException("请先读取安装信息并检查备份位置");
            if (MessageBox.Show(this, (backupEnabled.IsChecked == true ? "将先创建完整备份。" : "本次不创建备份，若数据损坏可能无法恢复。") + "卸载程序后业务数据、设备身份、配置和未决记录全部保留。", "确认保留数据卸载", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
            var receipt = await RunMaintenance("完整备份并卸载程序\n程序："+location.ReleaseRoot+"\n保留数据："+location.DataRoot+"\n备份："+backupFile,
                (progress,token)=>ParcelStartupServices.RemoveAsync(registrationHash,plan.Identity,backupFile,progress,token));
            ShowRemovalResult(receipt);
        });
        AddRollbackSection();
        CloseAction();
        Loaded += async (_, _) => { if (AutomaticRead) await Run(Read); };
    }
    async Task Read()
    {
        if (removalCompleted) return;
        location = CurrentDeployment() ?? throw new InvalidDataException("未找到安装登记，请用完整安装包重新安装");
        if (location.LifecycleState != "Installed") { ShowRemovalResult(await Task.Run(InspectRemoval)); return; }
        registrationHash = RegistrationIdentity();
        if (UnknownOutcome) { Message("上次卸载尚未完成，请稍后查看结果，勿重复卸载。"); return; }
        backupFile = backupEnabled.IsChecked == true ? Path.Combine(Path.GetFullPath(backupDirectory.Text), location.ApplicationId + "-pre-remove-" + Wire.Id() + ".db") : "";
        plan = await RunMaintenance("卸载前检查\n数据：" + location.DataRoot + "\n备份：" + (backupFile.Length == 0 ? "本次不备份" : backupFile),
            (progress, token) => ParcelStartupServices.ReviewRemovalAsync(registrationHash, backupFile, progress, token), readOnly: true);
        Message("产品 " + location.ApplicationId + " · 源码 " + location.SourceCommit + "\n程序 " + location.ReleaseRoot + "\n数据 " + location.DataRoot +
            "\n备份 " + backupFile + "\n" + (plan.Allowed ? "检查通过，可以卸载；执行时会再次检查。" : "当前仍有执行者或无法安全停止，请关闭软件后重新检查。未决记录不会因卸载清偿。") + "\n" + plan.Summary);
    }
    async Task InspectOriginal()
    {
        operationLabel = "卸载检查";
        Message("正在检查卸载记录和剩余文件…");
        var observation = await Task.Run(() => (Location: CurrentDeployment(), Receipt: InspectRemoval()));
        var receipt = observation.Receipt;
        if (receipt.Status == "Installed") { Message("安装登记仍为“已安装”，无法确认卸载是否已执行。请稍后再查看，勿重复卸载。"); return; }
        location = observation.Location;
        ResolveKnownMaintenanceOutcome(); ShowRemovalResult(receipt);
        Message("检查完成（" + DateTimeOffset.Now.ToString("HH:mm:ss") + "）。" + Status.Text);
    }
    async Task ResumeOriginal()
    {
        operationLabel = "继续清理";
        if (UnknownOutcome) { Message("上次清理结果未确认，请先查看结果。"); return; }
        var current = CurrentDeployment() ?? throw new InvalidDataException("找不到安装登记");
        if (current.LifecycleState == "Installed") throw new InvalidOperationException("软件尚未卸载，不能直接清理");
        Message("正在按原安装库存继续清理，业务数据保留。");
        var result = await RunMaintenance("继续清理原程序清单", async (progress, _) =>
        { progress.Report(new(MaintenanceStage.RemovingProgram, null, null, false)); return await ParcelStartupServices.ResumeRemovalAsync(Wire.FileHash(WindowsDeploymentSetup.Registration(current.ApplicationId))); }, canRequestCancellation: false);
        location = current;
        ShowRemovalResult(result);
    }
    void ShowRemovalResult(WindowsRemovalReceipt receipt)
    {
        removalCompleted = true; plan = null; registrationHash = null;
        Title = "菜鸟供包台 · 卸载结果"; WorkflowHeading.Text = "卸载结果";
        Body.Children.Clear(); Actions.Children.Clear(); SessionActions.Children.Clear();
        var message = receipt.Status switch
        {
            "RemovedDataRetained" => "程序文件已清理，业务数据已保留。",
            "RestartRequired" => "软件已卸载。部分被占用的文件将在重启电脑后删除，请重启后再查看。",
            "RemovedWithRetainedFiles" => "软件已卸载，但仍有剩余文件未清理。",
            "RetiredCleanupPending" => "软件已卸载，程序文件清理尚未完成。",
            _ => "卸载结果尚未完全确认，请保留卸载记录。"
        };
        Message(message);
        var details = new TextBox { Text = "正常保留：" + string.Join("；", receipt.Retained) + "\n卸载结果：" + receipt.OperationFile + "\n程序目录：" + receipt.ReleaseRoot + "\n剩余文件/目录：\n" + string.Join("\n", receipt.RemainingFiles ?? []), IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 220 };
        Body.Children.Add(new Expander { Header = "卸载记录与剩余文件详情", Content = details });
        Note("业务数据、设备身份和共享驱动保留。可按原安装库存继续清理，无需原安装包。");
        Action("查看上次卸载结果", InspectOriginal, readOnly: true);
        if (receipt.Status != "RemovedDataRetained") Action("继续清理…", ResumeOriginal, readOnly: true);
        CloseAction();
    }
    void CloseAction()
    {
        // Closing is window navigation, not a maintenance operation. In-flight
        // work is still protected by the shared Closing/Busy guard.
        var close = new Button { Content = "关闭", IsCancel = true, MinHeight = 44, MinWidth = 100, Margin = new(0, 12, 10, 0), Padding = new(12, 6, 12, 6) };
        close.Click += (_, _) => Close();
        Actions.Children.Add(close);
    }
    protected override void OperationFailed(Exception error)
    {
        base.OperationFailed(error);
        if (removalCompleted && !UnknownOutcome)
            Status.Text = operationLabel + "未完成：" + error.Message + "\n卸载记录已保留，可重新查看或关闭。";
    }
}

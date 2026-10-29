using System.IO;
using System.Windows;
using System.Windows.Controls;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Assembly;
using Ipc.Platform;

namespace Ipc.Desktop;

sealed class ProductMaintenanceWindow : StartupWorkflowWindow
{
    internal Func<DeploymentLocation?> CurrentDeployment { get; set; } = () => WindowsDeploymentSetup.Current(ParcelStartupServices.ApplicationId);
    internal Func<WindowsRemovalReceipt> InspectRemoval { get; set; } = () => WindowsDeploymentSetup.InspectRemoval(ParcelStartupServices.ApplicationId);
    internal Func<DeploymentMedia, Task<WindowsRemovalReceipt>> ContinueRemoval { get; set; } = media =>
        ParcelStartupServices.ResumeRemovalAsync(media, Wire.FileHash(WindowsDeploymentSetup.Registration(ParcelStartupServices.ApplicationId)));
    StartupChangePreview? plan;
    DeploymentLocation? location;
    string? registrationHash;
    readonly System.Windows.Controls.TextBox backupDirectory;
    string backupFile = "";
    bool removalCompleted;
    string operationLabel = "读取卸载记录";
    readonly DeploymentMedia? cleanupMedia;
    internal ProductMaintenanceWindow(WindowsRemovalReceipt completed, string retainedDataRoot) : this()
        => ShowRemovalResult(completed);
    internal ProductMaintenanceWindow(DeploymentMedia media) : this() => cleanupMedia = media;
    public ProductMaintenanceWindow() : base("菜鸟供包台 · 产品修复与卸载")
    {
        backupDirectory = Folder("完整备份目录（卸载保留业务数据、身份、配置账、原件和扩展）", Path.Combine(WindowsStoragePaths.UserRoot, "backups"));
        backupDirectory.TextChanged += (_, _) =>
        {
            plan = null; backupFile = "";
            if (!removalCompleted) Message("备份目录已改变，原计划已失效。请重新读取安装与原责任。");
        };
        Note("此入口不启动业务或设备。程序修复使用完整可信介质并排安装；系统登记与快捷方式可从唯一部署登记重建。普通卸载不会删除其他产品共享驱动或数据。");
        Action("读取安装与原责任", Read, readOnly: true);
        Action("只读核对原卸载结果", InspectOriginal, readOnly: true);
        Action("从同源介质继续原清单…", ResumeOriginal, readOnly: true);
        Action("修复系统登记与快捷方式…", async () =>
        {
            if (removalCompleted) return;
            if (registrationHash is null) throw new InvalidOperationException("先读取原部署");
            await RunMaintenance("修复原安装系统入口\n保留原程序、配置和业务数据。",(progress,token)=>ParcelStartupServices.RepairRegistrationAsync(registrationHash,null,progress,token)); Message("系统安装信息已按原部署修复；未重置配置或数据。");
        });
        Action("完整备份并卸载程序…", async () =>
        {
            if (removalCompleted) return;
            if (plan is null || location is null || registrationHash is null || !plan.Allowed) throw new InvalidOperationException("先检查原责任和完整备份计划");
            if (MessageBox.Show(this, "将先复核并创建完整备份，再移除本安装的程序、快捷方式及系统项。原数据、机级身份、扩展及证据全部保留。被占用程序可能要求重启；旧EXE不能用来回读未来数据库。", "确认保留数据卸载", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
            var receipt = await RunMaintenance("完整备份并卸载程序\n程序："+location.ReleaseRoot+"\n保留数据："+location.DataRoot+"\n备份："+backupFile,
                (progress,token)=>ParcelStartupServices.RemoveAsync(registrationHash,plan.Identity,backupFile,progress,token));
            ShowRemovalResult(receipt);
        });
        CloseAction();
        Loaded += async (_, _) => await Run(Read);
    }
    async Task Read()
    {
        if (removalCompleted) return;
        location = CurrentDeployment() ?? throw new InvalidDataException("未找到原受保护部署登记；使用可信完整介质重新安装，不能猜测原目录");
        if (location.LifecycleState != "Installed") { ShowRemovalResult(await Task.Run(InspectRemoval)); return; }
        registrationHash = Wire.FileHash(WindowsDeploymentSetup.Registration(location.ApplicationId));
        if (UnknownOutcome) { Message("原操作尚未取得退役终态，请稍后只读核对。禁止重新提交卸载。"); return; }
        var launch = new BusinessLaunch(location.ConfigurationDirectory, location.DataRoot, null, null);
        var management = ParcelStartupServices.ManagementMode(launch);
        if (!(await management.ManagementModeStatusAsync()).Authorized)
            new ManagementModeWindow(management) { Owner = this }.ShowDialog();
        if (!(await management.ManagementModeStatusAsync()).Authorized)
        { Message("尚未进入管理模式，卸载没有执行；原配置和数据保留。"); return; }
        Directory.CreateDirectory(backupDirectory.Text);
        backupFile = Path.Combine(Path.GetFullPath(backupDirectory.Text), location.ApplicationId + "-pre-remove-" + Wire.Id() + ".db");
        var service = ParcelStartupServices.Maintenance(launch);
        plan = await RunMaintenance("只读核对卸载责任\n原数据："+location.DataRoot+"\n计划备份："+backupFile,(progress,token)=>service.PlanChangeAsync("Uninstall",backupFile,progress,token),readOnly:true);
        Message("产品 " + location.ApplicationId + " · 源码 " + location.SourceCommit + "\n程序 " + location.ReleaseRoot + "\n数据 " + location.DataRoot +
            "\n备份 " + backupFile + "\n" + (plan.Allowed ? "原责任检查允许进入维护，执行时再次复核。" : "原责任阻止移除，请使用匹配发布完成核对。") + "\n" + plan.Summary);
    }
    async Task InspectOriginal()
    {
        operationLabel = "卸载核对";
        Message("正在核对原卸载记录与磁盘剩余文件…");
        var observation = await Task.Run(() => (Location: CurrentDeployment(), Receipt: InspectRemoval()));
        var receipt = observation.Receipt;
        if (receipt.Status == "Installed") { Message("原登记仍为已安装，尚不能证明原卸载未提交。请稍后核对；不重复提交。"); return; }
        location = observation.Location;
        ResolveKnownMaintenanceOutcome(); ShowRemovalResult(receipt);
        Message("核对完成（" + DateTimeOffset.Now.ToString("HH:mm:ss") + "）。" + Status.Text);
    }
    async Task ResumeOriginal()
    {
        operationLabel = "继续清理";
        if (UnknownOutcome) { Message("原清理结果待确认，请先只读核对。"); return; }
        var current = CurrentDeployment() ?? throw new InvalidDataException("原部署登记缺失");
        if (current.LifecycleState == "Installed") throw new InvalidOperationException("尚未退役；不能绕过原责任检查执行清理");
        var media = cleanupMedia ?? throw new InvalidOperationException("请从卸载前同源完整安装介质进入‘核对并继续原卸载清理’，无需恢复已移除的认证服务。");
        if (media.SourceCommit != current.SourceCommit) throw new InvalidOperationException("当前安装包与原安装来源不同。请关闭此窗口，打开原版完整安装包后继续清理。");
        Message("正在继续原固定清单；不重新提交卸载、不删除数据或清单外文件。");
        var result = await RunMaintenance("继续原固定清单清理\n原程序："+current.ReleaseRoot+"\n保留数据："+current.DataRoot+"\n安装已经退役，不撤销退役或删除清单外文件。",async(progress,_)=>
        {progress.Report(new(MaintenanceStage.RemovingProgram,null,null,false));return await ContinueRemoval(media);},canRequestCancellation:false);
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
            "RemovedDataRetained" => "程序清单已清理，原数据正常保留。",
            "RestartRequired" => "原安装已退役。部分占用文件已登记重启清理，请重新启动 Windows 后核对。",
            "RemovedWithRetainedFiles" => "原安装已退役，仍有残件或清单外文件；未报告完整清理成功。",
            "RetiredCleanupPending" => "原安装已退役，固定清单清理尚无完成回执。",
            _ => "原卸载终态尚未完整核对，请保留原记录。"
        };
        Message(message);
        var details = new TextBox { Text = "正常保留：" + string.Join("；", receipt.Retained) + "\n卸载回执：" + receipt.OperationFile + "\n原程序目录：" + receipt.ReleaseRoot + "\n剩余文件/目录：\n" + string.Join("\n", receipt.RemainingFiles ?? []), IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 220 };
        Body.Children.Add(details);
        Note("不手动递归删除原目录。清单外文件、原身份、业务数据、扩展和共享驱动保留。继续清理仅可使用同源完整介质。");
        Action("只读核对原卸载结果", InspectOriginal, readOnly: true);
        if (cleanupMedia is not null && receipt.Status != "RemovedDataRetained")
        {
            var matches = location is not null && location.SourceCommit == cleanupMedia.SourceCommit;
            if (!matches) Note("当前安装包与原安装来源不一致，不能继续清理。请关闭此窗口，打开原版完整安装包。\n原安装源码：" + (location?.SourceCommit ?? "尚未核对") + "\n当前安装包源码：" + cleanupMedia.SourceCommit);
            Action("继续原清单清理…", ResumeOriginal, readOnly: true).IsEnabled = matches;
        }
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
            Status.Text = operationLabel + "未完成：" + error.Message + "\n原卸载记录已保留，可重新核对或关闭。";
    }
}

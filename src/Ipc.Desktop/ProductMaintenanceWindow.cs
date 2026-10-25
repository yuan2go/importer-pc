using System.IO;
using System.Windows;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Assembly;
using Ipc.Platform;

namespace Ipc.Desktop;

sealed class ProductMaintenanceWindow : StartupWorkflowWindow
{
    StartupChangePreview? plan;
    DeploymentLocation? location;
    string? registrationHash;
    readonly System.Windows.Controls.TextBox backupDirectory;
    string backupFile = "";
    bool removalCompleted;
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
            await Task.Run(() => ParcelStartupServices.RepairRegistrationAsync(registrationHash)); Message("系统安装信息已按原部署修复；未重置配置或数据。");
        });
        Action("完整备份并卸载程序…", async () =>
        {
            if (removalCompleted) return;
            if (plan is null || location is null || registrationHash is null || !plan.Allowed) throw new InvalidOperationException("先检查原责任和完整备份计划");
            if (MessageBox.Show(this, "将先复核并创建完整备份，再移除本安装的程序、快捷方式及系统项。原数据、机级身份、扩展及证据全部保留。被占用程序可能要求重启；旧EXE不能用来回读未来数据库。", "确认保留数据卸载", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
            var receipt = await Task.Run(() => ParcelStartupServices.RemoveAsync(registrationHash, plan.Identity, backupFile));
            ShowRemovalResult(receipt);
        });
        Action("关闭", () => { Busy = false; Close(); return Task.CompletedTask; }, readOnly: true);
        Loaded += async (_, _) => await Run(Read);
    }
    async Task Read()
    {
        if (removalCompleted) return;
        location = WindowsDeploymentSetup.Current(ParcelStartupServices.ApplicationId) ?? throw new InvalidDataException("未找到原受保护部署登记；使用可信完整介质重新安装，不能猜测原目录");
        registrationHash = Wire.FileHash(WindowsDeploymentSetup.Registration(location.ApplicationId));
        if (location.LifecycleState != "Installed") { ShowRemovalResult(WindowsDeploymentSetup.InspectRemoval(location.ApplicationId)); return; }
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
        plan = await Task.Run(() => service.PlanChangeAsync("Uninstall", backupFile));
        Message("产品 " + location.ApplicationId + " · 源码 " + location.SourceCommit + "\n程序 " + location.ReleaseRoot + "\n数据 " + location.DataRoot +
            "\n备份 " + backupFile + "\n" + (plan.Allowed ? "原责任检查允许进入维护，执行时再次复核。" : "原责任阻止移除，请使用匹配发布完成核对。") + "\n" + plan.Summary);
    }
    async Task InspectOriginal()
    {
        var receipt = await Task.Run(() => WindowsDeploymentSetup.InspectRemoval(ParcelStartupServices.ApplicationId));
        if (receipt.Status == "Installed") { Message("原登记仍为已安装，尚不能证明原卸载未提交。请稍后核对；不重复提交。"); return; }
        UnknownOutcome = false; ShowRemovalResult(receipt);
    }
    async Task ResumeOriginal()
    {
        if (UnknownOutcome) { Message("原清理结果待确认，请先只读核对。"); return; }
        var current = WindowsDeploymentSetup.Current(ParcelStartupServices.ApplicationId) ?? throw new InvalidDataException("原部署登记缺失");
        if (current.LifecycleState == "Installed") throw new InvalidOperationException("尚未退役；不能绕过原责任检查执行清理");
        var media = cleanupMedia ?? throw new InvalidOperationException("请从卸载前同源完整安装介质进入‘核对并继续原卸载清理’，无需恢复已移除的认证服务。");
        var hash = Wire.FileHash(WindowsDeploymentSetup.Registration(current.ApplicationId));
        Message("正在继续原固定清单；不重新提交卸载、不删除数据或清单外文件。");
        var result = await Task.Run(() => ParcelStartupServices.ResumeRemovalAsync(media, hash));
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
        Message(message + "\n正常保留：" + string.Join("；", receipt.Retained) + "\n卸载回执：" + receipt.OperationFile);
        var details = new System.Windows.Controls.TextBox { Text = "原程序目录：" + receipt.ReleaseRoot + "\n剩余文件/目录：\n" + string.Join("\n", receipt.RemainingFiles ?? []), IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto, MaxHeight = 220 };
        Body.Children.Add(details);
        Note("不手动递归删除原目录。清单外文件、原身份、业务数据、扩展和共享驱动保留。继续清理仅可使用同源完整介质。");
        Action("只读核对原卸载结果", InspectOriginal, readOnly: true);
        if (cleanupMedia is not null) Action("继续原清单清理…", ResumeOriginal, readOnly: true);
        Action("关闭", () => { Busy = false; Close(); return Task.CompletedTask; }, readOnly: true);
    }
}

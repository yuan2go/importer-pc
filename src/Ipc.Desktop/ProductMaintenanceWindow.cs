using System.IO;
using System.Windows;
using Ipc.Application.Contracts;
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
    internal ProductMaintenanceWindow(WindowsRemovalReceipt completed, string retainedDataRoot) : this()
        => ShowRemovalResult(completed.RestartRequired, completed.OperationFile, retainedDataRoot);
    public ProductMaintenanceWindow() : base("菜鸟供包台 · 产品修复与卸载")
    {
        backupDirectory = Folder("完整备份目录（卸载保留业务数据、身份、配置账、原件和扩展）", Path.Combine(WindowsStoragePaths.UserRoot, "backups"));
        Note("此入口不启动业务或设备。程序修复使用完整可信介质并排安装；系统登记与快捷方式可从唯一部署登记重建。普通卸载不会删除其他产品共享驱动或数据。");
        Action("读取安装与原责任", Read);
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
            ShowRemovalResult(receipt.RestartRequired, receipt.OperationFile, location.DataRoot);
        });
        Action("关闭", () => { Busy = false; Close(); return Task.CompletedTask; });
        Loaded += async (_, _) => await Run(Read);
    }
    async Task Read()
    {
        if (removalCompleted) return;
        location = WindowsDeploymentSetup.Current(ParcelStartupServices.ApplicationId) ?? throw new InvalidDataException("未找到原受保护部署登记；使用可信完整介质重新安装，不能猜测原目录");
        registrationHash = Wire.FileHash(WindowsDeploymentSetup.Registration(location.ApplicationId));
        if (location.LifecycleState != "Installed") { ShowRemovalResult(null, location.MaintenanceOperation ?? "", location.DataRoot); return; }
        Directory.CreateDirectory(backupDirectory.Text);
        backupFile = Path.Combine(Path.GetFullPath(backupDirectory.Text), location.ApplicationId + "-pre-remove-" + Wire.Id() + ".db");
        var service = ParcelStartupServices.Maintenance(new(location.ConfigurationDirectory, location.DataRoot, null, null));
        plan = await Task.Run(() => service.PlanChangeAsync("Uninstall", backupFile));
        Message("产品 " + location.ApplicationId + " · 源码 " + location.SourceCommit + "\n程序 " + location.ReleaseRoot + "\n数据 " + location.DataRoot +
            "\n备份 " + backupFile + "\n" + (plan.Allowed ? "原责任检查允许进入维护，执行时再次复核。" : "原责任阻止移除，请使用匹配发布完成核对。") + "\n" + plan.Summary);
    }
    void ShowRemovalResult(bool? restartRequired, string operationFile, string retainedDataRoot)
    {
        removalCompleted = true; plan = null; registrationHash = null;
        Title = "菜鸟供包台 · 卸载结果";
        WorkflowHeading.Text = "卸载结果";
        Body.Children.Clear(); Actions.Children.Clear(); SessionActions.Children.Clear();
        Message("菜鸟供包台已卸载。" + (restartRequired == true ? "请重新启动 Windows 完成占用文件清理。" : restartRequired == false ? "卸载操作已完成。" : "请按原卸载回执完成剩余文件清理。") +
            "\n原配置和业务数据已保留：" + retainedDataRoot + "\n卸载回执：" + operationFile);
        Action("关闭", () => { Busy = false; Close(); return Task.CompletedTask; });
    }
}

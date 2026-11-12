using System.Windows;
using System.Windows.Controls;
using Ipc.Platform;

namespace Ipc.Desktop;

// Host-owned offline routing: business runtime is stopped before this window is
// created. Every scenario reuses the existing authorized maintenance contracts.
public sealed class MaintenanceCenterWindow : Window
{
    public BusinessLaunch SelectedLaunch { get; private set; }
    public bool RetryRequested { get; private set; }
    public bool UninstallLaunched { get; private set; }
    internal Func<uint> LaunchUninstaller { get; set; } = () => WindowsDeploymentSetup.LaunchUninstaller(Ipc.Assembly.ParcelStartupServices.ApplicationId);
    public MaintenanceCenterWindow(BusinessLaunch launch)
    {
        SelectedLaunch = launch; Title = "维护中心 · 菜鸟供包台"; Width = 820; Height = 620; MinWidth = 620; MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; FontFamily = Ipc.Presentation.Wpf.UiTypography.Interface;
        FontSize = Ipc.Presentation.Wpf.UiTypography.Body;
        var body = new StackPanel { Margin = new(28) }; Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        body.Children.Add(new TextBlock { Text = "选择维护任务", FontSize = 24, Margin = new(0, 0, 0, 16) });
        body.Children.Add(new TextBlock { Text = "软件已停止。每项任务执行前核验所需管理权限、所有权及证据；任务取消或失败时，先核对原结果，不要直接重做。", TextWrapping = TextWrapping.Wrap });
        var feedback = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 0) }; body.Children.Add(feedback);
        Add("软件打不开：检查配置与启动", () => Repair(false));
        Add("恢复本站备份／上一部署回滚", () => Repair(true));
        if (Ipc.Assembly.ParcelStartupServices.ApplicationId == "sportmaster")
        {
            Add("清理旧程序构建", () => new StartupVersionPruneWindow(Ipc.Assembly.ParcelStartupServices.ManagementMode(SelectedLaunch)) { Owner = this }.ShowDialog());
            Add("历史维护结果与原清单余项", () => new MaintenanceHistoryWindow() { Owner = this }.ShowDialog());
        }
        Add("清理过期记录", () =>
        {
            var service = (Ipc.Application.Contracts.IStartupRetentionMaintenance)Ipc.Assembly.ParcelStartupServices.Maintenance(SelectedLaunch);
            new HistoryRetentionWindow(service, Ipc.Assembly.ParcelStartupServices.ManagementMode(SelectedLaunch)) { Owner = this }.ShowDialog();
        });
        var uninstall = new Button { Content = "卸载程序", MinHeight = 48, Margin = new(0, 12, 0, 0), HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new(16, 4, 16, 4) };
        System.Windows.Automation.AutomationProperties.SetAutomationId(uninstall, "Maintenance.Uninstall");
        var handingOff = false;
        Closing += (_, e) => { if (handingOff) e.Cancel = true; };
        uninstall.Click += async (_, _) =>
        {
            if (handingOff) return;
            handingOff = true; body.IsEnabled = false; feedback.Text = "正在核验独立卸载程序…";
            try
            {
                await Task.Run(LaunchUninstaller);
                UninstallLaunched = true; handingOff = false; Close();
            }
            catch (Exception error) { feedback.Text = "卸载入口未打开：" + error.Message; }
            finally { handingOff = false; body.IsEnabled = true; }
        };
        body.Children.Add(uninstall);
        Add("检查后启动软件", () => { RetryRequested = true; Close(); });
        var expert = new Expander { Header = "专家模式（供应商指导）", Margin = new(0, 16, 0, 0) };
        var advanced = new Button { Content = "打开启动修复全部操作…", MinHeight = 48 };
        advanced.Click += (_, _) => { try { Repair(false, true); } catch (Exception error) { feedback.Text = "启动修复未打开：" + error.Message; } };
        expert.Content = advanced; body.Children.Add(expert);
        void Add(string caption, Action action)
        {
            var button = new Button { Content = caption, MinHeight = 48, Margin = new(0, 12, 0, 0), HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new(16, 4, 16, 4) };
            button.Click += (_, _) => { try { action(); } catch (Exception error) { feedback.Text = "维护入口未完成：" + error.Message + "。数据未删除，请先查看上次操作结果。"; } }; body.Children.Add(button);
        }
    }
    void Repair(bool recovery, bool expert = false)
    {
        var window = new StartupRepairWindow(SelectedLaunch) { Owner = this, OpenRecoveryScenario = recovery, SceneOnly = !expert };
        window.ShowDialog(); SelectedLaunch = window.SelectedLaunch;
        if (window.RestoreAdopted) { Close(); return; }
        if (window.RetryRequested) { RetryRequested = true; Close(); }
    }
}

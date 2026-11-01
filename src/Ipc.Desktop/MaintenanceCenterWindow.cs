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
    public MaintenanceCenterWindow(BusinessLaunch launch)
    {
        SelectedLaunch = launch; Title = "维护中心 · 菜鸟供包台"; Width = 820; Height = 620; MinWidth = 620; MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; FontFamily = Ipc.Presentation.Wpf.UiTypography.Interface;
        FontSize = Ipc.Presentation.Wpf.UiTypography.Body;
        var body = new StackPanel { Margin = new(28) }; Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        body.Children.Add(new TextBlock { Text = "选择维护任务", FontSize = 24, Margin = new(0, 0, 0, 16) });
        body.Children.Add(new TextBlock { Text = "原业务实例已停止。下面每个任务仍核验管理权限、安装归属、完整备份及原请求责任；取消或失败不表示操作未发生，请核对原结果。", TextWrapping = TextWrapping.Wrap });
        var feedback = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 0) }; body.Children.Add(feedback);
        Add("软件打不开：检查配置与启动", () => Repair(false));
        Add("恢复备份／迁移到新电脑", () => Repair(true));
        Add("清理到期已结历史", () =>
        {
            var service = (Ipc.Application.Contracts.IStartupRetentionMaintenance)Ipc.Assembly.ParcelStartupServices.Maintenance(SelectedLaunch);
            new HistoryRetentionWindow(service, Ipc.Assembly.ParcelStartupServices.ManagementMode(SelectedLaunch)) { Owner = this }.ShowDialog();
        });
        Add("修复安装／卸载程序", () => new ProductMaintenanceWindow { Owner = this }.ShowDialog());
        Add("核对后启动软件", () => { RetryRequested = true; Close(); });
        Add("关闭", Close);
        void Add(string caption, Action action)
        {
            var button = new Button { Content = caption, MinHeight = 48, Margin = new(0, 12, 0, 0), HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new(16, 4, 16, 4) };
            button.Click += (_, _) => { try { action(); } catch (Exception error) { feedback.Text = "维护入口未完成：" + error.Message + "。请保留当前数据并核对原操作结果。"; } }; body.Children.Add(button);
        }
    }
    void Repair(bool recovery)
    {
        var window = new StartupRepairWindow(SelectedLaunch) { Owner = this, OpenRecoveryScenario = recovery };
        window.ShowDialog(); SelectedLaunch = window.SelectedLaunch;
        if (window.RetryRequested) { RetryRequested = true; Close(); }
    }
}

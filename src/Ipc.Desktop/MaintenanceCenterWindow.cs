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
        body.Children.Add(new TextBlock { Text = "软件已停止。每项任务执行前都会检查管理权限和备份；任务取消或失败时，请先查看结果，不要直接重做。", TextWrapping = TextWrapping.Wrap });
        var feedback = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 0) }; body.Children.Add(feedback);
        Add("软件打不开：检查配置与启动", () => Repair(false));
        Add("恢复备份／迁移到新电脑", () => Repair(true));
        Add("清理过期记录", () =>
        {
            var service = (Ipc.Application.Contracts.IStartupRetentionMaintenance)Ipc.Assembly.ParcelStartupServices.Maintenance(SelectedLaunch);
            new HistoryRetentionWindow(service, Ipc.Assembly.ParcelStartupServices.ManagementMode(SelectedLaunch)) { Owner = this }.ShowDialog();
        });
        Add("卸载程序", () => new ProductMaintenanceWindow { Owner = this }.ShowDialog());
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
        if (window.RetryRequested) { RetryRequested = true; Close(); }
    }
}

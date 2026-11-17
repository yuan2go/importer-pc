using System.Diagnostics;
using System.Windows.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Ipc.Platform;

namespace Ipc.Desktop;

public abstract partial class StartupWorkflowWindow
{
    readonly Border maintenancePanel = new Border { Visibility = Visibility.Collapsed, Margin = new(0, 8, 0, 8), Padding = new(8), BorderThickness = new(1) }.Themed(Border.BorderBrushProperty, "Line").Themed(Border.BackgroundProperty, "Surface");
    readonly TextBox maintenanceReadout = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 54, MaxHeight = 140, BorderThickness = new(0), Background = Brushes.Transparent };
    readonly Button maintenanceCancel = new() { Content = "请求取消", MinHeight = 44, MinWidth = 110, Margin = new(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top };
    readonly TextBlock maintenanceElapsed = new() { Margin = new(0, 4, 0, 0) };
    CancellationTokenSource? maintenanceCancellation;
    long maintenanceGeneration;
    string? unknownMaintenanceContext;
    bool maintenanceCompact;
    FrameworkElement CreateMaintenancePanel(bool compact = false)
    {
        maintenanceCompact = compact;
        if (compact) maintenanceReadout.MaxHeight = 90;
        AutomationProperties.SetAutomationId(maintenanceReadout, "Maintenance.Progress");
        AutomationProperties.SetName(maintenanceReadout, "维护详情");
        AutomationProperties.SetLiveSetting(maintenanceReadout, AutomationLiveSetting.Polite);
        AutomationProperties.SetAutomationId(maintenanceCancel, "Maintenance.Cancel");
        var layout = new DockPanel(); DockPanel.SetDock(maintenanceCancel, Dock.Right); layout.Children.Add(maintenanceCancel); var content = new StackPanel();
        if (compact) content.Children.Add(new Expander { Header = "安装记录与路径详情", Content = maintenanceReadout });
        else content.Children.Add(maintenanceReadout);
        content.Children.Add(maintenanceElapsed); layout.Children.Add(content); maintenancePanel.Child = layout;
        maintenanceCancel.Click += (_, _) =>
        {
            if (maintenanceCancellation is null) return;
            maintenanceCancel.IsEnabled = false;
            maintenanceReadout.Text += "\n已请求停止，正在等待结果，请勿重复操作。";
            maintenanceCancellation.Cancel();
        };
        return maintenancePanel;
    }
    protected void ResolveKnownMaintenanceOutcome()
    {
        if (UnknownOutcome) maintenanceReadout.Text = "原操作已取得明确结果。请以上方核对说明为准。";
        UnknownOutcome = false;
        unknownMaintenanceContext = null;
    }
    protected void HideCompletedMaintenanceProgress()
    {
        if (maintenanceCancellation is not null || UnknownOutcome)
            throw new InvalidOperationException("维护仍在执行或结果尚未确认，不能收起当前结果。");
        maintenancePanel.Visibility = Visibility.Collapsed;
    }
    protected async Task<T> RunMaintenance<T>(string context, Func<IProgress<MaintenanceProgress>, CancellationToken, Task<T>> operation, bool readOnly = false, bool canRequestCancellation = true)
    {
        if (UnknownOutcome && !readOnly) throw new InstallationOutcomeUnknownException("上次维护结果未确认，暂不能再次执行维护操作。");
        var originalContext = context;
        if (readOnly && unknownMaintenanceContext is not null) context = "上次操作结果未确认：\n" + unknownMaintenanceContext + "\n本次检查：\n" + context;
        Message(UnknownOutcome ? "上次操作仍未确认；检查不会解除限制。" : "维护正在执行，请查看当前步骤。");
        using var cancellation = new CancellationTokenSource(); maintenanceCancellation = cancellation; var generation = ++maintenanceGeneration;
        if (maintenanceCompact) { maintenanceReadout.MinHeight = 54; maintenanceReadout.MaxHeight = 90; maintenanceElapsed.Visibility = Visibility.Visible; }
        maintenancePanel.Visibility = Visibility.Visible; maintenanceCancel.Visibility = canRequestCancellation ? Visibility.Visible : Visibility.Collapsed; maintenanceCancel.IsEnabled = canRequestCancellation;
        maintenanceReadout.Text = (canRequestCancellation ? "等待授权与检查；提交前可以请求取消。" : "正在继续上次操作，此步骤不能取消。") + "\n" + context; maintenanceReadout.Focus();
        var elapsed = Stopwatch.StartNew();
        void UpdateElapsed() => maintenanceElapsed.Text = "已用时 " + ((long)elapsed.Elapsed.TotalSeconds) + " 秒";
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) }; timer.Tick += (_, _) => UpdateElapsed(); UpdateElapsed(); timer.Start();
        var progress = new Progress<MaintenanceProgress>(p =>
        {
            if (generation != maintenanceGeneration) return;
            var stage = p.Stage switch
            {
                MaintenanceStage.Checking => "正在检查权限和目标目录",
                MaintenanceStage.CopyingSnapshot => "正在复制数据用于检查",
                MaintenanceStage.CopyingDatabase => "正在复制数据库",
                MaintenanceStage.CopyingCustody => "正在复制恢复所需的文件",
                MaintenanceStage.Verifying => "正在校验备份",
                MaintenanceStage.PublishingBackup => "正在保存完整备份结果",
                MaintenanceStage.PublishingRestore => "正在保存恢复结果",
                MaintenanceStage.RetiringDeployment => "正在卸载程序",
                MaintenanceStage.RemovingBackupSet => "正在删除所选完整备份清单",
                MaintenanceStage.RemovingProgram => "正在清理本次安装的程序与系统入口",
                MaintenanceStage.InstallingMachine => "已进入机器变更；正在安装程序和系统入口",
                MaintenanceStage.RepairingSystem => "正在修复开始菜单和程序入口",
                MaintenanceStage.CancelAccepted => "已接受取消请求，正在停止并确认结果",
                MaintenanceStage.CancelTooLate => "已进入提交或完成阶段；取消不能回滚，请等待结果",
                _ => throw new InvalidOperationException("未知维护阶段")
            };
            var count = p.ProcessedBytes is { } done ? " · " + done.ToString("N0") + " 字节" + (p.TotalBytes is { } total ? " / " + total.ToString("N0") : "") : "";
            maintenanceReadout.Text = stage + count + "\n" + (cancellation.IsCancellationRequested ? "已请求取消，仍须等待最终结果。" : p.CanCancel ? "可以请求取消；已生成的文件将保留。" : "此阶段不可取消，请等待最终结果。") + "\n" + context;
            maintenanceCancel.IsEnabled = canRequestCancellation && p.CanCancel && !cancellation.IsCancellationRequested;
        });
        try
        {
            var result = await Task.Run(() => operation(progress, cancellation.Token));
            maintenanceReadout.Text = (readOnly ? "已取得检查结果。" : "已完成。") + (cancellation.IsCancellationRequested ? "取消请求未改变已完成的结果。" : "") + "\n" + context;
            return result;
        }
        catch (MaintenanceCancelledException)
        { maintenanceReadout.Text = "已确认取消本次操作。已生成的备份或临时目录保留；取消不回滚已完成的步骤，请按原回执核对，重新执行时选择新的目标目录。\n" + context; throw; }
        catch (InstallationOutcomeUnknownException)
        { UnknownOutcome = true; unknownMaintenanceContext ??= originalContext; maintenanceReadout.Text = "结果未确认，请勿重复执行。\n" + context; throw; }
        catch
        { maintenanceReadout.Text = "操作未完成，已生成的文件保留。请按下方原因处理。\n" + context; throw; }
        finally
        {
            timer.Stop(); elapsed.Stop(); UpdateElapsed(); maintenanceGeneration++; maintenanceCancellation = null; maintenanceCancel.IsEnabled = false; maintenanceCancel.Visibility = Visibility.Collapsed;
            // Keep the complete read-only receipt scrollable, while returning space
            // to installer options after a known result. Active/unknown stays expanded.
            if (maintenanceCompact && !UnknownOutcome) { maintenanceReadout.MinHeight = 42; maintenanceReadout.MaxHeight = 42; maintenanceElapsed.Visibility = Visibility.Collapsed; }
        }
    }
    protected Task RunMaintenance(string context, Func<IProgress<MaintenanceProgress>, CancellationToken, Task> operation)
        => RunMaintenance(context, async (progress, token) => { await operation(progress, token); return true; });
}

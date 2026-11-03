using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Platform;
using Microsoft.Win32;

namespace Ipc.Application.Sportmaster.Desktop;

public partial class SupplyStationWindow
{
    StationStatisticsControl? statistics;
    FrameworkElement? exceptions, maintenanceCenter;
    public Func<Task>? OpenMaintenanceOwnerAsync { get; set; }
    async void ProductRestart_Click(object sender, RoutedEventArgs e) => await InspectOrActivateConfiguration();
    async Task InspectOrActivateConfiguration()
    {
        if (!ViewModel.HasConfigurationActivationFailure) { await RequestActivation(); return; }
        await OpenOperatorSettingsAsync();
        if (ViewModel.IsManagementMode) SetPage("diagnostics");
    }
    StationHelpWindow? help;
    void Help_Click(object sender, RoutedEventArgs e) => ShowHelp();
    void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e) { if (e.Key == System.Windows.Input.Key.F1) { e.Handled = true; ShowHelp(); } }
    // Not modal: the station keeps working and stays visible while someone reads the help.
    void ShowHelp()
    {
        if (help is { IsLoaded: true }) { help.Activate(); return; }
        help = new StationHelpWindow(this, ViewModel.Application as IDiagnosticPackageApplication);
        help.Closed += (_, _) => { help = null; RestoreBarcodeFocus(); };
        help.Show();
    }
    void Statistics_Click(object sender, RoutedEventArgs e) => SetPage("statistics");
    void Exceptions_Click(object sender, RoutedEventArgs e) => SetPage("exceptions");
    void MaintenanceCenter_Click(object sender, RoutedEventArgs e) => SetPage("maintenance-center");
    FrameworkElement CreateExceptions() => new StationExceptionsControl(ViewModel, ShowDetailsAsync, async id =>
    {
        await ShowDetailsAsync(id);
        if (ViewModel.Selected?.Id != id) throw new InvalidOperationException("原作业读取未确认，请重新选择");
        await OpenProductTool(MaintenanceTool.Recovery);
    });
    async Task OpenProductTool(MaintenanceTool index)
    { await OpenOperatorSettingsAsync(); if (ViewModel.IsManagementMode && ViewModel.HasLoadedConfiguration) maintenance.ShowTechnicalTool(index); }
    FrameworkElement CreateMaintenanceCenter()
    {
        var body = new StackPanel { Margin = new(24, 20, 24, 16) }; body.Children.Add(ProductText("维护中心", title: true));
        var tasks = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Stretch };
        var attention = ProductText(""); body.Children.Add(attention);
        void Attention() => attention.Text = "待核对包裹 " + (ViewModel.Snapshot?.Operations.Count(o => o.Wcs?.State is StageState.Unknown or StageState.Conflict) ?? 0) + " · " + ViewModel.ConfigurationActivationOutcome;
        System.ComponentModel.PropertyChangedEventHandler attentionChanged = (_, args) => { if (args.PropertyName is nameof(ViewModel.Snapshot) or nameof(ViewModel.Fresh)) Attention(); };
        body.Loaded += (_, _) => { ViewModel.PropertyChanged += attentionChanged; Attention(); };
        body.Unloaded += (_, _) => ViewModel.PropertyChanged -= attentionChanged;
        // Only tasks that exist nowhere else: diagnostics, backup, support export and offline maintenance.
        body.Children.Add(ProductText("请选择要做的维护。前四项在软件运行中完成；恢复备份、换机和修复/卸载需先完全停止软件。"));
        void TaskRow(string name, string description, string action, Func<Task> run)
        {
            var row = new DockPanel { Margin = new(0, 12, 0, 8) }; var button = ProductButton(action, run); DockPanel.SetDock(button, Dock.Right); row.Children.Add(button);
            var text = new StackPanel(); text.Children.Add(new TextBlock { Text = name, FontWeight = UiTypography.Emphasis }); text.Children.Add(ProductText(description)); row.Children.Add(text); tasks.Children.Add(row);
        }
        TaskRow("连接或供包异常", "查看两台相机、RCS、存储与备份的实际状态，再按对象处理。", "打开诊断", () => { SetPage("diagnostics"); return Task.CompletedTask; });
        TaskRow("创建完整备份", "先暂停新供包，等进行中的请求处理完，再备份数据库、配置和 RCS 返回记录。完成后点击“检查并恢复”。", "备份…", CreateProductBackup);
        TaskRow("备份后恢复供包", "重新检查连接、暂停状态、存储容量和未确认作业，通过后恢复供包；不会重发格口未确认的请求。", "检查并恢复", async () => { await ViewModel.RequireManagementModeAsync(); var s = await ViewModel.Application.SnapshotAsync(); var r = await ViewModel.Application.CommandAsync(new(Wire.Id(), s.Revision, "Resume")); ViewModel.Message = r.Status + " · " + r.Reason; });
        TaskRow("远程支持", "导出诊断包：设备状态、近期请求索引和校验清单，不含条码、图片和密码；文件只保存在本机。", "导出诊断包…", ExportProductDiagnostics);
        TaskRow("恢复备份、换机、修复或卸载", "先完全停止软件，再进入离线维护。备份恢复到新目录，不覆盖原数据。", "进入离线维护…", RequestOfflineMaintenance);
        // Optional notice from the station's update folder; reading never installs or pauses anything.
        var update = ProductText("正在读取新版本说明…");
        async Task CheckUpdate()
        {
            if (ViewModel.Application is not IUpdateNoticeSource source) { update.Text = "当前版本不支持新版本提示。"; return; }
            var notice = await source.ReadUpdateNoticeAsync(CancellationToken.None);
            update.Text = notice.Text;
            update.SetResourceReference(TextBlock.ForegroundProperty, notice.State is UpdateNoticeState.CurrentRevoked ? "Danger" : notice.State is UpdateNoticeState.Available ? "Accent" : "Muted");
        }
        TaskRow("软件更新", "新版本说明由实施人员放在本机数据目录的 updates 文件夹（或在其中的 source.txt 写共享路径）。这里只提示，安装仍按部署卡进行。", "重新检查", CheckUpdate);
        tasks.Children.Add(update);
        body.Loaded += async (_, _) => { try { await CheckUpdate(); } catch (Exception) { update.Text = "新版本说明读取失败，不影响作业；请稍后重新检查。"; } };
        tasks.SizeChanged += (_, _) => { var two = tasks.ActualWidth >= 900; foreach (var item in tasks.Children.OfType<FrameworkElement>()) item.Width = Math.Max(220, tasks.ActualWidth / (two ? 2 : 1) - 24); };
        body.Children.Add(tasks);
        var cancelExport = new Button { Content = "取消诊断导出", MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Left };
        cancelExport.Click += (_, _) => productDiagnosticCancellation?.Cancel(); body.Children.Add(cancelExport);
        var cleanup = ProductText(""); body.Children.Add(cleanup);
        body.Loaded += async (_, _) => await ViewModel.RunAsync(async () => { if (ViewModel.Application is IStationOperations api) { var s = await api.ReadCleanupStatusAsync(); cleanup.Text = $"自动清理：{(s.Enabled ? "已启用" : "未启用")} · {s.State}\n{s.Detail}"; } }, "读取维护状态");
        return new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
    async Task CreateProductBackup()
    {
        await ViewModel.RequireManagementModeAsync();
        var dialog = new SaveFileDialog { Filter = "完整备份数据库|*.db", InitialDirectory = "D:\\", FileName = "station-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".db" };
        if (dialog.ShowDialog(this) != true) return;
        if (MessageBox.Show(this, "将暂停新供包并创建完整备份。完成后请核对状态，再点击“检查并恢复”。", "创建完整备份", MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;
        await ViewModel.PauseAsync(); await ViewModel.RefreshAsync(); ViewModel.BackupPath = dialog.FileName; await ViewModel.BackupAsync();
    }
    CancellationTokenSource? productDiagnosticCancellation;
    async Task ExportProductDiagnostics()
    {
        await ViewModel.RequireManagementModeAsync();
        var dialog = new SaveFileDialog { Filter = "诊断包|*.zip", InitialDirectory = "D:\\", FileName = "station-diagnostics-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip" };
        if (dialog.ShowDialog(this) != true || ViewModel.Application is not IDiagnosticPackageApplication api) return;
        if (productDiagnosticCancellation is not null) throw new InvalidOperationException("诊断包正在导出，请等待或取消");
        using var cancellation = new CancellationTokenSource(); productDiagnosticCancellation = cancellation;
        try
        {
            var receipt = await api.ExportDiagnosticPackageReportAsync(dialog.FileName, cancellation.Token);
            ViewModel.Message = (receipt.Status == DiagnosticPackageStatus.Partial ? "诊断摘要已导出（含未收集项）：" : "诊断包已导出：") + receipt.Path +
                "\n" + string.Join("\n", receipt.Notices);
        }
        finally { productDiagnosticCancellation = null; }
    }
    async Task RequestOfflineMaintenance()
    {
        await ViewModel.RequireManagementModeAsync();
        if (root.OpenMaintenanceOwnerAsync is null) throw new InvalidOperationException("当前版本不支持离线维护");
        if (MessageBox.Show(this, "将完全停止软件并关闭所有窗口，然后打开离线维护中心。业务数据保留。", "进入离线维护", MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;
        await ViewModel.PauseAsync(); await ViewModel.RefreshAsync();
        if (ViewModel.Snapshot?.Paused != true || !ViewModel.Fresh) throw new InvalidOperationException("暂停结果未确认，软件未停止");
        root.offlineMaintenanceRequested = true; root.Close();
    }
    bool offlineMaintenanceRequested;
    Button ProductButton(string label, Func<Task> action)
    {
        var button = new Button { Content = label, MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Left, Padding = new(12, 4, 12, 4), Margin = new(0, 6, 12, 6) };
        button.SetBinding(IsEnabledProperty, new Binding(nameof(ViewModel.CanStartOperation)) { Source = ViewModel });
        button.Click += async (_, _) => await ViewModel.RunAsync(action, label);
        return button;
    }
    static TextBlock ProductText(string value, bool title = false) => new() { Text = value, TextWrapping = TextWrapping.Wrap, Margin = new(0, 6, 0, 6), FontSize = title ? UiTypography.PageTitle : UiTypography.Body, FontWeight = title ? UiTypography.Emphasis : FontWeights.Normal };
    async Task<string> ReadProductHealth()
    {
        await ViewModel.RequireManagementModeAsync(); await ViewModel.RefreshAsync(); var snapshot = ViewModel.Snapshot ?? throw new InvalidOperationException("当前状态不可读"); var text = new StringBuilder();
        foreach (var reader in snapshot.Configuration.Effective.Topology?.Readers ?? [snapshot.Configuration.Effective.Reader])
        {
            if (ViewModel.Application is not ICameraAcquisitionDiagnostics api) continue;
            var sample = await api.ReadCameraAcquisitionAsync(reader.DeviceId, CancellationToken.None);
            var name = reader.DeviceId == ViewModel.Left.ReaderId ? "左相机" : reader.DeviceId == ViewModel.Right.ReaderId ? "右相机" : reader.DeviceId;
            text.AppendLine(name + "：" + CameraAcquisitionPresentation.Describe(sample));
        }
        foreach (var health in snapshot.Health.Where(h => h.InstanceId == snapshot.Configuration.Effective.Wcs?.InstanceId))
        {
            var confirmation = !health.Ready ? "组件未就绪" : health.RemoteConfirmation == RemoteConnectionConfirmation.ResponseReceived && health.ValidUntil > DateTimeOffset.UtcNow && health.ConfirmedAt <= DateTimeOffset.UtcNow
                ? "心跳正常" : "等待 RCS 确认";
            text.AppendLine($"RCS：{confirmation} · 最近确认 {health.ConfirmedAt.ToLocalTime():HH:mm:ss}");
        }
        text.AppendLine($"存储剩余：{snapshot.Capacity?.FreeStorageBytes / 1048576}MiB · 备份：{(snapshot.BackupScheduleStatus?.Enabled != true ? "未启用" : snapshot.BackupScheduleStatus.Outcome == "BackupCreated" ? "最近一次已完成" : "未完成或结果未确认")}");
        text.AppendLine("体检只读取现有相机和心跳数据，不会发送测试请求；在线不代表能识别条码或取得格口。");
        return text.ToString();
    }
}

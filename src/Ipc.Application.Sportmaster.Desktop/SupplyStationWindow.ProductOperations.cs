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
    async void ProductRestart_Click(object sender, RoutedEventArgs e) => await RequestActivation();
    void Statistics_Click(object sender, RoutedEventArgs e) => SetPage("statistics");
    void Exceptions_Click(object sender, RoutedEventArgs e) => SetPage("exceptions");
    void MaintenanceCenter_Click(object sender, RoutedEventArgs e) => SetPage("maintenance-center");
    sealed record ExceptionRow(ParcelOperation Original)
    {
        public string Id => Original.Id;
        public string PositionId => Original.PositionId == "left" ? "左位" : Original.PositionId == "right" ? "右位" : Original.PositionId ?? "未确认";
        public string ParcelCode => Original.ParcelCode;
        public string State => Original.Wcs?.State is StageState.Unknown or StageState.Conflict ? "格口未确认" : Original.Wcs?.State == StageState.Rejected ? "已拒绝" : "需核对作业";
        public string Reason => Original.Wcs?.State is StageState.Unknown or StageState.Conflict
            ? ParcelPresentation.RcsNotDelivered(Original) ? "已确认请求未送达 RCS。连接恢复后可重新扫描本件；原请求不会重发。" : "格口未确认。勿重复上件，请查询本件结果。"
            : ParcelPresentation.RcsException(Original) ?? "本件未分配格口，请查看详情并按原因处理。";
    }
    FrameworkElement CreateExceptions()
    {
        var root = new DockPanel { Margin = new(24, 20, 24, 16) }; var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        header.Children.Add(ProductText("异常处理", title: true));
        header.Children.Add(ProductText("格口未确认的包裹勿重复上件，先查询本件结果；解除限制不会重新发送。被拒绝的包裹按原因和现场流程处理。"));
        var grid = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, EnableRowVirtualization = true, CanUserAddRows = false };
        foreach (var (label, field) in new[] { ("作业号", "Id"), ("投递位", "PositionId"), ("条码", "ParcelCode"), ("状态", "State"), ("原因", "Reason") })
            grid.Columns.Add(new DataGridTextColumn { Header = label, Binding = new Binding(field), Width = DataGridLength.Auto });
        var feedback = ProductText(""); header.Children.Add(feedback);
        var updateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        var active = false; var refreshing = false; long viewGeneration = 0;
        async Task Refresh()
        {
            if (refreshing || !active) return; refreshing = true; var version = viewGeneration;
            try
            {
                var selected = (grid.SelectedItem as ExceptionRow)?.Id;
                var snapshot = await ViewModel.Application.SnapshotAsync();
                var recent = await ViewModel.Application.HistoryAsync(limit: 50);
                if (!active || version != viewGeneration) return;
                var rows = snapshot.Operations.Concat(recent.Items.Select(i => i.Operation)).DistinctBy(o => o.Id)
                    .Where(o => o.Wcs?.State is StageState.Unknown or StageState.Conflict or StageState.Rejected || o.RecoveryRequired)
                    .OrderByDescending(o => o.ObservedAt).Select(o => new ExceptionRow(o)).ToArray();
                grid.ItemsSource = rows; grid.SelectedItem = rows.FirstOrDefault(r => r.Id == selected);
                feedback.Text = $"更新于{DateTime.Now:HH:mm:ss}。显示全部格口未确认的作业，以及最近 50 件中被拒绝的作业。选中一件查看详情和可执行的操作。";
            }
            finally { refreshing = false; }
        }
        var actions = new WrapPanel(); header.Children.Add(actions);
        actions.Children.Add(ProductButton("刷新异常", Refresh));
        actions.Children.Add(ProductButton("查看详情与核对…", async () => { if (grid.SelectedItem is not ExceptionRow row) throw new InvalidOperationException("请先选择一件作业"); await ShowDetailsAsync(row.Original); }));
        actions.Children.Add(ProductButton("打开恢复操作…", () => OpenProductTool(MaintenanceTool.Recovery)));
        root.Children.Add(grid);
        root.Loaded += async (_, _) => { active = true; viewGeneration++; updateTimer.Start(); await ViewModel.RunAsync(Refresh, "读取异常作业"); };
        root.Unloaded += (_, _) => { active = false; viewGeneration++; updateTimer.Stop(); };
        updateTimer.Tick += async (_, _) => { try { await Refresh(); } catch (Exception error) { if (active) feedback.Text = "异常列表读取中断，请点击“刷新异常”；作业状态不受影响。" + DiagnosticText.Protect(error.Message); } };
        return root;
    }
    async Task OpenProductTool(MaintenanceTool index)
    { await OpenOperatorSettingsAsync(); if (ViewModel.IsManagementMode && ViewModel.HasLoadedConfiguration) maintenance.ShowTechnicalTool(index); }
    FrameworkElement CreateMaintenanceCenter()
    {
        var body = new StackPanel { Margin = new(24, 20, 24, 16) }; body.Children.Add(ProductText("维护中心", title: true));
        body.Children.Add(ProductText("请选择要做的维护。前五项在软件运行中完成；恢复备份、换机和修复/卸载需先完全停止软件。软件暂停不是安全急停。"));
        void TaskRow(string name, string description, string action, Func<Task> run)
        {
            var row = new DockPanel { Margin = new(0, 12, 0, 8) }; var button = ProductButton(action, run); DockPanel.SetDock(button, Dock.Right); row.Children.Add(button);
            var text = new StackPanel(); text.Children.Add(new TextBlock { Text = name, FontWeight = UiTypography.Emphasis }); text.Children.Add(ProductText(description)); row.Children.Add(text); body.Children.Add(row);
        }
        TaskRow("连接或供包异常", "查看两台相机、RCS、存储与备份的实际状态，再按对象处理。", "打开诊断", () => { SetPage("diagnostics"); return Task.CompletedTask; });
        TaskRow("待核对包裹", "逐件查看被拒绝和格口未确认的包裹；不会自动重发。", "处理异常", () => { SetPage("exceptions"); return Task.CompletedTask; });
        TaskRow("配置已保存但未生效", "重启软件使已保存的设置生效；重启失败时继续使用原设置，并显示原因。", "检查并重启…", async () => { await ViewModel.RequireManagementModeAsync(); if (ViewModel.Snapshot?.Configuration.Pending is null) throw new InvalidOperationException("没有待重启设置"); await RequestActivation(); });
        TaskRow("创建完整备份", "先暂停新供包，等进行中的请求处理完，再备份数据库、配置和 RCS 返回记录。完成后点击“检查并恢复”。", "备份…", CreateProductBackup);
        TaskRow("备份后恢复供包", "重新检查连接、暂停状态、存储容量和未确认作业，通过后恢复供包；不会重发格口未确认的请求。", "检查并恢复", async () => { await ViewModel.RequireManagementModeAsync(); var s = await ViewModel.Application.SnapshotAsync(); var r = await ViewModel.Application.CommandAsync(new(Wire.Id(), s.Revision, "Resume")); ViewModel.Message = r.Status + " · " + r.Reason; });
        TaskRow("自动清理与班次", "设置保留策略、清理时刻和备份份数上限；清理后保留统计汇总。", "打开设置", () => { SetPage("statistics"); return Task.CompletedTask; });
        TaskRow("远程支持", "导出诊断包：设备状态、近期请求索引和校验清单，不含条码、图片和密码；文件只保存在本机。", "导出诊断包…", ExportProductDiagnostics);
        TaskRow("恢复备份、换机、修复或卸载", "先完全停止软件，再进入离线维护。备份恢复到新目录，不覆盖原数据。", "进入离线维护…", RequestOfflineMaintenance);
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
        if (dialog.ShowDialog(this) != true || ViewModel.Application is not IStationOperations api) return;
        if (productDiagnosticCancellation is not null) throw new InvalidOperationException("诊断包正在导出，请等待或取消");
        using var cancellation = new CancellationTokenSource(); productDiagnosticCancellation = cancellation;
        try { ViewModel.Message = "诊断包已导出：" + await api.ExportDiagnosticPackageAsync(dialog.FileName, cancellation.Token); }
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
            if (ViewModel.Application is not ICameraPreviewLeaseDiagnostics api) continue;
            using var sample = await api.ReadCameraPreviewLeaseAsync(reader.DeviceId, CancellationToken.None);
            var d = sample.Diagnostics;
            var name = reader.DeviceId == ViewModel.Left.ReaderId ? "左相机" : reader.DeviceId == ViewModel.Right.ReaderId ? "右相机" : reader.DeviceId;
            text.AppendLine(d is null ? name + "：" + sample.Status : $"{name}：有效帧 {d.Frames} · 已解码 {d.DecodeCompleted} · 识别到条码 {d.Candidates} · 最近有效帧 {d.LastFrameAt?.ToLocalTime():HH:mm:ss} · {d.State}");
            if (!string.IsNullOrEmpty(d?.Failure)) text.AppendLine("最近错误：" + DiagnosticText.Protect(d.Failure));
        }
        foreach (var health in snapshot.Health.Where(h => h.InstanceId == snapshot.Configuration.Effective.Wcs?.InstanceId))
        {
            var confirmation = !health.Ready ? "组件未就绪" : health.RemoteConfirmation == RemoteConnectionConfirmation.ResponseReceived
                ? "心跳正常（只表示 RCS 可连接）" : "等待 RCS 确认";
            text.AppendLine($"RCS：{confirmation} · 确认于 {health.ConfirmedAt.ToLocalTime():HH:mm:ss} · 有效至 {health.ValidUntil.ToLocalTime():HH:mm:ss}");
        }
        text.AppendLine($"存储剩余：{snapshot.Capacity?.FreeStorageBytes / 1048576}MiB · 备份：{(snapshot.BackupScheduleStatus?.Enabled != true ? "未启用" : snapshot.BackupScheduleStatus.Outcome == "BackupCreated" ? "最近一次已完成" : "未完成或结果未确认")}");
        text.AppendLine("体检只读取现有相机和心跳数据，不会发送测试请求；在线不代表能识别条码或取得格口。");
        return text.ToString();
    }
}

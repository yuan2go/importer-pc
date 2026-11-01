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
        public string State => Original.Wcs?.State is StageState.Unknown or StageState.Conflict ? "格口未确认" : Original.Wcs?.State == StageState.Rejected ? "已拒绝" : "需核对原作业";
        public string Reason => Original.Wcs?.State is StageState.Unknown or StageState.Conflict
            ? ParcelPresentation.RcsNotDelivered(Original) ? "已证实本次正文未送达；连接恢复后可独立重新扫描，原请求不重发。" : "原请求结果未确认。勿重复上件，请核对本件原请求。"
            : ParcelPresentation.RcsException(Original) ?? "本件未取得可用格口，请查看本件详情并按原因处理。";
    }
    FrameworkElement CreateExceptions()
    {
        var root = new DockPanel { Margin = new(24, 20, 24, 16) }; var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        header.Children.Add(ProductText("异常处理", title: true));
        header.Children.Add(ProductText("结果未确认时勿重复上件。先核对本件原请求；解除阻断不代表重新发送。拒绝记录保留原因，按现场确认的处置流程处理。"));
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
                feedback.Text = $"更新于{DateTime.Now:HH:mm:ss}。未确认责任来自当前业务账；拒绝记录显示最近50件中的异常。选择一件查看证据与允许动作。";
            }
            finally { refreshing = false; }
        }
        var actions = new WrapPanel(); header.Children.Add(actions);
        actions.Children.Add(ProductButton("刷新异常", Refresh));
        actions.Children.Add(ProductButton("查看本件与核对…", async () => { if (grid.SelectedItem is not ExceptionRow row) throw new InvalidOperationException("请先选择一件作业"); await ShowDetailsAsync(row.Original); }));
        actions.Children.Add(ProductButton("打开恢复操作…", () => OpenProductTool(MaintenanceTool.Recovery)));
        root.Children.Add(grid);
        root.Loaded += async (_, _) => { active = true; viewGeneration++; updateTimer.Start(); await ViewModel.RunAsync(Refresh, "读取异常作业"); };
        root.Unloaded += (_, _) => { active = false; viewGeneration++; updateTimer.Stop(); };
        updateTimer.Tick += async (_, _) => { try { await Refresh(); } catch (Exception error) { if (active) feedback.Text = "异常状态读取中断，请刷新核对；原请求保持。" + DiagnosticText.Protect(error.Message); } };
        return root;
    }
    async Task OpenProductTool(MaintenanceTool index)
    { await OpenOperatorSettingsAsync(); if (ViewModel.IsManagementMode && ViewModel.HasLoadedConfiguration) maintenance.ShowTechnicalTool(index); }
    FrameworkElement CreateMaintenanceCenter()
    {
        var body = new StackPanel { Margin = new(24, 20, 24, 16) }; body.Children.Add(ProductText("维护中心", title: true));
        body.Children.Add(ProductText("先选择要完成的任务。运行维护沿用当前业务实例；恢复备份、迁移与程序维护须先完整停止原实例。软件暂停不是安全急停。"));
        void TaskRow(string name, string description, string action, Func<Task> run)
        {
            var row = new DockPanel { Margin = new(0, 12, 0, 8) }; var button = ProductButton(action, run); DockPanel.SetDock(button, Dock.Right); row.Children.Add(button);
            var text = new StackPanel(); text.Children.Add(new TextBlock { Text = name, FontWeight = UiTypography.Emphasis }); text.Children.Add(ProductText(description)); row.Children.Add(text); body.Children.Add(row);
        }
        TaskRow("连接或供包异常", "查看两台相机、RCS、存储与备份的实际状态，再按对象处理。", "打开诊断", () => { SetPage("diagnostics"); return Task.CompletedTask; });
        TaskRow("待核对包裹", "按原作业查看拒绝与未确认结果，保持原请求身份，不盲重发。", "处理异常", () => { SetPage("exceptions"); return Task.CompletedTask; });
        TaskRow("配置已保存但未生效", "只有已保存的配置可申请重启；失败保留原账并显示实际结果。", "核对并重启…", async () => { await ViewModel.RequireManagementModeAsync(); if (ViewModel.Snapshot?.Configuration.Pending is null) throw new InvalidOperationException("没有待重启设置"); await RequestActivation(); });
        TaskRow("创建完整备份", "暂停新供包并排空在途请求；原始回包和配置与数据库一起备份。完成后核对再恢复。", "备份…", CreateProductBackup);
        TaskRow("备份后的供包恢复", "重新检查实际连接、暂停状态、容量及原请求责任；不重发未知请求。", "检查并恢复", async () => { await ViewModel.RequireManagementModeAsync(); var s = await ViewModel.Application.SnapshotAsync(); var r = await ViewModel.Application.CommandAsync(new(Wire.Id(), s.Revision, "Resume")); ViewModel.Message = r.Status + " · " + r.Reason; });
        TaskRow("自动历史清理与班次", "设置保留策略、清理时刻和备份份数上限；清理后保留统计汇总。", "打开设置", () => { SetPage("statistics"); return Task.CompletedTask; });
        TaskRow("远程支持", "导出有界白名单诊断包，包含状态、近期请求索引及完整性清单；文件保留在本机。", "导出诊断包…", ExportProductDiagnostics);
        TaskRow("恢复备份、换机、修复或卸载", "完整停止原实例后进入独立维护。恢复目标隔离，原根退役、权限和请求责任仍须核对。", "进入离线维护…", RequestOfflineMaintenance);
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
        if (root.OpenMaintenanceOwnerAsync is null) throw new InvalidOperationException("当前宿主未提供离线维护入口");
        if (MessageBox.Show(this, "将停止整个业务实例并关闭辅助窗口，随后进入维护中心。原请求及业务数据保留。", "进入离线维护", MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;
        await ViewModel.PauseAsync(); await ViewModel.RefreshAsync();
        if (ViewModel.Snapshot?.Paused != true || !ViewModel.Fresh) throw new InvalidOperationException("暂停结果尚未确认，保留当前实例");
        root.offlineMaintenanceRequested = true; root.Close();
    }
    bool offlineMaintenanceRequested;
    Button ProductButton(string label, Func<Task> action)
    { var button = new Button { Content = label, MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Left, Padding = new(12, 4, 12, 4), Margin = new(0, 6, 12, 6) }; button.Click += async (_, _) => await ViewModel.RunAsync(action, label); return button; }
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
            text.AppendLine(d is null ? name + "：" + sample.Status : $"{name}：有效帧{d.Frames} · 解码完成{d.DecodeCompleted} · 含码候选{d.Candidates} · 最近有效帧{d.LastFrameAt?.ToLocalTime():HH:mm:ss} · {d.State}");
            if (!string.IsNullOrEmpty(d?.Failure)) text.AppendLine("最近错误：" + DiagnosticText.Protect(d.Failure));
        }
        foreach (var health in snapshot.Health.Where(h => h.InstanceId == snapshot.Configuration.Effective.Wcs?.InstanceId))
            text.AppendLine($"RCS：{(health.Ready ? "当前可用" : "未确认可用")} · 最近确认{health.ConfirmedAt.ToLocalTime():HH:mm:ss} · 有效至{health.ValidUntil.ToLocalTime():HH:mm:ss}");
        text.AppendLine($"存储剩余：{snapshot.Capacity?.FreeStorageBytes / 1048576}MiB · 备份：{(snapshot.BackupScheduleStatus?.Enabled != true ? "未启用" : snapshot.BackupScheduleStatus.Outcome == "BackupCreated" ? "最近完成" : "待核对")}");
        text.AppendLine("只读体检复用现有采集与心跳事实，不发送测试业务请求；连接在线不代表识码或取格口成功。");
        return text.ToString();
    }
}

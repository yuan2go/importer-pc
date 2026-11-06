using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Platform;
using Microsoft.Win32;

namespace Ipc.Application.Sportmaster.Desktop;

public partial class SupplyStationWindow
{
    StationStatisticsControl? statistics;
    FrameworkElement? exceptions;
    public Func<Task>? OpenMaintenanceOwnerAsync { get; set; }
    async void ProductRestart_Click(object sender, RoutedEventArgs e) => await InspectOrActivateConfiguration();
    async Task InspectOrActivateConfiguration()
    {
        if (!ViewModel.HasConfigurationActivationFailure) { await RequestActivation(); return; }
        await OpenOperatorSettingsAsync();
        if (ViewModel.IsManagementMode) SetPage("diagnostics");
    }
    void Statistics_Click(object sender, RoutedEventArgs e) => SetPage("statistics");
    void Exceptions_Click(object sender, RoutedEventArgs e) => SetPage("exceptions");
    FrameworkElement CreateExceptions() => new StationExceptionsControl(ViewModel, ShowDetailsAsync, async id =>
    {
        await ShowDetailsAsync(id);
        if (ViewModel.Selected?.Id != id) throw new InvalidOperationException("原作业读取未确认，请重新选择");
        await OpenProductTool(MaintenanceTool.Recovery);
    });
    async Task OpenProductTool(MaintenanceTool index)
    { await OpenOperatorSettingsAsync(); if (ViewModel.IsManagementMode && ViewModel.HasLoadedConfiguration) maintenance.ShowTechnicalTool(index); }
    Button? cancelProductDiagnostic;
    FrameworkElement CreateMaintenanceTools()
    {
        var body = new StackPanel();
        body.Children.Add(ProductText("备份和诊断导出在软件运行中完成；恢复备份、换机、修复及卸载需先完全停止软件。"));
        var actions = new WrapPanel();
        actions.Children.Add(ProductButton("创建完整备份…", CreateProductBackup));
        actions.Children.Add(ProductButton("导出诊断包…", ExportProductDiagnostics));
        cancelProductDiagnostic = new Button { Content = "取消导出", MinHeight = 44, Visibility = Visibility.Collapsed, Margin = new(0, 6, 12, 6) };
        cancelProductDiagnostic.Click += (_, _) => productDiagnosticCancellation?.Cancel(); actions.Children.Add(cancelProductDiagnostic);
        actions.Children.Add(ProductButton("离线维护…", RequestOfflineMaintenance)); body.Children.Add(actions);
        return body;
    }
    async Task CreateProductBackup()
    {
        await ViewModel.RequireManagementModeAsync();
        var dialog = new SaveFileDialog { Filter = "完整备份数据库|*.db", InitialDirectory = "D:\\", FileName = "station-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".db" };
        if (dialog.ShowDialog(ManagementDialogOwner) != true) return;
        if (MessageBox.Show(ManagementDialogOwner, "将暂停新供包并创建完整备份。备份完成后可检查并恢复供包。", "创建完整备份", MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;
        await ViewModel.PauseAsync("创建完整备份前暂停"); await ViewModel.RefreshAsync();
        if (ViewModel.Snapshot?.Paused != true || !ViewModel.Fresh) throw new InvalidOperationException("暂停尚未确认，未创建备份。");
        var receipt = await ViewModel.Application.BackupAsync(dialog.FileName);
        ViewModel.Message = receipt.Status + " · " + receipt.Reason;
        await ViewModel.RefreshAsync();
        if (receipt.Status != "BackupCreated") return;
        if (MessageBox.Show(ManagementDialogOwner, "完整备份已完成。现在检查连接、未确认作业和存储条件，并恢复供包？", "备份完成", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        await ViewModel.RequireManagementModeAsync();
        var snapshot = await ViewModel.Application.SnapshotAsync();
        var resumed = await ViewModel.Application.CommandAsync(new(Wire.Id(), snapshot.Revision, "Resume"));
        ViewModel.Message = resumed.Status + " · " + resumed.Reason;
        await ViewModel.RefreshAsync();
    }
    CancellationTokenSource? productDiagnosticCancellation;
    async Task ExportProductDiagnostics()
    {
        await ViewModel.RequireManagementModeAsync();
        var dialog = new SaveFileDialog { Filter = "诊断包|*.zip", InitialDirectory = "D:\\", FileName = "station-diagnostics-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip" };
        if (dialog.ShowDialog(ManagementDialogOwner) != true || ViewModel.Application is not IDiagnosticPackageApplication api) return;
        if (productDiagnosticCancellation is not null) throw new InvalidOperationException("诊断包正在导出，请等待或取消");
        using var cancellation = new CancellationTokenSource(); productDiagnosticCancellation = cancellation;
        if (cancelProductDiagnostic is not null) cancelProductDiagnostic.Visibility = Visibility.Visible;
        try
        {
            var receipt = await api.ExportDiagnosticPackageReportAsync(dialog.FileName, cancellation.Token);
            ViewModel.Message = (receipt.Status == DiagnosticPackageStatus.Partial ? "诊断摘要已导出（含未收集项）：" : "诊断包已导出：") + receipt.Path +
                "\n" + string.Join("\n", receipt.Notices);
        }
        finally { productDiagnosticCancellation = null; if (cancelProductDiagnostic is not null) cancelProductDiagnostic.Visibility = Visibility.Collapsed; }
    }
    async Task RequestOfflineMaintenance()
    {
        await ViewModel.RequireManagementModeAsync();
        if (root.OpenMaintenanceOwnerAsync is null) throw new InvalidOperationException("当前版本不支持离线维护");
        if (MessageBox.Show(ManagementDialogOwner, "将完全停止软件并关闭所有窗口，然后打开离线维护。业务数据保留。", "进入离线维护", MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;
        await ViewModel.PauseAsync("进入离线维护前暂停"); await ViewModel.RefreshAsync();
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
}

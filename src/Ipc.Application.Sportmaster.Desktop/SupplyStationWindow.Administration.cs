using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;
namespace Ipc.Application.Sportmaster.Desktop;

public partial class SupplyStationWindow
{
    SoftwareLogControl? logs;
    ManagementCenterWindow? managementCenter;
    Window ManagementDialogOwner => managementCenter is { IsVisible: true } center ? center : this;
    string dailyPage = "workstation";
    bool openingManagement, centerClosing, centerCloseReady;
    string managementWindowId = Ipc.Platform.Wire.Id();
    bool workspaceOpened;
    bool? navigationCollapsed;
    public static readonly DependencyProperty IsNavigationCollapsedProperty = DependencyProperty.Register(nameof(IsNavigationCollapsed), typeof(bool), typeof(SupplyStationWindow), new PropertyMetadata(false));
    public bool IsNavigationCollapsed { get => (bool)GetValue(IsNavigationCollapsedProperty); private set => SetValue(IsNavigationCollapsedProperty, value); }
    void ApplyNavigation()
    {
        IsNavigationCollapsed = navigationCollapsed ?? ViewModel.IsCompact;
        NavigationToggle.Content = IsNavigationCollapsed ? "展开菜单" : "收起菜单";
        NavigationToggle.ToolTip = NavigationToggle.Content;
        System.Windows.Automation.AutomationProperties.SetName(NavigationToggle, (string)NavigationToggle.Content);
    }
    void NavigationToggle_Click(object sender, RoutedEventArgs e) { navigationCollapsed = !IsNavigationCollapsed; ApplyNavigation(); }
    async Task AuthenticateManagementAsync(bool newEntry = false)
    {
        if (ViewModel.Application is not IWorkstationAdministration service) return;
        // Each new visit requires a password, even if another window granted a temporary lease.
        if (newEntry) await service.ExitManagementModeAsync();
        await ViewModel.RefreshManagementModeAsync();
        if (!ViewModel.ManagementStatus.Available) throw new InvalidOperationException("管理授权服务暂时不可用，请稍后重试；原授权保留。");
        if (!ViewModel.IsManagementMode)
        {
            new ManagementModeWindow(service, "管理中心") { Owner = managementCenter is { IsVisible: true } center ? center : this }.ShowDialog();
            await ViewModel.RefreshManagementModeAsync();
        }
        await EnsureManagementWorkspaceAsync();
        EnforceManagementModeView();
    }
    async Task EnsureManagementWorkspaceAsync()
    {
        if (ViewModel.IsManagementMode && !workspaceOpened && ViewModel.Application is IManagementWorkspace workspace)
        {
            managementWindowId = Ipc.Platform.Wire.Id(); workspaceOpened = true;
            try
            {
                var receipt = await ViewModel.ServiceAsync(() => workspace.SetManagementWorkspaceAsync(managementWindowId, true, CancellationToken.None));
                if (receipt.Status == "PausedWithCameraWarning") ViewModel.Message = receipt.Reason;
            }
            catch
            {
                await ViewModel.ServiceAsync(() => workspace.SetManagementWorkspaceAsync(managementWindowId, false, CancellationToken.None));
                workspaceOpened = false;
                throw;
            }
        }
    }
    async void ManagementMode_Click(object sender, RoutedEventArgs e)
    {
        if (openingManagement) return;
        if (managementCenter is { } center) { if (center.IsVisible) center.Activate(); return; }
        openingManagement = true; ManagementCenterEntry.IsEnabled = false;
        try
        {
            await AuthenticateManagementAsync(newEntry: true);
            if (ViewModel.IsManagementMode && !closing && !closed) SetPage("diagnostics");
        }
        catch (Exception) { ViewModel.Message = "管理中心未能打开，请稍后重试或检查管理授权服务。"; }
        finally { openingManagement = false; ManagementCenterEntry.IsEnabled = true; }
    }
    async Task RenewManagementAsync()
    {
        if (ViewModel.Application is not IWorkstationAdministration service) return;
        await ViewModel.RunAsync(async () => { await service.RenewManagementModeAsync(); await ViewModel.RefreshManagementModeAsync(); }, "延长管理授权");
    }
    async Task ManagePasswordAsync()
    {
        if (ViewModel.Application is not IWorkstationAdministration service || managementCenter is not { } center) return;
        new ManagementModeWindow(service) { Owner = center }.ShowDialog();
        await ViewModel.RefreshManagementModeAsync(); EnforceManagementModeView();
    }
    void ShowManagementPage(string page)
    {
        if (managementCenter is null)
        {
            centerCloseReady = false;
            var center = new ManagementCenterWindow(ViewModel, NavigateManagement, async () =>
            {
                try { await AuthenticateManagementAsync(); }
                catch (Exception) { ViewModel.Message = "管理密码验证未完成，请稍后重试。"; }
            }, RenewManagementAsync, ManagePasswordAsync) { Owner = this };
            managementCenter = center;
            center.Closing += CloseManagementCenter;
            center.Closed += (_, _) =>
            {
                center.DetachPage(); managementCenter = null;
                ViewModel.SelectedPage = dailyPage; ApplyLayout();
            };
            // Queue the native modal loop so callers can finish loading or selecting the requested page.
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(async () =>
            {
                try
                {
                    await EnsureManagementWorkspaceAsync();
                    if (managementCenter == center && !closing && !closed) center.ShowDialog();
                }
                catch (Exception) { ViewModel.Message = "管理中心尚未打开；暂停采集未完成，请检查相机状态后重试。"; center.Close(); }
            }));
        }
        ViewModel.SelectedPage = page;
        managementCenter.ShowPage(page switch
        {
            "maintenance" => maintenance,
            "diagnostics" => diagnostics ??= CreateDiagnostics(),
            "debug" => debug ??= new StationDebugControl(ViewModel, id => OpenOperatorSettingsAsync(id), ShowDetailsAsync),
            "logs" => logs ??= CreateOperationLogs(),
            _ => throw new ArgumentException("未安装的管理页面")
        });
        managementCenter.UpdateAuthorization(ViewModel.ManagementStatus);
    }
    async void NavigateManagement(string page)
    {
        if (!ViewModel.IsManagementMode) return;
        SetPage(page);
        if (page == "maintenance") { pendingMaintenanceLoad = !ViewModel.HasLoadedConfiguration; await LoadRequestedMaintenanceAsync(); }
    }
    async void CloseManagementCenter(object? sender, CancelEventArgs e)
    {
        if (centerCloseReady || closing || closed) return;
        e.Cancel = true;
        if (centerClosing || sender is not ManagementCenterWindow center) return;
        centerClosing = true; center.SetClosing(true);
        try
        {
            await Dispatcher.Yield();
            await ViewModel.DrainAsync().WaitAsync(TimeSpan.FromSeconds(5));
            if (workspaceOpened && ViewModel.Application is IManagementWorkspace workspace)
            {
                await ViewModel.ServiceAsync(() => workspace.SetManagementWorkspaceAsync(managementWindowId, false, CancellationToken.None));
                workspaceOpened = false;
            }
            if (ViewModel.Application is IWorkstationAdministration service) await service.ExitManagementModeAsync();
            await ViewModel.RefreshManagementModeAsync();
            centerCloseReady = true; center.Close();
        }
        catch (Exception) { center.ShowCloseFailure(); }
        finally { centerClosing = false; center.SetClosing(false); }
    }
    async void Logs_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await AuthenticateManagementAsync(newEntry: managementCenter is null);
            if (ViewModel.IsManagementMode) SetPage("logs");
        }
        catch (Exception) { ViewModel.Message = "日志未能打开，请重新验证管理密码后再试。"; }
    }
    void EnforceManagementModeView()
    {
        maintenance.IsEnabled = ViewModel.IsManagementMode;
        managementCenter?.UpdateAuthorization(ViewModel.ManagementStatus);
        if (!ViewModel.IsManagementMode)
        {
            managementCenter?.DetachPage();
            logs?.Revoke(); debug?.Revoke();
        }
        else if (managementCenter is not null && ViewModel.SelectedPage is "maintenance" or "diagnostics" or "logs" or "debug") ShowManagementPage(ViewModel.SelectedPage);
    }
}

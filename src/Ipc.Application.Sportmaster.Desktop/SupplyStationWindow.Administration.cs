using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Platform;
namespace Ipc.Application.Sportmaster.Desktop;

public partial class SupplyStationWindow
{
    SoftwareLogControl? logs;
    ManagementCenterWindow? managementCenter;
    StationSettingsWindow? stationSettings;
    FrameworkElement? parameterActions;
    Window ManagementDialogOwner => managementCenter is { IsVisible: true } center ? center : stationSettings is { IsEditing: true, IsVisible: true } settings ? settings : this;
    bool ConfirmStationExit() => new SupplyStationExitWindow(ViewModel, activationRequested) { Owner = ManagementDialogOwner }.ShowDialog() == true;
    string dailyPage = "workstation";
    bool openingManagement, centerClosing, centerCloseReady;
    string managementWindowId = Ipc.Platform.Wire.Id();
    bool workspaceOpened, workspaceReady;
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
    async Task<bool> AuthenticateManagementAsync(bool newEntry = false, Window? passwordOwner = null)
    {
        if (ViewModel.Application is not IWorkstationAdministration service) return false;
        // Each new visit requires a password, even if another window granted a temporary lease.
        if (newEntry) await service.ExitManagementModeAsync();
        await ViewModel.RefreshManagementModeAsync();
        if (!ViewModel.ManagementStatus.Available) throw new InvalidOperationException("管理授权服务暂时不可用，请稍后重试；原授权保留。");
        if (!ViewModel.IsManagementMode)
        {
            if (passwordOwner is { IsVisible: false }) return false;
            var password = new ManagementModeWindow(service, "管理中心") { Owner = managementCenter is { IsVisible: true } center ? center : passwordOwner ?? this };
            password.ShowDialog();
            if (!password.Entered) return false;
            await ViewModel.RefreshManagementModeAsync();
            if (!ViewModel.ManagementStatus.Available) throw new InvalidOperationException("暂时无法核对管理授权，请稍后重试；原授权保留。");
            if (!ViewModel.IsManagementMode) throw new UnauthorizedAccessException("管理授权已结束，请重新验证后进入管理中心。");
        }
        await EnsureManagementWorkspaceAsync();
        // Once Application has paused production, retain an owned window even
        // if a later status update locks its content during the native cutover.
        if (workspaceOpened && managementCenter is null && passwordOwner is not StationSettingsWindow && !closing && !closed) ShowManagementPage("diagnostics");
        EnforceManagementModeView();
        return true;
    }
    async Task EnsureManagementWorkspaceAsync()
    {
        if (ViewModel.IsManagementMode && !workspaceOpened && ViewModel.Application is IManagementWorkspace workspace)
        {
            managementWindowId = Ipc.Platform.Wire.Id(); workspaceOpened = true;
            try
            {
                var receipt = await ViewModel.ServiceAsync(() => workspace.SetManagementWorkspaceAsync(managementWindowId, true, CancellationToken.None));
                workspaceReady = true;
                if (receipt.Status == "PausedWithCameraWarning") ViewModel.Message = receipt.Reason;
            }
            catch (Exception opening)
            {
                BusinessReceipt receipt;
                try { receipt = await ViewModel.ServiceAsync(() => workspace.SetManagementWorkspaceAsync(managementWindowId, false, CancellationToken.None)); }
                catch (Exception cleanup)
                {
                    if (managementCenter is null && stationSettings is not { IsEditing: true } && !closing && !closed) ShowManagementPage("diagnostics");
                    throw new AggregateException("管理窗口暂停与恢复均未确认，保留原窗口供核对。", opening, cleanup);
                }
                if (receipt.Status != "Restored") throw new InvalidOperationException("管理窗口的原资源结束尚未确认。");
                workspaceOpened = false;
                debug?.ConfirmWorkspaceClosed(receipt);
                throw;
            }
        }
    }
    async void ManagementMode_Click(object sender, RoutedEventArgs e)
    {
        if (openingManagement || closing || closed || ViewModel.Application is not IWorkstationAdministration service) return;
        if (stationSettings is { IsEditing: true } settings) { settings.Activate(); return; }
        if (managementCenter is { } center) { center.Activate(); return; }
        openingManagement = true;
        try
        {
            await service.ExitManagementModeAsync();
            await ViewModel.RefreshManagementModeAsync();
            if (!closing && !closed) ShowManagementPage("diagnostics");
        }
        catch (Exception) { ViewModel.Message = "供包运维工具未能打开，请检查管理授权服务。"; }
        finally { openingManagement = false; }
    }

    void StationSettings_Click(object sender, RoutedEventArgs e)
    {
        if (closing || closed) return;
        if (stationSettings is { } existing)
        {
            if (existing.WindowState == WindowState.Minimized) existing.WindowState = WindowState.Normal;
            existing.Activate();
            return;
        }
        centerCloseReady = false;
        var options = new StationSettingsWindow(ViewModel, EditStationSettingsAsync) { Owner = this };
        options.Closing += CloseStationSettings;
        options.Closed += (_, _) =>
        {
            options.DetachEditor(); stationSettings = null;
            if (!closing && !closed && options.ReturnToWork) SetPage("workstation");
        };
        stationSettings = options;
        options.Show();
    }

    async Task EditStationSettingsAsync(StationSettingsWindow settings)
    {
        if (openingManagement || centerClosing || closing || closed) return;
        if (managementCenter is { IsVisible: true } center) { center.Activate(); return; }
        openingManagement = true;
        try
        {
            if (!await AuthenticateManagementAsync(newEntry: !settings.IsEditing, settings) || !settings.IsVisible || !workspaceReady) return;
            if (!settings.IsEditing)
            {
                parameterActions ??= maintenance.TakeFooter();
                maintenance.SetStationSettingsLayout(true);
                settings.StartEditing(maintenance);
            }
            settings.UpdateAuthorization();
            if (!ViewModel.HasLoadedConfiguration)
                await ViewModel.RunAsync(ViewModel.EnsureMaintenanceLoadedAsync, "读取本站设置");
        }
        catch (Exception error) { settings.ShowAuthorizationFailure(ChineseInterfaceText.ParameterMessage(error.Message, "暂时无法进入编辑，请重试或检查管理授权服务。")); }
        finally { openingManagement = false; }
    }

    async void CloseStationSettings(object? sender, CancelEventArgs e)
    {
        if (sender is not StationSettingsWindow settings || centerCloseReady || closing || closed) return;
        if (openingManagement || centerClosing) { e.Cancel = true; return; }
        if (!settings.IsEditing) return;
        e.Cancel = true;
        centerClosing = true;
        try
        {
            await Dispatcher.Yield();
            if (ConfigurationCloseGuard.HasUnsaved(ViewModel, maintenance) && MessageBox.Show(settings,
                "本次更改会保留在当前运行的供包台，重新授权后可继续编辑。要关闭设置吗？", "有未保存的更改",
                MessageBoxButton.OKCancel, MessageBoxImage.Information, MessageBoxResult.Cancel) != MessageBoxResult.OK)
            { settings.CancelReturn(); return; }
            settings.IsEnabled = false;
            await ReleaseManagedWorkspaceAsync(settings.LockAfterRelease);
            centerCloseReady = true;
            settings.Close();
        }
        catch (Exception error) { settings.ShowAuthorizationFailure("设置尚未关闭。" + ChineseInterfaceText.ParameterMessage(error.Message, "资源释放未确认，请重试。")); }
        finally { centerCloseReady = false; centerClosing = false; settings.IsEnabled = true; }
    }

    async Task OpenManagementCenterAsync(string page, Window passwordOwner)
    {
        if (openingManagement || closing || closed) return;
        if (stationSettings is { IsEditing: true } settingsInUse) { settingsInUse.Activate(); return; }
        if (managementCenter is { } center) { if (center.IsVisible) center.Activate(); return; }
        openingManagement = true; ManagementCenterEntry.IsEnabled = false;
        try
        {
            if (await AuthenticateManagementAsync(newEntry: true, passwordOwner) && ViewModel.IsManagementMode && !closing && !closed)
                NavigateManagement(page);
        }
        catch (Exception error)
        {
            ViewModel.Message = ChineseInterfaceText.ParameterMessage(error.Message, "管理中心未能打开，请稍后重试或检查管理授权服务。");
            if (passwordOwner is StationSettingsWindow settings && settings.IsVisible) settings.ShowAuthorizationFailure();
        }
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
        if (stationSettings is { IsEditing: true } settings) { settings.Activate(); return; }
        if (managementCenter is null)
        {
            maintenance.SetStationSettingsLayout(false);
            centerCloseReady = false;
            var center = new ManagementCenterWindow(ViewModel, NavigateManagement, async () =>
            {
                try { if (await AuthenticateManagementAsync() && ViewModel.SelectedPage == "maintenance") NavigateManagement("maintenance"); }
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
                    if (managementCenter == center && !closing && !closed)
                    {
                        ShowManagementPage(ViewModel.SelectedPage);
                        center.ShowDialog();
                    }
                }
                catch (Exception) { ViewModel.Message = "管理中心尚未打开；暂停采集未完成，请检查相机状态后重试。"; center.Close(); }
            }));
        }
        ViewModel.SelectedPage = page;
        if (ViewModel.IsManagementMode && workspaceReady) managementCenter.ShowPage(page switch
        {
            "maintenance" => maintenance,
            "diagnostics" => ShowDiagnosticOverview(),
            "debug" => debug ??= new StationDebugControl(ViewModel, id => OpenOperatorSettingsAsync(id), ShowDetailsAsync),
            "logs" => logs ??= CreateOperationLogs(),
            "maintenance-center" => ShowMaintenanceTools(),
            _ => throw new ArgumentException("未安装的管理页面")
        }, page == "maintenance" ? parameterActions ??= maintenance.TakeFooter() : null);
        else managementCenter.DetachPage();
        managementCenter.ShowWorkspaceState(workspaceReady);
        managementCenter.UpdateAuthorization(ViewModel.ManagementStatus);
    }
    async void NavigateManagement(string page)
    {
        if (!ViewModel.IsManagementMode || !workspaceReady) { ShowManagementPage(page); return; }
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
            if (debug is not null) await debug.QuiesceAsync();
            if (logs is not null) await logs.SuspendAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await ReleaseManagedWorkspaceAsync(center.LockAfterWorkspaceRelease);
            centerCloseReady = true; center.Close();
        }
        catch (Exception error) { center.ShowCloseFailure(ChineseInterfaceText.ParameterMessage(error.Message, "资源释放或结果核对未完成。"), Ipc.Platform.DiagnosticText.Protect(error.ToString())); }
        finally { centerClosing = false; center.SetClosing(false); if (workspaceOpened) logs?.ResumeAfterCancelledClose(); }
    }
    async Task ReleaseManagedWorkspaceAsync(Action lockEditor)
    {
        await ViewModel.DrainAsync().WaitAsync(TimeSpan.FromSeconds(5));
        if (workspaceOpened && ViewModel.Application is IManagementWorkspace workspace)
        {
            var receipt = await ViewModel.ServiceAsync(() => workspace.SetManagementWorkspaceAsync(managementWindowId, false, CancellationToken.None));
            if (receipt.Status != "Restored") throw new InvalidOperationException("管理窗口的原资源结束尚未确认。");
            workspaceOpened = false;
            workspaceReady = false;
            lockEditor();
            debug?.ConfirmWorkspaceClosed(receipt);
        }
        else if (debug is not null) await debug.CloseAsync();
        if (ViewModel.Application is IWorkstationAdministration service) await service.ExitManagementModeAsync();
        await ViewModel.RefreshManagementModeAsync();
    }
    async void Logs_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (await AuthenticateManagementAsync(newEntry: managementCenter is null) && ViewModel.IsManagementMode) SetPage("logs");
        }
        catch (Exception) { ViewModel.Message = "日志未能打开，请重新验证管理密码后再试。"; }
    }
    void EnforceManagementModeView()
    {
        maintenance.IsEnabled = ViewModel.IsManagementMode;
        stationSettings?.UpdateAuthorization();
        managementCenter?.ShowWorkspaceState(workspaceReady);
        managementCenter?.UpdateAuthorization(ViewModel.ManagementStatus);
        if (!ViewModel.IsManagementMode)
        {
            managementCenter?.DetachPage();
            logs?.Revoke(); debug?.Revoke();
        }
        else if (managementCenter is not null && ViewModel.SelectedPage is "maintenance" or "diagnostics" or "logs" or "debug" or "maintenance-center") ShowManagementPage(ViewModel.SelectedPage);
    }
}

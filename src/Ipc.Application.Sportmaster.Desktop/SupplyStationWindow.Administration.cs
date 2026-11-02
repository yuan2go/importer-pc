using System.Windows;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;
namespace Ipc.Application.Sportmaster.Desktop;

public partial class SupplyStationWindow
{
    SoftwareLogControl? logs;
    string? restrictedReturn;
    async void ManagementRenew_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Application is not IWorkstationAdministration service) return;
        await ViewModel.RunAsync(async () => { await service.RenewManagementModeAsync(); await ViewModel.RefreshManagementModeAsync(); ViewModel.Message = "管理模式可继续使用十五分钟；已有调试轮次和维护票据仍遵守原期限。"; }, "延长管理模式");
    }
    async void ManagementMode_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Application is not IWorkstationAdministration service) { ViewModel.Message = "此组合尚未提供管理模式。"; return; }
        try { await ViewModel.RefreshManagementModeAsync(); if (ViewModel.IsManagementMode) await service.ExitManagementModeAsync(); else new ManagementModeWindow(service) { Owner = this }.ShowDialog(); await ViewModel.RefreshManagementModeAsync(); EnforceManagementModeView(); if (ViewModel.IsManagementMode && restrictedReturn is { } page) { restrictedReturn = null; SetPage(page); } }
        catch (Exception error) { ViewModel.Message = error.Message; }
    }
    async void Logs_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await ViewModel.RefreshManagementModeAsync();
            if (!ViewModel.IsManagementMode && ViewModel.Application is IWorkstationAdministration service)
            { new ManagementModeWindow(service) { Owner = this }.ShowDialog(); await ViewModel.RefreshManagementModeAsync(); }
            await ViewModel.RequireManagementModeAsync(); restrictedReturn = null; SetPage("logs");
        }
        catch (Exception error) { ViewModel.Message = error.Message; }
    }
    void EnforceManagementModeView()
    {
        maintenance.IsEnabled = ViewModel.IsManagementMode;
        if (ViewModel.IsManagementMode && restrictedReturn is { } previous) { restrictedReturn = null; SetPage(previous); }
        if (!ViewModel.IsManagementMode)
        {
            logs?.Revoke();
            debug?.Revoke();
            if (ViewModel.SelectedPage is "maintenance" or "diagnostics" or "logs" or "debug" or "maintenance-center") { restrictedReturn = ViewModel.SelectedPage; SetPage("workstation"); }
        }
    }
}

using System.Windows;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;
namespace Ipc.Application.Sportmaster.Desktop;

public partial class SupplyStationWindow
{
    SoftwareLogControl? logs;
    string? restrictedReturn;
    async void ManagementMode_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Application is not IWorkstationAdministration service) { ViewModel.Message = "此组合尚未提供管理模式。"; return; }
        try { await ViewModel.RefreshManagementModeAsync(); if (ViewModel.IsManagementMode) await service.ExitManagementModeAsync(); else new ManagementModeWindow(service) { Owner = this }.ShowDialog(); await ViewModel.RefreshManagementModeAsync(); EnforceManagementModeView(); if (ViewModel.IsManagementMode && restrictedReturn is { } page) { restrictedReturn = null; SetPage(page); } }
        catch (Exception error) { ViewModel.Message = error.Message; }
    }
    async void Logs_Click(object sender, RoutedEventArgs e)
    {
        try { await ViewModel.RequireManagementModeAsync(); SetPage("logs"); }
        catch (Exception error) { ViewModel.Message = error.Message; }
    }
    void EnforceManagementModeView()
    {
        maintenance.IsEnabled = ViewModel.IsManagementMode;
        if (ViewModel.IsManagementMode && restrictedReturn is { } previous) { restrictedReturn = null; SetPage(previous); }
        if (!ViewModel.IsManagementMode)
        {
            logs?.Revoke();
            if (ViewModel.SelectedPage is "maintenance" or "diagnostics" or "logs") { restrictedReturn = ViewModel.SelectedPage; SetPage("workstation"); }
        }
    }
}

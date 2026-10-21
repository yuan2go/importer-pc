using System.Windows;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;
namespace Ipc.Application.Sportmaster.Desktop;

public partial class SupplyStationWindow
{
    SoftwareLogControl? logs;
    async void Administrator_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Application is not IWorkstationAdministration service) { ViewModel.Message = "此组合尚未提供软件管理员服务。"; return; }
        new AdministratorWindow(service) { Owner = this }.ShowDialog();
        await ViewModel.RefreshAdministratorAsync(); EnforceAdministratorView();
    }
    async void Logs_Click(object sender, RoutedEventArgs e)
    {
        try { await ViewModel.RequireAdministratorAsync(); SetPage("logs"); }
        catch (Exception error) { ViewModel.Message = error.Message; }
    }
    void EnforceAdministratorView()
    {
        maintenance.IsEnabled = ViewModel.IsAdministrator;
        if (!ViewModel.IsAdministrator)
        {
            logs?.Revoke();
            if (ViewModel.SelectedPage is "maintenance" or "diagnostics" or "logs") SetPage("workstation");
        }
    }
}

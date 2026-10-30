using System.Windows;
using System.Windows.Controls;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;

namespace Ipc.Application.Sportmaster.Desktop;

public partial class SupplyStationWindow
{
    readonly Grid workContent = new();
    void BuildOperatorWorkspace()
    {
        workContent.Children.Add(work);
    }
    async Task OpenOperatorSettingsAsync(string? deviceId = null, string? instanceId = null)
    {
        try
        {
            await ViewModel.RefreshManagementModeAsync();
            if (!ViewModel.IsManagementMode && ViewModel.Application is IWorkstationAdministration service)
            {
                new ManagementModeWindow(service) { Owner = this }.ShowDialog();
                await ViewModel.RefreshManagementModeAsync();
            }
            if (!ViewModel.IsManagementMode) { ViewModel.Message = "未进入管理模式；参数未修改。"; return; }
            SetPage("maintenance");
            await ViewModel.RunAsync(ViewModel.EnsureMaintenanceLoadedAsync, "读取连接参数");
            if (ViewModel.SelectedPage != "maintenance" || !ViewModel.IsManagementMode || !ViewModel.HasLoadedConfiguration) return;
            var target = deviceId is null ? instanceId : ViewModel.ReaderDrafts.FirstOrDefault(r => r.Original.DeviceId == deviceId)?.Capability.InstanceId;
            if (target is not null) maintenance.NavigateToParameter(target + "/Provider");
            else if (deviceId is not null) ViewModel.Message = "当前配置中未找到该相机，请核对设备设置。";
        }
        catch (Exception error) { ViewModel.Message = "未打开参数设置：" + error.Message; }
    }
}

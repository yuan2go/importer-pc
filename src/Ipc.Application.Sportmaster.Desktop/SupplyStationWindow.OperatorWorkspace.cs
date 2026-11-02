using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;

namespace Ipc.Application.Sportmaster.Desktop;

public partial class SupplyStationWindow
{
    readonly Grid workContent = new();
    string recentInputPosition = "left";
    bool focusRestoreQueued;
    void BuildOperatorWorkspace()
    {
        workContent.Children.Add(work);
        Deactivated += (_, _) => ViewModel.SetInputTarget("");
        Activated += (_, _) => RestoreBarcodeFocus();
        Loaded += (_, _) => RestoreBarcodeFocus();
        ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ViewModel.InputTargetPosition) && ViewModel.InputTargetPosition is "left" or "right")
                recentInputPosition = ViewModel.InputTargetPosition;
            if (args.PropertyName == nameof(ViewModel.SelectedPage) && ViewModel.SelectedPage == "workstation") RestoreBarcodeFocus();
        };
        PreviewTextInput += (_, args) =>
        {
            // Route scanner text away from workspace actions before its Enter.
            // Explicit Tab/Enter navigation and other editors remain native.
            if (!CanRestoreBarcodeFocus || Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) ||
                args.Text.Length == 0 || args.Text.Any(char.IsControl) || Keyboard.FocusedElement is not ButtonBase) return;
            if (RecentPosition() is { } position) { args.Handled = true; position.ReceiveScannerText(args.Text); }
        };
    }
    bool CanRestoreBarcodeFocus => IsLoaded && IsActive && IsEnabled && !closing && !closed &&
        ViewModel.SelectedPage == "workstation" && ViewModel.ViewportReady && !OwnedWindows.OfType<Window>().Any(w => w.IsVisible && w.IsActive);
    SupplyStationPosition? RecentPosition() => work.Children.OfType<SupplyStationPosition>().FirstOrDefault(c =>
        c.DataContext is SupplyStationPositionViewModel p && p.PositionId == recentInputPosition);
    void RestoreBarcodeFocus()
    {
        if (focusRestoreQueued) return;
        focusRestoreQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            focusRestoreQueued = false;
            if (CanRestoreBarcodeFocus) RecentPosition()?.FocusBarcodeInput();
        }));
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

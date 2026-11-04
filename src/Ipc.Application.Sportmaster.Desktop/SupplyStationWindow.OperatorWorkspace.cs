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
        InitializePace();
        workContent.Children.Add(work);
        Deactivated += (_, _) => ViewModel.SetInputTarget("");
        Activated += (_, _) => RestoreBarcodeFocus();
        Loaded += (_, _) => RestoreBarcodeFocus();
        // F2 / F3 choose the left / right input position without the mouse. A scanner sends text and Enter only,
        // so it cannot trigger these keys; other pages, dialogs and owned windows keep their own keys.
        PreviewKeyDown += (_, args) =>
        {
            if (args.Key is not (Key.F2 or Key.F3) || Keyboard.Modifiers != ModifierKeys.None || !CanRestoreBarcodeFocus) return;
            var id = args.Key == Key.F2 ? "left" : "right";
            if (work.Children.OfType<SupplyStationPosition>().FirstOrDefault(c => c.DataContext is SupplyStationPositionViewModel p && p.PositionId == id) is not { IsVisible: true, IsEnabled: true } target) return;
            args.Handled = true; recentInputPosition = id; target.FocusBarcodeInput();
        };
        ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ViewModel.InputTargetPosition) && ViewModel.InputTargetPosition is "left" or "right")
                recentInputPosition = ViewModel.InputTargetPosition;
            if (args.PropertyName == nameof(ViewModel.SelectedPage)) ViewModel.NotifyPaceVisibility();
            if (args.PropertyName == nameof(ViewModel.Fresh) && !ViewModel.Fresh) ViewModel.SetPace(null);
            if (args.PropertyName == nameof(ViewModel.SelectedPage) && ViewModel.SelectedPage == "workstation") RestoreBarcodeFocus();
        };
        PreviewTextInput += (_, args) =>
        {
            // Route scanner text away from workspace actions before its Enter.
            // Explicit Tab/Enter navigation and other editors remain native.
            if (!CanRestoreBarcodeFocus || Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) ||
                args.Text.Length == 0 || args.Text.Any(char.IsControl)) return;
            if (RecentPosition() is { } position && (Keyboard.FocusedElement is ButtonBase ||
                position.IsKeyboardFocusWithin && Keyboard.FocusedElement is TextBox { IsReadOnly: true }))
            { args.Handled = true; position.ReceiveScannerText(args.Text); }
        };
    }
    void InitializePace()
    {
        try { ViewModel.SetPaceVisibility(Ipc.Platform.PresentationPreferences.Read("sportmaster", "pace") ?? true); }
        catch (Exception error) { ViewModel.Message = ChineseInterfaceText.ParameterMessage(error.Message, "效率显示偏好未能读取，原文件保留；请检查本机设置文件与访问权限。"); }
        void Preference(string app, string surface, bool visible) { if (app == "sportmaster" && surface == "pace") Dispatcher.InvokeAsync(() => ViewModel.SetPaceVisibility(visible)); }
        Ipc.Platform.PresentationPreferences.DisplayChanged += Preference;
        Closed += (_, _) => Ipc.Platform.PresentationPreferences.DisplayChanged -= Preference;
        var paceTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        CancellationTokenSource? lifetime = null; bool reading = false; StationPace.Value? last = null;
        async Task Read()
        {
            ViewModel.SetPace(last);
            if (reading || lifetime is null || ViewModel.SelectedPage != "workstation" || !ViewModel.Fresh) return;
            reading = true; var token = lifetime.Token;
            try { var value = await StationPace.Read(ViewModel.Application).WaitAsync(token); if (!token.IsCancellationRequested) { last = value; ViewModel.SetPace(value); } }
            catch (OperationCanceledException) { }
            catch (Exception error) { last = null; ViewModel.SetPace(null); PaceBadge.ToolTip = ChineseInterfaceText.ParameterMessage(error.Message, "作业效率读取中断；持续失败时请在诊断页导出诊断记录。"); }
            finally { reading = false; }
        }
        paceTimer.Tick += async (_, _) => await Read();
        Loaded += async (_, _) => { lifetime = new(); paceTimer.Start(); await Read(); };
        Closed += (_, _) => { paceTimer.Stop(); lifetime?.Cancel(); lifetime?.Dispose(); lifetime = null; };
        var menu = new ContextMenu(); var hide = new MenuItem { Header = "隐藏作业效率", ToolTip = "只改变当前 Windows 用户的显示偏好" };
        hide.Click += async (_, _) => { try { await ViewModel.RequireManagementModeAsync(); Ipc.Platform.PresentationPreferences.Save("sportmaster", "pace", false); ViewModel.SetPaceVisibility(false); } catch (Exception error) { ViewModel.Message = error.Message; } }; menu.Items.Add(hide); PaceBadge.ContextMenu = menu;
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

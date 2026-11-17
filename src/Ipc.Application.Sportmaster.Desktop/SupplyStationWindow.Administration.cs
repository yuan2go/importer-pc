using System.Windows;
using Ipc.Application.ParcelFlow.Desktop;

namespace Ipc.Application.Sportmaster.Desktop;

public partial class SupplyStationWindow
{
    StationSettingsWindow? settingsWindow;
    Window ManagementDialogOwner => settingsWindow is { IsVisible: true } settings ? settings : this;
    bool ConfirmStationExit() => new SupplyStationExitWindow(ViewModel, activationRequested) { Owner = ManagementDialogOwner }.ShowDialog() == true;
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
    void StationSettings_Click(object sender, RoutedEventArgs e)
    {
        if (ActivateSettingsWindow()) return;
        try
        {
            var settings = new StationSettingsWindow(ViewModel, maintenance) { Owner = this };
            settingsWindow = settings;
            settings.Closed += (_, _) => { if (settingsWindow == settings) settingsWindow = null; EnforceManagementModeView(); };
            settings.Show();
        }
        catch (Exception error) { ViewModel.Message = ChineseInterfaceText.ParameterMessage(error.Message, "设置窗口未能打开，原输入保留。"); }
    }
    internal void ReturnToWorkstation()
    {
        settingsWindow?.Hide(); SetPage("workstation"); Activate();
    }
    void EnforceManagementModeView() => maintenance.IsEnabled = ViewModel.IsManagementMode;
    async void ProductRestart_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.HasConfigurationActivationFailure) { StationSettings_Click(sender, e); return; }
        await RequestActivation();
    }
}

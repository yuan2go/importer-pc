using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;

namespace Ipc.Application.Sportmaster.Desktop;

public partial class SupplyStationWindow
{
    readonly Grid workContent = new();
    void BuildOperatorWorkspace()
    {
        var workStyle = new Style(typeof(Grid));
        var hide = new DataTrigger { Binding = new Binding(nameof(ViewModel.ShowSetupGuidance)), Value = true };
        hide.Setters.Add(new Setter(VisibilityProperty, Visibility.Collapsed)); workStyle.Triggers.Add(hide); work.Style = workStyle;
        workContent.Children.Add(work);
        var body = new StackPanel { Margin = new(32), MaxWidth = 680, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        body.Children.Add(new TextBlock { Text = "暂不能供包", FontSize = UiTypography.Exception, FontWeight = UiTypography.Emphasis, Margin = new(0, 0, 0, 24) });
        var heading = new TextBlock { FontSize = UiTypography.PageTitle, FontWeight = UiTypography.Emphasis, TextWrapping = TextWrapping.Wrap };
        heading.SetBinding(TextBlock.TextProperty, new Binding(nameof(ViewModel.SetupHeading))); body.Children.Add(heading);
        var missing = new TextBlock { FontSize = UiTypography.Position, TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 24) };
        missing.SetBinding(TextBlock.TextProperty, new Binding(nameof(ViewModel.SetupItems))); body.Children.Add(missing);
        var instruction = new TextBlock { FontSize = UiTypography.Body, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 20) };
        instruction.SetBinding(TextBlock.TextProperty, new Binding(nameof(ViewModel.SetupInstruction))); body.Children.Add(instruction);
        var configure = new Button { Content = "去参数设置", MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Left, Padding = new(20, 8, 20, 8) };
        configure.Click += async (_, _) => await OpenOperatorSettingsAsync(); body.Children.Add(configure);
        var feedback = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxHeight = 64 };
        feedback.SetBinding(TextBlock.TextProperty, new Binding(nameof(ViewModel.WorkFeedback))); body.Children.Add(feedback);
        body.Children.Add(new TextBlock { Text = "修改参数需要进入管理模式。", FontSize = UiTypography.Label, Margin = new(0, 8, 0, 0) });
        var guide = new ScrollViewer { Name = "ConnectionSetupGuide", Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        guide.SetBinding(VisibilityProperty, new Binding(nameof(ViewModel.ShowSetupGuidance)) { Converter = new BooleanToVisibilityConverter() });
        workContent.Children.Add(guide);
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

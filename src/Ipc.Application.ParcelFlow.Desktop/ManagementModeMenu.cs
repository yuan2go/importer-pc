using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Ipc.Platform;

namespace Ipc.Application.ParcelFlow.Desktop;

// A compact view of the existing authority. It owns no session or renewal state.
public sealed class ManagementModeMenu : Button
{
    public static readonly DependencyProperty StatusProperty = DependencyProperty.Register(nameof(Status), typeof(ManagementModeStatus), typeof(ManagementModeMenu),
        new PropertyMetadata(new ManagementModeStatus(false, false, null), (owner, _) => ((ManagementModeMenu)owner).Refresh()));
    public ManagementModeStatus Status { get => (ManagementModeStatus)GetValue(StatusProperty); set => SetValue(StatusProperty, value); }
    string unauthorizedLabel = "普通模式";
    public string UnauthorizedLabel { get => unauthorizedLabel; set { unauthorizedLabel = value; Refresh(); } }
    public event RoutedEventHandler? ModeActionRequested;
    readonly TextBlock label = new();
    readonly MenuItem state = new() { IsEnabled = false };
    readonly MenuItem expires = new() { IsEnabled = false };
    readonly MenuItem action = new();
    readonly ContextMenu menu = new() { FontFamily = UiTypography.Interface, FontSize = UiTypography.Body };
    public ManagementModeMenu()
    {
        FontSize = UiTypography.Body; FontWeight = FontWeights.Normal; MinHeight = 44; Padding = new(10, 6, 10, 6);
        Background = new SolidColorBrush(Color.FromRgb(246, 248, 251)); Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)); BorderBrush = Brushes.Transparent;
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new System.Windows.Shapes.Path { Data = Geometry.Parse("M3,7 V4 A4,4 0 0 1 11,4 V7 M1,7 H13 V17 H1 Z M7,10 V14"), Width = 14, Height = 17, Stroke = Foreground, StrokeThickness = 1.3, Stretch = Stretch.Uniform, Margin = new(0, 0, 7, 0) });
        content.Children.Add(label);
        content.Children.Add(new System.Windows.Shapes.Path { Data = Geometry.Parse("M0,0 L4,4 L8,0"), Width = 8, Height = 4, Stroke = Foreground, StrokeThickness = 1.3, Stretch = Stretch.Uniform, Margin = new(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        Content = content;
        menu.Items.Add(state); menu.Items.Add(expires); menu.Items.Add(new Separator()); menu.Items.Add(action);
        action.Click += (_, _) => ModeActionRequested?.Invoke(this, new RoutedEventArgs());
        ContextMenu = menu; Refresh();
        System.Windows.Automation.AutomationProperties.SetName(this, "管理模式菜单");
    }
    protected override void OnClick()
    {
        Refresh(); menu.PlacementTarget = this; menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom; menu.IsOpen = true;
    }
    void Refresh()
    {
        if (label is null) return;
        var current = Status;
        label.Text = !current.Available ? "管理不可用" : current.Authorized ? "管理模式" : UnauthorizedLabel;
        state.Header = !current.Available ? "管理授权服务不可用" : current.Authorized ? "管理权限已开启" : "管理权限未开启";
        expires.Visibility = current.Authorized ? Visibility.Visible : Visibility.Collapsed;
        expires.Header = current.ExpiresAt is { } end ? "将于 " + end.ToLocalTime().ToString("HH:mm") + " 自动退出" : "";
        action.Header = current.Authorized ? "退出管理模式" : current.Configured || current.NeedsLegacyConversion ? "重新验证" : "设置管理密码";
        action.IsEnabled = current.Available;
    }
}

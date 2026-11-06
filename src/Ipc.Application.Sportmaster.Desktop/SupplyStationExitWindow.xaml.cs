using System.Windows;
using System.Windows.Input;
using Ipc.Application.ParcelFlow.Desktop;

namespace Ipc.Application.Sportmaster.Desktop;

public partial class SupplyStationExitWindow : Window
{
    public SupplyStationExitWindow(ParcelViewModel viewModel, bool activation = false)
    {
        InitializeComponent();
        UiTypography.Apply(this);
        DataContext = viewModel;
        if (activation)
        {
            Title = "重启供包台";
            ExitHeading.Text = "确认重启供包台？";
            ExitDescription.Text = "将停止当前供包业务，关闭相关窗口，再重新打开以应用配置。";
            ContinueExitButton.Content = "继续重启";
        }
        Loaded += (_, _) =>
        {
            // Retain the footer on small work areas; only the body may scroll.
            Width = Math.Min(580, Math.Min(Owner?.ActualWidth ?? SystemParameters.WorkArea.Width, SystemParameters.WorkArea.Width) - 24);
            Height = Math.Min(340, Math.Min(Owner?.ActualHeight ?? SystemParameters.WorkArea.Height, SystemParameters.WorkArea.Height) - 24);
            CancelExitButton.Focus();
            Keyboard.Focus(CancelExitButton);
        };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; DialogResult = false; } };
    }
    void CancelExit(object sender, RoutedEventArgs e) => DialogResult = false;
    void ContinueExit(object sender, RoutedEventArgs e) => DialogResult = true;
    void DragHeader(object sender, MouseButtonEventArgs e) { if (e.ChangedButton == MouseButton.Left) DragMove(); }
}

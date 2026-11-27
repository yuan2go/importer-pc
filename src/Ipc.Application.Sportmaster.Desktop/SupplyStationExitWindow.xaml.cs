using Ipc.Presentation.Wpf;
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
            Title = UiText.Get("Work.Restart");
            ExitHeading.Text = UiText.Get("Work.RestartPrompt");
            ExitDescription.Text = UiText.Get("Work.RestartScope");
            ContinueExitButton.Content = UiText.Get("Work.ContinueRestart");
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

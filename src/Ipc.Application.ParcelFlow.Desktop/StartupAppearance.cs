using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Ipc.Application.ParcelFlow.Desktop;

// Limited to the installation and authorization surfaces; parameter controls keep their own styles.
public static class StartupAppearance
{
    public static void Apply(Window window)
    {
        window.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Ipc.Application.ParcelFlow.Desktop;component/StartupAppearance.xaml", UriKind.Relative) });
        window.Background = Brushes.White; window.Foreground = new SolidColorBrush(Color.FromRgb(32, 37, 45));
        UiTypography.Apply(window);
        window.UseLayoutRounding = true;
        FitToWorkArea(window);
    }
    public static void FitToWorkArea(Window window)
    {
        void Fit()
        {
            var work = SystemParameters.WorkArea;
            window.MaxWidth = Math.Max(320, work.Width - 16); window.MaxHeight = Math.Max(240, work.Height - 16);
            window.MinWidth = Math.Min(window.MinWidth, window.MaxWidth); window.MinHeight = Math.Min(window.MinHeight, window.MaxHeight);
        }
        window.SourceInitialized += (_, _) => Fit(); window.DpiChanged += (_, _) => Fit(); Fit();
    }
    public static void Primary(Button button) => button.SetResourceReference(FrameworkElement.StyleProperty, "StartupPrimary");
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace Ipc.Presentation.Wpf;

// A view model supplies a semantic key. The target keeps a DynamicResource reference
// so already-rendered controls respond to palette changes as well as state changes.
public static class PaletteBrush
{
    public static readonly DependencyProperty ForegroundKeyProperty = Register("ForegroundKey", TextElement.ForegroundProperty);
    public static readonly DependencyProperty BackgroundKeyProperty = Register("BackgroundKey", Control.BackgroundProperty);

    public static string? GetForegroundKey(DependencyObject target) => (string?)target.GetValue(ForegroundKeyProperty);
    public static void SetForegroundKey(DependencyObject target, string? value) => target.SetValue(ForegroundKeyProperty, value);
    public static string? GetBackgroundKey(DependencyObject target) => (string?)target.GetValue(BackgroundKeyProperty);
    public static void SetBackgroundKey(DependencyObject target, string? value) => target.SetValue(BackgroundKeyProperty, value);

    static DependencyProperty Register(string name, DependencyProperty targetProperty) =>
        DependencyProperty.RegisterAttached(name, typeof(string), typeof(PaletteBrush), new PropertyMetadata(null, (target, change) =>
        {
            if (target is not FrameworkElement element) return;
            if (change.NewValue is string key) element.SetResourceReference(targetProperty, key);
            else element.ClearValue(targetProperty);
        }));
}

using System.Windows;

namespace Ipc.Presentation.Wpf;

// Colours come from the Workstation theme keys (Ink, Muted, Line, Surface, Background, Soft, Accent*, Success, Warning, Danger);
// code never writes colour values. The reference resolves where the element is shown.
public static class ThemeResources
{
    public static T Themed<T>(this T element, DependencyProperty property, string key) where T : FrameworkElement
    {
        element.SetResourceReference(property, key);
        return element;
    }
}

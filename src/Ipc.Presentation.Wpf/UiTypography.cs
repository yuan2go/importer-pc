using System.Windows;
using System.Windows.Media;

namespace Ipc.Presentation.Wpf;

/// <summary>Built-in presentation roles shared by XAML and code-created surfaces.</summary>
public static partial class UiTypography
{
    public static FontFamily Interface { get; } = new(new Uri("pack://application:,,,/Ipc.Presentation.Wpf;component/"),
        "./Fonts/#Noto Sans CJK SC");
    // Identifiers and technical text share the shipped family; the role retains
    // its own sizing without introducing a system-font dependency.
    public static FontFamily Code => Interface;
    public static FontWeight Regular => FontWeights.Normal;
    public static FontWeight Emphasis => FontWeights.Medium;

    public static void Apply(Window window)
    {
        window.FontFamily = Interface;
        window.FontSize = Body;
        window.FontWeight = Regular;
        window.UseLayoutRounding = true;
    }
}

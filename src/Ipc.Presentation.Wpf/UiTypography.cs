using System.Windows;
using System.Windows.Media;

namespace Ipc.Presentation.Wpf;

/// <summary>Built-in presentation roles shared by XAML and code-created surfaces.</summary>
public static partial class UiTypography
{
    public static FontFamily Interface { get; } = new(new Uri("pack://application:,,,/Ipc.Presentation.Wpf;component/"),
        "./Fonts/#Noto Sans CJK SC, Microsoft YaHei UI, Segoe UI");
    public static FontFamily Code { get; } = new(new Uri("pack://application:,,,/Ipc.Presentation.Wpf;component/"),
        "Consolas, ./Fonts/#Noto Sans CJK SC, Microsoft YaHei UI, Segoe UI");
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

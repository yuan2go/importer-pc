using System.Windows.Media;
namespace Ipc.Presentation.Wpf;

// WPF objects are frozen; the source tokens remain UI-framework independent.
public static class OperatorBrushes
{
    static SolidColorBrush Brush(string value)
    { var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value)); brush.Freeze(); return brush; }
    public static SolidColorBrush Ink { get; } = Brush(OperatorTokens.Ink);
    public static SolidColorBrush Muted { get; } = Brush(OperatorTokens.Muted);
    public static SolidColorBrush Surface { get; } = Brush(OperatorTokens.Surface);
    public static SolidColorBrush Line { get; } = Brush(OperatorTokens.Line);
    public static SolidColorBrush Accent { get; } = Brush(OperatorTokens.Accent);
    public static SolidColorBrush AccentSoft { get; } = Brush(OperatorTokens.AccentSoft);
    public static SolidColorBrush Neutral { get; } = Brush(OperatorTokens.Neutral);
    public static SolidColorBrush Danger { get; } = Brush(OperatorTokens.Danger);
}

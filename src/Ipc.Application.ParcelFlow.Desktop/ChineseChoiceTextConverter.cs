using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
namespace Ipc.Application.ParcelFlow.Desktop;

public sealed class ChineseChoiceTextConverter : IValueConverter
{
    public static DataTemplate CreateTemplate()
    {
        var caption = new FrameworkElementFactory(typeof(TextBlock));
        caption.SetBinding(TextBlock.TextProperty, new Binding { Converter = new ChineseChoiceTextConverter() });
        return new DataTemplate { VisualTree = caption };
    }
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => ChineseInterfaceText.Choice(value);
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

// Ordinary parameter feedback is a display projection. The original remains in diagnostics.
public sealed class ParameterMessageTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => ChineseInterfaceText.ParameterMessage(value as string ?? "");
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class PositionNameTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => ParcelViewModel.PositionName(value as string ?? "main");
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;

namespace Ipc.Presentation.Wpf;

public sealed class LocalizedFormatExtension(string key, string path) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var format = new MultiBinding { Converter = FormatValue.Instance, Mode = BindingMode.OneWay };
        format.Bindings.Add(new Binding(path));
        format.Bindings.Add(new Binding("[" + key + "]") { Source = UiText.Current });
        return format.ProvideValue(serviceProvider);
    }

    sealed class FormatValue : IMultiValueConverter
    {
        internal static FormatValue Instance { get; } = new();
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
            values[0] == DependencyProperty.UnsetValue || values[1] is not string pattern
                ? DependencyProperty.UnsetValue : string.Format(CultureInfo.CurrentUICulture, pattern, values[0]);
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}

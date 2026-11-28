using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;

namespace Ipc.Presentation.Wpf;

// Bind only labels authored by this application, never live device or business data.
public sealed class LocalizedLabelExtension(string path) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var label = new MultiBinding { Converter = LocalizeLabel.Instance, Mode = BindingMode.OneWay };
        label.Bindings.Add(new Binding(path));
        label.Bindings.Add(new Binding(nameof(UiText.CurrentLanguage)) { Source = UiText.Current });
        return label.ProvideValue(serviceProvider);
    }

    sealed class LocalizeLabel : IMultiValueConverter
    {
        internal static LocalizeLabel Instance { get; } = new();
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
            values[0] is string label ? UiText.Authored(label) : DependencyProperty.UnsetValue;
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}

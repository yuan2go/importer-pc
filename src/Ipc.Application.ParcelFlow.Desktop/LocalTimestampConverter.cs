using System.Globalization;
using System.Windows.Data;

namespace Ipc.Application.ParcelFlow.Desktop;

// Presentation only: original offsets remain in the operation/log facts and exports.
public sealed class LocalTimestampConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is DateTimeOffset timestamp ? timestamp.ToLocalTime() : value;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

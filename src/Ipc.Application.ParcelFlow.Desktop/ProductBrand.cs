using System.Windows;
using System.Windows.Media.Imaging;

namespace Ipc.Application.ParcelFlow.Desktop;

public static class ProductBrand
{
    public static void Apply(Window window) => window.Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Ipc.Application.ParcelFlow.Desktop;component/Assets/importer-pc.ico"));
}

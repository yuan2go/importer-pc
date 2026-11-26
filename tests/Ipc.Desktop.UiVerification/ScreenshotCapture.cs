using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

static partial class Program
{
    static void CaptureInternal(string evidence, string name, FrameworkElement visual, List<string> lines)
    {
        var dpi = VisualTreeHelper.GetDpi(visual);
        if (Environment.GetEnvironmentVariable("IPC_EXPECTED_DPI") is { } expected &&
            Math.Abs(dpi.DpiScaleX - double.Parse(expected, CultureInfo.InvariantCulture)) > 0.001)
            throw new InvalidOperationException($"Expected DPI scale {expected}, actual {dpi.DpiScaleX}.");
        var width = (int)Math.Ceiling(visual.ActualWidth * dpi.DpiScaleX);
        var height = (int)Math.Ceiling(visual.ActualHeight * dpi.DpiScaleY);
        var picture = new RenderTargetBitmap(width, height, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        picture.Render(visual);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(picture));
        using var output = File.Create(Path.Combine(evidence, name + ".png"));
        png.Save(output);
        var report = $"{name}: {visual.ActualWidth:0.#}×{visual.ActualHeight:0.#} DIP, {width}×{height} px, {dpi.PixelsPerInchX} DPI";
        lines.Add(report);
        Console.WriteLine(report);
    }
}

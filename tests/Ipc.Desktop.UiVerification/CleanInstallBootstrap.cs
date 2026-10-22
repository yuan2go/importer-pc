using System.Reflection;
using Expression = System.Linq.Expressions.Expression;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Ipc.Platform;

static partial class Program
{
    static async Task VerifyCleanInstallBootstrap(string evidence, List<string> lines)
    {
        void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); lines.Add("PASS CLEAN-INSTALL " + text); }
        var anchor = WindowsStoragePaths.ProtectedAnchor ?? throw new InvalidOperationException("Saved storage root required");
        Check(!Directory.Exists(anchor), "actual saved D location has no old deployment directory");
        var assembly = Assembly.LoadFrom(Environment.GetEnvironmentVariable("IPC_FROZEN_DESKTOP") ?? throw new InvalidOperationException("Set frozen Desktop"));
        var media = Environment.GetEnvironmentVariable("IPC_CLEAN_INSTALL_MEDIA") ?? throw new InvalidOperationException("Set same-source Windows media");
        var type = assembly.GetType("Ipc.Desktop.StartupSetupWindow", true)!;
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var step = type.GetField("step", flags)!;
        var delegateType = type.GetConstructors().Single().GetParameters()[1].ParameterType;
        var parameters = delegateType.GetMethod("Invoke")!.GetParameters().Select(p => Expression.Parameter(p.ParameterType, p.Name)).ToArray();
        var prepare = Expression.Lambda(delegateType, Expression.Constant(Task.FromResult(media)), parameters).Compile();
        var launch = new BusinessLaunch("", Path.Combine(WindowsStoragePaths.UserRoot, "data"), null, null);
        var window = (Window)Activator.CreateInstance(type, new object?[] { launch, prepare, "1.11.1" })!;
        try
        {
            window.Show();
            await WaitStep(1);
            Check(new System.Windows.Interop.WindowInteropHelper(window).Handle != IntPtr.Zero, "actual WPF installation options render with no storage root");
            Check(Descendants(window).OfType<TextBox>().Count(x => x.Text.StartsWith(anchor, StringComparison.OrdinalIgnoreCase)) == 2, "program and data defaults both remain on D");
            Capture("clean-install-options");
            var next = Descendants(window).OfType<Button>().Single(x => Equals(x.Content, "下一步"));
            next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitStep(2);
            Check(Descendants(window).OfType<Button>().Any(x => Equals(x.Content, "安装") && x.IsEnabled), "real Application review reaches install confirmation without precreating directories");
            Check(!Directory.Exists(anchor) && WindowsDeploymentSetup.Current("sportmaster") is null, "review creates no machine policy, registration, management credentials or deployment root");
            Capture("clean-install-confirmation");
        }
        finally { window.Close(); }
        Check(!Directory.Exists(anchor), "cancel before Windows installation leaves the computer clean");
        async Task WaitStep(int expected)
        {
            var until = DateTimeOffset.UtcNow.AddSeconds(45);
            while ((int)step.GetValue(window)! != expected && DateTimeOffset.UtcNow < until) await Task.Delay(50);
            Check((int)step.GetValue(window)! == expected, "native installation step " + expected + ": " + string.Join(" | ", Descendants(window).OfType<TextBlock>().Where(x => x.IsVisible).Select(x => x.Text)));
        }
        void Capture(string name)
        {
            window.UpdateLayout(); var dpi = VisualTreeHelper.GetDpi(window);
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            bitmap.Render(window); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = File.Create(Path.Combine(evidence, name + ".png")); png.Save(output);
        }
    }
}

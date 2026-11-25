using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Ipc.Platform;

static partial class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        if (args is not ["--typography" or "--smoke-screenshots"])
        {
            Console.Error.WriteLine("Usage: Ipc.Desktop.UiVerification --typography | --smoke-screenshots");
            return 2;
        }

        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(root.FullName, "ImporterPc.slnx")) && root.Parent is { } parent)
            root = parent;
        var evidenceRoot = Environment.GetEnvironmentVariable("IPC_UI_EVIDENCE") ?? Path.Combine(root.FullName, ".artifacts", "window-native");
        var evidence = Path.Combine(evidenceRoot, args[0][2..] + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(evidence);
        WindowsStoragePaths.UserRootForVerification = () => Path.Combine(evidence, "isolated-user-root");
        typeof(PresentationPreferences).GetField("UserRootForVerification", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(null, (Func<string>)(() => Path.Combine(evidence, "user-preferences")));
        var lines = new List<string>();
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new() { Source = new Uri("/Ipc.Presentation.Wpf;component/Themes/Workstation.xaml", UriKind.Relative) });
        using var watchdog = new Timer(_ => Environment.FailFast("UI verification exceeded five minutes."), null, TimeSpan.FromMinutes(5), Timeout.InfiniteTimeSpan);
        app.Startup += async (_, _) =>
        {
            var code = 0;
            try
            {
                if (args[0] == "--typography") await VerifyTypography(evidence, lines);
                else await SmokeScreenshots(root.FullName, evidence, lines);
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                lines.Add(error.ToString());
                code = 1;
            }
            finally
            {
                File.WriteAllLines(Path.Combine(evidence, "WINDOWS-WPF.log"), lines);
                Console.WriteLine($"Evidence: {evidence}");
                app.Shutdown(code);
            }
        };
        return app.Run();
    }

    static async Task Capture(Window window, double width, double height, string evidence, string name, List<string> lines)
    {
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var client = (FrameworkElement)window.Content;
        if (Math.Abs(client.ActualWidth - width) >= 1 || Math.Abs(client.ActualHeight - height) >= 1)
        {
            await NativeClientSize.Set(window, client, width, height);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        }
        window.UpdateLayout();
        CaptureInternal(evidence, name, client, lines);
    }

    static IEnumerable<T> Visuals<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Visuals<T>(VisualTreeHelper.GetChild(root, i)))
                yield return child;
    }

    static async Task AwaitUi(Func<bool> ready)
    {
        var started = Stopwatch.GetTimestamp();
        do
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            if (ready()) return;
            await Task.Delay(25);
        } while (Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(20));
        throw new TimeoutException("Native page did not become ready.");
    }
}

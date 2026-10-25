using System.Diagnostics;
using Ipc.Platform;

namespace Ipc.Desktop
{
    // Compile the production App constructor and dispatcher registration unchanged.
    public partial class App : System.Windows.Application
    {
        public App(string isolatedRoot) : this()
        {
            crashDiagnostics.Dispose();
            crashDiagnostics = new(isolatedRoot, "synthetic-wpf", new string('b', 40));
            crashDiagnostics.SetContext("synthetic-wpf-instance", "synthetic-wpf-epoch", "dispatcher-verification");
        }
    }
}
static class P0Dispatcher
{
    public static int Child(string root)
    {
        var app = new Ipc.Desktop.App(root) { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
        app.Startup += (_, _) => app.Dispatcher.BeginInvoke(new Action(() => throw new InvalidOperationException("synthetic dispatcher unhandled")));
        return app.Run();
    }
    public static int Parent()
    {
        var root = Path.Combine(Path.GetTempPath(), "ipc-p0-dispatcher-" + Wire.Id()); Directory.CreateDirectory(root);
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var value in new[] { typeof(P0Dispatcher).Assembly.Location, "--p0-dispatcher-child", root }) start.ArgumentList.Add(value);
        using var child = Process.Start(start)!; var stdout = child.StandardOutput.ReadToEndAsync(); var stderr = child.StandardError.ReadToEndAsync();
        if (!child.WaitForExit(20000)) { child.Kill(true); child.WaitForExit(); throw new TimeoutException("WPF fatal child did not terminate"); }
        _ = stdout.GetAwaiter().GetResult(); _ = stderr.GetAwaiter().GetResult();
        var log = File.ReadAllText(Path.Combine(root, "runtime.jsonl"));
        if (child.ExitCode != CrashDiagnostics.FatalExitCode || !log.Contains("DispatcherUnhandledException") || !log.Contains("synthetic-wpf-instance")) throw new Exception("FAIL WPF fatal context/exit");
        Console.WriteLine("PASS ST14 production WPF dispatcher hook flushes context and fails fast with explicit exit code " + child.ExitCode);
        return 0;
    }
}

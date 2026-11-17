using System.Windows;

namespace Ipc.Operations.Desktop;

public partial class App : System.Windows.Application
{
    OperationsToolLease? lease;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e); ShutdownMode = ShutdownMode.OnExplicitShutdown;
        try
        {
            if (e.Args.Length != 0) throw new InvalidOperationException("请从当前安装入口打开，运维工具不接受目录或业务命令参数。");
            var installation = await Task.Run(() => OperationsInstallation.Load(AppContext.BaseDirectory));
            lease = new(installation);
            MainWindow = new OperationsWindow(installation); ShutdownMode = ShutdownMode.OnMainWindowClose; MainWindow.Title = Ipc.Platform.InstallationIdentity.OperationsDisplayName; MainWindow.Show();
        }
        catch (Exception error)
        {
            MessageBox.Show(Ipc.Platform.DiagnosticText.Protect(error.Message), "无法打开供包运维工具", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown(2);
        }
    }
    protected override void OnExit(ExitEventArgs e) { lease?.Dispose(); base.OnExit(e); }
}

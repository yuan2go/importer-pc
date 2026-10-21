using System.Windows;
using System.IO;
using Ipc.Desktop;
using Ipc.Platform;

namespace Ipc.Installer;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var preparation = new InstallerPreparation();
            var launch = new BusinessLaunch("", Path.Combine(WindowsStoragePaths.UserRoot, "data"), null, null);
            var window = new StartupSetupWindow(launch, preparation.PrepareAsync, preparation.BusinessVersion);
            window.Closed += (_, _) =>
            {
                try { preparation.Cleanup(); Shutdown(0); }
                catch (Exception error) { MessageBox.Show(error.Message, "安装准备文件清理未完成", MessageBoxButton.OK, MessageBoxImage.Warning); Shutdown(2); }
            };
            MainWindow = window; window.Show();
        }
        catch (Exception error) { MessageBox.Show(error.Message, "安装包不可用", MessageBoxButton.OK, MessageBoxImage.Error); Shutdown(2); }
    }
}

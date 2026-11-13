using System.Windows;
using System.IO;
using Ipc.Desktop;
using Ipc.Assembly;
using Ipc.Platform;

namespace Ipc.Installer;

public partial class App : System.Windows.Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        if (InstallerCommandLine.IsQuiet(e.Args))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            InstallerPreparation? silentPreparation = null; var result = 1603;
            try
            {
                var command = InstallerCommandLine.Parse(e.Args);
                string? media = null;
                if (command.Action != "Uninstall")
                {
                    silentPreparation = new InstallerPreparation();
                    media = await silentPreparation.PrepareAsync(new Progress<InstallationPreparationProgress>(), default);
                }
                result = await Task.Run(() => ParcelStartupServices.RunQuietAsync(e.Args, media));
            }
            catch (ArgumentException) { result = 87; }
            catch { result = 1603; }
            finally { if (result != 1627) silentPreparation?.Cleanup(); }
            Shutdown(result); return;
        }
        base.OnStartup(e);
        try
        {
            var preparation = new InstallerPreparation();
            var launch = new BusinessLaunch("", InstallationIdentity.DefaultLaunchDataRoot, null, null);
            var window = new StartupSetupWindow(launch, preparation.PrepareAsync, preparation.BusinessVersion);
            window.Closed += (_, _) =>
            {
                try
                {
                    var clean = window.CanCleanPreparation;
                    if (clean) preparation.Cleanup();
                    Shutdown(clean ? 0 : 3); // Unknown outcome is not an installation success exit.
                }
                catch (Exception error) { MessageBox.Show(error.Message, "安装准备文件清理未完成", MessageBoxButton.OK, MessageBoxImage.Warning); Shutdown(2); }
            };
            MainWindow = window; window.Show();
        }
        catch (Exception error) { MessageBox.Show(error.Message, "安装包不可用", MessageBoxButton.OK, MessageBoxImage.Error); Shutdown(2); }
    }
}

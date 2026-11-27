using Ipc.Presentation.Wpf;
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
            finally
            {
                try { if (result != 1627) silentPreparation?.Cleanup(); }
                catch (Exception error) { System.Diagnostics.Trace.TraceError("Quiet installation preparation cleanup failed: {0}", error); result = 1603; }
            }
            Shutdown(result); return;
        }
        base.OnStartup(e);
        try
        {
            var language = InstallerLanguageWindow.Choose(Resources);
            if (language is null) { Shutdown(1602); return; }
            var preparation = new InstallerPreparation();
            var launch = new BusinessLaunch("", InstallationIdentity.DefaultLaunchDataRoot, null, null);
            var window = new StartupSetupWindow(launch, preparation.PrepareAsync, preparation.BusinessVersion, language);
            window.Closed += (_, _) =>
            {
                try
                {
                    var clean = window.CanCleanPreparation;
                    if (clean) preparation.Cleanup();
                    Shutdown(clean ? 0 : 3); // Unknown outcome is not an installation success exit.
                }
                catch (Exception error) { MessageBox.Show(error.Message, UiText.Get("Setup.CleanupFailed"), MessageBoxButton.OK, MessageBoxImage.Warning); Shutdown(2); }
            };
            MainWindow = window; window.Show();
        }
        catch (Exception error) { MessageBox.Show(error.Message, UiText.Get("Setup.PackageUnavailable"), MessageBoxButton.OK, MessageBoxImage.Error); Shutdown(2); }
    }
}

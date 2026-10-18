using System.IO;
using System.Windows;
using Ipc.Assembly;
using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Desktop;

public partial class App : System.Windows.Application
{
    IBusinessRuntime? runtime;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
#if SPORTMASTER
            const string applicationId = "sportmaster";
#elif DIRECTPLC
            const string applicationId = "direct-plc";
#else
            const string applicationId = "wcs-to-plc";
#endif
            var options = DesktopLaunchOptions.Parse(e.Args, applicationId);
            var launch = options.Business;
            runtime = ParcelAssembly.Create(launch);
            if (launch.View is not null && !runtime.Presentation.Views.Contains(launch.View) || launch.Layout is not null && !runtime.Presentation.Layouts.Contains(launch.Layout)) throw new InvalidDataException("页面或布局不属于本次选定业务");
            await runtime.StartAsync(CancellationToken.None);
            var application = (IParcelApplication)runtime.Application;
#if SPORTMASTER
            var window = Ipc.Application.Sportmaster.Desktop.Presentation.Create(application, launch.View, launch.Layout, options.Ui, StopAsync);
#elif DIRECTPLC
            var window = Ipc.Application.DirectPlc.Desktop.Presentation.Create(application, launch.View, launch.Layout);
#else
            var window = Ipc.Application.WcsToPlc.Desktop.Presentation.Create(application, launch.View, launch.Layout);
#endif
#if !SPORTMASTER
            window.StopOwnerAsync = StopAsync;
#endif
            MainWindow = window; window.Show();
        }
        catch (Exception ex)
        {
            Exception failure = ex;
            try { await StopAsync(); } catch (Exception cleanup) { failure = new AggregateException("启动与清理失败，保留诊断", ex, cleanup); }
            MessageBox.Show(failure.Message, "启动失败 / NotReady", MessageBoxButton.OK, MessageBoxImage.Error); Shutdown(2);
        }
    }
    async Task StopAsync()
    {
        if (runtime is null) return;
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await runtime.StopAsync(budget.Token); await runtime.DisposeAsync(); runtime = null;
    }
}

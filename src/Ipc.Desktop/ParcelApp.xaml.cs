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
            var map = e.Args.Chunk(2).ToDictionary(x => x[0], x => x.Length == 2 ? x[1] : throw new ArgumentException("启动参数缺值"));
            if (map.Keys.Any(x => x is not ("--config" or "--data-root" or "--view" or "--layout"))) throw new ArgumentException("启动参数无效");
            var launch = new BusinessLaunch(map.GetValueOrDefault("--config", System.IO.Path.Combine(AppContext.BaseDirectory, "config")),
                map.GetValueOrDefault("--data-root", System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ImporterPc", "data")), map.GetValueOrDefault("--view"), map.GetValueOrDefault("--layout"));
            runtime = ParcelAssembly.Create(launch);
            if (launch.View is not null && !runtime.Presentation.Views.Contains(launch.View) || launch.Layout is not null && !runtime.Presentation.Layouts.Contains(launch.Layout)) throw new InvalidDataException("页面或布局不属于本次选定业务");
            await runtime.StartAsync(CancellationToken.None);
            var application = (IParcelApplication)runtime.Application;
#if SPORTMASTER
            var window = Ipc.Application.Sportmaster.Desktop.Presentation.Create(application, launch.View, launch.Layout);
#elif DIRECTPLC
            var window = Ipc.Application.DirectPlc.Desktop.Presentation.Create(application, launch.View, launch.Layout);
#else
            var window = Ipc.Application.WcsToPlc.Desktop.Presentation.Create(application, launch.View, launch.Layout);
#endif
            window.StopOwnerAsync = StopAsync; MainWindow = window; window.Show();
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

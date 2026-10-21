using System.Windows;
using Ipc.Product;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Ipc.Desktop;
public partial class App : global::System.Windows.Application
{
    IHost? host;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var map = new Dictionary<string,string>(StringComparer.Ordinal);
            for(var i=0;i<e.Args.Length;i+=2)
            {
                if(i+1>=e.Args.Length || e.Args[i] is not ("--config" or "--data-root" or "--view" or "--layout") || e.Args[i+1].StartsWith("--",StringComparison.Ordinal) || !map.TryAdd(e.Args[i],e.Args[i+1]))
                    throw new ArgumentException("启动参数无效；支持 --config、--data-root、--view、--layout，每项仅一个值。");
            }
            var root = map.GetValueOrDefault("--config", System.IO.Path.Combine(AppContext.BaseDirectory, "config"));
            host = ProductHost.Build(new(System.IO.Path.Combine(root, "product.json"), System.IO.Path.Combine(root, "station.json"), System.IO.Path.Combine(root, "presentation.json"),
                map.GetValueOrDefault("--data-root", System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ImporterPc", "data")), View: map.GetValueOrDefault("--view"), Layout: map.GetValueOrDefault("--layout")));
            await host.StartAsync();
            new MainWindow(host.Services.GetRequiredService<ProductRuntime>(), true).Show();
        }
        catch (Exception ex)
        {
            Exception failure = ex;
            try { await StopRuntimeAsync(); }
            catch (Exception cleanup) { failure = new AggregateException("启动失败且受控清理报告故障", ex, cleanup); }
            MessageBox.Show(failure.Message, "启动失败 · 尚未就绪", MessageBoxButton.OK, MessageBoxImage.Error); Shutdown(2);
        }
    }
    public async Task StopRuntimeAsync()
    {
        var owned = host; if (owned is null) return;
        var errors = new List<Exception>();
        try { await owned.StopAsync().ConfigureAwait(false); } catch (Exception ex) { errors.Add(ex); }
        var disposed = false;
        try { await owned.Services.GetRequiredService<ProductRuntime>().DisposeAsync().ConfigureAwait(false); disposed = true; }
        catch (Exception ex) { errors.Add(ex); }
        if (disposed)
        {
            try { owned.Dispose(); host = null; } catch (Exception ex) { errors.Add(ex); }
        }
        if (errors.Count > 0) throw new AggregateException("停止和资源清理保留故障", errors);
    }
}

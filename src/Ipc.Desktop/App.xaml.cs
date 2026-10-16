using System.Windows;
using Ipc.Product;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Ipc.Desktop;
public partial class App : Application
{
    IHost? host;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var map = e.Args.Chunk(2).ToDictionary(x => x[0], x => x.Length == 2 ? x[1] : "");
            var root = map.GetValueOrDefault("--config", System.IO.Path.Combine(AppContext.BaseDirectory, "config"));
            host = ProductHost.Build(new(System.IO.Path.Combine(root, "product.json"), System.IO.Path.Combine(root, "station.json"), System.IO.Path.Combine(root, "presentation.json"),
                map.GetValueOrDefault("--data-root", System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ImporterPc", "data"))));
            await host.StartAsync();
            new MainWindow(host.Services.GetRequiredService<ProductRuntime>(), true).Show();
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "启动失败 / NotReady", MessageBoxButton.OK, MessageBoxImage.Error); Shutdown(2); }
    }
    public async Task StopRuntimeAsync() { if (host is not null) { await host.StopAsync(); host.Dispose(); host = null; } }
}

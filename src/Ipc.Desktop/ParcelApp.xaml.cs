using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
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
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var startup = new ParcelStartupWindow();
        MainWindow = startup; startup.Show();
        await System.Windows.Threading.Dispatcher.Yield(DispatcherPriority.Background);
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
            startup.Phase("读取配置与本地记录", "检查所选 Application、配置身份和本地恢复责任；尚未开放业务。", "参数校验完成");
            var starting = await Task.Run(() => ParcelAssembly.Create(launch)); runtime = starting;
            if (launch.View is not null && !starting.Presentation.Views.Contains(launch.View) || launch.Layout is not null && !starting.Presentation.Layouts.Contains(launch.Layout)) throw new InvalidDataException("页面或布局不属于本次选定业务");
            startup.Phase("启动所选能力并恢复原请求", "正在调用应用启动流程。设备、存储、身份和未决请求由 Application 判定；不使用假进度百分比。", "配置和页面选择已读取");
            await Task.Run(() => starting.StartAsync(CancellationToken.None));
            startup.Phase("读取当前运行状态", "启动调用已返回，正在读取应用快照；业务是否可用由实际阻断和许可决定。", "启动流程已返回");
            var application = (IParcelApplication)starting.Application;
            var initial = await Task.Run(application.SnapshotAsync);
            startup.Phase("创建工作窗口", "应用状态：" + initial.State + "\n" + string.Join("\n", initial.BlockingReasons), "已取得运行实例 " + initial.RuntimeId);
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
            startup.AllowClose = true; startup.Close(); ShutdownMode = ShutdownMode.OnMainWindowClose;
        }
        catch (Exception ex)
        {
            Exception failure = ex;
            try { await StopAsync(); } catch (Exception cleanup) { failure = new AggregateException("启动与清理失败，保留诊断", ex, cleanup); }
            startup.Fail(failure.ToString(), async () => { await StopAsync(); startup.AllowClose = true; Shutdown(2); });
        }
    }
    async Task StopAsync()
    {
        if (runtime is null) return;
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var stopping = runtime;
        await Task.Run(async () => { await stopping.StopAsync(budget.Token); await stopping.DisposeAsync(); }); runtime = null;
    }
}

// A real startup surface precedes synchronous configuration/SQLite work. Its rows
// describe completed calls, never guessed device health or simulated percentages.
sealed class ParcelStartupWindow : Window
{
    readonly TextBlock title = new() { FontSize = 24, FontWeight = FontWeights.Bold, Text = "正在启动客户端" };
    readonly TextBlock detail = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12), Text = "校验启动参数，尚未创建业务或设备会话。" };
    readonly TextBox diagnostic = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 120 };
    readonly StackPanel actions = new() { Orientation = Orientation.Horizontal };
    readonly List<string> completed = [];
    public bool AllowClose { get; set; }
    public ParcelStartupWindow()
    {
        Title = "启动与恢复 · Importer PC"; Width = 760; Height = 520; MinWidth = 500; MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var grid = new Grid { Margin = new Thickness(24) };
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) }); grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        Grid.SetRow(detail, 1); Grid.SetRow(diagnostic, 2); Grid.SetRow(actions, 3);
        grid.Children.Add(title); grid.Children.Add(detail); grid.Children.Add(diagnostic); grid.Children.Add(actions); Content = grid;
        Closing += (_, e) => { if (!AllowClose) e.Cancel = true; };
    }
    public void Phase(string heading, string description, string? completedCall = null)
    {
        title.Text = heading; detail.Text = description;
        if (completedCall is not null) completed.Add(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz") + " · " + completedCall);
        diagnostic.Text = string.Join("\n", completed);
    }
    public void Fail(string reason, Func<Task> close)
    {
        title.Text = "启动未完成 / NotReady"; detail.Text = "保留诊断；未进入作业。若停止失败，窗口继续持有资源责任，可重试停止。";
        diagnostic.Text = string.Join("\n", completed) + "\n\n" + reason;
        var copy = new Button { Content = "复制诊断", MinHeight = 44 };
        copy.Click += (_, _) => { try { Clipboard.SetText(diagnostic.Text); } catch (Exception error) { detail.Text = "复制失败，可选择诊断文本复制：" + error.Message; } };
        var exit = new Button { Content = "停止并退出", MinHeight = 44 };
        exit.Click += async (_, _) =>
        {
            exit.IsEnabled = false;
            try { await close(); }
            catch (Exception error) { diagnostic.Text += "\n\n停止未完成：" + error; exit.IsEnabled = true; }
        };
        actions.Children.Add(copy); actions.Children.Add(exit);
    }
}

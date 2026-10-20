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
    ParcelStartupWindow startup = null!;
    DesktopLaunchOptions? selected;
#if SPORTMASTER
    const string ApplicationId = "sportmaster";
#elif DIRECTPLC
    const string ApplicationId = "direct-plc";
#else
    const string ApplicationId = "wcs-to-plc";
#endif
    protected override async void OnStartup(StartupEventArgs e)
    {
        if (e.Args.SequenceEqual(new[] { "--environment-report" }))
        {
            Console.WriteLine(Wire.Encode(WindowsClientCompatibility.Capture())); Shutdown(0); return;
        }
        // WinExe installation helper: dispatch before creating any WPF window.
        if (e.Args.FirstOrDefault() == "--startup-command")
        {
            try { await Task.Run(() => ParcelStartupServices.Handle(e.Args)); Shutdown(Environment.ExitCode); }
            catch { Shutdown(2); }
            return;
        }
        base.OnStartup(e); ShutdownMode = ShutdownMode.OnExplicitShutdown;
        startup = new ParcelStartupWindow(); MainWindow = startup;
        try
        {
            var mode = e.Args.FirstOrDefault() is "--setup" or "--maintenance" or "--product-maintenance" ? e.Args[0] : null;
            if (mode == "--product-maintenance") { new ProductMaintenanceWindow().ShowDialog(); startup.AllowClose = true; Shutdown(); return; }
            selected = DesktopLaunchOptions.Parse(mode is null ? e.Args : e.Args[1..], ApplicationId);
            var published = File.Exists(Path.Combine(AppContext.BaseDirectory, "application-release.json"));
            if (mode == "--setup" || mode is null && published && DeploymentMedia.ReadInstalled(AppContext.BaseDirectory, ApplicationId) is null)
            {
                var setup = new StartupSetupWindow(selected.Business); MainWindow = setup; setup.ShowDialog();
                startup.AllowClose = true; Shutdown(); return;
            }
            if (mode == "--maintenance") { await Repair(); return; }
            startup.Show(); await Dispatcher.Yield(DispatcherPriority.Background);
            await StartSelected();
        }
        catch (Exception ex) { await Failed(ex); }
    }
    async Task StartSelected(bool firstConfigurationReviewed = false)
    {
        try
        {
            startup.Show(); MainWindow = startup;
            if (runtime is not null) throw new InvalidOperationException("原实例尚未完成停止，不创建第二个运行实例");
            var options = selected!; var launch = options.Business;
            if (!firstConfigurationReviewed)
            {
                var maintenance = ParcelStartupServices.Maintenance(launch);
                var initialDocument = await Task.Run(maintenance.ReadAsync);
                var route = DesktopLaunchOptions.StartupRoute(initialDocument);
                if (route != DesktopStartupRoute.Runtime)
                {
                    await Repair(route == DesktopStartupRoute.Repair ? string.Join("\n", initialDocument.Diagnostics) : null,
                        initialConfiguration: route == DesktopStartupRoute.FirstConfiguration);
                    return;
                }
            }
            startup.BeginAttempt();
            startup.Phase("读取配置与本地记录", "检查所选 Application、配置身份和本地恢复责任；尚未开放业务。", "参数校验完成");
            var starting = await Task.Run(() => ParcelAssembly.Create(launch)); runtime = starting;
            if (launch.View is not null && !starting.Presentation.Views.Contains(launch.View) || launch.Layout is not null && !starting.Presentation.Layouts.Contains(launch.Layout)) throw new InvalidDataException("页面或布局不属于本次选定业务");
            startup.Phase("启动所选能力并恢复原请求", "正在调用应用启动流程。设备、存储、身份和未决请求由 Application 判定。", "配置和页面选择已读取");
            await Task.Run(() => starting.StartAsync(CancellationToken.None));
            startup.Phase("读取当前运行状态", "启动调用已返回，正在读取应用快照。", "启动流程已返回");
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
            MainWindow = window; window.Show(); startup.AllowClose = true; startup.Close(); ShutdownMode = ShutdownMode.OnMainWindowClose;
        }
        catch (Exception ex) { await Failed(ex); }
    }
    async Task Failed(Exception error)
    {
        selected ??= new(new(Path.Combine(AppContext.BaseDirectory, "config"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ImporterPc", "data"), null, null), DesktopLaunchOptions.DefaultUi(ApplicationId));
        Exception failure = error;
        try { await StopAsync(); } catch (Exception cleanup) { failure = new AggregateException("启动与清理失败，保留诊断", error, cleanup); }
        startup.Show(); MainWindow = startup;
        startup.Fail(failure.ToString(), async () => { await StopAsync(); startup.AllowClose = true; Shutdown(2); },
            async () => { await StopAsync(); await StartSelected(); },
            async () => { await StopAsync(); await Repair(failure.Message); },
            ShowSetup);
    }
    async Task Repair(string? failure = null, bool initialConfiguration = false)
    {
        // Stop and Dispose must both succeed before any repair UI can issue a mutation.
        await StopAsync(); selected ??= DesktopLaunchOptions.Parse([], ApplicationId);
        var repair = new StartupRepairWindow(selected.Business, failure, initialConfiguration);
        startup.Hide(); repair.ShowDialog();
        if (repair.ReplacementLaunched) { startup.AllowClose = true; Shutdown(); return; }
        if (repair.RetryRequested) { selected = selected with { Business = repair.SelectedLaunch }; await StartSelected(firstConfigurationReviewed: initialConfiguration); }
        else { startup.Show(); MainWindow = startup; startup.Fail("离线维护已关闭。可重试启动或重新打开维护。", async () => { await StopAsync(); startup.AllowClose = true; Shutdown(); },
            () => StartSelected(), () => Repair(), ShowSetup); }
    }
    async Task ShowSetup()
    {
        await StopAsync(); startup.Hide();
        var setup = new StartupSetupWindow(selected!.Business); MainWindow = setup; setup.ShowDialog();
        if (setup.Launched) { startup.AllowClose = true; Shutdown(); }
        else { MainWindow = startup; startup.Show(); }
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
    readonly WrapPanel actions = new();
    readonly List<string> completed = [];
    public bool AllowClose { get; set; }
    public ParcelStartupWindow()
    {
        Ipc.Application.ParcelFlow.Desktop.ProductBrand.Apply(this);
        Title = "启动与恢复 · 菜鸟供包台"; Width = 760; Height = 520; MinWidth = 500; MinHeight = 360;
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
    public void BeginAttempt() { actions.Children.Clear(); completed.Clear(); }
    public void Fail(string reason, Func<Task> close, Func<Task> retry, Func<Task> repair, Func<Task> setup)
    {
        actions.Children.Clear();
        title.Text = "客户端已打开 · 业务尚未就绪"; detail.Text = "可修改参数、查看诊断；未进入作业。若停止失败，窗口继续持有资源责任，可重试停止。";
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
        actions.Children.Add(copy);
        void Add(string label, Func<Task> action)
        {
            var button = new Button { Content = label, MinHeight = 44 };
            button.Click += async (_, _) =>
            {
                actions.IsEnabled = false;
                try { await action(); }
                catch (Exception error) { diagnostic.Text += "\n\n操作未完成：" + error; }
                finally { actions.IsEnabled = true; }
            };
            actions.Children.Add(button);
        }
        Add("重试启动", retry); Add("修改参数 / 离线维护", repair); Add("安装 / 修复", setup); actions.Children.Add(exit);
    }
}

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
    bool recoveryChild;
#if SPORTMASTER
    const string ApplicationId = "sportmaster";
#elif DIRECTPLC
    const string ApplicationId = "direct-plc";
#else
    const string ApplicationId = "wcs-to-plc";
#endif
    protected override async void OnStartup(StartupEventArgs e)
    {
        if (e.Args.FirstOrDefault() == "--supervise-desktop")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            crashDiagnostics.SetContext("supervisor:" + e.Args.ElementAtOrDefault(1), "no-business-connections", "supervising");
            await DesktopRecoverySupervisor.HandleAsync(e.Args); Shutdown(Environment.ExitCode); return;
        }
        recoveryChild = e.Args.FirstOrDefault() == "--recovery-child";
        var arguments = recoveryChild ? e.Args[1..] : e.Args;
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
            var mode = arguments.FirstOrDefault() is "--setup" or "--maintenance" or "--product-maintenance" or "--after-install" ? arguments[0] : null;
            if (mode == "--product-maintenance") { new ProductMaintenanceWindow().ShowDialog(); startup.AllowClose = true; Shutdown(); return; }
            selected = DesktopLaunchOptions.Parse(mode is null ? arguments : arguments[1..], ApplicationId);
            var published = File.Exists(Path.Combine(AppContext.BaseDirectory, "application-release.json"));
            if (mode == "--setup" || mode is null && published && DeploymentMedia.ReadInstalled(AppContext.BaseDirectory, ApplicationId) is null)
            {
                var setup = new StartupSetupWindow(selected.Business); MainWindow = setup; setup.ShowDialog();
                startup.AllowClose = true; Shutdown(); return;
            }
            if (mode == "--maintenance") { await Repair(); return; }
            if (mode == "--after-install")
            {
                var classification = await Task.Run(() => ParcelStartupServices.Maintenance(selected.Business).ClassifyAsync());
                var route = DesktopLaunchOptions.StartupRoute(classification);
                if (route != DesktopStartupRoute.Runtime) { await Repair(initialConfiguration: route == DesktopStartupRoute.FirstConfiguration); return; }
                startup.AfterInstallation(() => { startup.AllowClose = true; Shutdown(); return Task.CompletedTask; }, () => StartSelected());
                startup.Show(); return;
            }
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
            if (!recoveryChild && OperatingSystem.IsWindows() && File.Exists(Path.Combine(AppContext.BaseDirectory, "application-release.json")) &&
                DeploymentMedia.ReadInstalled(AppContext.BaseDirectory, ApplicationId) is { LifecycleState: "Installed", Recovery.Enabled: true } installed &&
                Path.GetFullPath(launch.DataRoot).Equals(Path.GetFullPath(installed.DataRoot), StringComparison.OrdinalIgnoreCase) &&
                Path.GetFullPath(launch.ConfigurationDirectory).Equals(Path.GetFullPath(installed.ConfigurationDirectory), StringComparison.OrdinalIgnoreCase))
            {
                var presentation = new List<string> { "--ui", options.Ui };
                if (launch.View is not null) presentation.AddRange(["--view", launch.View]);
                if (launch.Layout is not null) presentation.AddRange(["--layout", launch.Layout]);
                if (await Task.Run(() => DesktopRecoverySupervisor.StartForInstalledDesktop(installed, presentation.ToArray()))) { startup.AllowClose = true; Shutdown(0); return; }
            }
            if (!firstConfigurationReviewed)
            {
                var maintenance = ParcelStartupServices.Maintenance(launch);
                var initialDocument = await Task.Run(maintenance.ClassifyAsync);
                var route = DesktopLaunchOptions.StartupRoute(initialDocument);
                if (route != DesktopStartupRoute.Runtime)
                {
                    await Repair(route == DesktopStartupRoute.Repair ? string.Join("\n", initialDocument.Diagnostics) : null,
                        initialConfiguration: route == DesktopStartupRoute.FirstConfiguration);
                    return;
                }
            }
            startup.BeginAttempt();
            startup.Phase("读取配置与本地记录", "检查所选 Application、配置身份和本地恢复责任；尚未开放业务。", "启动配置入口已判定");
            var starting = await Task.Run(() => ParcelAssembly.Create(launch)); runtime = starting;
            if (launch.View is not null && !starting.Presentation.Views.Contains(launch.View) || launch.Layout is not null && !starting.Presentation.Layouts.Contains(launch.Layout)) throw new InvalidDataException("页面或布局不属于本次选定业务");
            startup.Phase("启动所选能力并恢复原请求", "正在调用应用启动流程。设备、存储、身份和未决请求由 Application 判定。", "配置和页面选择已读取");
            await Task.Run(() => starting.StartAsync(CancellationToken.None));
            startup.Phase("读取当前运行状态", "启动调用已返回，正在读取应用快照。", "启动流程已返回");
            var application = (IParcelApplication)starting.Application;
            var initial = await Task.Run(application.SnapshotAsync);
            crashDiagnostics.SetContext(initial.RuntimeId.ToString(), string.Join(",", initial.Devices?.Select(d => d.DeviceId + ":" + d.Epoch) ?? []), initial.State);
            var effective = initial.Configuration.Effective;
            crashDiagnostics.SetProgressFile(Path.Combine(launch.DataRoot, effective.Mode.ToString(), effective.StationId, effective.ApplicationId, "runtime-progress.json"));
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
        selected ??= new(new(Path.Combine(AppContext.BaseDirectory, "config"), Path.Combine(WindowsStoragePaths.UserRoot, "data"), null, null), DesktopLaunchOptions.DefaultUi(ApplicationId));
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
        else if (initialConfiguration)
        {
            startup.Show(); MainWindow = startup;
            if (repair.ConfigurationSaved)
            {
                selected = selected with { Business = repair.SelectedLaunch };
                startup.AfterConfiguration(() => { startup.AllowClose = true; Shutdown(); return Task.CompletedTask; }, () => StartSelected(firstConfigurationReviewed: true));
            }
            else startup.ConfigurationPaused(() => { startup.AllowClose = true; Shutdown(); return Task.CompletedTask; }, () => Repair(initialConfiguration: true));
        }
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
    Task? ownedShutdown;
    async Task StopAsync()
    {
        if (runtime is null) return;
        var stopping = runtime;
        if (ownedShutdown is null || ownedShutdown.IsFaulted || ownedShutdown.IsCanceled)
        {
            ownedShutdown = Task.Run(async () => await stopping.DisposeAsync());
            _ = ownedShutdown.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
        try { await ownedShutdown.WaitAsync(TimeSpan.FromSeconds(5)); runtime = null; ownedShutdown = null; }
        catch
        {
            if (stopping is Ipc.Platform.IRuntimeOwnership { HasUnreleasedResources: false }) { runtime = null; ownedShutdown = null; }
            throw; // Existing close UI remains usable and explicitly reports retained ownership.
        }
    }
}

// A real startup surface precedes synchronous configuration/SQLite work. Its rows
// describe completed calls, never guessed device health or simulated percentages.
sealed class ParcelStartupWindow : Window
{
    readonly TextBlock title = new() { FontSize = UiTypography.PageTitle, FontWeight = UiTypography.Emphasis, Text = "正在启动客户端" };
    readonly TextBlock detail = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12), Text = "校验启动参数，尚未创建业务或设备会话。" };
    readonly TextBox diagnostic = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 120 };
    readonly WrapPanel actions = new();
    readonly List<string> completed = [];
    public bool AllowClose { get; set; }
    public ParcelStartupWindow()
    {
        Ipc.Application.ParcelFlow.Desktop.ProductBrand.Apply(this);
        Title = "启动与恢复 · 菜鸟供包台"; Width = 760; Height = 520; MinWidth = 500; MinHeight = 360;
        Ipc.Application.ParcelFlow.Desktop.StartupAppearance.FitToWorkArea(this);
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
    public void BeginAttempt() { actions.Children.Clear(); completed.Clear(); diagnostic.Visibility = Visibility.Visible; MinHeight = 360; Height = 520; }
    public void ConfigurationPaused(Func<Task> close, Func<Task> resume)
    {
        title.Text = "供包台尚未完成配置";
        detail.Text = "完成配置后即可尝试启动。已保存的内容会保留；未连接设备。";
        diagnostic.Visibility = Visibility.Collapsed; actions.Children.Clear(); Height = 280; MinHeight = 260;
        Add("继续配置", resume); Add("退出", close);
        void Add(string label, Func<Task> run)
        {
            var button = new Button { Content = label, MinHeight = 44, Margin = new(0, 8, 12, 0) };
            button.Click += async (_, _) =>
            {
                actions.IsEnabled = false;
                try { await run(); }
                catch (Exception error) { await FailedResume(error); }
                finally { actions.IsEnabled = true; }
            };
            actions.Children.Add(button);
        }
        Task FailedResume(Exception error)
        {
            diagnostic.Visibility = Visibility.Visible; MinHeight = 360; Height = 520;
            detail.Text = "无法继续配置，请查看诊断后重试。"; diagnostic.Text = error.ToString(); return Task.CompletedTask;
        }
    }
    public void AfterInstallation(Func<Task> close, Func<Task> start)
    {
        ConfigurationPaused(close, start);
        title.Text = "供包台已打开";
        detail.Text = "已识别现有配置。点击启动后才检查并连接设备与服务；安装完成不代表设备已可运行。";
        ((Button)actions.Children[0]).Content = "启动供包台";
    }
    public void AfterConfiguration(Func<Task> close, Func<Task> start)
    {
        AfterInstallation(close, start);
        title.Text = "配置已保存 · 尚未启动";
        detail.Text = "点击启动后才检查并连接设备与服务。保存配置不代表设备已可运行。";
    }
    public void Fail(string reason, Func<Task> close, Func<Task> retry, Func<Task> repair, Func<Task> setup)
    {
        actions.Children.Clear(); diagnostic.Visibility = Visibility.Visible; MinHeight = 360; Height = 520;
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

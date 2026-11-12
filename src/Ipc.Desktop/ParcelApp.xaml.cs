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
    CancellationTokenSource? fatalObservationStop;
    Task? fatalObservation;
#if SPORTMASTER
    const string ApplicationId = "sportmaster";
#elif DIRECTPLC
    const string ApplicationId = "direct-plc";
#else
    const string ApplicationId = "wcs-to-plc";
#endif
    protected override async void OnStartup(StartupEventArgs e)
    {
        if (InstallerCommandLine.IsQuiet(e.Args))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try
            {
                Shutdown(await Task.Run(() => ParcelStartupServices.RunQuietAsync(e.Args,
                Directory.GetParent(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))!.FullName)));
            }
            catch { Shutdown(1603); }
            return;
        }
        if (e.Args.FirstOrDefault() == "--supervise-desktop")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try
            {
                if (OrdinaryRuntimeProcess.RelaunchCurrentIfElevated(e.Args)) { Shutdown(0); return; }
            }
            catch (Exception error)
            {
                crashDiagnostics.Fatal("Supervisor.OrdinaryStartupFailed", error);
                MessageBox.Show("无法以普通权限启动供包台监督入口。原配置和记录保留；请核对安装记录。\n" + error.Message,
                    "启动未完成", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(2); return;
            }
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
            if (mode == "--product-maintenance")
            {
                await OpenUninstaller(arguments); return;
            }
            selected = DesktopLaunchOptions.Parse(mode is null ? arguments : arguments[1..], ApplicationId, recoveryChild);
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
                // A complete existing configuration starts directly: starting only connects cameras
                // and RCS; no chute request exists until a parcel is placed or keyed in.
                startup.Show(); await Dispatcher.Yield(DispatcherPriority.Background);
                await StartSelected(); return;
            }
            startup.Show(); await Dispatcher.Yield(DispatcherPriority.Background);
            await StartSelected();
        }
        catch (Exception ex) { await Failed(ex); }
    }
    async Task OpenUninstaller(string[] arguments)
    {
        startup.BeginAttempt(); startup.Phase("正在打开卸载程序", "正在核验维护入口并等待原窗口退出；尚未删除程序或数据。"); startup.Show();
        try
        {
            var installation = WindowsDeploymentSetup.Current(ApplicationId);
            if (installation is not null && TrustedProgramRoots.Contains(installation.ReleaseRoot, AppContext.BaseDirectory))
                await Task.Run(() => WindowsDeploymentSetup.LaunchUninstaller(ApplicationId));
            else
            {
                await WindowsDeploymentSetup.WaitForUninstallHostAsync(arguments);
                startup.Hide(); new ProductMaintenanceWindow().ShowDialog();
            }
            startup.AllowClose = true; Shutdown();
        }
        catch (Exception error)
        {
            startup.Show(); startup.UninstallFailed(error.ToString(), () => { startup.AllowClose = true; Shutdown(); return Task.CompletedTask; }, () => OpenUninstaller(arguments));
        }
    }
    async Task StartSelected(bool firstConfigurationReviewed = false)
    {
        try
        {
            startup.Show(); MainWindow = startup;
            if (runtime is not null) throw new InvalidOperationException("原实例尚未完成停止，不创建第二个运行实例");
            startup.BeginAttempt();
            var options = selected!; var launch = options.Business;
            var ordinaryArguments = new List<string>();
            ordinaryArguments.AddRange(["--config", Path.GetFullPath(launch.ConfigurationDirectory), "--data-root", Path.GetFullPath(launch.DataRoot), "--ui", options.Ui]);
            if (launch.View is not null) ordinaryArguments.AddRange(["--view", launch.View]);
            if (launch.Layout is not null) ordinaryArguments.AddRange(["--layout", launch.Layout]);
            // Reuse the same-user bootstrap before configuration or supervision.
            // A supervised child must never replace its registered process ID.
            if (!recoveryChild && OrdinaryRuntimeProcess.RelaunchCurrentIfElevated(ordinaryArguments.ToArray()))
            { startup.AllowClose = true; Shutdown(0); return; }
#if SPORTMASTER
            startup.Phase("检查相机依赖", "正在检查相机组件与系统运行库。缺失时从同版维护缓存恢复，系统安装可能需要 Windows 授权。", "开始核验相机启动依赖");
            if (await ParcelStartupServices.EnsureCameraDependenciesAsync(AppContext.BaseDirectory))
                startup.Phase("相机依赖已恢复", "正在继续启动检查，尚未连接设备。", "相机启动依赖已恢复并复查");
#endif
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
            startup.Phase("读取配置与本地记录", "正在检查配置和上次未处理完的作业，尚未开始供包。", "启动配置入口已判定");
            var starting = await Task.Run(() => ParcelAssembly.Create(launch)); runtime = starting;
            if (starting is IRuntimeFatalSource fatalSource)
            {
                fatalObservationStop = new();
                fatalObservation = RuntimeFatalMonitor.RunAsync(fatalSource, crashDiagnostics, fatalObservationStop.Token);
                _ = fatalObservation.ContinueWith(t => crashDiagnostics.Fatal("RuntimeFatalMonitor.Failed", t.Exception!), CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
            if (launch.View is not null && !starting.Presentation.Views.Contains(launch.View) || launch.Layout is not null && !starting.Presentation.Layouts.Contains(launch.Layout)) throw new InvalidDataException("页面或布局不属于本次选定业务");
            startup.Phase("正在启动设备并恢复未完成的作业", "正在调用应用启动流程。设备、存储、身份和未决请求由 Application 判定。", "配置和页面选择已读取");
            await Task.Run(() => starting.StartAsync(CancellationToken.None));
            startup.Phase("读取当前运行状态", "启动完成，正在读取状态。", "启动流程已返回");
            var application = (IParcelApplication)starting.Application;
            // Setup/repair can authorize the same local authority in this process.
            // Every new runtime entry returns to ordinary mode, including a controlled reopen.
            await Ipc.Application.ParcelFlow.Desktop.WorkstationEntry.BeginRuntimeAsync(application);
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
            if (window is Ipc.Application.ParcelFlow.Desktop.ShellWindow shell) { shell.ReopenOwnerAsync = ReopenStopped; shell.OpenRetentionOwnerAsync = OpenRetentionStopped; }
#if SPORTMASTER
            if (window is Ipc.Application.Sportmaster.Desktop.SupplyStationWindow station) { station.ReopenOwnerAsync = ReopenStopped; station.OpenRetentionOwnerAsync = OpenRetentionStopped; station.OpenMaintenanceOwnerAsync = OpenMaintenanceStopped; }
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
        if (repair.RestoreAdopted) selected = selected with { Business = repair.SelectedLaunch };
        if (repair.ReplacementLaunched) { startup.AllowClose = true; Shutdown(); return; }
        if (repair.RetryRequested) { selected = selected with { Business = repair.SelectedLaunch }; await StartSelected(firstConfigurationReviewed: initialConfiguration); }
        else if (initialConfiguration)
        {
            // Saved first configuration opens the station ("保存并打开作业"); closing it unsaved
            // (draft kept or discarded) exits, and the next launch reopens first configuration.
            if (repair.ConfigurationSaved)
            {
                selected = selected with { Business = repair.SelectedLaunch };
                await StartSelected(firstConfigurationReviewed: true);
            }
            else { startup.AllowClose = true; Shutdown(); }
        }
        else
        {
            startup.Show(); MainWindow = startup; startup.Fail("离线维护已关闭。可重试启动或重新打开维护。", async () => { await StopAsync(); startup.AllowClose = true; Shutdown(); },
            () => StartSelected(), () => Repair(), ShowSetup);
        }
    }
    async Task ShowSetup()
    {
        await StopAsync(); startup.Hide();
        var setup = new StartupSetupWindow(selected!.Business); MainWindow = setup; setup.ShowDialog();
        if (setup.Launched) { startup.AllowClose = true; Shutdown(); }
        else { MainWindow = startup; startup.Show(); }
    }
    Task? ownedShutdown;
    Task ReopenStopped()
    {
        if (runtime is not null) throw new InvalidOperationException("原实例尚未释放，不能重开");
        ShutdownMode = ShutdownMode.OnExplicitShutdown; startup = new ParcelStartupWindow(); MainWindow = startup;
        Dispatcher.BeginInvoke(new Action(async () => await StartSelected()));
        return Task.CompletedTask;
    }
    Task OpenMaintenanceStopped()
    {
        if (runtime is not null) throw new InvalidOperationException("原实例尚未释放，不能进入离线维护");
        ShutdownMode = ShutdownMode.OnExplicitShutdown; startup = new ParcelStartupWindow(); MainWindow = startup;
        Dispatcher.BeginInvoke(new Action(async () =>
        {
            var center = new MaintenanceCenterWindow(selected!.Business); MainWindow = center; center.ShowDialog();
            selected = selected with { Business = center.SelectedLaunch };
            MainWindow = startup;
            if (center.UninstallLaunched) { startup.AllowClose = true; Shutdown(); }
            else if (center.RetryRequested) await StartSelected();
            else { startup.AfterMaintenance(() => { startup.AllowClose = true; Shutdown(); return Task.CompletedTask; }, () => StartSelected()); startup.Show(); }
        }));
        return Task.CompletedTask;
    }
    Task OpenRetentionStopped()
    {
        if (runtime is not null) throw new InvalidOperationException("原实例尚未释放，不能进入离线维护");
        ShutdownMode = ShutdownMode.OnExplicitShutdown; startup = new ParcelStartupWindow(); MainWindow = startup;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            var service = (IStartupRetentionMaintenance)ParcelStartupServices.Maintenance(selected!.Business);
            new HistoryRetentionWindow(service, ParcelStartupServices.ManagementMode(selected.Business)).ShowDialog();
            startup.AfterMaintenance(() => { startup.AllowClose = true; Shutdown(); return Task.CompletedTask; }, () => StartSelected()); startup.Show();
        }));
        return Task.CompletedTask;
    }
    async Task StopAsync()
    {
        if (runtime is null) return;
        var stopping = runtime;
        if (ownedShutdown is null || ownedShutdown.IsFaulted || ownedShutdown.IsCanceled)
        {
            ownedShutdown = Task.Run(async () => await stopping.DisposeAsync());
            _ = ownedShutdown.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
        try
        {
            await ownedShutdown.WaitAsync(TimeSpan.FromSeconds(5)); runtime = null; ownedShutdown = null;
            await DetachFatalObservationAsync();
        }
        catch
        {
            if (stopping is Ipc.Platform.IRuntimeOwnership { HasUnreleasedResources: false }) { runtime = null; ownedShutdown = null; await DetachFatalObservationAsync(); }
            throw; // Existing close UI remains usable and explicitly reports retained ownership.
        }
    }
    async Task DetachFatalObservationAsync()
    {
        fatalObservationStop?.Cancel();
        if (fatalObservation is not null) await fatalObservation;
        fatalObservationStop?.Dispose(); fatalObservationStop = null; fatalObservation = null;
    }
}

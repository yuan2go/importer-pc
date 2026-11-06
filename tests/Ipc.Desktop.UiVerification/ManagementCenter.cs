using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Platform;

static partial class Program
{
    static async Task VerifyManagementCenter(string root, string evidence, List<string> lines)
    {
        void Check(bool value, string text) { if (!value) throw new InvalidOperationException(text); lines.Add("PASS MANAGEMENT-CENTER " + text); }
        async Task Until(Func<bool> ready)
        {
            for (int i = 0; i < 300; i++) { await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); if (ready()) return; await Task.Delay(25); }
            throw new TimeoutException("Management center UI did not settle: " + string.Join(" | ", System.Windows.Application.Current.Windows.OfType<Window>().Where(w => w.IsVisible).Select(w => w.Title + ": " + string.Join("; ", Descendants(w).OfType<TextBlock>().Select(t => t.Text).Where(t => t.Contains("密码") || t.Contains("授权") || t.Contains("未能"))))) + " · " + System.Windows.Application.Current.Windows.OfType<SupplyStationWindow>().FirstOrDefault()?.ViewModel.Message);
        }
        var launch = new BusinessLaunch(Path.Combine(evidence, "config"), Path.Combine(evidence, "data"), null, null);
        Directory.CreateDirectory(launch.ConfigurationDirectory);
        var config = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(root, "tests/Ipc.Application.Verification/sportmaster.synthetic.json"))) with { Login = null };
        ExtensionStore.AtomicWrite(Path.Combine(launch.ConfigurationDirectory, "business.json"), config);
        await using var runtime = new ParcelRuntime(launch, new SportmasterFlow(), new CommercialProductization.SyntheticFactory(launch)) { RuntimeIdentityPreflight = () => { } };
        await runtime.StartAsync(default);
        const string password = "Synthetic-Center-Only";
        await runtime.ConfigureManagementPasswordAsync(password);
        await VerifyManagementStatusOrdering(runtime, Check);
        var window = new SupplyStationWindow(runtime, null, "standard") { ConfirmDiscardDrafts = _ => true };
        window.Show();
        var vm = window.ViewModel;
        await Until(() => vm.Fresh);
        await SetClient(window, 1366, 768);
        var client = (Grid)window.FindName("ClientArea");
        var navigation = (Border)window.FindName("DailyNavigation");
        var clock = (TextBlock)window.FindName("StationClock");
        var header = (Border)window.FindName("TimeHeader");
        Check(Descendants(header).OfType<TextBlock>().Single() == clock && !Descendants(header).OfType<Button>().Any(b => b.IsVisible) && !Descendants(header).OfType<Image>().Any(), "top bar contains only the clock");
        var clockRight = clock.TranslatePoint(new Point(clock.ActualWidth, 0), client).X;
        Check(clock.HorizontalAlignment == HorizontalAlignment.Right && clockRight > client.ActualWidth * .8 && clockRight <= client.ActualWidth - 8,
            "clock is right aligned in the brand-free header");
        var entry = (Button)window.FindName("ManagementCenterEntry");
        Check(entry.TranslatePoint(new Point(0, entry.ActualHeight), navigation).Y >= navigation.ActualHeight - 12, "management entry is anchored at the bottom");
        Check(!window.IsNavigationCollapsed && navigation.ActualWidth == 120, "wide layout starts with the compact expanded navigation");
        var toggle = (Button)window.FindName("NavigationToggle");
        bool ArrowPointsLeft()
        {
            var arrow = (System.Windows.Shapes.Path)toggle.Template.FindName("NavigationChevron", toggle);
            var figure = arrow.Data.GetFlattenedPathGeometry().Figures.Single();
            var next = figure.Segments[0] is LineSegment line ? line.Point : ((PolyLineSegment)figure.Segments[0]).Points[0];
            return next.X < figure.StartPoint.X;
        }
        var handleCenter = toggle.TranslatePoint(new Point(toggle.ActualWidth / 2, toggle.ActualHeight / 2), navigation);
        Check(ArrowPointsLeft() && toggle.ActualWidth >= 44 && toggle.ActualHeight >= 44 && Math.Abs(handleCenter.Y - navigation.ActualHeight / 2) <= 1 &&
            handleCenter.X >= navigation.ActualWidth - 16, "left-pointing handle sits on the navigation edge at mid height with a full-size target");
        var entryContent = Descendants(entry).OfType<StackPanel>().Single(p => p.Children.OfType<System.Windows.Shapes.Path>().Any() && p.Children.OfType<TextBlock>().Any());
        Check(entryContent.Orientation == Orientation.Vertical && entryContent.Children.OfType<TextBlock>().Single().Text == "管理中心",
            "expanded navigation places labels below their icons");
        var statusLine = (Grid)window.FindName("SupplyStatusLine");
        var workStatus = (TextBlock)window.FindName("WorkStatus");
        var pace = (Border)window.FindName("PaceSummary"); var rcs = (Border)window.FindName("RcsConnection");
        var feedbackBar = (Border)window.FindName("OperationalFeedbackBar");
        double CenterY(FrameworkElement element) => element.TranslatePoint(new Point(0, element.ActualHeight / 2), statusLine).Y;
        Check(Math.Abs(CenterY(workStatus) - CenterY(pace)) <= 2 && Math.Abs(CenterY(workStatus) - CenterY(rcs)) <= 2,
            "supply state, shift totals and RCS share the same row and vertical alignment");
        Check(!feedbackBar.IsVisible && ((Grid)feedbackBar.Parent).RowDefinitions[1].ActualHeight == 0,
            "empty feedback consumes no second row");
        CaptureInternal(evidence, "management-main-expanded", client, lines);
        var positionSnapshot = await runtime.SnapshotAsync();
        await runtime.SetPositionEnabledAsync(new(Wire.Id(), positionSnapshot.RuntimeId, "right", positionSnapshot.Positions!.Single(p => p.Id == "right").SupplyControl!.Revision, false), default);
        await vm.RefreshAsync(); window.UpdateLayout();
        Check(vm.OperatorStatus.Contains("仅左") && Math.Abs(CenterY(workStatus) - CenterY(rcs)) <= 2,
            "single-position availability and its operating instruction stay on the same status row");
        vm.Message = "管理中心未能打开，请稍后重试或检查管理授权服务。"; window.UpdateLayout();
        Check(feedbackBar.IsVisible && ((Grid)feedbackBar.Parent).RowDefinitions[1].ActualHeight <= 44 &&
            feedbackBar.TranslatePoint(new Point(), (Grid)feedbackBar.Parent).Y >= statusLine.ActualHeight,
            "management errors occupy a separate narrow row below the complete status line");
        CaptureInternal(evidence, "homepage-single-position-error", client, lines);
        PresentationPreferences.SaveWindowMode("sportmaster", PresentationPreferences.WindowOpeningMode.FullScreen);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
        var minimize = (Button)window.FindName("MinimizeFullScreen");
        Check(minimize.IsVisible && clock.TranslatePoint(new Point(clock.ActualWidth, 0), client).X + 8 <= minimize.TranslatePoint(new Point(), client).X,
            "full-screen time and minimize share the right header without overlapping");
        CaptureInternal(evidence, "homepage-fullscreen-error", client, lines);
        PresentationPreferences.SaveWindowMode("sportmaster", PresentationPreferences.WindowOpeningMode.Window);
        await SetClient(window, 1366, 768);
        vm.Message = ""; window.UpdateLayout();
        Check(!feedbackBar.IsVisible && ((Grid)feedbackBar.Parent).RowDefinitions[1].ActualHeight == 0,
            "clearing feedback immediately returns its row height to zero");
        positionSnapshot = await runtime.SnapshotAsync();
        await runtime.SetPositionEnabledAsync(new(Wire.Id(), positionSnapshot.RuntimeId, "right", positionSnapshot.Positions!.Single(p => p.Id == "right").SupplyControl!.Revision, true), default);
        await vm.RefreshAsync(); window.UpdateLayout();
        Click(window, "收起菜单"); window.UpdateLayout();
        Check(window.IsNavigationCollapsed && navigation.ActualWidth == 52 && !ArrowPointsLeft() && entry.IsVisible && entry.ActualHeight >= 44, "narrow collapsed rail retains the right-pointing handle and a usable management control");
        Check(AutomationProperties.GetName(entry).Contains("管理中心") && entry.ToolTip is not null, "icon-only management entry has an accessible name and tooltip");
        CaptureInternal(evidence, "management-main-collapsed", client, lines);
        Click(window, "展开菜单"); window.UpdateLayout();
        Check(!window.IsNavigationCollapsed && navigation.ActualWidth == 120 && ArrowPointsLeft(), "navigation can be expanded again with the left-pointing handle");
        var runtimeId = vm.Snapshot!.RuntimeId;
        var page = ((ContentControl)window.FindName("PageHost")).Content;
        // Start each native password dialog via a dispatcher action, leaving this verification free to drive its modal loop.
        void RequestEntry() => window.Dispatcher.BeginInvoke(new Action(() => entry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent))));
        async Task<ManagementModeWindow> LoginDialog()
        {
            await Until(() => System.Windows.Application.Current.Windows.OfType<ManagementModeWindow>().Any(w => w.IsVisible && Descendants(w).OfType<Button>().Any(b => Equals(b.Content, "进入管理中心") && b.IsEnabled)));
            return System.Windows.Application.Current.Windows.OfType<ManagementModeWindow>().Single(w => w.IsVisible);
        }
        await runtime.EnterManagementModeAsync(password); await vm.RefreshManagementModeAsync();
        RequestEntry(); var login = await LoginDialog();
        Check(!vm.IsManagementMode && !System.Windows.Application.Current.Windows.OfType<ManagementCenterWindow>().Any(), "opening requires password verification even with an existing authorization");
        CaptureInternal(evidence, "management-password", login, lines);
        ((PasswordBox)Descendants(login).OfType<PasswordBox>().Single(p => p.Name == "ManagementPassword")).Password = "wrong-password";
        Click(login, "进入管理中心");
        await Until(() => Descendants(login).OfType<TextBlock>().Any(t => t.Text.Contains("管理密码不正确")));
        Check(!vm.IsManagementMode && !login.Entered && !System.Windows.Application.Current.Windows.OfType<ManagementCenterWindow>().Any(), "incorrect password cannot open protected pages");
        Click(login, "取消"); await Until(() => !login.IsVisible && entry.IsEnabled);
        RequestEntry(); login = await LoginDialog();
        Descendants(login).OfType<PasswordBox>().Single(p => p.Name == "ManagementPassword").Password = password;
        Click(login, "进入管理中心");
        await Until(() => System.Windows.Application.Current.Windows.OfType<ManagementCenterWindow>().Any(w => w.IsVisible));
        var center = System.Windows.Application.Current.Windows.OfType<ManagementCenterWindow>().Single();
        Check(login.Entered && center.Owner == window && !ManagementOwnerEnabled(new System.Windows.Interop.WindowInteropHelper(window).Handle) && vm.SelectedPage == "diagnostics", "management opens as an owned modal window with diagnostics selected");
        Check(ReferenceEquals(center.DataContext, vm) && ReferenceEquals(((ContentControl)window.FindName("PageHost")).Content, page) && vm.Snapshot.RuntimeId == runtimeId, "management shares the business instance and leaves the workstation content in place");
        var cards = Descendants(center).OfType<Border>().Where(b => AutomationProperties.GetAutomationId(b).StartsWith("DiagnosticCard.", StringComparison.Ordinal)).ToArray();
        Check(cards.Length == 4 && cards.All(c => c.Parent is Grid) && cards.Select(Grid.GetRow).SequenceEqual(new[] { 0, 0, 1, 1 }) && cards.Select(Grid.GetColumn).SequenceEqual(new[] { 0, 1, 0, 1 }), "diagnostic cards use two fixed columns and two rows");
        var grantId = vm.ManagementStatus.SessionId;
        var fenced = await runtime.SnapshotAsync();
        Check(fenced.Positions!.All(p => !p.Admit), "opening the native management window fences production admission");
        CaptureInternal(evidence, "management-center-diagnostics", center, lines);
        Click(center, "设置"); await Until(() => vm.HasLoadedConfiguration && !vm.Busy);
        Check(vm.ManagementStatus.SessionId == grantId && !System.Windows.Application.Current.Windows.OfType<ManagementModeWindow>().Any(w => w.IsVisible), "switching management tabs preserves the single password grant");
        var maintenance = Descendants(center).OfType<MaintenancePage>().Single();
        var editor = Descendants(maintenance).OfType<ParameterSettingsControl>().Single();
        editor.ShowPage(0, "显示偏好"); center.UpdateLayout();
        var opening = Descendants(editor).OfType<ComboBox>().Single(t => AutomationProperties.GetAutomationId(t) == "WindowOpeningMode");
        opening.SelectedValue = PresentationPreferences.WindowOpeningMode.FullScreen;
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var saveWindow = (Button)maintenance.FindName("SaveChanges");
        Check(saveWindow.IsEnabled && vm.HasUnsavedDisplayPreferences, "management window includes full-screen selection in the bottom save action");
        saveWindow.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => !vm.Busy && !vm.HasUnsavedWindowPreference);
        Check(window.WindowStyle == WindowStyle.None && window.WindowState == WindowState.Maximized && center.IsVisible && center.WindowStyle != WindowStyle.None &&
            PresentationPreferences.ReadWindowMode("sportmaster") == PresentationPreferences.WindowOpeningMode.FullScreen,
            "saving inside management immediately changes and persists the main workstation only");
        editor.NavigateTo("StationCode");
        Descendants(editor).OfType<TextBox>().Single(t => AutomationProperties.GetAutomationId(t) == "StationCode").Text = "SYNTHETIC-MANAGEMENT-DRAFT";
        editor.ShowPage(0, "显示偏好"); center.UpdateLayout();
        Check(vm.HasUnsavedConfiguration, "unsaved configuration is retained in the management window");
        CaptureInternal(evidence, "management-center-settings", center, lines);
        // External revocation exercises the same lock path as lease expiry without waiting fifteen minutes.
        await runtime.ExitManagementModeAsync(); await vm.RefreshManagementModeAsync();
        Check(((Border)center.FindName("AuthorizationGate")).IsVisible && !maintenance.IsEnabled, "authorization revocation locks protected content immediately");
        Check(vm.HasUnsavedConfiguration && vm.Snapshot.RuntimeId == runtimeId, "revocation preserves drafts and the running business instance");
        CaptureInternal(evidence, "management-center-locked", center, lines);
        _ = center.Dispatcher.BeginInvoke(new Action(() => Click(center, "验证管理密码"))); login = await LoginDialog();
        Descendants(login).OfType<PasswordBox>().Single(p => p.Name == "ManagementPassword").Password = password;
        Click(login, "进入管理中心"); await Until(() => vm.IsManagementMode);
        Check(maintenance.IsEnabled && vm.HasUnsavedConfiguration, "reauthentication restores the same unsaved editor");
        await SetClient(center, 800, 420); CaptureInternal(evidence, "management-center-800", center, lines);
        Click(center, "调试");
        await Until(() => Descendants(center).OfType<FrameworkElement>().Any(e => e.GetType().Name == "StationDebugControl"));
        var debug = Descendants(center).OfType<FrameworkElement>().Single(e => e.GetType().Name == "StationDebugControl");
        var active = debug.GetType().GetField("active", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await Until(() => debug.IsLoaded && active.GetValue(debug) is true);
        var requestCount = vm.Snapshot.Operations.Count();
        await runtime.ExitManagementModeAsync(); await vm.RefreshManagementModeAsync();
        await Until(() => !debug.IsLoaded && active.GetValue(debug) is false);
        _ = center.Dispatcher.BeginInvoke(new Action(() => Click(center, "验证管理密码"))); login = await LoginDialog();
        Descendants(login).OfType<PasswordBox>().Single(p => p.Name == "ManagementPassword").Password = password;
        Click(login, "进入管理中心");
        await Until(() => vm.IsManagementMode && debug.IsLoaded && active.GetValue(debug) is true);
        Check(vm.Snapshot.Operations.Count() == requestCount, "debug reauthentication restarts the same control without creating requests");
        Click(center, "关闭"); await Until(() => !center.IsVisible && window.IsEnabled);
        Check(!vm.IsManagementMode && vm.SelectedPage == "workstation" && vm.HasUnsavedConfiguration && vm.Snapshot.RuntimeId == runtimeId, "closing ends authorization, preserves the draft and returns to the same workstation");
        RequestEntry(); login = await LoginDialog();
        Check(!vm.IsManagementMode, "reopening requests the password again");
        Click(login, "取消"); await Until(() => !login.IsVisible && entry.IsEnabled);
        for (var visit = 0; visit < 3; visit++)
        {
            RequestEntry(); login = await LoginDialog();
            Descendants(login).OfType<PasswordBox>().Single(p => p.Name == "ManagementPassword").Password = password;
            Click(login, "进入管理中心");
            await Until(() => System.Windows.Application.Current.Windows.OfType<ManagementCenterWindow>().Any(w => w.IsVisible));
            var currentCenter = System.Windows.Application.Current.Windows.OfType<ManagementCenterWindow>().Single();
            Check(vm.IsManagementMode && !System.Windows.Application.Current.Windows.OfType<ManagementModeWindow>().Any(w => w.IsVisible), "visit " + visit + " opens protected pages after exactly one successful password entry");
            Click(currentCenter, "关闭"); await Until(() => !currentCenter.IsVisible && entry.IsEnabled);
        }
        await SetClient(window, 800, 420); CaptureInternal(evidence, "management-main-800", client, lines);
        window.Close(); await Until(() => !window.IsVisible);
        var guardedService = DispatchProxy.Create<IManagementStatusUiFixture, ManagementStatusUiProbe>();
        var guardedProbe = (ManagementStatusUiProbe)(object)guardedService; guardedProbe.Inner = runtime;
        var guardedWindow = new SupplyStationWindow(guardedService, null, "standard"); guardedWindow.Show();
        await Until(() => guardedWindow.ViewModel.Fresh);
        guardedProbe.AfterOpen = async () =>
        {
            await runtime.ExitManagementModeAsync();
            await guardedWindow.Dispatcher.InvokeAsync(guardedWindow.ViewModel.RefreshManagementModeAsync).Task.Unwrap();
        };
        _ = guardedWindow.Dispatcher.BeginInvoke(new Action(() => ((Button)guardedWindow.FindName("ManagementCenterEntry")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent))));
        login = await LoginDialog();
        Descendants(login).OfType<PasswordBox>().Single(p => p.Name == "ManagementPassword").Password = password;
        Click(login, "进入管理中心");
        await Until(() => System.Windows.Application.Current.Windows.OfType<ManagementCenterWindow>().Any(w => w.IsVisible));
        var guardedCenter = System.Windows.Application.Current.Windows.OfType<ManagementCenterWindow>().Single();
        Check(!guardedWindow.ViewModel.IsManagementMode && ((Border)guardedCenter.FindName("AuthorizationGate")).IsVisible &&
            (await runtime.SnapshotAsync()).Positions!.All(p => !p.Admit),
            "authorization lost during entry still opens the owned locked window instead of silently stranding paused production");
        Click(guardedCenter, "关闭"); await Until(() => !guardedCenter.IsVisible);
        Check((await runtime.SnapshotAsync()).Positions!.All(p => p.Admit), "the locked entry window can close and restore production without another password");
        guardedWindow.Close(); await Until(() => !guardedWindow.IsVisible);
        lines.Add("Windows WPF HWND/Dispatcher, synthetic ports and data. No real SDK, customer RCS or site acceptance.");
    }
    static async Task VerifyManagementEntryOrdering(string root, string evidence, List<string> lines)
    {
        var launch = new BusinessLaunch(Path.Combine(evidence, "config"), Path.Combine(evidence, "data"), null, null);
        Directory.CreateDirectory(launch.ConfigurationDirectory);
        var config = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(root, "tests/Ipc.Application.Verification/sportmaster.synthetic.json")));
        ExtensionStore.AtomicWrite(Path.Combine(launch.ConfigurationDirectory, "business.json"), config);
        await using var runtime = new ParcelRuntime(launch, new SportmasterFlow(), new CommercialProductization.SyntheticFactory(launch))
        { RuntimeIdentityPreflight = () => { if (config.Mode != RunMode.Simulation) throw new InvalidOperationException("Synthetic management entry fixture only"); } };
        await runtime.StartAsync(default);
        await VerifyManagementStatusOrdering(runtime, (valid, reason) =>
        {
            if (!valid) throw new InvalidOperationException(reason);
            lines.Add("PASS MANAGEMENT-ENTRY " + reason);
        });
    }
    static async Task VerifyManagementStatusOrdering(IParcelApplication runtime, Action<bool, string> check)
    {
        var service = DispatchProxy.Create<IManagementStatusUiFixture, ManagementStatusUiProbe>();
        var probe = (ManagementStatusUiProbe)(object)service; probe.Inner = runtime;
        var model = new ParcelViewModel(service, "Management status ordering", "Synthetic UI");
        var authorized = new ManagementModeStatus(true, true, DateTimeOffset.UtcNow.AddMinutes(15)) { SessionId = Wire.Id() };
        var shared = new TaskCompletionSource<ManagementModeStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        probe.StatusRead = () => shared.Task;
        var foreground = model.RefreshManagementModeAsync();
        var background = model.RefreshManagementModeAsync();
        var appliedBeforeReturn = false;
        var observation = foreground.ContinueWith(_ => appliedBeforeReturn = model.IsManagementMode,
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        shared.SetResult(authorized);
        await Task.WhenAll(foreground, background, observation);
        check(appliedBeforeReturn, "foreground password handoff cannot return before a coalesced authorized status is applied");
        var older = new TaskCompletionSource<ManagementModeStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        var newer = new TaskCompletionSource<ManagementModeStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        probe.StatusRead = () => older.Task;
        foreground = model.RefreshManagementModeAsync();
        probe.StatusRead = () => newer.Task;
        background = model.RefreshManagementModeAsync();
        older.SetResult(authorized);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var returnedBeforeLatest = foreground.IsCompleted;
        newer.SetResult(new(true, false, null));
        await Task.WhenAll(foreground, background);
        check(!returnedBeforeLatest && !model.IsManagementMode, "superseded foreground reads await the newer status and cannot authorize after revocation");
    }
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "IsWindowEnabled")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    static extern bool ManagementOwnerEnabled(nint handle);
}

public interface IManagementStatusUiFixture : IParcelApplication, IWorkstationAdministration, IManagementWorkspace { }
public class ManagementStatusUiProbe : ProjectionProbe
{
    public Func<Task<ManagementModeStatus>>? StatusRead;
    public Func<Task>? AfterOpen;
    protected override object? Invoke(MethodInfo? method, object?[]? arguments) =>
        method!.Name == nameof(IWorkstationAdministration.ManagementModeStatusAsync) && StatusRead is { } read ? read() :
        method.Name == nameof(IManagementWorkspace.SetManagementWorkspaceAsync) ? Workspace(method, arguments!) : base.Invoke(method, arguments);
    async Task<BusinessReceipt> Workspace(MethodInfo method, object?[] arguments)
    {
        var receipt = await (Task<BusinessReceipt>)method.Invoke(Inner, arguments)!;
        if (arguments[1] is true && AfterOpen is { } opened) await opened();
        return receipt;
    }
}

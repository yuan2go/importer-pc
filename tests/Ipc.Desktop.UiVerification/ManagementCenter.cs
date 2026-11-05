using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
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
            throw new TimeoutException("Management center UI did not settle.");
        }
        var launch = new BusinessLaunch(Path.Combine(evidence, "config"), Path.Combine(evidence, "data"), null, null);
        Directory.CreateDirectory(launch.ConfigurationDirectory);
        var config = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(root, "tests/Ipc.Application.Verification/sportmaster.synthetic.json"))) with { Login = null };
        ExtensionStore.AtomicWrite(Path.Combine(launch.ConfigurationDirectory, "business.json"), config);
        await using var runtime = new ParcelRuntime(launch, new SportmasterFlow(), new CommercialProductization.SyntheticFactory(launch)) { RuntimeIdentityPreflight = () => { } };
        await runtime.StartAsync(default);
        const string password = "Synthetic-Center-Only";
        await runtime.ConfigureManagementPasswordAsync(password);
        var window = new SupplyStationWindow(runtime, null, "standard") { ConfirmDiscardDrafts = _ => true };
        window.Show();
        var vm = window.ViewModel;
        await Until(() => vm.Fresh);
        await SetClient(window, 1366, 768);
        var client = (Grid)window.FindName("ClientArea");
        var navigation = (Border)window.FindName("DailyNavigation");
        var clock = (TextBlock)window.FindName("StationClock");
        var header = (Border)window.FindName("TimeHeader");
        Check(Descendants(header).OfType<TextBlock>().Single() == clock && !Descendants(header).OfType<Button>().Any() && !Descendants(header).OfType<Image>().Any(), "top bar contains only the clock");
        var clockCenter = clock.TranslatePoint(new Point(clock.ActualWidth / 2, 0), client).X;
        Check(Math.Abs(clockCenter - client.ActualWidth / 2) <= 1, "clock is centered across the whole client area");
        var entry = (Button)window.FindName("ManagementCenterEntry");
        Check(entry.TranslatePoint(new Point(0, entry.ActualHeight), navigation).Y >= navigation.ActualHeight - 12, "management entry is anchored at the bottom");
        Check(!window.IsNavigationCollapsed && navigation.ActualWidth == 176, "wide layout starts with expanded navigation");
        CaptureInternal(evidence, "management-main-expanded", client, lines);
        Click(window, "收起菜单"); window.UpdateLayout();
        Check(window.IsNavigationCollapsed && navigation.ActualWidth == 64 && entry.IsVisible && entry.ActualHeight >= 44, "collapsed rail retains a usable management control");
        Check(AutomationProperties.GetName(entry).Contains("管理中心") && entry.ToolTip is not null, "icon-only management entry has an accessible name and tooltip");
        CaptureInternal(evidence, "management-main-collapsed", client, lines);
        Click(window, "展开菜单"); window.UpdateLayout();
        Check(!window.IsNavigationCollapsed && navigation.ActualWidth == 176, "navigation can be expanded again");
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
        Check(!vm.IsManagementMode && !System.Windows.Application.Current.Windows.OfType<ManagementCenterWindow>().Any(), "incorrect password cannot open protected pages");
        Click(login, "取消"); await Until(() => !login.IsVisible && entry.IsEnabled);
        RequestEntry(); login = await LoginDialog();
        Descendants(login).OfType<PasswordBox>().Single(p => p.Name == "ManagementPassword").Password = password;
        Click(login, "进入管理中心");
        await Until(() => System.Windows.Application.Current.Windows.OfType<ManagementCenterWindow>().Any(w => w.IsVisible));
        var center = System.Windows.Application.Current.Windows.OfType<ManagementCenterWindow>().Single();
        Check(center.Owner == window && !ManagementOwnerEnabled(new System.Windows.Interop.WindowInteropHelper(window).Handle) && vm.SelectedPage == "diagnostics", "management opens as an owned modal window with diagnostics selected");
        Check(ReferenceEquals(center.DataContext, vm) && ReferenceEquals(((ContentControl)window.FindName("PageHost")).Content, page) && vm.Snapshot.RuntimeId == runtimeId, "management shares the business instance and leaves the workstation content in place");
        CaptureInternal(evidence, "management-center-diagnostics", center, lines);
        Click(center, "设置"); await Until(() => vm.HasLoadedConfiguration && !vm.Busy);
        var maintenance = Descendants(center).OfType<MaintenancePage>().Single();
        var editor = Descendants(maintenance).OfType<ParameterSettingsControl>().Single();
        editor.ShowPage(0, "显示偏好"); center.UpdateLayout();
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
        await SetClient(window, 800, 420); CaptureInternal(evidence, "management-main-800", client, lines);
        window.Close(); await Until(() => !window.IsVisible);
        lines.Add("Windows WPF HWND/Dispatcher, synthetic ports and data. No real SDK, customer RCS or site acceptance.");
    }
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "IsWindowEnabled")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    static extern bool ManagementOwnerEnabled(nint handle);
}

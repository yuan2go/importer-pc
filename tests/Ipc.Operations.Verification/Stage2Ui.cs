using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Operations;
using Ipc.Operations.Desktop;
using Ipc.Platform;

internal static partial class Program
{
    static IEnumerable<DependencyObject> Visuals(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var node in Visuals(VisualTreeHelper.GetChild(root, i))) yield return node;
    }
    static void ControlUi(bool loseOwner = false)
    {
        // Same isolated personal-storage boundary as the existing native UI
        // suite; never read/write the developer's installed preferences/ACLs.
        WindowsStoragePaths.UserRootForVerification = () => Path.Combine(root, "isolated-user-root");
        typeof(PresentationPreferences).GetField("UserRootForVerification", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(null, (Func<string>)(() => Path.Combine(root, "user-preferences")));
        using var use = new OperationsToolLease(Installation());
        var app = new System.Windows.Application();
        app.Resources.MergedDictionaries.Add(new() { Source = new Uri("/Ipc.Presentation.Wpf;component/Themes/Workstation.xaml", UriKind.Relative) });
        var window = new OperationsWindow(Installation());
        async Task Until(Func<bool> condition)
        {
            var started = Stopwatch.GetTimestamp();
            while (!condition())
            {
                if (Stopwatch.GetElapsedTime(started) > TimeSpan.FromSeconds(20)) throw new TimeoutException("Native operations condition not reached");
                await Task.Delay(30); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            }
        }
        Button FindButton(string text) => Visuals(window).OfType<Button>().Single(b => Equals(b.Content, text));
        void Press(Button button) => window.Dispatcher.BeginInvoke(new Action(() => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent))));
        app.Startup += async (_, _) =>
        {
            try
            {
                window.Show(); var tabs = (TabControl)window.FindName("Pages");
                Check(tabs.Items.Cast<TabItem>().Select(t => t.Header?.ToString()).SequenceEqual(new[] { "运行总览", "日志中心", "设备调试", "专业参数", "维护中心" }), "five actual operations navigation targets are present");
                tabs.SelectedIndex = 3; await Until(() => Visuals(window).OfType<OperationsWorkspaceControl>().Any());
                Press(FindButton("连接并验证管理密码…"));
                await Until(() => app.Windows.OfType<ManagementModeWindow>().Any(w => w.IsVisible));
                var login = app.Windows.OfType<ManagementModeWindow>().Single(w => w.IsVisible);
                Visuals(login).OfType<PasswordBox>().Single(p => p.Name == "ManagementPassword").Password = OperationsPassword;
                Press(Visuals(login).OfType<Button>().Single(b => b.Name == "EnterManagementMode"));
                var workspace = Visuals(window).OfType<OperationsWorkspaceControl>().Single();
                var model = (SupplyStationViewModel)workspace.DataContext;
                try { await Until(() => !login.IsVisible && model.HasLoadedConfiguration && model.Fresh && model.IsManagementMode); }
                catch (Exception error)
                {
                    var diagnostic = Visuals(window).OfType<Expander>().First(e => Equals(e.Header, "操作诊断"));
                    diagnostic.IsExpanded = true; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    throw new InvalidOperationException($"Native login/load state: login={login.IsVisible}; loaded={model.HasLoadedConfiguration}; fresh={model.Fresh}; grant={model.IsManagementMode}; message={model.Message}; diagnostic={Visuals(window).OfType<TextBox>().FirstOrDefault(b => AutomationProperties.GetAutomationId(b) == "Operations.Failure")?.Text}", error);
                }
                Check(!model.HasUnsavedConfiguration && !model.HasUnsavedDisplayPreferences,
                    "opening professional parameters creates no phantom dirty draft; business=" + model.HasUnsavedConfiguration + "; preferences=" + model.HasUnsavedDisplayPreferences);
                var editor = Visuals(window).OfType<MaintenancePage>().Single();
                Check(editor.Audience == Ipc.Application.Contracts.SmParameterAudience.Technical, "operations reuses the existing complete editor in technical scope");
                Check(editor.NavigateToParameter("ReadTimeoutMs"), "technical read deadline is reachable in the real operations editor");
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Check(Visuals(window).OfType<TextBox>().Any(t => AutomationProperties.GetAutomationId(t) == "ReadTimeoutMs" && t.IsVisible), "technical field is actually visible");
                Check(!editor.NavigateToParameter("StationCode"), "technical editor cannot navigate to the removed field-owned station code");
                Check(model.Snapshot!.Positions!.All(p => p.Admit), "native login and professional page leave legitimate admission open");
                tabs.SelectedIndex = 2;
                await Until(() => Visuals(window).OfType<Button>().Any(b => Equals(b.Content, "进入设备调试（暂停整站供包）") && b.IsEnabled));
                var confirm = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                confirm.Tick += (_, _) =>
                {
                    var dialog = FindWindow("#32770", "进入设备调试");
                    if (dialog == IntPtr.Zero) return;
                    GetWindowThreadProcessId(dialog, out var process);
                    if (process == Environment.ProcessId) { SendMessage(dialog, 0x111, new IntPtr(1), IntPtr.Zero); confirm.Stop(); }
                };
                confirm.Start(); Press(FindButton("进入设备调试（暂停整站供包）"));
                await Until(() => FindButton("结束调试并核对恢复").IsEnabled && Visuals(window).OfType<UserControl>().Any(c => c.GetType().Name == "StationDebugControl"));
                await Until(() => model.Snapshot!.Positions!.All(p => !p.Admit));
                Check(true, "explicit native debug confirmation loads the original camera/RCS control behind its production fence");
                if (loseOwner)
                {
                    Console.WriteLine("READY");
                    await Task.Run(() => Console.ReadLine());
                    var channel = (OperationsControlClient)typeof(OperationsWindow).GetField("control", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(window)!;
                    await Until(() => !channel.Connected);
                    window.Close(); return;
                }
                Press(FindButton("结束调试并核对恢复"));
                await Until(() => model.Snapshot!.Positions!.All(p => p.Admit) && FindButton("进入设备调试（暂停整站供包）").IsEnabled);
                tabs.SelectedIndex = 4;
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Check(FindButton("创建完整备份…").IsVisible && FindButton("完整备份保管、离线配置、恢复、回滚、清理与卸载…").IsVisible,
                    "online backup and detached offline maintenance are both reachable");
                tabs.SelectedIndex = 1; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); Press(FindButton("切换完整在线日志／离线运行日志"));
                await Until(() => Visuals(window).OfType<SoftwareLogControl>().Any());
                var fullLogs = Visuals(window).OfType<SoftwareLogControl>().Single();
                await Until(() => !(bool)typeof(SoftwareLogControl).GetField("busy", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(fullLogs)! &&
                    Visuals(fullLogs).OfType<DataGrid>().Any(g => g.Items.Count > 0));
                Check(true, "original full log UI completes an actual query and displays original runtime records");
                var dpi = VisualTreeHelper.GetDpi(window);
                var picture = new RenderTargetBitmap((int)(window.ActualWidth * dpi.DpiScaleX), (int)(window.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
                picture.Render(window); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(picture));
                using (var output = File.Create(Path.Combine(root, "operations-stage2-native.png"))) png.Save(output);
                tabs.SelectedIndex = 0; await Until(() => !model.Busy);
                Check(!model.HasUnsavedConfiguration && !model.HasUnsavedDisplayPreferences, "ordinary page navigation does not modify the retained parameter draft");
                window.Close();
            }
            catch (Exception error) { Console.Error.WriteLine(error); Environment.Exit(1); }
        };
        app.Run();
        if (Environment.ExitCode != 0) throw new InvalidOperationException("Stage2 native operations verification failed");
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string kind, string title);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out int process);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);
}

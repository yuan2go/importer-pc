using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
        void Capture(string name)
        {
            var client = (FrameworkElement)window.Content;
            var dpi = VisualTreeHelper.GetDpi(client);
            var picture = new RenderTargetBitmap((int)Math.Ceiling(client.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(client.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            picture.Render(client);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(picture));
            using var output = File.Create(Path.Combine(root, name + ".png"));
            png.Save(output);
            Console.WriteLine($"CAPTURE {name}: client {client.ActualWidth:0.##}x{client.ActualHeight:0.##} DIP, {dpi.PixelsPerInchX} DPI");
        }
        app.Startup += async (_, _) =>
        {
            try
            {
                window.Show(); var tabs = (TabControl)window.FindName("Pages");
                Check(tabs.Items.Cast<TabItem>().Select(t => t.Header?.ToString()).SequenceEqual(new[] { "运行总览", "日志中心", "设备调试", "专业参数", "维护中心" }), "five actual operations navigation targets are present");
                var authorize = FindButton("连接并验证管理密码…");
                NativeButtonLayout.CheckCaption(authorize, Check);
                var environment = (ToggleButton)window.FindName("EnvironmentToggle");
                var installation = (TextBox)window.FindName("InstallationText");
                Check(authorize.IsVisible && !installation.IsVisible, "global authorization is available from overview and environment details start folded");
                environment.IsChecked = true;
                await Until(() => installation.IsVisible);
                Check(installation.IsReadOnly && installation.Text.Contains(Location().SourceCommit, StringComparison.Ordinal) &&
                    installation.Text.Contains(Location().DataRoot, StringComparison.Ordinal),
                    "the environment disclosure shows the full source commit and local data path as copyable text");
                environment.IsChecked = false;
                tabs.SelectedIndex = 3; await Until(() => Visuals(window).OfType<OperationsWorkspaceControl>().Any());
                foreach (var (index, name) in new[] { (2, "operations-debug-locked"), (3, "operations-parameters-locked") })
                {
                    tabs.SelectedIndex = index;
                    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
                    Check(FindButton("验证管理密码").IsVisible && !Visuals(window).OfType<TextBlock>().Any(t =>
                        t.IsVisible && (t.Text.Contains("状态读取已中断") || t.Text == "正在读取设置…")),
                        "unauthorized " + name + " offers an actionable grant without a false read failure");
                    Check(!Visuals(window).OfType<TextBox>().Any(t => t.IsVisible && t.Text.Contains("模拟工程不操作真实设备")),
                        "unconnected operations header does not claim a simulated engineering session");
                    KeyboardTraversal.CheckWindow(window, name, Check);
                    Capture(name);
                }
                Press(FindButton("验证管理密码"));
                await Until(() => app.Windows.OfType<ManagementModeWindow>().Any(w => w.IsVisible));
                var login = app.Windows.OfType<ManagementModeWindow>().Single(w => w.IsVisible);
                await Until(() => Visuals(login).OfType<PasswordBox>().Any(p => p.Name == "ManagementPassword" && p.IsVisible && p.IsEnabled));
                Visuals(login).OfType<PasswordBox>().Single(p => p.Name == "ManagementPassword").Password = OperationsPassword;
                var enter = Visuals(login).OfType<Button>().Single(b => b.Name == "EnterManagementMode");
                await Until(() => enter.IsVisible && enter.IsEnabled); Press(enter);
                var workspace = Visuals(window).OfType<OperationsWorkspaceControl>().Single();
                var model = (SupplyStationViewModel)workspace.DataContext;
                try { await Until(() => !login.IsVisible && model.HasLoadedConfiguration && model.Fresh && model.IsManagementMode); }
                catch (Exception error)
                {
                    environment.IsChecked = true; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    throw new InvalidOperationException($"Native login/load state: login={login.IsVisible}; loaded={model.HasLoadedConfiguration}; fresh={model.Fresh}; grant={model.IsManagementMode}; message={model.Message}; diagnostic={Visuals(window).OfType<TextBox>().FirstOrDefault(b => AutomationProperties.GetAutomationId(b) == "Operations.Failure")?.Text}", error);
                }
                Check(!model.HasUnsavedConfiguration && !model.HasUnsavedDisplayPreferences,
                    "opening professional parameters creates no phantom dirty draft; business=" + model.HasUnsavedConfiguration + "; preferences=" + model.HasUnsavedDisplayPreferences);
                var authorizationStatus = Visuals((ContentControl)window.FindName("AuthorizationStatusHost")).OfType<TextBlock>().Single();
                await Until(() => authorizationStatus.Text.StartsWith("已授权", StringComparison.Ordinal));
                Check(authorizationStatus.IsVisible && !installation.IsVisible && ReferenceEquals(authorize, FindButton("连接并验证管理密码…")),
                    "authorized native header shows actual status while account actions remain folded");
                var editor = Visuals(window).OfType<MaintenancePage>().Single();
                Check(editor.Audience == Ipc.Application.Contracts.SmParameterAudience.Technical, "operations reuses the existing complete editor in technical scope");
                Check(editor.NavigateToParameter("ReadTimeoutMs"), "technical read deadline is reachable in the real operations editor");
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Check(Visuals(window).OfType<TextBox>().Any(t => AutomationProperties.GetAutomationId(t) == "ReadTimeoutMs" && t.IsVisible), "technical field is actually visible");
                Check(!editor.NavigateToParameter("StationCode"), "technical editor cannot navigate to the removed field-owned station code");
                Check(!model.Snapshot!.Paused && (loseOwner ? model.Snapshot.Positions!.Single(p => p.Id == "right").Admit : model.Snapshot.Positions!.All(p => p.Admit)), "native login and professional page leave legitimate admission open; existing parcel ownership is retained");
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
                var groups = new[] { "Operations.Maintenance.Daily", "Operations.Maintenance.Responsibilities", "DangerZoneCard" }
                    .Select(id => Visuals(window).OfType<Border>().Single(b => AutomationProperties.GetAutomationId(b) == id)).ToArray();
                var danger = groups[2];
                var groupOrigins = groups.Select(b => b.TranslatePoint(new(), tabs)).ToArray();
                Check(groupOrigins[0].X == groupOrigins[1].X && groupOrigins[1].X == groupOrigins[2].X &&
                    groupOrigins[0].Y < groupOrigins[1].Y && groupOrigins[1].Y < groupOrigins[2].Y,
                    "maintenance presents daily actions, responsibility checks and offline maintenance in three vertical sections");
                Check(FindButton("创建完整备份…").IsVisible &&
                    Visuals(danger).OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == "Operations.Maintenance.Offline").IsVisible,
                    "online backup and detached offline maintenance are both reachable with offline maintenance isolated in its own card");
                var client = (FrameworkElement)window.Content;
                await NativeClientSize.Set(window, client, 1280, 760);
                var offlineBottom = danger.TranslatePoint(new Point(0, danger.ActualHeight), client).Y;
                Check(Math.Abs(client.ActualWidth - 1280) <= 1 && Math.Abs(client.ActualHeight - 760) <= 1 &&
                    offlineBottom <= client.ActualHeight && danger.TranslatePoint(new Point(), client).Y >= 0,
                    $"complete high-risk section in {client.ActualWidth:0.##}x{client.ActualHeight:0.##} client, bottom={offlineBottom:0.##}");
                KeyboardTraversal.CheckWindow(window, "operations-maintenance-native", Check);
                Capture("operations-maintenance-native");
                tabs.SelectedIndex = 1;
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                var offlineLogMode = (RadioButton)window.FindName("OfflineLogMode");
                var onlineLogMode = (RadioButton)window.FindName("OnlineLogMode");
                Check(offlineLogMode.IsChecked == true && onlineLogMode.IsChecked == false && ((FrameworkElement)window.FindName("OfflineLogs")).IsVisible,
                    "log center starts with the offline segment selected and its filters visible");
                Check(!((Button)window.FindName("CancelQuery")).IsVisible, "idle offline log query hides cancellation");
                var filters = (FrameworkElement)window.FindName("Filters");
                var inputs = new[] { "LogAreas", "TimeRange", "Level", "SourceFilter", "Keyword" }
                    .Select(name => (FrameworkElement)window.FindName(name)).ToArray();
                Check(inputs.All(input => input.ActualHeight >= 44 && input.ActualHeight == inputs[0].ActualHeight &&
                    input.TranslatePoint(new(), filters).Y == inputs[0].TranslatePoint(new(), filters).Y),
                    "offline filter inputs align at the same height and baseline");
                onlineLogMode.IsChecked = true;
                await Until(() => onlineLogMode.IsChecked == true && offlineLogMode.IsChecked == false &&
                    !((FrameworkElement)window.FindName("OfflineLogs")).IsVisible && Visuals(window).OfType<SoftwareLogControl>().Any());
                var fullLogs = Visuals(window).OfType<SoftwareLogControl>().Single();
                await Until(() => !(bool)typeof(SoftwareLogControl).GetField("busy", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(fullLogs)! &&
                    Visuals(fullLogs).OfType<DataGrid>().Any(g => g.Items.Count > 0));
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
                var resultHeight = Visuals(fullLogs).OfType<DataGrid>().Single().ActualHeight;
                Check(resultHeight >= 140,
                    "original full log UI completes an actual query with a usable native results viewport; height=" + resultHeight);
                KeyboardTraversal.CheckWindow(window, "operations-stage2-native", Check);
                Capture("operations-stage2-native");
                offlineLogMode.IsChecked = true;
                await Until(() => offlineLogMode.IsChecked == true && onlineLogMode.IsChecked == false &&
                    ((FrameworkElement)window.FindName("OfflineLogs")).IsVisible && !((FrameworkElement)window.FindName("OnlineLogs")).IsVisible);
                Check(true, "the offline segment restores its own filters without leaving the log center");
                tabs.SelectedIndex = 0;
                var positions = (ItemsControl)window.FindName("PositionCards");
                var devices = (ItemsControl)window.FindName("DeviceRows");
                await Until(() => !model.Busy && positions.ItemContainerGenerator.ContainerFromIndex(1) is FrameworkElement);
                window.UpdateLayout();
                var metrics = (UniformGrid)window.FindName("OverviewMetrics");
                var metricOrigins = metrics.Children.OfType<FrameworkElement>().Select(card => card.TranslatePoint(new(), metrics)).ToArray();
                Check(metricOrigins.Length == 4 && metricOrigins.All(origin => origin.Y == metricOrigins[0].Y) &&
                    metricOrigins[0].X < metricOrigins[1].X && metricOrigins[1].X < metricOrigins[2].X && metricOrigins[2].X < metricOrigins[3].X &&
                    ((TextBlock)window.FindName("VisibleOperationsText")).Text.EndsWith(" 件", StringComparison.Ordinal),
                    "verified overview shows four live summary cards in one row");
                Point Origin(ItemsControl control, int index) =>
                    ((FrameworkElement)control.ItemContainerGenerator.ContainerFromIndex(index)).TranslatePoint(new(), control);
                Check(positions.Items.Count == 2 && Origin(positions, 0).Y == Origin(positions, 1).Y && Origin(positions, 0).X < Origin(positions, 1).X,
                    "left and right position cards are in the same row in physical order");
                var healthOrigins = Enumerable.Range(0, devices.Items.Count).Select(index => Origin(devices, index)).ToArray();
                Check(healthOrigins.Length == 4 && healthOrigins.All(origin => origin.Y == healthOrigins[0].Y) &&
                    healthOrigins[0].X < healthOrigins[1].X && healthOrigins[1].X < healthOrigins[2].X && healthOrigins[2].X < healthOrigins[3].X,
                    "three device confirmations and storage occupy four aligned columns");
                KeyboardTraversal.CheckWindow(window, "operations-overview-native", Check);
                Capture("operations-overview-native");
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

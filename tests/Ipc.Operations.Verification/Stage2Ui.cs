using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Globalization;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Operations;
using Ipc.Operations.Desktop;
using Ipc.Platform;
using Ipc.Presentation.Wpf;

internal static partial class Program
{
    static IEnumerable<DependencyObject> Visuals(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var node in Visuals(VisualTreeHelper.GetChild(root, i))) yield return node;
    }
    static void ControlUi(bool loseOwner, bool smoke)
    {
        // Same isolated personal-storage boundary as the existing native UI
        // suite; never read/write the developer's installed preferences/ACLs.
        WindowsStoragePaths.UserRootForVerification = () => Path.Combine(root, "isolated-user-root");
        typeof(PresentationPreferences).GetField("UserRootForVerification", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(null, (Func<string>)(() => Path.Combine(root, "user-preferences")));
        using var use = new OperationsToolLease(Installation());
        var app = new System.Windows.Application();
        app.Resources.MergedDictionaries.Add(new() { Source = new Uri("/Ipc.Presentation.Wpf;component/Themes/Workstation.xaml", UriKind.Relative) });
        var installation = Installation();
        if (!smoke) PresentationPreferences.SaveTheme(installation.Location.ApplicationId, WorkstationThemeManager.IndustrialDarkId);
        var window = new OperationsWindow(installation);
        if (!smoke)
        {
            var expected = (Color)ColorConverter.ConvertFromString("#0F1722");
            Check(app.Resources["Background"] is SolidColorBrush palette && palette.Color == expected,
                "operations loads the saved dark palette at application scope so its dialogs inherit the same theme");
        }
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
        Button AuthorizeButton() => Visuals(window).OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == "Operations.Authorize");
        void Press(Button button) => window.Dispatcher.BeginInvoke(new Action(() => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent))));
        void Capture(string name)
        {
            window.UpdateLayout();
            var client = (FrameworkElement)window.Content;
            var dpi = VisualTreeHelper.GetDpi(client);
            if (Environment.GetEnvironmentVariable("IPC_EXPECTED_DPI") is { } expected &&
                Math.Abs(dpi.DpiScaleX - double.Parse(expected, CultureInfo.InvariantCulture)) > 0.001)
                throw new InvalidOperationException($"Expected DPI scale {expected}, actual {dpi.DpiScaleX}.");
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
                await NativeClientSize.Set(window, (FrameworkElement)window.Content, 1280, 760);
                await Until(() => ((TextBlock)window.FindName("ConnectionText")).Text == "工作站已连接");
                Capture("operations-overview");
                tabs.SelectedIndex = 1;
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                if (smoke)
                {
                    var query = (Button)window.FindName("QueryButton");
                    await Until(() => query.IsEnabled && ((ComboBox)window.FindName("LogAreas")).SelectedItem is not null);
                    Press(query);
                    await Until(() => ((DataGrid)window.FindName("LogGrid")).Items.Count > 0 && query.IsEnabled);
                }
                Capture("operations-logs");
                tabs.SelectedIndex = 3;
                await Until(() => Visuals(window).OfType<OperationsWorkspaceControl>().Any());
                Capture("operations-parameters-locked");
                Check(Equals(AuthorizeButton().Content, "验证管理密码"), "unauthorized action has the full password label");
                Press(AuthorizeButton());
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
                    throw new InvalidOperationException($"Native login/load state: login={login.IsVisible}; loaded={model.HasLoadedConfiguration}; fresh={model.Fresh}; grant={model.IsManagementMode}; message={model.Message}; diagnostic={Visuals(window).OfType<TextBox>().FirstOrDefault(b => AutomationProperties.GetAutomationId(b) == "Operations.Failure")?.Text}", error);
                }
                Check(!model.HasUnsavedConfiguration && !model.HasUnsavedDisplayPreferences,
                    "opening professional parameters creates no phantom dirty draft; business=" + model.HasUnsavedConfiguration + "; preferences=" + model.HasUnsavedDisplayPreferences);
                await Until(() => Equals(AuthorizeButton().Content, "管理授权") && AuthorizeButton().IsEnabled);
                if (smoke)
                {
                    Press(AuthorizeButton());
                    var options = (System.Windows.Controls.Primitives.Popup)window.FindName("AuthorizationOptions");
                    await Until(() => options.IsOpen);
                    Check(Visuals(options.Child).OfType<Button>().Any(button => Equals(button.Content, "延长本窗授权")),
                        "authorized action opens the existing renewal options");
                    options.IsOpen = false;
                }
                var editor = Visuals(window).OfType<MaintenancePage>().Single();
                Check(editor.Audience == Ipc.Application.Contracts.SmParameterAudience.Technical, "operations reuses the existing complete editor in technical scope");
                Check(editor.NavigateToParameter("ReadTimeoutMs"), "technical read deadline is reachable in the real operations editor");
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Check(!editor.NavigateToParameter("StationCode"), "technical editor cannot navigate to the removed field-owned station code");
                Check(!editor.NavigateToParameter("ThemeId") && !Visuals(window).OfType<ComboBox>().Any(choice =>
                    AutomationProperties.GetAutomationId(choice) == "StationSettings.Theme"), "technical editor does not expose the per-user theme selector");
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
                await Until(() => FindButton("结束调试并恢复").IsEnabled && Visuals(window).OfType<UserControl>().Any(c => c.GetType().Name == "StationDebugControl"));
                await Until(() => model.Snapshot!.Positions!.All(p => !p.Admit));
                if (smoke)
                {
                    var debugView = Visuals(window).OfType<UserControl>().Single(c => c.GetType().Name == "StationDebugControl");
                    var calibration = (Button)debugView.FindName("calibrate");
                    var calibrationStatus = (TextBlock)debugView.FindName("calibrationStatus");
                    await Until(() => calibration.IsEnabled && calibrationStatus.Text == "未校准");
                    Press(calibration);
                    await Until(() => calibrationStatus.Text == "空托盘已确认");
                    var surface = (FrameworkElement)window.Content;
                    Check(calibration.TranslatePoint(new Point(0, calibration.ActualHeight), surface).Y <= surface.ActualHeight - 48,
                        "calibration action fits below the live picture without scrolling");
                    Capture("operations-debug-calibration");
                    var request = (TextBox)debugView.FindName("requestBarcode");
                    request.Text = "6950123456789";
                    await Until(() => ((Button)debugView.FindName("prepare")).IsEnabled);
                    Press((Button)debugView.FindName("prepare"));
                    var send = (Button)debugView.FindName("send");
                    await Until(() => ((TextBox)debugView.FindName("requestBody")).Text.Length > 0 && send.IsEnabled);
                    Press(send);
                    await Until(() => ((TextBox)debugView.FindName("responseBody")) is { IsVisible: true, Text.Length: > 0 });
                }
                Capture("operations-debug-authorized");
                if (smoke)
                {
                    var debugView = Visuals(window).OfType<UserControl>().Single(c => c.GetType().Name == "StationDebugControl");
                    var surface = (FrameworkElement)window.Content;
                    foreach (var part in new[] { "requestBody", "responseBody" })
                    {
                        var box = (TextBox)debugView.FindName(part);
                        var bottom = box.TranslatePoint(new Point(0, box.ActualHeight), surface).Y;
                        Check(box.ActualHeight >= 6 * box.FontSize * box.FontFamily.LineSpacing && bottom <= surface.ActualHeight - 32,
                            $"{part} keeps six full lines in view at 1280×760 DIP (box {box.ActualHeight}, bottom {bottom})");
                    }
                }
                if (loseOwner)
                {
                    Console.WriteLine("READY");
                    await Task.Run(() => Console.ReadLine());
                    var channel = (OperationsControlClient)typeof(OperationsWindow).GetField("control", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(window)!;
                    await Until(() => !channel.Connected);
                    window.Close(); return;
                }
                Press(FindButton("结束调试并恢复"));
                await Until(() => model.Snapshot is { Paused: false, Positions: { } positions } &&
                    (smoke ? positions.Single(p => p.Id == "right").Admit : positions.All(p => p.Admit)) &&
                    FindButton("进入设备调试（暂停整站供包）").IsEnabled);
                tabs.SelectedIndex = 4;
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Capture("operations-maintenance");
                tabs.SelectedIndex = 1;
                var offlineLogMode = (RadioButton)window.FindName("OfflineLogMode");
                var onlineLogMode = (RadioButton)window.FindName("OnlineLogMode");
                onlineLogMode.IsChecked = true;
                await Until(() => onlineLogMode.IsChecked == true && offlineLogMode.IsChecked == false &&
                    !((FrameworkElement)window.FindName("OfflineLogs")).IsVisible && Visuals(window).OfType<SoftwareLogControl>().Any());
                var fullLogs = Visuals(window).OfType<SoftwareLogControl>().Single();
                await Until(() => !(bool)typeof(SoftwareLogControl).GetField("busy", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(fullLogs)! &&
                    Visuals(fullLogs).OfType<DataGrid>().Any(g => g.Items.Count > 0));
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
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

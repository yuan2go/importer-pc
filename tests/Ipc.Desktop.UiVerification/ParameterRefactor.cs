using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster;
using Ipc.Platform;

static partial class Program
{
    static async Task VerifyParameterRefactor(string root, string evidence, List<string> lines, bool interactive)
    {
        void Check(bool valid, string reason) { if (!valid) throw new InvalidOperationException(reason); lines.Add("PASS PARAM01-NATIVE " + reason); }
        if (Environment.GetEnvironmentVariable("IPC_FROZEN_DESKTOP") is { } desktopPath)
        {
            var desktop = System.Reflection.Assembly.LoadFrom(desktopPath);
            var startupType = desktop.GetType("Ipc.Desktop.StartupRepairWindow", true)!;
            var missingLaunch = new BusinessLaunch(Path.Combine(evidence, "missing-config"), Path.Combine(evidence, "missing-data"), null, null);
            var first = (Window)Activator.CreateInstance(startupType, [missingLaunch, null, true])!; first.Show();
            var busy = startupType.BaseType!.GetField("Busy", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            for (var i = 0; i < 150 && ((bool)busy.GetValue(first)! || !Descendants(first).OfType<TabControl>().Any(t => t.Name == "StartupTasks")); i++) await Task.Delay(20);
            var setupTabs = Descendants(first).OfType<TabControl>().Single(t => t.Name == "StartupTasks"); setupTabs.SelectedIndex = 1; first.UpdateLayout();
            Check(first.Title.Contains("首次工位配置") && Descendants(first).OfType<ParameterSettingsControl>().Single().Model!.Policy.StationCode == "station-01", "missing configuration opens the existing first-configuration page with shared system defaults, without an extra missing-configuration window");
            Check(!File.Exists(Path.Combine(missingLaunch.ConfigurationDirectory, "business.json")) && !Directory.Exists(missingLaunch.DataRoot), "opening first configuration neither saves parameters nor creates a runtime database/device owner");
            first.Close();
        }
        var configPath = Path.Combine(evidence, "config"); Directory.CreateDirectory(configPath);
        var c = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(root, "config/sportmaster/business.json")));
        ExtensionStore.AtomicWrite(Path.Combine(configPath, "business.json"), c);
        var launch = new BusinessLaunch(configPath, Path.Combine(evidence, "data"), null, null);
        await using var runtime = new ParcelRuntime(launch, new SportmasterFlow(), new CommercialProductization.SyntheticFactory(launch)) { RuntimeIdentityPreflight = () => { } };
        await runtime.StartAsync(default);
        var vm = new ParcelViewModel(runtime, "Sportmaster", "合成原生验证"); await vm.RefreshAsync(); await vm.LoadCapabilitiesAsync();
        var page = new MaintenancePage { DataContext = vm };
        var window = new Window { Title = "PARAM-01 原生交互验证 · 合成数据", Content = page, Width = 1600, Height = 900, WindowStartupLocation = WindowStartupLocation.CenterScreen };
        window.Show(); await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle); window.UpdateLayout();
        var control = Descendants(page).OfType<ParameterSettingsControl>().Single();
        var categories = Descendants(control).OfType<TabControl>().Single();
        Check(categories.Items.Cast<TabItem>().Select(t => t.Header).SequenceEqual(new[] { "系统", "业务模块", "驱动", "组件" }), "one shared native editor exposes exactly the four parameter categories");
        var station = Descendants(control).OfType<TextBox>().Single(x => AutomationProperties.GetName(x) == "StationCode");
        station.Text = "SYSTEM-EDIT"; Check(station.Focus(), "system parameter receives native keyboard focus"); station.Select(2, 4);
        var selection = station.SelectionStart; await vm.RefreshAsync(); await vm.RefreshAsync(); window.UpdateLayout();
        Check(station.Text == "SYSTEM-EDIT" && station.IsKeyboardFocused && station.SelectionStart == selection && station.SelectionLength == 4, "repeated real snapshots preserve input, focus and selection");
        Capture("parameters-system");
        categories.SelectedIndex = 1; window.UpdateLayout();
        var pattern = Descendants(control).OfType<TextBox>().Single(x => AutomationProperties.GetName(x) == "BarcodePattern"); pattern.Text = "OK[0-9]+";
        var sample = Descendants(control).OfType<TextBox>().Single(x => AutomationProperties.GetName(x) == "条码筛选样例"); sample.Text = "noise\nother\nOK003";
        var test = Descendants(control).OfType<Button>().Single(x => Equals(x.Content, "测试候选筛选"));
        test.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        for (var i = 0; i < 100 && !Descendants(control).OfType<TextBlock>().Any(x => x.Text.Contains("选中码：OK003")); i++) await Task.Delay(20);
        Check(Descendants(control).OfType<TextBlock>().Any(x => x.Text.Contains("选中码：OK003")) && runtime.HasActiveWork, "native sample test evaluates the third candidate without stopping runtime or saving configuration");
        Capture("parameters-business");
        vm.StructuredPolicy!.QueueCapacity = "bad"; vm.ParameterEditor!.ValidateFields(); window.UpdateLayout();
        var error = Descendants(control).OfType<Button>().Single(x => x.Content is TextBlock text && text.Text.Contains("QueueCapacity"));
        error.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); window.UpdateLayout();
        Check(categories.SelectedIndex == 0 && Descendants(control).OfType<TextBox>().Single(x => AutomationProperties.GetName(x) == "QueueCapacity").IsKeyboardFocused && pattern.Text == "OK[0-9]+", "error summary locates the invalid field while preserving business inputs");
        Capture("parameters-error"); vm.StructuredPolicy.QueueCapacity = "64"; vm.ParameterEditor.ValidateFields();
        foreach (var size in new[] { (1366d, 768d), (900d, 640d), (800d, 420d) })
        {
            window.Width = size.Item1; window.Height = size.Item2; window.UpdateLayout();
            Check(control.ActualWidth > 600 && control.ActualHeight > 100 && Descendants(control).OfType<ScrollViewer>().Any(s => s.IsVisible && s.ViewportHeight > 0), "parameter fields remain scrollable at " + size);
        }
        Capture("parameters-compact");
        // Actual versioned descriptor, synthetic installed-catalog fixture; no SDK is loaded.
        var package = new PackageReference("hikrobot.idmvs-reader", "1.0.3", new string('a', 64));
        var fields = Wire.Decode<ParameterField[]>(File.ReadAllText(Path.Combine(root, "src/Ipc.Drivers.Hikrobot/parameters.json")));
        var descriptor = new CapabilityDescriptor("observation", "extension:hikrobot.idmvs-reader/idmvs-reader", "1.0.3", "HIKROBOT-IDMVS-OBSERVATION-v1", fields, new(), package, "idmvs-reader");
        var driverConfig = c with { Extensions = new([package], c.Topology!.Readers.Select((r, i) => new ExtensionInstance("reader." + i, package.Id, "idmvs-reader", r.DeviceId, true, new() { ["device.ip"] = "192.0.2." + (21 + i), ["bcr-code"] = "BCR" + i })).ToArray()) };
        var devices = c.Topology!.Readers.Select((r, i) => r with { Driver = "extension:reader." + i }).ToArray(); driverConfig = driverConfig with { Reader = devices[0], Topology = c.Topology with { Readers = devices } };
        var offline = new StartupConfigurationDraft(driverConfig, new([descriptor], [package.Id], Packages: [new(package, [])]));
        var offlineEditor = new ParameterSettingsControl { Model = offline.CreateParameterEditor(runtime.TestBarcodeFilterAsync) }; window.Content = offlineEditor; window.Width = 1366; window.Height = 768; window.Left = 24; window.Top = 24; window.UpdateLayout();
        var offlineTabs = Descendants(offlineEditor).OfType<TabControl>().Single(); offlineTabs.SelectedIndex = 2; window.UpdateLayout();
        var ips = Descendants(offlineEditor).OfType<TextBox>().Where(x => AutomationProperties.GetName(x).EndsWith("/device.ip")).ToArray();
        Check(ips.Length == 2 && ips[0].Text != ips[1].Text && offline.Readers[0].Provider == offline.Readers[1].Provider, "fixed two readers use one package with independent parameter objects and IPs");
        ips[1].Text = "192.0.2.99"; Check(offline.Readers[0].Parameters.Single(p => p.Field.Key == "device.ip").Value == "192.0.2.21", "editing right driver never changes left driver parameters");
        Capture("parameters-drivers");
        if (interactive)
        {
            File.WriteAllText(Path.Combine(root, ".artifacts/parameter-refactor/INTERACTIVE.txt"), evidence);
            var closed = new TaskCompletionSource(); window.Closed += (_, _) => closed.TrySetResult(); await closed.Task;
        }
        else window.Close();
        lines.Add("SYNTHETIC native WPF/Dispatcher and geometry; real SDK, RCS, target IPC resolution/DPI/touch/site NOT_RUN.");
        void Capture(string name)
        {
            window.UpdateLayout(); var dpi = VisualTreeHelper.GetDpi(window); var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32); bitmap.Render(window);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(Path.Combine(evidence, name + ".png")); png.Save(file);
            lines.Add($"CAPTURE {name} {window.ActualWidth}x{window.ActualHeight} DIP scale={dpi.DpiScaleX},{dpi.DpiScaleY}");
        }
    }
}

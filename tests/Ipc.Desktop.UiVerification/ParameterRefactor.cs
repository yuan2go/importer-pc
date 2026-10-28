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
            ManagementModeFixture.AuthorizeRoot(missingLaunch.DataRoot);
            var first = (Window)Activator.CreateInstance(startupType, [missingLaunch, null, true])!; first.Show();
            var busy = startupType.BaseType!.GetField("Busy", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            var startupEditor = await FirstConfigurationReady(first, busy, Check);
            startupEditor.SelectCategory(0); first.UpdateLayout();
            Check(first.Title.Contains("首次配置") && Descendants(first).OfType<ParameterSettingsControl>().Single().Model!.Policy.StationCode == "station-01", "missing configuration opens the existing first-configuration page with shared system defaults, without an extra missing-configuration window");
            Check(!File.Exists(Path.Combine(missingLaunch.ConfigurationDirectory, "business.json")) && !Directory.EnumerateFiles(missingLaunch.DataRoot, "*.db", SearchOption.AllDirectories).Any(), "opening first configuration neither saves parameters nor creates a runtime database/device owner");
            first.Close();
        }
        var configPath = Path.Combine(evidence, "config"); Directory.CreateDirectory(configPath);
        var c = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(root, "tests/Ipc.Application.Verification/sportmaster.synthetic.json"))) with { StationId = "native-inherit-" + Wire.Id() };
        c = c with { CapabilityInstances = ParcelBindings.All(c).Append(new CapabilityBinding("login", "reference-login", "1.0.0", "operator-spare", "reference://operator", "PARCEL-REFERENCE-v1", new() { ["subjects"] = "operator" })).ToArray() };
        ExtensionStore.AtomicWrite(Path.Combine(configPath, "business.json"), c);
        var launch = new BusinessLaunch(configPath, Path.Combine(evidence, "data"), null, null);
        await using var runtime = new ParcelRuntime(launch, new SportmasterFlow(), new CommercialProductization.SyntheticFactory(launch)) { RuntimeIdentityPreflight = () => { } };
        await runtime.StartAsync(default); await ManagementModeFixture.Authorize((IParcelApplication)runtime.Application);
        var vm = new ParcelViewModel(runtime, "Sportmaster", "合成原生验证"); await vm.RefreshAsync(); await vm.LoadCapabilitiesAsync();
        var page = new MaintenancePage { DataContext = vm };
        var window = new Window { Title = "PARAM-01 原生交互验证 · 合成数据", Content = page, Width = 1600, Height = 900, WindowStartupLocation = WindowStartupLocation.CenterScreen };
        window.Show(); await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle); window.UpdateLayout();
        var control = Descendants(page).OfType<ParameterSettingsControl>().Single();
        var categoryTargets = new[]
        {
            (Category: "系统", Path: "StationCode"),
            (Category: "业务模块", Path: "BarcodePattern"),
            (Category: "驱动", Path: vm.ReaderDrafts[0].Capability.InstanceId + "/Provider"),
            (Category: "组件", Path: vm.CapabilityDrafts[0].InstanceId + "/Target")
        };
        foreach (var target in categoryTargets)
        {
            Check(control.NavigateTo(target.Path), target.Category + " is reachable through its actual parameter field");
            window.UpdateLayout();
            Check(Descendants(control).OfType<FrameworkElement>().Any(x => x.IsVisible && AutomationProperties.GetName(x) == target.Path),
                target.Category + " navigation reveals the existing field in the shared editor");
        }
        control.NavigateTo("StationCode"); window.UpdateLayout();
        var station = Descendants(control).OfType<TextBox>().Single(x => AutomationProperties.GetName(x) == "StationCode");
        station.Text = "SYSTEM-EDIT"; Check(station.Focus(), "system parameter receives native keyboard focus"); station.Select(2, 4);
        var selection = station.SelectionStart; await vm.RefreshAsync(); await vm.RefreshAsync(); window.UpdateLayout();
        Check(station.Text == "SYSTEM-EDIT" && station.IsKeyboardFocused && station.SelectionStart == selection && station.SelectionLength == 4, "repeated real snapshots preserve input, focus and selection");
        Capture("parameters-system");
        Check(control.NavigateTo("BarcodePattern"), "business barcode policy is directly reachable without depending on a tab layout"); window.UpdateLayout();
        var pattern = Descendants(control).OfType<TextBox>().Single(x => AutomationProperties.GetName(x) == "BarcodePattern"); pattern.Text = "OK[0-9]+";
        var preview = Descendants(control).OfType<Expander>().Single(e => Equals(e.Header, "测试条码"));
        preview.IsExpanded = true; window.UpdateLayout();
        var sample = Descendants(control).OfType<TextBox>().Single(x => AutomationProperties.GetName(x) == "条码筛选样例"); sample.Text = "noise\nother\nOK003";
        var test = Descendants(control).OfType<Button>().Single(x => Equals(x.Content, "测试候选筛选"));
        test.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        for (var i = 0; i < 100 && !Descendants(control).OfType<TextBlock>().Any(x => x.Text.Contains("选中码：OK003")); i++) await Task.Delay(20);
        Check(Descendants(control).OfType<TextBlock>().Any(x => x.Text.Contains("选中码：OK003")) && runtime.HasActiveWork, "native sample test evaluates the third candidate without stopping runtime or saving configuration");
        Capture("parameters-business");
        var exclusion = Descendants(control).OfType<TextBox>().Single(x => AutomationProperties.GetName(x) == "BarcodeExclusionPattern");
        exclusion.Text = "OK003";
        Check(Descendants(control).OfType<TextBlock>().Any(x => x.Text.Contains("结果已过期")), "editing exclusion invalidates the existing local preview without discarding candidate input");
        var previewDetails = Descendants(preview).OfType<Expander>().Single(e => Equals(e.Header, "测试诊断详情"));
        previewDetails.IsExpanded = true; window.UpdateLayout();
        var previewDiagnostic = Descendants(previewDetails).OfType<TextBox>().Single();
        test.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        for (var i = 0; i < 100 && !previewDiagnostic.Text.Contains("第3项：命中排除规则"); i++) await Task.Delay(20);
        Check(Descendants(preview).OfType<TextBlock>().Any(x => x.Text.Contains("选中码：无")) &&
            previewDiagnostic.IsReadOnly && previewDiagnostic.Text.Contains("第3项：命中排除规则"), "native preview exposes exclusion reason and never selects an excluded code");
        exclusion.Focus(); exclusion.Select(1, 3); await vm.RefreshAsync(); window.UpdateLayout();
        Check(exclusion.IsKeyboardFocused && exclusion.SelectionLength == 3 && exclusion.Text == "OK003", "new exclusion input retains native focus, selection and draft across snapshots");
        Capture("parameters-exclusion");
        Check(control.NavigateTo("DiagnosticLogDays"), "diagnostic retention has its own system field distinct from resident history");
        var days = Descendants(control).OfType<TextBox>().Single(x => AutomationProperties.GetName(x) == "DiagnosticLogDays"); days.Text = "0";
        vm.ParameterEditor!.ValidateFields(); Check(control.FocusFirstIssue() && days.IsKeyboardFocused && days.Text == "0", "log retention error focuses the exact field and preserves invalid input");
        days.Text = "14"; vm.ParameterEditor.ValidateFields(); Capture("parameters-retention");
        control.ShowPage(0, "显示偏好"); window.UpdateLayout();
        Check(!Descendants(control).OfType<TextBox>().Any(x => AutomationProperties.GetName(x) is "DisplayNameDraft" or "TargetEfficiencyDraft"), "SM removes display-only name and efficiency inputs");
        var opening = Descendants(control).OfType<ComboBox>().Single(x => AutomationProperties.GetName(x) == "WindowOpeningMode");
        Check(opening.IsVisible && opening.Focus(), "window opening preference is visible and accepts keyboard focus");
        var configRevision = vm.Snapshot!.Configuration.Revision;
        Descendants(control).OfType<Button>().Single(x => Equals(x.Content, "保存打开方式")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(!vm.HasUnsavedDisplayPreferences && vm.Snapshot.Configuration.Revision == configRevision, "local window preference does not create a business revision or hidden display draft");
        Capture("parameters-display");
        Check(control.NavigateTo("OperatorLogin"), "optional operator login belongs to business and has a native binding selector");
        var login = Descendants(control).OfType<ComboBox>().Single(x => AutomationProperties.GetName(x) == "OperatorLogin");
        var role = vm.ConfigurationRoles.Single(x => x.Role == "登录");
        var maintenanceBefore = Wire.Encode(vm.BuildStructuredConfiguration().MaintenanceLogin);
        login.SelectedItem = role.Choices.First(x => x.Capability is not null); window.UpdateLayout();
        Check(vm.ParameterEditor.HasOperatorLogin && control.NavigateTo("SessionLifetimeMs") && Wire.Encode(vm.BuildStructuredConfiguration().MaintenanceLogin) == maintenanceBefore, "selecting operator login reveals its session policy without creating the removed maintenance role");
        login.SelectedItem = role.Choices.First(x => x.Capability is null); window.UpdateLayout();
        Check(!vm.ParameterEditor.HasOperatorLogin && !control.NavigateTo("SessionLifetimeMs"), "unbinding operator login hides only the inapplicable session policy");
        Capture("parameters-login");
        vm.StructuredPolicy!.QueueCapacity = "bad"; vm.ParameterEditor!.ValidateFields();
        Check(vm.ParameterEditor.Issues.Any(i => i.Path == "QueueCapacity"), "complete validation retains the real invalid queue field");
        control.ShowReview();
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle); window.UpdateLayout();
        var error = Descendants(control).OfType<Button>().Single(x => x.Content is TextBlock text && text.Text.Contains("扫码消息缓冲上限") && !text.Text.Contains("QueueCapacity"));
        Check(error.IsVisible && error.IsEnabled, "the current check-results page exposes the real field-location action");
        error.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await System.Windows.Threading.Dispatcher.Yield(); window.UpdateLayout();
        Check(Descendants(control).OfType<TextBox>().Single(x => AutomationProperties.GetName(x) == "QueueCapacity").IsKeyboardFocused && pattern.Text == "OK[0-9]+", "error summary selects business budget object and focuses the invalid field while preserving barcode inputs");
        Capture("parameters-error"); vm.StructuredPolicy.QueueCapacity = "64"; vm.ParameterEditor.ValidateFields();
        foreach (var size in new[] { (1366d, 768d), (900d, 640d), (800d, 420d) })
        {
            window.Width = size.Item1; window.Height = size.Item2; window.UpdateLayout();
            Check(control.ActualWidth > 600 && control.ActualHeight > 100 && Descendants(control).OfType<ScrollViewer>().Any(s => s.IsVisible && s.ViewportHeight > 0), "parameter fields remain scrollable at " + size);
        }
        Capture("parameters-compact");
        // Current built-in descriptor and synthetic identities; no SDK is loaded.
        var catalog = new Ipc.Assembly.ParcelCapabilityFactory(launch).Catalog();
        var descriptor = catalog.Providers.Single(p => p.Kind == "observation" && p.Implementation == "hikrobot-mvs-reader");
        var devices = c.Topology!.Readers.Select((r, i) => r with { Driver = descriptor.Implementation,
            Parameters = new() { ["device.ip"] = "192.0.2." + (21 + i), ["device.serial"] = "SYNTH-" + i } }).ToArray();
        var driverConfig = c with { Reader = devices[0], Topology = c.Topology with { Readers = devices } };
        var offline = new StartupConfigurationDraft(driverConfig, catalog);
        var offlineEditor = new ParameterSettingsControl { Model = offline.CreateParameterEditor(runtime.TestBarcodeFilterAsync) }; window.Content = offlineEditor; window.Width = 1366; window.Height = 768; window.Left = 24; window.Top = 24; window.UpdateLayout();
        Check(offlineEditor.NavigateTo(offline.Readers[0].InstanceId + "/device.ip"), "left driver address is reachable by its stable field path"); window.UpdateLayout();
        var leftIp = Descendants(offlineEditor).OfType<TextBox>().Single(x => AutomationProperties.GetName(x).EndsWith("/device.ip"));
        Check(leftIp.Text == "192.0.2.21" && offline.Readers.Length == 2 && offline.Readers[0].Provider == descriptor && offline.Readers[0].Provider == offline.Readers[1].Provider,
            "fixed two readers use the current SDK descriptor and display only the selected object's parameters");
        Check(offlineEditor.NavigateTo(offline.Readers[1].InstanceId + "/device.ip"), "right instance is directly addressable by stable field path");
        await System.Windows.Threading.Dispatcher.Yield(); window.UpdateLayout();
        var rightIp = Descendants(offlineEditor).OfType<TextBox>().Single(x => AutomationProperties.GetName(x).EndsWith("/device.ip"));
        Check(rightIp.Text == "192.0.2.22" && !ReferenceEquals(leftIp, rightIp), "object selection preserves distinct native editors and initial addresses");
        rightIp.Text = "192.0.2.99"; Check(offline.Readers[0].Parameters.Single(p => p.Field.Key == "device.ip").Value == "192.0.2.21", "editing right driver never changes left driver parameters");
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
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(Path.Combine(evidence, "inherit-" + name + ".png")); png.Save(file);
            lines.Add($"CAPTURE {name} {window.ActualWidth}x{window.ActualHeight} DIP scale={dpi.DpiScaleX},{dpi.DpiScaleY}");
        }
    }
}

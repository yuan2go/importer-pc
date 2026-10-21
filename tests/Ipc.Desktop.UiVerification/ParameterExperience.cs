using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Platform;

static partial class Program
{
    // Full production window + public schemas. The display projection is synthetic;
    // it never installs a provider, connects hardware, or submits this visual draft.
    static async Task VerifyParameterExperience(string root, string evidence, List<string> lines, bool interactive, bool baseline)
    {
        void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); lines.Add("PASS EXPERIENCE " + message); }
        var folder = Path.Combine(evidence, "config"); Directory.CreateDirectory(folder);
        var config = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(root, "config/sportmaster/business.json"))) with { StationCode = "SYNTHETIC-STATION" };
        ExtensionStore.AtomicWrite(Path.Combine(folder, "business.json"), config);
        var launch = new BusinessLaunch(folder, Path.Combine(evidence, "data"), null, null);
        await using var runtime = new ParcelRuntime(launch, new SportmasterFlow(), new CommercialProductization.SyntheticFactory(launch)) { RuntimeIdentityPreflight = () => { } };
        await runtime.StartAsync(default);
        var app = DispatchProxy.Create<IParcelApplication, ProjectionProbe>(); var probe = (ProjectionProbe)(object)app; probe.Inner = runtime;
        var window = new SupplyStationWindow(app, null, "standard") { ConfirmDiscardDrafts = _ => true };
        window.Show(); await Task.Delay(250); await window.ViewModel.RefreshAsync();
        var vm = window.ViewModel; var client = (Grid)window.FindName("ClientArea");
        await SetClient(window, 1366, 768); Click(window, "参数配置");
        for (var i = 0; i < 150 && (vm.Busy || !vm.HasLoadedConfiguration); i++) await Task.Delay(20);
        var page = (MaintenancePage)((ContentControl)window.FindName("PageHost")).Content;
        var control = (ParameterSettingsControl)page.FindName("MainParameterEditor");
        var package = new PackageReference("hikrobot.idmvs-reader", "1.0.3", new string('a', 64));
        var readerFields = Wire.Decode<ParameterField[]>(File.ReadAllText(Path.Combine(root, "src/Ipc.Drivers.Hikrobot/parameters.json")));
        var rcsFields = Wire.Decode<ParameterField[]>(File.ReadAllText(Path.Combine(root, "src/Ipc.Integrations.SportmasterRcs/parameters.json")));
        var reader = new CapabilityDescriptor("observation", "extension:hikrobot.idmvs-reader/idmvs-reader", "1.0.3", "HIKROBOT-IDMVS-OBSERVATION-v1", readerFields, new(), package, "idmvs-reader");
        var rcs = new CapabilityDescriptor("integration", "sportmaster-rcs-task", "1.3.0", "SPORTMASTER-RCS-SORTER-v1", rcsFields, new());
        var binding = new CapabilityBinding("integration", rcs.Implementation, rcs.Version, "rcs.one", "http://192.0.2.10/rcs/sorter/api", rcs.Contract, new()
        {
            ["source"] = "SYNTHETIC", ["locale"] = "zh_CN", ["zone-id"] = "71", ["project-id"] = "83", ["quantity"] = "1",
            ["specified-chute"] = "false", ["request-id-base"] = "810000000", ["request-id-count"] = "1000",
            ["left-station-code"] = "SYNTHETIC-COMMON", ["right-station-code"] = "SYNTHETIC-COMMON", ["left-bcr-code"] = "TAKE-L", ["right-bcr-code"] = "TAKE-R"
        });
        var devices = config.Topology!.Readers.Select((r, i) => r with { Driver = "extension:reader." + i }).ToArray();
        var visualConfig = config with { Wcs = binding, Reader = devices[0], Topology = config.Topology with { Readers = devices },
            Extensions = new([package], devices.Select((r, i) => new ExtensionInstance("reader." + i, package.Id, reader.Module!, r.DeviceId, true,
                new() { ["device.ip"] = "192.0.2." + (21 + i), ["bcr-code"] = "HEART-" + i })).ToArray()) };
        probe.Catalog = new([reader, rcs, .. (await runtime.CapabilitiesAsync()).Providers.Where(p => p.Kind is not ("observation" or "integration"))], [package.Id], Packages: [new(package, [])]);
        probe.Project = s => s with { Configuration = s.Configuration with { Effective = visualConfig }, Health =
            [new(binding.InstanceId, true, DateTimeOffset.UtcNow.AddSeconds(-1), DateTimeOffset.UtcNow.AddMinutes(1), "SYNTHETIC local lifecycle; upstream acceptance unconfirmed")] };
        for (var i = 0; i < 100 && vm.Snapshot?.Configuration.Effective.Wcs?.InstanceId != binding.InstanceId; i++) { await vm.RefreshAsync(); await Task.Delay(20); }
        await vm.LoadCapabilitiesAsync(); var model = vm.ParameterEditor!;
        Check(ReferenceEquals(control.Model, model) && !vm.HasUnsavedConfiguration, "full schema uses the real shared editor and clean save baseline");
        Check(model.ComponentLabel(vm.ConfigurationRoles.Single(r => r.Role == "维护认证").Selection.Capability!) == "维护认证", "daily component directory displays its existing business role");
        window.UpdateLayout();
        Check(!((Border)window.FindName("WorkstationFooter")).IsVisible && ((TextBlock)window.FindName("SettingsRunMode")).IsVisible &&
            ((TextBlock)window.FindName("SettingsRunMode")).Text.Contains("模拟模式 · 软件暂停不是安全急停"),
            "settings hides the duplicate job footer while retaining visible mode and software-pause boundary");
        lines.Add("SYNTHETIC full window and schema projection; no SDK, no remote RCS, no install; visual draft is never staged.");
        foreach (var size in new[] { (1366d, 768d), (1092d, 614d), (800d, 420d) })
        {
            await SetClient(window, size.Item1, size.Item2);
            foreach (var item in new[] { (2, "左相机", "camera"), (3, "调度系统连接", "rcs") })
            {
                control.ShowPage(item.Item1, item.Item2); window.UpdateLayout();
                Capture(item.Item3 + "-" + size.Item1);
                var save = (Button)page.FindName("SaveChanges"); var bounds = save.TransformToAncestor(client).TransformBounds(new Rect(save.RenderSize));
                Check(bounds.Bottom <= client.ActualHeight + 1 && bounds.Right <= client.ActualWidth + 1 && save.ActualHeight >= 44, "save remains visible with full schema at " + size);
                var first = Boxes().First(b => AutomationProperties.GetName(b) == (item.Item3 == "camera" ? "reader.0/device.ip" : "rcs.one/Target"));
                Check(first.ActualWidth >= 240 && first.ActualHeight >= 36, "readable address dimensions at " + size + ": " + first.RenderSize);
                if (size.Item1 == 1366 && item.Item3 == "rcs")
                {
                    var source = Boxes().Single(b => AutomationProperties.GetName(b) == "rcs.one/source");
                    Check(!Descendants(control).OfType<TextBlock>().Any(t => t.IsVisible && t.Text == "最多 64 个字符") &&
                        AutomationProperties.GetHelpText(source).Contains("64") && Equals(source.ToolTip, "最多 64 个字符"),
                        "routine character limits leave the form body but remain accessible as field help");
                    var heartbeat = Boxes().Single(b => AutomationProperties.GetName(b) == "rcs.one/heartbeat-interval-seconds");
                    var heartbeatBounds = heartbeat.TransformToAncestor(control).TransformBounds(new Rect(heartbeat.RenderSize));
                    Check(heartbeatBounds.Top >= 0 && heartbeatBounds.Bottom <= control.ActualHeight + 1,
                        "normal RCS connection and heartbeat inputs fit above the fixed save bar at 1366x768");
                }
                control.NavigateTo(AutomationProperties.GetName(first)); window.UpdateLayout();
                var fieldBounds = first.TransformToAncestor(control).TransformBounds(new Rect(first.RenderSize));
                Check(fieldBounds.Top >= -1 && fieldBounds.Bottom <= control.ActualHeight + 1, "field navigation scrolls the complete input above the fixed footer at " + size);
                if (size.Item1 == 800) Capture(item.Item3 + "-800-focused");
            }
        }
        await SetClient(window, 1366, 768);
        control.NavigateTo("reader.0/device.ip"); var ip = Boxes().Single(b => AutomationProperties.GetName(b) == "reader.0/device.ip");
        ip.Text = "192.0.2.999"; ip.Select(2, 4); await vm.RefreshAsync(); window.UpdateLayout();
        Check(vm.HasUnsavedConfiguration && ((Button)page.FindName("SaveChanges")).IsEnabled, "full schema edit updates the real save footer before validation");
        Check(ip.Text == "192.0.2.999" && ip.SelectionLength == 4, "snapshot preserves raw invalid IP and selection");
        Check(!model.ValidateFields() && model.Issues.Any(i => i.Path == "reader.0/device.ip"), "invalid IPv4 rejected and localized before review"); control.FocusFirstIssue(); Capture("field-error");
        foreach (var invalid in new[] { "127.1", "192.0.2.01", "0.0.0.0", "255.255.255.255", "::1" })
        { ip.Text = invalid; Check(!model.ValidateFields() && model.Issues.Any(i => i.Path == "reader.0/device.ip"), "driver canonical address rule retained: " + invalid); }
        ip.Text = "192.0.2.23"; control.ShowPage(3, "调度系统连接"); model.ValidateFields();
        control.NavigateTo("rcs.one/request-id-base"); window.UpdateLayout();
        Check(Boxes().Single(b => AutomationProperties.GetName(b) == "rcs.one/request-id-base").IsKeyboardFocused, "advanced request range stays directly reachable and focused"); Capture("request-range");
        if (!baseline) Check(Descendants(control).OfType<Expander>().Any(e => Equals(e.Header, "请求编号区间") && e.IsExpanded), "request range uses progressive disclosure and search expands it");
        control.ShowPage(2, "左相机"); window.UpdateLayout();
        Check(ReferenceEquals(ip, Boxes().Single(b => AutomationProperties.GetName(b) == "reader.0/device.ip")) && ip.Text == "192.0.2.23", "object navigation retains original editor and corrected input");
        control.ShowPage(0, "供包台标识"); window.UpdateLayout(); Capture("system");
        model.Policy.BackupEnabled = true; model.Policy.BackupDirectory = @"D:\Synthetic\Station-Backup\Daily-Recovery";
        control.ShowPage(0, "自动备份"); window.UpdateLayout(); Capture("backup");
        if (!baseline) Check(Boxes().Single(b => AutomationProperties.GetName(b) == "BackupDirectory").ActualWidth >
            Boxes().Single(b => AutomationProperties.GetName(b) == "BackupIntervalMinutes").ActualWidth * 2, "backup path receives its own wide row");
        control.ShowPage(2, "左相机"); window.UpdateLayout();
        vm.MarkReadFailure("SYNTHETIC 状态读取中断，请检查连接"); window.UpdateLayout(); Capture("stale");
        Check(vm.ParameterRuntimeSummary(new("reader.0", devices[0].DeviceId, "左相机")).Contains("运行状态未知"), "stale evidence is never rendered as confirmed");
        if (!baseline)
        {
            vm.NewCapabilityKind = "login"; await vm.AddCapabilityInstanceAsync();
            var role = vm.ConfigurationRoles.Single(r => r.Role == "维护认证");
            var original = role.Selection;
            var replacement = role.Choices.Single(c => c.Capability?.InstanceId == "login.1");
            replacement.Capability!.Provider = original.Capability!.Provider;
            control.ShowPage(3, "维护认证"); window.UpdateLayout();
            var directory = Descendants(control).OfType<ListBox>().Single(b => b.Name == "ParameterDirectory");
            var selectedPage = directory.SelectedItem;
            var retainedModel = control.Model;
            var target = Boxes().Single(b => AutomationProperties.GetName(b) == original.Capability.InstanceId + "/Target");
            target.Text = "reference://synthetic-maintenance-draft"; target.Focus(); target.Select(2, 5);
            role.Selection = replacement; window.UpdateLayout();
            Check(directory.SelectedItem?.ToString() == "身份认证 · " + original.Capability.InstanceId &&
                directory.Items.Cast<object>().Count(p => p.ToString() == "维护认证") == 1,
                "role reassignment relabels old and new component directory entries immediately");
            Check(ReferenceEquals(control.Model, retainedModel) && ReferenceEquals(directory.SelectedItem, selectedPage) &&
                target.IsKeyboardFocused && target.SelectionLength == 5 && target.Text == "reference://synthetic-maintenance-draft",
                "role relabeling preserves selected object, editor, focus, selection and unsaved draft");
            Check(control.ShowPage(3, "维护认证") && control.NavigateTo("login.1/Target"), "updated role label navigates to the newly bound instance");
            var searchBox = Descendants(control).OfType<TextBox>().Single(b => AutomationProperties.GetAutomationId(b) == "ParameterSearch");
            searchBox.Text = "维护认证"; window.UpdateLayout();
            Check(Descendants(control).OfType<TextBlock>().Any(t => t.Text == "组件 / 维护认证"), "role name is searchable on the newly bound instance");
            role.Selection = role.Choices.Single(c => c.Capability is null); window.UpdateLayout();
            Check(!directory.Items.Cast<object>().Any(p => p.ToString() == "维护认证") &&
                !Descendants(control).OfType<TextBlock>().Any(t => t.Text == "组件 / 维护认证"), "unbinding clears stale role names and search results");
            role.Selection = original; searchBox.Clear(); control.ShowPage(3, "维护认证"); window.UpdateLayout(); Capture("role-labels");
        }
        if (interactive)
        {
            await SetClient(window, 1366, 768); control.ShowPage(3, "调度系统连接");
            Directory.CreateDirectory(Path.Combine(root, ".artifacts")); File.WriteAllText(Path.Combine(root, ".artifacts/EXPERIENCE-INTERACTIVE.txt"), evidence);
            var closed = new TaskCompletionSource(); window.Closed += (_, _) => closed.TrySetResult(); await closed.Task;
        }
        else window.Close();
        IEnumerable<TextBox> Boxes() => Descendants(control).OfType<TextBox>();
        void Capture(string name)
        {
            window.UpdateLayout(); var dpi = VisualTreeHelper.GetDpi(client);
            var image = new RenderTargetBitmap((int)Math.Ceiling(client.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(client.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            image.Render(client); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image));
            using var output = File.Create(Path.Combine(evidence, name + ".png")); png.Save(output);
            lines.Add($"CAPTURE {name}: {client.ActualWidth}x{client.ActualHeight} DIP; scale={dpi.DpiScaleX},{dpi.DpiScaleY}");
        }
    }
}

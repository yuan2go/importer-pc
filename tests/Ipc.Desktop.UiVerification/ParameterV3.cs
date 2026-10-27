using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Platform;

static partial class Program
{
    static async Task VerifyParameterV3(string root, string evidence, List<string> lines, bool interactive)
    {
        void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); lines.Add("PASS PS05 " + reason); }
        var folder = Path.Combine(evidence, "v3-config"); Directory.CreateDirectory(folder);
        var config = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(root, "config/sportmaster/business.json"))) with { StationCode = "SYNTHETIC-STATION" };
        ExtensionStore.AtomicWrite(Path.Combine(folder, "business.json"), config);
        var launch = new BusinessLaunch(folder, Path.Combine(evidence, "v3-data"), null, null);
        await using var runtime = new ParcelRuntime(launch, new SportmasterFlow(), new CommercialProductization.SyntheticFactory(launch)) { RuntimeIdentityPreflight = () => { } };
        await runtime.StartAsync(default); await ManagementModeFixture.Authorize((IParcelApplication)runtime.Application);
        var application = DispatchProxy.Create<IManagementParcelFixture, ProjectionProbe>(); var probe = (ProjectionProbe)(object)application; probe.Inner = runtime;
        var window = new SupplyStationWindow(application, null, "standard") { ConfirmDiscardDrafts = _ => true };
        window.Show(); await Task.Delay(250); await window.ViewModel.RefreshAsync();
        var vm = window.ViewModel; var client = (Grid)window.FindName("ClientArea");
        await vm.PauseAsync();
        for (var i = 0; i < 150 && vm.Snapshot?.Paused != true; i++) { await vm.RefreshAsync(); await Task.Delay(20); }
        Check(vm.EnvironmentStatus.StartsWith("模拟模式") && vm.BlockingReasons.Contains("软件已暂停新入场") &&
            vm.Snapshot!.BlockingReasons.Contains("Software admission paused; this is not an emergency stop"),
            "Chinese pause/mode presentation preserves original application facts");
        await SetClient(window, 1366, 768); Click(window, "参数配置");
        for (var i = 0; i < 150 && (vm.Busy || !vm.HasLoadedConfiguration); i++) await Task.Delay(20);
        var page = (MaintenancePage)((ContentControl)window.FindName("PageHost")).Content;
        var editor = (ParameterSettingsControl)page.FindName("MainParameterEditor");
        Check(!vm.HasUnsavedConfiguration && !((Button)page.FindName("SaveChanges")).IsEnabled, "unchanged native form disables save without requiring authentication");
        var revision = (await runtime.SnapshotAsync()).Configuration.Revision;
        Check(!await vm.PrepareConfigurationReviewAsync() && !vm.CanSubmitReviewedConfiguration && (await runtime.SnapshotAsync()).Configuration.Revision == revision,
            "forced empty review remains non-submittable and creates no revision");
        editor.NavigateTo("StationCode"); var station = Descendants(editor).OfType<TextBox>().Single(x => AutomationProperties.GetName(x) == "StationCode");
        station.Text = "SYNTHETIC-EDIT"; station.Focus(); station.Select(2, 4);
        await vm.RefreshAsync(); await vm.RefreshAsync();
        Check(station.IsKeyboardFocused && station.SelectionStart == 2 && station.SelectionLength == 4 && ((Button)page.FindName("SaveChanges")).IsEnabled,
            "live context updates preserve native selection and enable edited save before authorization");
        var left = vm.ReaderDrafts.Single(r => r.Capability.Role == "left");
        editor.ShowPage(2, "左相机"); window.UpdateLayout();
        var target = new ParameterDiagnosticTarget(left.Capability.InstanceId, left.Original.DeviceId, "左相机");
        Check(vm.ParameterRuntimeSummary(target).Contains("运行地址") && !vm.ParameterRuntimeSummary(target).Contains("SYNTHETIC-EDIT"), "runtime context is derived from Effective rather than edited values");
        Capture("v3-left-running");
        Descendants(editor).OfType<Button>().Single(b => Equals(b.Content, "连接诊断")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); window.UpdateLayout();
        Check(vm.SelectedPage == "diagnostics" && Descendants(window).OfType<TextBlock>().Any(t => t.IsVisible && t.Text == "左相机"), "selected camera opens its native diagnostics with visible object context");
        Check(Descendants(window).OfType<Button>().Any(b => b.IsVisible && Equals(b.Content, "设置左相机")), "camera diagnostics provides a direct action for the matching connection");
        Capture("v3-object-diagnostics");
        Click(window, "返回当前对象设置");
        for (var wait = 0; wait < 100 && (vm.SelectedPage != "maintenance" || vm.Busy); wait++) await Task.Delay(20);
        Check(vm.SelectedPage == "maintenance" && !vm.Busy, "object settings return completes its authority and load checks");
        window.UpdateLayout(); editor.NavigateTo("StationCode");
        Check(ReferenceEquals(station, Descendants(editor).OfType<TextBox>().Single(x => AutomationProperties.GetName(x) == "StationCode")) && station.Text == "SYNTHETIC-EDIT", "object diagnostics returns to the cached form and preserves draft identity");
        vm.MarkReadFailure("SYNTHETIC stale probe");
        Check(vm.ParameterRuntimeSummary(target).Contains("运行状态未知") && !vm.ParameterRuntimeSummary(target).Contains("运行连接：已确认"), "stale snapshot cannot continue presenting a healthy connection");
        var staleReview = new ConfigurationSaveReviewWindow(window, vm); staleReview.Show(); staleReview.UpdateLayout();
        Check(Descendants(staleReview).OfType<Button>().Single(b => b.Name == "ReviewPause").IsVisible,
            "stale saved pause state cannot hide the independent pause action in review");
        CaptureInternal(evidence, "v3-save-review", staleReview, lines);
        staleReview.Close();
        await Until(() => vm.Fresh);
        var probesRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.BeforeMonitorProbe = (_, token) => probesRelease.Task.WaitAsync(token);
        await vm.PauseAsync(); await vm.RunAsync(() => vm.AuthorizeMaintenanceAsync("SIMULATION_ONLY"));
        await Until(() => vm.CanConfigure);
        vm.ConfigurationReason = "Synthetic V3 native save";
        Check(await vm.PrepareConfigurationReviewAsync(), "edited candidate is ready for the native save review");
        var readyReview = new ConfigurationSaveReviewWindow(window, vm); readyReview.Show();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); readyReview.UpdateLayout();
        var confirm = Descendants(readyReview).OfType<Button>().Single(b => b.Name == "ConfirmSaveChanges");
        var confirmBounds = confirm.TransformToAncestor(readyReview).TransformBounds(new Rect(confirm.RenderSize));
        Check(confirm.IsEnabled && confirmBounds.Top >= 0 && confirmBounds.Bottom <= readyReview.ActualHeight && confirmBounds.Right <= readyReview.ActualWidth,
            "review exposes its enabled confirm action inside the actual window");
        CaptureInternal(evidence, "v3-save-review-ready", readyReview, lines); readyReview.Close();
        await vm.SaveStructuredConfigurationAsync();
        await Until(() => vm.Snapshot?.Configuration.Pending?.StationCode == "SYNTHETIC-EDIT");
        Check(vm.Snapshot!.Configuration.Pending?.StationCode == "SYNTHETIC-EDIT" && vm.Snapshot.Configuration.Effective.StationCode == "SYNTHETIC-STATION" && !vm.HasUnsavedConfiguration,
            "native save confirms Pending while Effective stays unchanged");
        Check(!((Button)page.FindName("SaveChanges")).IsEnabled && vm.ParameterRuntimeSummary(target).Contains("待重启"), "confirmed Pending disables repeat save and labels the running configuration separately");
        Capture("v3-saved-pending");
        runtime.BeforeMonitorProbe = null; probesRelease.TrySetResult();

        var rcsFields = Wire.Decode<ParameterField[]>(File.ReadAllText(Path.Combine(root, "src/Ipc.Integrations.SportmasterRcs/parameters.json")));
        var rcsBinding = config.Wcs! with { Implementation = "sportmaster-rcs-task", Version = "1.3.0", Contract = "SPORTMASTER-RCS-SORTER-v1", Target = "http://192.0.2.10/rcs/sorter/api" };
        var rcsDescriptor = new CapabilityDescriptor(rcsBinding.Kind, rcsBinding.Implementation, rcsBinding.Version, rcsBinding.Contract, rcsFields, new());
        var rcsDraft = new CapabilityDraft(rcsBinding.Kind, [rcsDescriptor], rcsBinding, true, "wcs");
        var rcsControl = new ParameterSettingsControl { RuntimeContext = vm };
        ParameterDiagnosticTarget? requested = null;
        rcsControl.DiagnosticsRequested += (_, selected) => requested = selected;
        rcsControl.Model = new ParameterEditorModel(vm.StructuredPolicy!, "sportmaster", [], [rcsDraft], runtime.TestBarcodeFilterAsync);
        probe.Project = s => s with { Configuration = s.Configuration with { Effective = s.Configuration.Effective with { Wcs = rcsBinding } }, Health = [new(rcsBinding.InstanceId, true, DateTimeOffset.UtcNow.AddSeconds(-1), DateTimeOffset.UtcNow.AddMinutes(1), "SYNTHETIC local lifecycle; upstream acceptance unconfirmed")] };
        await Until(() => vm.Snapshot?.Configuration.Effective.Wcs?.Implementation == rcsBinding.Implementation);
        window.Content = rcsControl; rcsControl.NavigateTo(rcsBinding.InstanceId + "/Target"); window.UpdateLayout();
        rcsDraft.Target = "http://192.0.2.11/rcs/sorter/api";
        var rcsSummary = vm.ParameterRuntimeSummary(new(rcsBinding.InstanceId, null, "RCS 连接"));
        Check(rcsSummary.Contains("192.0.2.10") && !rcsSummary.Contains("192.0.2.11") && rcsSummary.Contains("upstream acceptance unconfirmed") && rcsSummary.Contains("不表示远端接受"), "RCS summary preserves local health evidence and Effective address without claiming remote acceptance");
        Descendants(rcsControl).OfType<Button>().Single(b => Equals(b.Content, "连接诊断")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(requested?.InstanceId == rcsBinding.InstanceId && requested.DeviceId is null, "shared RCS diagnostics preserves its exact instance identity");
        Capture("v3-rcs-running");
        probe.Project = null; await Until(() => vm.Snapshot?.Configuration.Effective.Wcs?.Implementation == config.Wcs!.Implementation); window.Content = client;

        var choiceFields = new[] { new ParameterField("entity.mode", "synthetic", "实体关联方式", "choice", Choices: ["observe-only", "trigger"]) };
        var choiceBinding = config.Wcs! with { Parameters = new() { ["entity.mode"] = "observe-only" } };
        var choiceDescriptor = new CapabilityDescriptor(choiceBinding.Kind, choiceBinding.Implementation, choiceBinding.Version, choiceBinding.Contract, choiceFields, new());
        var choiceDraft = new CapabilityDraft(choiceBinding.Kind, [choiceDescriptor], choiceBinding, true, "synthetic");
        var choiceEditor = new ParameterSettingsControl { Model = new ParameterEditorModel(vm.StructuredPolicy!, "sportmaster", [], [choiceDraft], runtime.TestBarcodeFilterAsync) };
        window.Content = choiceEditor; choiceEditor.NavigateTo(choiceBinding.InstanceId + "/entity.mode"); window.UpdateLayout();
        var association = Descendants(choiceEditor).OfType<ComboBox>().Single(x => AutomationProperties.GetName(x).EndsWith("/entity.mode"));
        Check(Equals(association.SelectedItem, "observe-only") && Descendants(association).OfType<TextBlock>().Any(x => x.Text == "仅观察"),
            "native choice displays Chinese while selection retains its protocol value");
        association.SelectedItem = "trigger"; window.UpdateLayout();
        Check(choiceDraft.Build().Parameters["entity.mode"] == "trigger" && Descendants(association).OfType<TextBlock>().Any(x => x.Text == "触发关联"),
            "Chinese choice change persists original protocol value without changing binding identity");

        // Synthetic descriptor tests both declarations, including secret-reference
        // without Sensitive=true. No real credential or provider is installed.
        var fields = new[] { new ParameterField("masked", "synthetic", "敏感字段", "string", Sensitive: true), new ParameterField("reference", "synthetic", "秘密引用", "secret-reference") };
        var binding = config.Wcs! with { Parameters = new() { ["masked"] = "SYNTHETIC-A", ["reference"] = "secret:synthetic-reference" } };
        var descriptor = new CapabilityDescriptor(binding.Kind, binding.Implementation, binding.Version, binding.Contract, fields, new());
        var draft = new CapabilityDraft(binding.Kind, [descriptor], binding, true, "synthetic");
        var model = new ParameterEditorModel(vm.StructuredPolicy!, "sportmaster", [], [draft], runtime.TestBarcodeFilterAsync);
        var secureEditor = new ParameterSettingsControl { Model = model }; window.Content = secureEditor;
        secureEditor.NavigateTo(binding.InstanceId + "/reference"); window.UpdateLayout();
        var secret = Descendants(secureEditor).OfType<PasswordBox>().Single(x => AutomationProperties.GetName(x).EndsWith("/reference"));
        Check(secret.Password == binding.Parameters["reference"] && !Descendants(secureEditor).OfType<TextBox>().Any(t => t.Text.Contains("synthetic-reference") || t.Text == "SYNTHETIC-A"), "sensitive and secret-reference values never appear in ordinary text editors");
        secret.Password = "secret:synthetic-edited"; secureEditor.ShowPage(0, "供包台标识"); secureEditor.NavigateTo(binding.InstanceId + "/reference");
        Check(ReferenceEquals(secret, Descendants(secureEditor).OfType<PasswordBox>().Single(x => AutomationProperties.GetName(x).EndsWith("/reference"))) && draft.Parameters.Single(p => p.Field.Key == "reference").Value == "secret:synthetic-edited",
            "masked editing and object navigation preserve the exact reference instead of a sentinel");
        var peer = System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(secret);
        var valueProvider = peer!.GetPattern(System.Windows.Automation.Peers.PatternInterface.Value) as System.Windows.Automation.Provider.IValueProvider;
        Check(peer.IsPassword() && valueProvider is not null && valueProvider.Value.Length == 0, "native password automation returns an empty value, never the reference");
        Capture("v3-sensitive-reference");
        await vm.SwitchToAdvancedConfigurationAsync();
        var reviewDraft = vm.CapabilityDrafts.Single(d => d.InstanceId == binding.InstanceId);
        foreach (var field in fields) reviewDraft.Parameters.Add(new(field, binding.Parameters[field.Key]));
        var candidate = Wire.Decode<ParcelConfiguration>(vm.ConfigurationDraft);
        vm.ConfigurationDraft = Wire.Encode(candidate with { Wcs = candidate.Wcs! with { Parameters = new(candidate.Wcs.Parameters) { ["reference"] = "secret:synthetic-edited" } } });
        var maskedReview = vm.GetConfigurationChangeItems().Single(c => c.TechnicalPath.EndsWith("Wcs.Parameters.reference"));
        Check(!maskedReview.Before.Contains("secret:") && !maskedReview.After.Contains("secret:"), "review masks secret-reference even without the Sensitive flag");
        window.Content = client; await vm.LoadCapabilitiesAsync(); editor.ShowPage(2, "左相机");
        foreach (var size in new[] { (1366d, 768d), (1092d, 614d), (800d, 420d) })
        {
            await SetClient(window, size.Item1, size.Item2); window.UpdateLayout();
            var save = (Button)page.FindName("SaveChanges"); var bounds = save.TransformToAncestor(client).TransformBounds(new Rect(save.RenderSize));
            Check(bounds.Bottom <= client.ActualHeight + 1 && bounds.Right <= client.ActualWidth + 1 && save.ActualHeight >= 44 && editor.ActualHeight > 80,
                $"native saved-state footer and scrollable editor remain reachable at {size}: footer={bounds}, client={client.RenderSize}, editorHeight={editor.ActualHeight}, visible={editor.IsVisible}");
            Capture("v3-size-" + size.Item1 + "x" + size.Item2);
        }
        await SetClient(window, 1366, 768); editor.NavigateTo("StationCode");
        if (interactive)
        {
            Directory.CreateDirectory(Path.Combine(root, ".artifacts")); File.WriteAllText(Path.Combine(root, ".artifacts/V3-INTERACTIVE.txt"), evidence);
            var closed = new TaskCompletionSource(); window.Closed += (_, _) => closed.TrySetResult(); await closed.Task;
        }
        else window.Close();
        lines.Add("Native HWND/Dispatcher synthetic runtime; real SDK/RCS, target IPC, physical 100% DPI and touch NOT_RUN.");
        async Task Until(Func<bool> observed)
        {
            // A duplicate RefreshAsync may return while the window's poll is in
            // flight. Assert observable state rather than assuming it applied.
            for (var i = 0; i < 150; i++) { await vm.RefreshAsync(); if (observed()) return; await Task.Delay(20); }
            throw new InvalidOperationException("PS05 state wait expired: " + vm.ReadStatus + " / " + vm.Message);
        }
        void Capture(string name)
        {
            window.UpdateLayout(); var target = (FrameworkElement)window.Content; var dpi = VisualTreeHelper.GetDpi(target);
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(target.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(target.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                var bounds = new Rect(0, 0, target.ActualWidth, target.ActualHeight);
                dc.DrawRectangle(window.Background ?? Brushes.White, null, bounds);
                dc.DrawRectangle(new VisualBrush(target) { Stretch = Stretch.Fill, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = bounds }, null, bounds);
            }
            bitmap.Render(visual);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using var output = File.Create(Path.Combine(evidence, name + ".png")); png.Save(output);
            lines.Add($"CAPTURE {name}: {target.ActualWidth}x{target.ActualHeight} DIP; scale={dpi.DpiScaleX},{dpi.DpiScaleY}");
        }
    }
}

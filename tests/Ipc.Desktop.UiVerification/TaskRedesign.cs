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
    static async Task VerifyTaskRedesign(string root, string evidence, List<string> lines, bool interactive)
    {
        void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); lines.Add("PASS UIR " + reason); }
        var configRoot = Path.Combine(evidence, "config"); Directory.CreateDirectory(configRoot);
        var config = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(root, "config/sportmaster/business.json"))) with { TimeoutMs = 30000 };
        ExtensionStore.AtomicWrite(Path.Combine(configRoot, "business.json"), config);
        var launch = new BusinessLaunch(configRoot, Path.Combine(evidence, "data"), null, null);
        var factory = new VisualFactory(launch, true);
        await using var runtime = new ParcelRuntime(launch, new SportmasterFlow(), factory) { RuntimeIdentityPreflight = () => { } };
        await runtime.StartAsync(default);
        var application = DispatchProxy.Create<IParcelApplication, ProjectionProbe>(); var probe = (ProjectionProbe)(object)application; probe.Inner = runtime;
        var window = new SupplyStationWindow(application, null, "standard") { ConfirmDiscardDrafts = _ => true };
        window.Show(); await Task.Delay(300); await window.ViewModel.RefreshAsync();
        var vm = window.ViewModel; var client = (Grid)window.FindName("ClientArea");
        await SetClient(window, 1600, 900); Capture("work-waiting-1600", client);
        await vm.EmitPositionAsync(vm.Left); await vm.EmitPositionAsync(vm.Right);
        await Until(() => vm.Right.Operation?.State == ParcelState.SlotResolved);
        Check(vm.Left.Operation?.State == ParcelState.ResolvingWcs && vm.Left.TaskId == "—" && vm.Left.WaveNo == "—" && vm.Left.ResponseQuantity == "—", "left processing suppresses the entire task while right completes");
        Capture("work-processing-1600", client); factory.ReleaseLeft.TrySetResult();
        await Until(() => vm.Left.Operation?.State == ParcelState.SlotResolved);
        ParcelSnapshot WithTasks(ParcelSnapshot s) => s with
        {
            Operations = s.Operations.Select(o => o with
            {
                Wcs = o.Wcs! with { Replies = o.Wcs!.Replies.Select(r => r with { Destination = new(WcsDestinationOutcome.Assigned, null, null, null, "TASK-" + o.PositionId, "SKU-" + o.PositionId, null, null, null, null, null, "0007-" + o.PositionId, "0002") }).ToArray() }
            }).ToArray()
        };
        probe.Project = WithTasks; await vm.RefreshAsync();
        Check(vm.Left.TaskId == "TASK-left" && vm.Right.TaskId == "TASK-right" && vm.Left.WaveNo == "0007-left" && vm.Right.ResponseQuantity == "0002", "all task fields correlate to each position's original accepted request");
        Capture("work-assigned-1600", client);
        var leftControl = Descendants(window).OfType<SupplyStationPosition>().Single(x => ReferenceEquals(x.DataContext, vm.Left));
        var barcode = (TextBox)leftControl.FindName("CurrentBarcode"); window.Activate(); Check(barcode.Focus(), "work barcode accepts native focus"); barcode.Select(1, 3);
        for (var i = 0; i < 4; i++) await vm.RefreshAsync();
        Check(barcode.IsKeyboardFocused && barcode.SelectionStart == 1 && barcode.SelectionLength == 3, "polling preserves native selection and focus");
        probe.Project = s => WithTasks(s) with { Inputs = s.Inputs!.Select(i => i.PositionId == "left" ? i with { Outcome = ParcelInputOutcome.Rejected, Observation = i.Observation with { Id = "new-observation", Codes = ["noise", "unmatched"] }, Reason = "合成：无匹配条码", BarcodeSelection = new("NoMatchingCode", null, 0, null, "本次无匹配条码") } : i).ToArray() };
        await vm.RefreshAsync();
        Check(vm.Left.TaskId == "—" && vm.Left.SkuId == "—" && vm.Left.WaveNo == "—" && vm.Left.ResponseQuantity == "—" && vm.Left.Slot == "未取得" && vm.Right.TaskId == "TASK-right" && vm.Left.OriginalResponsibility.Length > 0, "rejected observation clears every current task field and retains original responsibility separately");
        Capture("work-rejected-new-input", client);
        probe.Project = s => WithTasks(s) with { Operations = WithTasks(s).Operations.Select(o => o.PositionId == "left" ? o with { State = ParcelState.WcsUnknown, Wcs = o.Wcs! with { State = StageState.Unknown }, Slot = null } : o).ToArray() };
        await vm.RefreshAsync(); Check(vm.Left.TaskId == "—" && vm.Right.TaskId == "TASK-right", "unknown stage cannot expose a retained accepted destination");
        Capture("work-unknown", client);
        vm.MarkReadFailure("合成读取中断");
        Check(vm.Left.WaveNo == "已失效" && vm.Right.ResponseQuantity == "已失效" && !vm.Right.CurrentTaskAvailable, "stale snapshot invalidates all task fields together"); Capture("work-stale", client);
        probe.Project = WithTasks; await vm.RefreshAsync();
        await VerifyClientGeometry(window, vm, probe, evidence, "supply-station-v2", Check);
        probe.Project = WithTasks; await vm.RefreshAsync(); await SetClient(window, 1366, 768); Capture("work-assigned-1366", client);
        Click(window, "作业记录"); await Task.Delay(120); await vm.OpenHistoryAsync(vm.Left.Operation!.Id);
        Check(vm.Selected?.Id == vm.Left.Operation.Id && ParcelViewModel.DestinationSummary(WithTasks(vm.Snapshot!).Operations.Single(o => o.Id == vm.Selected.Id)).Contains("0007-left"), "history keeps selected original ownership and exposes added response fields");
        Capture("history", client);
        Click(window, "诊断与恢复"); window.UpdateLayout();
        var diagnostics = (ContentControl)window.FindName("PageHost");
        Check(Descendants(diagnostics).OfType<TextBox>().All(t => t.IsReadOnly) && Descendants(diagnostics).OfType<TextBlock>().Any(t => t.Text.Contains("权威结果查询")), "diagnostics remains read-only and explains the actual RCS query boundary");
        Capture("diagnostics", client);
        Click(window, "参数配置"); await Until(() => vm.HasLoadedConfiguration && !vm.Busy);
        var maintenance = (MaintenancePage)((ContentControl)window.FindName("PageHost")).Content;
        var editor = (ParameterSettingsControl)maintenance.FindName("MainParameterEditor");
        editor.NavigateTo("StationCode"); var station = Descendants(editor).OfType<TextBox>().Single(x => AutomationProperties.GetName(x) == "StationCode");
        station.Text = "SYNTHETIC-UNSAVED"; station.Focus(); station.Select(2, 4);
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var busy = vm.RunAsync(() => pending.Task, "合成慢检查");
        try
        {
            await System.Windows.Threading.Dispatcher.Yield();
            Check(editor.IsEnabled && station.IsKeyboardFocused && ((Grid)maintenance.FindName("MaintenanceScope")).IsEnabled, "slow operation keeps object viewing, scrolling and current editing available");
        }
        finally { pending.TrySetResult(); await busy; }
        Click(window, "作业"); Click(window, "参数配置"); await Until(() => !vm.Busy);
        Check(ReferenceEquals(editor, maintenance.FindName("MainParameterEditor")) && station.Text == "SYNTHETIC-UNSAVED", "page navigation retains the same editor and unsaved draft");
        Capture("parameters-system", client);
        var search = Descendants(editor).OfType<TextBox>().Single(x => AutomationProperties.GetName(x) == "搜索参数");
        search.Text = "队列"; window.UpdateLayout();
        Capture("parameters-search", client);
        var hit = Descendants(editor).OfType<Button>().Single(x => AutomationProperties.GetName(x).StartsWith("定位参数 输入队列容量"));
        hit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); window.UpdateLayout();
        Check(search.Text.Length == 0 && Descendants(editor).OfType<TextBox>().Any(x => AutomationProperties.GetName(x) == "QueueCapacity" && x.IsKeyboardFocused),
            "cross-category search locates and focuses the actual field without recreating editors");
        editor.NavigateTo("StationCode");
        var returnedStation = Descendants(editor).OfType<TextBox>().Single(x => AutomationProperties.GetName(x) == "StationCode");
        Check(ReferenceEquals(station, returnedStation) && station.Text == "SYNTHETIC-UNSAVED", $"search and category navigation retain exact unsaved controls and input (same={ReferenceEquals(station, returnedStation)}, before={station.Text}, after={returnedStation.Text})");
        search.Text = "没有这种字段"; window.UpdateLayout();
        Check(Descendants(editor).OfType<TextBlock>().Any(t => t.Text.StartsWith("未找到匹配参数")), "empty search explains how to return without changing the draft");
        search.Clear();
        Click(maintenance, "保存准备…"); window.UpdateLayout();
        Check(((Expander)maintenance.FindName("SavePreparation")).IsExpanded && Descendants(maintenance).OfType<TextBox>().Any(t => AutomationProperties.GetName(t) == "维护变更原因"), "save preparation exposes authorization and reason beside the guarded save action");
        Capture("parameters-save-preparation", client);
        ((Expander)maintenance.FindName("SavePreparation")).IsExpanded = false;
        await VerifyRcsEditor(root, runtime, config, window, Capture, Check);
        window.Content = client; await SetClient(window, 1366, 768); Click(window, "作业");
        if (Environment.GetEnvironmentVariable("IPC_FROZEN_DESKTOP") is { } desktopPath)
        {
            var type = System.Reflection.Assembly.LoadFrom(desktopPath).GetType("Ipc.Desktop.StartupRepairWindow", true)!;
            var offline = launch with { DataRoot = Path.Combine(evidence, "offline-data") };
            var first = (Window)Activator.CreateInstance(type, [offline, null, true])!; first.Show();
            var busyField = type.BaseType!.GetField("Busy", BindingFlags.NonPublic | BindingFlags.Instance)!;
            for (var i = 0; i < 150 && ((bool)busyField.GetValue(first)! || !Descendants(first).OfType<TabControl>().Any(t => t.Name == "StartupTasks")); i++) await Task.Delay(20);
            var tasks = Descendants(first).OfType<TabControl>().Single(t => t.Name == "StartupTasks");
            Check(tasks.Items.Cast<TabItem>().Select(t => t.Header).SequenceEqual(new[] { "1 业务模块", "2 系统", "3 驱动", "4 组件", "5 检查与完成" }), "first configuration has exactly five task steps with no daily navigation");
            ParameterSettingsControl? firstEditor = null;
            for (var i = 0; i < 5; i++)
            {
                tasks.SelectedIndex = i; first.UpdateLayout();
                if (i < 4)
                {
                    var current = Descendants(first).OfType<ParameterSettingsControl>().Single();
                    firstEditor ??= current; Check(ReferenceEquals(current, firstEditor), "startup step " + (i + 1) + " reuses one editor and complete draft");
                }
                Capture("startup-step-" + (i + 1), (FrameworkElement)first.Content);
            }
            Check(!Directory.Exists(offline.DataRoot) || !Directory.GetFiles(offline.DataRoot, "*.db", SearchOption.AllDirectories).Any(), "opening/navigating startup does not create an Application database or start devices");
            first.Close();
        }
        await VerifyUnsavedChoices(window, Capture, Check);
        if (interactive)
        {
            // The same built controls remain open for actual Windows input. No hardware.
            Directory.CreateDirectory(Path.Combine(root, ".artifacts/ui-task-redesign"));
            File.WriteAllText(Path.Combine(root, ".artifacts/ui-task-redesign/INTERACTIVE.txt"), evidence);
            var closed = new TaskCompletionSource(); window.Closed += (_, _) => closed.TrySetResult(); await closed.Task;
        }
        else window.Close();
        lines.Add("WPF synthetic runtime/ports; native HWND and Dispatcher. Physical target IPC, SDK/RCS and site acceptance NOT_RUN.");
        async Task Until(Func<bool> done)
        {
            for (var i = 0; i < 120; i++) { await vm.RefreshAsync(); if (done()) return; await Task.Delay(25); }
            throw new InvalidOperationException("Bounded UI state wait expired.");
        }
        void Capture(string name, FrameworkElement target)
        {
            target.UpdateLayout(); var dpi = VisualTreeHelper.GetDpi(target);
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(target.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(target.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32); bitmap.Render(target);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using var output = File.Create(Path.Combine(evidence, name + ".png")); png.Save(output);
            lines.Add($"CAPTURE {name}: {target.ActualWidth}x{target.ActualHeight} DIP; Windows scale {dpi.DpiScaleX}/{dpi.DpiScaleY}");
        }
    }
    static async Task VerifyUnsavedChoices(Window owner, Action<string, FrameworkElement> capture, Action<bool, string> check)
    {
        async Task<bool> Choose(string button, Func<Task>? save, bool failure = false, int count = 1)
        {
            var ticks = 0; var invoked = false; var sawFailure = false; var timedOut = false;
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
            timer.Tick += (_, _) =>
            {
                var dialog = owner.OwnedWindows.OfType<Window>().SingleOrDefault(x => x.Title == "保留未完成设置");
                if (dialog is null) return;
                if (++ticks > 60) { timedOut = true; dialog.DialogResult = false; return; }
                if (!invoked)
                {
                    capture("unsaved-" + (count > 1 ? "multiple" : failure ? "before-failure" : button == "继续编辑" ? "cancel" : "decision"), (FrameworkElement)dialog.Content);
                    invoked = true;
                    Descendants(dialog).OfType<Button>().Single(b => Equals(b.Content, button)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                else if (failure && Descendants(dialog).OfType<TextBlock>().Any(t => t.Text.Contains("SYNTHETIC-DRAFT-FAIL")))
                {
                    sawFailure = true; capture("unsaved-save-failed", (FrameworkElement)dialog.Content);
                    Descendants(dialog).OfType<Button>().Single(b => Equals(b.Content, "继续编辑")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
            };
            timer.Start();
            bool result;
            try { result = await ConfigurationCloseGuard.ReviewUnsavedAsync(owner, count, save, "关闭"); }
            finally { timer.Stop(); }
            check(!timedOut && invoked && (!failure || sawFailure), "unsaved decision dialog completes the selected action and retains failed-save feedback");
            return result;
        }
        var saves = 0;
        check(!await Choose("继续编辑", () => { saves++; return Task.CompletedTask; }) && saves == 0, "cancel preserves editor without saving");
        check(await Choose("放弃未保存输入并关闭", () => { saves++; return Task.CompletedTask; }) && saves == 0, "explicit discard does not save");
        check(await Choose("加密保留后关闭", () => { saves++; return Task.CompletedTask; }) && saves == 1, "keep calls encrypted draft persistence exactly once before close");
        check(!await Choose("加密保留后关闭", () => throw new IOException("SYNTHETIC-DRAFT-FAIL"), true), "failed preservation cannot authorize closing");
        check(!await Choose("继续编辑", null, count: 2), "multiple unsaved windows retain distinct candidates without merging them");
    }
    static async Task VerifyRcsEditor(string root, ParcelRuntime runtime, ParcelConfiguration config, Window window, Action<string, FrameworkElement> capture, Action<bool, string> check)
    {
        var package = new PackageReference("hikrobot.idmvs-reader", "1.0.3", new string('a', 64));
        var readerFields = Wire.Decode<ParameterField[]>(File.ReadAllText(Path.Combine(root, "src/Ipc.Drivers.Hikrobot/parameters.json")));
        var rcsFields = Wire.Decode<ParameterField[]>(File.ReadAllText(Path.Combine(root, "src/Ipc.Integrations.SportmasterRcs/parameters.json")));
        var reader = new CapabilityDescriptor("observation", "extension:hikrobot.idmvs-reader/idmvs-reader", "1.0.3", "HIKROBOT-IDMVS-OBSERVATION-v1", readerFields, new(), package, "idmvs-reader");
        var rcs = new CapabilityDescriptor("integration", "sportmaster-rcs-task", "1.3.0", "SPORTMASTER-RCS-SORTER-v1", rcsFields, new());
        var binding = new CapabilityBinding("integration", rcs.Implementation, rcs.Version, "rcs.one", "http://192.0.2.10/rcs/sorter/api", rcs.Contract, new()
        {
            ["source"] = "SYNTHETIC", ["locale"] = "zh_CN", ["zone-id"] = "71", ["project-id"] = "83", ["quantity"] = "1",
            ["specified-chute"] = "false", ["chute-code"] = "OLD-CHUTE", ["request-id-base"] = "810000000", ["request-id-count"] = "1000",
            ["left-station-code"] = "OLD-LEFT", ["right-station-code"] = "OLD-RIGHT", ["left-bcr-code"] = "TAKE-L", ["right-bcr-code"] = "TAKE-R"
        });
        var devices = config.Topology!.Readers.Select((r, i) => r with { Driver = "extension:reader." + i }).ToArray();
        var draft = new StartupConfigurationDraft(config with { Wcs = binding, Reader = devices[0], Topology = config.Topology with { Readers = devices },
            Extensions = new([package], devices.Select((r, i) => new ExtensionInstance("reader." + i, package.Id, reader.Module!, r.DeviceId, true, new() { ["device.ip"] = "192.0.2." + (21 + i), ["bcr-code"] = "HEART-" + i })).ToArray()) },
            new([reader, rcs, .. (await runtime.CapabilitiesAsync()).Providers.Where(p => p.Kind is not ("observation" or "integration"))], [package.Id], Packages: [new(package, [])]));
        var model = draft.CreateParameterEditor(runtime.TestBarcodeFilterAsync); var control = new ParameterSettingsControl { Model = model };
        window.Content = control; window.Width = 1366; window.Height = 768; window.UpdateLayout();
        check(!model.ValidateFields() && model.Issues.Any(i => i.Path.EndsWith("left-station-code")), "old unequal station codes block saving without silently choosing a side");
        control.NavigateTo("rcs.one/left-station-code"); window.UpdateLayout();
        var common = Descendants(control).OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "rcs.one/left-station-code");
        check(common.Text.Length == 0, "conflicting legacy pair is visible with blank explicit migration input"); capture("parameters-station-conflict", control);
        common.Text = "SYNTHETIC-COMMON";
        var rcsDraft = draft.Capabilities.Single(c => c.InstanceId == "rcs.one"); var built = rcsDraft.Build();
        check(built.Parameters["left-station-code"] == "SYNTHETIC-COMMON" && built.Parameters["right-station-code"] == "SYNTHETIC-COMMON" && !built.Parameters.ContainsKey("chute-code"), "explicit shared station edit projects both keys and false specified-chute omits inactive value");
        check(rcsDraft.Parameters.Single(p => p.Field.Key == "chute-code").Value == "OLD-CHUTE", "inactive chute input remains in the raw draft");
        check(model.ValidateFields(), "corrected synthetic provider candidate passes field checks before normal-form captures");
        control.NavigateTo("rcs.one/quantity"); window.UpdateLayout(); capture("parameters-business-rules", control);
        control.NavigateTo("reader.1/device.ip"); window.UpdateLayout(); capture("parameters-right-driver", control);
        check(Descendants(control).OfType<TextBox>().Any(t => t.Text == "HEART-1") && Descendants(control).OfType<TextBox>().Any(t => t.Text == "TAKE-R"), "right driver displays distinct heart and take-chute encodings");
        control.NavigateTo("rcs.one/Target"); window.UpdateLayout(); capture("parameters-rcs-component", control);
        check(!Descendants(control).OfType<TextBox>().Any(t => !t.IsReadOnly && t.Text == "71"), "moved system values are read-only references on the RCS object");
        var template = StartupSiteTemplates.Export("sportmaster", draft.Capture());
        check(!template.Defaults.Values.Keys.Any(k => k == "policy.StationCode" || k.Contains("parameter.") || k.StartsWith("policy.Backup")), "safe template excludes heart station code, instance parameters and backup settings");
        var localBefore = draft.Capture();
        var restored = draft.WithTemplate(StartupSiteTemplates.Import("sportmaster", template));
        check(localBefore.Values.All(x => restored.Capture().Values.GetValueOrDefault(x.Key) == x.Value), "same-provider safe template preserves every local parameter and identity");
        var invalidProviderTemplate = new StartupEditorState(new(template.Defaults.Values) { ["cap.0.provider"] = "missing-provider" });
        try { draft.WithTemplate(invalidProviderTemplate); throw new InvalidOperationException("missing template provider unexpectedly accepted"); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("精确提供者")) { }
        check(localBefore.Values.All(x => draft.Capture().Values.GetValueOrDefault(x.Key) == x.Value), "failed template import leaves the entire current draft untouched");
        rcsDraft.Target = "http://192.0.2.10/other";
        rcsDraft.Parameters.Single(p => p.Field.Key == "request-id-base").Value = (long.MaxValue - 1).ToString();
        draft.Readers[1].Parameters.Single(p => p.Field.Key == "bcr-code").Value = "HEART-0";
        check(!model.ValidateFields() && model.Issues.Any(i => i.Path == "rcs.one/Target") &&
            model.Issues.Any(i => i.Path == "rcs.one/request-id-count") && model.Issues.Count(i => i.Path.EndsWith("/bcr-code")) == 2,
            "endpoint, overflowing request range and duplicated heartbeat identities locate exact editable fields");
        control.NavigateTo("rcs.one/Target"); capture("parameters-errors", control);
        await System.Windows.Threading.Dispatcher.Yield();
    }
}

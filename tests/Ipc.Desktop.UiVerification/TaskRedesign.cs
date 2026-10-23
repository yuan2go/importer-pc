using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
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
        await runtime.StartAsync(default); await ManagementModeFixture.Authorize((IParcelApplication)runtime.Application);
        var application = DispatchProxy.Create<IManagementParcelFixture, ProjectionProbe>(); var probe = (ProjectionProbe)(object)application; probe.Inner = runtime;
        var window = new SupplyStationWindow(application, null, "standard") { ConfirmDiscardDrafts = _ => true };
        window.Show(); await Task.Delay(300); await window.ViewModel.RefreshAsync();
        var vm = window.ViewModel; var client = (Grid)window.FindName("ClientArea");
        await SetClient(window, 1600, 900); Capture("work-waiting-1600", client);
        Check(vm.Left.RcsServiceStatus == "RCS 服务：模拟", "simulation does not appear as an online real RCS service");
        foreach (var item in new (bool Ready, string Detail, string Label, bool Expired)[] { (true, "local only", "等待连接确认", false), (true, "RCS heartbeat HTTP 200 at synthetic; application acknowledgement unconfirmed", "在线", false), (false, "RCS heartbeat failed: timeout", "离线", false), (true, "RCS heartbeat HTTP 200 at old", "状态未知", true) })
        {
            probe.Project = s => s with
            {
                Configuration = s.Configuration with { Effective = s.Configuration.Effective with { Mode = RunMode.Production, Wcs = s.Configuration.Effective.Wcs! with { Contract = "SPORTMASTER-RCS-SORTER-v1" } } },
                Health = [new(s.Configuration.Effective.Wcs!.InstanceId, item.Ready, DateTimeOffset.UtcNow.AddSeconds(-2), DateTimeOffset.UtcNow.AddSeconds(item.Expired ? -1 : 3), item.Detail)]
            };
            for (var n = 0; n < 100 && vm.Left.RcsServiceStatus != "RCS 服务：" + item.Label; n++) { await vm.RefreshAsync(); await Task.Delay(20); }
            Check(vm.Left.RcsServiceStatus == "RCS 服务：" + item.Label && vm.Right.RcsServiceStatus == vm.Left.RcsServiceStatus, "both positions present factual RCS " + item.Label + " from the shared service snapshot");
        }
        vm.MarkReadFailure("synthetic RCS snapshot failure");
        Check(vm.Left.RcsServiceStatus == "RCS 服务：状态未知", "stale RCS snapshot cannot retain an online indicator");
        probe.Project = null; await vm.RefreshAsync();
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
        probe.Project = WithTasks;
        // RefreshAsync deliberately coalesces an already-running poll. Wait for
        // this projection to reach the UI, not merely for a duplicate read call.
        await Until(() => vm.Left.TaskId == "TASK-left" && vm.Right.TaskId == "TASK-right" && vm.Left.WaveNo == "0007-left" && vm.Right.ResponseQuantity == "0002");
        Check(vm.Left.TaskId == "TASK-left" && vm.Right.TaskId == "TASK-right" && vm.Left.WaveNo == "0007-left" && vm.Right.ResponseQuantity == "0002", "all task fields correlate to each position's original accepted request");
        Capture("work-assigned-1600", client);
        var leftControl = Descendants(window).OfType<SupplyStationPosition>().Single(x => ReferenceEquals(x.DataContext, vm.Left));
        Check(leftControl.FindName("ReaderIdentity") is null && vm.Left.ReaderIdentity == vm.Left.ReaderId &&
            ((TextBlock)leftControl.FindName("ScanSummary")).Text.Contains(vm.Left.ScanTime) &&
            leftControl.FindName("ReconcileOriginal") is null && !((Button)leftControl.FindName("ContinueOriginal")).IsVisible &&
            ((Button)leftControl.FindName("PositionDiagnostics")).IsVisible,
            "workstation exposes the effective reader and observation time while ordinary work hides inapplicable recovery actions");
        var beforeDiagnostics = (vm.Left.Operation!.Id, vm.Right.Operation!.Id, factory.Resolves, factory.Queries);
        Click(leftControl, "本位诊断"); window.UpdateLayout();
        Check(vm.SelectedPage == "diagnostics" && Descendants((ContentControl)window.FindName("PageHost")).OfType<TextBlock>().Any(t => t.Text.StartsWith(vm.Left.Heading + "\n")),
            "position diagnosis opens read-only context for the clicked reader");
        Click(window, "工作台"); window.UpdateLayout();
        Check(beforeDiagnostics == (vm.Left.Operation!.Id, vm.Right.Operation!.Id, factory.Resolves, factory.Queries) &&
            ReferenceEquals(leftControl, Descendants(window).OfType<SupplyStationPosition>().Single(x => ReferenceEquals(x.DataContext, vm.Left))),
            "diagnosis round trip retains both original operations and native controls without sending or querying a business request");
        var barcode = (TextBox)leftControl.FindName("CurrentBarcode"); window.Activate(); Check(barcode.Focus(), "work barcode accepts native focus"); barcode.Select(1, 3);
        for (var i = 0; i < 4; i++) await vm.RefreshAsync();
        Check(barcode.IsKeyboardFocused && barcode.SelectionStart == 1 && barcode.SelectionLength == 3, "polling preserves native selection and focus");
        probe.Project = s => WithTasks(s) with { Inputs = s.Inputs!.Select(i => i.PositionId == "left" ? i with { Outcome = ParcelInputOutcome.Rejected, Observation = i.Observation with { Id = "new-observation", Codes = ["noise", "unmatched"] }, Reason = "合成：无匹配条码", BarcodeSelection = new("NoMatchingCode", null, 0, null, "本次无匹配条码") } : i).ToArray() };
        await Until(() => vm.Left.Input?.Observation.Id == "new-observation");
        Check(vm.Left.TaskId == "—" && vm.Left.SkuId == "—" && vm.Left.WaveNo == "—" && vm.Left.ResponseQuantity == "—" && vm.Left.Slot == "未取得" && vm.Right.TaskId == "TASK-right" && vm.Left.OriginalResponsibility.Length > 0, "rejected observation clears every current task field and retains original responsibility separately");
        Capture("work-rejected-new-input", client);
        probe.Project = s => WithTasks(s) with { Operations = WithTasks(s).Operations.Select(o => o.PositionId == "left" ? o with { State = ParcelState.WcsUnknown, Wcs = o.Wcs! with { State = StageState.Unknown }, Slot = null } : o).ToArray() };
        await Until(() => vm.Left.Operation?.State == ParcelState.WcsUnknown); Check(vm.Left.TaskId == "—" && vm.Right.TaskId == "TASK-right", "unknown stage cannot expose a retained accepted destination");
        Check(leftControl.FindName("ReconcileOriginal") is null && vm.Left.HasUncertainOriginal &&
            !((Button)leftControl.FindName("ContinueOriginal")).IsVisible && !vm.Right.HasUncertainOriginal,
            "unknown original retains responsibility without a permanent query action and leaves the other position independent");
        Capture("work-unknown", client);
        vm.MarkReadFailure("合成读取中断");
        Check(vm.Left.WaveNo == "已失效" && vm.Right.ResponseQuantity == "已失效" && !vm.Right.CurrentTaskAvailable, "stale snapshot invalidates all task fields together"); Capture("work-stale", client);
        Check(leftControl.FindName("ReconcileOriginal") is null && !vm.Left.CanQueryOriginal &&
            ((Button)leftControl.FindName("PositionDiagnostics")).IsEnabled,
            "stale original retains its context with reconciliation disabled and read-only diagnosis available");
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
        await Until(() => !vm.Busy);
        var firstLoadRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var priorTask = vm.RunAsync(() => firstLoadRelease.Task, "合成：首开参数前尚有任务");
        try
        {
            Click(window, "参数配置"); await System.Windows.Threading.Dispatcher.Yield();
            Check(vm.SelectedPage == "maintenance" && !vm.HasLoadedConfiguration, "opening settings during another task retains a pending load instead of clearing or bypassing that task");
        }
        finally { firstLoadRelease.TrySetResult(); await priorTask; }
        await Until(() => vm.HasLoadedConfiguration && !vm.Busy);
        Check(vm.HasLoadedConfiguration, "first settings load completes after the preceding task without a second navigation click");
        var maintenance = (MaintenancePage)((ContentControl)window.FindName("PageHost")).Content;
        var editor = (ParameterSettingsControl)maintenance.FindName("MainParameterEditor");
        Check(Descendants(window).OfType<Button>().Any(b => b.IsVisible && Equals(b.Content, "返回作业")) &&
            !Descendants(window).OfType<Button>().Any(b => b.IsVisible && Equals(b.Content, "作业")),
            "focused settings replaces the daily sidebar with a visible return-to-work action");
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
        var results = Descendants(editor).OfType<ListBox>().Single(x => AutomationProperties.GetName(x) == "参数搜索结果");
        Check(results.IsVisible && results.Items.Count > 0 && search.Focus(), "parameter search presents matches and accepts native keyboard input");
        search.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(search)!, 0, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent }); window.UpdateLayout();
        Check(search.Text.Length == 0 && Descendants(editor).OfType<TextBox>().Any(x => AutomationProperties.GetName(x) == "QueueCapacity" && x.IsKeyboardFocused),
            "cross-category search locates and focuses the actual field without recreating editors");
        editor.NavigateTo("StationCode");
        var returnedStation = Descendants(editor).OfType<TextBox>().Single(x => AutomationProperties.GetName(x) == "StationCode");
        Check(ReferenceEquals(station, returnedStation) && station.Text == "SYNTHETIC-UNSAVED", $"search and category navigation retain exact unsaved controls and input (same={ReferenceEquals(station, returnedStation)}, before={station.Text}, after={returnedStation.Text})");
        search.Text = "没有这种字段"; window.UpdateLayout();
        Check(results.Items.Count == 0 && Descendants(editor).OfType<TextBlock>().Any(t => t.IsVisible && t.Text.Contains("没有匹配项")), "empty search explains how to return without changing the draft");
        search.Clear();
        window.ShowPositionDiagnostics(vm.Left); window.UpdateLayout(); Capture("work-position-diagnostics", client);
        Click(window, "返回当前对象设置"); await Until(() => vm.SelectedPage == "maintenance" && !vm.Busy); window.UpdateLayout();
        var directory = Descendants(editor).OfType<ListBox>().Single(x => AutomationProperties.GetName(x) == "设置目录");
        Check(directory.SelectedItem?.ToString() == "左相机" && ReferenceEquals(editor, maintenance.FindName("MainParameterEditor")) && station.Text == "SYNTHETIC-UNSAVED",
            "position diagnosis returns to the bound reader settings while retaining the same complete unsaved candidate");
        var revisionBeforeReview = (await runtime.SnapshotAsync()).Configuration.Revision;
        editor.NavigateTo("QueueCapacity"); window.UpdateLayout();
        var budget = Descendants(editor).OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "QueueCapacity");
        var originalBudget = budget.Text; budget.Text = "bad";
        ((Button)maintenance.FindName("SaveChanges")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => !vm.Busy && vm.ParameterEditor!.Issues.Any(i => i.Path == "QueueCapacity")); window.UpdateLayout();
        Check(!window.OwnedWindows.OfType<Window>().Any(w => w.Title == "保存更改") && budget.Text == "bad" &&
            budget.IsKeyboardFocused && station.Text == "SYNTHETIC-UNSAVED",
            "save automatically finds the invalid field and preserves the draft without opening a review or submitting");
        Capture("parameters-save-validation", client);
        budget.Text = originalBudget; editor.NavigateTo("StationCode"); window.UpdateLayout();
        Check(!vm.ParameterEditor!.Issues.Any(i => i.Path == "QueueCapacity") && station.Text == "SYNTHETIC-UNSAVED" &&
            (await runtime.SnapshotAsync()).Configuration.Revision == revisionBeforeReview,
            "correcting and leaving a field refreshes its local error without moving focus, losing another draft or saving");
        Check(vm.IsManagementMode && ((Button)maintenance.FindName("SaveChanges")).IsEnabled,
            "save review is accessible under the authenticated software administrator");
        var reviewSeen = false; double editorHeight = 0;
        await ObserveSaveReviewAsync(window, maintenance, dialog =>
        {
            reviewSeen = !vm.Busy && Descendants(dialog).OfType<TextBox>().Any(t => t.IsVisible && AutomationProperties.GetName(t) == "维护变更原因") &&
                vm.IsManagementMode && !Descendants(dialog).OfType<PasswordBox>().Any(t => t.IsVisible);
            var changes = Descendants(dialog).OfType<DataGrid>().Single(g => AutomationProperties.GetName(g) == "本次设置更改");
            Check(vm.ConfigurationReviewIsCurrent && changes.Items.OfType<ConfigurationChangeItem>().Any(c => c.Path == "StationCode" && c.After == "SYNTHETIC-UNSAVED"),
                "native review displays the current station change from the validated complete candidate");
            Check(vm.Fresh && vm.Snapshot?.Paused == true && !Descendants(dialog).OfType<Button>().Single(b => b.Name == "ReviewPause").IsVisible,
                "confirmed pause is shown as a review condition without a redundant pause action");
            editorHeight = editor.ActualHeight;
            Capture("parameters-save-review", (FrameworkElement)dialog.Content);
        });
        window.UpdateLayout();
        Check(reviewSeen && editor.ActualHeight == editorHeight && station.Text == "SYNTHETIC-UNSAVED" &&
            (await runtime.SnapshotAsync()).Configuration.Revision == revisionBeforeReview,
            "one save review owns authorization and reason without holding Busy, shrinking the editor, or staging on cancel");
        await vm.RunAsync(vm.SwitchToAdvancedConfigurationAsync, "合成：切换同一候选到高级编辑");
        await vm.RunAsync(vm.LoadStructuredFromJsonAsync, "合成：采用同一候选到表单");
        Check(vm.CanEditStructuredConfiguration && vm.GetConfigurationChangeItems().Any(c => c.Path == "StationCode" && c.After == "SYNTHETIC-UNSAVED") &&
            (await runtime.SnapshotAsync()).Configuration.Revision == revisionBeforeReview,
            "adopting the unsaved JSON candidate into the form preserves the original review baseline and never persists it");
        await VerifyRcsEditor(root, runtime, config, window, Capture, Check);
        window.Content = client; await SetClient(window, 1366, 768); Click(window, "作业");
        Check(((Border)window.FindName("WorkstationFooter")).IsVisible && !((TextBlock)window.FindName("SettingsRunMode")).IsVisible,
            "returning to work restores the existing footer without a second settings mode line");
        if (Environment.GetEnvironmentVariable("IPC_FROZEN_DESKTOP") is { } desktopPath)
        {
            var type = System.Reflection.Assembly.LoadFrom(desktopPath).GetType("Ipc.Desktop.StartupRepairWindow", true)!;
            var offline = launch with { DataRoot = Path.Combine(evidence, "offline-data") }; ManagementModeFixture.AuthorizeRoot(offline.DataRoot);
            var first = (Window)Activator.CreateInstance(type, [offline, null, true])!; first.Show();
            var busyField = type.BaseType!.GetField("Busy", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var firstEditor = await FirstConfigurationReady(first, busyField, Check);
            var firstModel = firstEditor.Model;
            for (var i = 0; i < 5; i++)
            {
                var category = new[] { 1, 0, 2, 3, 4 }[i];
                firstEditor.SelectCategory(category); first.UpdateLayout();
                Check(firstEditor.SelectedCategory == category && ReferenceEquals(firstModel, firstEditor.Model) &&
                    ReferenceEquals(firstEditor, Descendants(first).OfType<ParameterSettingsControl>().Single()),
                    "startup section " + (i + 1) + " reuses one editor and complete draft");
                Capture("startup-step-" + (i + 1), (FrameworkElement)first.Content);
            }
            CheckFirstConfigurationReview(first, firstEditor, Check);
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
            throw new InvalidOperationException($"Bounded UI state wait expired. Busy={vm.Busy}, loaded={vm.HasLoadedConfiguration}, page={vm.SelectedPage}; {vm.Message}");
        }
        void Capture(string name, FrameworkElement target)
        {
            target.UpdateLayout(); var dpi = VisualTreeHelper.GetDpi(target);
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(target.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(target.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            // Capture the target's local visual bounds. Direct Render(target) also
            // applies its parent offset, clipping margin-bearing dialog roots.
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                var bounds = new Rect(0, 0, target.ActualWidth, target.ActualHeight);
                dc.DrawRectangle(Window.GetWindow(target)?.Background ?? Brushes.White, null, bounds);
                dc.DrawRectangle(new VisualBrush(target) { Stretch = Stretch.Fill, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = bounds }, null, bounds);
            }
            bitmap.Render(visual);
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

using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
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
    static void CaptureInternal(string evidence, string name, FrameworkElement target, List<string> lines)
    {
        target.UpdateLayout(); var dpi = VisualTreeHelper.GetDpi(target);
        var bounds = new Rect(0, 0, target.ActualWidth, target.ActualHeight);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, bounds);
            dc.DrawRectangle(new VisualBrush(target) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = bounds, Stretch = Stretch.Fill }, null, bounds);
        }
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(bounds.Width * dpi.DpiScaleX), (int)Math.Ceiling(bounds.Height * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(visual); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(evidence, name + ".png")); png.Save(file);
        lines.Add($"CAPTURE {name}: {bounds.Width}x{bounds.Height} DIP, Windows DPI {dpi.DpiScaleX}/{dpi.DpiScaleY}");
        if (Environment.GetEnvironmentVariable("IPC_TYPOGRAPHY_CONTROLS") == "1")
        {
            var controls = new FrameworkElement[] { target }.Concat(Descendants(target).OfType<FrameworkElement>())
                .Where(e => e.IsVisible && (e is TextBlock || e is TextBox || e is Button || e is Window));
            File.WriteAllText(Path.Combine(evidence, name + "-typography.json"), System.Text.Json.JsonSerializer.Serialize(new {
                Dpi = dpi.DpiScaleX, Client = new { Width = bounds.Width, Height = bounds.Height },
                Controls = controls.Select(e => TypographyControlRow(e, target)).ToArray()
            }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
    }
    static async Task VerifyLeafNavigation(Window window, ParameterSettingsControl editor, string evidence, List<string> lines)
    {
        void Check(bool ok, string reason) { if (!ok) throw new InvalidOperationException(reason); lines.Add("PASS LEAF " + reason); }
        async Task Flush() { await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout(); }
        async Task Settled() { for (var i = 0; i < 200 && editor.IsNavigating; i++) await Task.Delay(20); await Flush(); Check(!editor.IsNavigating, "native page validation finishes within its budget"); }
        var model = editor.Model!;
        Check(ChineseInterfaceText.ParameterMessage("") == "", "empty feedback cannot become a fabricated parameter error");
        Check(!ChineseInterfaceText.ParameterMessage("rcs.one · quantity: required").Contains("quantity") && !ChineseInterfaceText.ParameterMessage("request-id-count 参数超出范围").Contains("request-id-count"), "parameter failure banners suppress raw schema keys as well as field-level errors");
        TextBox Box(string path) { window.UpdateLayout(); return Descendants(editor).OfType<TextBox>().Single(b => AutomationProperties.GetName(b) == path); }
        var directory = Descendants(editor).OfType<ListBox>().Single(l => AutomationProperties.GetName(l) == "设置目录");
        editor.ShowPage(1, "条码筛选"); await Flush(); Box("BarcodePattern").Text = "["; await Flush();
        Check(!await editor.MoveNextAsync() && editor.CurrentPageTitle == "条码筛选" && model.Issues.Any(i => i.Path == "BarcodePattern"), "invalid actual regex blocks this leaf page");
        Check(!Descendants(editor).OfType<TextBlock>().Any(t => t.IsVisible && (t.Text.Contains("BarcodeFilter") || t.Text.Contains("BarcodePattern") || t.Text.Contains("required"))), "ordinary error text contains no backing property or raw required rule");
        CaptureInternal(evidence, "leaf-barcode-error", window, lines);
        Box("BarcodePattern").Text = ""; await Flush();
        Check(await editor.MoveNextAsync() && editor.CurrentPageTitle == "格口请求", "corrected optional rule advances only one leaf, not the entire category");
        model.Policy.BarcodePattern = model.Policy.BarcodeExclusionPattern = " ";
        Check(await model.ValidatePageAsync(new HashSet<string> { "BarcodePattern", "BarcodeExclusionPattern" }), "whitespace-only optional rules retain the existing absent-filter semantics");
        model.Policy.BarcodePattern = model.Policy.BarcodeExclusionPattern = "";
        var rcs = model.Components.Single(ParameterOwnership.IsRcs);
        var specified = rcs.Parameters.Single(p => p.Field.Key == "specified-chute"); var originalSpecified = specified.Value;
        specified.Value = ""; await Flush();
        directory.SelectedItem = directory.Items.Cast<object>().Single(p => p.ToString() == "RCS 服务"); await Settled();
        Check(editor.CurrentPageTitle == "格口请求" && model.Issues.Any(i => i.Path == rcs.InstanceId + "/specified-chute"), "forward directory jump cannot skip a missing value on the current leaf");
        specified.Value = originalSpecified;
        var chute = rcs.Parameters.Single(p => p.Field.Key == "chute-code");
        var oldSpecified = specified.Value; var oldChute = chute.Value; specified.Value = "true"; chute.Value = ""; await Flush();
        Check(!await editor.MoveNextAsync() && model.Issues.Any(i => i.Path.EndsWith("/chute-code")), "conditional specified chute remains required on its own page");
        specified.Value = "false"; await Flush();
        Check(await editor.MoveNextAsync() && editor.CurrentPageTitle == "接收与等待限制", "explicitly disabling the condition permits the next leaf without deleting retained values");
        var oldQueue = model.Policy.QueueCapacity; Box("QueueCapacity").Text = "bad"; await Flush();
        Check(!await editor.MoveNextAsync() && editor.CurrentPageTitle == "接收与等待限制", "numeric error blocks processing page");
        Check(!Descendants(editor).OfType<TextBlock>().Any(t => t.IsVisible && t.Text.Contains("QueueCapacity")), "numeric error uses a Chinese name");
        editor.MovePrevious(); await Flush();
        Check(editor.CurrentPageTitle == "格口请求" && model.Policy.QueueCapacity == "bad", $"backward navigation retains invalid process-only input: page={editor.CurrentPageTitle}; value={model.Policy.QueueCapacity}");
        directory.SelectedItem = directory.Items.Cast<object>().Single(p => p.ToString() == "RCS 服务"); await Settled();
        Check(editor.CurrentPageTitle == "接收与等待限制", "forward multi-page jump stops at the first invalid intermediate leaf");
        var search = Descendants(editor).OfType<TextBox>().Single(b => AutomationProperties.GetName(b) == "搜索参数");
        search.Text = "服务地址"; await Flush();
        search.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(search), 0, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent }); await Settled();
        Check(editor.CurrentPageTitle == "接收与等待限制", "keyboard search cannot bypass current-page validation");
        Box("QueueCapacity").Text = oldQueue; await Flush();
        Check(await editor.MoveNextAsync() && editor.CurrentPageTitle == "供包台标识" && model.Issues.All(i => i.Path != "QueueCapacity"), "editing bound input clears obsolete field errors and advances to the real next page");
        specified.Value = oldSpecified; chute.Value = oldChute;
        // A slow format check cannot advance after the user changes the input.
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<BarcodeFilterResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        model.Policy.BarcodePattern = "^initial$";
        var slowModel = new ParameterEditorModel(model.Policy, model.ApplicationId, model.Readers, model.Components, (_, _) => { started.TrySetResult(); return release.Task; });
        var slow = new ParameterSettingsControl { FirstConfiguration = true, Model = slowModel };
        var child = new Window { Owner = window, Content = slow, Width = 800, Height = 600 }; child.Show(); slow.ShowPage(1, "条码筛选"); child.UpdateLayout();
        var pending = slow.MoveNextAsync(); await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Descendants(slow).OfType<TextBox>().Single(b => AutomationProperties.GetName(b) == "BarcodePattern").Text = "^123$";
        release.SetResult(BarcodeFiltering.Select(new("", null), ["__format_check__"]));
        Check(!await pending && slow.CurrentPageTitle == "条码筛选", "a stale async regex result cannot advance after input changes");
        child.Close(); model.Policy.BarcodePattern = ""; model.ValidateFields(); editor.ShowPage(1, "条码筛选"); await Flush();
    }
    static async Task VerifyInternalPages(string root, string evidence, List<string> lines)
    {
        void Check(bool ok, string reason) { if (!ok) throw new InvalidOperationException(reason); lines.Add("PASS INTERNAL " + reason); }
        var configRoot = Path.Combine(evidence, "config"); Directory.CreateDirectory(configRoot);
        var config = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(root, "config/sportmaster/business.json"))) with { Login = null, TimeoutMs = 30000, StationId = "native-" + Wire.Id() };
        ExtensionStore.AtomicWrite(Path.Combine(configRoot, "business.json"), config);
        var launch = new BusinessLaunch(configRoot, Path.Combine(evidence, "data"), null, null); var factory = new VisualFactory(launch, true);
        await using var runtime = new ParcelRuntime(launch, new SportmasterFlow(), factory) { RuntimeIdentityPreflight = () => { } };
        await runtime.StartAsync(default); await ManagementModeFixture.Authorize(runtime);
        var window = new SupplyStationWindow(runtime, null, "standard") { ConfirmDiscardDrafts = _ => true }; window.Show();
        await Task.Delay(300); var vm = window.ViewModel; await vm.RefreshAsync(); var client = (Grid)window.FindName("ClientArea");
        await SetClient(window, 1366, 768); CaptureInternal(evidence, "workstation-1366", client, lines);
        runtime.EmitReferenceAt("left", "native-left", "00123456", "native-left-event"); runtime.EmitReferenceAt("right", "native-right", "00987654", "native-right-event"); factory.ReleaseLeft.TrySetResult();
        for (var i = 0; i < 100 && (vm.Left.Slot == "未取得" || vm.Right.Slot == "未取得"); i++) { await Task.Delay(30); await vm.RefreshAsync(); }
        var ids = (vm.Left.Operation!.Id, vm.Right.Operation!.Id); var calls = (factory.Resolves, factory.Queries);
        Click(window, "作业记录"); for (var i = 0; i < 100 && vm.Busy; i++) await Task.Delay(20); await vm.LoadHistoryAsync(); window.UpdateLayout();
        var history = (HistoryPage)((ContentControl)window.FindName("PageHost")).Content;
        var rows = (DataGrid)history.FindName("HistoryRows"); Check(rows.Items.Count == 2, "query uses the real application history selection");
        CaptureInternal(evidence, "history-1366", client, lines);
        rows.SelectedIndex = 0; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout(); CaptureInternal(evidence, "history-selected-1366", client, lines);
        vm.HistoryBarcode = "00123456"; await vm.LoadHistoryAsync(); Check(rows.Items.Count == 1 && vm.HistoryItems.Single().Operation.ParcelCode == "00123456", "full barcode query preserves leading zeros and distinct entities");
        vm.ClearHistoryFilters(); window.UpdateLayout();
        Check(vm.HistoryItems.Length == 1 && Descendants(history).OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "历史完整条码").Text == "", "clear filters updates fields without changing the previous fixed result range");
        vm.HistoryBarcode = "not-present"; await vm.LoadHistoryAsync(); CaptureInternal(evidence, "history-empty", client, lines); Check(rows.Items.Count == 0, "empty query has no invented records");
        vm.ClearHistoryFilters(); await vm.LoadHistoryAsync();
        await SetClient(window, 800, 420); CaptureInternal(evidence, "history-800", client, lines);
        Check(rows.ActualHeight >= 72 && rows.TranslatePoint(new Point(), client).Y + rows.ActualHeight <= client.ActualHeight + 1, $"compact history result stays inside the native client: height={rows.ActualHeight}, top={rows.TranslatePoint(new Point(), client).Y}, client={client.ActualHeight}");
        var firstRow = Descendants(rows).OfType<DataGridRow>().OrderBy(r => r.GetIndex()).First();
        Check(firstRow.ActualHeight >= 32 && firstRow.TranslatePoint(new Point(), rows).Y + firstRow.ActualHeight <= rows.ActualHeight, "compact history keeps a complete header and at least one full result row");
        ((Expander)history.FindName("HistoryFilters")).IsExpanded = true; window.UpdateLayout(); CaptureInternal(evidence, "history-filters-800", client, lines);
        Check(((ScrollViewer)history.FindName("HistoryFilterScroll")).ScrollableHeight > 0 && rows.ActualHeight >= 72, "long filters scroll locally while retaining query actions and results");
        await SetClient(window, 1366, 768); window.ShowPositionDiagnostics(vm.Right); window.UpdateLayout(); CaptureInternal(evidence, "diagnostics-1366", client, lines);
        Check(Descendants(window).OfType<TextBox>().Any(t => AutomationProperties.GetName(t) == "业务状态"), "diagnostics labels the actual state instead of exposing unlabeled values");
        await SetClient(window, 800, 420); CaptureInternal(evidence, "diagnostics-800", client, lines);
        Click(window, "返回作业"); await vm.RefreshAsync();
        Check(ids == (vm.Left.Operation!.Id, vm.Right.Operation!.Id) && calls == (factory.Resolves, factory.Queries), "query and diagnostic navigation retain both original operation identities and issue no new business calls");
        Click(window, "参数设置"); await vm.EnsureMaintenanceLoadedAsync(); window.UpdateLayout();
        var editor = Descendants(window).OfType<ParameterSettingsControl>().Single(); editor.ShowPage(1, "接收与等待限制");
        editor.Model!.Policy.QueueCapacity = "bad"; Check(!await editor.MoveNextAsync(), "daily parameter editor uses the same per-leaf guard");
        CaptureInternal(evidence, "daily-parameter-error-800", client, lines); editor.Model.Policy.QueueCapacity = "64";
        Check(await editor.MoveNextAsync(), "daily editor continues after correction");
        var snapshot = vm.StructuredPolicy; snapshot!.TimeoutMs = "28000"; await runtime.ExitManagementModeAsync(); await vm.RefreshAsync(); await Task.Delay(650);
        Check(!vm.CanEditFormConfiguration && vm.SelectedPage == "workstation" && ReferenceEquals(snapshot, vm.StructuredPolicy) && snapshot.TimeoutMs == "28000", "revocation blocks restricted workspace and retains only process draft input");
        window.Close();
        await ManagementModeFixture.Authorize(runtime);
        await VerifyLogRecovery(runtime, evidence, lines);
        lines.Add("Native WPF HWND/Dispatcher, synthetic data/ports only. Production devices, installation and OS DPI changes NOT_RUN.");
    }
    static async Task VerifyLogRecovery(IWorkstationAdministration administration, string evidence, List<string> lines)
    {
        void Check(bool ok, string reason) { if (!ok) throw new InvalidOperationException(reason); lines.Add("PASS LOG-UI " + reason); }
        var service = DispatchProxy.Create<IWorkstationAdministration, LogUiProbe>(); var probe = (LogUiProbe)(object)service; probe.Inner = administration;
        var control = new SoftwareLogControl(service); var window = new Window { Content = control, Width = 820, Height = 620 }; window.Show();
        async Task Flush() { await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout(); }
        Button Button(string label) => Descendants(control).OfType<Button>().Single(b => Equals(b.Content, label));
        async Task Settled() { for (var i = 0; i < 200 && !Button("查询").IsEnabled; i++) await Task.Delay(20); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout(); Check(Button("查询").IsEnabled, "query state settles after cancellation/failure/success"); }
        probe.Query = async (_, _, token) => { await Task.Delay(Timeout.Infinite, token); throw new InvalidOperationException(); };
        Click(window, "查询"); await Task.Delay(50); window.UpdateLayout();
        Check(!Button("查询").IsEnabled && Button("取消当前任务").IsVisible, "only a real pending task exposes an enabled cancellation action");
        CaptureInternal(evidence, "logs-pending", window, lines); Click(window, "取消当前任务"); await Settled();
        Check(!Button("取消当前任务").IsVisible && Descendants(control).OfType<TextBlock>().Any(t => t.Text.Contains("任务已取消")), "cancel completes without a fabricated result or commit claim");
        probe.Query = (_, _, _) => Task.FromException<SoftwareLogPage>(new IOException("SYNTHETIC InternalField.Name unavailable"));
        Click(window, "查询"); await Settled();
        var diagnostic = Descendants(control).OfType<Expander>().Single(e => Equals(e.Header, "诊断详情"));
        Check(diagnostic.IsVisible && !diagnostic.IsExpanded && !Descendants(control).OfType<TextBlock>().Any(t => t.IsVisible && t.Text.Contains("InternalField")), "technical query failure stays in collapsed diagnostic detail");
        CaptureInternal(evidence, "logs-failed", window, lines);
        probe.Query = (_, _, _) => Task.FromResult(new SoftwareLogPage("empty", [], false, [])); Click(window, "查询"); await Settled();
        Check(!Button("下一页").IsEnabled && Descendants(control).OfType<DataGrid>().Single().Items.Count == 0, "empty authoritative result disables paging"); CaptureInternal(evidence, "logs-empty", window, lines);
        var offsets = new List<int>(); var failNext = true;
        var entry = new SoftwareLogEntry(new("fixture", 1, "hash", "synthetic.jsonl"), "合成", DateTimeOffset.UtcNow, "信息", "{\"message\":\"合成查询状态\",\"Path\":\"D:\\\\synthetic\"}");
        probe.Query = (_, offset, _) =>
        {
            offsets.Add(offset);
            if (offset == 200 && failNext) { failNext = false; return Task.FromException<SoftwareLogPage>(new IOException("SYNTHETIC next page failed")); }
            return Task.FromResult(new SoftwareLogPage("fixed", [entry], offset == 0, []));
        };
        Click(window, "查询"); await Settled(); Click(window, "下一页"); await Settled(); Click(window, "下一页"); await Settled();
        Check(offsets.SequenceEqual(new[] { 0, 200, 200 }) && Button("上一页").IsEnabled && !Button("下一页").IsEnabled, "failed paging does not commit or skip the requested offset on retry");
        var grid = Descendants(control).OfType<DataGrid>().Single(); grid.SelectedIndex = 0; Descendants(control).OfType<Expander>().Single(e => Equals(e.Header, "完整日志与上下文")).IsExpanded = true; window.UpdateLayout();
        // Original formatting now completes in the background; retain the exact
        // content assertion after a bounded wait for that actual presentation.
        for (var i = 0; i < 250 && !Descendants(control).OfType<TextBox>().Single(t => t.IsReadOnly && t.AcceptsReturn).Text.Contains(entry.Text); i++) await Task.Delay(20);
        Check(Descendants(control).OfType<TextBox>().Single(t => t.IsReadOnly && t.AcceptsReturn).Text.Contains(entry.Text), "selected row detail preserves the exact original record");
        Descendants(control).OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "日志关键词").Text = "changed"; Descendants(control).OfType<Expander>().Single(e => Equals(e.Header, "时间、更多筛选、上下文与导出")).IsExpanded = true; window.UpdateLayout();
        Check(grid.Items.Count == 0 && !Button("导出全部匹配…").IsEnabled && !Button("下一页").IsEnabled, "changed input invalidates the prior snapshot before export or paging");
        window.Width = 800 + window.ActualWidth - control.ActualWidth;
        window.Height = 420 + window.ActualHeight - control.ActualHeight;
        await Flush();
        Check(Math.Abs(control.ActualWidth - 800) < 1 && Math.Abs(control.ActualHeight - 420) < 1, "log regression uses the actual 800x420 DIP client");
        var keyword = Descendants(control).OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "日志关键词");
        var level = Descendants(control).OfType<ComboBox>().Single();
        var details = Descendants(control).OfType<Expander>().Single(e => Equals(e.Header, "完整日志与上下文"));
        async Task ExpandDetail()
        {
            grid.SelectedIndex = 0; details.IsExpanded = true; await Flush();
            Check(details.IsVisible && details.IsExpanded && !keyword.IsVisible && !level.IsVisible, "compact detail temporarily owns the filter space");
        }
        void CheckFilters(string transition)
        {
            Check(keyword.IsVisible && level.IsVisible && !details.IsExpanded && !details.IsVisible && keyword.Text == "changed",
                transition + " restores keyword/level controls and draft after clearing detail");
        }
        Click(window, "查询"); await Settled(); await ExpandDetail();
        CaptureInternal(evidence, "logs-compact-detail-before-query", control, lines);
        Click(window, "查询"); await Settled(); CheckFilters("query");
        CaptureInternal(evidence, "logs-compact-filters-after-query", control, lines);
        await ExpandDetail(); Click(window, "下一页"); await Settled(); CheckFilters("paging");
        await ExpandDetail(); level.SelectedIndex = 1; await Flush(); CheckFilters("filter change");
        Check(grid.Items.Count == 0 && !Button("下一页").IsEnabled, "restored filters retain snapshot invalidation");
        Click(window, "查询"); await Settled(); await ExpandDetail();
        window.Content = null; await Flush(); window.Content = control; await Flush(); CheckFilters("unload and return");
        Check(grid.Items.Count == 0 && !Button("导出全部匹配…").IsEnabled, "return requires a fresh query before export");
        window.Close();
    }
}

public class LogUiProbe : DispatchProxy
{
    public IWorkstationAdministration Inner = null!;
    public Func<SoftwareLogQuery, int, CancellationToken, Task<SoftwareLogPage>>? Query;
    public Func<Task<SoftwareLogContext>>? Context { get; set; }
    protected override object? Invoke(MethodInfo? method, object?[]? arguments)
        => method!.Name == nameof(IWorkstationAdministration.QueryLogsAsync) && Query is not null
            ? Query((SoftwareLogQuery)arguments![0]!, (int)arguments[2]!, (CancellationToken)arguments[3]!)
            : method.Name == nameof(IWorkstationAdministration.LogContextAsync) && Context is not null ? Context()
            : method.Invoke(Inner, arguments);
}

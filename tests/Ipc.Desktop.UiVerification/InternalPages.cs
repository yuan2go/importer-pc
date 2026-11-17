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
            File.WriteAllText(Path.Combine(evidence, name + "-typography.json"), System.Text.Json.JsonSerializer.Serialize(new
            {
                Dpi = dpi.DpiScaleX,
                Client = new { Width = bounds.Width, Height = bounds.Height },
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
        TextBox Box(string path) { window.UpdateLayout(); return Descendants(editor).OfType<TextBox>().Single(b => AutomationProperties.GetAutomationId(b) == path); }
        var directory = Descendants(editor).OfType<ListBox>().Single(l => AutomationProperties.GetName(l) == "设置目录");
        editor.ShowPage(1, "条码筛选"); await Flush(); Box("BarcodePattern").Text = "["; await Flush();
        Check(!await editor.MoveNextAsync() && editor.CurrentPageTitle == "条码筛选" && model.Issues.Any(i => i.Path == "BarcodePattern"), "invalid actual regex blocks this leaf page");
        Check(!Descendants(editor).OfType<TextBlock>().Any(t => t.IsVisible && (t.Text.Contains("BarcodeFilter") || t.Text.Contains("BarcodePattern") || t.Text.Contains("required"))), "ordinary error text contains no backing property or raw required rule");
        CaptureInternal(evidence, "leaf-barcode-error", window, lines);
        Box("BarcodePattern").Text = ""; await Flush();
        Check(await editor.MoveNextAsync() && editor.IsReviewPage, "corrected optional rule advances to the current four-step completion page");
        model.Policy.BarcodePattern = model.Policy.BarcodeExclusionPattern = " ";
        await Flush();
        var whitespaceValid = await model.ValidatePageAsync(new HashSet<string> { "BarcodePattern", "BarcodeExclusionPattern" });
        if (!whitespaceValid) lines.Add("LEAF WHITESPACE " + string.Join(" | ", model.Issues.Select(i => i.Path + ":" + i.Message)) + " :: " + Wire.Encode(model.Policy.BuildBarcodePolicy()));
        Check(whitespaceValid, "whitespace-only optional rules retain the existing absent-filter semantics");
        model.Policy.BarcodePattern = model.Policy.BarcodeExclusionPattern = "";
        Check(!editor.ShowPage(1, "接收与等待限制"), "retired queue page cannot be opened");
        editor.NavigateTo("ReadTimeoutMs"); var originalDeadline = model.Policy.ReadTimeoutMs;
        Box("ReadTimeoutMs").Text = "bad"; await Flush();
        Check(!await editor.MoveNextAsync() && !editor.IsReviewPage, "invalid automatic reading deadline blocks forward navigation");
        Check(!Descendants(editor).OfType<TextBlock>().Any(t => t.IsVisible && t.Text.Contains("ReadTimeoutMs")), "numeric error uses a Chinese field name");
        editor.MovePrevious(); await Flush();
        Check(model.Policy.ReadTimeoutMs == "bad" && editor.SelectedCategory == 2, "backward navigation preserves invalid deadline input while returning to cameras");
        editor.NavigateTo("ReadTimeoutMs");
        Descendants(editor).OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == "FirstSetup.Category.4").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Settled();
        Check(!editor.IsReviewPage && model.Policy.ReadTimeoutMs == "bad", "the completion step button cannot bypass current-page validation");
        Box("ReadTimeoutMs").Text = originalDeadline; await Flush();
        Check(await editor.MoveNextAsync() && editor.IsReviewPage && model.Issues.All(i => i.Path != "ReadTimeoutMs"), "correcting the bound deadline clears its error and permits completion");
        // A slow format check cannot advance after the user changes the input.
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<BarcodeFilterResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        model.Policy.BarcodePattern = "^initial$";
        var slowModel = new ParameterEditorModel(model.Policy, model.ApplicationId, model.Readers, model.Components, (_, _) => { started.TrySetResult(); return release.Task; });
        var slow = new ParameterSettingsControl { FirstConfiguration = true, Model = slowModel };
        var child = new Window { Owner = window, Content = slow, Width = 800, Height = 600 }; await using var childOwner = new NativeWindowOwner(child);
        Task<bool>? pending = null;
        try
        {
            child.Show(); slow.ShowPage(1, "条码筛选"); child.UpdateLayout();
            pending = slow.MoveNextAsync(); await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Descendants(slow).OfType<TextBox>().Single(b => AutomationProperties.GetAutomationId(b) == "BarcodePattern").Text = "^123$";
            release.TrySetResult(BarcodeFiltering.Select(new("", null), ["__format_check__"]));
            Check(!await pending.WaitAsync(TimeSpan.FromSeconds(5)) && slow.CurrentPageTitle == "条码筛选", "a stale async regex result cannot advance after input changes and finishes within its real await budget");
        }
        finally
        {
            release.TrySetResult(BarcodeFiltering.Select(new("", null), ["__format_check__"]));
            if (pending is not null) await pending.WaitAsync(TimeSpan.FromSeconds(5));
        }
        model.Policy.BarcodePattern = ""; model.ValidateFields(); editor.ShowPage(1, "条码筛选"); await Flush();
    }
    static async Task VerifyInternalPages(string root, string evidence, List<string> lines)
    {
        var folder = Path.Combine(evidence, "leaf-config"); Directory.CreateDirectory(folder);
        var config = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(root, "tests/Ipc.Application.Verification/sportmaster.synthetic.json"))) with { Login = null };
        ExtensionStore.AtomicWrite(Path.Combine(folder, "business.json"), config);
        var launch = new BusinessLaunch(folder, Path.Combine(evidence, "leaf-data"), null, null);
        await using var runtime = new ParcelRuntime(launch, new SportmasterFlow(), new CommercialProductization.SyntheticFactory(launch)) { RuntimeIdentityPreflight = () => { } };
        await runtime.StartAsync(default); await ManagementModeFixture.Authorize(runtime);
        var vm = new ParcelViewModel(runtime, "SM设置回归", "合成设备"); await vm.RefreshAsync(); await vm.LoadCapabilitiesAsync();
        var editor = new ParameterSettingsControl { FirstConfiguration = true, Model = vm.ParameterEditor! };
        var window = new Window { Content = editor, Width = 1000, Height = 720 }; await using var owner = new NativeWindowOwner(window); window.Show();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var revision = (await runtime.SnapshotAsync()).Configuration.Revision;
        await VerifyLeafNavigation(window, editor, evidence, lines);
        if ((await runtime.SnapshotAsync()).Configuration.Revision != revision) throw new InvalidOperationException("Leaf navigation unexpectedly persisted configuration");
        lines.Add("PASS INTERNAL actual current SM leaf validation and stale async navigation leave the configuration ledger unchanged");
        await VerifyLogRecovery(runtime, evidence, lines);
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
        Check(!Button("取消当前任务").IsVisible && Descendants(control).OfType<TextBlock>().Any(t => t.Text.Contains("操作已取消")), "cancel completes without a fabricated result or commit claim");
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
        var grid = Descendants(control).OfType<DataGrid>().Single(); grid.SelectedIndex = 0; Descendants(control).OfType<Expander>().Single(e => Equals(e.Header, "日志原文")).IsExpanded = true; window.UpdateLayout();
        // Original formatting now completes in the background; retain the exact
        // content assertion after a bounded wait for that actual presentation.
        for (var i = 0; i < 250 && !Descendants(control).OfType<TextBox>().Single(t => t.IsReadOnly && t.AcceptsReturn).Text.Contains(entry.Text); i++) await Task.Delay(20);
        Check(Descendants(control).OfType<TextBox>().Single(t => t.IsReadOnly && t.AcceptsReturn).Text.Contains(entry.Text), "selected row detail preserves the exact original record");
        Descendants(control).OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "日志关键词").Text = "changed"; Descendants(control).OfType<Expander>().Single(e => Equals(e.Header, "更多筛选")).IsExpanded = true; window.UpdateLayout();
        Check(grid.Items.Count == 0 && !Button("导出…").IsEnabled && !Button("下一页").IsEnabled, "changed input invalidates the prior snapshot before export or paging");
        window.Width = 800 + window.ActualWidth - control.ActualWidth;
        window.Height = 420 + window.ActualHeight - control.ActualHeight;
        await Flush();
        Check(Math.Abs(control.ActualWidth - 800) < 1 && Math.Abs(control.ActualHeight - 420) < 1, "log regression uses the actual 800x420 DIP client");
        var keyword = Descendants(control).OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "日志关键词");
        var level = Descendants(control).OfType<ComboBox>().Single(c => AutomationProperties.GetName(c) == "日志级别");
        var details = Descendants(control).OfType<Expander>().Single(e => Equals(e.Header, "日志原文"));
        async Task ExpandDetail()
        {
            grid.SelectedIndex = 0; details.IsExpanded = true; await Flush();
            Check(details.IsVisible && details.IsExpanded && !keyword.IsVisible && !level.IsVisible, "compact detail temporarily owns the filter space");
        }
        void CheckFilters(string transition)
        {
            var advanced = Descendants(control).OfType<Expander>().Single(e => Equals(e.Header, "更多筛选"));
            Check(keyword.IsVisible && advanced.IsVisible && !details.IsExpanded && !details.IsVisible && keyword.Text == "changed",
                transition + " restores keyword, more-filter access and draft after clearing detail");
            advanced.IsExpanded = true; window.UpdateLayout();
            Check(level.IsVisible, transition + " keeps the existing level field reachable through the current more-filter disclosure");
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
        Check(grid.Items.Count == 0 && !Button("导出…").IsEnabled, "return requires a fresh query before export");
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

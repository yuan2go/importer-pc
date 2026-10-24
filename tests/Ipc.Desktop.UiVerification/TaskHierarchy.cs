using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Platform;

static partial class Program
{
    static async Task VerifyTaskHierarchy(string root, string evidence, List<string> lines, bool baseline)
    {
        void Check(bool ok, string reason) { if (!ok) throw new InvalidOperationException(reason); lines.Add("PASS HIERARCHY " + reason); }
        var configRoot = Path.Combine(evidence, "config"); Directory.CreateDirectory(configRoot);
        var seed = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(root, "config/sportmaster/business.json")));
        var config = seed with { Login = seed.MaintenanceLogin! with { InstanceId = "synthetic.operator", Target = "reference://operator", Parameters = new() { ["subjects"] = "alice" } } };
        ExtensionStore.AtomicWrite(Path.Combine(configRoot, "business.json"), config);
        var launch = new BusinessLaunch(configRoot, Path.Combine(evidence, "data"), null, null);
        await using var runtime = new ParcelRuntime(launch, new SportmasterFlow(), new CommercialProductization.SyntheticFactory(launch)) { RuntimeIdentityPreflight = () => { } };
        await runtime.StartAsync(default);
        var app = DispatchProxy.Create<IManagementParcelFixture, HierarchyProjectionProbe>(); var probe = (ProjectionProbe)(object)app; probe.Inner = runtime;
        foreach (var ui in new[] { "classic", "supply-station" })
        {
            var window = Presentation.Create(app, null, "standard", ui);
            if (window is ShellWindow shell) shell.ConfirmDiscardDrafts = _ => true;
            else ((SupplyStationWindow)window).ConfirmDiscardDrafts = _ => true;
            window.Show(); await Task.Delay(300); var vm = (ParcelViewModel)window.DataContext;
            void Navigate(string page)
            {
                if (window is not ShellWindow) { Click(window, page); return; }
                var button = Descendants(window).OfType<Button>().FirstOrDefault(b => Equals(b.Content, page) && b.IsVisible);
                if (button is not null) button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                else Descendants(window).OfType<MenuItem>().First(m => Equals(m.Header, "页面与窗口")).Items.OfType<MenuItem>().Single(m => Equals(m.Header, page)).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            }
            var client = (Grid)window.FindName("ClientArea"); var host = (ContentControl)window.FindName("PageHost");
            await SetClient(window, 1366, 768); await vm.RefreshAsync();
            CaptureInternal(evidence, ui + "-login-1366", client, lines);
            await ManagementModeFixture.Authorize(runtime); vm.OperatorSubject = "alice"; await vm.LoginAsync("SIMULATION_ONLY"); await vm.RefreshAsync();
            var session = vm.Snapshot!.CurrentOperator!.SessionId;
            vm.SelectedPositionId = "left"; vm.AdmissionId = "hierarchy-" + ui; vm.ParcelCode = "000123456789"; vm.Emit();
            for (var i = 0; i < 150 && (vm.Current?.State != ParcelState.SlotResolved || vm.Current.AdmissionId != vm.AdmissionId); i++) { await Task.Delay(20); await vm.RefreshAsync(); }
            Check(vm.Current?.State == ParcelState.SlotResolved && vm.Current.AdmissionId == vm.AdmissionId, ui + " captures the newly emitted synthetic parcel, not the previous one");
            var originalOperations = string.Join("|", vm.Operations.Select(o => o.Id));
            foreach (var size in new[] { (1366d, 768d), (1092d, 614d), (800d, 420d) })
            {
                await SetClient(window, size.Item1, size.Item2);
                foreach (var page in new[] { "工作台", "作业记录", "参数设置", "日志查询" })
                {
                    Navigate(page);
                    if (vm is SupplyStationViewModel stationVm)
                    {
                        var expected = page switch { "工作台" => "workstation", "作业记录" => "history", "参数设置" => "maintenance", _ => "logs" };
                        for (var i = 0; i < 100 && stationVm.SelectedPage != expected; i++) await Task.Delay(20);
                        Check(stationVm.SelectedPage == expected, "native navigation reaches " + expected);
                    }
                    for (var i = 0; i < 150 && vm.Busy; i++) await Task.Delay(20);
                    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
                    if (page == "参数设置") { await vm.EnsureMaintenanceLoadedAsync(); Descendants(host).OfType<ParameterSettingsControl>().Single().ShowPage(0, "数据与记录"); }
                    if (page == "日志查询")
                    {
                        Click(host, "查询");
                        var query = Descendants(host).OfType<Button>().Single(b => Equals(b.Content, "查询"));
                        for (var i = 0; i < 100 && !query.IsEnabled; i++) await Task.Delay(20);
                        Check(query.IsEnabled && Descendants(host).OfType<DataGrid>().Single().Items.Count == 12, "native log page shows the completed synthetic query result");
                    }
                    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
                    var key = ui + "-" + page + "-" + (int)size.Item1;
                    CaptureInternal(evidence, key, client, lines);
                    lines.Add($"GEOMETRY {key}: PageHost Y={host.TranslatePoint(new Point(), client).Y:F1} Height={host.ActualHeight:F1}");
                    if (!baseline)
                    {
                        Check(Inside(host, client) && host.ActualHeight >= (size.Item1 == 800 ? 200 : size.Item2 * .60), key + " preserves usable task content");
                        if (page != "工作台" && window is ShellWindow)
                            Check(!((PasswordBox)window.FindName("LoginProof")).IsVisible && !Descendants(window).OfType<Button>().Single(b => Equals(b.Content, "核对原请求")).IsVisible, key + " does not expose unrelated login or current-position recovery");
                        if (page == "参数设置") Check(Inside(Descendants(host).OfType<Button>().Single(b => Equals(b.Content, "保存更改…")), client), key + " keeps the actual save action visible");
                    }
                }
            }
            if (baseline)
            {
                window.Close(); for (var i = 0; i < 100 && window.IsVisible; i++) await Task.Delay(20);
                continue;
            }
            Check(string.Join("|", vm.Operations.Select(o => o.Id)) == originalOperations, ui + " navigation retains original operations without creating another parcel");
            Navigate("参数设置"); await vm.EnsureMaintenanceLoadedAsync();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
            var editor = Descendants(host).OfType<ParameterSettingsControl>().Single(); editor.ShowPage(1, "处理参数");
            window.UpdateLayout();
            vm.StructuredPolicy!.TimeoutMs = "28000"; var draft = vm.StructuredPolicy;
            var input = Descendants(editor).OfType<TextBox>().Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "TimeoutMs");
            input.BringIntoView(); input.Focus(); input.SelectAll(); await vm.RefreshAsync();
            Check(input.IsKeyboardFocused && input.SelectedText == input.Text, ui + " polling preserves field focus and selection");
            Check(input.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)), ui + " native keyboard traversal remains available");
            if (!baseline && window is ShellWindow classic)
            {
                vm.Message = "QueueCapacity: invalid value"; window.UpdateLayout();
                Check(!((TextBlock)classic.FindName("OperationFeedback")).Text.Contains("QueueCapacity"), "parameter feedback retains Chinese projection");
                var diagnostics = (Expander)classic.FindName("StationDiagnostics"); diagnostics.IsExpanded = true; window.UpdateLayout();
                Check(Inside(diagnostics, client) && host.ActualHeight >= 120, "expanded bottom diagnostics leave task content available");
                CaptureInternal(evidence, "classic-diagnostics-800", client, lines); diagnostics.IsExpanded = false;
                Navigate("工作台");
                window.UpdateLayout();
                var recovery = Descendants(classic).OfType<Button>().Single(b => Equals(b.Content, "核对原请求"));
                var recoveryY = recovery.TranslatePoint(new Point(), client).Y;
                var gate = new TaskCompletionSource(); var pending = vm.RunAsync(() => gate.Task, "合成等待"); window.UpdateLayout();
                Check(recovery.IsVisible && !recovery.IsEnabled && recovery.TranslatePoint(new Point(), client).Y == recoveryY, "busy operation disables recovery without hiding or moving its fixed command row");
                gate.SetResult(); await pending; window.UpdateLayout();
                var position = Descendants(host).OfType<Button>().First(b => Equals(b.Content, "选择此位为命令目标"));
                var positionGrid = (Grid)position.Parent;
                var detail = Descendants(positionGrid).OfType<Expander>().Single(); detail.IsExpanded = true; window.UpdateLayout();
                var positionScroll = (ScrollViewer)positionGrid.Children[0]; positionScroll.ScrollToEnd(); window.UpdateLayout();
                var previousOffset = positionScroll.VerticalOffset; await vm.RefreshAsync(); await Task.Delay(650); window.UpdateLayout();
                Check(Descendants(host).Contains(positionScroll) && detail.IsExpanded && previousOffset > 0 && positionScroll.VerticalOffset == previousOffset, "classic position refresh retains native detail expansion and scroll offset");
                positionScroll.ScrollToTop(); detail.IsExpanded = false;
                vm.MarkReadFailure("合成状态读取失败"); window.UpdateLayout();
                Check(((TextBlock)classic.FindName("ActiveBlockingReasons")).IsVisible && !vm.CanResumeInViewport, "stale state remains visible and commands remain guarded");
                CaptureInternal(evidence, "classic-stale-800", client, lines); await vm.RefreshAsync();
                probe.Project = s => s with { BlockingReasons = Enumerable.Range(1, 18).Select(i => "合成长阻断原因 " + i + "：保留原包裹和请求，核对后处理。").ToArray() };
                for (var i = 0; i < 100 && (vm.Snapshot?.BlockingReasons.Length != 18 || !vm.Fresh); i++) { await vm.RefreshAsync(); await Task.Delay(20); }
                Check(vm.Fresh && vm.Snapshot?.BlockingReasons.Length == 18, "long blocker projection replaces the stale snapshot before measuring layout");
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                window.UpdateLayout(); var status = (ScrollViewer)classic.FindName("OperationalStatusScroll");
                lines.Add($"LONG-BLOCKERS host={host.ActualHeight} status={status.ActualHeight} scrollable={status.ScrollableHeight}");
                CaptureInternal(evidence, "classic-long-blockers-start-800", client, lines);
                Check(status.ScrollableHeight > 0 && host.ActualHeight >= 140, "long blockers have one bounded scroll without taking the task viewport");
                status.ScrollToEnd(); window.UpdateLayout(); Check(status.VerticalOffset > 0, "all blocker lines remain reachable");
                CaptureInternal(evidence, "classic-long-blockers-800", client, lines); probe.Project = null; await vm.RefreshAsync();
            }
            foreach (var state in new[] { StageState.Unknown, StageState.Rejected, StageState.Conflict })
            {
                probe.Project = s => HierarchyState(s, state);
                for (var i = 0; i < 100 && vm.Current?.Wcs?.State != state; i++) { await vm.RefreshAsync(); await Task.Delay(20); }
                Navigate("工作台"); await Task.Delay(50); window.UpdateLayout();
                Check(vm.Current?.Wcs?.State == state && vm.CurrentSlot == "未取得", ui + " " + state + " does not reuse the previously accepted slot");
                if (vm is SupplyStationViewModel station)
                {
                    var left = station.Left; var right = station.Right;
                    Check(left.Status.Contains(state == StageState.Rejected ? "拒绝" : "核对") && left.Presentation.Foreground == (state == StageState.Rejected ? "#B52F38" : "#895509"), state + " result text and semantic color agree with operation facts");
                    Check(left.AttentionReason.Contains("合成") && left.NextStep.Contains(state == StageState.Rejected ? "拒绝" : "请勿重复") && !left.CanContinue, state + " explains reason and next step without permitting resend");
                    Check(!station.Snapshot!.Paused && right.CanAdmit && !right.HasUncertainOriginal && !right.CurrentTaskAvailable, state + " remains local to left; right idle position remains ready");
                    Check(left.CanAdmit == (state == StageState.Rejected), state + " admission follows application position availability");
                }
                CaptureInternal(evidence, ui + "-" + state + "-800", client, lines);
                if (state == StageState.Unknown)
                {
                    Navigate("日志查询"); for (var i = 0; i < 100 && host.Content is not SoftwareLogControl; i++) await Task.Delay(20);
                    window.UpdateLayout();
                    Check(((TextBlock)window.FindName("ActiveBlockingReasons")).IsVisible, ui + " unresolved original result remains visible while searching logs");
                    CaptureInternal(evidence, ui + "-logs-unknown-800", client, lines);
                }
            }
            probe.Project = null; await vm.RefreshAsync();
            await VerifyHierarchy02(window, vm, (HierarchyProjectionProbe)probe, Navigate, evidence, lines);
            await runtime.ExitManagementModeAsync(); await vm.RefreshAsync(); await Task.Delay(650);
            Check(host.Content is not MaintenancePage && host.Content is not SoftwareLogControl && ReferenceEquals(draft, vm.StructuredPolicy) && draft.TimeoutMs == "28000" && vm.Snapshot!.CurrentOperator!.SessionId == session, ui + " revocation retains draft and independent operator while closing protected content");
            bool denied = false; try { await vm.SaveStructuredConfigurationAsync(); } catch (UnauthorizedAccessException) { denied = true; }
            Check(denied, ui + " direct save remains denied after revocation");
            window.Close(); for (var i = 0; i < 100 && window.IsVisible; i++) await Task.Delay(20);
            Check(!window.IsVisible, ui + " window drains and closes");
        }
        lines.Add("Real Windows HWND/Dispatcher rendering at actual DPI; isolated synthetic ports/data. Physical input, other OS DPI, target IPC, installation, SDK/RCS/site NOT_RUN.");
    }
}

public class HierarchyProjectionProbe : ProjectionProbe
{
    public Func<BusinessCommand, Task<BusinessReceipt>>? Command;
    public bool FailSnapshots;
    protected override object? Invoke(MethodInfo? method, object?[]? arguments)
    {
        if (method!.Name == nameof(IParcelApplication.SnapshotAsync) && FailSnapshots) return Task.FromException<ParcelSnapshot>(new IOException("合成状态读取失败"));
        if (method.Name == nameof(IBusinessApplication.CommandAsync) && Command is not null) return Command((BusinessCommand)arguments![0]!);
        if (method.Name == nameof(IWorkstationAdministration.QueryLogsAsync))
        {
            var rows = Enumerable.Range(1, 12).Select(i => new SoftwareLogEntry(new("synthetic", i, "fixture", "synthetic.jsonl"),
                "合成运行日志", new DateTimeOffset(2026, 10, 24, 8, 0, i, TimeSpan.FromHours(8)), "信息", "合成记录 " + i + "：运行状态已读取。")).ToArray();
            return Task.FromResult(new SoftwareLogPage("synthetic-fixed-selection", rows, false, []));
        }
        return base.Invoke(method, arguments);
    }
}

using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Platform;

static partial class Program
{
    static async Task VerifyWorkstationStatusLogs(string root, string evidence, List<string> lines)
    {
        void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); lines.Add("PASS WSL01 " + message); }
        async Task Flush(Window window) { await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout(); }
        var folder = Path.Combine(evidence, "config"); Directory.CreateDirectory(folder);
        var config = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(root, "config/sportmaster/business.json")));
        var operatorBinding = new CapabilityBinding("login", "reference-login", "1.0.0", "operator", "reference://operator", "PARCEL-REFERENCE-v1", new() { ["subjects"] = "operator" });
        config = config with { CapabilityInstances = ParcelBindings.All(config).Append(operatorBinding).ToArray() };
        ExtensionStore.AtomicWrite(Path.Combine(folder, "business.json"), config);
        var launch = new BusinessLaunch(folder, Path.Combine(evidence, "data"), null, null);
        await using var runtime = new ParcelRuntime(launch, new SportmasterFlow(), new CommercialProductization.SyntheticFactory(launch)) { RuntimeIdentityPreflight = () => { } };
        await runtime.StartAsync(default);
        await ManagementModeFixture.Authorize(runtime);
        Check((await runtime.ManagementModeStatusAsync()).Authorized, "setup-like authorization exists before runtime entry");
        await WorkstationEntry.BeginRuntimeAsync(runtime);
        Check(!(await runtime.ManagementModeStatusAsync()).Authorized, "the actual desktop runtime-entry helper clears setup authorization");
        var app = DispatchProxy.Create<IHistoryProjectionFixture, HierarchyProjectionProbe>(); var probe = (HierarchyProjectionProbe)(object)app; probe.Inner = runtime;
        var window = new SupplyStationWindow(app, "sportmaster-maintenance", "standard") { ConfirmDiscardDrafts = _ => true }; window.Show();
        var vm = window.ViewModel; var client = (Grid)window.FindName("ClientArea");
        async Task RefreshUntil(Func<bool> predicate)
        {
            for (int i = 0; i < 120; i++) { await vm.RefreshAsync(); await Flush(window); if (predicate()) return; await Task.Delay(20); }
            throw new TimeoutException("Native presentation did not converge");
        }
        await RefreshUntil(() => vm.Fresh);
        Check(!vm.IsManagementMode && vm.SelectedPage == "workstation", "restricted launch view returns to work in ordinary mode");
        Check(!Descendants(window).OfType<Button>().Any(b => b.IsVisible && b.Content is "参数设置" or "日志查询"), "ordinary navigation hides parameter and log entries");
        foreach (var size in new[] { (1366d, 768d), (1092d, 614d), (800d, 420d) })
        {
            await SetClient(window, size.Item1, size.Item2); await Flush(window);
            Check(((Border)window.FindName("RcsConnection")).IsVisible, "shared RCS status stays visible at " + size.Item1);
            foreach (var position in Descendants(window).OfType<SupplyStationPosition>())
            {
                var badge = (Border)position.FindName("CameraConnection"); var scan = (Grid)position.FindName("ScanInformation");
                Check(badge.IsVisible && Inside(badge, client) && scan.IsVisible, "camera badge and empty scan group remain available at " + size.Item1);
            }
            CaptureInternal(evidence, "work-empty-" + (int)size.Item1, client, lines);
        }
        await SetClient(window, 1366, 768);
        // Projection-only network states: no hardware, remote calls or production qualification.
        ParcelSnapshot Connections(ParcelSnapshot s, string state)
        {
            var now = DateTimeOffset.UtcNow; var effective = s.Configuration.Effective;
            effective = effective with { Mode = RunMode.Production, Wcs = effective.Wcs! with { Contract = "SPORTMASTER-RCS-SORTER-v1" }, DeferredConnections = state == "deferred" ? [effective.Wcs!.InstanceId, .. s.Positions!.Select(p => p.ReaderDeviceId)] : null };
            return s with { Configuration = s.Configuration with { Effective = effective },
                Devices = s.Devices!.Select((d, i) => d with { Health = state == "offline" && i == 0 ? DeviceHealth.Disconnected : DeviceHealth.Ready, ConfirmedAt = now.AddSeconds(-1), ValidUntil = state == "expired" ? now.AddMilliseconds(-1) : now.AddMinutes(1), Source = "TCP:client-LF:synthetic", Invalidity = null }).ToArray(),
                Health = [new(effective.Wcs!.InstanceId, state != "offline", now.AddSeconds(-1), now.AddMinutes(1), state == "local" ? "local lifecycle only" : "RCS heartbeat HTTP 200")] };
        }
        foreach (var state in new[] { "online", "offline", "expired", "deferred", "local" })
        {
            probe.Project = s => Connections(s, state);
            await RefreshUntil(() => state switch { "online" => vm.Left.DevicePresentation.Text == "相机：在线", "offline" => vm.Left.DevicePresentation.Text == "相机：离线", "expired" => vm.Left.DevicePresentation.Text.Contains("待确认"), "deferred" => vm.Left.DevicePresentation.Text.Contains("待配置"), _ => vm.RcsPresentation.Text.Contains("待确认") });
            Check(state switch { "online" => vm.RcsPresentation.Foreground == "#147450", "offline" => vm.RcsPresentation.Foreground == "#B52F38" && vm.Right.DevicePresentation.Text == "相机：在线", "deferred" => vm.RcsPresentation.Text.Contains("待配置"), "local" => !vm.RcsPresentation.Text.Contains("在线"), _ => !vm.Left.DevicePresentation.Text.Contains("在线") }, "connection evidence distinguishes " + state + " and keeps sides independent");
            CaptureInternal(evidence, "connection-" + state, client, lines);
        }
        probe.Project = _ => throw new IOException("合成状态读取中断");
        vm.MarkReadFailure("合成状态读取中断"); await Flush(window);
        Check(!vm.Left.DevicePresentation.Text.Contains("在线") && !vm.RcsPresentation.Text.Contains("在线"), "stale snapshot revokes camera and RCS online claims");
        probe.Project = null; await RefreshUntil(() => vm.Fresh);
        await ManagementModeFixture.Authorize(runtime); await vm.RefreshManagementModeAsync();
        Click(window, "参数设置");
        for (int i = 0; i < 100 && !vm.HasLoadedConfiguration; i++) await Task.Delay(30);
        await vm.EnsureMaintenanceLoadedAsync(); await Flush(window);
        var editor = Descendants(window).OfType<ParameterSettingsControl>().Single();
        Check(!editor.ShowPage(3, "维护认证") && !editor.ShowPage(3, "身份认证"), "daily role-based legacy maintenance-only component is absent from the real directory");
        var model = editor.Model!; var login = model.Components.Single(c => c.Provider?.Implementation == "reference-login");
        var loginRole = model.ComponentRoles.Single(r => r.Role == "登录"); var originalLogin = loginRole.Selection;
        loginRole.Selection = loginRole.Choices.Single(c => ReferenceEquals(c.Capability, login));
        Check(model.ComponentLabel(login) == "操作员登录" && !model.ComponentRoles.Any(r => r.Role == "维护认证"),
            "current operator authentication retains its own purpose and Chinese label without an obsolete management role");
        loginRole.Selection = originalLogin;
        var draft = vm.StructuredPolicy!; draft.TimeoutMs = "29000";
        await runtime.ExitManagementModeAsync(); await vm.RefreshManagementModeAsync(); await Flush(window);
        Check(vm.SelectedPage == "workstation" && ReferenceEquals(draft, vm.StructuredPolicy) && draft.TimeoutMs == "29000" && !vm.HasManagementNotice && !vm.Message.Contains("管理授权已结束"), "mode exit protects the exact draft and removes the routine banner");
        Check(ChineseInterfaceText.Notice("Global software pause") == "供包已暂停" && !ChineseInterfaceText.Notice("Production NotReady: exact deployment qualification missing/expired").Contains("Production"), "pause and deployment reasons use Chinese display copy");
        Check(vm.HistoryPositionChoices.Where(x => x.Id is "left" or "right").All(x => !x.Label.Contains(x.Id)), "history labels hide internal left/right values");
        foreach (var ui in new[] { "classic", "supply-station" })
        {
            Window guarded = ui == "classic" ? CreateSharedShellForVerification(runtime, "sportmaster-maintenance") : Presentation.Create(runtime, "sportmaster-maintenance", "standard", ui); guarded.Show(); await Task.Delay(200);
            Check(ui == "classic" ? ((ShellWindow)guarded).SelectedWorkspace == "work" : ((SupplyStationWindow)guarded).ViewModel.SelectedPage == "workstation", ui + " initial protected page is fenced");
            guarded.Close(); await Task.Delay(100);
        }
        await ManagementModeFixture.Authorize(runtime); await vm.RefreshManagementModeAsync();
        Click(window, "返回作业"); // Reauthorization may resume the protected draft.
        runtime.EmitReferenceAt("left", "wsl-left", "000123456", "wsl-event-left"); runtime.EmitReferenceAt("right", "wsl-right", "000987654", "wsl-event-right");
        await RefreshUntil(() => vm.Left.SlotAvailable && vm.Right.SlotAvailable);
        var request = vm.Left.RequestId;
        Check(vm.Left.Barcode == "000123456" && vm.Right.Barcode == "000987654", "real synthetic Application chain preserves independent barcode/task results");
        CaptureInternal(evidence, "work-results-1366", client, lines);
        await SetClient(window, 800, 420); await Flush(window);
        foreach (var position in Descendants(window).OfType<SupplyStationPosition>())
        {
            var slot = (TextBox)position.FindName("CurrentSlot"); var body = (ScrollViewer)position.FindName("PositionBody");
            Check(slot.IsVisible && slot.TranslatePoint(new Point(), body).Y + slot.ActualHeight <= body.ActualHeight + 1,
                "current destination remains in the compact scroll viewport without scrolling");
        }
        CaptureInternal(evidence, "work-results-800", client, lines);
        window.Close(); await Task.Delay(250);
        await VerifyTerminalLogs(runtime, evidence, lines);
        Check((await runtime.SnapshotAsync()).Operations.Any(x => x.Wcs?.Request.Id == request), "UI, mode and log changes preserve the original request");
        lines.Add("Native HWND/Dispatcher at actual Windows DPI; synthetic identity and ports. Real camera/RCS, installed product and site acceptance NOT_RUN.");
    }

    static async Task VerifyTerminalLogs(IWorkstationAdministration runtime, string evidence, List<string> lines)
    {
        void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); lines.Add("PASS WSL01-LOG " + message); }
        var service = DispatchProxy.Create<IWorkstationAdministration, LogUiProbe>(); var probe = (LogUiProbe)(object)service; probe.Inner = runtime;
        int queries = 0;
        var rows = Enumerable.Range(1, 201).Select(i => new SoftwareLogEntry(new("synthetic", i, "hash-" + i, "synthetic.jsonl"), "合成日志", DateTimeOffset.Now, "信息", "合成日志 " + i + "\n第二行详情")).ToArray();
        probe.Query = (_, offset, _) => { queries++; return Task.FromResult(new SoftwareLogPage("same-selection", rows.Skip(offset).Take(200).ToArray(), offset == 0, [])); };
        var control = new SoftwareLogControl(service); var client = new Grid(); client.Children.Add(control);
        var window = new Window { Content = client, Width = 1000, Height = 700 };
        NameScope.SetNameScope(window, new NameScope()); window.RegisterName("ClientArea", client); window.Show();
        var mode = Descendants(control).OfType<ComboBox>().Single(c => AutomationProperties.GetName(c) == "日志显示方式");
        var terminal = Descendants(control).OfType<TextBox>().Single(t => AutomationProperties.GetAutomationId(t) == "TerminalLogView");
        var table = Descendants(control).OfType<DataGrid>().Single();
        async Task Settle() { for (int i = 0; i < 200 && !Descendants(control).OfType<Button>().Single(b => Equals(b.Content, "查询")).IsEnabled; i++) await Task.Delay(20); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout(); }
        Click(window, "查询"); await Settle();
        Check(table.Items.Count == 200 && queries == 1, "initial list uses the bounded fixed query page");
        table.SelectedIndex = 0; await Task.Delay(50); mode.SelectedIndex = 1; await Settle();
        Check(terminal.IsVisible && !table.IsVisible && terminal.Text.Contains("合成日志 1\n第二行详情") && terminal.Text.Contains("合成日志 200") && queries == 1, "terminal switch shows multiline originals from the exact existing page without querying again");
        terminal.Focus(); terminal.Select(0, 12); var selected = terminal.SelectedText;
        mode.SelectedIndex = 0; mode.SelectedIndex = 1; await Settle();
        Check(terminal.IsReadOnly && terminal.SelectedText == selected && table.SelectedIndex == 0, "switching retains native text selection and selected list row");
        CaptureInternal(evidence, "logs-terminal", window, lines);
        foreach (var size in new[] { (1000d, 700d), (800d, 420d) })
        {
            await SetClient(window, size.Item1, size.Item2); await Settle();
            Check(terminal.ActualHeight >= 80 && Inside(terminal, client) && mode.IsVisible, "terminal viewport and selector remain reachable at " + size.Item1);
            CaptureInternal(evidence, "logs-terminal-" + (int)size.Item1, window, lines);
        }
        Click(window, "下一页"); await Settle();
        Check(terminal.Text.Contains("合成日志 201") && !terminal.Text.Contains("合成日志 200") && table.Items.Count == 1, "terminal pagination replaces both views with the same next page");
        var huge = rows[0] with { Text = new string('长', 100000) };
        probe.Query = (_, _, _) => Task.FromResult(new SoftwareLogPage("bounded", [huge], false, []));
        Click(window, "查询"); await Settle();
        Check(terminal.Text.Length <= 65536 && terminal.Text.Contains("呈现预算") && huge.Text.Length == 100000,
            "terminal bounds rendering and discloses full export while preserving the complete source record");
        var delayed = new TaskCompletionSource<SoftwareLogPage>(TaskCreationOptions.RunContinuationsAsynchronously);
        probe.Query = (_, _, _) => delayed.Task; Click(window, "查询"); await Task.Delay(20); control.Revoke();
        delayed.SetResult(new("revoked", [rows[0]], false, [])); await Settle();
        Check(terminal.Text.Length == 0 && table.Items.Count == 0, "late query cannot republish either view after revocation");
        window.Close();
    }
}

using System.IO;
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
    static async Task VerifyVisualProductization(string root, string evidence, List<string> lines, bool baseline, bool interactive)
    {
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); lines.Add("PASS " + message); }
        lines.Add("SYNTHETIC identity/ports and explicitly projected abnormal facts. Real WPF/Dispatcher/HWND. No policy provisioning, device or network actions.");
        var configRoot = Path.Combine(evidence, "config"); Directory.CreateDirectory(configRoot);
        var config = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(root, "config/sportmaster/business.json")));
        File.WriteAllText(Path.Combine(configRoot, "business.json"), Wire.Encode(config));
        var launch = new BusinessLaunch(configRoot, Path.Combine(evidence, "data"), null, null);
        var factory = new VisualFactory(launch, !baseline);
        await using var runtime = new ParcelRuntime(launch, new SportmasterFlow(), factory) { RuntimeIdentityPreflight = () => { } };
        await runtime.StartAsync(default);
        var application = DispatchProxy.Create<IParcelApplication, ProjectionProbe>();
        var probe = (ProjectionProbe)(object)application; probe.Inner = runtime;
        var window = (SupplyStationWindow)Presentation.Create(application, null, "standard");
        window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Check(Ipc.Desktop.DesktopLaunchOptions.Parse([], "sportmaster").Ui == "supply-station" && Ipc.Desktop.DesktopLaunchOptions.Parse(["--ui", "classic"], "sportmaster").Ui == "classic" &&
            Ipc.Desktop.DesktopLaunchOptions.Parse([], "direct-plc").Ui == "classic" && Ipc.Desktop.DesktopLaunchOptions.Parse([], "wcs-to-plc").Ui == "classic", "actual desktop parser and presentation default use product-specific routing with explicit classic compatibility");
        window.Show(); await Task.Delay(650); await window.ViewModel.RefreshAsync();
        var vm = window.ViewModel;
        if (!baseline)
        {
            await vm.PauseAsync();
            Check(vm.OperatorFeedback.Contains("操作已提交") && vm.Message.StartsWith("Persisted · ") && vm.Snapshot!.Paused,
                "operator feedback is Chinese while original command receipt and authoritative pause fact remain available");
            await vm.RunAsync(() => vm.CommandAsync("Resume"), "恢复新入场");
        }
        if (!baseline && Environment.GetEnvironmentVariable("IPC_FROZEN_DESKTOP") is { } desktopPath)
        {
            var desktopAssembly = System.Reflection.Assembly.LoadFrom(desktopPath);
            var type = desktopAssembly.GetType("Ipc.Desktop.StartupRepairWindow", true)!;
            var offline = launch with { DataRoot = Path.Combine(evidence, "offline-data") };
            var first = (Window)Activator.CreateInstance(type, [offline, null, true])!; first.Show();
            for (var i = 0; i < 100 && Descendants(first).OfType<TabItem>().Count() < 5; i++) await Task.Delay(30);
            for (var i = 0; i < 100 && !Descendants(first).OfType<Button>().Any(b => Equals(b.Content, "读取所选配置") && b.IsEnabled); i++) await Task.Delay(30);
            first.UpdateLayout();
            Check(Descendants(first).OfType<TabItem>().Count() == 5 && Descendants(first).OfType<Button>().Any(b => Equals(b.Content, "保存首配待生效…")), "frozen desktop first configuration exposes task steps and existing guarded save");
            Check(!Directory.Exists(offline.DataRoot) || !Directory.GetFiles(offline.DataRoot, "*.db", SearchOption.AllDirectories).Any(), "offline first configuration never starts the Application or creates a business database");
            Capture("first-configuration", first);
            var policy = Descendants(first).OfType<TabItem>().Single(t => Equals(t.Header, "3 运行策略")); policy.IsSelected = true; first.UpdateLayout();
            var timeout = Descendants(first).OfType<TextBox>().Single(b => AutomationProperties.GetName(b).StartsWith("调用超时")); timeout.Text = "1601";
            Check(timeout.IsEnabled && timeout.Focus(), "first-configuration native form becomes enabled and accepts keyboard focus after the read completes");
            var review = Descendants(first).OfType<TabItem>().Single(t => Equals(t.Header, "4 检查与启动")); review.IsSelected = true; first.UpdateLayout(); policy.IsSelected = true; first.UpdateLayout();
            Check(timeout.Text == "1601" && Descendants(first).OfType<TextBox>().Contains(timeout), "first-configuration step navigation retains the same native unsaved form controls");
            Capture("first-configuration-policy", first);
            type.GetProperty("ConfirmDiscardInputs", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(first, new Func<bool>(() => false)); first.Close();
            Check(first.IsVisible && timeout.Text == "1601", "integrated first-configuration guard refuses to discard an unsaved native draft");
            timeout.Text = config.TimeoutMs.ToString(); first.Close();
        }
        // Same live WPF fixture captures the before/after state; no images are generated.
        await Fit(1366, 768); Capture("waiting-1366");
        await vm.EmitPositionAsync(vm.Left); await vm.EmitPositionAsync(vm.Right);
        if (!baseline)
        {
            for (var i = 0; i < 100 && vm.Right.Operation?.State != ParcelState.SlotResolved; i++) { await vm.RefreshAsync(); await Task.Delay(20); }
            Check(vm.Right.Operation?.State == ParcelState.SlotResolved && vm.Left.Operation?.State == ParcelState.ResolvingWcs, "delayed left request does not block right result or change its owner");
            Capture("left-processing-right-valid-1366"); factory.ReleaseLeft.TrySetResult();
        }
        for (var i = 0; i < 100; i++) { await vm.RefreshAsync(); if (vm.Left.Operation?.State == ParcelState.SlotResolved && vm.Right.Operation?.State == ParcelState.SlotResolved) break; await Task.Delay(40); }
        Check(vm.Left.Operation?.Id != vm.Right.Operation?.Id && vm.Left.Slot != "未取得" && vm.Right.Slot != "未取得", "left and right resolve independent operations on the shared live Application");
        Capture("normal-1366");
        var classic = (ShellWindow)Presentation.Create(application, null, "standard", "classic");
        classic.Show(); await Task.Delay(250);
        var classicVm = (ParcelViewModel)classic.DataContext; await classicVm.RefreshAsync();
        Check(!((Expander)classic.FindName("StationDiagnostics")).IsExpanded &&
            Descendants(classic).OfType<Expander>().Where(e => Equals(e.Header, "观察、原作业与原因详情")).All(e => !e.IsExpanded),
            "classic ordinary operation starts with technical diagnostics and per-position originals disclosed on demand");
        classicVm.Message = "Persisted · SYNTHETIC full original receipt with request owner";
        Check(((TextBlock)classic.FindName("OperationFeedback")).Text.Contains("操作已提交") && classicVm.Message.Contains("SYNTHETIC full original"),
            "classic and default share the friendly receipt projection while preserving the complete technical receipt");
        probe.Project = s => s with { State = "PartiallyReady" }; await classicVm.RefreshAsync();
        Check(classicVm.Status == "部分投递位可作业", "partial readiness is translated without changing the authoritative state or permissions");
        probe.Project = null; Capture("classic-1366", classic); classic.Close();
        await Fit(1100, 650); Capture("normal-1100");
        if (!baseline)
        {
            vm.Left.NewParcel(); vm.Right.NewParcel(); await vm.EmitPositionAsync(vm.Left); await vm.EmitPositionAsync(vm.Right);
            for (var i = 0; i < 100; i++) { await vm.RefreshAsync(); if (vm.Left.Operation?.State == ParcelState.WcsUnknown && vm.Right.Operation?.State == ParcelState.SlotResolved) break; await Task.Delay(20); }
            var originalRequest = vm.Left.Operation?.Wcs?.Request.Id; var rightOwner = vm.Right.Operation?.Id;
            Check(originalRequest is not null && vm.Left.Slot == "未取得" && vm.Right.Operation?.State == ParcelState.SlotResolved, "real lost left reply preserves Unknown while right continues independently");
            await vm.ReconcilePositionAsync(vm.Left); await vm.RefreshAsync();
            Check(vm.Left.Operation?.Wcs?.Request.Id == originalRequest && vm.Left.Operation?.State == ParcelState.SlotResolved && vm.Right.Operation?.Id == rightOwner && factory.Resolves == 4 && factory.Queries == 1, "query reconciles the exact original request once without another send or replacing the right owner");
            var positions = Descendants(window).OfType<SupplyStationPosition>().ToArray();
            var left = positions.Single(p => ReferenceEquals(p.DataContext, vm.Left));
            var details = Descendants(left).OfType<Expander>().First(); details.IsExpanded = true;
            var code = (TextBox)left.FindName("CurrentBarcode");
            code.Focus(); code.Select(1, 4); var text = code.SelectedText;
            var scroll = (ScrollViewer)left.FindName("PositionDetailsScroll"); scroll.ScrollToVerticalOffset(20); await System.Windows.Threading.Dispatcher.Yield(); var offsetBefore = scroll.VerticalOffset;
            vm.Left.InspectionReason = "SYNTHETIC unsaved inspection";
            for (var i = 0; i < 8; i++) { await vm.RefreshDisplayAsync(); await Task.Delay(80); }
            Check(code.IsKeyboardFocused && code.SelectedText == text && details.IsExpanded && scroll.VerticalOffset == offsetBefore && vm.Left.InspectionReason == "SYNTHETIC unsaved inspection", "repeated native snapshots retain focus, barcode selection, disclosure, scroll and unsaved inspection");
            Click(window, "作业记录"); await Task.Delay(250);
            await vm.OpenHistoryAsync();
            var history = (HistoryPage)((ContentControl)window.FindName("PageHost")).Content;
            var filter = (Expander)history.FindName("HistoryFilters"); filter.IsExpanded = true; window.UpdateLayout();
            var dateInput = Descendants(history).OfType<DateTimeInput>().First(); dateInput.Timestamp = "2026-10-19T08:09:10+05:30";
            var clock = Descendants(dateInput).OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "时间");
            var offset = Descendants(dateInput).OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "UTC偏移");
            Check(clock.Text == "08:09:10" && offset.Text == "+05:30", "native calendar editor preserves the original wall time and explicit nonlocal UTC offset");
            clock.Text = "25:99:00";
            Check(dateInput.Timestamp.Contains("25:99:00") && vm.HistoryFrom.Contains("25:99:00"), "invalid native time propagates as invalid input instead of retaining an older query time");
            Click(history, "不限时间"); Check(vm.HistoryFrom.Length == 0 && vm.HistoryToExclusive.Length == 0, "native clear-time action removes both exact time boundaries");
            Capture("history-filters-1100"); filter.IsExpanded = false;
            var rows = (DataGrid)history.FindName("HistoryRows");
            rows.SelectedItem = vm.HistoryItems.FirstOrDefault();
            var selected = vm.Selected?.Id;
            for (var i = 0; i < 4; i++) await vm.RefreshAsync();
            Check(vm.Selected?.Id == selected && selected is not null, "native history row and command owner survive live snapshot refresh");
            Capture("history-1100");
            var actions = (WrapPanel)history.FindName("HistoryActions");
            var inspection = (Expander)history.FindName("HistoryInspection");
            var historyScroll = (ScrollViewer)history.FindName("HistoryDetailsScroll");
            vm.InspectionReason = string.Concat(Enumerable.Repeat("SYNTHETIC 原实体检查说明 ", 40));
            await SetClient(window, 800, 420); window.UpdateLayout();
            var before = actions.TranslatePoint(new Point(), history);
            inspection.IsExpanded = true; historyScroll.ScrollToBottom(); window.UpdateLayout();
            var after = actions.TranslatePoint(new Point(), history);
            Check(before == after && after.Y >= 0 && after.Y + actions.ActualHeight <= history.ActualHeight &&
                Descendants(actions).OfType<Button>().All(b => b.ActualHeight >= 44),
                "history recovery actions remain fixed and fully visible at minimum client size while long inspection details scroll");
            Capture("history-expanded-minimum");
            inspection.IsExpanded = false; vm.InspectionReason = ""; await Fit(1100, 650);
            Click(window, "设备与维护"); await Task.Delay(200); vm.ConfigurationReason = "SYNTHETIC unsaved settings";
            Capture("maintenance-1100");
            Click(window, "工作台"); await Task.Delay(100);
            Check(details.IsExpanded && vm.ConfigurationReason == "SYNTHETIC unsaved settings" && vm.Left.InspectionReason == "SYNTHETIC unsaved inspection", "page navigation preserves existing controls and independent drafts");
            details.IsExpanded = false;
            Click(window, "辅助窗口"); await Task.Delay(350);
            var child = window.OwnedWindows.OfType<SupplyStationWindow>().Single();
            Check(ReferenceEquals(child.ViewModel.Application, vm.Application) && child.ViewModel.Snapshot?.RuntimeId == vm.Snapshot?.RuntimeId, "auxiliary native window shares the same Application and device lifetime");
            child.Close(); await Task.Delay(100);
            var support = new ProductSupportWindow(runtime, vm.Left.Operation?.Id) { Owner = window }; support.Show(); await Task.Delay(120);
            Capture("support-help", support); ((TabControl)support.Content).SelectedIndex = 1; support.UpdateLayout(); Capture("support-preview", support); support.Close();
        }
        var original = (await runtime.SnapshotAsync()).Operations;
        probe.Project = s => s with { Operations = s.Operations.Select(o => o.PositionId == "left" ? o with { State = ParcelState.WcsUnknown, Slot = null, Wcs = o.Wcs! with { State = StageState.Unknown, Failure = "SYNTHETIC 回执未确认；保持原请求，只查询核对。" } } : o with { State = ParcelState.WcsRejected, Slot = null, Wcs = o.Wcs! with { State = StageState.Rejected, Failure = "SYNTHETIC 上游明确拒绝，检查原回执原因。" } }).ToArray() };
        await vm.RefreshAsync(); Capture("unknown-rejected-1100");
        Check(vm.Left.Slot == "未取得" && vm.Right.Slot == "未取得" && vm.Left.Operation?.Wcs?.Request.Id == original.Single(o => o.Id == vm.Left.Operation?.Id).Wcs?.Request.Id, "Unknown/rejection never reuse a slot and retain the original request owner");
        probe.Project = s => s with { Operations = s.Operations.Select(o => o with { ParcelCode = new string('0', 110) + "SYNTHETIC-LONG-CODE", Slot = "012345678901234567890123456789" }).ToArray() };
        await vm.RefreshAsync(); Capture("long-content-1100");
        vm.MarkReadFailure("SYNTHETIC 状态读取暂不可用"); Capture("stale-1100");
        Check(vm.Left.Slot.Contains("失效") && !vm.Left.CanContinue && !vm.CanResume, "failed snapshot invalidates displayed slot and new commands without clearing original responsibility");
        probe.Project = null; await vm.RefreshAsync();
        await Fit(900, 520); Capture("normal-900");
        if (!baseline) { await SetClient(window, 800, 420); Capture("minimum-client-800x420"); }
        var client = (Grid)window.FindName("ClientArea");
        foreach (var position in Descendants(window).OfType<SupplyStationPosition>())
        {
            var actions = (Border)position.FindName("FixedActions"); var rect = actions.TransformToAncestor(client).TransformBounds(new Rect(actions.RenderSize));
            Check(actions.ActualHeight >= 44 && rect.Bottom <= client.ActualHeight && rect.Top >= 0, "compact recovery actions remain in actual client area");
            var barcode = (TextBox)position.FindName("CurrentBarcode"); var slot = (TextBox)position.FindName("CurrentSlot");
            var actionBounds = actions.TransformToAncestor(position).TransformBounds(new Rect(actions.RenderSize));
            lines.Add($"MINIMUM {((SupplyStationPositionViewModel)position.DataContext).Heading} barcode={barcode.ActualHeight} slot={slot.ActualHeight} actionsBottom={actionBounds.Bottom} positionHeight={position.ActualHeight}");
            Check(barcode.ActualHeight >= 24 && slot.ActualHeight >= 36 && actionBounds.Bottom <= position.ActualHeight,
                "minimum native client keeps barcode, slot and full action buttons visible inside each position");
        }
        await Fit(1366, 768);
        if (interactive)
        {
            lines.Add("INTERACTIVE preview ready; application timers remain active for human/native input inspection.");
            File.WriteAllText(Path.Combine(root, ".artifacts/visual-preview.txt"), evidence);
            for (var i = 0; i < 480 && window.IsVisible; i++) await Task.Delay(1000);
        }
        window.ConfirmDiscardDrafts = _ => true; window.Close(); await Task.Delay(200);
        lines.Add("NOT_RUN physical touch, hardware, mixed-monitor DPI, UAC installation and site acceptance.");
        async Task Fit(double width, double height)
        { window.WindowState = WindowState.Normal; window.Width = width; window.Height = height; window.UpdateLayout(); await Task.Delay(220); lines.Add($"SIZE window={window.ActualWidth}x{window.ActualHeight} client={((Grid)window.FindName("ClientArea")).ActualWidth}x{((Grid)window.FindName("ClientArea")).ActualHeight} DPI={VisualTreeHelper.GetDpi(window).DpiScaleX}"); }
        void Capture(string name, Window? target = null)
        {
            target ??= window; target.UpdateLayout(); var dpi = VisualTreeHelper.GetDpi(target);
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(target.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(target.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32); bitmap.Render(target);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(Path.Combine(evidence, (baseline ? "before-" : "after-") + name + ".png")); png.Save(file);
        }
    }

    sealed class VisualFactory(BusinessLaunch launch, bool exerciseAsync) : IParcelCapabilityFactory
    {
        readonly CommercialProductization.SyntheticFactory inner = new(launch);
        public readonly TaskCompletionSource ReleaseLeft = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Resolves, Queries;
        public ParcelConfiguration Normalize(ParcelConfiguration c) => inner.Normalize(c);
        public void Validate(ParcelConfiguration c) => inner.Validate(c);
        public void ValidateStructure(ParcelConfiguration c) => inner.ValidateStructure(c);
        public CapabilityCatalog Catalog() => inner.Catalog();
        public ParcelPorts Create(ParcelConfiguration c) { var ports = inner.Create(c); return ports with { Wcs = new VisualWcs(ports.Wcs!, this, exerciseAsync) }; }
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
    sealed class VisualWcs(IWcsPort inner, VisualFactory owner, bool exerciseAsync) : IWcsPort
    {
        int leftCalls;
        public string InstanceId => inner.InstanceId;
        public Task StartAsync(CancellationToken t) => inner.StartAsync(t);
        public Task<CapabilityHealth> HealthAsync(CancellationToken t) => inner.HealthAsync(t);
        public Task ReconnectAsync(CancellationToken t) => inner.ReconnectAsync(t);
        public async Task<StageReply> ResolveSlotAsync(FrozenRequest request, CancellationToken t)
        {
            Interlocked.Increment(ref owner.Resolves);
            var left = Wire.Decode<ParcelPayload>(request.Payload).PositionId == "left";
            var call = left ? Interlocked.Increment(ref leftCalls) : 0;
            if (exerciseAsync && left && call == 1) await owner.ReleaseLeft.Task.WaitAsync(t);
            var result = await inner.ResolveSlotAsync(request, t);
            if (exerciseAsync && left && call == 2) throw new IOException("SYNTHETIC reply lost after peer committed the original request");
            return result;
        }
        public Task<StageReply?> QueryAsync(FrozenRequest r, CancellationToken t) { Interlocked.Increment(ref owner.Queries); return inner.QueryAsync(r, t); }
        public Task StopAsync(CancellationToken t) => inner.StopAsync(t);
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}

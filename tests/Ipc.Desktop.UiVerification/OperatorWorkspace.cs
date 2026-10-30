using System.Reflection;
using System.Windows;
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
    static async Task VerifyOperatorWorkspace(string root, string evidence, List<string> lines)
    {
        void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); lines.Add("PASS OPERATOR01 " + message); }
        var folder = Path.Combine(evidence, "operator-config"); Directory.CreateDirectory(folder);
        var config = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(root, "tests/Ipc.Application.Verification/sportmaster.synthetic.json")));
        ExtensionStore.AtomicWrite(Path.Combine(folder, "business.json"), config);
        var launch = new BusinessLaunch(folder, Path.Combine(evidence, "operator-data"), null, null);
        await using var runtime = new ParcelRuntime(launch, new SportmasterFlow(), new CommercialProductization.SyntheticFactory(launch)) { RuntimeIdentityPreflight = () => { } };
        await runtime.StartAsync(default); await ManagementModeFixture.Authorize(runtime);
        var app = DispatchProxy.Create<IHistoryProjectionFixture, HierarchyProjectionProbe>();
        var probe = (HierarchyProjectionProbe)(object)app; probe.Inner = runtime;
        var window = new SupplyStationWindow(app, null, "standard") { ConfirmDiscardDrafts = _ => true, Title = "供包界面 · 合成场景验证" };
        window.Show(); var vm = window.ViewModel; var client = (Grid)window.FindName("ClientArea");
        async Task Flush() { await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout(); }
        async Task Until(Func<bool> ready)
        {
            for (int i = 0; i < 150; i++) { await vm.RefreshAsync(); await Flush(); if (ready()) return; await Task.Delay(20); }
            throw new TimeoutException("Operator presentation did not converge");
        }
        await SetClient(window, 1366, 768); await Until(() => vm.Fresh && vm.IsManagementMode);
        // Presentation-only unconfigured fixture. It does not start any Production resources.
        ParcelSnapshot Deferred(ParcelSnapshot s) => s with {
            Configuration = s.Configuration with { Effective = s.Configuration.Effective with { Mode = RunMode.Production,
                DeferredConnections = [vm.Left.ReaderId, vm.Right.ReaderId, s.Configuration.Effective.Wcs!.InstanceId] } },
            Positions = s.Positions!.Select(p => p with { Admit = false, BlockingReasons = ["连接待配置"] }).ToArray(),
            BlockingReasons = ["连接待配置：左相机、右相机、RCS。"] };
        probe.Project = Deferred; await Until(() => vm.ShowSetupGuidance);
        Check(vm.SetupItems == "左相机或右相机（配置一台即可）、RCS" && vm.SetupHeading.Contains("2"), "unconfigured guide lists the exact missing connection count");
        foreach (var size in new[] { (1366d, 768d), (800d, 420d) })
        {
            await SetClient(window, size.Item1, size.Item2); await Flush();
            Check(Descendants(window).OfType<SupplyStationPosition>().Count(p => p.IsVisible) == 2 && ((Border)window.FindName("OperationalStatusBar")).IsVisible,
                "unconfigured work retains both positions and the real status banner at " + size.Item1);
            CaptureInternal(evidence, "operator-unconfigured-" + (int)size.Item1, client, lines);
        }
        await SetClient(window, 1366, 768); window.ShowPositionDiagnostics(vm.Left); await Flush();
        Check(vm.SelectedPage == "diagnostics" && !vm.CanReconnectConfigured && vm.ReconnectHint.Contains("尚无已配置"), "unconfigured diagnostics disable reconnect with a specific reason");
        Check(vm.LeftConnectionDiagnosis.Contains("暂未启用") && vm.RightConnectionDiagnosis.Contains("暂未启用") && vm.RcsConnectionDiagnosis.Contains("尚未配置"), "each connection explains missing configuration instead of Unknown");
        foreach (var label in new[] { "设置左相机", "设置右相机", "设置RCS" }) Check(Descendants(window).OfType<Button>().Single(b => Equals(b.Content, label)).IsVisible, label + " is directly available");
        var diagnosticText = string.Join("\n", Descendants(window).OfType<TextBlock>().Where(t => t.IsVisible).Select(t => t.Text));
        Check(!diagnosticText.Contains("sportmaster.reader") && !diagnosticText.Contains("Health:") && !diagnosticText.Contains("原配置账") && !diagnosticText.Contains("工作集"), "default diagnostics use human labels; original identifiers remain available for support copy");
        Check(vm.DiagnosticTechnicalSummary.Contains(vm.Left.ReaderId), "support copy preserves exact diagnostic identity");
        probe.Project = s => s with { Devices = s.Devices!.Select(d => d.DeviceId == vm.Left.ReaderId ? d with { Health = DeviceHealth.Unknown, Source = "SDK:hikrobot-mvs; phase=acquiring-decoder-unavailable; frames=12; bytes=75497472; decoder=Unavailable; businessReady=false" } : d).ToArray() };
        await Until(() => vm.LeftConnectionDiagnosis.Contains("12帧"));
        Check(vm.LeftConnectionDiagnosis.Contains("尚未接入条码识别") && !vm.LeftConnectionDiagnosis.Contains("连接已确认"), "received MVS images are distinguished from a usable barcode decoder");
        probe.Project = s => s with { Devices = s.Devices!.Select(d => d.DeviceId == vm.Left.ReaderId ? d with { Health = DeviceHealth.Unknown, Source = "SDK:hikrobot-mvs; phase=acquiring-decoder-unavailable; frames=0; decoder=Unavailable; businessReady=false" } : d).ToArray() };
        await Until(() => vm.LeftConnectionDiagnosis.Contains("尚未收到帧"));
        Check(vm.LeftConnectionDiagnosis.Contains("核对触发"), "zero-image MVS state identifies the trigger/acquisition check");
        probe.Project = Deferred; await Until(() => vm.ShowSetupGuidance);
        vm.Message = "合成重连失败，请检查网络"; await Flush();
        Check(Descendants(window).OfType<TextBlock>().Any(t => t.IsVisible && t.Text.Contains("合成重连失败")), "diagnostics exposes scoped action failures when its redundant global status banner is hidden");
        vm.Message = ""; await Flush();
        CaptureInternal(evidence, "operator-diagnostics-unconfigured", client, lines);
        Click(window, "设置右相机"); await Until(() => vm.HasLoadedConfiguration && !vm.Busy && vm.SelectedPage == "maintenance"); await Flush();
        var editor = Descendants(window).OfType<ParameterSettingsControl>().Single();
        Check(editor.CurrentPageTitle == "右相机", "diagnostic action opens the exact right camera in the existing editor");
        Click(window, "返回作业"); probe.Project = null; await Until(() => !vm.ShowSetupGuidance && vm.Left.CanAdmit);
        runtime.EmitReferenceAt("left", "operator-left", "000123456", "operator-event-left");
        runtime.EmitReferenceAt("right", "operator-right", "000987654", "operator-event-right");
        await Until(() => vm.Left.SlotAvailable && vm.Right.SlotAvailable);
        var original = vm.Left.Operation!; var originalRequest = Wire.Encode(original.Wcs!.Request);
        await runtime.ExitManagementModeAsync(); await vm.RefreshManagementModeAsync(); await Until(() => !vm.IsManagementMode && vm.Left.SlotAvailable);
        foreach (var size in new[] { (1366d, 768d), (800d, 420d) })
        {
            await SetClient(window, size.Item1, size.Item2); await Flush();
            foreach (var position in Descendants(window).OfType<SupplyStationPosition>())
            {
                Check(!Descendants(position).OfType<Expander>().Any(), "daily position has no technical or nested recovery accordion");
                var slot = (TextBox)position.FindName("CurrentSlot"); var body = (ScrollViewer)position.FindName("PositionBody");
                Check(slot.IsVisible && slot.TranslatePoint(new Point(), body).Y + slot.ActualHeight <= body.ActualHeight + 1, "valid destination fits without scrolling at " + size.Item1);
            }
            CaptureInternal(evidence, "operator-normal-" + (int)size.Item1, client, lines);
        }
        await SetClient(window, 1366, 768); await Flush();
        var left = Descendants(window).OfType<SupplyStationPosition>().Single(p => ReferenceEquals(p.DataContext, vm.Left));
        var code = (TextBox)left.FindName("BarcodeEntry"); window.Activate(); code.Focus(); code.Select(1, 4);
        var selected = code.SelectedText; await vm.RefreshAsync(); await Flush();
        Check(code.IsKeyboardFocused && code.SelectedText == selected, "ordinary refresh retains barcode focus and text selection");
        // Coherent display fixtures cover copy and layout only; the separate chain suite verifies Application transitions.
        foreach (var scenario in new[] { "no-read", "duplicate", "unknown" })
        {
            probe.Project = s => scenario == "unknown" ? HierarchyState(s, StageState.Unknown) : s with {
                Inputs = s.Inputs!.Select(i => i.PositionId != "left" ? i : i with {
                    Outcome = scenario == "duplicate" ? ParcelInputOutcome.Duplicate : ParcelInputOutcome.Rejected,
                    OperationId = null, BarcodeSelection = scenario == "no-read" ? null : i.BarcodeSelection, Reason = scenario == "duplicate" ? "重复读取，未创建新任务" : "未读到条码，请检查包裹标签后重新读码",
                    Observation = scenario == "duplicate" ? i.Observation : i.Observation with { Codes = [], Kind = "NoRead" } }).ToArray(),
                Positions = s.Positions!.Select(p => p.Id != "left" ? p : p with { CurrentOperation = scenario == "duplicate" ? p.CurrentOperation : null }).ToArray() };
            await Until(() => scenario == "unknown" ? vm.Left.HasUncertainOriginal : vm.Left.InputUnadmitted && vm.Left.Input?.Outcome == (scenario == "duplicate" ? ParcelInputOutcome.Duplicate : ParcelInputOutcome.Rejected));
            Check(!vm.Left.SlotAvailable && vm.Right.SlotAvailable && !vm.ShowSetupGuidance, scenario + " hides the current left destination and retains the healthy right result");
            CaptureInternal(evidence, "operator-" + scenario, client, lines);
        }
        probe.Project = s => Deferred(s); await Until(() => vm.MissingConnections.Length == 2);
        Check(!vm.ShowSetupGuidance, "configuration guide never covers an existing current parcel");
        probe.FailSnapshots = true; vm.MarkReadFailure("Synthetic read failure"); await Flush();
        Check(!vm.ShowSetupGuidance && !vm.CanReconnectConfigured && !vm.Left.SlotAvailable, "stale state revokes guide, reconnect and destination claims");
        probe.FailSnapshots = false; probe.Project = null; await Until(() => vm.Fresh);
        await runtime.ExitManagementModeAsync(); await vm.RefreshManagementModeAsync(); window.ShowPositionDiagnostics(vm.Left); await Flush();
        Check(vm.SelectedPage == "workstation", "ordinary operator cannot enter maintenance diagnostics");
        await ManagementModeFixture.Authorize(runtime); // Compare exact originals under the same authority; operator snapshots intentionally redact payloads.
        Check(Wire.Encode((await runtime.SnapshotAsync()).Operations.Single(o => o.Id == original.Id).Wcs!.Request) == originalRequest,
            "all presentation and navigation scenarios preserve the frozen original request");
        window.Close(); await Task.Delay(150);
        lines.Add("Native Windows WPF with synthetic data. Normal results use Application; deferred/error examples are explicitly projected display fixtures. Real equipment, production release and user/site validation NOT_RUN.");
    }
}

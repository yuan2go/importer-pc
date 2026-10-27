using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster;
using Ipc.Platform;

static partial class Program
{
    static async Task VerifyDebugLogLive(string root, string evidence, List<string> lines)
    {
        void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); lines.Add("PASS DEBUG-LIVE " + message); }
        async Task Until(Func<Task<bool>> ready)
        {
            for (var i = 0; i < 200; i++) { if (await ready()) return; await Task.Delay(25); }
            throw new TimeoutException("Debug/live scenario did not converge");
        }
        await VerifyJournalTail(evidence, Check);
        await using var peer = new HierarchyRcsPeer();
        var folder = Path.Combine(evidence, "config"); Directory.CreateDirectory(folder);
        var seed = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(root, "config/sportmaster/business.json")));
        var binding = new CapabilityBinding("integration", "sportmaster-rcs", "1.0.0", "sportmaster.integration", peer.Endpoint, "SPORTMASTER-RCS-SORTER-v1", new()
        {
            ["source"] = "SYNTHETIC-DEBUG", ["locale"] = "zh_CN", ["zone-id"] = "71", ["project-id"] = "83",
            ["specified-chute"] = "false", ["request-id-base"] = "910000000", ["request-id-count"] = "10000", ["heartbeat-interval-seconds"] = "3600",
            ["left-station-code"] = "TASK-STATION", ["right-station-code"] = "TASK-STATION", ["left-bcr-code"] = "TASK-LEFT", ["right-bcr-code"] = "TASK-RIGHT"
        });
        var config = seed with { Login = null, Wcs = binding, TimeoutMs = 10000 };
        ExtensionStore.AtomicWrite(Path.Combine(folder, "business.json"), config);
        var launch = new BusinessLaunch(folder, Path.Combine(evidence, "data"), null, null);
        await using var runtime = new ParcelRuntime(launch, new SportmasterFlow(), new HierarchyRcsFactory(launch)) { RuntimeIdentityPreflight = () => { } };
        await runtime.StartAsync(default);
        bool denied = false; try { await runtime.SetDebugLoggingAsync(true); } catch (UnauthorizedAccessException) { denied = true; }
        Check(denied, "ordinary mode cannot enable detailed runtime logging");
        await ManagementModeFixture.Authorize(runtime);
        Check(!(await runtime.DiagnosticSettingsAsync()).DebugEnabled, "every new runtime starts with detailed logging off");
        async Task<ParcelOperation> Scan(string position, string admission, string code, ParcelState expected = ParcelState.SlotResolved)
        {
            Check(runtime.EmitReferenceAt(position, admission, code, "event-" + admission), "synthetic input accepted: " + admission);
            await Until(async () => (await runtime.SnapshotAsync()).Operations.Any(o => o.AdmissionId == admission && o.State == expected));
            return (await runtime.SnapshotAsync()).Operations.Single(o => o.AdmissionId == admission);
        }
        await Scan("left", "debug-off", "000123");
        var before = await runtime.SnapshotAsync();
        var initial = await runtime.ReadDiagnosticTailAsync(null, null, default);
        Check(!initial.Entries.Any(e => e.Text.Contains("\"Level\":\"调试\"")) && initial.Entries.Any(e => e.Text.Contains("输入处理完成")),
            "normal mode retains detailed operational summaries without optional debug records");
        var hot = await runtime.SetDebugLoggingAsync(true);
        Check(hot.DebugEnabled && hot.Revision == 1 && (await runtime.SnapshotAsync()).RuntimeId == before.RuntimeId &&
            (await runtime.SnapshotAsync()).Configuration.Revision == before.Configuration.Revision, "debug switch takes effect without restart or a pending configuration revision");
        Check((await runtime.SetDebugLoggingAsync(true)).Revision == hot.Revision, "reapplying the same hot setting creates no extra revision");
        var original = await Scan("left", "debug-on", "000123");
        await Until(async () => (await runtime.ReadDiagnosticTailAsync(null, null, default)).Entries.Any(e => e.Text.Contains("原请求响应详情") && e.Text.Contains(original.Wcs!.Request.Id)));
        var captured = await runtime.ReadDiagnosticTailAsync(null, null, default); var joined = string.Join("\n", captured.Entries.Select(e => e.Text));
        Check(joined.Contains("扫码原始候选与筛选结果") && joined.Contains("原请求发送开始") && joined.Contains("原请求响应详情") &&
            joined.Contains("ElapsedMs") && joined.Contains("StatusCode") && joined.Contains("waveNo"), "real Application and loopback RCS trace contains candidates, original request, elapsed time and decoded HTTP response");
        Check(peer.Exchanges.Count == 2 && original.Wcs!.Attempts == 1, "enabling debug does not replay or duplicate a business request");
        var rejected = await Scan("left", "debug-business-rejected", "000126", ParcelState.WcsRejected);
        var unknown = await Scan("left", "debug-invalid-response", "000127", ParcelState.WcsUnknown);
        await Until(async () => (await runtime.ReadDiagnosticTailAsync(null, null, default)).Entries.Any(e => e.Text.Contains("SYNTH_CHUTE_CLOSED")));
        var failureTrace = string.Join("\n", (await runtime.ReadDiagnosticTailAsync(null, null, default)).Entries.Select(e => e.Text));
        Check(failureTrace.Contains(unknown.Wcs!.Request.Id) && failureTrace.Contains(rejected.Wcs!.Request.Id) && failureTrace.Contains("skuId") &&
            (await runtime.SnapshotAsync()).Operations.Single(o => o.Id == unknown.Id).Wcs!.State == StageState.Unknown,
            "unknown protocol result and explicit rejection retain original requests and diagnostic response evidence");
        var service = DispatchProxy.Create<IRuntimeLogUiFixture, RuntimeLogUiProbe>(); var probe = (RuntimeLogUiProbe)(object)service; probe.Inner = runtime;
        var control = new SoftwareLogControl(service); var client = new Grid(); client.Children.Add(control);
        var window = new Window { Content = client, Width = 1100, Height = 700 }; NameScope.SetNameScope(window, new NameScope()); window.RegisterName("ClientArea", client); window.Show();
        var debug = Descendants(control).OfType<CheckBox>().Single(c => AutomationProperties.GetAutomationId(c) == "DebugLoggingMode");
        var follow = Descendants(control).OfType<CheckBox>().Single(c => AutomationProperties.GetName(c) == "实时跟随日志");
        var mode = Descendants(control).OfType<ComboBox>().Single(c => AutomationProperties.GetName(c) == "日志显示方式");
        var terminal = Descendants(control).OfType<TextBox>().Single(t => AutomationProperties.GetAutomationId(t) == "TerminalLogView");
        var pause = Descendants(control).OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == "PauseLogDisplay");
        await Until(() => Task.FromResult(debug.IsEnabled && debug.IsChecked == true));
        Check(debug.IsChecked == true, "log-page parameter reads the shared actual runtime setting");
        mode.SelectedIndex = 1; follow.IsChecked = true;
        await Until(() => Task.FromResult(terminal.Text.Contains("原请求响应详情")));
        Check(captured.Entries.Any(e =>
        {
            using var json = JsonDocument.Parse(e.Text);
            return json.RootElement.TryGetProperty("At", out var at) && terminal.Text.Contains("原始时间：" + at.GetDateTimeOffset().ToString("O"));
        }), "terminal timestamp uses the source event time rather than the later append-publication time");
        Click(window, "暂停显示"); terminal.Focus(); terminal.Select(0, Math.Min(40, terminal.Text.Length));
        var frozen = terminal.Text; var selection = terminal.SelectedText; var reads = probe.TailReads;
        var right = await Scan("right", "written-while-display-paused", "000139"); await Task.Delay(1250);
        Check(terminal.Text == frozen && terminal.SelectedText == selection && probe.TailReads == reads && Equals(pause.Content, "继续显示"),
            "pause freezes text, selection and live reads while business processing continues");
        Check((await runtime.ReadDiagnosticTailAsync(null, null, default)).Entries.Any(e => e.Text.Contains(right.Wcs!.Request.Id)),
            "background file append and tail publication continue during display pause");
        CaptureInternal(evidence, "terminal-display-paused", window, lines);
        Click(window, "继续显示"); await Until(() => Task.FromResult(terminal.Text.Contains("written-while-display-paused")));
        Check(terminal.Text.Length <= 65536, "resume catches up from the same cursor within the presentation budget");
        await SetClient(window, 800, 420); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
        Check(terminal.ActualHeight >= 80 && Inside(pause, client) && debug.IsVisible && follow.IsVisible, "hot setting and pause controls leave a usable terminal at 800x420");
        CaptureInternal(evidence, "terminal-live-800", window, lines);
        debug.IsChecked = false; await Until(async () => !(await runtime.DiagnosticSettingsAsync()).DebugEnabled && debug.IsEnabled);
        Check(!debug.IsChecked!.Value, "unchecking the page parameter immediately disables detailed logging");
        var disabled = await Scan("right", "debug-disabled-again", "000123"); await Task.Delay(150);
        var tail = await runtime.ReadDiagnosticTailAsync(null, null, default);
        Check(!tail.Entries.Any(e => e.Text.Contains("\"Level\":\"调试\"") && e.Text.Contains(disabled.Id)), "new operations after disabling no longer generate debug stage traces");
        var page = await runtime.QueryLogsAsync(new(DateTimeOffset.Now.AddMinutes(-10), DateTimeOffset.Now.AddMinutes(1), Level: "调试"), null, 0, default);
        Check(page.Entries.Length > 0 && page.Entries.All(e => e.Level == "调试"), "debug records are persisted and queryable through the ordinary fixed-range log reader");
        runtime.ReleaseLogSelection(page.Selection);
        // Ignore-cancellation transport seam proves stale reads cannot overwrite a frozen view.
        var delayed = new TaskCompletionSource<RuntimeDiagnosticBatch>(TaskCreationOptions.RunContinuationsAsynchronously);
        probe.Tail = () => delayed.Task;
        var startReads = probe.TailReads; await Until(() => Task.FromResult(probe.TailReads > startReads));
        Click(window, "暂停显示"); frozen = terminal.Text;
        delayed.SetResult(captured with { Entries = [new(999999, DateTimeOffset.Now, "LATE-PAUSED-CONTENT")] }); await Task.Delay(150);
        Check(terminal.Text == frozen && !terminal.Text.Contains("LATE-PAUSED-CONTENT"), "late live response cannot replace a paused screen");
        probe.Tail = null; mode.SelectedIndex = 0; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Check(!follow.IsChecked!.Value && !terminal.IsVisible, "returning to list exits live follow and restores fixed-query behavior");
        mode.SelectedIndex = 1; follow.IsChecked = true;
        await Until(() => Task.FromResult(terminal.Text.Contains("原请求")));
        var revoked = new TaskCompletionSource<RuntimeDiagnosticBatch>(TaskCreationOptions.RunContinuationsAsynchronously); probe.Tail = () => revoked.Task;
        startReads = probe.TailReads; await Until(() => Task.FromResult(probe.TailReads > startReads));
        await runtime.SetDebugLoggingAsync(true);
        await runtime.ExitManagementModeAsync(); control.Revoke(); revoked.SetResult(captured);
        await Task.Delay(150);
        Check(terminal.Text.Length == 0 && !debug.IsEnabled, "revocation clears live data and fences a late response");
        denied = false; try { await runtime.DiagnosticSettingsAsync(); } catch (UnauthorizedAccessException) { denied = true; }
        Check(denied, "direct diagnostic-settings reads retain the management authority guard");
        window.Close();
        await runtime.DisposeAsync();
        var posts = peer.Exchanges.Count;
        await using var restarted = new ParcelRuntime(launch, new SportmasterFlow(), new HierarchyRcsFactory(launch)) { RuntimeIdentityPreflight = () => { } };
        await restarted.StartAsync(default); await ManagementModeFixture.Authorize(restarted);
        Check(!(await restarted.DiagnosticSettingsAsync()).DebugEnabled && peer.Exchanges.Count == posts,
            "restart defaults debugging off and never replays the retained unknown request");
        lines.Add("Native HWND/Dispatcher and actual loopback HTTP with synthetic identity/observations; no physical camera, production RCS, installed replacement or site qualification.");
    }

    static async Task VerifyJournalTail(string evidence, Action<bool, string> check)
    {
        var root = Path.Combine(evidence, "journal-tail");
        await using var journal = new RuntimeJournal(root, _ => { });
        long Written() { using var json = JsonDocument.Parse(Wire.Encode(journal.Status)); return json.RootElement.GetProperty("Written").GetInt64(); }
        async Task Drain(long expected)
        { for (int i = 0; i < 400 && Written() < expected; i++) await Task.Delay(10); check(Written() >= expected, "diagnostic append reached the expected bounded test batch"); }
        for (int batch = 0; batch < 7; batch++)
        {
            for (int i = 0; i < 100; i++) journal.Write(new { At = DateTimeOffset.Now, Source = "合成", Level = "信息", Message = "tail-" + (batch * 100 + i) });
            await Drain((batch + 1) * 100);
        }
        var tail = journal.ReadTail(null, null);
        check(tail.Entries.Length == 200 && tail.Cursor == 700, "first live view reads only the latest 200 successfully appended records");
        var missed = journal.ReadTail(tail.Stream, 0);
        check(missed.Missed == 188 && missed.HasMore && missed.Entries.Length == 200, "bounded 512-record cache explicitly reports cursor overrun and catch-up");
        var secret = "SYNTHETIC-SECRET-DO-NOT-LOG";
        journal.Write(new { At = DateTimeOffset.Now, Body = JsonSerializer.Serialize(new { password = secret, nested = new { token = secret } }), PartialBody = "{\"authorization\":\"" + secret + "\"," });
        await Drain(701);
        check(!journal.ReadTail(null, null).Entries.Any(e => e.Text.Contains(secret)) && !File.ReadAllText(Path.Combine(root, "runtime.jsonl")).Contains(secret), "nested JSON bodies and partial quoted sensitive fields are redacted in files and live views");
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var hook = typeof(RuntimeJournal).GetProperty("BeforeAppend", BindingFlags.Instance | BindingFlags.NonPublic)!;
        hook.SetValue(journal, (Action)(() => { entered.Set(); release.Wait(); }));
        journal.Write(new { Event = "hold-writer" });
        await Task.Run(() => { if (!entered.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException(); });
        try
        {
            for (int i = 0; i < 400; i++) journal.WriteDebug(new { Level = "调试", Event = "pressure", Number = i });
            journal.Write(new { Event = "ordinary-event-still-admitted" });
            using var status = JsonDocument.Parse(Wire.Encode(journal.Status));
            check(status.RootElement.GetProperty("DebugDropped").GetInt64() > 0 && status.RootElement.GetProperty("Dropped").GetInt64() == 0,
                "debug pressure is counted and leaves capacity for ordinary logs without blocking callers");
        }
        finally { hook.SetValue(journal, null); release.Set(); }
    }
}

public interface IRuntimeLogUiFixture : IWorkstationAdministration, IRuntimeDiagnosticsControl { }
public class RuntimeLogUiProbe : DispatchProxy
{
    public IWorkstationAdministration Inner = null!;
    public Func<Task<RuntimeDiagnosticBatch>>? Tail;
    public int TailReads;
    protected override object? Invoke(MethodInfo? method, object?[]? arguments)
    {
        if (method!.Name == nameof(IRuntimeDiagnosticsControl.ReadDiagnosticTailAsync)) { TailReads++; if (Tail is not null) return Tail(); }
        return method.Invoke(Inner, arguments);
    }
}

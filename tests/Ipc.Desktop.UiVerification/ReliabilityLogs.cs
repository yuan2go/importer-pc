using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster;
using Ipc.Assembly;
using Ipc.Platform;

static partial class Program
{
    static async Task VerifyReliabilityLogs(string root, string evidence, List<string> lines)
    {
        var config = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(root, "config/sportmaster/business.json")));
        var configRoot = Path.Combine(evidence, "config"); Directory.CreateDirectory(configRoot); ExtensionStore.AtomicWrite(Path.Combine(configRoot, "business.json"), config);
        var launch = new BusinessLaunch(configRoot, Path.Combine(evidence, "data"), null, null);
        // Match the existing frozen native fixtures: exercise real Application,
        // SQLite and WPF with explicit synthetic ports/identity. This scenario
        // neither installs machine policy nor qualifies deployment permission.
        await using var runtime = new ParcelRuntime(launch, new SportmasterFlow(), new CommercialProductization.SyntheticFactory(launch))
            { RuntimeIdentityPreflight = () => { } };
        lines.Add("SYNTHETIC ports/runtime identity; real Application/SQLite/WPF. Machine policy and production permissions remain independently guarded.");
        await runtime.StartAsync(default); await ManagementModeFixture.Authorize(runtime);
        var service = DispatchProxy.Create<IWorkstationAdministration, LogUiProbe>(); var probe = (LogUiProbe)(object)service; probe.Inner = runtime;
        var entries = Enumerable.Range(1, 401).Select(i => new SoftwareLogEntry(new("synthetic", i, "hash-" + i, "运行/runtime.jsonl"), "合成", DateTimeOffset.UtcNow, "错误",
            JsonSerializer.Serialize(new { Message = "Synthetic exception " + i, Exception = string.Concat(Enumerable.Repeat("    at Synthetic.Stack.Frame()\n", 300)), Path = @"D:\synthetic\logs" }))).ToArray();
        probe.Query = (_, _, _) => Task.FromResult(new SoftwareLogPage("fixed", [entries[200]], false, []));
        probe.Context = () => Task.FromResult(new SoftwareLogContext(entries, ["Synthetic 401 original records; no field qualification"]));
        var control = new SoftwareLogControl(service); var window = new Window { Content = control, Width = 1000, Height = 700 }; window.Show();
        Button Button(string label) => Descendants(control).OfType<Button>().Single(b => Equals(b.Content, label));
        async Task Settle() { var sw = Stopwatch.StartNew(); while (!Button("查询").IsEnabled) { if (sw.Elapsed.TotalSeconds > 15) throw new TimeoutException(); await Task.Delay(10); } await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout(); }
        var waits = new List<double>(); using var stop = new CancellationTokenSource();
        var dispatcher = window.Dispatcher;
        var monitor = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                var sw = Stopwatch.StartNew(); await dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
                lock (waits) waits.Add(sw.Elapsed.TotalMilliseconds);
                try { await Task.Delay(10, stop.Token); } catch (OperationCanceledException) { break; }
            }
        });
        try
        {
            var advanced = (Expander)typeof(SoftwareLogControl).GetField("advancedFilters", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(control)!;
            advanced.IsExpanded = true; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
            var dates = Descendants(control).OfType<DateTimeInput>().ToArray();
            if (dates.Length != 2 || Descendants(control).OfType<DatePicker>().Count() != 2) throw new InvalidOperationException("Log dates must be native calendar/time controls");
            Click(window, "最近7天"); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            if (!DateTimeOffset.TryParse(dates[0].Timestamp, out var start) || !DateTimeOffset.TryParse(dates[1].Timestamp, out var end) || (end - start).TotalDays is < 6.99 or > 7.01) throw new InvalidOperationException("Quick log range did not update both bound native inputs");
            Click(window, "查询"); await Settle();
            var applied = (TextBox)typeof(SoftwareLogControl).GetField("effectiveRange", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(control)!;
            if (!applied.Text.Contains("已应用范围") || !applied.Text.Contains(start.ToString("zzz"))) throw new InvalidOperationException("Applied log range omitted actual offset");
            Descendants(dates[0]).OfType<DatePicker>().Single().SelectedDate = DateTime.Today.AddDays(-2);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            if (Button("导出全部匹配…").IsEnabled || Button("导出时间范围…").IsEnabled || !applied.Text.Contains("尚无有效查询快照")) throw new InvalidOperationException("Native date edit must invalidate fixed export selection");
            Click(window, "最近24小时"); advanced.IsExpanded = false;
            lines.Add("PASS LIA-LOG native calendars, quick ranges and offset bind to actual query; editing date invalidates both fixed export actions");
            Click(window, "查询"); await Settle();
            Descendants(control).OfType<DataGrid>().Single().SelectedIndex = 0;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Descendants(control).OfType<Expander>().Single(e => Equals(e.Header, "时间、更多筛选、上下文与导出")).IsExpanded = true;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
            var watch = Stopwatch.StartNew(); Click(window, "查看原始上下文"); await Settle();
            var detail = Descendants(control).OfType<TextBox>().Single(t => t.IsReadOnly && t.AcceptsReturn);
            stop.Cancel(); await monitor;
            lines.Add($"MEASURE H02 native context_records={entries.Length}; original_chars={entries.Sum(e => e.Text.Length)}; presented_chars={detail.Text.Length}; elapsed_ms={watch.Elapsed.TotalMilliseconds:F2}; dispatcher_max_ms={waits.DefaultIfEmpty().Max():F2}");
            if (detail.Text.Length > 65536) throw new InvalidOperationException("H02 context presentation exceeded the 65536-character total budget");
            if (!detail.Text.Contains("导出") || !detail.Text.Contains("呈现预算")) throw new InvalidOperationException("Budgeted detail must disclose omitted presentation and the complete original export path");
            CaptureInternal(evidence, "logs-bounded-context", window, lines);
            // A slow old context cannot republish after unload/revocation.
            var late = new TaskCompletionSource<SoftwareLogContext>(TaskCreationOptions.RunContinuationsAsynchronously); probe.Context = () => late.Task;
            Click(window, "查看原始上下文"); await Task.Delay(20); control.Revoke(); late.SetResult(new([entries[0]], [])); await Settle();
            if (detail.Text.Length != 0) throw new InvalidOperationException("Old context returned after revocation");
            lines.Add("PASS H02 native bounded background context, explicit full-export notice, late result revoked; layout/font unchanged");
        }
        finally { stop.Cancel(); await monitor; window.Close(); }
    }
}

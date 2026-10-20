using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Platform;

static partial class Program
{
    // Independent analysis fixture: no new production diagnostics, budget or admission policy.
    static async Task VerifyAuditPerformance(string root, string evidence, List<string> lines)
    {
        var configRoot = Path.Combine(evidence, "config"); Directory.CreateDirectory(configRoot);
        File.Copy(Path.Combine(root, "config/sportmaster/business.json"), Path.Combine(configRoot, "business.json"));
        var launch = new BusinessLaunch(configRoot, Path.Combine(evidence, "data"), null, null);
        var startup = Stopwatch.StartNew();
        await using var runtime = new ParcelRuntime(launch, new SportmasterFlow(), new VisualFactory(launch, false)) { RuntimeIdentityPreflight = () => { } };
        await runtime.StartAsync(default);
        var startReturnedMs = startup.Elapsed.TotalMilliseconds;
        ParcelSnapshot snapshot = await runtime.SnapshotAsync();
        for (var i = 0; snapshot.Availability?.Admit != true && i < 1500; i++)
        { await Task.Delay(20); snapshot = await Task.Run(runtime.SnapshotAsync); }
        if (snapshot.Availability?.Admit != true) throw new InvalidOperationException("Synthetic reader health never became ready for admission");
        var startMs = startup.Elapsed.TotalMilliseconds;
        var gate = (SemaphoreSlim)typeof(ParcelRuntime).GetField("gate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(runtime)!;
        var repository = typeof(ParcelRuntime).GetField("repository", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(runtime)!;
        var pending = repository.GetType().GetMethod("PendingReceipts")!;
        var capacity = typeof(ParcelRuntime).GetMethod("Capacity", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var datasets = new List<object>(); var inputSamples = new List<double>();
        foreach (var total in new[] { 0, 50, 199, 300 })
        {
            for (var n = inputSamples.Count; n < total; n++)
            {
                var position = n % 2 == 0 ? "left" : "right";
                var entity = "synthetic-perf-" + n; var watch = Stopwatch.StartNew();
                if (!runtime.EmitReferenceAt(position, entity, "000" + n.ToString("D10"), Wire.Id())) throw new InvalidOperationException("Synthetic input was not accepted by the reader");
                var terminal = false;
                for (var poll = 0; poll < 300; poll++)
                {
                    snapshot = await Task.Run(runtime.SnapshotAsync);
                    var operation = snapshot.Operations.SingleOrDefault(o => o.AdmissionId == entity);
                    if (operation?.State == ParcelState.SlotResolved) { terminal = true; break; }
                    await Task.Delay(10);
                }
                if (!terminal)
                {
                    File.WriteAllText(Path.Combine(evidence, "failed-input-snapshot.json"), Wire.Encode(snapshot));
                    throw new InvalidOperationException("Synthetic operation never reached its own terminal result: " + entity);
                }
                inputSamples.Add(watch.Elapsed.TotalMilliseconds);
            }
            var snapshotMs = new List<double>(); var cloneMs = new List<double>(); var capacityMs = new List<double>(); var pendingMs = new List<double>();
            var allocated = GC.GetTotalAllocatedBytes(true); var cpu = Process.GetCurrentProcess().TotalProcessorTime;
            for (var i = 0; i < 40; i++)
            {
                var sw = Stopwatch.StartNew(); snapshot = await Task.Run(runtime.SnapshotAsync); snapshotMs.Add(sw.Elapsed.TotalMilliseconds);
                sw.Restart(); _ = Wire.Decode<ParcelSnapshot>(Wire.Encode(snapshot)); cloneMs.Add(sw.Elapsed.TotalMilliseconds);
                await gate.WaitAsync();
                try
                {
                    sw.Restart(); _ = pending.Invoke(repository, null); pendingMs.Add(sw.Elapsed.TotalMilliseconds);
                    sw.Restart(); _ = capacity.Invoke(runtime, null); capacityMs.Add(sw.Elapsed.TotalMilliseconds);
                }
                finally { gate.Release(); }
            }
            // Normal and time/state-filtered history each retain their actual server query contract.
            var historyMs = new List<double>();
            for (var i = 0; i < 20; i++)
            { var sw = Stopwatch.StartNew(); _ = await Task.Run(() => runtime.HistoryAsync(limit: 50)); historyMs.Add(sw.Elapsed.TotalMilliseconds); }
            datasets.Add(new { Total = total, Resident = snapshot.Operations.Length, snapshot.Capacity, JsonBytes = Encoding.UTF8.GetByteCount(Wire.Encode(snapshot)),
                Snapshot = Stats(snapshotMs), Clone = Stats(cloneMs), CapacityProjection = Stats(capacityMs), PendingReceiptQuery = Stats(pendingMs), HistoryPage50 = Stats(historyMs),
                AllocationBytes = GC.GetTotalAllocatedBytes(true) - allocated, CpuMs = (Process.GetCurrentProcess().TotalProcessorTime - cpu).TotalMilliseconds });
            lines.Add($"PERF total={total} resident={snapshot.Operations.Length} snapshot P50={Median(snapshotMs):F2}ms clone P50={Median(cloneMs):F2}ms capacity P50={Median(capacityMs):F2}ms");
        }
        // Controlled gate contention: demonstrates scheduling cost without pretending it is a field distribution.
        var contention = new List<double>();
        for (var i = 0; i < 10; i++)
        {
            await gate.WaitAsync(); var sw = Stopwatch.StartNew(); var read = Task.Run(runtime.SnapshotAsync);
            await Task.Delay(50); gate.Release(); await read; contention.Add(sw.Elapsed.TotalMilliseconds);
        }
        var windows = new List<SupplyStationWindow>(); var idle = new List<object>();
        var firstRender = Stopwatch.StartNew();
        var window = (SupplyStationWindow)Presentation.Create(runtime, null, "standard"); windows.Add(window); window.Show();
        await window.ViewModel.RefreshDisplayAsync(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var windowToReadyMs = firstRender.Elapsed.TotalMilliseconds;
        var refresh = new List<double>(); var notifications = 0;
        window.ViewModel.PropertyChanged += (_, _) => notifications++;
        for (var i = 0; i < 30; i++)
        {
            var sw = Stopwatch.StartNew(); await window.ViewModel.RefreshDisplayAsync();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); refresh.Add(sw.Elapsed.TotalMilliseconds);
        }
        for (var count = 1; count <= 2; count++)
        {
            if (count == 2) { var child = (SupplyStationWindow)Presentation.Create(runtime, null, "standard"); windows.Add(child); child.Show(); }
            await Task.Delay(1500);
            var process = Process.GetCurrentProcess(); process.Refresh();
            var cpu = process.TotalProcessorTime; var alloc = GC.GetTotalAllocatedBytes(true); var memory = process.PrivateMemorySize64;
            var bytes = Directory.GetFiles(launch.DataRoot, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
            var gaps = new List<double>(); var watch = Stopwatch.StartNew(); var last = watch.Elapsed.TotalMilliseconds;
            // UI-thread heartbeat includes Dispatcher delays and timer quantization, with no universal pass threshold.
            for (var i = 0; i < 400; i++) { await Task.Delay(50); var now = watch.Elapsed.TotalMilliseconds; gaps.Add(now - last); last = now; }
            process.Refresh();
            idle.Add(new { Windows = count, WallMs = watch.Elapsed.TotalMilliseconds, CpuMs = (process.TotalProcessorTime - cpu).TotalMilliseconds,
                AllocationBytes = GC.GetTotalAllocatedBytes(true) - alloc, PrivateBytesStart = memory, PrivateBytesEnd = process.PrivateMemorySize64,
                DataBytesStart = bytes, DataBytesEnd = Directory.GetFiles(launch.DataRoot, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length), HeartbeatGap = Stats(gaps) });
        }
        foreach (var owned in windows.AsEnumerable().Reverse()) { owned.ConfirmDiscardDrafts = _ => true; owned.Close(); }
        var result = new { Scope = "Synthetic only; no device, upstream contract, production identity or target hardware qualification. Single process, sequential inputs; no same-side in-flight admission decision.",
            RuntimeStartReturnedMs = startReturnedMs, RuntimeStartToAdmissionReadyMs = startMs, WindowShowToAppliedSnapshotAndIdleMs = windowToReadyMs,
            InputToOwnTerminalPoll = Stats(inputSamples), Datasets = datasets, Controlled50MsGateHold = Stats(contention),
            NativeRefreshThroughDispatcherIdle = Stats(refresh), NativeRefreshNotifications = notifications, Idle = idle,
            Environment = new { OS = Environment.OSVersion.ToString(), Framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription, CpuCount = Environment.ProcessorCount, Is64Bit = Environment.Is64BitProcess },
            Limits = "Measurements include synthetic-peer filesystem work; terminal polling includes snapshot/poll delay. Clone and capacity probes are separate experiments, not additive phase attribution. Allocation includes background work. 20-second idle samples do not prove long-term stability; no field budget supplied." };
        File.WriteAllText(Path.Combine(evidence, "R02-performance.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        lines.Add("PASS R-02 samples completed with 300 independently correlated terminal results; no same-side overlap or production-policy changes.");
        static double Median(List<double> values) => values.Order().ElementAt(values.Count / 2);
        static object Stats(List<double> values)
        {
            var ordered = values.Order().ToArray();
            double Percentile(double p) => ordered[Math.Min(ordered.Length - 1, (int)Math.Ceiling(p * ordered.Length) - 1)];
            return new { Count = values.Count, P50Ms = Percentile(.50), P95Ms = Percentile(.95), P99Ms = Percentile(.99), MaxMs = ordered[^1], SamplesMs = values };
        }
    }
}

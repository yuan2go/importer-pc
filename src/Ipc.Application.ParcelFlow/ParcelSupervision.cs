using System.Collections.Concurrent;
using System.Diagnostics;
using Ipc.Platform;
using Ipc.Application.Contracts;

namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime : IRuntimeProgress
{
    sealed class UiProgress
    {
        public long Tick = Stopwatch.GetTimestamp(), Count;
        public double Maximum, Last;
        public readonly long[] Milliseconds = new long[2049];
        public readonly object Sync = new();
        public object Snapshot(string id)
        {
            lock (Sync)
            {
                double Percentile(double p)
                { long total = 0; for (var i = 0; i < Milliseconds.Length; i++) { total += Milliseconds[i]; if (Count != 0 && total >= Math.Ceiling(Count * p)) return i == 2048 ? Maximum : i; } return 0; }
                return new
                {
                    Id = id,
                    LastDispatchAgeMs = Stopwatch.GetElapsedTime(Tick).TotalMilliseconds,
                    Samples = Count,
                    LastDispatchMs = Last,
                    MaximumDispatchMs = Maximum,
                    P95UpperBoundMs = Percentile(.95),
                    P99UpperBoundMs = Percentile(.99),
                    HistogramCounts = Milliseconds.Select((count, upper) => new { UpperMs = upper, Count = count }).Where(b => b.Count != 0).ToArray(),
                    Histogram = "1ms upper bounds; values above 2047ms use observed maximum; independent 500ms probe"
                };
            }
        }
    }
    readonly ConcurrentDictionary<string, UiProgress> uiProgress = new();
    readonly ConcurrentDictionary<string, Task> backgroundTasks = new();
    readonly ConcurrentDictionary<string, (DateTimeOffset At, string Detail)> backgroundProgress = new();
    Task? supervisor;
    string? supervisionFailure;
    long manualCommandsSubmitted, manualCommandsCompleted;
    public void RegisterUi(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _) || uiProgress.Count >= 8) throw new InvalidOperationException("UI progress registration limit/identity");
        uiProgress.TryAdd(id, new());
    }
    public void ReportUiProgress(string id, double dispatchMilliseconds)
    {
        if (!double.IsFinite(dispatchMilliseconds) || dispatchMilliseconds < 0) throw new ArgumentOutOfRangeException(nameof(dispatchMilliseconds));
        if (uiProgress.TryGetValue(id, out var progress)) lock (progress.Sync)
        { progress.Tick = Stopwatch.GetTimestamp(); progress.Count++; progress.Last = dispatchMilliseconds; progress.Maximum = Math.Max(progress.Maximum, dispatchMilliseconds); progress.Milliseconds[(int)Math.Min(2048, Math.Ceiling(dispatchMilliseconds))]++; }
    }
    public void UnregisterUi(string id) => uiProgress.TryRemove(id, out _);
    public object RuntimeProgress()
    {
        int reads, administration, managementStatus; lock (readOwnership) { reads = readOperations.Count; administration = administrationOperations.Count; managementStatus = managementStatusOperations.Count; }
        return new
        {
            SchemaVersion = 2,
            At = DateTimeOffset.UtcNow,
            RuntimeElapsedMs = Stopwatch.GetElapsedTime(performanceStarted).TotalMilliseconds,
            SourceVersion = System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(ParcelRuntime).Assembly)?.InformationalVersion,
            ProcessId = Environment.ProcessId,
            RuntimeId = runtimeId,
            Stopping = stopping.IsCancellationRequested,
            Connections = readers.Values.Select(r => new
            {
                r.Source.DeviceId,
                r.Epoch,
                Health = r.Health?.Health.ToString(),
                Acquisition = (r.Source as IObservationDiagnosticsSource)?.ReadDiagnostics(),
                Performance = (r.Source as IObservationPerformanceSource)?.ReadPerformance()
            }).ToArray(),
            Flow = FlowPerformance(),
            Presentation = PresentationProgress(),
            Workers = WorkerProgress(),
            ManualCommands = new { Submitted = Interlocked.Read(ref manualCommandsSubmitted), Completed = Interlocked.Read(ref manualCommandsCompleted), Meaning = "Durable command acceptance/decision; not RCS completion" },
            RcsDispatch = (ports?.Wcs as IWcsDispatchResources)?.DispatchProgress,
            Ui = uiProgress.Select(p => p.Value.Snapshot(p.Key)).ToArray(),
            Background = backgroundTasks.Select(p => new
            {
                Name = p.Key,
                State = p.Value.Status.ToString(),
                LastProgress = backgroundProgress.TryGetValue(p.Key, out var seen) ? (DateTimeOffset?)seen.At : null,
                Detail = backgroundProgress.TryGetValue(p.Key, out var detail) ? detail.Detail : "NotStarted"
            }).ToArray(),
            ReadOperations = reads,
            AdministrationOperations = administration,
            ManagementStatusOperations = managementStatus,
            OverloadWrites = Volatile.Read(ref pendingOverloadWrites),
            Diagnostics = journal?.Status,
            Failure = Volatile.Read(ref failure),
            SupervisionFailure = Volatile.Read(ref supervisionFailure)
        };
    }
    void StartSupervision() => supervisor = Task.Run(async () =>
    {
        using var process = Process.GetCurrentProcess();
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        var cameraSample = Stopwatch.StartNew();
        try
        {
            do
            {
                try
                {
                    process.Refresh();
                    long Bytes(string name) { var f = new FileInfo(Path.Combine(directory!, name)); return f.Exists ? f.Length : 0; }
                    var sample = new
                    {
                        Progress = RuntimeProgress(),
                        Resource = new
                        {
                            process.WorkingSet64,
                            process.PrivateMemorySize64,
                            process.HandleCount,
                            Threads = process.Threads.Count,
                            ManagedBytes = GC.GetTotalMemory(false),
                            CpuTotalMs = process.TotalProcessorTime.TotalMilliseconds,
                            LogicalProcessors = Environment.ProcessorCount,
                            DatabaseBytes = Bytes(Id + ".db"),
                            WalBytes = Bytes(Id + ".db-wal")
                        },
                        Policy = "Observation only; no restart/replay/GC; an absent/stale file requires external investigation"
                    };
                    ExtensionStore.AtomicWrite(Path.Combine(directory!, "runtime-progress.json"), sample);
                    if (cameraSample.Elapsed >= TimeSpan.FromSeconds(5))
                    {
                        cameraSample.Restart();
                        var workers = WorkerProgress();
                        var now = DateTimeOffset.UtcNow;
                        if (workers.Any(w => w.ProducerStageMs >= 1000 ||
                            w.OldestWaiting is { } waiting && now - waiting >= TimeSpan.FromSeconds(1) ||
                            w.ProcessingSince is { } processing && now - processing >= TimeSpan.FromSeconds(1)))
                            WriteRuntimeLog("扫码节奏", "扫码队列等待采样", new
                            {
                                Workers = workers,
                                Meaning = "独立于业务锁采样；处理中输入尚无完成结论。队列包含等待业务锁的输入，容量等待仍由采集端持有；不据此重发、清队列或释放原请求。"
                            });
                        foreach (var reader in readers.Values)
                            if (reader.Source is IObservationDiagnosticsSource source)
                                DebugDiagnostic("相机通信", "相机取帧与解码采样", () => source.ReadDiagnostics());
                    }
                    Volatile.Write(ref supervisionFailure, null);
                }
                catch (Exception ex) { Volatile.Write(ref supervisionFailure, ex.GetType().Name + "; external collector required; ownership retained"); }
            } while (await timer.WaitForNextTickAsync(stopping.Token).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
    });
}

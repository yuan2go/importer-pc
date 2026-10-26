using System.Collections.Concurrent;
using System.Diagnostics;
using Ipc.Platform;

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
                return new { Id = id, LastDispatchAgeMs = Stopwatch.GetElapsedTime(Tick).TotalMilliseconds, Samples = Count,
                    LastDispatchMs = Last, MaximumDispatchMs = Maximum, P95UpperBoundMs = Percentile(.95), P99UpperBoundMs = Percentile(.99),
                    HistogramCounts = Milliseconds.Select((count, upper) => new { UpperMs = upper, Count = count }).Where(b => b.Count != 0).ToArray(),
                    Histogram = "1ms upper bounds; values above 2047ms use observed maximum; independent 500ms probe" };
            }
        }
    }
    readonly ConcurrentDictionary<string, UiProgress> uiProgress = new();
    readonly ConcurrentDictionary<string, Task> backgroundTasks = new();
    readonly ConcurrentDictionary<string, (DateTimeOffset At, string Detail)> backgroundProgress = new();
    Task? supervisor;
    string? supervisionFailure;
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
        return new { At = DateTimeOffset.UtcNow, ProcessId = Environment.ProcessId, RuntimeId = runtimeId, Stopping = stopping.IsCancellationRequested,
            Connections = readers.Values.Select(r => new { r.Source.DeviceId, r.Epoch, Health = r.Health?.Health.ToString() }).ToArray(),
            Workers = WorkerProgress(), Ui = uiProgress.Select(p => p.Value.Snapshot(p.Key)).ToArray(),
            Background = backgroundTasks.Select(p => new { Name = p.Key, State = p.Value.Status.ToString(),
                LastProgress = backgroundProgress.TryGetValue(p.Key, out var seen) ? (DateTimeOffset?)seen.At : null,
                Detail = backgroundProgress.TryGetValue(p.Key, out var detail) ? detail.Detail : "NotStarted" }).ToArray(),
            ReadOperations = reads, AdministrationOperations = administration, ManagementStatusOperations = managementStatus, OverloadWrites = Volatile.Read(ref pendingOverloadWrites), Diagnostics = journal?.Status,
            Failure = Volatile.Read(ref failure), SupervisionFailure = Volatile.Read(ref supervisionFailure) };
    }
    void StartSupervision() => supervisor = Task.Run(async () =>
    {
        using var process = Process.GetCurrentProcess();
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            do
            {
                try
                {
                    process.Refresh();
                    long Bytes(string name) { var f = new FileInfo(Path.Combine(directory!, name)); return f.Exists ? f.Length : 0; }
                    var sample = new { Progress = RuntimeProgress(), Resource = new { process.WorkingSet64, process.PrivateMemorySize64, process.HandleCount,
                        Threads = process.Threads.Count, ManagedBytes = GC.GetTotalMemory(false), DatabaseBytes = Bytes(Id + ".db"), WalBytes = Bytes(Id + ".db-wal") },
                        Policy = "Observation only; no restart/replay/GC; an absent/stale file requires external investigation" };
                    ExtensionStore.AtomicWrite(Path.Combine(directory!, "runtime-progress.json"), sample);
                    Volatile.Write(ref supervisionFailure, null);
                }
                catch (Exception ex) { Volatile.Write(ref supervisionFailure, ex.GetType().Name + "; external collector required; ownership retained"); }
            } while (await timer.WaitForNextTickAsync(stopping.Token).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
    });
}

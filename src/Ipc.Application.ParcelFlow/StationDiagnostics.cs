using System.Diagnostics;
using Ipc.Application.Contracts;
namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime : IStationDiagnostics
{
    readonly object latencySync = new();
    readonly Queue<(long At, double Ms)> requestLatencies = new();
    readonly long latencyStarted = Stopwatch.GetTimestamp();
    long omittedLatencies;
    void RecordRequestLatency(ParcelOperation operation, double elapsed)
    {
        if (operation.DebugRequest is not null || operation.OperatorDebugRequest is not null || !double.IsFinite(elapsed) || elapsed < 0) return;
        lock (latencySync)
        {
            PruneLatencies();
            if (requestLatencies.Count == 4096) { requestLatencies.Dequeue(); omittedLatencies++; }
            requestLatencies.Enqueue((Stopwatch.GetTimestamp(), elapsed));
        }
    }
    void PruneLatencies()
    { while (requestLatencies.TryPeek(out var first) && Stopwatch.GetElapsedTime(first.At) > TimeSpan.FromMinutes(10)) requestLatencies.Dequeue(); }
    public Task<RequestLatencyWindow> ReadRequestLatencyAsync(CancellationToken token) => OwnedRead(owned =>
    {
        Management.Require(); owned.ThrowIfCancellationRequested();
        lock (latencySync)
        {
            PruneLatencies(); var values = requestLatencies.Select(x => x.Ms).Order().ToArray();
            var coverage = Stopwatch.GetElapsedTime(latencyStarted);
            return Task.FromResult(new RequestLatencyWindow(values.Length, omittedLatencies,
                values.Length == 0 ? null : values[(values.Length - 1) / 2],
                values.Length == 0 ? null : values[Math.Max(0, (int)Math.Ceiling(values.Length * .95) - 1)],
                coverage < TimeSpan.FromMinutes(10) ? coverage : TimeSpan.FromMinutes(10)));
        }
    }, token);
}

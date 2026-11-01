using System.Diagnostics;
using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime : IParcelPresentationDiagnostics
{
    readonly object presentationGate = new();
    readonly Dictionary<string, ParcelReadFeedback> presentationFeedback = [];
    readonly Queue<ParcelReadFeedback> presentationReads = [];
    readonly Queue<ParcelPresentationMetric> presentationMetrics = [];
    void RecordPresentationRead(string position, Observation observation)
    {
        if (observation.DecodedTicks <= 0 || observation.DecodedTicks > Stopwatch.GetTimestamp()) return;
        var feedback = new ParcelReadFeedback(runtimeId, position, observation.Id, observation.DeviceId, observation.Epoch,
            observation.Codes is { Length: 1 } codes ? codes[0] : null, observation.DecodedTicks, observation.ObservedAt);
        lock (presentationGate)
        {
            presentationFeedback[position] = feedback;
            if (presentationReads.Count == 512) presentationReads.Dequeue();
            presentationReads.Enqueue(feedback);
        }
    }
    public Task<ParcelReadFeedback[]> ReadPresentationFeedbackAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        lock (presentationGate) return Task.FromResult(stopped || stopping.IsCancellationRequested ? [] : presentationFeedback.Values.ToArray());
    }
    public Task ReportPresentationAsync(Guid runtime, string observation, string stage, long composedTicks, long snapshotTicks, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (stopped || stopping.IsCancellationRequested || runtime != runtimeId || stage is not ("ReadFeedbackComposed" or "SlotComposed") ||
            composedTicks <= 0 || composedTicks > Stopwatch.GetTimestamp() || snapshotTicks > composedTicks) return Task.CompletedTask;
        lock (presentationGate)
        {
            var read = presentationReads.LastOrDefault(r => r.ObservationId == observation);
            if (read is null || composedTicks < read.DetectedTicks) return Task.CompletedTask;
            if (presentationMetrics.Any(m => m.ObservationId == observation && m.Stage == stage)) return Task.CompletedTask;
            var metric = new ParcelPresentationMetric(observation, read.PositionId, stage,
                Stopwatch.GetElapsedTime(read.DetectedTicks, composedTicks).TotalMilliseconds,
                snapshotTicks > 0 ? Stopwatch.GetElapsedTime(snapshotTicks, composedTicks).TotalMilliseconds : null, DateTimeOffset.UtcNow);
            if (presentationMetrics.Count == 512) presentationMetrics.Dequeue();
            presentationMetrics.Enqueue(metric);
        }
        return Task.CompletedTask;
    }
    object PresentationProgress()
    {
        lock (presentationGate) return new { Availability = presentationMetrics.Count == 0 ? "NotMeasured" : "SamplesRecorded", Boundary = "Same-process decode completion to DWM composition fence; not camera exposure or photon timing",
            Recent = presentationMetrics.ToArray(), Window = 512 };
    }
}

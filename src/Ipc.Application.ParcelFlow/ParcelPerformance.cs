using System.Diagnostics;
using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.ParcelFlow;

// Passive correlation in the existing supervision snapshot. Never retains code,
// payload, URL, slot value or pixels; never queries the database or external ports.
public sealed partial class ParcelRuntime
{
    const int PerformanceWindow = 512;
    readonly long performanceStarted = Stopwatch.GetTimestamp();
    readonly object performanceSync = new();
    readonly Queue<FlowPerformanceEvent> performanceEvents = new();
    readonly Dictionary<string, Dictionary<string, long>> performanceCounts = new();
    long performanceSequence;
    sealed record FlowPerformanceEvent(long Sequence, DateTimeOffset At, DateTimeOffset? ObservedAt, string Position, string Kind,
        string? ObservationId, string? DeviceId, string? Epoch, string? SourceEventId, string? OperationId,
        string? RequestId, string? State, string? LocalFailure, double? ElapsedMs, double? SinceDecodeMs);

    void RecordFlowPerformance(string position, string kind, string? observation, AcquisitionIdentity? acquisition,
        string? operation = null, string? request = null, string? state = null, string? localFailure = null,
        double? elapsed = null, long decodedTicks = 0)
    {
        double? sinceDecode = decodedTicks > 0 && decodedTicks <= Stopwatch.GetTimestamp()
            ? Stopwatch.GetElapsedTime(decodedTicks).TotalMilliseconds : null;
        lock (performanceSync)
        {
            if (!performanceCounts.TryGetValue(position, out var counts)) performanceCounts.Add(position, counts = new());
            counts[kind] = counts.GetValueOrDefault(kind) + 1;
            if (performanceEvents.Count == PerformanceWindow) performanceEvents.Dequeue();
            performanceEvents.Enqueue(new(++performanceSequence, DateTimeOffset.UtcNow, acquisition?.ObservedAt, position, kind, observation,
                acquisition?.DeviceId, acquisition?.Epoch, acquisition?.SourceEventId, operation, request, state, localFailure, elapsed, sinceDecode));
        }
    }
    void RecordInputPerformance(ParcelInputSnapshot input)
    {
        var o = input.Observation;
        RecordFlowPerformance(input.PositionId, ParcelInputReasons.IsSamePlacement(input) ? "SamePlacement" : "Input" + input.Outcome,
            o.Id, o.OperatorInput is null ? new(o.DeviceId, o.Epoch, o.SourceEventId, o.AdmissionId, o.ObservedAt) : null,
            input.OperationId, state: input.Outcome.ToString(), decodedTicks: o.DecodedTicks);
    }
    void RecordRcsPerformance(ParcelOperation op, string kind, double? elapsed = null)
    {
        ParcelReadFeedback? read;
        lock (presentationGate) read = op.Acquisition is { } acquisition
            ? presentationReads.LastOrDefault(r => r.DeviceId == acquisition.DeviceId && r.Epoch == acquisition.Epoch && r.SourceEventId == acquisition.SourceEventId)
            : null;
        RecordFlowPerformance(op.PositionId ?? "Unassigned", kind, read?.ObservationId, op.Acquisition, op.Id,
            op.Wcs?.Request.Id, op.Wcs?.State.ToString(), op.Wcs?.LocalFailure.ToString(), elapsed, read?.DetectedTicks ?? 0);
    }
    void RecordIngressPerformance(string position, Observation observation, string kind, double elapsed)
        => RecordFlowPerformance(position, kind, observation.Id,
            observation.OperatorInput is null ? new(observation.DeviceId, observation.Epoch, observation.SourceEventId, observation.AdmissionId, observation.ObservedAt) : null,
            elapsed: elapsed, decodedTicks: observation.DecodedTicks);
    object FlowPerformance()
    {
        lock (performanceSync) return new { Window = PerformanceWindow, Sequence = performanceSequence,
            Counts = performanceCounts.ToDictionary(p => p.Key, p => new Dictionary<string, long>(p.Value)),
            Recent = performanceEvents.ToArray(), Boundary = "Observed input / persisted application result; not physical parcel completion" };
    }
}

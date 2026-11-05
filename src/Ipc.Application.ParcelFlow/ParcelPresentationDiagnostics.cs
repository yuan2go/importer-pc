using System.Diagnostics;
using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime : IParcelPresentationDiagnostics
{
    readonly object presentationGate = new();
    readonly Dictionary<string, ParcelReadFeedback> presentationFeedback = [];
    readonly Queue<ParcelReadFeedback> presentationReads = [];
    readonly HashSet<string> continuedPresentationReads = [];
    readonly HashSet<string> classifiedPresentationReads = [];
    readonly Dictionary<string, ObservationReadFeedback> presentationReadFrames = [];
    ParcelInputSnapshot[] presentationSnapshotInputs = [];
    readonly Queue<ParcelPresentationMetric> presentationMetrics = [];
    long presentationMetricCount;
    void RecordPresentationRead(string position, Observation observation, bool operatorInput = false, string? retainedInput = null)
    {
        if (observation.DecodedTicks <= 0 || observation.DecodedTicks > Stopwatch.GetTimestamp()) return;
        var placementOperation = operatorInput ? null : ContinuingCameraPlacementOwner(position, observation);
        var continuing = placementOperation is not null;
        var feedback = new ParcelReadFeedback(runtimeId, position, observation.Id, observation.DeviceId, observation.Epoch,
            observation.Codes is { Length: 1 } codes && codes[0] is { Length: > 0 and <= 4096 } single && !single.Any(char.IsControl) ? single : null,
            observation.DecodedTicks, observation.ObservedAt)
        {
            OperatorInput = operatorInput,
            PlacementOperationId = placementOperation,
            RetainedObservationId = retainedInput,
            CandidateCount = observation.Codes?.Length ?? 0,
            SourceSequence = observation.Sequence,
            SourceEventId = observation.SourceEventId,
            PendingDisplayBudgetMs = (int)Math.Clamp(2L * definition.QueuedObservationTimeoutMs(Effective), 1000, 120000)
        };
        lock (presentationGate)
        {
            if (presentationReads.Any(r => r.ObservationId == feedback.ObservationId && r.DeviceId == feedback.DeviceId && r.Epoch == feedback.Epoch)) return;
            var latest = !presentationFeedback.TryGetValue(position, out var previous) || previous.DetectedTicks <= feedback.DetectedTicks;
            if (latest)
            {
                // Changed code/epoch or an unclassified new read breaks the chain.
                // A later return to the old code cannot revive its old destination.
                if (continuing && previous is { OperatorInput: false } && previous.DeviceId == feedback.DeviceId &&
                    previous.Epoch == feedback.Epoch && previous.SingleCode == feedback.SingleCode &&
                    (continuedPresentationReads.Contains(previous.ObservationId) || classifiedPresentationReads.Contains(previous.ObservationId)))
                    continuedPresentationReads.Add(feedback.ObservationId);
                presentationFeedback[position] = feedback;
                MatchPendingCameraCodes(position, feedback);
                if (cameraReadFeedback.TryGetValue(position, out var camera) && camera.Frame.CompletedTicks == feedback.DetectedTicks &&
                    camera.Frame.DeviceId == feedback.DeviceId && camera.Frame.Epoch == feedback.Epoch)
                    presentationReadFrames[feedback.ObservationId] = camera.Frame;
            }
            if (presentationReads.Count == 512)
            {
                var retired = presentationReads.Dequeue();
                continuedPresentationReads.Remove(retired.ObservationId); classifiedPresentationReads.Remove(retired.ObservationId);
                presentationReadFrames.Remove(retired.ObservationId);
            }
            presentationReads.Enqueue(feedback);
            if (latest) OfferPresentation();
        }
    }
    public Task<ParcelReadFeedback[]> ReadPresentationFeedbackAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        lock (presentationGate) return Task.FromResult(stopped || stopping.IsCancellationRequested ? [] : ProjectedReads(presentationSnapshotInputs));
    }
    // Live reads can lead the durable input snapshot. Repeated observations of
    // a confirmed placement travel with its classified input. New/expired reads
    // and no-code facts keep their immediate invalidation behaviour.
    ParcelReadFeedback[] ProjectedReads(ParcelInputSnapshot[] inputs) => presentationFeedback.Values.Select(read =>
        continuedPresentationReads.Contains(read.ObservationId) && Stopwatch.GetElapsedTime(read.DetectedTicks) <
            TimeSpan.FromMilliseconds(read.PendingDisplayBudgetMs) && inputs.FirstOrDefault(i => i.PositionId == read.PositionId) is { } input
            ? presentationReads.LastOrDefault(r => r.PositionId == read.PositionId && r.ObservationId == input.Observation.Id &&
                r.DeviceId == read.DeviceId && r.Epoch == read.Epoch) ?? read : read).ToArray();
    ParcelCameraReadFeedback[] ProjectedCameraReads(ParcelReadFeedback[] reads) => cameraReadFeedback.Values.Select(camera =>
        camera.Frame.Outcome == ObservationReadOutcome.Codes && presentationFeedback.TryGetValue(camera.PositionId, out var latest) &&
        continuedPresentationReads.Contains(latest.ObservationId) && reads.FirstOrDefault(r => r.PositionId == camera.PositionId) is { } read &&
        presentationReadFrames.TryGetValue(read.ObservationId, out var frame) && frame.Epoch == camera.Frame.Epoch
            ? new ParcelCameraReadFeedback(camera.PositionId, frame) : camera).ToArray();
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
            presentationMetricCount++;
        }
        return Task.CompletedTask;
    }
    object PresentationProgress()
    {
        lock (presentationGate) return new
        {
            Availability = presentationMetrics.Count == 0 ? "NotMeasured" : "SamplesRecorded",
            Boundary = "Same-process decode completion to DWM composition fence; not camera exposure or photon timing",
            Recent = presentationMetrics.ToArray(),
            Total = presentationMetricCount,
            Window = 512,
            Subscribers = presentationSubscribers.Count,
            Publisher = presentationPublisher?.Status.ToString() ?? "NotStarted",
            Failure = presentationFailure
        };
    }
}

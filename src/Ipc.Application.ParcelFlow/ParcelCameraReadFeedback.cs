using System.Diagnostics;
using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime
{
    readonly Dictionary<string, ParcelCameraReadFeedback> cameraReadFeedback = [];
    readonly Dictionary<string, ObservationReadFeedback> pendingCameraCodes = [];
    readonly Dictionary<string, (string Signature, long Ticks, int Count, ObservationReadFeedback? Last)> cameraLogWindows = [];
    void LogCameraFrame(string position, ObservationReadFeedback frame, int count, string? retainedOperation)
    {
        var outcome = frame.Outcome == ObservationReadOutcome.Codes ? "识码成功" : frame.Outcome == ObservationReadOutcome.NoCode ? "图像已识别，未读到条码" : "图像识码失败";
        Diagnostic("相机识码", outcome, frame.Outcome is ObservationReadOutcome.Codes or ObservationReadOutcome.NoCode ? "信息" : "警告",
            Wire.Encode(new { PositionId = position, frame.DeviceId, frame.Epoch, frame.FrameNumber, frame.SourceEventId, frame.ObservedAt, frame.Outcome, frame.CandidateCount,
                RawCandidateCount = frame.Codes?.Length ?? frame.CandidateCount, Candidates = frame.Codes, frame.Formats,
                Selection = frame.Codes is { } codes ? BarcodeFiltering.Select(Effective.BarcodeFilter, codes) : null,
                frame.Scene, frame.FailureReason, FramesSinceLastLog = count, RetainedOperationId = retainedOperation }));
    }
    readonly List<(IObservationReadFeedbackSource Source, Action<ObservationReadFeedback> Handler)> cameraReadSubscriptions = [];
    void RegisterCameraReadFeedback(ObservationSession session)
    {
        if (Id != "sportmaster" || session.Source is not IObservationReadFeedbackSource source) return;
        Action<ObservationReadFeedback> handler = frame =>
        {
            // The registered source owns this callback. Old epochs/ticks cannot
            // invalidate a newer observation, and decode facts never enter the DB.
            if (Volatile.Read(ref accepting) == 0 || frame.DeviceId != session.Source.DeviceId || frame.Epoch != session.Epoch ||
                frame.CompletedTicks <= 0 || frame.CompletedTicks > Stopwatch.GetTimestamp() || frame.CandidateCount is < 0 or > BarcodeFiltering.MaximumCandidates ||
                frame.Outcome == ObservationReadOutcome.Codes != (frame.CandidateCount > 0) || !Enum.IsDefined(frame.Outcome)) return;
            var position = positions.Values.SingleOrDefault(p => p.ReaderDeviceId == frame.DeviceId)?.Id;
            if (position is null) return;
            if (frame.Outcome == ObservationReadOutcome.Codes && frame.Codes is { } codes)
            {
                if (codes.Length != frame.CandidateCount) return;
                var selected = BarcodeFiltering.Select(Effective.BarcodeFilter ?? new BarcodeFilterPolicy(""), codes);
                var eligible = selected.Candidates?.Count(c => c.Outcome == "Eligible") ?? selected.Matches;
                frame = frame with { CandidateCount = eligible, Outcome = eligible == 0 ? ObservationReadOutcome.NoCode : ObservationReadOutcome.Codes };
            }
            // Decode feedback is not a qualified tray-presence classifier.
            string? retainedOperation = null;
            string? retainedInput = null;
            lock (cameraLogWindows)
            {
                var signature = Wire.Hash(Wire.Encode(new { frame.Epoch, frame.Outcome, frame.CandidateCount, frame.FailureReason,
                    Candidates = frame.Codes?.Select((code, index) => Wire.Encode(new { Code = code, Format = frame.Formats?.ElementAtOrDefault(index) })).Order(StringComparer.Ordinal).ToArray(),
                    Policy = Effective.BarcodeFilter }));
                cameraLogWindows.TryGetValue(position, out var prior);
                if (prior.Signature != signature && prior.Count > 0 && prior.Last is { } last)
                    LogCameraFrame(position, last, prior.Count, null);
                var count = prior.Signature == signature ? prior.Count + 1 : 1;
                if (prior.Ticks == 0 || prior.Signature != signature || Stopwatch.GetElapsedTime(prior.Ticks) >= TimeSpan.FromSeconds(5))
                {
                    LogCameraFrame(position, frame, count, retainedOperation);
                    cameraLogWindows[position] = (signature, Stopwatch.GetTimestamp(), 0, frame);
                }
                else cameraLogWindows[position] = (signature, prior.Ticks, count, frame);
            }
            lock (presentationGate)
            {
                cameraReadFeedback.TryGetValue(position, out var old);
                var latest = pendingCameraCodes.GetValueOrDefault(position) ?? old?.Frame;
                if (latest is not null && (latest.CompletedTicks >= frame.CompletedTicks ||
                    latest.Epoch == frame.Epoch && latest.FrameNumber >= frame.FrameNumber)) return;
                // Codes arrives before the synchronous observation receiver. Retain
                // the previous no-code watermark until the matching observation cue
                // can be published in the same presentation envelope.
                if (frame.Outcome == ObservationReadOutcome.Codes &&
                    (!presentationFeedback.TryGetValue(position, out var read) || read.OperatorInput ||
                     read.DeviceId != frame.DeviceId || read.Epoch != frame.Epoch || read.DetectedTicks != frame.CompletedTicks))
                {
                    pendingCameraCodes[position] = frame;
                    return;
                }
                pendingCameraCodes.Remove(position);
                var changed = old is null || old.Frame.Epoch != frame.Epoch || old.Frame.Outcome != frame.Outcome ||
                    old.RetainedOperationId != retainedOperation || old.RetainedObservationId != retainedInput || frame.Outcome == ObservationReadOutcome.Codes;
                cameraReadFeedback[position] = new(position, frame) { RetainedOperationId = retainedOperation, RetainedObservationId = retainedInput };
                if (changed) OfferPresentation();
            }
        };
        source.ReadFeedback += handler;
        cameraReadSubscriptions.Add((source, handler));
    }
    void MatchPendingCameraCodes(string position, ParcelReadFeedback read)
    {
        if (read.OperatorInput || !pendingCameraCodes.TryGetValue(position, out var frame) ||
            read.DeviceId != frame.DeviceId || read.Epoch != frame.Epoch || read.DetectedTicks != frame.CompletedTicks ||
            read.CandidateCount != frame.CandidateCount) return;
        cameraReadFeedback[position] = new(position, frame);
        pendingCameraCodes.Remove(position);
    }
    void UnregisterCameraReadFeedback(IControlledObservationSource replaced)
    {
        foreach (var subscription in cameraReadSubscriptions.Where(x => ReferenceEquals(x.Source, replaced)).ToArray())
        { subscription.Source.ReadFeedback -= subscription.Handler; cameraReadSubscriptions.Remove(subscription); }
    }
    void ReleaseCameraReadFeedback()
    {
        foreach (var (source, handler) in cameraReadSubscriptions) source.ReadFeedback -= handler;
        cameraReadSubscriptions.Clear();
    }
    ParcelSnapshot WithReadFeedbackSnapshot(ParcelSnapshot snapshot)
    {
        // A snapshot can reach a window before its stream notification. Publish
        // frame and observation watermarks from the same short critical section.
        lock (presentationGate)
        {
            foreach (var input in snapshot.Inputs ?? [])
                if (presentationReads.Any(r => r.ObservationId == input.Observation.Id)) classifiedPresentationReads.Add(input.Observation.Id);
            var reads = ProjectedReads(snapshot.Inputs ?? []);
            return snapshot with { CameraReads = ProjectedCameraReads(reads), ReadFeedbacks = reads };
        }
    }
}

using System.Diagnostics;
using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime
{
    readonly Dictionary<string, ParcelCameraReadFeedback> cameraReadFeedback = [];
    readonly Dictionary<string, ObservationReadFeedback> pendingCameraCodes = [];
    readonly List<(IObservationReadFeedbackSource Source, Action<ObservationReadFeedback> Handler)> cameraReadSubscriptions = [];
    void RegisterCameraReadFeedback(ObservationSession session)
    {
        if (Id != "sportmaster" || session.Source is not IObservationReadFeedbackSource source) return;
        Action<ObservationReadFeedback> handler = frame =>
        {
            // The registered source owns this callback. Old epochs/ticks cannot
            // invalidate a newer observation, and decode facts never enter the DB.
            if (Volatile.Read(ref accepting) == 0 || frame.DeviceId != session.Source.DeviceId || frame.Epoch != session.Epoch ||
                frame.CompletedTicks <= 0 || frame.CompletedTicks > Stopwatch.GetTimestamp() || frame.CandidateCount is < 0 or >= 64 ||
                frame.Outcome == ObservationReadOutcome.Codes != (frame.CandidateCount > 0) || !Enum.IsDefined(frame.Outcome)) return;
            var position = positions.Values.SingleOrDefault(p => p.ReaderDeviceId == frame.DeviceId)?.Id;
            if (position is null) return;
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
                var changed = old is null || old.Frame.Epoch != frame.Epoch || old.Frame.Outcome != frame.Outcome || frame.Outcome == ObservationReadOutcome.Codes;
                cameraReadFeedback[position] = new(position, frame);
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
    void ReleaseCameraReadFeedback()
    {
        foreach (var (source, handler) in cameraReadSubscriptions) source.ReadFeedback -= handler;
        cameraReadSubscriptions.Clear();
    }
    ParcelCameraReadFeedback[] CameraReadFeedbackSnapshot()
    {
        lock (presentationGate) return cameraReadFeedback.Values.ToArray();
    }
}

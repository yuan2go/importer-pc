using System.Diagnostics;
using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime
{
    readonly Dictionary<string, ParcelCameraReadFeedback> cameraReadFeedback = [];
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
                if (cameraReadFeedback.TryGetValue(position, out var old) &&
                    (old.Frame.CompletedTicks >= frame.CompletedTicks || old.Frame.Epoch == frame.Epoch && old.Frame.FrameNumber >= frame.FrameNumber)) return;
                var changed = old is null || old.Frame.Epoch != frame.Epoch || old.Frame.Outcome != frame.Outcome || frame.Outcome == ObservationReadOutcome.Codes;
                cameraReadFeedback[position] = new(position, frame);
                if (changed) OfferPresentation();
            }
        };
        source.ReadFeedback += handler;
        cameraReadSubscriptions.Add((source, handler));
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

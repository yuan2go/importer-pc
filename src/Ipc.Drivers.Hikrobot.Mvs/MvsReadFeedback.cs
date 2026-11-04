using Ipc.Platform;

namespace Ipc.Drivers.Hikrobot.Mvs;

internal sealed partial class MvsAcquisition
#if !MVS_ACQUISITION_EXTENSION
    : IObservationReadFeedbackSource
#endif
{
#if !MVS_ACQUISITION_EXTENSION
    public event Action<ObservationReadFeedback>? ReadFeedback;
#endif
    void PublishReadFeedback(uint number, DateTimeOffset observedAt, long completedTicks, int count, bool rejected = false, long? scene = null, string? failureReason = null)
    {
#if !MVS_ACQUISITION_EXTENSION
        if (Recognizes)
            ReadFeedback?.Invoke(new(DeviceId, Epoch, number, FramePrefix + ".frame." + number, observedAt, completedTicks, count,
                rejected ? ObservationReadOutcome.RejectedFrame : count == 0 ? ObservationReadOutcome.NoCode : ObservationReadOutcome.Codes) { Scene = scene, FailureReason = failureReason });
#endif
    }
}

namespace Ipc.Platform;

// Ephemeral decode facts only. No entity detection, admission, quantity or device health.
public enum ObservationReadOutcome { Codes, NoCode, RejectedFrame }
public sealed record ObservationReadFeedback(string DeviceId, string Epoch, uint FrameNumber, string SourceEventId,
    DateTimeOffset ObservedAt, long CompletedTicks, int CandidateCount, ObservationReadOutcome Outcome);
public interface IObservationReadFeedbackSource
{
    event Action<ObservationReadFeedback>? ReadFeedback;
}

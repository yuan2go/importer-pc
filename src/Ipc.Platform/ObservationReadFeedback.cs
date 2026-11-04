namespace Ipc.Platform;

// Ephemeral decode facts only. No entity detection, admission, quantity or device health.
public enum ObservationReadOutcome { Codes, NoCode, RejectedFrame }
public sealed record ObservationReadFeedback(string DeviceId, string Epoch, uint FrameNumber, string SourceEventId,
    DateTimeOffset ObservedAt, long CompletedTicks, int CandidateCount, ObservationReadOutcome Outcome)
{
    // Scene identity from the camera's stillness check: frames with the same value show the same unmoved
    // scene. Null when the source does not compare scenes; then a no-code frame is judged on its own.
    public long? Scene { get; init; }
    public string? FailureReason { get; init; }
}
public interface IObservationReadFeedbackSource
{
    event Action<ObservationReadFeedback>? ReadFeedback;
}

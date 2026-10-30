namespace Ipc.Platform;

// Passive, bounded counters from the acquisition owner; never probes a device or grants admission.
public interface IObservationDiagnosticsSource
{
    ObservationDiagnostics ReadDiagnostics();
}
public sealed record ObservationDiagnostics(string DeviceId, string Epoch, string Phase, string State,
    long AcquireCalls, long NoData, long Frames, long DecodeStarted, long DecodeCompleted,
    long EmptyFrames, long Candidates, long Delivered, long Rejected, bool DecodePaused,
    DateTimeOffset? LastFrameAt, double? LastFrameAgeMs, DateTimeOffset? LastDecodeAt, string? Failure);

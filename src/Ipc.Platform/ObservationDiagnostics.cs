namespace Ipc.Platform;

// Passive, bounded counters from the acquisition owner; never probes a device or grants admission.
public interface IObservationDiagnosticsSource
{
    ObservationDiagnostics ReadDiagnostics();
}

// The supervisor samples these bounded traces separately from frequently read UI counters.
public interface IObservationPerformanceSource
{
    object ReadPerformance();
}
public sealed record ObservationDiagnostics(string DeviceId, string Epoch, string Phase, string State,
    long AcquireCalls, long NoData, long Frames, long DecodeStarted, long DecodeCompleted,
    long EmptyFrames, long Candidates, long Delivered, long Rejected, bool DecodePaused,
    DateTimeOffset? LastFrameAt, double? LastFrameAgeMs, DateTimeOffset? LastDecodeAt, string? Failure)
{
    public DecodeWindowSnapshot? Window { get; init; }
    public long SupersededDecodeFrames { get; init; }
    public long RejectedFrames { get; init; }
    public string? LastFrameRejection { get; init; }
    public long OriginalFrameCopies { get; init; }
    public bool WaitingForOriginalCapacity { get; init; }
    public long SdkFrameNumberGaps { get; init; }
    public long SdkGapEvents { get; init; }
}

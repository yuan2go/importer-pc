namespace Ipc.Application.Contracts;

// Ephemeral, same-process presentation measurements; no business command or admission authority.
public interface IParcelPresentationDiagnostics
{
    Task<ParcelReadFeedback[]> ReadPresentationFeedbackAsync(CancellationToken token);
    Task ReportPresentationAsync(Guid runtime, string observation, string stage, long composedTicks, long snapshotTicks, CancellationToken token);
}
public sealed record ParcelReadFeedback(Guid RuntimeId, string PositionId, string ObservationId, string DeviceId, string Epoch,
    string? SingleCode, long DetectedTicks, DateTimeOffset ObservedAt)
{
    public bool OperatorInput { get; init; }
    // Application-confirmed existing placement; display only, never admission authority.
    public string? PlacementOperationId { get; init; }
    // Bounded display metadata only; neither selection nor admission authority.
    public int CandidateCount { get; init; } = SingleCode is null ? 0 : 1;
    public long SourceSequence { get; init; }
    public string? SourceEventId { get; init; }
    // Same-process display deadline only. Expiry grants no business/retry authority.
    public int PendingDisplayBudgetMs { get; init; } = 10000;
}
public sealed record ParcelPresentationMetric(string ObservationId, string PositionId, string Stage, double ReadToCompositionMs,
    double? SnapshotToCompositionMs, DateTimeOffset At);

public sealed record ParcelCameraReadFeedback(string PositionId, Ipc.Platform.ObservationReadFeedback Frame)
{
    // Application-owned placement identity, only for an unchanged scene after a missed/rejected decode.
    public string? RetainedOperationId { get; init; }
}

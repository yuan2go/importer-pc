using Ipc.Platform;
namespace Ipc.Application.Contracts;

public enum SmRoundState { Reading, ReadException, Submitted, Result, AwaitingRemoval, Ended }
public sealed record SmParcelRound(string Id, string PositionId, long Version, Guid ClockRuntime,
    DateTimeOffset StartedAt, long StartedTicks, int ReadTimeoutMs, long ConfigurationRevision,
    BarcodeFilterPolicy? Filter, SmRoundState State, string[] FormalCodes)
{
    public string? SelectedCode { get; init; }
    public Observation? Observation { get; init; }
    public string? OperationId { get; init; }
    public string? CommandId { get; init; }
    public bool Manual { get; init; }
    public bool AttemptStarted { get; init; }
    public bool AutoExpired { get; init; }
    public bool RequireRemoval { get; init; }
    public DateTimeOffset? SuccessAt { get; init; }
    public long? SuccessTicks { get; init; }
    public DateTimeOffset? EndedAt { get; init; }
    public string? EndReason { get; init; }
    public string? ReadOutcome { get; init; }
    public string? PresenceEvidence { get; init; }
    public string? RequestId { get; init; }
    public long? ProtocolRequestId { get; init; }
    public OriginalFrameArchive? OriginalFrame { get; init; }
    public string? RcsOutcome { get; init; }
}
public sealed record SmPositionCycle(string PositionId, bool AutoSuppressed, TrayPresence Presence,
    string? Epoch, long Sequence, string Generation, string? Evidence)
{
    public DateTimeOffset? ClearedAt { get; init; }
}
public sealed record SmRoundHistoryPage(SmParcelRound[] Items);

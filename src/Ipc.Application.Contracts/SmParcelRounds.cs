using Ipc.Platform;
namespace Ipc.Application.Contracts;

public enum SmRoundState { Reading, ReadException, Submitted, Result, AwaitingRemoval, Ended }
[Flags]
public enum SmRemoteRecoveryCause { None = 0, RequestMayBind = 1, MembershipMayApply = 2 }
public sealed record SmParcelRound(string Id, string PositionId, long Version, Guid ClockRuntime,
    DateTimeOffset StartedAt, long StartedTicks, int ReadTimeoutMs, long ConfigurationRevision,
    BarcodeFilterPolicy? Filter, SmRoundState State, string[] FormalCodes)
{
    public string? SelectedCode { get; init; }
    public Observation? Observation { get; init; }
    public string? OperationId { get; init; }
    public string? CommandId { get; init; }
    // Establishment basis, frozen when the round starts. The submitted request's
    // OperatorInput identifies keyboard/PDA supplementation independently.
    public bool Manual { get; init; }
    public bool AttemptStarted { get; init; }
    public bool AutoExpired { get; init; }
    public bool RequireRemoval { get; init; }
    public DateTimeOffset? SuccessAt { get; init; }
    public long? SuccessTicks { get; init; }
    public Guid? SuccessClockRuntime { get; init; }
    public DateTimeOffset? EndedAt { get; init; }
    public string? EndReason { get; init; }
    public string? ReadOutcome { get; init; }
    public string? PresenceEvidence { get; init; }
    public string? RequestId { get; init; }
    public long? ProtocolRequestId { get; init; }
    public OriginalFrameArchive? OriginalFrame { get; init; }
    public string? RcsOutcome { get; init; }
    public string? ArrivalGeneration { get; init; }
    public string? VehicleNo { get; init; }
    public long CaptureBoundaryTicks { get; init; }
}
public sealed record SmPositionCycle(string PositionId, bool AutoSuppressed, TrayPresence Presence,
    string? Epoch, long Sequence, string Generation, string? Evidence)
{
    [System.Text.Json.Serialization.JsonRequired]
    public string RecoveryToken { get; init; } = Wire.Id();
    public DateTimeOffset? ClearedAt { get; init; }
    public string? VehicleNo { get; init; }
    public string? ArrivalEvidence { get; init; }
    public DateTimeOffset? ArrivalAt { get; init; }
    public string? SubmittedOperation { get; init; }
    public bool SubmissionAttempted { get; init; }
    public string? TargetOperation { get; init; }
    public bool Withdrawn { get; init; }
    public long? HeartbeatPending { get; init; }
    public bool HeartbeatIncluded { get; init; }
    public string? HeartbeatConfiguration { get; init; }
    public bool HeartbeatMaintenanceLease { get; init; }
    public bool? UncertainMembership { get; init; }
    public SmRemoteRecoveryCause RecoveryCause { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string? RecoveryReason => RecoveryCause.HasFlag(SmRemoteRecoveryCause.MembershipMayApply)
        ? "旧心跳成员仍可能迟到生效；请停用相关位，维护核对客户RCS调度状态后恢复。"
        : RecoveryCause.HasFlag(SmRemoteRecoveryCause.RequestMayBind)
            ? "原取格口请求仍可能迟到绑定目标；请停用相关位，维护核对客户RCS原请求后恢复。" : null;
}
public sealed record SmRoundHistoryPage(SmParcelRound[] Items);

using Ipc.Platform;
namespace Ipc.Application.Contracts;

public sealed record SmHeartbeatMember(string PositionId, string StationCode, string BcrCode, long ControlRevision);
public sealed record SmHeartbeatIntent(long RequestId, string Configuration, SmHeartbeatMember[] Members)
{
    public Dictionary<string, long> Controls { get; init; } = [];
    public bool MaintenanceLease { get; init; }
}
public sealed record SmHeartbeatConfirmation(bool HttpResponded, bool ProtocolConfirmed, StationVehicleState[] Vehicles,
    DateTimeOffset ReceivedAt, DateTimeOffset ValidUntil);
// The Application journals the exact intent before HTTP, without holding its
// business gate across I/O. A timeout is not remote cancellation evidence.
public interface ISmHeartbeatOwner
{
    Task<SmHeartbeatIntent?> BeginHeartbeatAsync(long requestId, CancellationToken token);
    Task CompleteHeartbeatAsync(SmHeartbeatIntent intent, SmHeartbeatConfirmation confirmation);
}
public interface ISmHeartbeatControl
{
    void SetHeartbeatOwner(ISmHeartbeatOwner owner);
    void RefreshHeartbeat();
}
public sealed record SmArrivalSnapshot(string? VehicleNo, string Generation, bool Confirmed, bool Submitted,
    bool CameraBoundaryReady, string? RecoveryReason, string? TargetOperation)
{
    public string? CaptureFailure { get; init; }
    public bool SubmissionAttempted { get; init; }
    public bool PlacementClosed { get; init; }
    public string? RecoveryToken { get; init; }
}
public sealed record ConfirmSmRemoteRecovery(string CommandId, Guid RuntimeId, string PositionId,
    string ExpectedGeneration, string ExternalEvidence)
{
    public long ExpectedControlRevision { get; init; }
    public string? ExpectedTargetOperation { get; init; }
    public string? ExpectedRecoveryToken { get; init; }
}
public interface ISmRemoteRecovery
{
    Task<BusinessReceipt> ConfirmRemoteRecoveryAsync(ConfirmSmRemoteRecovery command, CancellationToken token);
}

using Ipc.Platform;
namespace Ipc.Application.Contracts;

public sealed record SmArrivalSnapshot(string? VehicleNo, string Generation, bool Confirmed, bool Submitted,
    bool CameraBoundaryReady, string? RecoveryReason, string? TargetOperation)
{
    public string? CaptureFailure { get; init; }
    public bool SubmissionAttempted { get; init; }
    public bool PlacementClosed { get; init; }
    public string? PlacementBoundary { get; init; }
    public string? RecoveryToken { get; init; }
    public bool Configured { get; init; } = true;
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

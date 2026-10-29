namespace Ipc.Application.Contracts;

public sealed record ArmCameraRead(string CommandId, Guid RuntimeId, long ConfigurationRevision, string SessionId,
    string DeviceId, string? ExpectedRoundId);
public sealed record SendCameraRound(string CommandId, Guid RuntimeId, string RoundId, string RequestHash);
public sealed record CameraDebugRequestEvidence(string SessionId, string RoundId, string FrameId, string DeviceId, string Epoch,
    string SourceEventId, string OriginalSha256, string ManagementActor);
public sealed record CameraReadRound(string? Id, string? SessionId, string? DeviceId, string State, string Detail,
    CameraFrameMetadata? Frame, string? Barcode, string? OperationId, string? RequestId, string? RequestBody,
    string? RequestHash, string? Target, string? Result, bool CanSend, long Revision);
public interface ICameraReadRounds
{
    Task<CameraReadRound> ArmCameraReadAsync(ArmCameraRead command, CancellationToken token);
    Task<CameraReadRound> ReadCameraReadRoundAsync(CancellationToken token);
    Task<CameraReadRound> SendCameraRoundAsync(SendCameraRound command, CancellationToken token);
}

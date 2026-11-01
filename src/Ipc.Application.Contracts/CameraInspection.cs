using Ipc.Platform;
namespace Ipc.Application.Contracts;

public enum CameraInspectionMode { ObserveProduction, Isolated }
public sealed record CameraInspectionLimits(int DurationMinutes = 10, int ImageMiB = 1024, int Records = 200000);
public sealed record BeginCameraInspection(string CommandId, Guid RuntimeId, long ExpectedConfigurationRevision, CameraInspectionMode Mode,
    string Reason, CameraInspectionLimits Limits, bool PhysicalSceneConfirmed = false)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool SingleReadRounds { get; init; }
}
public sealed record EndCameraInspection(string CommandId, string SessionId, bool TestPiecesRemoved, bool ResumeProduction, string Reason)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool CloseReadRounds { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? ExpectedRoundId { get; init; }
}
public sealed record CameraInspectionStatus(string? SessionId, CameraInspectionMode? Mode, string State, bool BusinessFenced,
    DateTimeOffset? ExpiresAt, long Frames, long ImageBytes, long EvictedImages, long MissingFrames, string Detail, string ConfigurationHash)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool SingleReadRounds { get; init; }
}
public sealed record CameraFrameMetadata(string Id, string SessionId, string DeviceId, string Epoch, long FrameNumber,
    DateTimeOffset CapturedAt, int Width, int Height, RecognitionSymbol[] Symbols, double DecodeMilliseconds,
    string? ObservationId, string? SourceEventId, string? IsolatedSessionId, string OriginalSha256, bool ImageAvailable, string ImageState,
    long Sequence = 0, long BusinessSequence = 0, string? OperationId = null, long VideoFramesSkippedBefore = 0,
    string? PhysicalSerial = null, string? DeviceAddress = null);
public sealed record CameraFramePage(CameraFrameMetadata[] Rows, long Cursor, long HighWater, bool HasMore);
public sealed record CameraOriginalImage(CameraFrameMetadata Metadata, byte[] Gray8);
public sealed record CameraBusinessRow(long Sequence, string Position, string DeviceId, string ObservationId, string Epoch,
    string SourceEventId, DateTimeOffset ObservedAt, string[] Candidates, int CandidateCount, string Outcome,
    string Reason, string? OperationId, string? InspectionSessionId, string? SelectedCode);
public sealed record CameraBusinessPage(CameraBusinessRow[] Rows, long Cursor, long HighWater, bool HasMore, string Coverage, long FirstSequence = 0, long LastSequence = 0);
public sealed record CameraOperationStatus(string Id, string State, string? Slot, string? RequestId, string? StageState,
    int Attempts, bool BusinessAccepted, string? Reason, long Revision);
public sealed record CameraOperationDetail(CameraOperationStatus Status, ParcelOperation Original);
public sealed record CameraInspectionExport(string Path, string Sha256, long Bytes, long Frames, long MissingImages);
public sealed record CameraInspectionSession(string Id, CameraInspectionMode Mode, string State, DateTimeOffset ExpiresAt, long Frames)
{ public string Label => Id[..Math.Min(12, Id.Length)] + " · " + (Mode == CameraInspectionMode.Isolated ? "实机隔离" : "生产观察") + " · " + State + " · " + Frames + "帧"; }

public interface ICameraInspection : ICameraDiagnostics
{
    Task<CameraInspectionStatus> CameraInspectionStatusAsync(CancellationToken token);
    Task<CameraInspectionStatus> BeginCameraInspectionAsync(BeginCameraInspection command, CancellationToken token);
    Task<CameraInspectionStatus> EndCameraInspectionAsync(EndCameraInspection command, CancellationToken token);
    Task<CameraInspectionStatus> StopCameraInspectionCaptureAsync(string sessionId, string reason, CancellationToken token);
    Task<CameraInspectionSession[]> ListCameraInspectionSessionsAsync(CancellationToken token);
    Task<CameraFramePage> ReadCameraFramesAsync(string sessionId, string deviceId, long after, long? before, int limit, CancellationToken token);
    Task<CameraOriginalImage?> ReadCameraOriginalAsync(string frameId, CancellationToken token);
    Task<CameraFrameMetadata?> FindObservationFrameAsync(string observationId, CancellationToken token);
    Task<CameraBusinessPage> ReadCameraBusinessAsync(string deviceId, long after, long? before, int limit, CancellationToken token);
    Task<ParcelInputSnapshot?> ReadCameraBusinessDetailAsync(long sequence, CancellationToken token);
    Task<CameraOperationStatus[]> ReadCameraOperationsAsync(string[] operationIds, CancellationToken token);
    Task<CameraOperationDetail?> ReadCameraOperationDetailAsync(string operationId, CancellationToken token);
    Task<CameraInspectionExport> ExportCameraInspectionAsync(string sessionId, string destination, CancellationToken token);
    Task<CameraInspectionExport> ExportCameraOriginalAsync(string frameId, string destination, CancellationToken token);
    Task PurgeCameraInspectionAsync(string sessionId, string reason, CancellationToken token);
}

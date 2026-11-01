using Ipc.Platform;
namespace Ipc.Application.Contracts;

public interface ICameraDiagnostics
{
    Task<CameraPreviewResult> ReadCameraPreviewAsync(string deviceId, CancellationToken token);
}
public sealed record CameraPreviewResult(string Status, ObservationPreview? Frame)
{
    public ObservationDiagnostics? Diagnostics { get; init; }
}
public interface ICameraPreviewLeaseDiagnostics
{
    Task<CameraPreviewLeaseResult> ReadCameraPreviewLeaseAsync(string deviceId, CancellationToken token);
}
public sealed record CameraPreviewLeaseResult(string Status, ObservationPreviewLease? Lease) : IDisposable
{
    public ObservationPreview? Frame => Lease?.Frame;
    public ObservationDiagnostics? Diagnostics { get; init; }
    public void Dispose() => Lease?.Dispose();
}

using Ipc.Platform;
namespace Ipc.Application.Contracts;

public interface ICameraDiagnostics
{
    Task<CameraPreviewResult> ReadCameraPreviewAsync(string deviceId, CancellationToken token);
}
public sealed record CameraPreviewResult(string Status, ObservationPreview? Frame);

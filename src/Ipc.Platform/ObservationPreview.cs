namespace Ipc.Platform;

// Passive view of the existing acquisition owner. No native buffer is exposed.
public interface IObservationPreviewSource
{
    ObservationPreview? ReadPreview();
}
public sealed record ObservationPreview(string DeviceId, string Epoch, long FrameNumber,
    DateTimeOffset CapturedAt, int Width, int Height, byte[] Gray8);

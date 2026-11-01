namespace Ipc.Platform;

// Passive view of the existing acquisition owner. No native buffer is exposed.
public interface IObservationPreviewSource
{
    ObservationPreview? ReadPreview();
}
public sealed record ObservationPreview(string DeviceId, string Epoch, long FrameNumber,
    DateTimeOffset CapturedAt, int Width, int Height, byte[] Gray8);

public interface IObservationPreviewLeaseSource
{
    ObservationPreviewLease? ReadPreviewLease();
}
// Gray8 can be larger than Width*Height. Only that prefix is valid. Consumers must
// finish copying before Dispose and cannot retain/use the borrowed pixels afterward.
public sealed class ObservationPreviewLease(ObservationPreview frame, Action release) : IDisposable
{
    Action? ownedRelease = release;
    public ObservationPreview Frame => Volatile.Read(ref ownedRelease) is not null ? frame : throw new ObjectDisposedException(nameof(ObservationPreviewLease));
    public void Dispose() => Interlocked.Exchange(ref ownedRelease, null)?.Invoke();
}

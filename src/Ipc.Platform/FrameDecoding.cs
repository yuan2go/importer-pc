namespace Ipc.Platform;

public enum FramePixelFormat { Mono8 }
public readonly record struct FrameView(ReadOnlyMemory<byte> Pixels, int Width, int Height, int Stride,
    FramePixelFormat PixelFormat, ulong FrameNumber, ulong DeviceTimestamp);
public readonly record struct CodeRegion(int X, int Y, int Width, int Height);
public sealed record DecodedCode(string Text, string Symbology, CodeRegion Region, double? Quality = null);

// Implementations must return promptly, honor cancellation, and release the view
// before completing. Native code without that guarantee belongs in a supervised
// process. Neither this port nor a successful self-test grants deployment approval.
public interface IFrameDecoder : IAsyncDisposable
{
    ValueTask<IReadOnlyList<DecodedCode>> DecodeAsync(FrameView frame, CancellationToken cancellationToken);
}

// Abort must fence further calls and release the actual decoder owner, not just
// cancel a wrapper Task. The runner retains the frame if this contract fails.
public interface IAbortableFrameDecoder : IFrameDecoder
{
    Task AbortAsync(CancellationToken cancellationToken);
}

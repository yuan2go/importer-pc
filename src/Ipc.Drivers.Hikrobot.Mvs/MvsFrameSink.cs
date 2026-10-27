namespace Ipc.Drivers.Hikrobot.Mvs;

// Borrowed native pixels are valid only during Copy. The sink must copy into its
// fixed pool synchronously and must not decode, log, wait or retain this pointer.
public readonly record struct MvsBorrowedFrame(nint Pixels, int Width, int Height, int Stride,
    int Length, ulong FrameNumber, ulong DeviceTimestamp, ulong Trigger);
public interface IMvsFrameSink
{
    IMvsFrameCopy? Copy(MvsBorrowedFrame frame, string registration);
}
public interface IMvsFrameCopy
{
    // A nonblocking ownership transfer into the sink's bounded queue.
    void Submit();
    // SDK Free failed: reject the copy and latch the service's input fault.
    void Reject();
}

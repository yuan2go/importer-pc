namespace Ipc.Platform;

// A lease over the exact raw Mono8 frame behind an observation. No new SDK
// connection, trigger, latest-frame lookup or filesystem work on the producer.
public interface IOriginalFrameSource
{
    void EnableOriginalFrames();
    IOriginalFrameLease RetainOriginalFrame(AcquisitionIdentity identity);
    void ReleaseOriginalFrame(AcquisitionIdentity identity);
}
public interface IOriginalFrameLease : IDisposable
{
    int Width { get; }
    int Height { get; }
    ReadOnlyMemory<byte> Pixels { get; }
}

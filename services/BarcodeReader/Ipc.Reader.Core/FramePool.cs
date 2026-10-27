using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Ipc.Reader.Core;

public readonly record struct CaptureFrame(nint Pixels, int Width, int Height, int Stride,
    FramePixelFormat PixelFormat, ulong FrameNumber, ulong DeviceTimestamp, ulong Trigger, int Length);

public sealed record BufferBudget(int Slots, int QueueCapacity, int MaximumPayload, int SdkNodes,
    long MaximumResidentBytes)
{
    // Two worker copies cover transport storage and the VM-owned image. SDK nodes
    // and one acquisition loan are reserved separately from the managed pool.
    public long ValidateAndCalculate()
    {
        if (Slots is < 3 or > 32 || QueueCapacity < 1 || QueueCapacity > Slots - 2 ||
            MaximumPayload is < 1 or > 33554432 || SdkNodes is < 1 or > 4 ||
            MaximumResidentBytes is < 1 or > 1073741824)
            throw new InvalidDataException("BufferConfigurationInvalid");
        long bytes = checked((long)(Slots + SdkNodes + 3) * MaximumPayload + 1048576);
        if (bytes > MaximumResidentBytes) throw new InvalidDataException("BufferMemoryBudgetExceeded");
        return bytes;
    }
}

public sealed class FramePool
{
    readonly ConcurrentQueue<byte[]> available = new();
    readonly int maximumPayload;
    int rented;
    public int InUse => Volatile.Read(ref rented);
    public FramePool(BufferBudget budget)
    {
        budget.ValidateAndCalculate(); maximumPayload = budget.MaximumPayload;
        for (var i = 0; i < budget.Slots; i++)
            available.Enqueue(GC.AllocateUninitializedArray<byte>(maximumPayload, pinned: true));
    }
    public FrameLease? TryCopy(CaptureFrame frame, string bootId, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (frame.Pixels == 0 || frame.Width < 1 || frame.Height < 1 || frame.Stride < frame.Width ||
            frame.PixelFormat != FramePixelFormat.Mono8 || frame.Length > maximumPayload ||
            (long)frame.Stride * frame.Height != frame.Length || string.IsNullOrWhiteSpace(bootId))
            throw new InvalidDataException("CaptureLayoutInvalid");
        if (!available.TryDequeue(out var bytes)) return null;
        Interlocked.Increment(ref rented);
        var lease = new FrameLease(this, bytes, frame, bootId);
        try
        {
            Marshal.Copy(frame.Pixels, bytes, 0, frame.Length);
            token.ThrowIfCancellationRequested(); return lease;
        }
        catch { lease.Dispose(); throw; }
    }
    void Return(byte[] bytes, int used)
    {
        bytes.AsSpan(0, used).Clear();
        Interlocked.Decrement(ref rented); available.Enqueue(bytes);
    }
    public sealed class FrameLease : IDisposable
    {
        readonly FramePool pool;
        readonly byte[] pixels;
        readonly CaptureFrame metadata;
        int returned;
        internal FrameLease(FramePool pool, byte[] pixels, CaptureFrame metadata, string bootId)
        { this.pool = pool; this.pixels = pixels; this.metadata = metadata; BootId = bootId; }
        public long CapturedAt { get; } = Stopwatch.GetTimestamp();
        public string BootId { get; }
        public ulong Trigger => metadata.Trigger;
        public FrameView View
        {
            get
            {
                ObjectDisposedException.ThrowIf(Volatile.Read(ref returned) != 0, this);
                return new(pixels.AsMemory(0, metadata.Length), metadata.Width, metadata.Height, metadata.Stride,
                    metadata.PixelFormat, metadata.FrameNumber, metadata.DeviceTimestamp);
            }
        }
        public void Dispose()
        { if (Interlocked.Exchange(ref returned, 1) == 0) pool.Return(pixels, metadata.Length); }
    }
}

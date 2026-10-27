using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Ipc.Platform;

namespace Ipc.Drivers.Hikrobot.Mvs;

internal sealed class FramePool
{
    readonly ConcurrentQueue<byte[]> available = new();
    readonly int capacity;
    int rented;
    public int InUse => Volatile.Read(ref rented);
    public int Capacity => capacity;
    public FramePool(int count, int maximumPayload)
    {
        capacity = count;
        for (var i = 0; i < count; i++) available.Enqueue(GC.AllocateUninitializedArray<byte>(maximumPayload, pinned: true));
    }
    public FrameLease? TryCopy(MvsFrame frame, string epoch)
    {
        if (!available.TryDequeue(out var bytes)) return null;
        Interlocked.Increment(ref rented);
        var lease = new FrameLease(this, bytes, frame, epoch);
        try { Marshal.Copy(frame.Data, bytes, 0, checked((int)frame.Length)); return lease; }
        catch { lease.Dispose(); throw; }
    }
    void Return(byte[] bytes, int used)
    {
        bytes.AsSpan(0, used).Clear();
        available.Enqueue(bytes); Interlocked.Decrement(ref rented);
    }
    internal sealed class FrameLease(FramePool pool, byte[] bytes, MvsFrame frame, string epoch) : IDisposable
    {
        int returned;
        public long CapturedAt { get; } = System.Diagnostics.Stopwatch.GetTimestamp();
        public string Epoch => epoch;
        public uint Trigger => frame.Trigger;
        public FrameView View => new(bytes.AsMemory(0, checked((int)frame.Length)), checked((int)frame.Width), checked((int)frame.Height),
            checked((int)frame.Width), FramePixelFormat.Mono8, frame.Number, frame.DeviceTimestamp);
        public void Dispose() { if (Interlocked.Exchange(ref returned, 1) == 0) pool.Return(bytes, checked((int)frame.Length)); }
    }
}

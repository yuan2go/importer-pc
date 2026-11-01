using System.Buffers;
using Ipc.Platform;

namespace Ipc.Drivers.Hikrobot.Mvs;

internal sealed class MvsOriginalFrames : IDisposable
{
    const int MaximumFrames = 32;
    const long MaximumBytes = 268435456;
    readonly object gate = new();
    readonly Dictionary<AcquisitionIdentity, Frame> frames = new();
    bool enabled, disposed;
    int count;
    long bytes;
    internal int Count { get { lock (gate) return count; } }
    internal void Enable() { lock (gate) { ObjectDisposedException.ThrowIf(disposed, this); enabled = true; } }
    internal Frame? Reserve(int length, int width, int height, CancellationToken token)
    {
        if (length <= 0 || length > MaximumBytes || width <= 0 || height <= 0 || (long)width * height != length)
            throw new ArgumentOutOfRangeException(nameof(length), "Original frame reservation requires a complete, bounded Mono8 image");
        CancellationTokenRegistration wake = default;
        var registered = false;
        try
        {
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (!enabled) return null;
                while (true)
                {
                    token.ThrowIfCancellationRequested(); ObjectDisposedException.ThrowIf(disposed, this);
                    if (count < MaximumFrames && bytes + length <= MaximumBytes)
                    {
                        var buffer = ArrayPool<byte>.Shared.Rent(length);
                        if (bytes + buffer.Length <= MaximumBytes)
                        { count++; bytes += buffer.Length; return new Frame(this, buffer, length, width, height) { WaitedForCapacity = registered }; }
                        ArrayPool<byte>.Shared.Return(buffer, true);
                    }
                    if (!registered)
                    {
                        wake = token.Register(() => { lock (gate) Monitor.PulseAll(gate); });
                        registered = true;
                        // Register runs synchronously if cancellation already happened.
                        // Check again before sleeping so that pulse cannot be lost.
                        token.ThrowIfCancellationRequested();
                    }
                    Monitor.Wait(gate);
                }
            }
        }
        // A cancellation callback may be waiting for gate; never drain it inside gate.
        finally { wake.Dispose(); }
    }
    internal Frame? Copy(byte[] pixels, int length, int width, int height)
    {
        lock (gate)
        {
            if (!enabled) return null;
            ObjectDisposedException.ThrowIf(disposed, this);
            if (count >= MaximumFrames || bytes + length > MaximumBytes) throw new IOException("Original frame memory quota exhausted; source fenced before decoding");
            byte[]? copy = null;
            try
            {
                copy = ArrayPool<byte>.Shared.Rent(length);
                if (bytes + copy.Length > MaximumBytes) throw new IOException("Original frame pool-byte quota exhausted; source fenced before decoding");
                pixels.AsSpan(0, length).CopyTo(copy); var frame = new Frame(this, copy, length, width, height); count++; bytes += copy.Length; return frame;
            }
            catch { if (copy is not null) ArrayPool<byte>.Shared.Return(copy, true); throw; }
        }
    }
    internal sealed class Frame(MvsOriginalFrames owner, byte[] pixels, int length, int width, int height) : IDisposable
    {
        internal byte[] Pixels = pixels;
        internal readonly int Length = length, Width = width, Height = height;
        internal int References = 1;
        internal bool WaitedForCapacity { get; init; }
        internal int Leases;
        internal bool ProvisionalOwned = true;
        internal AcquisitionIdentity? Identity;
        public void Dispose() { lock (owner.gate) { if (!ProvisionalOwned) return; ProvisionalOwned = false; owner.Release(this); } }
    }
    internal void Publish(AcquisitionIdentity identity, Frame? frame)
    {
        if (frame is null) return;
        lock (gate) { ObjectDisposedException.ThrowIf(disposed, this); frames.Add(identity, frame); frame.Identity = identity; }
    }
    internal IOriginalFrameLease Retain(AcquisitionIdentity identity)
    {
        lock (gate)
        {
            if (!frames.TryGetValue(identity, out var frame)) throw new IOException("Original frame unavailable; latest-frame substitution forbidden");
            frame.References++; frame.Leases++; return new Lease(this, frame);
        }
    }
    internal void Release(AcquisitionIdentity identity)
    {
        lock (gate) if (frames.TryGetValue(identity, out var frame) && frame.ProvisionalOwned) { frame.ProvisionalOwned = false; Release(frame); }
    }
    void Release(Frame frame)
    {
        lock (gate)
        {
            if (frame.References <= 0) return;
            if (--frame.References != 0) return;
            if (frame.Identity is { } identity) frames.Remove(identity);
            bytes -= frame.Pixels.Length; ArrayPool<byte>.Shared.Return(frame.Pixels, true); frame.Pixels = []; count--; Monitor.PulseAll(gate);
        }
    }
    sealed class Lease(MvsOriginalFrames owner, Frame frame) : IOriginalFrameLease
    {
        int released;
        public int Width => frame.Width;
        public int Height => frame.Height;
        public ReadOnlyMemory<byte> Pixels => Volatile.Read(ref released) == 0 ? frame.Pixels.AsMemory(0, frame.Length) : throw new ObjectDisposedException(nameof(Lease));
        public void Dispose() { if (Interlocked.Exchange(ref released, 1) == 0) lock (owner.gate) { frame.Leases--; owner.Release(frame); } }
    }
    public void Dispose()
    {
        lock (gate)
        {
            if (count != frames.Count || frames.Values.Any(f => f.Leases != 0)) throw new InvalidOperationException("Original frame acquisition/writer still owns pixels; disposal must wait");
            foreach (var frame in frames.Values.ToArray()) Release(frame);
            disposed = true;
            Monitor.PulseAll(gate);
        }
    }
}

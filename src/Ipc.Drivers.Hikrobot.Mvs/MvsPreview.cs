using System.Diagnostics;
using System.Buffers;
using System.Runtime.InteropServices;
using Ipc.Platform;
namespace Ipc.Drivers.Hikrobot.Mvs;

// At most one 640x480 managed thumbnail; demand expires after pages stop reading.
internal sealed class MvsPreview
{
    readonly object sync = new();
    long demandAt, publishedAt;
    bool requested;
    ObservationPreview? current;
    byte[]? thumbnail;
    int borrowed;
    public ObservationPreview? Read()
    {
        lock (sync)
        {
            requested = true; demandAt = Stopwatch.GetTimestamp();
            return current is { } frame ? frame with { Gray8 = frame.Gray8.AsSpan(0, frame.Width * frame.Height).ToArray() } : null;
        }
    }
    public ObservationPreviewLease? ReadLease()
    {
        lock (sync)
        {
            requested = true; demandAt = Stopwatch.GetTimestamp();
            if (current is not { } frame) return null;
            if (borrowed >= 8) throw new InvalidOperationException("Preview lease budget exhausted; release outstanding thumbnails");
            var count = checked(frame.Width * frame.Height); var copy = ArrayPool<byte>.Shared.Rent(count);
            frame.Gray8.AsSpan(0, count).CopyTo(copy);
            copy.AsSpan(count).Clear();
            borrowed++;
            return new(frame with { Gray8 = copy }, () =>
            {
                lock (sync) { ArrayPool<byte>.Shared.Return(copy, clearArray: true); borrowed--; }
            });
        }
    }
    void ReleaseThumbnail()
    {
        current = null;
        if (thumbnail is { } pixels) { thumbnail = null; ArrayPool<byte>.Shared.Return(pixels, clearArray: true); }
    }
    public void Clear() { lock (sync) { ReleaseThumbnail(); requested = false; publishedAt = 0; } }
    public void Offer(byte[] pixels, int width, int height, string device, string epoch, long frame, DateTimeOffset at)
        => OfferCore(pixels, IntPtr.Zero, width, height, device, epoch, frame, at);
    public void Offer(IntPtr pixels, int width, int height, string device, string epoch, long frame, DateTimeOffset at)
        => OfferCore(null, pixels, width, height, device, epoch, frame, at);
    void OfferCore(byte[]? pixels, IntPtr native, int width, int height, string device, string epoch, long frame, DateTimeOffset at)
    {
        lock (sync)
        {
            var now = Stopwatch.GetTimestamp();
            if (!requested || Stopwatch.GetElapsedTime(demandAt, now) > TimeSpan.FromSeconds(2)) { ReleaseThumbnail(); requested = false; return; }
            if (publishedAt != 0 && Stopwatch.GetElapsedTime(publishedAt, now) < TimeSpan.FromSeconds(1d / 15)) return;
            var scale = Math.Max(1d, Math.Max(width / 640d, height / 480d));
            var w = Math.Max(1, (int)(width / scale)); var h = Math.Max(1, (int)(height / scale));
            var size = checked(w * h);
            if (thumbnail is null || thumbnail.Length < size) { ReleaseThumbnail(); thumbnail = ArrayPool<byte>.Shared.Rent(size); }
            var result = thumbnail;
            // Only one scanline plus a bounded thumbnail, even when decoding is paused.
            var row = pixels is null ? ArrayPool<byte>.Shared.Rent(width) : null;
            try
            {
            for (var y = 0; y < h; y++)
            {
                var offset = checked((int)((long)y * height / h) * width);
                if (row is not null) Marshal.Copy(IntPtr.Add(native, offset), row, 0, width);
                for (var x = 0; x < w; x++)
                    result[y * w + x] = row is not null ? row[(int)((long)x * width / w)] : pixels![offset + (int)((long)x * width / w)];
            }
            current = new(device, epoch, frame, at, w, h, result); publishedAt = now;
            }
            finally { if (row is not null) ArrayPool<byte>.Shared.Return(row, clearArray: true); }
        }
    }
}

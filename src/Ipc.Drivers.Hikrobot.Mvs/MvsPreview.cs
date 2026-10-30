using System.Diagnostics;
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
    public ObservationPreview? Read()
    {
        lock (sync)
        {
            requested = true; demandAt = Stopwatch.GetTimestamp();
            return current is { } frame ? frame with { Gray8 = (byte[])frame.Gray8.Clone() } : null;
        }
    }
    public void Clear() { lock (sync) { current = null; requested = false; publishedAt = 0; } }
    public void Offer(byte[] pixels, int width, int height, string device, string epoch, long frame, DateTimeOffset at)
        => OfferCore(pixels, IntPtr.Zero, width, height, device, epoch, frame, at);
    public void Offer(IntPtr pixels, int width, int height, string device, string epoch, long frame, DateTimeOffset at)
        => OfferCore(null, pixels, width, height, device, epoch, frame, at);
    void OfferCore(byte[]? pixels, IntPtr native, int width, int height, string device, string epoch, long frame, DateTimeOffset at)
    {
        lock (sync)
        {
            var now = Stopwatch.GetTimestamp();
            if (!requested || Stopwatch.GetElapsedTime(demandAt, now) > TimeSpan.FromSeconds(2)) { current = null; requested = false; return; }
            if (publishedAt != 0 && Stopwatch.GetElapsedTime(publishedAt, now) < TimeSpan.FromMilliseconds(200)) return;
            var scale = Math.Max(1d, Math.Max(width / 640d, height / 480d));
            var w = Math.Max(1, (int)(width / scale)); var h = Math.Max(1, (int)(height / scale));
            var result = new byte[w * h];
            // Only one scanline plus a bounded thumbnail, even when decoding is paused.
            var row = pixels is null ? new byte[width] : null;
            for (var y = 0; y < h; y++)
            {
                var offset = checked((int)((long)y * height / h) * width);
                if (row is not null) Marshal.Copy(IntPtr.Add(native, offset), row, 0, width);
                for (var x = 0; x < w; x++)
                    result[y * w + x] = row is not null ? row[(int)((long)x * width / w)] : pixels![offset + (int)((long)x * width / w)];
            }
            current = new(device, epoch, frame, at, w, h, result); publishedAt = now;
        }
    }
}

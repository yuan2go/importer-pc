using System.Diagnostics;
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
    {
        lock (sync)
        {
            var now = Stopwatch.GetTimestamp();
            if (!requested || Stopwatch.GetElapsedTime(demandAt, now) > TimeSpan.FromSeconds(2)) { current = null; requested = false; return; }
            if (publishedAt != 0 && Stopwatch.GetElapsedTime(publishedAt, now) < TimeSpan.FromMilliseconds(500)) return;
            var scale = Math.Max(1d, Math.Max(width / 640d, height / 480d));
            var w = Math.Max(1, (int)(width / scale)); var h = Math.Max(1, (int)(height / scale));
            var result = new byte[w * h];
            for (var y = 0; y < h; y++) for (var x = 0; x < w; x++)
                result[y * w + x] = pixels[(int)((long)y * height / h) * width + (int)((long)x * width / w)];
            current = new(device, epoch, frame, at, w, h, result); publishedAt = now;
        }
    }
}

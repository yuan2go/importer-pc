using System.Runtime.InteropServices;

namespace Ipc.Drivers.Hikrobot.Mvs;

internal enum StillnessDecision { Still, Moving, Fallback }

// Recognise only a settled scene. The operator places the parcel on a stationary cart; frames
// taken while the hand or parcel is moving are blurred or half covered, and a partial read would
// start a placement with the wrong code. Each frame is reduced to a coarse grid of samples and
// compared with the frame where the scene last changed (not with the previous frame, so a slow
// slide still counts as movement). A frame is decodable once that reference has held for the
// still time. If the scene never settles (vibration, a hand resting on the parcel), one frame
// is decoded every fallback interval so the station never stops reading.
internal sealed class MvsStillness(int stillMs, int fallbackMs)
{
    // About 200 x 130 samples on a 3072 x 2048 frame: cheap next to the full copy for decoding.
    const int GridColumns = 192, GridRows = 128;
    // A sample "changed" when it differs by more than this many grey levels after removing a
    // global brightness shift (auto exposure). Sensor noise stays well below it.
    const int ChangedLevel = 24;
    // The scene moved when more than this share of samples changed (0.3%: a hand edge or a
    // parcel edge moving a few pixels).
    const double MovingShare = 0.003;

    byte[]? reference, current, row;
    int width, height;
    long referenceTicks, lastDecodableTicks;
    public bool Enabled => stillMs > 0;
    public int StillMs => stillMs;
    public void Reset() { reference = current = row = null; width = height = 0; referenceTicks = lastDecodableTicks = 0; }
    // Identity of the scene the last observed frame belongs to: the monotonic time its reference was taken.
    // Two frames with the same value show the same unmoved scene. Unique across instances and cameras in a
    // process because it is a Stopwatch timestamp; null while the gate is off or before the first frame.
    public long? Scene => Enabled && reference is not null ? referenceTicks : null;

    public StillnessDecision Observe(nint data, int frameWidth, int frameHeight, long ticks)
    {
        if (!Enabled) return StillnessDecision.Still;
        if (frameWidth != width || frameHeight != height || reference is null)
        {
            width = frameWidth; height = frameHeight;
            reference = Sample(data, new byte[GridColumns * GridRows]); current = new byte[reference.Length];
            referenceTicks = ticks; lastDecodableTicks = ticks;
            return StillnessDecision.Moving;
        }
        Sample(data, current!);
        if (Moved(reference, current!)) { (reference, current) = (current!, reference); referenceTicks = ticks; }
        if (Milliseconds(referenceTicks, ticks) >= stillMs) { lastDecodableTicks = ticks; return StillnessDecision.Still; }
        if (Milliseconds(lastDecodableTicks, ticks) >= fallbackMs) { lastDecodableTicks = ticks; return StillnessDecision.Fallback; }
        return StillnessDecision.Moving;
    }

    // ticks are Stopwatch timestamps (monotonic), as delivered by the capture loop.
    static double Milliseconds(long from, long to) => (to - from) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

    // Copies one sampled row at a time from the borrowed SDK buffer; never holds the pointer.
    byte[] Sample(nint data, byte[] samples)
    {
        row ??= new byte[width];
        if (row.Length < width) row = new byte[width];
        for (var y = 0; y < GridRows; y++)
        {
            var sourceRow = (int)((long)(2 * y + 1) * height / (2 * GridRows));
            Marshal.Copy(data + (nint)((long)sourceRow * width), row, 0, width);
            for (var x = 0; x < GridColumns; x++) samples[y * GridColumns + x] = row[(int)((long)(2 * x + 1) * width / (2 * GridColumns))];
        }
        return samples;
    }

    internal static bool Moved(byte[] a, byte[] b)
    {
        long sum = 0;
        for (var i = 0; i < a.Length; i++) sum += b[i] - a[i];
        var shift = (int)(sum / a.Length);
        var changed = 0;
        for (var i = 0; i < a.Length; i++) if (Math.Abs(b[i] - a[i] - shift) > ChangedLevel) changed++;
        return changed > a.Length * MovingShare;
    }
}

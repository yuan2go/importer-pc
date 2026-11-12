using System.Diagnostics;
using System.Runtime.InteropServices;
using Ipc.Platform;

namespace Ipc.Drivers.Hikrobot.Mvs;

// A taught background model, independent of recognition and stillness gating.
// No unsupervised background adaptation: a stationary parcel must never become empty.
internal sealed class MvsTrayPresence
{
    const int Columns = 192, Rows = 128;
    readonly object gate = new();
    byte[]? reference, teaching;
    int width, height, count;
    long first, previous, lastPublished;
    bool requested;
    TrayPresence pending, published;
    string referenceId = "";
    double? changedFraction;
    int? brightnessShift;
    bool? exposureValid;
    string status = "未校准；在位状态未知";
    public string Status { get { lock (gate) return status; } private set => status = value; }
    public bool CanDecode { get { lock (gate) return !requested && (reference is null || published == TrayPresence.Present); } }
    public TrayPresenceModelSnapshot Snapshot()
    {
        lock (gate) return new(Status, reference is not null, requested, reference is null ? null : referenceId,
            width, height, published, pending, count, previous != 0 && first != 0 ? Math.Max(0, Elapsed(first, previous)) : 0,
            changedFraction, brightnessShift, exposureValid);
    }
    public void BreakContinuity()
    {
        lock (gate)
        {
            if (requested && teaching is not null) FailTeaching();
            teaching = null; count = 0; previous = 0; pending = published = TrayPresence.Unknown; lastPublished = 0;
            changedFraction = null; brightnessShift = null; exposureValid = null;
            if (reference is not null) Status = "图像连续性中断；在位状态未知";
        }
    }
    void FailTeaching()
    {
        requested = false; teaching = reference = null; referenceId = "";
        Status = "校准失败；请保持空托盘和稳定图像后重新校准";
    }
    public void Teach()
    {
        lock (gate)
        {
            reference = teaching = null; requested = true; count = 0; previous = 0;
            changedFraction = null; brightnessShift = null; exposureValid = null;
            published = pending = TrayPresence.Unknown; lastPublished = 0; referenceId = "";
            Status = "空托盘校准中；请保持托盘为空且画面稳定";
        }
    }
    public TrayPresenceEvidence? Observe(nint data, int w, int h, long ticks)
    {
        lock (gate)
        {
            if (!requested && reference is null) return null;
            var samples = new byte[Columns * Rows];
            var row = new byte[w];
            for (var y = 0; y < Rows; y++)
            {
                Marshal.Copy(data + (nint)((long)((2 * y + 1L) * h / (2 * Rows)) * w), row, 0, w);
                for (var x = 0; x < Columns; x++) samples[y * Columns + x] = row[(int)((2 * x + 1L) * w / (2 * Columns))];
            }
            if (width != w || height != h)
            {
                if (width != 0 && requested && teaching is not null) FailTeaching();
                reference = teaching = null; count = 0; width = w; height = h;
                referenceId = ""; published = pending = TrayPresence.Unknown; first = ticks;
                changedFraction = null; brightnessShift = null;
                Status = requested ? Status : "图像尺寸变化；请重新校准空托盘";
            }
            if (previous != 0 && Elapsed(previous, ticks) > 500)
            {
                if (requested && teaching is not null) FailTeaching();
                teaching = null; count = 0; first = ticks; pending = published = TrayPresence.Unknown; lastPublished = 0;
            }
            previous = ticks;
            exposureValid = samples.Count(v => v is < 5 or > 250) < samples.Length * .2;
            if (requested)
            {
                if (exposureValid == false)
                {
                    FailTeaching();
                    return new(TrayPresence.Unknown, "EmptyTeachingExposureInvalid; explicit calibration must be repeated");
                }
                var delta = teaching is null ? (Changed: 1d, Shift: 0) : Difference(teaching, samples);
                changedFraction = teaching is null ? null : delta.Changed; brightnessShift = teaching is null ? null : delta.Shift;
                if (teaching is null) { teaching = samples; first = ticks; count = 0; }
                else if (delta.Changed > .001 || Math.Abs(delta.Shift) > 12)
                {
                    FailTeaching();
                    return new(TrayPresence.Unknown, "EmptyTeachingChanged; explicit calibration must be repeated");
                }
                count++;
                if (count >= 3 && Elapsed(first, ticks) >= 800)
                {
                    reference = teaching; referenceId = Wire.Hash(Convert.ToBase64String(reference));
                    requested = false; teaching = null; count = 0; pending = TrayPresence.Unknown;
                }
            }
            var state = TrayPresence.Unknown;
            double changed = 0; int shift = 0;
            if (reference is not null)
            {
                (changed, shift) = Difference(reference, samples);
                changedFraction = changed; brightnessShift = shift; exposureValid = exposureValid == true && Math.Abs(shift) <= 12;
                // Large exposure shifts, near-black and saturation invalidate inference.
                if (exposureValid == true)
                    state = changed <= .001 ? TrayPresence.Absent : changed >= .01 ? TrayPresence.Present : TrayPresence.Unknown;
            }
            if (pending != state) { pending = state; count = 0; first = ticks; }
            count++;
            var confirmed = state == TrayPresence.Unknown || count >= 3 && Elapsed(first, ticks) >= (state == TrayPresence.Absent ? 800 : 100);
            var output = confirmed ? state : state == TrayPresence.Absent && published == TrayPresence.Present
                ? TrayPresence.Present : TrayPresence.Unknown;
            // Hysteresis retains the last confirmed occupied state while a matching
            // empty image accumulates its removal interval. True invalidity is Unknown.
            Status = requested || reference is null ? Status : output switch
            { TrayPresence.Absent => "空托盘已确认", TrayPresence.Present => "包裹在位", _ => "在位状态未知；等待可靠图像" };
            if (published == output && lastPublished != 0 && Elapsed(lastPublished, ticks) < 1000) return null;
            published = output; lastPublished = ticks;
            return new(output, $"TaughtMono8-v1;reference={referenceId};size={w}x{h};changed={changed:F5};shift={shift};frames={count};spanMs={Elapsed(first, ticks):F0}");
        }
    }
    static double Elapsed(long a, long b) => (b - a) * 1000d / Stopwatch.Frequency;
    static (double Changed, int Shift) Difference(byte[] a, byte[] b)
    {
        Span<int> histogram = stackalloc int[511];
        histogram.Clear();
        for (var i = 0; i < a.Length; i++) histogram[b[i] - a[i] + 255]++;
        var median = 0; var accumulated = 0;
        for (; median < histogram.Length; median++)
        { accumulated += histogram[median]; if (accumulated > a.Length / 2) break; }
        var shift = median - 255; var changed = 0;
        for (var i = 0; i < a.Length; i++) if (Math.Abs(b[i] - a[i] - shift) > 24) changed++;
        return (changed / (double)a.Length, shift);
    }
}

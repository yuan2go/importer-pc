using System.Diagnostics;
using System.Runtime.InteropServices;
using Ipc.Platform;

namespace Ipc.Drivers.Hikrobot.Mvs;

// A taught background model, independent of recognition and stillness gating.
// No unsupervised background adaptation: a stationary parcel must never become empty.
internal sealed class MvsTrayPresence
{
    internal const int DefaultEmptyConfirmMs = 300, MinimumEmptyConfirmMs = 150, MaximumEmptyConfirmMs = 2000;
    internal const int TeachingConfirmMs = 800;
    const int Columns = 192, Rows = 128;
    const double TeachingMaxChanged = .001, RuntimeAbsentMaxChanged = .0025, RuntimePresentMinChanged = .01;
    readonly int emptyConfirmMs;
    readonly object gate = new();
    byte[]? reference, teaching;
    int width, height, count;
    long first, previous, lastPublished, teachingStarted, teachingFirst;
    int teachingCount;
    long? publishedFirstPresentTicks;
    bool requested;
    TrayPresence pending, published;
    string referenceId = "";
    double? changedFraction;
    int? brightnessShift;
    bool? exposureValid;
    string status = "未校准；在位状态未知";

    internal MvsTrayPresence(int emptyConfirmMs = DefaultEmptyConfirmMs)
    {
        if (emptyConfirmMs is < MinimumEmptyConfirmMs or > MaximumEmptyConfirmMs)
            throw new ArgumentOutOfRangeException(nameof(emptyConfirmMs));
        this.emptyConfirmMs = emptyConfirmMs;
    }

    public string Status { get { lock (gate) return status; } private set => status = value; }
    public bool CanDecode { get { lock (gate) return !requested && (reference is null || published == TrayPresence.Present); } }
    public long? ContinuousPresenceTicks { get { lock (gate) return pending == TrayPresence.Present && published == TrayPresence.Present ? first : null; } }
    public TrayPresenceModelSnapshot Snapshot()
    {
        lock (gate)
        {
            ExpireTeaching();
            var since = requested ? teachingFirst : first;
            return new(Status, reference is not null, requested, reference is null ? null : referenceId,
                width, height, published, pending, requested ? teachingCount : count,
                previous != 0 && since != 0 ? Math.Max(0, Elapsed(since, previous)) : 0,
                changedFraction, brightnessShift, exposureValid);
        }
    }
    public void BreakContinuity()
    {
        lock (gate)
        {
            if (requested && teaching is not null) FailTeaching();
            teaching = null; count = 0; first = previous = 0; pending = published = TrayPresence.Unknown; lastPublished = 0;
            changedFraction = null; brightnessShift = null; exposureValid = null;
            if (reference is not null) Status = "图像连续性中断；在位状态未知";
        }
    }
    void ExpireTeaching()
    {
        if (requested && Stopwatch.GetElapsedTime(teachingStarted) >= TimeSpan.FromSeconds(5))
            FailTeaching("校准失败，未连续收到画面。请检查相机连接后重试。");
    }
    void FailTeaching(string? reason = null)
    {
        requested = false; teaching = reference = null; referenceId = "";
        Status = reason ?? "校准失败，画面不连续。请保持空托盘后重试。";
    }
    public void CancelTeaching()
    {
        lock (gate)
            if (requested) FailTeaching("校准已停止。请重新确认空托盘后再校准。");
    }
    public void Teach()
    {
        lock (gate)
        {
            reference = teaching = null; requested = true; count = 0; previous = 0;
            teachingStarted = Stopwatch.GetTimestamp(); teachingFirst = 0; teachingCount = 0;
            changedFraction = null; brightnessShift = null; exposureValid = null;
            published = pending = TrayPresence.Unknown; lastPublished = 0; referenceId = "";
            Status = "空托盘校准中；请保持托盘为空且画面稳定";
        }
    }
    public TrayPresenceEvidence? Observe(nint data, int w, int h, long ticks)
    {
        lock (gate)
        {
            ExpireTeaching();
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
                    FailTeaching("校准失败，画面过暗或过亮。请调整照明后重试。");
                    return new(TrayPresence.Unknown, "EmptyTeachingExposureInvalid; explicit calibration must be repeated");
                }
                var delta = teaching is null ? (Changed: 1d, Shift: 0) : Difference(teaching, samples);
                changedFraction = teaching is null ? null : delta.Changed; brightnessShift = teaching is null ? null : delta.Shift;
                if (teaching is null) { teaching = samples; teachingFirst = ticks; }
                else if (delta.Changed > TeachingMaxChanged || Math.Abs(delta.Shift) > 12)
                {
                    FailTeaching("校准失败，画面有移动。请清空托盘并保持画面稳定后重试。");
                    return new(TrayPresence.Unknown, "EmptyTeachingChanged; explicit calibration must be repeated");
                }
                teachingCount++;
                if (teachingCount >= 3 && Elapsed(teachingFirst, ticks) >= TeachingConfirmMs)
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
                    state = changed <= RuntimeAbsentMaxChanged ? TrayPresence.Absent :
                        changed >= RuntimePresentMinChanged ? TrayPresence.Present : TrayPresence.Unknown;
            }
            if (pending != state || first == 0) { pending = state; count = 0; first = ticks; }
            count++;
            var confirmed = state == TrayPresence.Unknown || count >= 3 && Elapsed(first, ticks) >= (state == TrayPresence.Absent ? emptyConfirmMs : 100);
            var output = confirmed ? state : state == TrayPresence.Absent && published == TrayPresence.Present
                ? TrayPresence.Present : TrayPresence.Unknown;
            // Hysteresis retains the last confirmed occupied state while a matching
            // empty image accumulates its removal interval. True invalidity is Unknown.
            Status = requested || reference is null ? Status : output switch
            { TrayPresence.Absent => "空托盘已确认", TrayPresence.Present => "包裹在位", _ => "在位状态未知；等待可靠图像" };
            var continuousStart = output == TrayPresence.Present && state == TrayPresence.Present ? first : (long?)null;
            // Loss of continuity invalidates late-decode authority immediately;
            // the independent empty hysteresis still decides physical removal.
            if (published == output && publishedFirstPresentTicks == continuousStart && lastPublished != 0 && Elapsed(lastPublished, ticks) < 1000) return null;
            publishedFirstPresentTicks = continuousStart;
            published = output; lastPublished = ticks;
            return new(output, $"TaughtMono8-v1;reference={referenceId};size={w}x{h};changed={changed:F5};shift={shift};frames={count};spanMs={Elapsed(first, ticks):F0};emptyMs={emptyConfirmMs}")
            { ReferenceSha256 = reference is null ? null : referenceId, FirstPresentTicks = continuousStart };
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

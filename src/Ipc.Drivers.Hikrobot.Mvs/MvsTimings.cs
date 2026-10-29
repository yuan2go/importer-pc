using System.Diagnostics;

namespace Ipc.Drivers.Hikrobot.Mvs;

// Bounded numeric diagnostics only: never retains pixels, barcode text or device identity.
// Counts/totals/extrema cover the session; percentiles cover the last Window samples.
internal sealed class MvsTimings
{
    internal const int Window = 512;
    readonly object gate = new();
    readonly Dictionary<string, Series> stages = new();
    readonly Queue<MvsFrameLatency> frames = new();
    long started = Stopwatch.GetTimestamp();
    sealed class Series
    {
        internal long Count;
        internal double Total, Min = double.PositiveInfinity, Max;
        internal readonly double[] Recent = new double[Window];
    }
    internal readonly struct Scope(MvsTimings owner, string stage) : IDisposable
    {
        readonly long started = Stopwatch.GetTimestamp();
        public void Dispose() => owner.Record(stage, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }
    internal Scope Measure(string stage) => new(this, stage);
    internal void Reset() { lock (gate) { stages.Clear(); frames.Clear(); started = Stopwatch.GetTimestamp(); } }
    internal double LastMs(string stage) { lock (gate) return stages.TryGetValue(stage, out var s) ? s.Recent[(s.Count - 1) % Window] : 0; }
    internal void Frame(MvsFrameLatency frame)
    {
        lock (gate) { if (frames.Count == Window) frames.Dequeue(); frames.Enqueue(frame); }
    }
    internal void Record(string stage, double ms)
    {
        lock (gate)
        {
            if (!stages.TryGetValue(stage, out var series)) stages.Add(stage, series = new());
            series.Recent[series.Count % Window] = ms;
            series.Count++; series.Total += ms; series.Min = Math.Min(series.Min, ms); series.Max = Math.Max(series.Max, ms);
        }
    }
    internal MvsTimingSnapshot Snapshot()
    {
        lock (gate)
            return new(Stopwatch.GetElapsedTime(started).TotalMilliseconds, Window, stages.ToDictionary(p => p.Key, p =>
            {
                var s = p.Value; var recent = s.Recent.Take((int)Math.Min(s.Count, Window)).Order().ToArray();
                double Percentile(double fraction) => recent[Math.Clamp((int)Math.Ceiling(recent.Length * fraction) - 1, 0, recent.Length - 1)];
                return new MvsStageTiming(s.Count, s.Total, s.Min, s.Max, recent.Length, Percentile(.5), Percentile(.95), Percentile(.99));
            }), frames.ToArray());
    }
}
internal sealed record MvsTimingSnapshot(double ElapsedMs, int PercentileWindow, Dictionary<string, MvsStageTiming> Stages, MvsFrameLatency[] RecentFrames);
internal sealed record MvsStageTiming(long Count, double TotalMs, double MinimumMs, double MaximumMs, int WindowCount, double P50Ms, double P95Ms, double P99Ms);
internal sealed record MvsFrameLatency(uint Number, DateTimeOffset DeliveredAt, ulong DeviceTimestampRaw, long HostTimestampRaw,
    double CopyMs, double ReleaseMs, double DecodeMs, double ClearMs, double ObservedToReceiverMs, double HandoffMs, int Candidates, bool Delivered);

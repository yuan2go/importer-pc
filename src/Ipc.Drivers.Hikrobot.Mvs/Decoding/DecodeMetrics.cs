using System.Diagnostics.Metrics;

namespace Ipc.Drivers.Hikrobot.Mvs;

public sealed record DecodeStatistics(long Acquired, long Decoded, long NoRead, long Dropped, long Stale,
    long AcquisitionGaps, int QueueDepth, int QueueHighWater, int BuffersInUse, long DecodeFailures);

internal sealed class DecodeMetrics : IDisposable
{
    readonly Meter meter;
    readonly Histogram<double> duration;
    long acquired, decoded, noRead, dropped, stale, gaps, failures;
    int depth, highWater;
    public DecodeMetrics(string device)
    {
        meter = new(new MeterOptions("Ipc.Mvs.Decoding") { Version = "1.0.0", Tags = [new KeyValuePair<string, object?>("device.id", device)] });
        // Capture performs only atomic increments. Metrics listeners run when
        // polling, never synchronously on the SDK loan/copy path.
        meter.CreateObservableCounter("mvs.frames", () => new[]
        {
            Measurement(ref acquired, "acquired"), Measurement(ref decoded, "decoded"), Measurement(ref noRead, "no_read"),
            Measurement(ref dropped, "dropped"), Measurement(ref stale, "stale_epoch"), Measurement(ref gaps, "sdk_gap"), Measurement(ref failures, "decode_failure")
        }, "frames");
        duration = meter.CreateHistogram<double>("mvs.decode.duration", "ms");
        meter.CreateObservableGauge("mvs.decode.queue.depth", () => Volatile.Read(ref depth), "frames");
        meter.CreateObservableGauge("mvs.decode.queue.high_water", () => Volatile.Read(ref highWater), "frames");
    }
    static Measurement<long> Measurement(ref long value, string outcome) => new(Interlocked.Read(ref value), new KeyValuePair<string, object?>("outcome", outcome));
    static void Count(ref long field, string outcome) => Interlocked.Increment(ref field);
    public void Acquired() => Count(ref acquired, "acquired");
    public void Decoded() => Count(ref decoded, "decoded");
    public void Duration(double milliseconds) => duration.Record(milliseconds);
    public void NoRead() => Count(ref noRead, "no_read");
    public void Drop() => Count(ref dropped, "dropped");
    public void Stale() => Count(ref stale, "stale_epoch");
    public void Gap() => Count(ref gaps, "sdk_gap");
    public void Failure() => Count(ref failures, "decode_failure");
    public void Queue(int value)
    {
        Volatile.Write(ref depth, value);
        int prior;
        do { prior = Volatile.Read(ref highWater); if (prior >= value) return; } while (Interlocked.CompareExchange(ref highWater, value, prior) != prior);
    }
    public DecodeStatistics Snapshot(int inUse) => new(Interlocked.Read(ref acquired), Interlocked.Read(ref decoded), Interlocked.Read(ref noRead),
        Interlocked.Read(ref dropped), Interlocked.Read(ref stale), Interlocked.Read(ref gaps), Volatile.Read(ref depth), Volatile.Read(ref highWater), inUse, Interlocked.Read(ref failures));
    public void Dispose() => meter.Dispose();
}

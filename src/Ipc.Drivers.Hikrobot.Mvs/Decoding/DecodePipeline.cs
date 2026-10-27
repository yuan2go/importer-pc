using System.Diagnostics;
using System.Threading.Channels;
using Ipc.Platform;

namespace Ipc.Drivers.Hikrobot.Mvs;

public enum DecoderState { Unavailable, SelfTesting, Ready, Degraded, Faulted, Stopping, Stopped }

internal sealed class DecodePipeline
{
    readonly MvsDecodeOptions options;
    readonly DecoderRunner runner;
    readonly FramePool pool;
    readonly Channel<FramePool.FrameLease> queue;
    readonly DecodeMetrics metrics;
    readonly string device, epoch;
    readonly Func<Observation, bool> receive;
    readonly Action changed;
    readonly CancellationTokenSource stop = new();
    readonly object gate = new();
    Task? worker, stopping, cancellationDispatch;
    FramePool.FrameLease? retained;
    int state = (int)DecoderState.Unavailable;
    bool accepting;
    long sequence;
    string failure = "";
    public DecoderState State => (DecoderState)Volatile.Read(ref state);
    public string Failure => Volatile.Read(ref failure);
    public int BuffersInUse => pool.InUse;
    uint? trigger, completedTrigger;
    int triggerFrames;
    ulong firstFrame;
    long triggerStarted;
    CodeSelection? selection;
    public DecodePipeline(MvsDecodeOptions options, Func<IFrameDecoder> decoderFactory, DecodeMetrics metrics, string device, string epoch,
        Func<Observation, bool> receive, Action changed)
    {
        this.options = options; this.metrics = metrics; this.device = device; this.epoch = epoch; this.receive = receive; this.changed = changed;
        pool = new(options.PoolSize, options.MaximumPayload);
        queue = Channel.CreateBounded<FramePool.FrameLease>(new BoundedChannelOptions(options.QueueCapacity)
        { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait, AllowSynchronousContinuations = false });
        runner = new(decoderFactory(), options.DecodeTimeout, options.StopTimeout);
    }
    public async Task StartAsync(CancellationToken token)
    {
        using var startup = CancellationTokenSource.CreateLinkedTokenSource(token, stop.Token);
        Volatile.Write(ref state, (int)DecoderState.SelfTesting); changed();
        try
        {
            if (!DecoderSelfTest.Matches(await runner.DecodeAsync(DecoderSelfTest.CreateFrame(), startup.Token).ConfigureAwait(false)))
                throw new InvalidDataException("Decoder self-test mismatch");
            lock (gate) { startup.Token.ThrowIfCancellationRequested(); accepting = true; state = (int)DecoderState.Ready; }
            worker = Task.Run(Consume); changed();
        }
        catch { Fail("SelfTestFailed", DecoderState.Faulted); throw; }
    }
    public FramePool.FrameLease? Copy(MvsFrame frame, string registration)
    {
        lock (gate)
        {
            if (registration != epoch) { metrics.Stale(); return null; }
            if (!accepting) { metrics.Drop(); return null; }
        }
        var lease = pool.TryCopy(frame, registration);
        if (lease is null) { metrics.Drop(); Fail("PoolExhausted", DecoderState.Degraded); }
        return lease;
    }
    // Called only AFTER FreeImageBuffer succeeds. Takes ownership in every case.
    public void Submit(FramePool.FrameLease lease)
    {
        bool failed;
        lock (gate)
        {
            failed = !accepting || lease.Epoch != epoch || !queue.Writer.TryWrite(lease);
            if (!failed) metrics.Queue(queue.Reader.Count);
        }
        if (failed) { lease.Dispose(); metrics.Drop(); Fail("QueueFullOrFenced", DecoderState.Degraded); }
    }
    public void Fail(string category, DecoderState severity = DecoderState.Faulted)
    {
        lock (gate)
        {
            if (State is DecoderState.Faulted or DecoderState.Degraded or DecoderState.Stopped or DecoderState.Stopping) return;
            failure = category; accepting = false; Volatile.Write(ref state, (int)severity);
            queue.Writer.TryComplete(); cancellationDispatch ??= stop.CancelAsync();
        }
        changed();
    }
    async Task Consume()
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                bool hasData;
                if (trigger is not null)
                {
                    var remaining = options.TriggerTimeout - Stopwatch.GetElapsedTime(triggerStarted);
                    if (remaining <= TimeSpan.Zero) throw new TimeoutException("TriggerIncomplete");
                    hasData = await queue.Reader.WaitToReadAsync(stop.Token).AsTask().WaitAsync(remaining, stop.Token).ConfigureAwait(false);
                }
                else hasData = await queue.Reader.WaitToReadAsync(stop.Token).ConfigureAwait(false);
                if (!hasData) break;
                while (queue.Reader.TryRead(out var frame))
                {
                    metrics.Queue(queue.Reader.Count);
                    try
                    {
                        if (stop.IsCancellationRequested) { metrics.Drop(); continue; }
                        if (frame.Epoch != epoch) { metrics.Stale(); continue; }
                        CheckTrigger(frame);
                        var started = Stopwatch.GetTimestamp();
                        IReadOnlyList<DecodedCode> codes;
                        try { codes = await runner.DecodeAsync(frame.View, stop.Token).ConfigureAwait(false); }
                        finally { metrics.Duration(Stopwatch.GetElapsedTime(started).TotalMilliseconds); }
                        metrics.Decoded();
                        if (stop.IsCancellationRequested) { metrics.Stale(); continue; }
                        if (trigger is not null && Stopwatch.GetElapsedTime(triggerStarted) > options.TriggerTimeout)
                            throw new TimeoutException("TriggerIncomplete");
                        var selected = options.Selection?.Select(frame.View, codes) ?? new CodeSelection(null, null, "SelectionUnavailable");
                        if (options.TriggerMode) Aggregate(frame, selected);
                        else Deliver(frame, selected);
                    }
                    finally
                    {
                        // A contract-violating in-process decoder can still own
                        // this view. Stop must prove completion before returning it.
                        if (runner.Idle) frame.Dispose(); else retained = frame;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (TimeoutException ex) { metrics.Failure(); Fail(ex.Message == "TriggerIncomplete" ? "TriggerIncomplete" : "DecodeTimeout",
            ex.Message == "TriggerIncomplete" ? DecoderState.Faulted : DecoderState.Degraded); }
        catch (Exception ex) { metrics.Failure(); Fail("DecodePipeline:" + ex.GetType().Name); }
        finally
        {
            while (queue.Reader.TryRead(out var frame)) { frame.Dispose(); metrics.Drop(); }
            metrics.Queue(0);
        }
    }
    void CheckTrigger(FramePool.FrameLease frame)
    {
        if (!options.TriggerMode) return;
        if (trigger is null)
        {
            if (completedTrigger is { } prior && (frame.Trigger <= prior || frame.Trigger - prior != 1))
                throw new InvalidDataException("TriggerCounterGapResetOrExtraFrame");
            trigger = frame.Trigger; triggerFrames = 0; firstFrame = frame.View.FrameNumber; triggerStarted = frame.CapturedAt; selection = null;
        }
        else if (frame.Trigger != trigger) throw new InvalidDataException("TriggerChangedBeforeComplete");
    }
    void Aggregate(FramePool.FrameLease frame, CodeSelection current)
    {
        if (current.Issue is "AmbiguousCodes" or "SelectionTimeout") selection = current;
        else if (selection?.Issue is not ("AmbiguousCodes" or "SelectionTimeout"))
        {
            if (current.Text is not null && selection?.Text is not null && (current.Text != selection.Text || current.Symbology != selection.Symbology))
                selection = new(null, null, "AmbiguousCodes");
            else if (current.Text is not null || selection is null) selection = current;
        }
        if (++triggerFrames != options.FramesPerTrigger) return;
        Deliver(frame, selection!); completedTrigger = trigger; trigger = null;
    }
    void Deliver(FramePool.FrameLease frame, CodeSelection selected)
    {
        Observation observation;
        lock (gate)
        {
            if (!accepting || stop.IsCancellationRequested) { metrics.Stale(); return; }
            var source = options.TriggerMode ? $"t{frame.Trigger}.f{firstFrame}-{frame.View.FrameNumber}" : $"f{frame.View.FrameNumber}";
            var admission = options.TriggerMode ? "mvs." + Wire.Hash(device + ":" + epoch + ":" + frame.Trigger)[..40].ToLowerInvariant() : "";
            if (selected.Text is null) metrics.NoRead();
            observation = new Observation(Wire.Id(), device, epoch, source, ++sequence, admission,
                !options.TriggerMode ? "UncorrelatedRead" : selected.Text is null ? "NoRead" : "Read",
                selected.Text is null ? [] : [selected.Text], null, DateTimeOffset.UtcNow, selected.Issue);
        }
        // One decode worker publishes. Never hold the capture gate across host
        // code: an in-flight callback is drained with this worker during Stop.
        if (!receive(observation)) throw new IOException("ObservationIngressRejected");
    }
    public void Fence()
    {
        lock (gate)
        {
            if (State == DecoderState.Stopped) return;
            accepting = false; queue.Writer.TryComplete(); cancellationDispatch ??= stop.CancelAsync();
            Volatile.Write(ref state, (int)DecoderState.Stopping);
        }
    }
    public Task StopAsync()
    {
        lock (gate)
        {
            if (State == DecoderState.Stopped) return stopping ?? Task.CompletedTask;
            accepting = false; queue.Writer.TryComplete(); cancellationDispatch ??= stop.CancelAsync();
            Volatile.Write(ref state, (int)DecoderState.Stopping);
            if (stopping is null || stopping.IsFaulted || stopping.IsCanceled) stopping = StopCore();
            return stopping;
        }
    }
    async Task StopCore()
    {
        // Never run decoder Dispose or a host callback under the publication lock.
        await Task.Yield();
        if (worker is not null) await worker.WaitAsync(options.DecodeTimeout + options.StopTimeout).ConfigureAwait(false);
        await runner.StopAsync().ConfigureAwait(false);
        if (cancellationDispatch is not null) await cancellationDispatch.WaitAsync(options.StopTimeout).ConfigureAwait(false);
        retained?.Dispose(); retained = null;
        while (queue.Reader.TryRead(out var frame)) { frame.Dispose(); metrics.Drop(); }
        metrics.Queue(0);
        if (pool.InUse != 0) throw new InvalidOperationException("Decoder pool still has an owner");
        lock (gate) { Volatile.Write(ref state, (int)DecoderState.Stopped); stop.Dispose(); }
    }
}

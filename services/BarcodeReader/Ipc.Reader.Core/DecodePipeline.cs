using System.Threading.Channels;

namespace Ipc.Reader.Core;

public sealed class DecodePipeline
{
    readonly object gate = new();
    readonly string bootId;
    readonly FramePool pool;
    readonly DecoderRunner runner;
    readonly TriggerAssembler triggers;
    readonly Channel<FramePool.FrameLease> queue;
    readonly Func<TriggerResult, bool> publish;
    readonly Action fault;
    readonly TimeSpan stopTimeout, decodeTimeout;
    readonly CancellationTokenSource cancellation = new();
    readonly HashSet<FramePool.FrameLease> copied = [];
    Task? worker, stopping, cancelDispatch;
    FramePool.FrameLease? retained;
    bool accepting, faulted, stopped;
    ulong? priorFrame;
    long frameGaps, poolOverloads, queueOverloads, staleResults, faultNotificationFailures;
    public int BuffersInUse => pool.InUse;
    public bool Faulted { get { lock (gate) return faulted; } }
    public long FrameGaps => Interlocked.Read(ref frameGaps);
    public long PoolOverloads => Interlocked.Read(ref poolOverloads);
    public long QueueOverloads => Interlocked.Read(ref queueOverloads);
    public long StaleResults => Interlocked.Read(ref staleResults);
    public long FaultNotificationFailures => Interlocked.Read(ref faultNotificationFailures);
    public DecodePipeline(string bootId, BufferBudget budget, IFrameDecoder decoder, WaybillSelector selection,
        int framesPerTrigger, TimeSpan decodeTimeout, TimeSpan triggerTimeout, TimeSpan stopTimeout,
        Func<TriggerResult, bool> publish, Action fault)
    {
        if (decodeTimeout <= TimeSpan.Zero || decodeTimeout > TimeSpan.FromSeconds(30) ||
            stopTimeout <= TimeSpan.Zero || stopTimeout > TimeSpan.FromSeconds(10))
            throw new InvalidDataException("DecoderTimeoutConfigurationInvalid");
        this.bootId = bootId; this.publish = publish; this.fault = fault; this.stopTimeout = stopTimeout; this.decodeTimeout = decodeTimeout;
        pool = new(budget); runner = new(decoder, decodeTimeout, stopTimeout);
        triggers = new(bootId, framesPerTrigger, triggerTimeout, selection);
        queue = Channel.CreateBounded<FramePool.FrameLease>(new BoundedChannelOptions(budget.QueueCapacity)
        { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait, AllowSynchronousContinuations = false });
    }
    public void Start()
    {
        lock (gate)
        {
            if (worker is not null || faulted || stopped) throw new InvalidOperationException("PipelineBoundaryAlreadyUsed");
            accepting = true; worker = Task.Run(Consume);
        }
    }
    // The camera owner invokes this while it holds the native loan. No user
    // callback, decode, logging or asynchronous queue wait runs on this path.
    public FramePool.FrameLease? Copy(CaptureFrame frame, string currentBoot)
    {
        lock (gate)
        {
            if (!accepting) return null;
            if (currentBoot != bootId)
            { Interlocked.Increment(ref staleResults); FailLocked(); return null; }
            if (priorFrame is { } prior && (prior == ulong.MaxValue || frame.FrameNumber != prior + 1))
            { Interlocked.Increment(ref frameGaps); FailLocked(); return null; }
            priorFrame = frame.FrameNumber;
            FramePool.FrameLease? lease;
            try { lease = pool.TryCopy(frame, bootId); }
            catch { FailLocked(); throw; }
            if (lease is null)
            { Interlocked.Increment(ref poolOverloads); FailLocked(); return null; }
            copied.Add(lease); return lease;
        }
    }
    // Called exactly once AFTER the original SDK loan was successfully returned.
    // On Free failure the camera owner calls RejectCopy and faults its lifecycle.
    public void Submit(FramePool.FrameLease frame)
    {
        lock (gate)
        {
            if (!copied.Remove(frame)) throw new InvalidOperationException("CopyOwnershipMismatch");
            if (!accepting || !queue.Writer.TryWrite(frame))
            {
                frame.Dispose();
                if (accepting) Interlocked.Increment(ref queueOverloads);
                FailLocked();
            }
        }
    }
    public void RejectCopy(FramePool.FrameLease frame)
    {
        lock (gate)
        {
            if (!copied.Remove(frame)) throw new InvalidOperationException("CopyOwnershipMismatch");
            frame.Dispose(); FailLocked();
        }
    }
    void FailLocked()
    {
        if (faulted || stopped) return;
        faulted = true; accepting = false; queue.Writer.TryComplete();
        cancelDispatch ??= cancellation.CancelAsync();
        // Exactly one notification is scheduled. Its code cannot stall capture.
        _ = Task.Run(() => { try { fault(); } catch { Interlocked.Increment(ref faultNotificationFailures); } });
    }
    async Task Consume()
    {
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                // Poll only for incomplete-trigger deadlines. No synthetic frames
                // or identities are created by this monotonic timeout check.
                var ready = queue.Reader.WaitToReadAsync(cancellation.Token).AsTask();
                while (!ready.IsCompleted)
                {
                    await Task.WhenAny(ready, Task.Delay(20, cancellation.Token)).ConfigureAwait(false);
                    triggers.CheckDeadline();
                    cancellation.Token.ThrowIfCancellationRequested();
                }
                if (!await ready.ConfigureAwait(false)) break;
                while (queue.Reader.TryRead(out var frame))
                {
                    try
                    {
                        cancellation.Token.ThrowIfCancellationRequested();
                        var codes = await runner.DecodeAsync(frame.View, cancellation.Token).ConfigureAwait(false);
                        cancellation.Token.ThrowIfCancellationRequested();
                        var result = triggers.Add(frame, codes);
                        if (result is not null)
                        {
                            lock (gate)
                            {
                                if (!accepting) { Interlocked.Increment(ref staleResults); continue; }
                            }
                            if (!publish(result)) throw new IOException("ProducerResultRejected");
                        }
                    }
                    finally { if (runner.Idle) frame.Dispose(); else retained = frame; }
                }
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch { lock (gate) FailLocked(); }
        finally { while (queue.Reader.TryRead(out var frame)) frame.Dispose(); }
    }
    public Task StopAsync()
    {
        lock (gate)
        {
            if (stopped) return stopping ?? Task.CompletedTask;
            accepting = false; queue.Writer.TryComplete(); cancelDispatch ??= cancellation.CancelAsync();
            if (stopping is null || stopping.IsFaulted || stopping.IsCanceled) stopping = StopCore();
            return stopping;
        }
    }
    async Task StopCore()
    {
        await Task.Yield();
        if (worker is not null) await worker.WaitAsync(stopTimeout + decodeTimeout).ConfigureAwait(false);
        await runner.StopAsync().ConfigureAwait(false);
        if (cancelDispatch is not null) await cancelDispatch.WaitAsync(stopTimeout).ConfigureAwait(false);
        retained?.Dispose(); retained = null;
        while (queue.Reader.TryRead(out var frame)) frame.Dispose();
        lock (gate)
        {
            if (copied.Count != 0 || pool.InUse != 0) throw new InvalidOperationException("CaptureStillOwnsCopies");
            stopped = true; cancellation.Dispose();
        }
    }
}

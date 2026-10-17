using System.Threading.Channels;
using Ipc.Platform;
using Ipc.Supply;
namespace Ipc.Product;

sealed class ObservationModule(StationApplication application, IObservationSource[] sources, int capacity) : IRuntimeModule, IRuntimeOwnership
{
    public string Id => "observations";
    public string[] Dependencies => [];
    public IObservationSource[] Sources => sources;
    public int SourceStartAttempts { get; private set; }
    readonly Channel<Observation> queue = Channel.CreateBounded<Observation>(new BoundedChannelOptions(capacity) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    Task? worker, healthWorker;
    readonly StationDispatcher sourceInput = new(8);
    readonly SemaphoreSlim lifecycle = new(1, 1);
    sealed record SourceHealth(DeviceHealthConfirmation? Confirmation = null, TaskCompletionSource? Fence = null);
    readonly Channel<SourceHealth> healthQueue = Channel.CreateBounded<SourceHealth>(new BoundedChannelOptions(Math.Max(8, sources.Length * 2)) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    readonly Dictionary<string, string> registeredEpochs = [];
    readonly HashSet<IObservationSource> sourceOwners = [], disposedSources = [];
    readonly Dictionary<IObservationSource, Task> sourceStops = [], sourceDisposals = [];
    bool sourceInputDisposed;
    Task? sourceInputDisposal;
    public bool HasActiveWork
    {
        get { lock (ingress) return sourceOwners.Count != 0 || worker is { IsCompleted: false } || healthWorker is { IsCompleted: false } ||
            sourceInputDisposal is { IsCompleted: false } || sourceDisposals.Values.Any(x => !x.IsCompleted); }
    }
    public bool HasUnreleasedResources => HasActiveWork || disposedSources.Count != sources.Length || !sourceInputDisposed;
    public string[] OwnershipDiagnostics
    {
        get
        {
            lock (ingress) return sourceOwners.Select(x => "Source ownership retained: " + x.DeviceId + "; epoch=" + x.Epoch)
                .Concat(worker is { IsCompleted: false } ? ["Observation worker still running"] : [])
                .Concat(healthWorker is { IsCompleted: false } ? ["Device health worker still running"] : []).ToArray();
        }
    }
    public Task<bool> ExecuteSourceAsync(Func<bool> input) => sourceInput.Invoke(() =>
    {
        lock (ingress) { if (stopping) throw new InvalidOperationException("Observation source input is stopping"); }
        return Task.FromResult(input());
    });
    int missed, pending, pendingOverloadMarkers;
    readonly object ingress = new();
    bool stopping;
    public async Task StartAsync(CancellationToken token)
    {
        application.InputDrained = () => Volatile.Read(ref pending) == 0 && Volatile.Read(ref pendingOverloadMarkers) == 0;
        healthWorker = Task.Run(async () =>
        {
            try
            {
                await foreach (var change in healthQueue.Reader.ReadAllAsync())
                {
                    if (change.Fence is not null) { change.Fence.TrySetResult(); continue; }
                    await application.SetDeviceHealthAsync(change.Confirmation!).ConfigureAwait(false);
                }
            }
            catch (Exception ex) { application.Latch("DeviceHealthWorkerFault", ex.GetType().Name); throw; }
        });
        worker = Task.Run(async () =>
        {
            try
            {
                await foreach (var observation in queue.Reader.ReadAllAsync())
                    try { await application.ReceiveAsync(observation).ConfigureAwait(false); }
                    finally { Interlocked.Decrement(ref pending); }
            }
            catch (Exception ex) { application.Latch("ObservationWorkerFault", ex.Message); throw; }
        }, CancellationToken.None);
        foreach (var source in sources) await StartSourceAsync(source, token).ConfigureAwait(false);
        await FlushHealthAsync(token).ConfigureAwait(false);
    }
    async Task StartSourceAsync(IObservationSource source, CancellationToken token)
    {
        if (source is not IControlledObservationSource controlled) throw new InvalidDataException("Source lacks runtime-owned epoch and reliable confirmation contract: " + source.DeviceId);
        var previous = registeredEpochs.GetValueOrDefault(source.DeviceId, "not-connected");
        var epoch = Wire.Id();
        await application.RegisterDeviceEpochAsync(source.DeviceId, previous, epoch, controlled.ConfirmationSource).ConfigureAwait(false);
        registeredEpochs[source.DeviceId] = epoch;
        sourceStops.Remove(source);
        lock (ingress) sourceOwners.Add(source); // Partial Start failure still owns its registration.
        SourceStartAttempts++;
        await controlled.StartControlledAsync(epoch, Receive, Health, token).ConfigureAwait(false);
    }
    bool Receive(Observation observation)
    {
        string rejection;
        lock (ingress)
        {
            if (stopping) return false;
            if (Volatile.Read(ref pendingOverloadMarkers) == 0 && !application.IsOverloaded)
            {
                Interlocked.Increment(ref pending);
                if (queue.Writer.TryWrite(observation)) return true;
                Interlocked.Decrement(ref pending);
            }
            missed++; Interlocked.Increment(ref pendingOverloadMarkers);
            rejection = $"Observation not admitted: device={observation.DeviceId}; epoch={observation.Epoch}; source={observation.SourceEventId}; unadmitted={missed}";
        }
        // H04 still requires the overload marker before returning rejection. A slow/failing
        // marker must not hold the common ingress gate and block other sources' shutdown.
        try { application.Latch("Overload", rejection); return false; }
        finally { Interlocked.Decrement(ref pendingOverloadMarkers); }
    }
    void Health(DeviceHealthConfirmation confirmation)
    {
        lock (ingress)
        {
            if (stopping) return;
            if (!healthQueue.Writer.TryWrite(new(confirmation))) application.Latch("DeviceHealthOverload", "Device health event not admitted; stop and inspect source");
        }
    }
    async Task FlushHealthAsync(CancellationToken token)
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await healthQueue.Writer.WriteAsync(new(Fence: ready), token).ConfigureAwait(false);
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(5), token).ConfigureAwait(false);
    }
    public async Task RestartSourceAsync(string deviceId)
    {
        await lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (ingress) { if (stopping) throw new InvalidOperationException("Source restart denied while stopping"); }
            var source = sources.Single(x => x.DeviceId == deviceId);
            // Failure retains old registration/ownership; no new epoch or duplicate source starts.
            await StopSourceAsync(source, CancellationToken.None).ConfigureAwait(false);
            await StartSourceAsync(source, CancellationToken.None).ConfigureAwait(false);
            await FlushHealthAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally { lifecycle.Release(); }
    }
    async Task StopSourceAsync(IObservationSource source, CancellationToken token)
    {
        // A timed-out adapter call is still running, not cancelled. Reuse that exact task;
        // never launch another Stop or a new registration while it owns callbacks.
        if (!sourceStops.TryGetValue(source, out var stop) || stop.IsFaulted || stop.IsCanceled)
            sourceStops[source] = stop = Task.Run(() => source.StopAsync(token), CancellationToken.None);
        await stop.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        lock (ingress) sourceOwners.Remove(source);
    }
    public async Task StopAsync(CancellationToken token)
    {
        lock (ingress) stopping = true;
        List<Exception> errors = [];
        await lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            foreach (var source in sources.Reverse())
            {
                lock (ingress) { if (!sourceOwners.Contains(source)) continue; }
                try
                {
                    await StopSourceAsync(source, token).ConfigureAwait(false);
                }
                catch (Exception ex) { errors.Add(new InvalidOperationException("Source stop failed; registration retained: " + source.DeviceId, ex)); }
            }
            // Ingress is sealed even if one source is broken. Independent accepted work can drain.
            queue.Writer.TryComplete(); healthQueue.Writer.TryComplete();
            if (worker is not null) try { await worker.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); } catch (Exception ex) { errors.Add(ex); }
            if (healthWorker is not null) try { await healthWorker.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); } catch (Exception ex) { errors.Add(ex); }
        }
        finally { lifecycle.Release(); }
        if (errors.Count != 0) throw new AggregateException("Observation shutdown failures", errors);
    }
    public async ValueTask DisposeAsync()
    {
        List<Exception> errors = [];
        try { await StopAsync(CancellationToken.None).ConfigureAwait(false); } catch (Exception ex) { errors.Add(ex); }
        foreach (var source in sources.Reverse())
        {
            lock (ingress)
            {
                if (disposedSources.Contains(source)) continue;
                if (sourceOwners.Contains(source)) { errors.Add(new InvalidOperationException("Source disposal deferred while callbacks may remain: " + source.DeviceId)); continue; }
            }
            try
            {
                if (!sourceDisposals.TryGetValue(source, out var disposal) || disposal.IsFaulted || disposal.IsCanceled)
                    sourceDisposals[source] = disposal = Task.Run(async () => await source.DisposeAsync().ConfigureAwait(false), CancellationToken.None);
                await disposal.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); disposedSources.Add(source);
            }
            catch (Exception ex) { errors.Add(new InvalidOperationException("Source disposal failed: " + source.DeviceId, ex)); }
        }
        if (!sourceInputDisposed)
            try
            {
                sourceInputDisposal ??= sourceInput.DisposeAsync().AsTask();
                await sourceInputDisposal.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); sourceInputDisposed = true;
            }
            catch (Exception ex) { errors.Add(ex); }
        if (errors.Count != 0) throw new AggregateException("Observation disposal failures", errors);
    }
}


using Ipc.Platform;
using Ipc.Application.Contracts;
using Ipc.Drivers;

namespace Ipc.Adapters;

public sealed class ManualObservationSource(string deviceId, string confirmationSource = "SIMULATION:manual") : IControlledObservationSource, ISyntheticObservationInput, IReaderImageCapture, IProductionArrivalSource
{
    ProductionArrivalCapture? arrival;
    public Task SetArrivalAsync(ProductionArrivalCapture boundary, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        lock (callbackGate) { ObjectDisposedException.ThrowIf(disposed, this); arrival = boundary; }
        return Task.CompletedTask;
    }
    Func<Observation, bool>? receive;
    Action<DeviceHealthConfirmation>? health;
    CancellationTokenSource? heartbeatStop;
    Task? heartbeat;
    long sequence;
    sealed class AcquisitionRecord
    {
        public readonly HashSet<string> Owners = [];
        public int Captures;
    }
    readonly Dictionary<AcquisitionIdentity, AcquisitionRecord> acquisitions = [];
    bool captureBound, disposed;
    readonly object callbackGate = new();
    readonly object emissionGate = new();
    readonly SemaphoreSlim lifecycle = new(1, 1);
    int inFlight;
    TaskCompletionSource? delivered;
    // A receiver can wait for its bounded queue. The consumer must still be able
    // to release an earlier acquisition before that wait can finish.
    readonly object acquisitionGate = new();
    public int RetainedAcquisitionCount { get { lock (acquisitionGate) return acquisitions.Count; } }
    public string DeviceId { get; } = deviceId;
    public string Epoch { get; private set; } = Wire.Id();
    public int StartCount { get; private set; }
    public string ConfirmationSource => confirmationSource;
    public async Task StartControlledAsync(string registeredEpoch, Func<Observation, bool> receiver, Action<DeviceHealthConfirmation> onHealth, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(registeredEpoch)) throw new ArgumentException("Registered epoch required", nameof(registeredEpoch));
        await lifecycle.WaitAsync(token).ConfigureAwait(false);
        try
        {
        lock (callbackGate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (receive is not null || heartbeatStop is not null) throw new InvalidOperationException("Source already owns a callback/heartbeat");
            receive = receiver; health = onHealth; Epoch = registeredEpoch; sequence = 0; StartCount++;
            heartbeatStop = new();
            var stop = heartbeatStop.Token;
            heartbeat = Task.Run(async () =>
            {
                try
                {
                    using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
                    while (await timer.WaitForNextTickAsync(stop).ConfigureAwait(false))
                        lock (callbackGate) { if (receive is not null) Confirm(DeviceHealth.Ready); }
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            }, CancellationToken.None);
        }
        try { Confirm(DeviceHealth.Ready); }
        catch (Exception original)
        {
            try { await StopCore(CancellationToken.None).ConfigureAwait(false); }
            catch (Exception cleanup) { throw new AggregateException("Synthetic source startup and cleanup failed", original, cleanup); }
            throw;
        }
        }
        finally { lifecycle.Release(); }
    }
    void Confirm(DeviceHealth state)
    {
        var at = DateTimeOffset.UtcNow;
        // These five seconds are a synthetic heartbeat contract, never a real SDK TTL.
        health?.Invoke(new(DeviceId, Epoch, state, at, state == DeviceHealth.Ready ? at.AddSeconds(5) : null, confirmationSource));
    }
    public bool Emit(string kind, string admissionId, string[]? codes = null, int? quantity = null, string? sourceId = null)
        => EmitObservation(kind, admissionId, codes, quantity, sourceId, null);
    public bool EmitPresence(TrayPresence state) => EmitObservation("Presence", "synthetic-presence." + Wire.Id(), [], null, null,
        new(state, "SIMULATION:explicit-qualified-physical-state"));
    bool EmitObservation(string kind, string admissionId, string[]? codes, int? quantity, string? sourceId, TrayPresenceEvidence? presence)
    {
        lock (emissionGate)
        {
            Func<Observation, bool> callback; Observation observation; string owner;
            lock (callbackGate)
            {
            callback = receive ?? throw new InvalidOperationException("Source stopped");
            observation = new Observation(Wire.Id(), DeviceId, Epoch, sourceId ?? Wire.Id(), Interlocked.Increment(ref sequence), admissionId,
                kind, codes ?? [], quantity, DateTimeOffset.UtcNow)
            { TrayPresence = presence, ArrivalGeneration = arrival?.ArrivalGeneration, CaptureBoundaryId = arrival?.BoundaryId, CaptureTicks = arrival is null ? 0 : System.Diagnostics.Stopwatch.GetTimestamp(), FrameNumber = arrival is null ? null : sequence };
            var identity = observation.Acquisition;
            owner = "observation:" + observation.Id;
            if (captureBound)
            {
                lock (acquisitionGate)
                {
                    if (!acquisitions.TryGetValue(identity, out var record)) acquisitions.Add(identity, record = new());
                    record.Owners.Add(owner);
                }
            }
            if (inFlight++ == 0) delivered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            var accepted = false;
            try { return accepted = callback(observation); }
            finally
            {
                if (!accepted) ReleaseAcquisition(observation.Acquisition, owner);
                lock (callbackGate) { if (--inFlight == 0) delivered!.TrySetResult(); }
            }
        }
    }
    public IImageCamera BindCapture(CapabilityBinding binding, string temporaryDirectory)
    {
        lock (callbackGate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (receive is not null || binding.Target != DeviceId) throw new InvalidOperationException("Bind original capture to this reader before startup");
            captureBound = true;
            return new AcquisitionView(this, new ReferenceImageCamera(binding, temporaryDirectory));
        }
    }
    public void RetainAcquisition(AcquisitionIdentity identity, string owner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        lock (acquisitionGate)
        {
            if (!acquisitions.TryGetValue(identity, out var record) || record.Owners.Count == 0)
                throw new IOException("Original acquisition unavailable; retaining a replacement is forbidden");
            record.Owners.Add(owner);
        }
    }
    public void ReleaseAcquisition(AcquisitionIdentity identity, string owner)
    {
        lock (acquisitionGate)
            if (acquisitions.TryGetValue(identity, out var record))
            {
                record.Owners.Remove(owner);
                if (record.Owners.Count == 0 && record.Captures == 0) acquisitions.Remove(identity);
            }
    }
    sealed class AcquisitionView(ManualObservationSource owner, IImageCamera port) : IImageCamera
    {
        public string InstanceId => port.InstanceId;
        public Task StartAsync(CancellationToken t) => port.StartAsync(t);
        public Task<CapabilityHealth> HealthAsync(CancellationToken t) => port.HealthAsync(t);
        public Task ReconnectAsync(CancellationToken t) => port.ReconnectAsync(t);
        public async Task<StageReply> CaptureAsync(FrozenRequest request, CancellationToken t)
        {
            var acquisition = Wire.Decode<ParcelPayload>(request.Payload).Acquisition;
            AcquisitionRecord record;
            lock (owner.acquisitionGate)
            {
                if (acquisition is null || !owner.acquisitions.TryGetValue(acquisition, out record!) || record.Owners.Count == 0)
                    throw new IOException("Original acquisition unavailable; latest frame substitution forbidden");
                record.Captures++;
            }
            try { return await port.CaptureAsync(request, t).ConfigureAwait(false); }
            finally
            {
                lock (owner.acquisitionGate)
                {
                    record.Captures--;
                    if (record.Owners.Count == 0 && record.Captures == 0) owner.acquisitions.Remove(acquisition);
                }
            }
        }
        public Task<StageReply?> QueryAsync(FrozenRequest request, CancellationToken t) => port.QueryAsync(request, t);
        public Task StopAsync(CancellationToken t) => port.StopAsync(t);
        public ValueTask DisposeAsync() => port.DisposeAsync();
    }
    public async Task StopAsync(CancellationToken token)
    {
        CancellationTokenSource? original;
        lock (callbackGate) original = heartbeatStop;
        await lifecycle.WaitAsync(token).ConfigureAwait(false);
        try
        {
            lock (callbackGate) if (!ReferenceEquals(original, heartbeatStop)) return;
            await StopCore(token).ConfigureAwait(false);
        }
        finally { lifecycle.Release(); }
    }
    async Task StopCore(CancellationToken token)
    {
        Task pending;
        lock (callbackGate)
        {
            receive = null; heartbeatStop?.Cancel();
            pending = Task.WhenAll(heartbeat ?? Task.CompletedTask, inFlight == 0 ? Task.CompletedTask : delivered!.Task);
        }
        Exception? failure = null;
        try { await pending.WaitAsync(token).ConfigureAwait(false); }
        catch (Exception error) { if (!pending.IsCompleted) throw; failure = error; }
        try { Confirm(DeviceHealth.Stopped); }
        catch (Exception error) { failure = failure is null ? error : new AggregateException(failure, error); }
        finally
        {
            lock (callbackGate) { health = null; heartbeat = null; heartbeatStop?.Dispose(); heartbeatStop = null; }
        }
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
    public async ValueTask DisposeAsync()
    {
        lock (callbackGate) disposed = true;
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        lock (acquisitionGate)
        {
            if (acquisitions.Values.Any(x => x.Captures != 0)) throw new InvalidOperationException("Original captures still active; source ownership retained");
            acquisitions.Clear();
        }
    }
}

// Replay is explicitly advanced; liveness confirms the synthetic source, not the age or
// authority of a replayed observation or a real device/entity permit.
public sealed record ReplayEvent(string Kind, string AdmissionId, string[] Codes, int? PackageQuantity, string SourceEventId);
public sealed class FileReplaySource(string deviceId, string file) : IControlledObservationSource
{
    readonly ManualObservationSource source = new(deviceId, "SIMULATION:replay");
    ReplayEvent[] events = [];
    int cursor;
    public string DeviceId => source.DeviceId;
    public string Epoch => source.Epoch;
    public string ConfirmationSource => source.ConfirmationSource;
    public async Task StartControlledAsync(string registeredEpoch, Func<Observation, bool> receiver, Action<DeviceHealthConfirmation> health, CancellationToken token)
    {
        if (new FileInfo(file).Length > 1_000_000) throw new InvalidDataException("Replay exceeds size limit");
        events = File.ReadLines(file).Where(x => !string.IsNullOrWhiteSpace(x)).Select(Wire.Decode<ReplayEvent>).ToArray();
        if (events.Length > 2000) throw new InvalidDataException("Replay event limit exceeded");
        cursor = 0; await source.StartControlledAsync(registeredEpoch, receiver, health, token).ConfigureAwait(false);
    }
    public bool Advance()
    {
        if (cursor == events.Length) throw new InvalidOperationException("Replay complete; no scene reset performed");
        var e = events[cursor++]; return source.Emit(e.Kind, e.AdmissionId, e.Codes, e.PackageQuantity, e.SourceEventId);
    }
    public Task StopAsync(CancellationToken token) => source.StopAsync(token);
    public ValueTask DisposeAsync() => source.DisposeAsync();
}

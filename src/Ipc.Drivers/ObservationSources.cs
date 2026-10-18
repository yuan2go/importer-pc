using Ipc.Platform;
using Ipc.Application.Contracts;
using Ipc.Drivers;

namespace Ipc.Adapters;

public sealed class ManualObservationSource(string deviceId, string confirmationSource = "SIMULATION:manual") : IControlledObservationSource, ISyntheticObservationInput, IReaderImageCapture
{
    Func<Observation, bool>? receive;
    Action<DeviceHealthConfirmation>? health;
    CancellationTokenSource? heartbeatStop;
    Task? heartbeat;
    long sequence;
    readonly Dictionary<string, AcquisitionIdentity> acquisitions = [];
    readonly object callbackGate = new();
    public string DeviceId { get; } = deviceId;
    public string Epoch { get; private set; } = Wire.Id();
    public int StartCount { get; private set; }
    public string ConfirmationSource => confirmationSource;
    // Compatibility for isolated synthetic callers; ProductRuntime uses the registered path.
    public Task StartAsync(Func<Observation, bool> receiver, Action<string, string, DeviceHealth> onHealth, CancellationToken token) =>
        StartControlledAsync(Wire.Id(), receiver, x => onHealth(x.DeviceId, x.Epoch, x.Health), token);
    public Task StartControlledAsync(string registeredEpoch, Func<Observation, bool> receiver, Action<DeviceHealthConfirmation> onHealth, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(registeredEpoch)) throw new ArgumentException("Registered epoch required", nameof(registeredEpoch));
        lock (callbackGate)
        {
            if (receive is not null || heartbeatStop is not null) throw new InvalidOperationException("Source already owns a callback/heartbeat");
            receive = receiver; health = onHealth; Epoch = registeredEpoch; sequence = 0; StartCount++;
            heartbeatStop = new(); Confirm(DeviceHealth.Ready);
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
        return Task.CompletedTask;
    }
    void Confirm(DeviceHealth state)
    {
        var at = DateTimeOffset.UtcNow;
        // These five seconds are a synthetic heartbeat contract, never a real SDK TTL.
        health?.Invoke(new(DeviceId, Epoch, state, at, state == DeviceHealth.Ready ? at.AddSeconds(5) : null, confirmationSource));
    }
    public bool Emit(string kind, string admissionId, string[]? codes = null, int? quantity = null, string? sourceId = null)
    {
        lock (callbackGate)
        {
            var callback = receive ?? throw new InvalidOperationException("Source stopped");
            var observation = new Observation(Wire.Id(), DeviceId, Epoch, sourceId ?? Wire.Id(), Interlocked.Increment(ref sequence), admissionId,
                kind, codes ?? [], quantity, DateTimeOffset.UtcNow);
            acquisitions.TryAdd(observation.Epoch + ":" + observation.SourceEventId, new(DeviceId, observation.Epoch, observation.SourceEventId, admissionId, observation.ObservedAt));
            if (acquisitions.Count > 4096) acquisitions.Remove(acquisitions.Keys.First());
            return callback(observation);
        }
    }
    public IImageCamera BindCapture(CapabilityBinding binding, string temporaryDirectory) => new AcquisitionView(this, new ReferenceImageCamera(binding, temporaryDirectory));
    sealed class AcquisitionView(ManualObservationSource owner, IImageCamera port) : IImageCamera
    {
        public string InstanceId => port.InstanceId;
        public Task StartAsync(CancellationToken t) => port.StartAsync(t);
        public Task<CapabilityHealth> HealthAsync(CancellationToken t) => port.HealthAsync(t);
        public Task ReconnectAsync(CancellationToken t) => port.ReconnectAsync(t);
        public Task<StageReply> CaptureAsync(FrozenRequest request, CancellationToken t)
        {
            var acquisition = Wire.Decode<ParcelPayload>(request.Payload).Acquisition;
            lock (owner.callbackGate)
                if (acquisition is null || !owner.acquisitions.TryGetValue(acquisition.Epoch + ":" + acquisition.SourceEventId, out var original) || original != acquisition)
                    throw new IOException("Original acquisition unavailable; latest frame substitution forbidden");
            return port.CaptureAsync(request, t);
        }
        public Task<StageReply?> QueryAsync(FrozenRequest request, CancellationToken t) => port.QueryAsync(request, t);
        public Task StopAsync(CancellationToken t) => port.StopAsync(t);
        public ValueTask DisposeAsync() => port.DisposeAsync();
    }
    public async Task StopAsync(CancellationToken token)
    {
        Task? pending;
        lock (callbackGate) { receive = null; heartbeatStop?.Cancel(); pending = heartbeat; }
        if (pending is not null) await pending.WaitAsync(token).ConfigureAwait(false);
        lock (callbackGate)
        {
            Confirm(DeviceHealth.Stopped); health = null; heartbeat = null;
            heartbeatStop?.Dispose(); heartbeatStop = null;
        }
    }
    public async ValueTask DisposeAsync() => await StopAsync(CancellationToken.None).ConfigureAwait(false);
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
    public Task StartAsync(Func<Observation, bool> receiver, Action<string, string, DeviceHealth> health, CancellationToken token) =>
        StartControlledAsync(Wire.Id(), receiver, x => health(x.DeviceId, x.Epoch, x.Health), token);
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

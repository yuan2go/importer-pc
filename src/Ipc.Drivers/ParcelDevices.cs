using Ipc.Platform;
using Ipc.Application.Contracts;
using Ipc.Reference.Protocol;
namespace Ipc.Drivers;

public sealed class ReferencePlc(CapabilityBinding binding, string peerRoot) : IPlcPort
{
    readonly ReferencePeer peer = new(binding, peerRoot, "plc-submit");
    public string InstanceId => binding.InstanceId;
    public Task StartAsync(CancellationToken token) => peer.StartAsync(token);
    public Task<CapabilityHealth> HealthAsync(CancellationToken token) => peer.HealthAsync(token);
    public async Task ReconnectAsync(CancellationToken token) { await peer.StopAsync(token).ConfigureAwait(false); await peer.StartAsync(token).ConfigureAwait(false); }
    public Task<StageReply> SubmitAsync(FrozenRequest command, CancellationToken token) => peer.SubmitAsync(command, token);
    public Task<StageReply?> QueryAsync(FrozenRequest original, CancellationToken token) => peer.QueryAsync(original, token);
    public Task StopAsync(CancellationToken token) => peer.StopAsync(token);
    public ValueTask DisposeAsync() => peer.DisposeAsync();
}
public sealed class ReferenceWeightSource(CapabilityBinding binding) : IControlledWeightSource, ISyntheticWeightInput
{
    readonly object gate = new();
    Func<WeightMeasurement, bool>? receive;
    bool running, disposed;
    public string InstanceId => binding.InstanceId;
    public Task StartAsync(CancellationToken token)
    { token.ThrowIfCancellationRequested(); lock (gate) { ObjectDisposedException.ThrowIf(disposed, this); running = true; } return Task.CompletedTask; }
    public Task StartControlledMeasurementsAsync(string registeredEpoch, Func<WeightObservation, bool> receiver, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(registeredEpoch)) throw new ArgumentException("Registered epoch required", nameof(registeredEpoch));
        long sequence = 0;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!running || receive is not null) throw new InvalidOperationException("Scale is stopped/already registered");
            receive = m => receiver(new(registeredEpoch, Interlocked.Increment(ref sequence), m));
        }
        return Task.CompletedTask;
    }
    public bool Emit(WeightMeasurement measurement)
    { lock (gate) { return running && measurement.DeviceId == binding.Target && receive?.Invoke(measurement) == true; } }
    public Task<CapabilityHealth> HealthAsync(CancellationToken token)
    { token.ThrowIfCancellationRequested(); lock (gate) { var now = DateTimeOffset.UtcNow; return Task.FromResult(new CapabilityHealth(InstanceId, running, now, now.AddSeconds(3), "SIMULATION: correlated scale input, no real measurement")); } }
    public Task ReconnectAsync(CancellationToken token) => StartAsync(token);
    public Task StopAsync(CancellationToken token)
    { token.ThrowIfCancellationRequested(); lock (gate) { receive = null; running = false; } return Task.CompletedTask; }
    public async ValueTask DisposeAsync() { await StopAsync(CancellationToken.None).ConfigureAwait(false); disposed = true; }
}

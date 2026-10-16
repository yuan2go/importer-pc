using Ipc.Platform;

namespace Ipc.Reference.BarcodeExtension;

// Independently built, SDK-free synthetic observation adapter. It owns real bounded
// callback/liveness state; accepted events enter the host's existing business pipeline.
public sealed class BarcodeModule : IObservationExtension
{
    public IControlledObservationSource Create(ExtensionContext context) => new BarcodeSource(context);
}

sealed class BarcodeSource(ExtensionContext context) : IControlledObservationSource, ISyntheticObservationInput
{
    readonly object gate = new();
    readonly int heartbeatMs = int.Parse(context.Parameters["health.interval-ms"], System.Globalization.CultureInfo.InvariantCulture);
    readonly string prefix = context.Parameters.GetValueOrDefault("input.prefix", "");
    Func<Observation, bool>? receiver;
    Action<DeviceHealthConfirmation>? confirm;
    CancellationTokenSource? stop;
    Task? heartbeat;
    long sequence;
    public string DeviceId => context.DeviceId;
    public string Epoch { get; private set; } = "not-started";
    public string ConfirmationSource => "SIMULATION:extension:" + context.InstanceId;
    public Task StartAsync(Func<Observation, bool> receive, Action<string, string, DeviceHealth> health, CancellationToken token) =>
        throw new InvalidOperationException("Controlled epoch registration required");
    public Task StartControlledAsync(string registeredEpoch, Func<Observation, bool> receive, Action<DeviceHealthConfirmation> health, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (stop is not null) throw new InvalidOperationException("Source still owns its previous registration");
            if (context.Parameters.GetValueOrDefault("fault.fail-start") == "true") throw new InvalidOperationException("Explicit synthetic extension startup fault");
            Epoch = registeredEpoch; receiver = receive; confirm = health; sequence = 0; stop = new();
            var cancellation = stop.Token; Confirm(DeviceHealth.Ready);
            heartbeat = Task.Run(async () =>
            {
                try
                {
                    using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(heartbeatMs));
                    while (await timer.WaitForNextTickAsync(cancellation).ConfigureAwait(false)) lock (gate) if (receiver is not null) Confirm(DeviceHealth.Ready);
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            });
        }
        return Task.CompletedTask;
    }
    void Confirm(DeviceHealth health)
    {
        var now = DateTimeOffset.UtcNow;
        confirm?.Invoke(new(DeviceId, Epoch, health, now, health == DeviceHealth.Ready ? now.AddMilliseconds(heartbeatMs * 5) : null, ConfirmationSource));
    }
    public bool Emit(string kind, string admissionId, string[] codes, int? quantity, string sourceEventId)
    {
        lock (gate)
        {
            if (receiver is null) throw new InvalidOperationException("Source stopped");
            if (kind == "Read" && codes.Any(x => !x.StartsWith(prefix, StringComparison.Ordinal)))
                codes = []; // Real no-code observation; never substitute an earlier accepted code.
            return receiver(new(Wire.Id(), DeviceId, Epoch, sourceEventId, ++sequence, admissionId, kind, codes, quantity, DateTimeOffset.UtcNow));
        }
    }
    public async Task StopAsync(CancellationToken token)
    {
        Task? running;
        lock (gate) { receiver = null; stop?.Cancel(); running = heartbeat; }
        if (running is not null) await running.WaitAsync(token).ConfigureAwait(false);
        lock (gate) { Confirm(DeviceHealth.Stopped); confirm = null; heartbeat = null; stop?.Dispose(); stop = null; }
    }
    public async ValueTask DisposeAsync() => await StopAsync(CancellationToken.None).ConfigureAwait(false);
}

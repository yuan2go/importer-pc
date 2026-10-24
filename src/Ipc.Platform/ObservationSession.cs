namespace Ipc.Platform;

// One registration per physical source. Health and event callbacks remain fenced to
// that registration even if a misbehaving old callback arrives after reconnect.
public sealed class ObservationSession(IControlledObservationSource source)
{
    string epoch = Wire.Id();
    DeviceState? health;
    long sequence;
    readonly object publication = new();
    internal Action<DeviceHealthConfirmation>? BeforeHealthPublish { get; set; }
    public IControlledObservationSource Source => source;
    public string Epoch => Volatile.Read(ref epoch);
    public DeviceState? Health => Volatile.Read(ref health);
    public bool AcceptSequence(Observation observation)
    {
        lock (publication)
        {
            if (observation.DeviceId != source.DeviceId || observation.Epoch != Epoch || observation.Sequence <= sequence) return false;
            sequence = observation.Sequence;
            if (Health is { } confirmed) Volatile.Write(ref health, confirmed with { Sequence = sequence });
            return true;
        }
    }
    public Task StartAsync(Func<Observation, bool> receive, CancellationToken token)
        => StartAsync(receive, null, token);
    Action<DeviceHealthConfirmation>? healthChanged;
    public Task StartAsync(Func<Observation, bool> receive, Action<DeviceHealthConfirmation>? onHealthChanged, CancellationToken token)
    {
        healthChanged = onHealthChanged;
        var registration = Epoch;
        return source.StartControlledAsync(registration, o => o.DeviceId == source.DeviceId && o.Epoch == registration && registration == Epoch && receive(o), h =>
        {
            if (h.DeviceId == source.DeviceId && h.Epoch == registration && registration == Epoch)
            {
                BeforeHealthPublish?.Invoke(h);
                DeviceState? previous;
                lock (publication)
                {
                    // Reconnect can retire the registration after the callback's
                    // first check. Comparison and publication must be indivisible.
                    if (registration != Epoch) return;
                    previous = Health;
                    Volatile.Write(ref health, new(h.DeviceId, h.Epoch, sequence, h.Health, h.ConfirmedAt, h.ConfirmedAt, h.ValidUntil, h.Source));
                }
                if (previous is null || previous.Health != h.Health || previous.Epoch != h.Epoch) onHealthChanged?.Invoke(h);
            }
        }, token);
    }
    public async Task ReconnectAsync(Func<Observation, bool> receive, CancellationToken token)
    {
        lock (publication) { Volatile.Write(ref epoch, Wire.Id()); sequence = 0; Volatile.Write(ref health, null); }
        await source.StopAsync(token).ConfigureAwait(false);
        await StartAsync(receive, healthChanged, token).ConfigureAwait(false);
    }
}

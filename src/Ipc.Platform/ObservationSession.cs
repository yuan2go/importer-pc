namespace Ipc.Platform;

// One registration per physical source. Health and event callbacks remain fenced to
// that registration even if a misbehaving old callback arrives after reconnect.
public sealed class ObservationSession(IControlledObservationSource source)
{
    string epoch = Wire.Id();
    DeviceState? health;
    long sequence;
    public IControlledObservationSource Source => source;
    public string Epoch => Volatile.Read(ref epoch);
    public DeviceState? Health => Volatile.Read(ref health);
    public bool AcceptSequence(Observation observation)
    {
        if (observation.DeviceId != source.DeviceId || observation.Epoch != Epoch || observation.Sequence <= Interlocked.Read(ref sequence)) return false;
        Interlocked.Exchange(ref sequence, observation.Sequence);
        if (Health is { } confirmed) Volatile.Write(ref health, confirmed with { Sequence = sequence });
        return true;
    }
    public Task StartAsync(Func<Observation, bool> receive, CancellationToken token)
    {
        var registration = Epoch;
        return source.StartControlledAsync(registration, o => o.DeviceId == source.DeviceId && o.Epoch == registration && registration == Epoch && receive(o), h =>
        {
            if (h.DeviceId == source.DeviceId && h.Epoch == registration && registration == Epoch)
                Volatile.Write(ref health, new(h.DeviceId, h.Epoch, Interlocked.Read(ref sequence), h.Health, h.ConfirmedAt, h.ConfirmedAt, h.ValidUntil, h.Source));
        }, token);
    }
    public async Task ReconnectAsync(Func<Observation, bool> receive, CancellationToken token)
    {
        Volatile.Write(ref epoch, Wire.Id()); Interlocked.Exchange(ref sequence, 0); Volatile.Write(ref health, null);
        await source.StopAsync(token).ConfigureAwait(false);
        await StartAsync(receive, token).ConfigureAwait(false);
    }
}

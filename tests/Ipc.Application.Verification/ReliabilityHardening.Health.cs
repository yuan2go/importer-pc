using Ipc.Platform;

static partial class ReliabilityHardening
{
    static async Task OldHealth()
    {
        var source = new ControlledReader(); var session = new ObservationSession(source);
        await session.StartAsync(_ => true, default);
        var oldHealth = source.Health!; var oldReceive = source.Receive!; var oldEpoch = session.Epoch;
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        session.BeforeHealthPublish = h => { if (h.Source == "old-late") { entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException(); } };
        var now = DateTimeOffset.UtcNow;
        var callback = Task.Run(() => oldHealth(new(source.DeviceId, oldEpoch, DeviceHealth.Faulted, now, now.AddSeconds(10), "old-late")));
        Check(entered.Wait(TimeSpan.FromSeconds(2)), "old health callback reached the registration-check/publication interleaving");
        try { await session.ReconnectAsync(_ => true, default); } finally { release.Set(); }
        await callback;
        Check(session.Health is { Health: DeviceHealth.Ready } health && health.Epoch == session.Epoch,
            "old health publication cannot overwrite the new reconnect registration");
        Check(!oldReceive(new(Wire.Id(), source.DeviceId, oldEpoch, "old", 9, "old", "parcel", ["1"], null, now)), "old observation registration is rejected after reconnect");
        await source.DisposeAsync();
    }
    sealed class ControlledReader : IControlledObservationSource
    {
        public string DeviceId => "synthetic-health";
        public string Epoch => "source-does-not-own-registration";
        public string ConfirmationSource => "controlled-health";
        public Action<DeviceHealthConfirmation>? Health;
        public Func<Observation, bool>? Receive;
        public Task StartControlledAsync(string epoch, Func<Observation, bool> receive, Action<DeviceHealthConfirmation> health, CancellationToken token)
        { Receive = receive; Health = health; var now = DateTimeOffset.UtcNow; health(new(DeviceId, epoch, DeviceHealth.Ready, now, now.AddSeconds(10), ConfirmationSource)); return Task.CompletedTask; }
        public Task StartAsync(Func<Observation, bool> receive, Action<string, string, DeviceHealth> health, CancellationToken token) => throw new NotSupportedException();
        public Task StopAsync(CancellationToken token) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

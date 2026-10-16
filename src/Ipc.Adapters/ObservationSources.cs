using Ipc.Platform;
using Ipc.Supply;

namespace Ipc.Adapters;

public sealed class ManualObservationSource(string deviceId) : IObservationSource
{
    Func<Observation, bool>? receive;
    Action<string, string, DeviceHealth>? health;
    long sequence;
    readonly object callbackGate = new();
    public string DeviceId { get; } = deviceId;
    public string Epoch { get; private set; } = Wire.Id();
    public int StartCount { get; private set; }
    public Task StartAsync(Func<Observation, bool> receiver, Action<string, string, DeviceHealth> onHealth, CancellationToken token)
    {
        if (receive is not null) throw new InvalidOperationException("Source already owns a callback");
        token.ThrowIfCancellationRequested(); receive = receiver; health = onHealth; Epoch = Wire.Id(); sequence = 0; StartCount++;
        health(DeviceId, Epoch, DeviceHealth.Ready); return Task.CompletedTask;
    }
    public bool Emit(string kind, string admissionId, string[]? codes = null, int? quantity = null, string? sourceId = null)
    {
        // Stop waits for the short ownership-transfer callback to return. No callback may
        // retain this registration and re-enter after Stop reports completion.
        lock (callbackGate)
        {
            var callback = receive ?? throw new InvalidOperationException("Source stopped");
            return callback(new(Wire.Id(), DeviceId, Epoch, sourceId ?? Wire.Id(), Interlocked.Increment(ref sequence), admissionId,
                kind, codes ?? [], quantity, DateTimeOffset.UtcNow));
        }
    }
    public Task StopAsync(CancellationToken token)
    {
        lock (callbackGate) receive = null;
        health?.Invoke(DeviceId, Epoch, DeviceHealth.Stopped); health = null; return Task.CompletedTask;
    }
    public async ValueTask DisposeAsync() => await StopAsync(CancellationToken.None);
}

// Replay is explicitly advanced, so entity boundaries can be inspected one event at a time.
// Input size/count are limited; it never loads SDKs or follows arbitrary network references.
public sealed record ReplayEvent(string Kind, string AdmissionId, string[] Codes, int? PackageQuantity, string SourceEventId);
public sealed class FileReplaySource(string deviceId, string file) : IObservationSource
{
    readonly ManualObservationSource source = new(deviceId);
    ReplayEvent[] events = [];
    int cursor;
    public string DeviceId => source.DeviceId;
    public string Epoch => source.Epoch;
    public async Task StartAsync(Func<Observation, bool> receiver, Action<string, string, DeviceHealth> health, CancellationToken token)
    {
        if (new FileInfo(file).Length > 1_000_000) throw new InvalidDataException("Replay exceeds size limit");
        events = File.ReadLines(file).Where(x => !string.IsNullOrWhiteSpace(x)).Select(Wire.Decode<ReplayEvent>).ToArray();
        if (events.Length > 2000) throw new InvalidDataException("Replay event limit exceeded");
        cursor = 0; await source.StartAsync(receiver, health, token);
    }
    public bool Advance()
    {
        if (cursor == events.Length) throw new InvalidOperationException("Replay complete; no scene reset performed");
        var e = events[cursor++]; return source.Emit(e.Kind, e.AdmissionId, e.Codes, e.PackageQuantity, e.SourceEventId);
    }
    public Task StopAsync(CancellationToken token) => source.StopAsync(token);
    public ValueTask DisposeAsync() => source.DisposeAsync();
}

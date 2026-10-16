namespace Ipc.Platform;

// Transport-neutral device observations. Names and wire fields are retained for
// existing replay/configuration/state JSON; business admission belongs to Supply.
public enum DeviceHealth { Connecting, Ready, Disconnected, Reconnecting, Faulted, Stopping, Stopped, Unknown }
public sealed record DeviceState(string DeviceId, string Epoch, long Sequence, DeviceHealth Health, DateTimeOffset UpdatedAt);
public sealed record Observation(string Id, string DeviceId, string Epoch, string SourceEventId, long Sequence,
    string AdmissionId, string Kind, string[] Codes, int? PackageQuantity, DateTimeOffset ObservedAt);
public interface IObservationSource : IAsyncDisposable
{
    string DeviceId { get; }
    string Epoch { get; }
    Task StartAsync(Func<Observation, bool> receive, Action<string, string, DeviceHealth> health, CancellationToken token);
    Task StopAsync(CancellationToken token);
}

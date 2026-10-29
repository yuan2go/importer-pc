namespace Ipc.Platform;

// Transport-neutral observations. A read projection never renews a source confirmation.
public enum DeviceHealth { Connecting, Ready, Disconnected, Reconnecting, Faulted, Stopping, Stopped, Unknown }
public sealed record DeviceState(string DeviceId, string Epoch, long Sequence, DeviceHealth Health, DateTimeOffset UpdatedAt,
    DateTimeOffset? ConfirmedAt = null, DateTimeOffset? ValidUntil = null, string Source = "unconfirmed", string? Invalidity = null)
{
    public string? ValidityReason(DateTimeOffset now)
    {
        if (!string.IsNullOrWhiteSpace(Invalidity)) return Invalidity;
        if (Health != DeviceHealth.Ready) return "Health:" + Health;
        if (ConfirmedAt is null || ValidUntil is null || string.IsNullOrWhiteSpace(Source) || Source == "unconfirmed") return "Unconfirmed";
        if (ConfirmedAt > now || ValidUntil <= ConfirmedAt) return "InvalidConfirmationTime";
        return ValidUntil <= now ? "Expired" : null;
    }
    public bool IsValid(DateTimeOffset now) => ValidityReason(now) is null;
}
// ConfirmedAt is the source's reliable confirmation time, not queue delivery/read time.
// Unknown real validity remains null and cannot open a business guard.
public sealed record DeviceHealthConfirmation(string DeviceId, string Epoch, DeviceHealth Health,
    DateTimeOffset ConfirmedAt, DateTimeOffset? ValidUntil, string Source);
public sealed record Observation(string Id, string DeviceId, string Epoch, string SourceEventId, long Sequence,
    string AdmissionId, string Kind, string[] Codes, int? PackageQuantity, DateTimeOffset ObservedAt,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? CandidateIssue = null)
{
    // Optional evidence fields retain the existing constructor/deconstruction ABI.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? InspectionSessionId { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? CaptureSessionId { get; init; }
}
// Runtime owns epoch registration; callbacks cannot replace the accepted epoch.
public interface IControlledObservationSource : IAsyncDisposable
{
    string DeviceId { get; }
    string Epoch { get; }
    string ConfirmationSource { get; }
    Task StartControlledAsync(string registeredEpoch, Func<Observation, bool> receive,
        Action<DeviceHealthConfirmation> health, CancellationToken token);
    Task StopAsync(CancellationToken token);
}

// Service targets are not devices; integrations publish their own health.
public enum ServiceStatus { Ready, Unavailable, Stopping, Stopped, Unknown }
public sealed record ServiceState(string TargetId, string TargetRevision, ServiceStatus Status,
    DateTimeOffset ConfirmedAt, DateTimeOffset ValidUntil, string Source)
{
    public string? ValidityReason(DateTimeOffset now) => Status != ServiceStatus.Ready ? "Health:" + Status :
        ConfirmedAt > now || ValidUntil <= ConfirmedAt || string.IsNullOrWhiteSpace(Source) ? "InvalidConfirmation" : ValidUntil <= now ? "Expired" : null;
    public bool IsValid(DateTimeOffset now) => ValidityReason(now) is null;
}

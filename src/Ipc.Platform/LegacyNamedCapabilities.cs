using Ipc.Platform;
// Public capability types are owned by Platform.
namespace Ipc.Application.Contracts;

public enum WeightSource { Disabled, Configured, Measured }
public enum WeightUnit { Kg, G }
public enum MeasurementQuality { Valid, Unstable, Overload, Invalid }
[method: System.Text.Json.Serialization.JsonConstructor]
public sealed record WeightPolicy(WeightSource Source, WeightUnit Unit, int Precision, decimal? ConfiguredValue = null, int MaximumAgeMs = 3000, int WaitTimeoutMs = 15000);
public sealed record WeightMeasurement(string Id, string AdmissionId, string DeviceId, decimal Value, WeightUnit Unit,
    int Precision, MeasurementQuality Quality, bool Stable, DateTimeOffset MeasuredAt);
[method: System.Text.Json.Serialization.JsonConstructor]
public sealed record CapabilityBinding(string Kind, string Implementation, string Version, string InstanceId, string Target,
    string Contract, Dictionary<string, string> Parameters, PackageReference? Package = null, string? Module = null, bool Enabled = true);
public sealed record CapabilityHealth(string InstanceId, bool Ready, DateTimeOffset ConfirmedAt, DateTimeOffset ValidUntil, string Detail);
public interface ICapabilityPort : IAsyncDisposable
{
    string InstanceId { get; }
    Task StartAsync(CancellationToken token);
    Task<CapabilityHealth> HealthAsync(CancellationToken token);
    Task ReconnectAsync(CancellationToken token);
    Task StopAsync(CancellationToken token);
}

[method: System.Text.Json.Serialization.JsonConstructor]
public sealed record OperatorIdentity(string Subject, string DisplayName, string Authority, string SessionId,
    DateTimeOffset AuthenticatedAt, bool Simulation, DateTimeOffset? ExpiresAt = null, string[]? Grants = null);
public interface IOperatorAuthenticator : ICapabilityPort
{
    Task<OperatorIdentity?> AuthenticateAsync(string subject, string proof, CancellationToken token);
}

using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow;

public abstract class FlowDefinition
{
    public abstract string Id { get; }
    public virtual string[] PositionRoles => ["main"];
    public abstract Task AdvanceAsync(IParcelExecution execution, ParcelOperation operation);
    public abstract ParcelOperation ApplyBusinessReply(IParcelExecution execution, ParcelOperation operation, ParcelStage stage, StageReply reply);
    public abstract bool PredecessorsAccepted(ParcelOperation operation, ParcelStage stage);
    public abstract ParcelStage[] ReconciliationStages(ParcelOperation operation);
    public abstract bool MainSettled(ParcelOperation operation);
    public virtual ParcelOperation RestoreMainProjection(ParcelOperation operation) => operation;
    public abstract string[] ValidateBusiness(ParcelConfiguration config);
    public string[] Validate(ParcelConfiguration c) => [.. ParcelConfigurationRules.Validate(c, Id, PositionRoles), .. ValidateBusiness(c)];

}
public sealed record ParcelPorts(IControlledObservationSource Reader, IWeightSource? Weight, IWcsPort? Wcs, IPlcPort? Plc,
    IReadOnlyDictionary<string, string> ImplementationHashes, IOperatorAuthenticator? Login = null, IImageCamera? Camera = null, IImageUploader? Uploader = null, IOperatorAuthenticator? Maintenance = null,
    IReadOnlyDictionary<string, IControlledObservationSource>? Readers = null, IReadOnlyDictionary<string, ICapabilityPort>? Devices = null);
public interface IParcelCapabilityFactory : IAsyncDisposable
{
    CapabilityRequirements Requirements(CapabilityBinding binding) => new(Query: true);
    bool SimulationIdentity(CapabilityBinding binding) => true;
    string? AdmissionFailure(ParcelConfiguration configuration) => null;
    CapabilityCatalog Catalog() => new([], []);
    IDisposable? ProtectConfiguration(ParcelConfiguration configuration) => null;
    void RegisterBackup(string database, ParcelConfiguration configuration) { }
    Task<BusinessReceipt> ManageAsync(string action, string identity) => throw new InvalidOperationException("Extension maintenance unavailable");
    Task<BusinessReceipt> InstallAsync(string file, string sha256) => throw new InvalidOperationException("Installation unavailable");
    ParcelConfiguration Normalize(ParcelConfiguration configuration);
    void Validate(ParcelConfiguration configuration);
    ParcelPorts Create(ParcelConfiguration configuration);
}
public static class WeightRules
{
    public static ParcelWeight? Configured(WeightPolicy policy) => policy.Source == WeightSource.Configured
        ? new(policy.ConfiguredValue!.Value, policy.Unit, policy.Precision, WeightSource.Configured, null, "CONFIGURED: not a device measurement") : null;
    public static string? Invalid(WeightMeasurement m, ParcelOperation operation, string expectedDevice, DateTimeOffset now)
    {
        var p = operation.WeightPolicy;
        if (p.Source != WeightSource.Measured || m.AdmissionId != operation.AdmissionId || m.DeviceId != expectedDevice) return "Measurement identity does not match the same parcel/device";
        if (!Guid.TryParseExact(m.Id, "N", out _) || m.Quality != MeasurementQuality.Valid || !m.Stable) return "Measurement quality/stability/identity is invalid";
        if (m.Value is <= 0 or > 100000 || m.Unit != p.Unit || m.Precision != p.Precision || decimal.Round(m.Value, p.Precision) != m.Value) return "Measurement value/unit/precision is invalid";
        if (m.MeasuredAt < operation.ObservedAt || m.MeasuredAt > now || now - m.MeasuredAt > TimeSpan.FromMilliseconds(p.MaximumAgeMs)) return "Measurement is stale or outside the parcel observation interval";
        return null;
    }
}

// A narrow transaction/port facade, not a workflow engine. Each Application explicitly
// chooses its steps and interprets its business replies and completion boundary.
public interface IParcelExecution
{
    ParcelOperation Current(string id);
    ParcelOperation PrepareWcs(ParcelOperation operation);
    ParcelOperation PreparePlc(ParcelOperation operation);
    void Persist(ParcelOperation operation, string reason);
    Task<ParcelOperation> ExecuteAsync(ParcelOperation operation, ParcelStage stage);
}

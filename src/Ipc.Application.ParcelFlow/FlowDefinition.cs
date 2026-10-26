using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow;

public abstract class FlowDefinition
{
    public abstract string Id { get; }
    public virtual ParcelConfiguration InitialConfiguration() => throw new InvalidDataException("本Application未声明缺配引导模板；仍可选择原配置或完整备份");
    public virtual string[] PositionRoles => ["main"];
    public virtual BarcodeFilterResult SelectBarcode(ParcelConfiguration configuration, Observation observation) => BarcodeFiltering.Select(null, observation.Codes, observation.CandidateIssue);
    public abstract Task AdvanceAsync(IParcelExecution execution, ParcelOperation operation);
    public abstract ParcelOperation ApplyBusinessReply(IParcelExecution execution, ParcelOperation operation, ParcelStage stage, StageReply reply);
    public abstract bool PredecessorsAccepted(ParcelOperation operation, ParcelStage stage);
    public abstract ParcelStage[] ReconciliationStages(ParcelOperation operation);
    public abstract bool MainSettled(ParcelOperation operation);
    public virtual int RequestTimeoutMs(ParcelOperation operation, ParcelStage stage) => operation.FrozenConfiguration.TimeoutMs;
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
    void ValidateStructure(ParcelConfiguration configuration) => Validate(configuration);
    ParcelPorts Create(ParcelConfiguration configuration);
}
public static class WeightRules
{
    public static bool WithinRange(decimal value, ParcelWeightRange? range) => range is null || value >= range.Minimum && value <= range.Maximum;
    public static ParcelWeight? Configured(WeightPolicy policy) => policy.Source == WeightSource.Configured
        ? new(policy.ConfiguredValue!.Value, policy.Unit, policy.Precision, WeightSource.Configured, null, "CONFIGURED: not a device measurement") : null;
    public static string? Invalid(WeightMeasurement m, ParcelOperation operation, string expectedDevice, DateTimeOffset now)
    {
        var p = operation.WeightPolicy;
        if (p.Source != WeightSource.Measured || m.AdmissionId != operation.AdmissionId || m.DeviceId != expectedDevice) return "Measurement identity does not match the same parcel/device";
        if (!Guid.TryParseExact(m.Id, "N", out _) || m.Quality != MeasurementQuality.Valid || !m.Stable) return "Measurement quality/stability/identity is invalid";
        if (m.Value is <= 0 or > 100000 || m.Unit != p.Unit || m.Precision != p.Precision || decimal.Round(m.Value, p.Precision) != m.Value) return "Measurement value/unit/precision is invalid";
        if (!WithinRange(m.Value, operation.FrozenConfiguration.WeightRange)) return "重量超出本件冻结的业务范围；继续等待有效测量，原等待期限不延长";
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

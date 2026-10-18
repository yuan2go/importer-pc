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
    public string[] Validate(ParcelConfiguration c)
    {
        List<string> e = [];
        if (c.Reader is null || c.Weight is null || c.CapabilityInstances?.Any(x => x is null) == true || c.Images is { Camera: null })
            return ["Malformed configuration: missing reader/weight/camera or null capability inventory"];
        if (c.SchemaVersion is not (1 or 2 or 3) || c.ApplicationId != Id || !ExtensionSchema.IsId(c.StationId) || !Enum.IsDefined(c.Mode)) e.Add("Invalid application/station/schema/mode");
        if (c.SchemaVersion == 1 && c.WeightValidity is not null || c.SchemaVersion >= 2 && (c.WeightValidity is null || !Enum.IsDefined(c.WeightValidity.Value))) e.Add("Weight validity requires explicit configuration schema 2; legacy schema 1 retains UntilDispatch");
        if (c.QueueCapacity is < 1 or > 4096 || c.TimeoutMs is < 100 or > 30000 || c.MaximumOperations is < 1 or > 100000) e.Add("Invalid resource/time/retention budget");
        if (c.Retention is { } retention && (retention.ResidentHistory is < 1 or > 10000 || retention.MinimumFreeBytes is < 1048576 or > 1099511627776)) e.Add("Invalid resident history/storage reserve policy");
        if (c.Reader is null || string.IsNullOrWhiteSpace(c.Reader.DeviceId) || c.Reader.Role != "parcel-reader" || string.IsNullOrWhiteSpace(c.Reader.Driver) || string.IsNullOrWhiteSpace(c.Reader.Revision)) e.Add("A stable parcel reader binding is required");
        if (c.Weight is not { } w || !Enum.IsDefined(w.Source) || !Enum.IsDefined(w.Unit) || w.Precision is < 0 or > 6 || w.MaximumAgeMs is < 100 or > 60000 || w.WaitTimeoutMs is < 100 or > 300000) e.Add("Invalid weight policy");
        else
        {
            if (w.Source == WeightSource.Configured && (w.ConfiguredValue is null or <= 0 or > 100000 || decimal.Round(w.ConfiguredValue.Value, w.Precision) != w.ConfiguredValue)) e.Add("Configured weight requires positive value at declared precision");
            if (w.Source != WeightSource.Configured && w.ConfiguredValue is not null) e.Add("Only configured weight may contain a configured value");
            if ((w.Source == WeightSource.Measured) != (c.Scale is not null)) e.Add("Measured weight requires exactly one scale binding");
        }
        if (c.Images is { } images && (images.Camera is null || images.MaximumBytes is < 1 or > 4194304 || !Enum.IsDefined(images.Strategy) || images.TotalBytes < images.MaximumBytes || images.TotalBytes > 268435456 || images.MaximumPending is < 1 or > 256)) e.Add("Image capture requires a binding and a bounded byte quota");
        e.AddRange(ValidateTopology(c));
        var roleBindings = new[] { c.Scale, c.Wcs, c.Plc, c.Login, c.MaintenanceLogin, c.Images?.Camera, c.Images?.Uploader }.OfType<CapabilityBinding>().ToArray();
        if (roleBindings.Select(x => x.InstanceId).Distinct().Count() != roleBindings.Length) e.Add("Capability roles cannot reuse the same instance identity");
        var bindings = ParcelBindings.All(c);
        if (bindings.Select(x => x.InstanceId).Distinct().Count() != bindings.Length) e.Add("Capability instances must be independent");
        foreach (var b in bindings)
            if (!ExtensionSchema.IsId(b.InstanceId) || !ExtensionSchema.IsVersion(b.Version) || string.IsNullOrWhiteSpace(b.Target) ||
                string.IsNullOrWhiteSpace(b.Contract) || string.IsNullOrWhiteSpace(b.Implementation) || b.Parameters is null ||
                b.Package is not null && (!ExtensionSchema.IsHash(b.Package.ManifestSha256) || string.IsNullOrWhiteSpace(b.Module))) e.Add("Incomplete exact capability identity");
        if (c.Scale is not null && c.Scale.Kind != "weight" || c.Plc is not null && c.Plc.Kind != "plc" || c.Wcs is not null && c.Wcs.Kind != "integration") e.Add("Wrong capability binding kind");
        if (c.MaintenanceLogin is not null && c.MaintenanceLogin.Kind != "login" || c.Login is not null && c.Login.Kind != "login" || c.Images is { } i && (i.Camera?.Kind != "camera" || i.Uploader is not null && i.Uploader.Kind != "upload")) e.Add("Wrong optional capability kind");
        if (c.SessionLifetimeMs is < 100 or > 86400000) e.Add("Invalid session lifetime");
        e.AddRange(ValidateBusiness(c)); return e.ToArray();
    }
    string[] ValidateTopology(ParcelConfiguration c)
    {
        if (c.SchemaVersion < 3) return c.Topology is null ? [] : ["Topology requires explicit schema3"];
        List<string> errors = [];
        if (c.Topology is not { Readers: { Length: > 0 and <= 8 }, Positions: { Length: > 0 and <= 8 } } t ||
            t.Readers.Any(x => x is null) || t.Positions.Any(x => x is null)) return ["Schema3 requires complete bounded device topology"];
        if (!t.Positions.Select(x => x.Id).Order().SequenceEqual(PositionRoles.Order())) errors.Add("Application position roles/count differ from its declared topology");
        if (t.Readers.Length != PositionRoles.Length || t.Readers.Select(x => x.DeviceId).Distinct().Count() != t.Readers.Length ||
            t.Positions.Select(x => x.ReaderDeviceId).Distinct().Count() != t.Positions.Length) errors.Add("Readers cannot be missing, duplicated or shared across positions");
        if (Wire.Encode(c.Reader) != Wire.Encode(t.Readers[0])) errors.Add("Legacy Reader anchor must equal first topology reader; flattening is forbidden");
        if (t.Readers.Any(x => x.Role != "parcel-reader" || !ExtensionSchema.IsId(x.DeviceId) || string.IsNullOrWhiteSpace(x.Driver) || string.IsNullOrWhiteSpace(x.Revision)) ||
            t.Positions.Any(x => !ExtensionSchema.IsId(x.Id) || !t.Readers.Any(d => d.DeviceId == x.ReaderDeviceId))) errors.Add("Invalid reader role/identity or position binding");
        var all = ParcelBindings.All(c);
        foreach (var p in t.Positions)
        {
            var camera = p.CameraInstanceId is null ? c.Images?.Camera : all.SingleOrDefault(x => x.InstanceId == p.CameraInstanceId);
            var scale = p.ScaleInstanceId is null ? c.Scale : all.SingleOrDefault(x => x.InstanceId == p.ScaleInstanceId);
            if (c.Images is not null && (camera is null || camera.Kind != "camera" || !camera.Enabled) || c.Images is null && p.CameraInstanceId is not null) errors.Add("Missing/disabled/wrong position camera");
            if (c.Weight.Source == WeightSource.Measured && (scale is null || scale.Kind != "weight" || !scale.Enabled) || c.Weight.Source != WeightSource.Measured && p.ScaleInstanceId is not null) errors.Add("Missing/disabled/wrong position scale");
        }
        if (errors.Count == 0)
        {
            if (c.Images is not null && ParcelBindings.Camera(c, t.Positions[0].Id)?.InstanceId != c.Images.Camera.InstanceId ||
                c.Scale is not null && ParcelBindings.Scale(c, t.Positions[0].Id)?.InstanceId != c.Scale.InstanceId)
                errors.Add("Legacy camera/scale anchor must match first position binding");
            foreach (var kind in new[] { "camera", "weight" })
            {
                var used = t.Positions.Select(p => kind == "camera" ? ParcelBindings.Camera(c, p.Id) : ParcelBindings.Scale(c, p.Id)).OfType<CapabilityBinding>().ToArray();
                if (used.Select(x => x.Target).Distinct().Count() != used.Length || used.Select(x => x.InstanceId).Distinct().Count() != used.Length) errors.Add("Physical camera/scale cannot be shared across independent positions");
            }
        }
        var roles = new[] { c.Scale, c.Wcs, c.Plc, c.Login, c.MaintenanceLogin, c.Images?.Camera, c.Images?.Uploader }.OfType<CapabilityBinding>().ToArray();
        if (roles.Select(x => x.InstanceId).Distinct().Count() != roles.Length || c.CapabilityInstances is { } inventory && (inventory.Select(x => x.InstanceId).Distinct().Count() != inventory.Length || roles.Any(b => inventory.Any(i => i.InstanceId == b.InstanceId && Wire.Encode(i) != Wire.Encode(b))))) errors.Add("Capability instance identity has contradictory bindings");
        return errors.ToArray();
    }

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

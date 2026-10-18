using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow;

public abstract class FlowDefinition
{
    public abstract string Id { get; }
    public abstract bool RequiresWcs { get; }
    public abstract bool RequiresPlc { get; }
    public abstract string[] ValidateBusiness(ParcelConfiguration config);
    public string[] Validate(ParcelConfiguration c)
    {
        List<string> e = [];
        if (c.SchemaVersion != 1 || c.ApplicationId != Id || !ExtensionSchema.IsId(c.StationId) || !Enum.IsDefined(c.Mode)) e.Add("Invalid application/station/schema/mode");
        if (c.QueueCapacity is < 1 or > 4096 || c.TimeoutMs is < 100 or > 30000 || c.MaximumOperations is < 1 or > 100000) e.Add("Invalid resource/time/retention budget");
        if (c.Reader is null || string.IsNullOrWhiteSpace(c.Reader.DeviceId) || c.Reader.Role != "parcel-reader" || string.IsNullOrWhiteSpace(c.Reader.Driver) || string.IsNullOrWhiteSpace(c.Reader.Revision)) e.Add("A stable parcel reader binding is required");
        if (c.Weight is not { } w || !Enum.IsDefined(w.Source) || !Enum.IsDefined(w.Unit) || w.Precision is < 0 or > 6 || w.MaximumAgeMs is < 100 or > 60000) e.Add("Invalid weight policy");
        else
        {
            if (w.Source == WeightSource.Configured && (w.ConfiguredValue is null or <= 0 or > 100000 || decimal.Round(w.ConfiguredValue.Value, w.Precision) != w.ConfiguredValue)) e.Add("Configured weight requires positive value at declared precision");
            if (w.Source != WeightSource.Configured && w.ConfiguredValue is not null) e.Add("Only configured weight may contain a configured value");
            if ((w.Source == WeightSource.Measured) != (c.Scale is not null)) e.Add("Measured weight requires exactly one scale binding");
        }
        if (RequiresWcs != (c.Wcs is not null) || RequiresPlc != (c.Plc is not null)) e.Add("Application capability dependencies do not match the flow");
        var bindings = new[] { c.Scale, c.Wcs, c.Plc }.OfType<CapabilityBinding>().ToArray();
        if (bindings.Select(x => x.InstanceId).Distinct().Count() != bindings.Length) e.Add("Capability instances must be independent");
        foreach (var b in bindings)
            if (!ExtensionSchema.IsId(b.InstanceId) || !ExtensionSchema.IsVersion(b.Version) || string.IsNullOrWhiteSpace(b.Target) ||
                string.IsNullOrWhiteSpace(b.Contract) || string.IsNullOrWhiteSpace(b.Implementation) || b.Parameters is null ||
                b.Package is not null && (!ExtensionSchema.IsHash(b.Package.ManifestSha256) || string.IsNullOrWhiteSpace(b.Module))) e.Add("Incomplete exact capability identity");
        if (c.Scale is not null && c.Scale.Kind != "weight" || c.Plc is not null && c.Plc.Kind != "plc" || c.Wcs is not null && c.Wcs.Kind != "integration") e.Add("Wrong capability binding kind");
        e.AddRange(ValidateBusiness(c)); return e.ToArray();
    }
}
public sealed record ParcelPorts(IControlledObservationSource Reader, IWeightSource? Weight, IWcsPort? Wcs, IPlcPort? Plc,
    IReadOnlyDictionary<string, string> ImplementationHashes);
public interface IParcelCapabilityFactory : IAsyncDisposable
{
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

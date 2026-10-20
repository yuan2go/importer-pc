using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow;

// Shared format/shape rules only. The three Applications retain their business rules.
public static partial class ParcelConfigurationRules
{
    public static string[] Validate(ParcelConfiguration c, string application, string[] positionRoles)
    {
        List<string> e = [];
        if (c.Reader is null || c.Weight is null || c.CapabilityInstances?.Any(x => x is null) == true || c.Images is { Camera: null })
            return ["Malformed configuration: missing reader/weight/camera or null capability inventory"];
        e.AddRange(ValidateCapabilityInventory(c));
        if (e.Count != 0) return e.ToArray(); // No topology/binding deduplication may hide invalid originals.
        if (c.SchemaVersion is not (1 or 2 or 3) || c.ApplicationId != application || !ExtensionSchema.IsId(c.StationId) || !Enum.IsDefined(c.Mode)) e.Add("Invalid application/station/schema/mode");
        if (c.StationCode is not null && (string.IsNullOrWhiteSpace(c.StationCode) || c.StationCode.Length > 256 || c.StationCode.Any(char.IsControl))) e.Add("Invalid system station code");
        if (c.SchemaVersion == 1 && c.WeightValidity is not null || c.SchemaVersion >= 2 && (c.WeightValidity is null || !Enum.IsDefined(c.WeightValidity.Value))) e.Add("Weight validity requires explicit configuration schema 2; legacy schema 1 retains UntilDispatch");
        if (c.QueueCapacity is < 1 or > 4096 || c.TimeoutMs is < 100 or > 30000 || c.MaximumOperations is < 1 or > 100000) e.Add("Invalid resource/time/retention budget");
        if (c.Retention is { } retention && (retention.ResidentHistory is < 1 or > 10000 || retention.MinimumFreeBytes is < 1048576 or > 1099511627776)) e.Add("Invalid resident history/storage reserve policy");
        if (c.BackupSchedule is { } backup && (!Path.IsPathFullyQualified(backup.Directory) || backup.IntervalMinutes is < 15 or > 10080 || backup.MaximumSets is < 1 or > 1000)) e.Add("备份计划须指定绝对目录、15～10080分钟周期和1～1000份容量上限；不会自动删除原件");
        if (c.Reader is null || string.IsNullOrWhiteSpace(c.Reader.DeviceId) || c.Reader.Role != "parcel-reader" || string.IsNullOrWhiteSpace(c.Reader.Driver) || string.IsNullOrWhiteSpace(c.Reader.Revision)) e.Add("A stable parcel reader binding is required");
        if (c.Weight is not { } w || !Enum.IsDefined(w.Source) || !Enum.IsDefined(w.Unit) || w.Precision is < 0 or > 6 || w.MaximumAgeMs is < 100 or > 60000 || w.WaitTimeoutMs is < 100 or > 300000) e.Add("Invalid weight policy");
        else
        {
            if (w.Source == WeightSource.Configured && (w.ConfiguredValue is null or <= 0 or > 100000 || decimal.Round(w.ConfiguredValue.Value, w.Precision) != w.ConfiguredValue)) e.Add("Configured weight requires positive value at declared precision");
            if (w.Source != WeightSource.Configured && w.ConfiguredValue is not null) e.Add("Only configured weight may contain a configured value");
            if ((w.Source == WeightSource.Measured) != (c.Scale is not null)) e.Add("Measured weight requires exactly one scale binding");
        }
        if (c.Images is { } images && (images.Camera is null || images.MaximumBytes is < 1 or > 4194304 || !Enum.IsDefined(images.Strategy) || images.TotalBytes < images.MaximumBytes || images.TotalBytes > 268435456 || images.MaximumPending is < 1 or > 256)) e.Add("Image capture requires a binding and a bounded byte quota");
        e.AddRange(ValidateTopology(c, positionRoles));
        if (c.Scale is not null && c.Scale.Kind != "weight" || c.Plc is not null && c.Plc.Kind != "plc" || c.Wcs is not null && c.Wcs.Kind != "integration") e.Add("Wrong capability binding kind");
        if (c.MaintenanceLogin is not null && c.MaintenanceLogin.Kind != "login" || c.Login is not null && c.Login.Kind != "login" || c.Images is { } i && (i.Camera?.Kind != "camera" || i.Uploader is not null && i.Uploader.Kind != "upload")) e.Add("Wrong optional capability kind");
        if (c.SessionLifetimeMs is < 100 or > 86400000) e.Add("Invalid session lifetime");
        return e.ToArray();
    }
    static string[] ValidateTopology(ParcelConfiguration c, string[] positionRoles)
    {
        if (c.SchemaVersion < 3) return c.Topology is null ? [] : ["Topology requires explicit schema3"];
        List<string> errors = [];
        if (c.Topology is not { Readers: { Length: > 0 and <= 8 }, Positions: { Length: > 0 and <= 8 } } t ||
            t.Readers.Any(x => x is null) || t.Positions.Any(x => x is null)) return ["Schema3 requires complete bounded device topology"];
        if (!t.Positions.Select(x => x.Id).Order().SequenceEqual(positionRoles.Order())) errors.Add("Application position roles/count differ from its declared topology");
        if (t.Readers.Length != positionRoles.Length || t.Readers.Select(x => x.DeviceId).Distinct().Count() != t.Readers.Length ||
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
        return errors.ToArray();
    }

}

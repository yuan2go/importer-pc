using Ipc.Platform;
namespace Ipc.Application.Contracts;

public enum SmParameterAudience { Complete, Field, Technical }

// The stored document and schema remain complete. This is the common editing
// responsibility of existing fields, independent of activation timing.
public static class SmParameterOwnership
{
    public static ParcelConfiguration ProjectCandidate(ParcelConfiguration original, ParcelConfiguration edited, SmParameterAudience audience)
    {
        if (audience == SmParameterAudience.Complete || original.ApplicationId != "sportmaster") return edited;
        var field = audience == SmParameterAudience.Field;
        Dictionary<string, string>? Parameters(Dictionary<string, string>? before, Dictionary<string, string>? after)
        {
            if (before is null && after is null) return null;
            var result = new Dictionary<string, string>(before ?? [], StringComparer.Ordinal);
            foreach (var key in (before?.Keys.AsEnumerable() ?? []).Union(after?.Keys.AsEnumerable() ?? []))
                if (FieldParameter(key) == field)
                {
                    if (after is not null && after.TryGetValue(key, out var value)) result[key] = value;
                    else result.Remove(key);
                }
            return result;
        }
        Ipc.Platform.DeviceBinding Reader(Ipc.Platform.DeviceBinding before)
        {
            var after = ParcelBindings.Readers(edited).SingleOrDefault(r => r.DeviceId == before.DeviceId)
                ?? throw new InvalidDataException("设置模板不能改变现有相机归属。");
            return before with { Parameters = Parameters(before.Parameters, after.Parameters) };
        }
        CapabilityBinding? Binding(CapabilityBinding? before, CapabilityBinding? after)
        {
            if (before is null) return null;
            if (after?.InstanceId != before.InstanceId) throw new InvalidDataException("设置模板不能改变现有组件归属。");
            return before with { Target = field ? after.Target : before.Target, Parameters = Parameters(before.Parameters, after.Parameters)! };
        }
        var result = original with
        {
            StationCode = field ? edited.StationCode : original.StationCode,
            DeferredConnections = field ? edited.DeferredConnections : original.DeferredConnections,
            ReadTimeoutMs = field ? original.ReadTimeoutMs : edited.ReadTimeoutMs,
            BarcodeFilter = field ? original.BarcodeFilter : edited.BarcodeFilter,
            Retention = field ? original.Retention : edited.Retention,
            BackupSchedule = field ? original.BackupSchedule : edited.BackupSchedule,
            OperationsPolicy = field ? original.OperationsPolicy : edited.OperationsPolicy,
            Images = field ? original.Images : edited.Images,
            Reader = Reader(original.Reader),
            Topology = original.Topology is { } topology ? topology with { Readers = topology.Readers.Select(Reader).ToArray() } : null,
            Wcs = Binding(original.Wcs, edited.Wcs),
            CapabilityInstances = original.CapabilityInstances?.Select(b => Binding(b, edited.CapabilityInstances?.SingleOrDefault(c => c.InstanceId == b.InstanceId))!).ToArray()
        };
        return result;
    }
    public static bool FieldParameter(string key) => key is
        "device.ip" or "device.serial" or "zone-id" or "project-id" or
        "left-bcr-code" or "right-bcr-code" or "destination-mode" or "destination-chute-code" or "no-dest-to-exp";

    public static bool FieldEditor(string path) => FieldParameter(path.Split('/').Last()) || path.Split('/').Last() is
        "StationCode" or "Target" or "ConfigureLater" or "ReadSerial" or
        "ShowPace" or "WindowOpeningMode" or "CueVolume";

    public static bool Show(SmParameterAudience audience, string path) => audience == SmParameterAudience.Complete ||
        FieldEditor(path) == (audience == SmParameterAudience.Field);
}

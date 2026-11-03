using Ipc.Platform;
namespace Ipc.Application.Contracts;

// Which configured devices a saved change reconnects. Camera IDs are reader device IDs;
// Integration means the RCS connection (its binding or the station code it reports).
public sealed record LiveDeviceChange(string[] Readers, bool Integration);

// The review and the executing Application use the same whole-candidate rules.
// They predict the mode only; execution still rechecks permission/CAS/drain and may
// fall back to a controlled restart when the runtime cannot swap the devices safely.
public static class ConfigurationActivation
{
    public static bool RulesOnly(string application, ParcelConfiguration effective, ParcelConfiguration? pending, ParcelConfiguration candidate)
        => application == "sportmaster" && pending is null &&
           Wire.Encode(candidate with { BarcodeFilter = effective.BarcodeFilter }) == Wire.Encode(effective);

    // SM only. A change applies live when, apart from the barcode rules, it touches only
    // the parameters of configured cameras and/or the configured RCS connection. Identities,
    // drivers and the configured/unconfigured state of each device must stay the same:
    // enabling or disabling a device, or any other setting, still needs a controlled restart.
    public static LiveDeviceChange? LiveDevices(string application, ParcelConfiguration effective, ParcelConfiguration? pending, ParcelConfiguration candidate)
    {
        if (application != "sportmaster" || pending is not null || effective.Topology is not { } before || candidate.Topology is not { } after ||
            before.Readers.Length != after.Readers.Length || Wire.Encode(effective.DeferredConnections ?? []) != Wire.Encode(candidate.DeferredConnections ?? []))
            return null;
        List<string> readers = [];
        for (var i = 0; i < before.Readers.Length; i++)
        {
            var (old, next) = (before.Readers[i], after.Readers[i]);
            if (old.DeviceId != next.DeviceId || old.Driver != next.Driver || old.Role != next.Role) return null;
            if (Wire.Encode(old) == Wire.Encode(next)) continue;
            if (ParcelConnectionSetup.IsDeferred(candidate, next.DeviceId)) return null;
            readers.Add(next.DeviceId);
        }
        var integration = Wire.Encode(effective.Wcs) != Wire.Encode(candidate.Wcs) || effective.StationCode != candidate.StationCode;
        if (integration && (effective.Wcs is not { } wcs || candidate.Wcs is not { } nextWcs || wcs.InstanceId != nextWcs.InstanceId ||
            wcs.Implementation != nextWcs.Implementation || wcs.Package is not null || nextWcs.Package is not null || ParcelConnectionSetup.IsDeferred(candidate, nextWcs.InstanceId)))
            return null;
        if (readers.Count == 0 && !integration) return null;
        // Put the live-applicable parts back to their effective values; whatever still differs needs a restart.
        var instances = candidate.CapabilityInstances?.Select(x => x.InstanceId == effective.Wcs?.InstanceId
            ? effective.CapabilityInstances?.SingleOrDefault(e => e.InstanceId == x.InstanceId) ?? x : x).ToArray();
        var rest = candidate with
        {
            BarcodeFilter = effective.BarcodeFilter,
            StationCode = effective.StationCode,
            Wcs = effective.Wcs,
            Reader = effective.Reader,
            Topology = after with { Readers = before.Readers },
            CapabilityInstances = instances
        };
        return Wire.Encode(rest) == Wire.Encode(effective) ? new([.. readers], integration) : null;
    }
}

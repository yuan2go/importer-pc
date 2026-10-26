using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Integrations.SportmasterRcs;

// Component-owned pure rules, compiled by the static host preflight and the component.
// No module loading, SDK, socket or resource acquisition occurs here.
internal static class RcsConfigurationValidation
{
    internal const string Contract = "SPORTMASTER-RCS-SORTER-v1";
    internal static (string Station, string Bcr)[] HeartbeatMapping(ParcelConfiguration configuration)
    {
        var station = configuration.StationCode;
        var readers = ParcelBindings.Readers(configuration);
        if (!ValidCode(station) || readers.Length is < 1 or > 64 || readers.Any(r => !ValidCode(r.DeviceId)) ||
            readers.Select(r => r.DeviceId).Distinct(StringComparer.Ordinal).Count() != readers.Length)
            throw new InvalidDataException("RCS heartbeat requires an explicit system StationCode and distinct configured reader DeviceIds");
        string BcrCode(DeviceBinding reader)
        {
            // Manual input is explicitly synthetic and has no hardware driver parameters.
            if (reader.Driver == "manual" && configuration.Mode == RunMode.Simulation) return reader.DeviceId;
            var instance = configuration.Extensions?.Instances.SingleOrDefault(x => "extension:" + x.Id == reader.Driver && x.DeviceId == reader.DeviceId && x.Enabled);
            var code = reader.Driver == "tcp-line" ? reader.Parameters?.GetValueOrDefault("bcr-code") : instance?.Parameters.GetValueOrDefault("bcr-code");
            return ValidCode(code) ? code! : throw new InvalidDataException("RCS heartbeat requires bcr-code in the selected reader driver's configuration: " + reader.DeviceId);
        }
        // Keep topology identity checks above even for deferred readers. Only an
        // explicitly configured reader participates in the heartbeat contract;
        // neither a missing code nor a stale draft code advertises a deferred one.
        var mapping = readers.Where(r => !ParcelConnectionSetup.IsDeferred(configuration, r.DeviceId))
            // A disabled selected extension cannot assert a live reader identity.
            .Where(r => configuration.Extensions?.Instances.SingleOrDefault(i => "extension:" + i.Id == r.Driver && i.DeviceId == r.DeviceId)?.Enabled != false)
            .Select(r => (Station: station!, Bcr: BcrCode(r))).ToArray();
        if (mapping.Select(p => p.Bcr).Distinct(StringComparer.Ordinal).Count() != mapping.Length)
            throw new InvalidDataException("RCS heartbeat reader bcr-code values must be distinct");
        return mapping;
    }
    static bool ValidCode(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 256 && !value.Any(char.IsControl);
}

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
            var positions = ParcelBindings.Positions(configuration).Where(p => p.ReaderDeviceId == reader.DeviceId).ToArray();
            if (positions.Length != 1 || positions[0].Id is not ("left" or "right"))
                throw new InvalidDataException("RCS camera code requires one explicit left/right position per reader: " + reader.DeviceId);
            var key = positions[0].Id + "-bcr-code";
            var code = configuration.Wcs?.Contract == Contract ? configuration.Wcs.Parameters.GetValueOrDefault(key) : null;
            // One component-owned mapping is also frozen in every destination
            // request. Driver selection/parameters cannot supply a second code.
            return ValidCode(code) ? code! : throw new InvalidDataException("RCS requires the shared camera code: " + key);
        }
        // A configured logical barcode source also serves operator input. Its
        // business code remains valid while the optional physical camera is deferred.
        var mapping = readers.Where(r => !ParcelConnectionSetup.IsDeferred(configuration, r.DeviceId) ||
                ParcelBindings.Positions(configuration).Single(p => p.ReaderDeviceId == r.DeviceId) is { } p &&
                ValidCode(configuration.Wcs?.Parameters.GetValueOrDefault(p.Id + "-bcr-code")))
            // A disabled selected extension cannot assert a live reader identity.
            .Where(r => configuration.Extensions?.Instances.SingleOrDefault(i => "extension:" + i.Id == r.Driver && i.DeviceId == r.DeviceId)?.Enabled != false)
            .Select(r => (Station: station!, Bcr: BcrCode(r))).ToArray();
        if (mapping.Select(p => p.Bcr).Distinct(StringComparer.Ordinal).Count() != mapping.Length)
            throw new InvalidDataException("RCS shared camera code values must be distinct");
        return mapping;
    }
    static bool ValidCode(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 256 && !value.Any(char.IsControl);
}

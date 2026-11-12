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
        // A configured logical barcode source also serves operator input. Its
        // business code remains valid while the optional physical camera is deferred.
        var positions = ParcelBindings.Positions(configuration);
        List<(string Station, string Bcr)> mapping = [];
        foreach (var reader in readers)
        {
            var position = positions.Single(p => p.ReaderDeviceId == reader.DeviceId);
            var key = position.Id + "-bcr-code";
            var code = configuration.Wcs?.Parameters.GetValueOrDefault(key);
            if (string.IsNullOrWhiteSpace(code)) continue;
            // A disabled selected extension cannot assert a live reader identity.
            if (configuration.Extensions?.Instances.SingleOrDefault(i => "extension:" + i.Id == reader.Driver && i.DeviceId == reader.DeviceId)?.Enabled == false) continue;
            if (position.Id is not ("left" or "right"))
                throw new InvalidDataException("RCS camera code requires one explicit left/right position per reader: " + reader.DeviceId);
            // This component-owned code is also frozen in destination requests.
            if (configuration.Wcs?.Contract != Contract || !ValidCode(code))
                throw new InvalidDataException("RCS requires the shared camera code: " + key);
            mapping.Add((station!, code));
        }
        if (mapping.Select(p => p.Bcr).Distinct(StringComparer.Ordinal).Count() != mapping.Count)
            throw new InvalidDataException("RCS shared camera code values must be distinct");
        return mapping.ToArray();
    }
    static bool ValidCode(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 256 && !value.Any(char.IsControl);
}

using System.Globalization;
using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Integrations.SportmasterRcs;

internal sealed class RcsTaskOptions
{
    internal readonly CapabilityBinding Binding;
    internal readonly Uri Endpoint;
    internal readonly string Source, Locale;
    internal readonly long ZoneId, ProjectId, RequestIdBase, RequestIdCount;
    internal readonly int Quantity;
    internal readonly bool SpecifiedChute;
    internal readonly string? ChuteCode;
    internal readonly int HeartbeatIntervalSeconds;
    internal static readonly string[] RequiredKeys = ["source", "locale", "zone-id", "project-id", "quantity", "specified-chute",
        "request-id-base", "request-id-count", "left-station-code", "left-bcr-code", "right-station-code", "right-bcr-code"];

    internal RcsTaskOptions(CapabilityBinding binding)
    {
        if (binding.Parameters is null) throw new InvalidDataException("RCS parameters are required");
        Binding = binding with { Parameters = new(binding.Parameters, StringComparer.Ordinal) };
        if (binding.Kind != "integration" || binding.Contract != SportmasterRcsTaskService.Contract || !binding.Enabled ||
            !Uri.TryCreate(binding.Target, UriKind.Absolute, out var endpoint) || endpoint.Scheme is not ("http" or "https") ||
            endpoint.AbsolutePath != "/rcs/sorter/api" || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0 || endpoint.UserInfo.Length != 0)
            throw new InvalidDataException("RCS requires an enabled integration binding and an explicit HTTP(S) /rcs/sorter/api target without credentials/query/fragment");
        Endpoint = endpoint;
        if (Binding.Parameters.Keys.Except(RequiredKeys.Concat(["chute-code", "heartbeat-interval-seconds"]), StringComparer.Ordinal).Any())
            throw new InvalidDataException("Unknown RCS parameter");
        foreach (var key in RequiredKeys) _ = Text(key, key is "source" or "locale" ? 64 : 256);
        Source = Text("source", 64); Locale = Text("locale", 64);
        ZoneId = Number("zone-id", 1, long.MaxValue); ProjectId = Number("project-id", 1, long.MaxValue);
        Quantity = checked((int)Number("quantity", 1, int.MaxValue));
        RequestIdBase = Number("request-id-base", 0, long.MaxValue - 1);
        RequestIdCount = Number("request-id-count", 1, long.MaxValue - RequestIdBase);
        HeartbeatIntervalSeconds = Binding.Parameters.ContainsKey("heartbeat-interval-seconds")
            ? checked((int)Number("heartbeat-interval-seconds", 1, 3600)) : 5;
        SpecifiedChute = Text("specified-chute", 5) switch { "true" => true, "false" => false, _ => throw new InvalidDataException("specified-chute must be true or false") };
        ChuteCode = Binding.Parameters.ContainsKey("chute-code") ? Text("chute-code", 64) : null;
        if (SpecifiedChute != (ChuteCode is not null)) throw new InvalidDataException("chute-code is required exactly when specified-chute=true");
    }
    internal string Text(string key, int maximum)
    {
        if (!Binding.Parameters.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value) || value.Length > maximum || value.Any(char.IsControl))
            throw new InvalidDataException("Missing/invalid RCS parameter: " + key);
        return value;
    }
    long Number(string key, long minimum, long maximum) =>
        long.TryParse(Text(key, 32), NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value >= minimum && value <= maximum
            ? value : throw new InvalidDataException("Invalid RCS numeric parameter: " + key);
    internal void ValidateOriginal(FrozenRequest request)
    {
        if (request.ApplicationId != "sportmaster" || request.Stage != "wcs-slot" || Wire.Encode(request.Binding) != Wire.Encode(Binding) ||
            Wire.Hash(request.Payload) != request.PayloadHash)
            throw new InvalidDataException("RCS request must retain its original Sportmaster binding and payload");
    }
    internal (string Station, string Bcr) Position(ParcelPayload payload)
    {
        if (payload.PositionId is not ("left" or "right") || payload.ReaderBinding is null || payload.Acquisition is null ||
            payload.Acquisition.DeviceId != payload.ReaderBinding.DeviceId || string.IsNullOrWhiteSpace(payload.ParcelCode) ||
            payload.ParcelCode.Length > 512 || payload.ParcelCode.Any(char.IsControl))
            throw new InvalidDataException("RCS requires an original reader/acquisition and explicit left/right position");
        return (Text(payload.PositionId + "-station-code", 256), Text(payload.PositionId + "-bcr-code", 256));
    }
}

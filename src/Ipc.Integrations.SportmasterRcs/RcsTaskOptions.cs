using System.Globalization;
using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Integrations.SportmasterRcs;

internal sealed class RcsTaskOptions
{
    internal readonly CapabilityBinding Binding;
    internal readonly Uri Endpoint;
    internal readonly string Source, Locale;
    internal readonly long ZoneId, ProjectId;
    internal readonly int HeartbeatIntervalMs;
    internal readonly int RequestTimeoutMs;
    internal readonly bool NoDestToExp;
    internal readonly string? SpecifiedChuteCode;
    internal static readonly string[] RequiredKeys = ["zone-id", "project-id"];
    // Old bindings remain readable as immutable evidence for already-sent requests.
    // These keys have no effect on new requests and are removed on candidate save.
    internal static readonly string[] RetiredKeys = ["request-id-base", "request-id-count", "specified-chute", "chute-code"];

    internal RcsTaskOptions(CapabilityBinding binding)
    {
        if (binding.Parameters is null) throw new InvalidDataException("RCS parameters are required");
        Binding = binding with { Parameters = new(binding.Parameters, StringComparer.Ordinal) };
        if (binding.Kind != "integration" || binding.Contract != RcsConfigurationValidation.Contract || !binding.Enabled ||
            !Uri.TryCreate(binding.Target, UriKind.Absolute, out var endpoint) || endpoint.Scheme is not ("http" or "https") ||
            endpoint.AbsolutePath != "/rcs/sorter/api" || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0 || endpoint.UserInfo.Length != 0)
            throw new InvalidDataException("RCS requires an enabled integration binding and an explicit HTTP(S) /rcs/sorter/api target without credentials/query/fragment");
        Endpoint = endpoint;
        if (Binding.Parameters.Keys.Except(RequiredKeys.Concat(RetiredKeys).Concat(["left-bcr-code", "right-bcr-code", "heartbeat-interval-ms", "heartbeat-interval-seconds", "request-timeout-ms", "no-dest-to-exp", RcsRequestPolicy.DestinationMode, RcsRequestPolicy.DestinationChuteCode]), StringComparer.Ordinal).Any())
            throw new InvalidDataException("Unknown RCS parameter");
        Source = "BAR"; Locale = "zh_CN";
        ZoneId = Number("zone-id", 1, long.MaxValue); ProjectId = Number("project-id", 1, long.MaxValue);
        (RequestTimeoutMs, HeartbeatIntervalMs, NoDestToExp) = RcsRequestPolicy.ReadSettings(Binding);
        SpecifiedChuteCode = RcsRequestPolicy.SpecifiedChuteCode(Binding.Parameters);
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
        var manual = payload.OperatorInput is { } input && Guid.TryParseExact(input.CommandId, "N", out _) &&
            input.Kind is "Keyboard" or "PdaKeyboard" && !string.IsNullOrWhiteSpace(input.Actor) &&
            payload.AdmissionId == "manual." + input.CommandId && payload.Acquisition is null;
        var camera = payload.OperatorInput is null && payload.Acquisition is { } acquisition && payload.ReaderBinding is { } reader &&
            acquisition.DeviceId == reader.DeviceId;
        if (payload.PositionId is not ("left" or "right") || payload.ReaderBinding is null || (!manual && !camera) || string.IsNullOrWhiteSpace(payload.ParcelCode) ||
            payload.ParcelCode.Length > 512 || payload.ParcelCode.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(payload.StationCode) || payload.StationCode.Length > 256 || payload.StationCode.Any(char.IsControl))
            throw new InvalidDataException("RCS requires an original camera acquisition or explicit operator submission and left/right position");
        var key = payload.PositionId + "-bcr-code";
        return (payload.StationCode, Text(key, 256));
    }
}

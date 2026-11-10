using System.Globalization;
using System.Text.Json;
using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Integrations.SportmasterRcs;

internal static class RcsDestinationProtocol
{
    internal const string Command = "sorter.task.dest_request";
    internal static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    internal static PreparedIntegrationRequest Prepare(RcsTaskOptions options, FrozenRequest request, long sequence)
        => PrepareWire(options, request, sequence, options.SpecifiedChuteCode);

    internal static bool MatchesFrozen(RcsTaskOptions options, FrozenRequest request, PreparedIntegrationRequest frozen)
    {
        using var original = JsonDocument.Parse(frozen.Body);
        var includesPolicy = original.RootElement.GetProperty("data")[0].GetProperty("params").TryGetProperty("noDestToExp", out _);
        // Requests sent before the optional policy existed retain their exact original bytes.
        // An explicit policy in the frozen binding may never be dropped from its wire evidence.
        if (!includesPolicy && (options.Binding.Parameters.ContainsKey("no-dest-to-exp") || options.SpecifiedChuteCode is not null)) return false;
        if (PrepareWire(options, request, frozen.RequestId, options.SpecifiedChuteCode, includesPolicy) == frozen) return true;
        if (options.SpecifiedChuteCode is not null) return false;
        // Original operator submissions used the same BAR envelope as camera reads.
        if (original.RootElement.TryGetProperty("source", out var source) && source.ValueKind == JsonValueKind.String &&
            source.GetString() == options.Source && PrepareWire(options, request, frozen.RequestId, null, includesPolicy, legacySource: true) == frozen) return true;
        // Only validate original wire evidence; this path cannot prepare a new
        // fixed-chute request or change the frozen bytes/target/ID.
        if (options.Binding.Parameters.GetValueOrDefault("specified-chute") != "true" ||
            !long.TryParse(options.Binding.Parameters.GetValueOrDefault("request-id-base"), out var first) ||
            !long.TryParse(options.Binding.Parameters.GetValueOrDefault("request-id-count"), out var count) ||
            first < 0 || count <= 0 || first > long.MaxValue - count || frozen.RequestId <= first || frozen.RequestId > first + count) return false;
        return PrepareWire(options, request, frozen.RequestId, options.Text("chute-code", 64), includesPolicy, legacySource: true) == frozen;
    }

    static PreparedIntegrationRequest PrepareWire(RcsTaskOptions options, FrozenRequest request, long requestId, string? chute,
        bool includePolicy = true, bool legacySource = false)
    {
        options.ValidateOriginal(request);
        if (requestId <= 0) throw new InvalidDataException("RCS requestId must be positive");
        var payload = Wire.Decode<ParcelPayload>(request.Payload);
        var position = options.Position(payload);
        var parameters = new Dictionary<string, object?>
        {
            ["zoneId"] = options.ZoneId, ["projectId"] = options.ProjectId, ["loadCode"] = payload.ParcelCode,
            ["stationCode"] = position.Station, ["bcrCode"] = position.Bcr, ["specifiedChute"] = chute is not null,
            ["chuteCode"] = chute, ["quantity"] = 1
        };
        if (includePolicy) parameters["noDestToExp"] = options.NoDestToExp;
        var source = legacySource ? options.Source : payload.OperatorInput?.Kind switch
        {
            "PdaKeyboard" => "PDA",
            "Keyboard" => "MANUAL",
            _ => options.Source
        };
        var body = JsonSerializer.Serialize(new
        {
            source,
            version = 1,
            requestId,
            locale = options.Locale,
            data = new[] { new { index = 1, command = Command, @params = parameters } }
        }, Json);
        return new(requestId, "application/json", body, Wire.Hash(body));
    }

    internal static (bool Accepted, string? Slot, WcsDestinationResult Destination) Parse(byte[] bytes, PreparedIntegrationRequest original)
    {
        using var doc = JsonDocument.Parse(bytes, new() { MaxDepth = 32 });
        var root = doc.RootElement; UniqueObjects(root);
        if (root.GetProperty("requestId").GetInt64() != original.RequestId) throw new InvalidDataException("RCS requestId mismatch");
        var results = root.GetProperty("result");
        if (results.ValueKind != JsonValueKind.Array || results.GetArrayLength() != 1) throw new InvalidDataException("RCS requires exactly one result");
        var result = results[0];
        if (result.GetProperty("index").GetInt32() != 1 || Text(result, "command", 128) != Command) throw new InvalidDataException("RCS index/command mismatch");
        var accepted = result.GetProperty("success").GetBoolean();
        var code = ResponseText(result, "code", 256);
        var message = ResponseText(result, "msg", 4096) ?? ResponseText(result, "message", 4096);
        var args = result.TryGetProperty("args", out var a) && a.ValueKind != JsonValueKind.Null ? a.GetRawText() : null;
        try
        {
            if (accepted)
            {
                var data = result.GetProperty("data");
                if (code is not null) throw new InvalidDataException("RCS success contradicts its error code");
                var slot = Text(data, "chuteCode", 64);
                return (true, slot, new(WcsDestinationOutcome.Assigned, code, message, args, Text(data, "taskId", 256), Text(data, "skuId", 256),
                    Measurement(data, "length"), Measurement(data, "width"), Measurement(data, "height"), Measurement(data, "weight"), null,
                    AddedText(data, "waveNo", 256), Quantity(data)));
            }
            if (code is null && message is null) throw new InvalidDataException("RCS rejection requires an error code or message");
            if (code == "NEED_UNPACK")
            {
                var data = result.GetProperty("data");
                var chutes = data.GetProperty("chutes");
                if (chutes.ValueKind != JsonValueKind.Array || chutes.GetArrayLength() > 256) throw new InvalidDataException("RCS unpack chute quota exceeded or invalid");
                var items = chutes.EnumerateArray().Select(c => new WcsUnpackChute(Text(c, "chuteNo", 64), Text(c, "orderNo", 256), Nonnegative(c, "remaining"), Text(c, "phase", 128))).ToArray();
                var unpack = new WcsUnpackDetails(Text(data, "barcode", 512), Text(data, "skuId", 256), Nonnegative(data, "scanQty"), items);
                using var sent = JsonDocument.Parse(original.Body);
                if (unpack.Barcode != sent.RootElement.GetProperty("data")[0].GetProperty("params").GetProperty("loadCode").GetString())
                    throw new InvalidDataException("RCS unpack barcode does not match original loadCode");
                return (false, null, new(WcsDestinationOutcome.UnpackRequired, code, message, args, null, unpack.SkuId, null, null, null, null, unpack));
            }
            using var originalRequest = JsonDocument.Parse(original.Body);
            var parameters = originalRequest.RootElement.GetProperty("data")[0].GetProperty("params");
            var manual = parameters.TryGetProperty("noDestToExp", out var policy) && policy.ValueKind == JsonValueKind.False;
            return (false, null, new(WcsDestinationOutcome.Rejected, code, message, args, null, null, null, null, null, null, null, ManualHandlingRequired: manual));
        }
        catch (Exception e) when (e is InvalidDataException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
        { throw new RcsResponseException(message ?? "RCS 未返回有效格口，请查看异常信息。", e); }
    }
    static int Nonnegative(JsonElement e, string name) => e.GetProperty(name).GetInt32() is var n && n >= 0 ? n : throw new InvalidDataException("RCS negative quantity");
    static string? ResponseText(JsonElement e, string name, int maximum)
    {
        if (!e.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        var text = value.GetString();
        if (string.IsNullOrWhiteSpace(text)) return null;
        return text.Length <= maximum && !text.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')) ? text : throw new InvalidDataException("RCS invalid response text: " + name);
    }
    static decimal? Measurement(JsonElement e, string name)
    {
        var value = e.GetProperty(name);
        if (value.ValueKind == JsonValueKind.Null || value.ValueKind == JsonValueKind.String && value.GetString() == "-") return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number) && number >= 0) return number;
        const NumberStyles styles = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent;
        if (value.ValueKind == JsonValueKind.String && decimal.TryParse(value.GetString(), styles, CultureInfo.InvariantCulture, out number) && number >= 0)
            return number;
        throw new InvalidDataException("RCS invalid measurement: " + name);
    }
    static string? Quantity(JsonElement data)
    {
        if (!data.TryGetProperty("quantity", out var value)) return null;
        if (value.ValueKind != JsonValueKind.Number) return OptionalText(data, "quantity", 256);
        if (value.TryGetInt64(out var count) && count >= 0) return value.GetRawText();
        throw new InvalidDataException("RCS invalid quantity");
    }
    static string Text(JsonElement e, string name, int maximum) => OptionalText(e, name, maximum) ?? throw new InvalidDataException("RCS missing field: " + name);
    // Added response fields are optional for older peers/receipts. Preserve response
    // text exactly; numeric request quantity is a different fact and is never a fallback.
    static string? AddedText(JsonElement e, string name, int maximum) =>
        !e.TryGetProperty(name, out _) ? null : OptionalText(e, name, maximum);
    static string? OptionalText(JsonElement e, string name, int maximum)
    {
        var value = e.GetProperty(name); if (value.ValueKind == JsonValueKind.Null) return null;
        var text = value.GetString();
        return !string.IsNullOrWhiteSpace(text) && text.Length <= maximum && !text.Any(char.IsControl) ? text : throw new InvalidDataException("RCS invalid text: " + name);
    }
    internal static void UniqueObjects(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.Object)
        {
            HashSet<string> keys = new(StringComparer.Ordinal);
            foreach (var p in e.EnumerateObject()) { if (!keys.Add(p.Name)) throw new InvalidDataException("RCS duplicate JSON property"); UniqueObjects(p.Value); }
        }
        else if (e.ValueKind == JsonValueKind.Array) foreach (var item in e.EnumerateArray()) UniqueObjects(item);
    }
}

// Created only after the original requestId/index/command have been matched.
internal sealed class RcsResponseException(string message, Exception inner) : Exception(message, inner);

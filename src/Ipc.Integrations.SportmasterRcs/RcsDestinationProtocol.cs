using System.Text.Json;
using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Integrations.SportmasterRcs;

internal static class RcsDestinationProtocol
{
    internal const string Command = "sorter.task.dest_request";
    internal static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    internal static PreparedIntegrationRequest Prepare(RcsTaskOptions options, FrozenRequest request, long sequence, bool verifyLegacyIntent = false)
    {
        options.ValidateOriginal(request);
        if (sequence <= 0 || sequence > options.RequestIdCount) throw new InvalidDataException("RCS request-id range exhausted; allocate a new disjoint range through controlled configuration");
        var payload = Wire.Decode<ParcelPayload>(request.Payload);
        var position = options.Position(payload);
        var requestId = checked(options.RequestIdBase + sequence);
        var body = JsonSerializer.Serialize(new
        {
            source = options.Source, version = 1, requestId, locale = options.Locale,
            data = new[] { new { index = 1, command = Command, @params = new {
                zoneId = options.ZoneId, projectId = options.ProjectId, loadCode = payload.ParcelCode,
                stationCode = position.Station, bcrCode = position.Bcr, specifiedChute = options.SpecifiedChute,
                chuteCode = options.ChuteCode, quantity = verifyLegacyIntent ? options.LegacyQuantity ?? 1 : 1
            } } }
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
        var code = OptionalText(result, "code", 256); var message = OptionalText(result, "message", 4096);
        var args = result.TryGetProperty("args", out var a) && a.ValueKind != JsonValueKind.Null ? a.GetRawText() : null;
        try
        {
            var data = result.GetProperty("data");
            if (accepted)
            {
                if (code is not null) throw new InvalidDataException("RCS success contradicts its error code");
                var slot = Text(data, "chuteCode", 64);
                return (true, slot, new(WcsDestinationOutcome.Assigned, code, message, args, Text(data, "taskId", 256), Text(data, "skuId", 256),
                    Measurement(data, "length"), Measurement(data, "width"), Measurement(data, "height"), Measurement(data, "weight"), null,
                    AddedText(data, "waveNo", 256), AddedText(data, "quantity", 256)));
            }
            if (string.IsNullOrWhiteSpace(code)) throw new InvalidDataException("RCS rejection requires an error code");
            if (code == "NEED_UNPACK")
            {
                var chutes = data.GetProperty("chutes");
                if (chutes.ValueKind != JsonValueKind.Array || chutes.GetArrayLength() > 256) throw new InvalidDataException("RCS unpack chute quota exceeded or invalid");
                var items = chutes.EnumerateArray().Select(c => new WcsUnpackChute(Text(c, "chuteNo", 64), Text(c, "orderNo", 256), Nonnegative(c, "remaining"), Text(c, "phase", 128))).ToArray();
                var unpack = new WcsUnpackDetails(Text(data, "barcode", 512), Text(data, "skuId", 256), Nonnegative(data, "scanQty"), items);
                using var sent = JsonDocument.Parse(original.Body);
                if (unpack.Barcode != sent.RootElement.GetProperty("data")[0].GetProperty("params").GetProperty("loadCode").GetString())
                    throw new InvalidDataException("RCS unpack barcode does not match original loadCode");
                return (false, null, new(WcsDestinationOutcome.UnpackRequired, code, message, args, null, unpack.SkuId, null, null, null, null, unpack));
            }
            return (false, null, new(WcsDestinationOutcome.Rejected, code, message, args, null, null, null, null, null, null, null));
        }
        catch (Exception e) when (e is InvalidDataException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
        { throw new RcsResponseException(message ?? "RCS未返回有效格口，请核对异常信息。", e); }
    }
    static int Nonnegative(JsonElement e, string name) => e.GetProperty(name).GetInt32() is var n && n >= 0 ? n : throw new InvalidDataException("RCS negative quantity");
    static decimal? Measurement(JsonElement e, string name)
    {
        var value = e.GetProperty(name); if (value.ValueKind == JsonValueKind.Null) return null;
        var number = value.GetDecimal(); return number >= 0 ? number : throw new InvalidDataException("RCS negative measurement");
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
    static void UniqueObjects(JsonElement e)
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

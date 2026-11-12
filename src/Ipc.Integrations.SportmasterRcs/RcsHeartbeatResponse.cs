using System.Text.Json;
using Ipc.Application.Contracts;

namespace Ipc.Integrations.SportmasterRcs;

public sealed partial class SportmasterRcsTaskService
{
    internal static StationVehicleState[] ReadHeartbeatVehicles(byte[] bytes, long requestId,
        (string Station, string Bcr)[] mapping, DateTimeOffset received, TimeSpan lifetime)
    {
        // HTTP evidence remains separate from this optional per-camera telemetry.
        // Never infer idle from a missing, failed, mismatched or duplicated result.
        try
        {
            using var document = JsonDocument.Parse(bytes, new() { MaxDepth = 32 });
            var root = document.RootElement;
            RcsDestinationProtocol.UniqueObjects(root);
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("requestId", out var id) ||
                !id.TryGetInt64(out var echoed) || echoed != requestId ||
                !root.TryGetProperty("result", out var results) || results.ValueKind != JsonValueKind.Array ||
                results.GetArrayLength() > mapping.Length) return [];
            List<StationVehicleState> states = [];
            HashSet<int> indices = [];
            foreach (var item in results.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("index", out var index) ||
                    !index.TryGetInt32(out var ordinal) || ordinal < 1 || ordinal > mapping.Length || !indices.Add(ordinal)) return [];
                if (!item.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True ||
                    Text(item, "command") != HeartbeatCommand || !item.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object) continue;
                var target = mapping[ordinal - 1];
                if (Text(data, "stationCode") != target.Station || Text(data, "bcrCode") != target.Bcr) continue;
                var vehicle = Text(data, "agvNo"); var status = Text(data, "status") ?? "UNKNOWN";
                if (vehicle is null) continue;
                states.Add(new(target.Station, target.Bcr, vehicle, status, received, received + lifetime));
            }
            return states.ToArray();
        }
        catch (JsonException) { return []; }
        catch (InvalidOperationException) { return []; }
        catch (InvalidDataException) { return []; }
    }
    static bool ValidHeartbeatConfirmation(byte[] bytes, long requestId, (string Station, string Bcr)[] mapping)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes, new() { MaxDepth = 32 });
            var root = document.RootElement;
            RcsDestinationProtocol.UniqueObjects(root);
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("requestId", out var id) ||
                !id.TryGetInt64(out var echoed) || echoed != requestId ||
                !root.TryGetProperty("result", out var results) || results.ValueKind != JsonValueKind.Array ||
                results.GetArrayLength() != mapping.Length) return false;
            HashSet<int> seen = [];
            foreach (var item in results.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("index", out var index) ||
                    !index.TryGetInt32(out var ordinal) || ordinal < 1 || ordinal > mapping.Length || !seen.Add(ordinal) ||
                    Text(item, "command") != HeartbeatCommand || !item.TryGetProperty("success", out var success) ||
                    success.ValueKind != JsonValueKind.True || !item.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object ||
                    Text(data, "stationCode") != mapping[ordinal - 1].Station || Text(data, "bcrCode") != mapping[ordinal - 1].Bcr) return false;
            }
            return true;
        }
        catch (Exception error) when (error is JsonException or InvalidDataException or InvalidOperationException) { return false; }
    }
    static string? Text(JsonElement element, string name) => element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 and <= 256 } text &&
        !string.IsNullOrWhiteSpace(text) && !text.Any(char.IsControl) ? text : null;
}

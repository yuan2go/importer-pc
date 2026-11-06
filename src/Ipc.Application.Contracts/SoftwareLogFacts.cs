using System.Text.Json;
namespace Ipc.Application.Contracts;

public static class SoftwareLogFacts
{
    public static SoftwareLogEntry Enrich(SoftwareLogEntry entry)
    {
        try
        {
            using var document = JsonDocument.Parse(entry.Text); var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return entry;
            var code = root.TryGetProperty("Event", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
            string? operation = ReadOperation(root);
            if (operation is null && root.TryGetProperty("Detail", out var detail))
            {
                if (detail.ValueKind == JsonValueKind.Object) operation = ReadOperation(detail);
                else if (detail.ValueKind == JsonValueKind.String && detail.GetString() is { Length: > 0 } body)
                { try { using var nested = JsonDocument.Parse(body); operation = ReadOperation(nested.RootElement); } catch (JsonException) { } }
            }
            return entry with { EventCode = code, OperationId = operation };
        }
        catch (JsonException) { return entry; }
    }
    static string? ReadOperation(JsonElement root)
        => root.ValueKind == JsonValueKind.Object && root.TryGetProperty("OperationId", out var op) && op.ValueKind == JsonValueKind.String && Guid.TryParseExact(op.GetString(), "N", out _) ? op.GetString() : null;
    public static bool Matches(SoftwareLogEntry entry, SoftwareLogQuery query)
        => (query.Level.Length == 0 || query.Level.Equals(entry.Level, StringComparison.OrdinalIgnoreCase)) &&
           (query.Source.Length == 0 || entry.Source.Contains(query.Source, StringComparison.OrdinalIgnoreCase)) &&
           (query.Levels.Length == 0 || query.Levels.Contains(entry.Level, StringComparer.OrdinalIgnoreCase)) &&
           (query.Sources.Length == 0 || query.Sources.Contains(entry.Source, StringComparer.OrdinalIgnoreCase)) &&
           (query.OperationId is null || query.OperationId == entry.OperationId) &&
           (!query.KeyEvents || entry.Level is "错误" or "警告" || entry.Source is "RCS请求" or "RCS结果" or "相机识码" or "扫码与准入" or "相机通信" or "参数" or "管理模式" or "管理中心" or "供包状态" or "投递位启停" or "作业" or "存储" or "业务控制");
}

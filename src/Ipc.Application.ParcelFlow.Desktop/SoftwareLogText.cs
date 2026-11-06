using System.Text;
using System.Text.Json;
using Ipc.Application.Contracts;

namespace Ipc.Application.ParcelFlow.Desktop;

// Pure presentation work; callers run it off the Dispatcher. Original evidence is
// unchanged in the application snapshot/files and the complete authorized export.
static class SoftwareLogText
{
    internal const int MaximumCharacters = 65536;
    const string BudgetNotice = "\n\n【内容过长，后面未显示。请用“导出筛选结果”或“导出该时段全部日志”查看完整内容。】";
    public static string Format(IEnumerable<SoftwareLogEntry> entries, CancellationToken token)
    {
        var text = new StringBuilder(); var remaining = MaximumCharacters - 2 * BudgetNotice.Length;
        foreach (var entry in entries)
        {
            token.ThrowIfCancellationRequested();
            var original = Original(entry, token);
            if (text.Length > 0) original = "\n\n" + original;
            if (original.Length > remaining)
            {
                var take = remaining;
                if (take > 0 && char.IsHighSurrogate(original[take - 1])) take--;
                text.Append(original.AsSpan(0, take)); text.Append(BudgetNotice);
                // Disclose the budget at the top as well as the end, within the
                // existing scrollable detail. Do not grow the status/footer area.
                text.Insert(0, BudgetNotice.TrimStart() + "\n\n"); return text.ToString();
            }
            text.Append(original); remaining -= original.Length;
        }
        token.ThrowIfCancellationRequested(); return text.ToString();
    }
    public static string Original(SoftwareLogEntry entry, CancellationToken token)
    {
        var text = new StringBuilder("原始时间：" + entry.At?.ToString("O") + "\n[" + entry.Location.File + ":" + entry.Location.Line + "]\n" + entry.Text);
        try
        {
            using var json = JsonDocument.Parse(entry.Text); bool heading = false;
            void Visit(JsonElement value, string path)
            {
                token.ThrowIfCancellationRequested();
                if (value.ValueKind == JsonValueKind.Object) foreach (var field in value.EnumerateObject()) Visit(field.Value, path + "/" + field.Name);
                else if (value.ValueKind == JsonValueKind.Array) { int index = 0; foreach (var item in value.EnumerateArray()) Visit(item, path + "/" + index++); }
                else if (value.ValueKind == JsonValueKind.String && value.GetString() is { } content && content.Contains('\n'))
                { if (!heading) { text.Append("\n\n原记录中的多行内容："); heading = true; } text.Append("\n\n").Append(path).Append(":\n").Append(content); }
            }
            Visit(json.RootElement, "");
        }
        catch (JsonException) { }
        return text.ToString();
    }
    public static string Summary(string text)
    {
        try
        {
            using var json = JsonDocument.Parse(text);
            if (json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("Source", out var diagnosticSource) &&
                diagnosticSource.GetString() is "RCS请求" or "相机识码" or "扫码与准入" &&
                json.RootElement.TryGetProperty("Detail", out var diagnosticDetail) && diagnosticDetail.ValueKind is JsonValueKind.String or JsonValueKind.Object)
            {
                using var diagnosticDocument = diagnosticDetail.ValueKind == JsonValueKind.String ? JsonDocument.Parse(diagnosticDetail.GetString()!) : null;
                var d = diagnosticDocument?.RootElement ?? diagnosticDetail;
                string Value(string key) => d.TryGetProperty(key, out var item) && item.ValueKind is not JsonValueKind.Null ? item.ToString() : "—";
                var side = Value("PositionId") switch { "left" => "左投递位", "right" => "右投递位", var other => other };
                var action = json.RootElement.GetProperty("Event").GetString();
                string Detail(string key, string label) => Value(key) is "—" or "" ? "" : " · " + label + Value(key);
                return OneLine(diagnosticSource.GetString() == "相机识码"
                    ? $"{side} · {action} · 帧 {Value("FrameNumber")} · 候选 {Value("CandidateCount")} · 本段 {Value("FramesSinceLastLog")} 帧" + Detail("FailureReason", "原因：")
                    : $"{side} · {action}" + Detail("SourceKind", "来源：") + Detail(diagnosticSource.GetString() == "RCS请求" ? "ParcelCode" : "Barcode", "条码 ") + Detail("ProtocolRequestId", "请求 ") + Detail("Reason", "原因：") + Detail("OperationId", "作业 "));
            }
            if (json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("Source", out var origin) && origin.ValueKind == JsonValueKind.String && origin.GetString() == "RCS结果" && json.RootElement.TryGetProperty("Detail", out var payload) && payload.ValueKind == JsonValueKind.String)
            {
                using var result = JsonDocument.Parse(payload.GetString()!); var r = result.RootElement;
                string Field(string key) => r.TryGetProperty(key, out var v) ? v.ValueKind == JsonValueKind.String ? v.GetString() ?? "—" : v.ToString() : "—";
                var state = Field("Result") switch { "Accepted" => "取得格口", "Rejected" => "未分配格口", "Unknown" or "Conflict" => "格口未确认", var other => other };
                var request = r.TryGetProperty("ProtocolRequestId", out var protocol) && protocol.ValueKind != JsonValueKind.Null ? protocol.ToString() : Field("RequestId");
                var reason = Field("ResponseMessage") != "—" ? Field("ResponseMessage") : Field("Failure");
                return OneLine((Field("PositionId") == "left" ? "左投递位" : "右投递位") + " · " + Field("ParcelCode") + " · 请求 " + request + " · " + state + " · 格口 " + Field("Slot") + " · " + Field("ElapsedMs") + " ms · " + reason);
            }
            if (json.RootElement.ValueKind == JsonValueKind.Object)
            {
                // The event name is the readable line ("相机自动重连"); a short plain detail may follow.
                // Nested JSON and long details stay in the original view and export.
                string? Plain(string name) => json.RootElement.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.String ? item.GetString() : null;
                if ((Plain("Event") ?? Plain("event")) is { Length: > 0 } action)
                {
                    var detail = Plain("Detail") ?? Plain("detail");
                    if (Plain("Source") == "相机通信" && detail is { Length: > 0 } && detail.TrimStart().StartsWith('{'))
                    {
                        using var camera = JsonDocument.Parse(detail);
                        if (camera.RootElement.TryGetProperty("Evidence", out var evidence) && evidence.GetString() is { } reason)
                        {
                            var marker = reason.IndexOf("failure=", StringComparison.Ordinal);
                            if (marker >= 0 && reason[(marker + 8)..].Split(';')[0] is { Length: > 0 } failure)
                                return OneLine(action + " · 原因：" + failure);
                        }
                    }
                    return OneLine(action + (detail is { Length: > 0 and <= 80 } d && !d.TrimStart().StartsWith('{') && !d.TrimStart().StartsWith('[') ? " · " + Device(d) : ""));
                }
                foreach (var name in new[] { "message", "Message", "detail", "Detail" })
                    if (Plain(name) is { } item) return OneLine(item);
            }
        }
        catch (JsonException) { }
        return OneLine(text);
    }
    // Camera device ids read as the station's own names.
    static string Device(string text) => System.Text.RegularExpressions.Regex.Replace(text, @"\b[\w-]+\.reader\.(left|right)\b", m => m.Groups[1].Value == "left" ? "左相机" : "右相机");
    // Log sources written with internal names are shown in station words; filtering still uses the original.
    public static string SourceName(string source) => source switch { "StorageCapacity" => "存储容量", "StorageWal" => "存储日志文件", _ => source };
    static string OneLine(string text)
    {
        var length = Math.Min(384, text.Length);
        if (length > 0 && char.IsHighSurrogate(text[length - 1])) length--;
        return text[..length].Replace("\r\n", " · ").Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ') + (length < text.Length ? "…（原文见详情/导出）" : "");
    }
}

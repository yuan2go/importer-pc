using System.Text;
using System.Text.Json;
using Ipc.Application.Contracts;

namespace Ipc.Application.ParcelFlow.Desktop;

// Pure presentation work; callers run it off the Dispatcher. Original evidence is
// unchanged in the application snapshot/files and the complete authorized export.
static class SoftwareLogText
{
    internal const int MaximumCharacters = 65536;
    const string BudgetNotice = "\n\n【已到本次呈现预算，后续内容未显示。请使用“导出全部匹配”或“导出时间范围”，并设置所需上下文，取得完整脱敏原记录。】";
    public static string Notice(string text) => text.EndsWith(BudgetNotice, StringComparison.Ordinal)
        ? "\n详情已到呈现预算；完整脱敏原记录可通过导出取得。" : "";
    public static string Format(IEnumerable<SoftwareLogEntry> entries, CancellationToken token)
    {
        var text = new StringBuilder(); var remaining = MaximumCharacters - BudgetNotice.Length;
        foreach (var entry in entries)
        {
            token.ThrowIfCancellationRequested();
            var original = Original(entry, token);
            if (text.Length > 0) original = "\n\n" + original;
            if (original.Length > remaining)
            {
                var take = remaining;
                if (take > 0 && char.IsHighSurrogate(original[take - 1])) take--;
                text.Append(original.AsSpan(0, take)); text.Append(BudgetNotice); return text.ToString();
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
            if (json.RootElement.ValueKind == JsonValueKind.Object)
                foreach (var name in new[] { "message", "Message", "detail", "Detail", "event", "Event" })
                    if (json.RootElement.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.String) return OneLine(item.GetString() ?? text);
        }
        catch (JsonException) { }
        return OneLine(text);
    }
    static string OneLine(string text)
    {
        var length = Math.Min(384, text.Length);
        if (length > 0 && char.IsHighSurrogate(text[length - 1])) length--;
        return text[..length].Replace("\r\n", " · ").Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ') + (length < text.Length ? "…（原文见详情/导出）" : "");
    }
}

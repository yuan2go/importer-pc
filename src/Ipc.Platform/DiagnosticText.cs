using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
namespace Ipc.Platform;

// Shared at write, query and export boundaries. Only diagnostic text is redacted;
// authoritative business requests and receipt custody bytes must never be rewritten.
public static class DiagnosticText
{
    static readonly Regex sensitiveKey = new("password|passwd|proof|token|secret|authorization|api[-_]?key|credential|private[-_]?key", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
    static readonly Regex sensitiveValue = new("(?i)((?:password|passwd|proof|token|secret|authorization|api[-_]?key|credential)\\s*[:=]\\s*)(?:\"[^\"]*\"|'[^']*'|[^\\s,;}]+)|(?:Bearer|Basic)\\s+[A-Za-z0-9+/=._-]+|(?<=://)[^/@\\s]+:[^/@\\s]+@", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
    static string Text(string value) => sensitiveValue.Replace(value, m => m.Groups[1].Success ? m.Groups[1].Value + "[已隐藏]" : "[已隐藏]");
    static JsonNode? Clean(JsonNode? node)
    {
        if (node is JsonObject obj)
            foreach (var name in obj.Select(p => p.Key).ToArray()) obj[name] = sensitiveKey.IsMatch(name) ? JsonValue.Create("[已隐藏]") : Clean(obj[name]?.DeepClone());
        else if (node is JsonArray array)
            for (var i = 0; i < array.Count; i++) array[i] = Clean(array[i]?.DeepClone());
        else if (node is JsonValue value && value.TryGetValue<string>(out var text)) return JsonValue.Create(Text(text));
        return node;
    }
    public static string Protect(string line)
    {
        try { return Clean(JsonNode.Parse(line))?.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) ?? line; }
        catch (JsonException) { return Text(line); }
    }
}

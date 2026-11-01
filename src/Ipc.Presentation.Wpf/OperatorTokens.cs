using System.Text.Json;

namespace Ipc.Presentation.Wpf;

// One versioned source for native operator resources and presentation mappings.
public static class OperatorTokens
{
    static readonly JsonElement values = Load();
    static JsonElement Load()
    {
        using var stream = typeof(OperatorTokens).Assembly.GetManifestResourceStream("Ipc.OperatorTokens.json")
            ?? throw new InvalidOperationException("Operator tokens resource is missing");
        using var document = JsonDocument.Parse(stream);
        var result = document.RootElement.GetProperty("sportmasterOperator");
        if (result.GetProperty("version").GetInt32() != 1) throw new InvalidOperationException("Unsupported operator tokens");
        return result.Clone();
    }
    static string Color(string key) => values.GetProperty(key).GetString()!;
    public static string Ink => Color("ink");
    public static string Muted => Color("muted");
    public static string Surface => Color("surface");
    public static string Line => Color("line");
    public static string Accent => Color("accent");
    public static string AccentSoft => Color("accentSoft");
    public static string Success => Color("success");
    public static string Danger => Color("danger");
    public static string Warning => Color("warning");
    public static string Inactive => Color("inactive");
    public static string Neutral => Color("neutral");
    public static double TouchHeight => values.GetProperty("touchHeight").GetDouble();
    public static double TouchGap => values.GetProperty("touchGap").GetDouble();
    public static double InstructionSize => values.GetProperty("instructionSize").GetDouble();
}

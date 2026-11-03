using System.IO;
using System.Text.RegularExpressions;

namespace Ipc.Application.Sportmaster.Desktop;

// Offline help is the operator card and fault catalog from docs/operator, embedded at build
// time so the window and the printed card come from the same text. Only the Markdown the two
// documents use is understood: headings, numbered steps, paragraphs and pipe tables.
public enum HelpBlockKind { Title, Heading, Step, Paragraph, Table }
public sealed record HelpBlock(HelpBlockKind Kind, string Text, string[][]? Rows = null);

public static partial class OperatorHelpDocument
{
    public static readonly (string Resource, string Title)[] Documents = [("help.quick-guide.md", "作业卡"), ("help.fault-codes.md", "故障目录")];

    public static string Read(string resource)
    {
        using var stream = typeof(OperatorHelpDocument).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException("帮助内容未随软件安装。");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static HelpBlock[] Parse(string markdown)
    {
        var blocks = new List<HelpBlock>();
        var table = new List<string[]>();
        void FlushTable()
        {
            if (table.Count > 0) blocks.Add(new(HelpBlockKind.Table, "", [.. table]));
            table.Clear();
        }
        foreach (var raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('|'))
            {
                var cells = line.Trim('|').Split('|').Select(c => Inline(c.Trim())).ToArray();
                if (!cells.All(c => SeparatorCell().IsMatch(c))) table.Add(cells);
                continue;
            }
            FlushTable();
            if (line.Length == 0) continue;
            if (line.StartsWith("# ", StringComparison.Ordinal)) blocks.Add(new(HelpBlockKind.Title, Inline(line[2..])));
            else if (line.StartsWith("## ", StringComparison.Ordinal)) blocks.Add(new(HelpBlockKind.Heading, Inline(line[3..])));
            else if (Step().Match(line) is { Success: true } step) blocks.Add(new(HelpBlockKind.Step, step.Groups[1].Value + ". " + Inline(step.Groups[2].Value)));
            else blocks.Add(new(HelpBlockKind.Paragraph, Inline(line)));
        }
        FlushTable();
        return [.. blocks];
    }

    // **bold**, `code` and [text](link) read as plain text; links between the two cards stay as their names.
    public static string Inline(string text) => Link().Replace(text, "$1").Replace("**", "").Replace("`", "");

    [GeneratedRegex(@"^:?-{3,}:?$")] private static partial Regex SeparatorCell();
    [GeneratedRegex(@"^(\d+)\.\s+(.*)$")] private static partial Regex Step();
    [GeneratedRegex(@"\[([^\]]+)\]\([^)]*\)")] private static partial Regex Link();
}

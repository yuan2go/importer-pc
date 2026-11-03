using System.Text.RegularExpressions;
using Ipc.Application.Sportmaster.Desktop;

// IE-P04 offline help: the help window renders docs/operator as embedded at build time.
// This suite parses the real documents so a Markdown construct the window cannot show,
// or a fault code missing from the catalog, fails here instead of on the station.
internal static class OperatorHelp04
{
    static void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); Console.WriteLine("PASS HELP04 " + text); }
    internal static void Run(string workspace)
    {
        string Doc(string name) => File.ReadAllText(Path.Combine(workspace, "docs", "operator", name));
        var guide = OperatorHelpDocument.Parse(Doc("quick-guide.md"));
        var faults = OperatorHelpDocument.Parse(Doc("fault-codes.md"));
        Check(guide.FirstOrDefault() is { Kind: HelpBlockKind.Title, Text: "供包员作业卡" } && guide.Count(b => b.Kind == HelpBlockKind.Step) == 4, "the operator card opens with its title and four steps");
        var text = guide.Concat(faults).SelectMany(b => b.Rows?.SelectMany(r => r) ?? [b.Text]).ToArray();
        Check(text.All(t => !t.Contains("**") && !t.Contains("](") && !t.Contains('`') && !t.StartsWith('#') && !t.Contains('|')), "no Markdown syntax reaches the window");
        var tables = faults.Where(b => b.Kind == HelpBlockKind.Table).ToArray();
        Check(tables.Length >= 1 && tables.All(t => t.Rows!.All(r => r.Length == t.Rows![0].Length) && t.Rows!.All(r => !r.All(c => c.All(ch => ch is '-' or ':')))),
            "tables keep a header, equal columns and no separator rows");
        var used = Regex.Matches(File.ReadAllText(Path.Combine(workspace, "src", "Ipc.Application.ParcelFlow.Desktop", "FaultGuidance.cs")), @"CP-[A-Z]+").Select(m => m.Value).ToHashSet();
        var listed = tables.SelectMany(t => t.Rows!.Skip(1)).Select(r => r[0]).ToHashSet();
        Check(used.Count >= 5 && used.IsSubsetOf(listed), "every fault code the station shows is in the catalog: " + string.Join(", ", used.Order()));
        var project = File.ReadAllText(Path.Combine(workspace, "src", "Ipc.Application.Sportmaster.Desktop", "Ipc.Application.Sportmaster.Desktop.csproj"));
        Check(OperatorHelpDocument.Documents.All(d => project.Contains($"LogicalName=\"{d.Resource}\"") && project.Contains("docs/operator/" + d.Resource["help.".Length..])),
            "both documents are embedded under the names the window reads");
        Check(OperatorHelpDocument.Parse("| a | b |\n|---|:---:|\n| [x](y.md) | **z** |\n\n1. one\n## h").Select(b => b.Kind).SequenceEqual([HelpBlockKind.Table, HelpBlockKind.Step, HelpBlockKind.Heading]) &&
            OperatorHelpDocument.Parse("| a | b |\n|---|:---:|\n| [x](y.md) | **z** |")[0].Rows![1].SequenceEqual(["x", "z"]), "links and emphasis read as plain text");
    }
}

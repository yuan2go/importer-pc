using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace Ipc.Application.Sportmaster.Desktop;

public sealed partial class StationHelpWindow
{
    static FlowDocumentScrollViewer Guide(string resource)
    {
        using var stream = typeof(StationHelpWindow).Assembly.GetManifestResourceStream("Ipc.Sportmaster.Help." + resource + ".md")
            ?? throw new InvalidDataException("随包帮助缺失，请核对当前程序的完整性。");
        if (stream.Length > 128 * 1024) throw new InvalidDataException("随包帮助超出读取预算。");
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true));
        var document = new FlowDocument { FontFamily = UiTypography.Interface, FontSize = UiTypography.Body, PagePadding = new(8) };
        document.SetResourceReference(FlowDocument.ForegroundProperty, "Ink");
        var pending = new List<string>();
        void Add(string text, bool heading = false)
        {
            // These are fixed, embedded product assets. Render text only: no HTML,
            // scripts, remote images, URI navigation or user-selected file paths.
            text = Regex.Replace(text, @"\[([^\]]+)\]\([^)]+\)", "$1", RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(100));
            text = text.Replace("`", "", StringComparison.Ordinal);
            var paragraph = new Paragraph { Margin = new(0, heading ? 14 : 0, 0, 12), LineHeight = UiTypography.Body * 1.5 };
            var fragments = text.Split("**", StringSplitOptions.None);
            for (var i = 0; i < fragments.Length; i++)
                paragraph.Inlines.Add(new Run(fragments[i]) { FontWeight = heading || i % 2 == 1 ? UiTypography.Emphasis : FontWeights.Normal });
            if (heading) paragraph.FontSize = UiTypography.Group;
            document.Blocks.Add(paragraph);
        }
        void Flush()
        {
            if (pending.Count == 0) return;
            Add(string.Join(" ", pending)); pending.Clear();
        }
        foreach (var line in reader.ReadToEnd().Split('\n'))
        {
            var value = line.Trim();
            if (value.Length == 0 || value.StartsWith("```", StringComparison.Ordinal)) { Flush(); continue; }
            if (value.StartsWith('#')) { Flush(); Add(value.TrimStart('#').Trim(), heading: true); }
            else if (value.StartsWith('|'))
            {
                Flush();
                var cells = value.Trim('|').Split('|').Select(c => c.Trim()).ToArray();
                if (cells.All(c => c.Trim('-', ':').Length == 0)) continue;
                Add("**" + cells[0] + "**：" + string.Join(" · ", cells.Skip(1)));
            }
            else if (value.Length >= 3 && char.IsAsciiDigit(value[0]) && value[1] == '.') { Flush(); Add(value); }
            else pending.Add(value);
        }
        Flush();
        return new FlowDocumentScrollViewer { Document = document, IsToolBarVisible = false, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
}

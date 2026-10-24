using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Ipc.Presentation.Wpf;

static partial class Program
{
    // Exercise actual WPF font resolution. Constants alone cannot prove that a
    // packaged face was loaded instead of silently falling back to a system font.
    static async Task VerifyTypography(string evidence, List<string> lines)
    {
        void Check(bool ok, string reason) { if (!ok) throw new InvalidOperationException(reason); lines.Add("PASS FONT " + reason); }
        var panel = new StackPanel { Margin = new Thickness(20) };
        TextBlock Sample(string text, FontFamily family, FontWeight weight)
        {
            var block = new TextBlock { Text = text, FontFamily = family, FontSize = 24, FontWeight = weight, Margin = new Thickness(0, 4, 0, 8) };
            panel.Children.Add(block); return block;
        }
        var regular = Sample("供包台 当前格口 包裹 异常 参数 保存核对 0123456789", UiTypography.Interface, FontWeights.Normal);
        var medium = Sample("供包台 当前格口 包裹 异常 参数 保存核对 0123456789", UiTypography.Interface, FontWeights.Medium);
        var code = Sample("I l 1 | O 0 8 B S 5 Z 2 · 条码与日志原文", UiTypography.Code, FontWeights.Normal);
        var fallback = Sample("中文回退：格口与包裹", new FontFamily(new Uri("pack://application:,,,/Ipc.Presentation.Wpf;component/"), "./Fonts/#UnavailableTypographyFixture, Microsoft YaHei UI"), FontWeights.Normal);
        var missingCharacter = Sample("主字库缺字回退：🚀", UiTypography.Interface, FontWeights.Normal);
        var window = new Window { Title = "Typography synthetic verification", Width = 1000, Height = 360, Content = panel };
        UiTypography.Apply(window); window.Show(); await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        var fontRows = new List<object>();
        foreach (var (block, name, expectedFile, expectedWeight) in new[] {
            (regular, "regular", "notosanscjksc-regular.otf", 400), (medium, "medium", "notosanscjksc-medium.otf", 500),
            (code, "code", "consola", 400), (fallback, "fallback", "msyh", 400),
            (missingCharacter, "missing-character", "seguiemj", 400) })
        {
            block.UpdateLayout();
            var runs = GlyphRuns(block).ToArray();
            Check(runs.Length > 0 && runs.All(r => r.GlyphIndices.All(g => g != 0)), name + " has actual glyphs without .notdef");
            Check(runs.Any(r => r.GlyphTypeface.FontUri.ToString().Contains(expectedFile, StringComparison.OrdinalIgnoreCase) && r.GlyphTypeface.Weight.ToOpenTypeWeight() == expectedWeight), name + " resolves expected font file and real weight");
            if (name is "regular" or "medium") Check(runs.All(r => r.GlyphTypeface.FontUri.ToString().Contains(expectedFile, StringComparison.OrdinalIgnoreCase)), name + " has no unexpected family fallback");
            foreach (var run in runs) fontRows.Add(new { Role = name, Sample = block.Text, Characters = run.Characters is null ? null : new string(run.Characters.ToArray()), FontUri = run.GlyphTypeface.FontUri.ToString(), Weight = run.GlyphTypeface.Weight.ToOpenTypeWeight(), run.GlyphTypeface.Version, GlyphCount = run.GlyphIndices.Count });
        }
        foreach (var (file, expected) in new[] { ("Regular", "2c76254f6fc379fddfce0a7e84fb5385bb135d3e399294f6eeb6680d0365b74b"), ("Medium", "ca094f6b0001fb048ca39ddd797a0cdb0179e1e55c6561e111c49c3e6a61d7b7") })
        {
            using var stream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Ipc.Presentation.Wpf;component/Fonts/NotoSansCJKsc-" + file + ".otf")).Stream;
            Check(Convert.ToHexStringLower(SHA256.HashData(stream)) == expected, file + " embedded bytes match official SHA256");
        }
        using (var license = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Ipc.Presentation.Wpf;component/Fonts/OFL.txt")).Stream)
        using (var reader = new StreamReader(license)) Check((await reader.ReadToEndAsync()).Contains("SIL OPEN FONT LICENSE Version 1.1"), "offline OFL license is embedded");
        CaptureInternal(evidence, "typography-font-resolution", panel, lines);
        File.WriteAllText(Path.Combine(evidence, "FONT-RESOLUTION.json"), JsonSerializer.Serialize(new { OperatingSystem = Environment.OSVersion.ToString(), Dpi = VisualTreeHelper.GetDpi(window).DpiScaleX, Assembly = typeof(UiTypography).Assembly.Location, Fonts = fontRows }, new JsonSerializerOptions { WriteIndented = true }));
        window.Close();
    }
    static IEnumerable<GlyphRun> GlyphRuns(Visual visual)
    {
        static IEnumerable<GlyphRun> Runs(Drawing? drawing)
        {
            if (drawing is GlyphRunDrawing glyph) yield return glyph.GlyphRun;
            if (drawing is DrawingGroup group) foreach (var child in group.Children) foreach (var run in Runs(child)) yield return run;
        }
        foreach (var run in Runs(VisualTreeHelper.GetDrawing(visual))) yield return run;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(visual); i++)
            if (VisualTreeHelper.GetChild(visual, i) is Visual child) foreach (var run in GlyphRuns(child)) yield return run;
    }
}

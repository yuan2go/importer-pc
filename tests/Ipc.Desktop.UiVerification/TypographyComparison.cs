using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Platform;

static partial class Program
{
    // Same fixture is also compiled against the frozen PR97 sources. Only the
    // system-family candidate has test overrides. Never used by production UI.
    static async Task VerifyTypographyComparison(string root, string evidence, List<string> lines, string[] args)
    {
        var variant = args.Contains("--system-family") ? "system" : args.Contains("--baseline") ? "baseline" : "noto";
        var fixturePath = Environment.GetEnvironmentVariable("IPC_TYPOGRAPHY_FIXTURE") ??
            Path.Combine(root, ".artifacts/typography/comparison-fixture.json");
        void Check(bool ok, string reason) { if (!ok) throw new InvalidOperationException(reason); lines.Add("PASS COMPARE " + reason); }
        var configRoot = Path.Combine(evidence, "config"); Directory.CreateDirectory(configRoot);
        var seed = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(root, "config/sportmaster/business.json")));
        var config = seed with { Login = seed.MaintenanceLogin! with { InstanceId = "synthetic.operator", Target = "reference://operator", Parameters = new() { ["subjects"] = "alice" } } };
        ExtensionStore.AtomicWrite(Path.Combine(configRoot, "business.json"), config);
        var launch = new BusinessLaunch(configRoot, Path.Combine(evidence, "data"), null, null);
        await using var runtime = new ParcelRuntime(launch, new SportmasterFlow(), new CommercialProductization.SyntheticFactory(launch)) { RuntimeIdentityPreflight = () => { } };
        await runtime.StartAsync(default);
        var app = DispatchProxy.Create<IManagementParcelFixture, HierarchyProjectionProbe>();
        var probe = (HierarchyProjectionProbe)(object)app; probe.Inner = runtime;
        ParcelSnapshot? frozen = File.Exists(fixturePath) ? Wire.Decode<ParcelSnapshot>(File.ReadAllText(fixturePath)) : null;
        var reports = new List<object>();
        foreach (var ui in new[] { "supply-station", "classic" })
        {
            var window = Presentation.Create(app, null, "standard", ui);
            if (window is ShellWindow shell) shell.ConfirmDiscardDrafts = _ => true;
            else ((SupplyStationWindow)window).ConfirmDiscardDrafts = _ => true;
            window.Show(); await Task.Delay(300);
            var vm = (ParcelViewModel)window.DataContext;
            if (frozen is null)
            {
                await ManagementModeFixture.Authorize(runtime); vm.OperatorSubject = "alice"; await vm.LoginAsync("SIMULATION_ONLY");
                for (var i = 0; i < 100 && vm.Snapshot?.CurrentOperator is null; i++) { await vm.RefreshAsync(); await Task.Delay(20); }
                vm.SelectedPositionId = "left"; vm.AdmissionId = "typography-fixed-entity"; vm.ParcelCode = "000123456789"; vm.Emit();
                for (var i = 0; i < 150 && vm.Current?.State != ParcelState.SlotResolved; i++) { await Task.Delay(20); await vm.RefreshAsync(); }
                Check(vm.Current?.State == ParcelState.SlotResolved, "fixture obtains a synthetic accepted result");
                frozen = vm.Snapshot! with { Operations = vm.Snapshot!.Operations.Select(o => o with { Slot = "L-07",
                    Wcs = o.Wcs! with { Replies = o.Wcs.Replies.Select(r => r with { Slot = "L-07" }).ToArray() } }).ToArray() };
                Directory.CreateDirectory(Path.GetDirectoryName(fixturePath)!); File.WriteAllText(fixturePath, Wire.Encode(frozen));
            }
            var snapshot = frozen;
            probe.Project = _ => snapshot;
            foreach (var (scenario, width, height) in new[] { ("normal", 1366d, 768d), ("normal", 1092d, 614d), ("normal", 800d, 420d), ("unknown", 800d, 420d), ("long", 800d, 420d) })
            {
                var longCode = new string('0', 72) + "123456789";
                var longSlot = "SYNTHETIC-LONG-DESTINATION-" + new string('8', 40);
                snapshot = scenario == "unknown" ? HierarchyState(frozen, StageState.Unknown) : scenario == "long" ? frozen with
                {
                    Operations = frozen.Operations.Select(o => o.PositionId != "left" ? o : o with { ParcelCode = longCode, Slot = longSlot,
                        Wcs = o.Wcs! with { Replies = o.Wcs.Replies.Select(r => r with { Slot = longSlot }).ToArray() } }).ToArray()
                } : frozen;
                await SetClient(window, width, height); await vm.RefreshAsync();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
                var beforeOverrides = new Dictionary<DependencyObject, string>();
                if (variant == "system")
                {
                    foreach (var element in new DependencyObject[] { window }.Concat(Descendants(window)))
                    {
                        var property = element is Control ? Control.FontFamilyProperty : element is TextBlock ? TextBlock.FontFamilyProperty : null;
                        if (property is null) continue;
                        var current = (FontFamily)element.GetValue(property);
                        beforeOverrides[element] = current.Source + " / " + DependencyPropertyHelper.GetValueSource(element, property).BaseValueSource;
                        element.SetValue(property, new FontFamily(current.Source.Contains("Consolas", StringComparison.OrdinalIgnoreCase)
                            ? "Consolas, Microsoft YaHei UI, Segoe UI" : "Microsoft YaHei UI, Segoe UI"));
                    }
                    window.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                }
                var client = (Grid)window.FindName("ClientArea"); var host = (ContentControl)window.FindName("PageHost");
                var key = variant + "-" + ui + "-" + scenario + "-" + (int)width;
                // Crop only the real PageHost: same data/state and client, no changing wall-clock chrome.
                CaptureInternal(evidence, key, host, lines);
                var controls = new DependencyObject[] { window }.Concat(Descendants(window)).OfType<FrameworkElement>()
                    .Where(e => e.IsVisible && (e is TextBox || e is TextBlock || e is Window || e is Button)).ToArray();
                var rows = controls.Select(e => TypographyControlRow(e, client, beforeOverrides.GetValueOrDefault(e))).ToArray();
                reports.Add(new { Variant = variant, Ui = ui, Scenario = scenario, Client = new { client.ActualWidth, client.ActualHeight },
                    Dpi = VisualTreeHelper.GetDpi(window).DpiScaleX, Host = TypographyBounds(host, client), Controls = rows });
                var code = controls.OfType<TextBox>().FirstOrDefault(b => b.Name == "CurrentBarcode" || Equals(b.Tag, "CurrentBarcode"));
                Check(code is not null && code.Text == (scenario == "long" ? longCode : "000123456789"), key + " exact barcode text preserved");
                code!.SelectAll(); Check(code.SelectedText == code.Text, key + " entire barcode remains selectable");
                if (scenario == "unknown") Check(vm.CurrentSlot == "未取得", key + " unknown never exposes accepted slot");
                if (variant == "noto" && !args.Contains("--observe-only"))
                {
                    var chinese = controls.OfType<TextBlock>().Where(b => b.Text.Any(c => c >= 0x4e00 && c <= 0x9fff)).ToArray();
                    Check(chinese.Length > 0, key + " inspects actual production Chinese controls");
                    var runs = chinese.SelectMany(TypographyGlyphRuns).Where(r => r.Characters is not null && r.Characters.Any(c => c >= 0x4e00 && c <= 0x9fff)).ToArray();
                    Check(runs.Length > 0 && runs.All(r => r.GlyphTypeface.FontUri.ToString().Contains("notosanscjksc", StringComparison.OrdinalIgnoreCase) && r.GlyphIndices.All(g => g != 0)),
                        key + " every rendered ordinary Chinese run uses bundled font without missing glyphs");
                }
            }
            window.Close(); for (var i = 0; i < 100 && window.IsVisible; i++) await Task.Delay(20);
        }
        var assemblies = new[] { typeof(SupplyStationWindow).Assembly, typeof(ShellWindow).Assembly, typeof(Program).Assembly }.Select(a => new {
            Name = a.GetName().Name, Sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(a.Location))),
            Version = a.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion });
        File.WriteAllText(Path.Combine(evidence, "COMPARISON.json"), JsonSerializer.Serialize(new { Variant = variant,
            OperatingSystem = Environment.OSVersion.ToString(), FixtureSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(fixturePath))),
            Assemblies = assemblies, Scenes = reports }, new JsonSerializerOptions { WriteIndented = true }));
        lines.Add("Comparison uses frozen synthetic presentation input, not an Application/field acceptance test. Private fixture is not published.");
    }

    static object TypographyBounds(FrameworkElement e, FrameworkElement root)
    {
        var p = e.TranslatePoint(new Point(), root);
        return new { X = p.X, Y = p.Y, Width = e.ActualWidth, Height = e.ActualHeight };
    }
    static object TypographyControlRow(FrameworkElement e, FrameworkElement root, string? beforeOverride = null)
    {
        var familyProperty = e is Control ? Control.FontFamilyProperty : TextBlock.FontFamilyProperty;
        var sizeProperty = e is Control ? Control.FontSizeProperty : TextBlock.FontSizeProperty;
        var weightProperty = e is Control ? Control.FontWeightProperty : TextBlock.FontWeightProperty;
        object Property(DependencyProperty property) => new { Value = e.GetValue(property).ToString(),
            Source = DependencyPropertyHelper.GetValueSource(e, property).BaseValueSource.ToString(),
            DependencyPropertyHelper.GetValueSource(e, property).IsExpression };
        var runs = (e is Window ? [] : TypographyGlyphRuns(e)).Select(r => new { Characters = r.Characters is null ? null : new string(r.Characters.ToArray()),
            FontUri = r.GlyphTypeface.FontUri.ToString(), Weight = r.GlyphTypeface.Weight.ToOpenTypeWeight(),
            Glyphs = r.GlyphIndices.ToArray(), AdvanceWidth = r.AdvanceWidths.Sum(), r.FontRenderingEmSize }).ToArray();
        return new { Type = e.GetType().Name, e.Name, AutomationName = AutomationProperties.GetName(e),
            Text = e is TextBlock block ? block.Text : e is TextBox box ? box.Text : e is Button button ? button.Content?.ToString() : null,
            Family = Property(familyProperty), Size = Property(sizeProperty), Weight = Property(weightProperty), TestOverrideFrom = beforeOverride,
            Bounds = TypographyBounds(e, root), MaxHeight = double.IsInfinity(e.MaxHeight) ? (double?)null : e.MaxHeight,
            Wrapping = e is TextBox tb ? tb.TextWrapping.ToString() : e is TextBlock text ? text.TextWrapping.ToString() : null,
            Scroll = e is TextBox scroll ? new { scroll.ExtentHeight, scroll.ViewportHeight, scroll.VerticalOffset } : null, GlyphRuns = runs };
    }
    static IEnumerable<GlyphRun> TypographyGlyphRuns(Visual visual)
    {
        static IEnumerable<GlyphRun> Runs(Drawing? drawing)
        {
            if (drawing is GlyphRunDrawing glyph) yield return glyph.GlyphRun;
            if (drawing is DrawingGroup group) foreach (var child in group.Children) foreach (var run in Runs(child)) yield return run;
        }
        foreach (var run in Runs(VisualTreeHelper.GetDrawing(visual))) yield return run;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(visual); i++)
            if (VisualTreeHelper.GetChild(visual, i) is Visual child) foreach (var run in TypographyGlyphRuns(child)) yield return run;
    }
}

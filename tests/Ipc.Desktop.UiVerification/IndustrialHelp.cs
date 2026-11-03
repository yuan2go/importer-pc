using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Platform;

static partial class Program
{
    static async Task VerifyIndustrialHelp(string root, string evidence, List<string> lines)
    {
        void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
            lines.Add("PASS IE01-HELP " + message);
        }
        async Task Until(Func<bool> condition)
        {
            for (var i = 0; i < 200 && !condition(); i++) await Task.Delay(20);
            Check(condition(), "native asynchronous state settled");
        }
        var configRoot = Path.Combine(evidence, "help-config"); Directory.CreateDirectory(configRoot);
        var config = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(root, "tests/Ipc.Application.Verification/sportmaster.synthetic.json")));
        File.WriteAllText(Path.Combine(configRoot, "business.json"), Wire.Encode(config));
        var launch = new BusinessLaunch(configRoot, Path.Combine(evidence, "help-data"), null, null);
        await using var runtime = new ParcelRuntime(launch, new SportmasterFlow(), new CommercialProductization.SyntheticFactory(launch)) { RuntimeIdentityPreflight = () => { } };
        await runtime.StartAsync(default);
        var before = await runtime.SnapshotAsync();
        var primary = (SupplyStationWindow)Presentation.Create(runtime, null, "standard");
        primary.Show(); await Task.Delay(300); await SetClient(primary, 1100, 600); primary.UpdateLayout();
        Capture("help-workstation-baseline", primary);
        ApplicationCommands.Help.Execute(null, primary);
        await Until(() => System.Windows.Application.Current.Windows.OfType<StationHelpWindow>().Any());
        var window = System.Windows.Application.Current.Windows.OfType<StationHelpWindow>().Single();
        Check(ReferenceEquals(window.Owner, primary), "actual help command reuses the running workstation owner");
        ApplicationCommands.Help.Execute(null, primary);
        Check(System.Windows.Application.Current.Windows.OfType<StationHelpWindow>().Count() == 1,
            "reopening help activates one existing window instead of creating another reader");
        var destination = Path.Combine(evidence, "ordinary-help.zip");
        typeof(StationHelpWindow).GetProperty("DestinationPicker", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, (Func<Window, string?>)(_ => destination));
        await Task.Delay(150); window.UpdateLayout();
        var tabs = Descendants(window).OfType<TabControl>().Single();
        Check(tabs.Items.Count == 5, "four fixed offline guides and version/diagnostic task are available");
        foreach (var item in tabs.Items.OfType<TabItem>().Take(4))
        {
            tabs.SelectedItem = item; window.UpdateLayout();
            var guide = (FlowDocumentScrollViewer)item.Content;
            Check(new TextRange(guide.Document.ContentStart, guide.Document.ContentEnd).Text.Length > 100,
                "embedded searchable document: " + item.Header);
        }
        tabs.SelectedIndex = 0; window.UpdateLayout(); Capture("help-quick-guide", window);
        tabs.SelectedIndex = 4; window.UpdateLayout();
        var overview = Descendants(window).OfType<Button>().Single(b => Equals(b.Content, "导出普通概览…"));
        var detailed = Descendants(window).OfType<Button>().Single(b => Equals(b.Content, "导出详细诊断…"));
        Check(overview.IsEnabled && !detailed.IsEnabled, "ordinary mode can export overview without enabling detailed sources");
        Check(!primary.ViewModel.Busy, "opening help does not take the workstation operation lock");
        overview.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => !window.IsExporting && File.Exists(destination));
        Check(!primary.ViewModel.Busy, "overview export does not disable normal workstation input");
        using (var zip = ZipFile.OpenRead(destination))
        {
            Check(zip.Entries.Count == 4 && zip.GetEntry("event-index.json") is null && zip.GetEntry("station.json") is null,
                "actual exported ZIP excludes detailed log/configuration/operation artifacts");
            using var manifest = JsonDocument.Parse(zip.GetEntry("manifest.json")!.Open());
            Check(manifest.RootElement.GetProperty("Authority").GetString() == "Operator" && manifest.RootElement.GetProperty("Status").GetString() == "Partial",
                "actual ordinary profile and missing-source status preserved");
        }
        Check(Descendants(window).OfType<TextBlock>().Any(t => t.Text.Contains("含未收集项", StringComparison.Ordinal)),
            "native completion does not turn a Partial receipt into complete success");
        var after = await runtime.SnapshotAsync();
        Check(after.RuntimeId == before.RuntimeId && after.Configuration.Revision == before.Configuration.Revision && after.Paused == before.Paused,
            "help and export preserve the runtime, configuration and pause state");
        foreach (var (width, height) in new[] { (900d, 680d), (640d, 420d) })
        {
            window.Width = width; window.Height = height; window.UpdateLayout();
            var close = Descendants(window).OfType<Button>().Single(b => Equals(b.Content, "返回作业"));
            var bounds = close.TransformToAncestor((FrameworkElement)window.Content).TransformBounds(new Rect(close.RenderSize));
            Check(close.ActualHeight >= 44 && bounds.Bottom <= ((FrameworkElement)window.Content).ActualHeight,
                "close action remains reachable in logical workspace " + width + "x" + height);
            Capture("help-diagnostics-" + width, window);
        }
        window.Close();
        Check((await runtime.SnapshotAsync()).RuntimeId == before.RuntimeId, "closing help leaves the business instance running");
        primary.ConfirmDiscardDrafts = _ => true; primary.Close(); await Task.Delay(100);
        lines.Add("IE01-HELP native WPF/actual ZIP/synthetic runtime only; system/real devices/site NOT_RUN");

        void Capture(string name, Window value)
        {
            value.UpdateLayout(); var dpi = VisualTreeHelper.GetDpi(value);
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(value.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(value.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            bitmap.Render(value); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = File.Create(Path.Combine(evidence, name + ".png")); encoder.Save(output);
            lines.Add("CAPTURE " + name + "; actual DPI=" + dpi.DpiScaleX);
        }
    }
}

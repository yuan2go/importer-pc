using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Platform;

static partial class Program
{
    static async Task VerifyCommercialPages(string root, string evidence, List<string> lines)
    {
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); lines.Add("PASS " + message); }
        lines.Add("SYNTHETIC runtime identity/ports; native WPF page, resource, keyboard and window checks only. UAC/installation/real equipment NOT_RUN.");
        var configRoot = Path.Combine(evidence, "config"); Directory.CreateDirectory(configRoot);
        var config = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(root, "config/sportmaster/business.json")));
        File.WriteAllText(Path.Combine(configRoot, "business.json"), Wire.Encode(config));
        var launch = new BusinessLaunch(configRoot, Path.Combine(evidence, "data"), null, null);
        if (Environment.GetEnvironmentVariable("IPC_FROZEN_DESKTOP") is { } desktopPath)
        {
            var draftFile = Path.Combine(launch.DataRoot, config.Mode.ToString(), config.StationId, config.ApplicationId, "startup-maintenance/editor-draft.bin");
            Directory.CreateDirectory(Path.GetDirectoryName(draftFile)!); File.WriteAllBytes(draftFile, [1, 2, 3, 4]); var draftHash = Wire.FileHash(draftFile);
            var desktop = System.Reflection.Assembly.LoadFrom(desktopPath);
            var type = desktop.GetType("Ipc.Desktop.StartupRepairWindow", throwOnError: true)!;
            var first = (Window)Activator.CreateInstance(type, [launch, null, true])!;
            first.Show();
            for (var i = 0; i < 100 && !Descendants(first).OfType<TextBlock>().Any(t => t.Text.Contains("草稿不可用")); i++) await Task.Delay(20);
            first.UpdateLayout();
            Check(first.Title.Contains("首次工位配置") && Descendants(first).OfType<Button>().Any(b => Equals(b.Content, "保存首配待生效…")) &&
                Descendants(first).OfType<Button>().Any(b => Equals(b.Content, "按当前首配启动")), "actual published desktop first-configuration window exposes explicit save/start through the existing editor");
            Check(!Directory.GetFiles(launch.DataRoot, "*.db", SearchOption.AllDirectories).Any() && !Directory.GetFiles(launch.DataRoot, "configuration.json", SearchOption.AllDirectories).Any(),
                "first-configuration native read creates neither database nor authoritative configuration");
            Check(Descendants(first).OfType<TextBlock>().Any(t => t.Text.Contains("草稿不可用")) && Descendants(first).OfType<TextBox>().Any(t => t.Text == config.TimeoutMs.ToString()) && Wire.FileHash(draftFile) == draftHash,
                "actual Windows DPAPI refusal leaves the original ciphertext unchanged while the published offline editor loads configuration fields and an explicit unavailable-draft diagnosis");
            Screenshot(first, Path.Combine(evidence, "commercial-first-configuration.png")); first.Close();
        }
        await using var runtime = new ParcelRuntime(launch, new SportmasterFlow(), new CommercialProductization.SyntheticFactory(launch)) { RuntimeIdentityPreflight = () => { } };
        await runtime.StartAsync(default); var identity = (await runtime.SnapshotAsync()).RuntimeId;
        var classic = (ShellWindow)Presentation.Create(runtime, null, "standard"); var alternate = (SupplyStationWindow)Presentation.Create(runtime, null, "standard", "supply-station");
        classic.Show(); alternate.Show(); await Task.Delay(400);
        Check(classic.Icon is not null && alternate.Icon is not null, "both native layouts load the repository-owned shared product icon");
        Check(ReferenceEquals(((ParcelViewModel)classic.DataContext).Application, alternate.ViewModel.Application), "native shared pages keep one Application instance");
        await SetClient(classic, 1100, 600); classic.Activate(); Click(classic, "作业记录"); await Task.Delay(250); classic.UpdateLayout();
        var page = (ContentControl)classic.FindName("PageHost");
        var filters = Descendants(page).OfType<Expander>().Single(e => e.Name == "HistoryFilters"); filters.IsExpanded = true; classic.UpdateLayout();
        var barcode = Descendants(page).OfType<TextBox>().Single(b => AutomationProperties.GetName(b) == "历史完整条码");
        Check(barcode.Focus() && barcode.IsKeyboardFocused, "native history exact-barcode filter takes keyboard focus and has an automation name");
        barcode.Text = "000000000000000000001"; barcode.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
        Check(((ParcelViewModel)classic.DataContext).HistoryBarcode == barcode.Text, "native history input preserves long leading-zero text in the view model");
        filters.IsExpanded = false; classic.UpdateLayout();
        Check(((HistoryPage)page.Content).ActualHeight > 200, "compact history collapses filters while keeping the fixed-query/export actions and detail area available");
        Check(Descendants(page).OfType<Button>().Any(b => (b.Content as string)?.Contains("导出") == true), "native history exposes bounded CSV export with cancel entry");
        Screenshot(classic, Path.Combine(evidence, "commercial-history-1100x600.png"));
        Click(classic, "设备与维护"); await Task.Delay(250);
        Check(Descendants(page).OfType<Button>().Any(b => Equals(b.Content, "加密保存未完成设置")) && Descendants(page).OfType<Button>().Any(b => Equals(b.Content, "继续未完成设置")), "native settings exposes persistent draft actions without replacing configuration authority");
        var support = new ProductSupportWindow(runtime, null) { Owner = classic }; support.Show(); await Task.Delay(200); support.UpdateLayout();
        Check(support.Icon is not null && support.Content is TabControl tabs && tabs.Items.Count == 2, "offline help and preview/about load as native owned tabs");
        Screenshot(support, Path.Combine(evidence, "commercial-support.png")); support.Close();
        Check((await runtime.SnapshotAsync()).RuntimeId == identity, "history/settings/help window actions never reconstruct the business runtime");
        lines.Add("NOT_RUN target client 1440x900 and target touch/DPI/reader hardware: current host work area " + SystemParameters.WorkArea + "; original full matrix assertion remains unchanged.");
        classic.ConfirmDiscardDrafts = _ => true; classic.Close(); alternate.ConfirmDiscardDrafts = _ => true; alternate.Close(); await Task.Delay(100);
        static void Screenshot(Window window, string path)
        {
            var dpi = VisualTreeHelper.GetDpi(window); var bitmap = new RenderTargetBitmap((int)(window.ActualWidth * dpi.DpiScaleX), (int)(window.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            bitmap.Render(window); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using var output = File.Create(path); png.Save(output);
        }
    }
}

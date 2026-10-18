using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Assembly;
using Ipc.Platform;

// Run on Windows: exercises actual WPF resource loading, navigation and window ownership.
static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var root = Path.GetFullPath(args.FirstOrDefault() ?? ".");
        var evidence = Path.Combine(Path.GetTempPath(), "ipc-sportmaster-wpf-" + Wire.Id());
        Directory.CreateDirectory(evidence);
        var lines = new List<string>();
        var desktop = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        desktop.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Ipc.Desktop.UiVerification;component/Themes/Workstation.xaml", UriKind.Relative) });
        desktop.Startup += async (_, _) =>
        {
            try { await Verify(root, evidence, lines); desktop.Shutdown(0); }
            catch (Exception ex) { lines.Add("FAIL " + ex); desktop.Shutdown(1); }
            finally { File.WriteAllLines(Path.Combine(evidence, "WINDOWS-WPF.log"), lines); File.WriteAllText(Path.Combine(root, "WINDOWS-WPF-EVIDENCE.txt"), evidence); }
        };
        return desktop.Run();
    }
    static async Task Verify(string root, string evidence, List<string> lines)
    {
        void Check(bool yes, string reason) { if (!yes) throw new InvalidOperationException(reason); lines.Add("PASS " + reason); }
        var launch = new BusinessLaunch(Path.Combine(root, "config/sportmaster"), Path.Combine(evidence, "data"), null, null);
        var runtime = SportmasterApplication.Create(launch, new ParcelCapabilityFactory(launch));
        await using (runtime)
        {
            await runtime.StartAsync(CancellationToken.None);
            var application = (IParcelApplication)runtime.Application;
            var stops = 0;
            var classic = Presentation.Create(application, null, null);
            var alternate = (SupplyStationWindow)Presentation.Create(application, null, "standard", "supply-station", async () => { await runtime.StopAsync(CancellationToken.None); stops++; });
            classic.Show(); alternate.Show(); await Task.Delay(1200);
            Check(classic is ShellWindow && alternate.ViewModel.Fresh, "classic and alternate native windows load actual resources and snapshots");
            Check(ReferenceEquals(((ParcelViewModel)classic.DataContext).Application, alternate.ViewModel.Application), "two UI choices share the same actual Application");
            Check(Descendants(alternate).OfType<SupplyStationPosition>().Count() == 2, "both native position controls load their own theme dictionaries");
            Click(alternate, "作业记录");
            Check(((ContentControl)alternate.FindName("PageHost")).Content is HistoryPage, "history navigation uses installed native page");
            Click(alternate, "设备与维护"); await Task.Delay(200);
            Check(((ContentControl)alternate.FindName("PageHost")).Content is MaintenancePage, "maintenance navigation uses installed native page");
            Click(alternate, "工作台"); Click(alternate, "辅助窗口"); await Task.Delay(700);
            var child = alternate.OwnedWindows.OfType<SupplyStationWindow>().Single();
            Check(ReferenceEquals(child.ViewModel.Application, application) && !ReferenceEquals(child.ViewModel, alternate.ViewModel), "auxiliary window inherits UI and shares Application with independent model");
            child.Close(); await Task.Delay(100);
            Check(stops == 0 && !child.IsVisible, "closing auxiliary window keeps business owner alive");
            foreach (var size in new[] { (1440, 980), (1100, 760) })
            {
                alternate.Width = size.Item1; alternate.Height = size.Item2; alternate.UpdateLayout();
                var bitmap = new RenderTargetBitmap((int)alternate.ActualWidth, (int)alternate.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(alternate); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(Path.Combine(evidence, $"supply-station-{size.Item1}x{size.Item2}.png")); png.Save(stream);
            }
            Click(alternate, "辅助窗口"); await Task.Delay(200);
            var remaining = alternate.OwnedWindows.OfType<SupplyStationWindow>().Single();
            alternate.Close();
            for (var i = 0; i < 100 && alternate.IsVisible; i++) await Task.Delay(50);
            Check(stops == 1 && !alternate.IsVisible && !remaining.IsVisible, "main close drains views, stops business once and closes owned windows");
            classic.Close();
        }
        lines.Add("PASS WINDOWS WPF SMOKE; real SDK/WCS/device/field acceptance remains BLOCKED");
    }
    static void Click(DependencyObject root, string name) => Descendants(root).OfType<Button>().Single(x => Equals(x.Content, name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
}

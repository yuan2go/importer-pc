using System.IO;
using System.Reflection;
using System.Windows.Threading;
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
static partial class Program
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
            try { await Verify(root, evidence, lines).WaitAsync(TimeSpan.FromSeconds(90)); desktop.Shutdown(0); }
            catch (Exception ex) { lines.Add("FAIL " + ex); desktop.Shutdown(1); }
            finally { File.WriteAllLines(Path.Combine(evidence, "WINDOWS-WPF.log"), lines); Directory.CreateDirectory(Path.Combine(root, ".artifacts")); File.WriteAllText(Path.Combine(root, ".artifacts/WINDOWS-WPF-EVIDENCE.txt"), evidence); }
        };
        return desktop.Run();
    }
    static async Task Verify(string root, string evidence, List<string> lines)
    {
        void Check(bool yes, string reason) { if (!yes) throw new InvalidOperationException(reason); lines.Add("PASS " + reason); }
        var configRoot = Path.Combine(evidence, "config"); Directory.CreateDirectory(configRoot);
        var config = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(root, "config/sportmaster/business.json")));
        config = config with { Login = config.MaintenanceLogin! with
        { InstanceId = "native.operator", Target = "reference://native-operator", Parameters = new() { ["subjects"] = "alice" } } };
        ExtensionStore.AtomicWrite(Path.Combine(configRoot, "business.json"), config);
        var launch = new BusinessLaunch(configRoot, Path.Combine(evidence, "data"), null, null);
        var runtime = SportmasterApplication.Create(launch, new ParcelCapabilityFactory(launch));
        await using (runtime)
        {
            await runtime.StartAsync(CancellationToken.None);
            var application = DispatchProxy.Create<IParcelApplication, ProjectionProbe>();
            var probe = (ProjectionProbe)(object)application; probe.Inner = (IParcelApplication)runtime.Application;
            var stops = 0; var stopCalls = 0; var failNextStop = false;
            var classic = Presentation.Create(application, null, null);
            var alternate = (SupplyStationWindow)Presentation.Create(application, null, "standard", "supply-station", async () => { stopCalls++; if (failNextStop) { failNextStop = false; throw new IOException("SYNTHETIC owner stop failed"); } await runtime.StopAsync(CancellationToken.None); stops++; });
            classic.Show(); alternate.Show(); await Task.Delay(1200);
            Check(classic is ShellWindow && alternate.ViewModel.Fresh, "classic and alternate native windows load actual resources and snapshots");
            Check(ReferenceEquals(((ParcelViewModel)classic.DataContext).Application, alternate.ViewModel.Application), "two UI choices share the same actual Application");
            Check(Descendants(alternate).OfType<SupplyStationPosition>().Count() == 2, "both native position controls load their own theme dictionaries");
            await ((ParcelViewModel)classic.DataContext).RunAsync(() => ((ParcelViewModel)classic.DataContext).LoginAsync("SIMULATION_ONLY"), "原生参考登录");
            Check(((ParcelViewModel)classic.DataContext).Snapshot!.CurrentOperator?.Subject == "alice", "native fixture enables actual optional operator authentication");
            await alternate.ViewModel.RefreshAsync();
            await alternate.ViewModel.EmitPositionAsync(alternate.ViewModel.Left);
            await alternate.ViewModel.EmitPositionAsync(alternate.ViewModel.Right);
            for (var i = 0; i < 100; i++)
            {
                await alternate.ViewModel.RefreshDisplayAsync();
                if (alternate.ViewModel.Snapshot!.Operations.Length == 2 && alternate.ViewModel.Snapshot.Operations.All(x => x.State == ParcelState.SlotResolved)) break;
                await Task.Delay(50);
            }
            Check(alternate.ViewModel.Snapshot!.Operations.Length == 2 && alternate.ViewModel.Snapshot.Operations.All(x => x.State == ParcelState.SlotResolved), "native window reads actual two-position operations");
            await alternate.ShowDetailsAsync(alternate.ViewModel.Left.Operation!);
            Check(alternate.ViewModel.SelectedHistory?.Operation.Id == alternate.ViewModel.Left.Operation!.Id, "left details and selected history identity agree");
            await alternate.ShowDetailsAsync(alternate.ViewModel.Right.Operation!);
            Check(alternate.ViewModel.SelectedHistory?.Operation.Id == alternate.ViewModel.Right.Operation!.Id, "right details replace both selected row and command target");
            Click(alternate, "作业记录");
            Check(((ContentControl)alternate.FindName("PageHost")).Content is HistoryPage, "history navigation uses installed native page");
            Click(alternate, "设备与维护"); await Task.Delay(200);
            Check(((ContentControl)alternate.FindName("PageHost")).Content is MaintenancePage, "maintenance navigation uses installed native page");
            await VerifyMaintenanceControls(classic, (ParcelViewModel)classic.DataContext, Check);
            await VerifyMaintenanceControls(alternate, alternate.ViewModel, Check);
            Click(alternate, "工作台"); Click(alternate, "辅助窗口"); await Task.Delay(700);
            var child = alternate.OwnedWindows.OfType<SupplyStationWindow>().Single();
            Check(ReferenceEquals(child.ViewModel.Application, application) && !ReferenceEquals(child.ViewModel, alternate.ViewModel), "auxiliary window inherits UI and shares Application with independent model");
            child.Close(); await Task.Delay(100);
            Check(stops == 0 && !child.IsVisible, "closing auxiliary window keeps business owner alive");
            probe.Project = snapshot => snapshot with
            {
                AuxiliaryReasons = Enumerable.Range(0, 50).Select(i => $"SYNTHETIC retained receipt {i}: original custody and upload failure detail").ToArray(),
                BlockingReasons = Enumerable.Range(0, 50).Select(i => $"SYNTHETIC long blocking reason {i}").ToArray()
            };
            alternate.ViewModel.Message = string.Join("\n", Enumerable.Repeat("SYNTHETIC long diagnostic", 50));
            await alternate.ViewModel.RefreshDisplayAsync(); alternate.UpdateLayout();
            var header = (ScrollViewer)alternate.FindName("HeaderDiagnostics");
            var diagnostics = (ScrollViewer)alternate.FindName("StatusDiagnostics");
            var page = (ContentControl)alternate.FindName("PageHost");
            Check(header.ActualHeight <= 132 && diagnostics.ActualHeight <= 96 && page.ActualHeight > 240,
                "50 receipt reasons and long messages scroll locally without consuming work area");
            Check(alternate.ViewModel.AuxiliaryState.Contains("receipt 49"), "bounded diagnostic display retains all original entries");
            var client = (Grid)alternate.FindName("ClientArea");
            foreach (var position in Descendants(alternate).OfType<SupplyStationPosition>())
            {
                var actions = (Border)position.FindName("FixedActions");
                var bounds = actions.TransformToAncestor(client).TransformBounds(new Rect(actions.RenderSize));
                Check(actions.ActualHeight >= 44 && bounds.Top >= 0 && bounds.Bottom <= client.ActualHeight,
                    "position recovery actions stay within the effective client area under diagnostic backlog");
            }
            await VerifyClientGeometry(classic, (ParcelViewModel)classic.DataContext, probe, evidence, "classic", Check);
            await VerifyClientGeometry(alternate, alternate.ViewModel, probe, evidence, "supply-station", Check);
            probe.Project = null; await alternate.ViewModel.RefreshDisplayAsync();
            Click(alternate, "辅助窗口"); await Task.Delay(200);
            var remaining = alternate.OwnedWindows.OfType<SupplyStationWindow>().Single();
            var fields = BindingFlags.Instance | BindingFlags.NonPublic;
            var pendingChildRead = new TaskCompletionSource();
            typeof(SupplyStationWindow).GetField("pendingRefresh", fields)!.SetValue(remaining, pendingChildRead.Task);
            var childTimer = (DispatcherTimer)typeof(SupplyStationWindow).GetField("timer", fields)!.GetValue(remaining)!;
            failNextStop = true; remaining.Close(); alternate.Close();
            Check(stopCalls == 0, "overlapping child and owner close drain the original pending read first");
            pendingChildRead.SetResult();
            for (var i = 0; i < 100 && !alternate.IsEnabled; i++) await Task.Delay(20);
            Check(alternate.IsVisible && alternate.IsEnabled && remaining.IsVisible && childTimer.IsEnabled && stops == 0,
                "owner stop failure retains both overlapping windows and resumes their native timers");
            remaining.Close(); await Task.Delay(100);
            Check(!remaining.IsVisible && !childTimer.IsEnabled, "independent native auxiliary close after root failure is terminal");
            alternate.Close();
            for (var i = 0; i < 100 && alternate.IsVisible; i++) await Task.Delay(50);
            Check(stops == 1 && stopCalls == 2 && !alternate.IsVisible && !remaining.IsVisible && !childTimer.IsEnabled,
                "owner retry stops business once and leaves closed auxiliary timer stopped");
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

// Synthetic layout projection only; every command still reaches the same real Application.
public class ProjectionProbe : DispatchProxy
{
    public IParcelApplication Inner = null!;
    public Func<ParcelSnapshot, ParcelSnapshot>? Project;
    protected override object? Invoke(MethodInfo? method, object?[]? arguments)
    {
        if (method!.Name == nameof(IParcelApplication.SnapshotAsync)) return Snapshot();
        return method.Invoke(Inner, arguments);
    }
    async Task<ParcelSnapshot> Snapshot()
    {
        var snapshot = await Inner.SnapshotAsync(); return Project?.Invoke(snapshot) ?? snapshot;
    }
}

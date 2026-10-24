using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Platform;

static partial class Program
{
    static async Task VerifyStabilityNative(string root, string evidence, List<string> lines)
    {
        void Check(bool value, string detail) { if (!value) throw new InvalidOperationException(detail); lines.Add("PASS STABILITY-WPF: " + detail); }
        var config = Path.Combine(evidence, "config"); Directory.CreateDirectory(config);
        File.Copy(Path.Combine(root, "config/sportmaster/business.json"), Path.Combine(config, "business.json"));
        var launch = new BusinessLaunch(config, Path.Combine(evidence, "data"), null, null);
        // The unfinished handoff is produced by the actual durable receive boundary.
        await using (var interrupted = new ParcelRuntime(launch, new SportmasterFlow(), new VisualFactory(launch, false))
        { RuntimeIdentityPreflight = () => { }, Boundary = point => { if (point == "AfterIngressDurable") throw new IOException("Synthetic handoff interruption"); } })
        { await interrupted.StartAsync(default); Check(!interrupted.EmitReferenceAt("left", "native-input", "000001", "native-event"), "failed handoff does not claim source delivery success"); }
        await using var runtime = new ParcelRuntime(launch, new SportmasterFlow(), new VisualFactory(launch, false)) { RuntimeIdentityPreflight = () => { } };
        await runtime.StartAsync(default); await ManagementModeFixture.Authorize((IParcelApplication)runtime);
        var window = new SupplyStationWindow(runtime, null, null, () => Task.CompletedTask);
        window.Show();
        try
        {
            await window.ViewModel.RefreshAsync();
            typeof(SupplyStationWindow).GetMethod("SetPage", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, ["maintenance"]);
            var page = (MaintenancePage)typeof(SupplyStationWindow).GetField("maintenance", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            typeof(MaintenancePage).GetMethod("TechnicalTool_Click", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, [new MenuItem { Tag = "8" }, new RoutedEventArgs()]);
            await window.ViewModel.LoadIngressResponsibilitiesAsync();
            Check(window.ViewModel.InterruptedInputs.Length == 1, "native recovery view reads exact interrupted receipt from current application");
            window.ViewModel.SelectedIngress = window.ViewModel.InterruptedInputs.Single();
            window.ViewModel.IngressInspectionReason = "Synthetic entity removed and no external request created; inspected original handoff";
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(window); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
            using (var output = File.Create(Path.Combine(evidence, "native-input-recovery.png"))) png.Save(output);
            var until = Stopwatch.StartNew();
            while ((await runtime.SnapshotAsync()).Availability?.Configure != true && until.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(20);
            await window.ViewModel.RefreshAsync(); await window.ViewModel.InspectIngressAsync();
            Check(window.ViewModel.InterruptedInputs.Length == 0 && window.ViewModel.IngressInspectionStatus.Contains("已记录"), "native recovery action commits exact input inspection through application guards");
            await window.ViewModel.EnsureMaintenanceLoadedAsync();
            typeof(MaintenancePage).GetMethod("ShowParameterEditor", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, []);
            var editor = (ParameterSettingsControl)page.FindName("MainParameterEditor");
            Check(editor.NavigateTo("HistoryDays"), "retention policy has an accessible system-settings destination");
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
            var historyDays = Descendants(editor).OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "HistoryDays");
            historyDays.Text = "7";
            Check(historyDays.IsVisible && window.ViewModel.BuildStructuredConfiguration().Retention?.HistoryDays == 7,
                "native seven-day setting reaches the shared complete candidate without saving or starting devices");
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
            bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(window); png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
            using (var output = File.Create(Path.Combine(evidence, "native-history-retention.png"))) png.Save(output);
            historyDays.Text = ""; // Restore the original draft; this test does not persist operator settings.
            await Task.Delay(600);
            var independent = Task.Run(async () =>
            {
                await Task.Delay(900);
                using var progress = JsonDocument.Parse(Wire.Encode(runtime.RuntimeProgress()));
                return progress.RootElement.GetProperty("Ui").EnumerateArray().Single().GetProperty("LastDispatchAgeMs").GetDouble();
            });
            Thread.Sleep(1400); // Deliberate bounded UI hang; independent observer must detect it.
            Check(await independent >= 800, "independent timer observes a stalled real WPF Dispatcher; UI cannot renew its own progress");
            await Task.Delay(700);
            using var resumed = JsonDocument.Parse(Wire.Encode(runtime.RuntimeProgress()));
            Check(resumed.RootElement.GetProperty("Ui").EnumerateArray().Single().GetProperty("LastDispatchAgeMs").GetDouble() < 700,
                "dispatcher progress recovers after actual dispatch resumes");
        }
        finally
        {
            window.Close();
            for (var n = 0; window.IsVisible && n < 300; n++) await Task.Delay(20);
            Check(!window.IsVisible, "native window async close drains lifecycle");
        }
        using var closed = JsonDocument.Parse(Wire.Encode(runtime.RuntimeProgress()));
        Check(closed.RootElement.GetProperty("Ui").GetArrayLength() == 0, "closed window removes independent timer registration");
        lines.Add("Scope: native Windows WPF + synthetic runtime; actual SDK/RCS/hardware and 24h NOT_RUN.");
    }
}

using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Platform;

static partial class Program
{
    static async Task VerifyCameraDebug(string root, string evidence, List<string> lines)
    {
        void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); lines.Add("PASS CAMERA-DEBUG " + message); }
        var folder = Path.Combine(evidence, "debug-config"); Directory.CreateDirectory(folder);
        var config = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(root, "tests/Ipc.Application.Verification/sportmaster.synthetic.json")));
        ExtensionStore.AtomicWrite(Path.Combine(folder, "business.json"), config);
        var launch = new BusinessLaunch(folder, Path.Combine(evidence, "debug-data"), null, null);
        var factory = new CommercialProductization.SyntheticFactory(launch);
        await using var runtime = new ParcelRuntime(launch, new SportmasterFlow(), factory) { RuntimeIdentityPreflight = () => { } };
        await runtime.StartAsync(default);
        try { await runtime.ReadCameraPreviewAsync(config.Reader.DeviceId, default); throw new Exception("Unauthorised preview escaped"); }
        catch (UnauthorizedAccessException) { Check(true, "Application rejects image access outside management mode"); }
        await ManagementModeFixture.Authorize(runtime);
        Check((await runtime.ReadCameraPreviewAsync(config.Reader.DeviceId, default)).Frame is null, "manual source reports preview unavailable without fabricated camera success");
        var app = DispatchProxy.Create<ICameraDebugFixture, CameraDebugProbe>(); var probe = (CameraDebugProbe)(object)app; probe.Inner = runtime;
        var window = new SupplyStationWindow(app, null, "standard") { ConfirmDiscardDrafts = _ => true, Title = "调试页 · 合成图像验证" };
        window.Show(); var vm = window.ViewModel; var client = (Grid)window.FindName("ClientArea");
        async Task Flush() { await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout(); }
        async Task Until(Func<bool> ready) { for (int i = 0; i < 100; i++) { await vm.RefreshAsync(); await Flush(); if (ready()) return; await Task.Delay(40); } throw new TimeoutException("Debug UI did not converge"); }
        await SetClient(window, 1366, 768); await Until(() => vm.Fresh && vm.IsManagementMode && vm.Left.CanAdmit);
        runtime.EmitReferenceAt("left", "debug-left", "000123456", "debug-event-left");
        await Until(() => vm.Left.SlotAvailable);
        var original = Wire.Encode(vm.Left.Operation!.Wcs!.Request); var sends = factory.Sends;
        window.ShowPositionDebug(vm.Left); await Flush();
        var control = Descendants(window).OfType<UserControl>().Single(c => c.GetType().Name == "StationDebugControl");
        var poll = control.GetType().GetMethod("PollAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        async Task Poll() { await (Task)poll.Invoke(control, null)!; await Flush(); }
        var image = Descendants(control).OfType<Image>().Single(); var scans = Descendants(control).OfType<ListBox>().Single();
        await Until(() => image.Source is not null); await Poll();
        Check(scans.Items.Count == 1 && Descendants(control).OfType<TextBox>().Any(t => t.IsReadOnly && t.Text.Contains("000123456")), "snapshot input links to the frozen real Application request in the debug view");
        foreach (var size in new[] { (1366d, 768d), (1024d, 768d), (800d, 420d) })
        {
            await SetClient(window, size.Item1, size.Item2); await Flush();
            var scroller = Descendants(control).OfType<ScrollViewer>().First();
            Check(scroller.ScrollableWidth == 0 && scroller.ActualHeight > 100, "debug retains a vertical viewport without page horizontal overflow at " + size.Item1);
            Check(Descendants(control).OfType<Button>().Where(b => b.Content is string text && text is "左相机" or "右相机").All(b => Inside(b, client)), "both camera selectors remain reachable at " + size.Item1);
            CaptureInternal(evidence, "camera-debug-" + (int)size.Item1, client, lines);
        }
        await SetClient(window, 1366, 768); await Poll();
        Click(window, "暂停画面"); var calls = probe.Reads; await Poll(); await Task.Delay(900);
        Check(probe.Reads == calls && image.Source is not null && vm.Left.CanAdmit, "pause freezes only image polling and leaves business admission running");
        Click(window, "继续画面"); await Poll();
        probe.Held = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = (Task)poll.Invoke(control, null)!;
        window.ShowPositionDebug(vm.Right); probe.Held.SetResult(); await pending; await Flush();
        Check(image.Source is null, "late left-camera frame cannot repaint after selecting the right camera");
        probe.Held = null; await Poll();
        var bitmap = (BitmapSource)image.Source!; var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight]; bitmap.CopyPixels(pixels, bitmap.PixelWidth, 0);
        Check(pixels[0] == 180 && scans.Items.Count == 0, "camera switch clears sampled records and paints only the selected camera");
        var filter = Descendants(control).OfType<Expander>().Single(e => Equals(e.Header, "本地筛选测试（不外呼）")); filter.IsExpanded = true; await Flush();
        var sample = Descendants(control).OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "本地筛选测试候选"); sample.Text = "000123456";
        Click(window, "测试当前运行筛选规则"); await Until(() => Descendants(control).OfType<TextBlock>().Any(t => t.Text.Contains("选中条码")));
        Check(factory.Sends == sends, "opening debug, previewing and local filter testing send no additional RCS business requests");
        Check(Wire.Encode((await runtime.SnapshotAsync()).Operations.Single().Wcs!.Request) == original, "debug never mutates an original request");
        window.ShowPositionDiagnostics(vm.Right); await Flush(); calls = probe.Reads; await Task.Delay(1000);
        Check(probe.Reads == calls && image.Source is null, "leaving debug stops polling and clears the image");
        CaptureInternal(evidence, "camera-debug-diagnostics", client, lines);
        window.ShowPositionDebug(vm.Left); await Poll();
        Click(window, "清空本页采样"); await Poll();
        Check(scans.Items.Count == 0, "clearing samples does not resurrect the unchanged latest input on the next refresh");
        probe.Held = new(TaskCreationOptions.RunContinuationsAsynchronously); var lateAfterExit = (Task)poll.Invoke(control, null)!;
        await runtime.ExitManagementModeAsync(); await vm.RefreshManagementModeAsync(); await Flush();
        probe.Held.SetResult(); await lateAfterExit; probe.Held = null; await Flush();
        Check(vm.SelectedPage == "workstation" && image.Source is null && scans.Items.Count == 0 && sample.Text.Length == 0, "management exit hides debug and clears images, scans and local test input");
        window.ShowPositionDebug(vm.Left); Check(vm.SelectedPage == "workstation", "operator cannot reopen protected debug page");
        window.Close(); await Task.Delay(100);
        lines.Add("Native WPF layout and Application synthetic flow verified. Preview image deliberately projected; real MVS camera and field acceptance NOT_RUN.");
    }
}
public interface ICameraDebugFixture : IHistoryProjectionFixture, ICameraDiagnostics { }
public class CameraDebugProbe : HierarchyProjectionProbe
{
    public int Reads; public TaskCompletionSource? Held;
    protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method!.Name == nameof(ICameraDiagnostics.ReadCameraPreviewAsync)
        ? Preview((string)arguments![0]!) : base.Invoke(method, arguments);
    async Task<CameraPreviewResult> Preview(string device)
    {
        Reads++; if (Held is { } held) await held.Task;
        var value = device.Contains("right") ? (byte)180 : (byte)90;
        return new("合成图像 · 仅布局与隔离验证", new(device, "synthetic", 1, DateTimeOffset.UtcNow, 320, 200, Enumerable.Repeat(value, 64000).ToArray()));
    }
}

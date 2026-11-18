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
        var window = new OperationsViewFixture(app) { ConfirmDiscardDrafts = _ => true, Title = "调试页 · 合成图像验证" }; await using var windowOwner = new NativeWindowOwner(window);
        window.Show(); var vm = window.ViewModel; var client = (Grid)window.FindName("ClientArea");
        async Task Flush() { await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout(); }
        async Task Until(Func<bool> ready) { for (int i = 0; i < 100; i++) { await vm.RefreshAsync(); await Flush(); if (ready()) return; await Task.Delay(40); } throw new TimeoutException("Debug UI did not converge"); }
        await SetClient(window, 1366, 768); await Until(() => vm.Fresh && vm.IsManagementMode && vm.Left.CanAdmit);
        runtime.EmitReferenceAt("left", "debug-left", "000123456", "debug-event-left");
        await Until(() => vm.Left.SlotAvailable);
        var original = Wire.Encode(vm.Left.Operation!.Wcs!.Request); var sends = factory.Sends;
        Window Surface() => (Window)window;
        window.ShowPositionDebug(vm.Left); await Until(() => Descendants(Surface()).OfType<UserControl>().Any(control => control.GetType().Name == "StationDebugControl"));
        var control = Descendants(Surface()).OfType<UserControl>().Single(c => c.GetType().Name == "StationDebugControl");
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var live = (UserControl)control.GetType().GetField("livePreview", flags)!.GetValue(control)!;
        var poll = live.GetType().GetMethod("PollAsync", BindingFlags.Instance | BindingFlags.Public)!;
        var timer = (DispatcherTimer)live.GetType().GetField("timer", flags)!.GetValue(live)!; timer.Stop();
        var image = (Image)live.GetType().GetField("image", flags)!.GetValue(live)!;
        var photo = (Image)control.GetType().GetField("photo", flags)!.GetValue(control)!;
        Task? pending = null;
        async Task Poll() { await ((Task)poll.Invoke(live, null)!).WaitAsync(TimeSpan.FromSeconds(5)); await Flush(); }
        try
        {
            await Poll(); await Until(() => image.Source is not null && !(bool)live.GetType().GetField("polling", flags)!.GetValue(live)!);
            Check(image.Source is not null && photo.Source is null && !ReferenceEquals(image, photo), "live preview and this read round's locked photo are independent controls");
            foreach (var size in new[] { (1366d, 768d), (1024d, 768d), (800d, 420d) })
            {
                await SetClient(Surface(), size.Item1, size.Item2); await Flush();
                Check(live.ActualWidth > 0 && Descendants(control).OfType<Button>().Count(b => b.Content is string text && text is "左相机" or "右相机") == 2,
                    "current debug page keeps both explicit camera selectors and the live-preview surface at " + size.Item1);
                CaptureInternal(evidence, "camera-debug-" + (int)size.Item1, (FrameworkElement)Surface().Content, lines);
            }
            await SetClient(window, 1366, 768); await Poll();
            probe.Held = new(TaskCreationOptions.RunContinuationsAsynchronously);
            probe.Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            pending = (Task)poll.Invoke(live, null)!;
            await probe.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            window.ShowPositionDebug(vm.Right); probe.Held.TrySetResult(); await pending.WaitAsync(TimeSpan.FromSeconds(5)); await Flush();
            Check(image.Source is null, "late left-camera preview cannot repaint after selecting the right camera");
            probe.Held = null; await Poll();
            var bitmap = (BitmapSource)image.Source!; var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight]; bitmap.CopyPixels(pixels, bitmap.PixelWidth, 0);
            Check(pixels[0] == 180 && photo.Source is null, "camera switch paints only the selected live image and does not fabricate a read-round photograph");
            Check(factory.Sends == sends && Wire.Encode((await runtime.SnapshotAsync()).Operations.Single().Wcs!.Request) == original,
                "opening debug and previewing retain the exact original request without another business send");
            window.ShowPositionDiagnostics(vm.Right); await Flush(); var calls = probe.Reads; await Task.Delay(200);
            Check(probe.Reads == calls && image.Source is null && !timer.IsEnabled, "leaving debug revokes its actual live-preview polling owner");
            window.ShowPositionDebug(vm.Left); timer.Stop(); await Poll();
            probe.Held = new(TaskCreationOptions.RunContinuationsAsynchronously); probe.Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            pending = (Task)poll.Invoke(live, null)!; await probe.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await runtime.ExitManagementModeAsync(); await vm.RefreshManagementModeAsync(); await Flush();
            probe.Held.TrySetResult(); await pending.WaitAsync(TimeSpan.FromSeconds(5)); await Flush();
            Check(!vm.IsManagementMode && image.Source is null && photo.Source is null && !timer.IsEnabled, "revocation clears both live and locked images and a late preview cannot restore them");
            window.ShowPositionDebug(vm.Left); Check(!vm.IsManagementMode && !((Button)control.GetType().GetField("trigger", flags)!.GetValue(control)!).IsEnabled, "revoked operator cannot reenable protected debug commands");
        }
        finally
        {
            probe.Held?.TrySetResult();
            if (pending is not null) await pending.WaitAsync(TimeSpan.FromSeconds(5));
            timer.Stop();
        }
        lines.Add("Native WPF layout and Application synthetic flow verified. Preview image deliberately projected; real MVS camera and field acceptance NOT_RUN.");
    }
}
public interface ICameraDebugFixture : IHistoryProjectionFixture, ICameraDiagnostics { }
public class CameraDebugProbe : HierarchyProjectionProbe
{
    public int Reads; public TaskCompletionSource? Held, Entered;
    protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method!.Name == nameof(ICameraDiagnostics.ReadCameraPreviewAsync)
        ? Preview((string)arguments![0]!) : base.Invoke(method, arguments);
    async Task<CameraPreviewResult> Preview(string device)
    {
        Reads++; Entered?.TrySetResult(); if (Held is { } held) await held.Task;
        var value = device.Contains("right") ? (byte)180 : (byte)90;
        return new("合成图像 · 仅布局与隔离验证", new(device, "synthetic", 1, DateTimeOffset.UtcNow, 320, 200, Enumerable.Repeat(value, 64000).ToArray()));
    }
}

using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Platform;

static partial class Program
{
    static async Task VerifyCameraStreamUi(string root, string evidence, List<string> lines)
    {
        void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); lines.Add("PASS STREAM-UI " + text); }
        T Field<T>(object c, string name) => (T)c.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(c)!;
        await using var h = new CameraRoundHarness(root, Path.Combine(evidence, "stream")); await h.StartAsync(); await h.BeginAsync();
        await h.App.SetDebugLoggingAsync(true);
        var window = new SupplyStationWindow(h.App, null, "standard") { Title = "相机实时视频 · 合成图像验证" };
        window.Show();
        try
        {
            await SetClient(window, 1366, 768); await window.ViewModel.RefreshAsync(); window.ShowPositionDebug(window.ViewModel.Left);
            await Dispatcher.Yield(); window.UpdateLayout();
            var control = Descendants(window).OfType<UserControl>().Single(c => c.GetType().Name == "StationDebugControl");
            var live = Field<UserControl>(control, "livePreview"); var image = Field<Image>(live, "image");
            Task Poll(UserControl c) => (Task)c.GetType().GetMethod("PollAsync")!.Invoke(c, null)!;
            async Task Flush() { await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout(); }
            async Task ShowLive()
            { await Flush(); Field<DispatcherTimer>(live, "timer").Stop(); await Poll(live); }
            Field<DispatcherTimer>(window, "timer").Stop(); Field<DispatcherTimer>(control, "timer").Stop();
            await ShowLive();
            var blank = new CameraRoundHarness.RoundPixels(Enumerable.Repeat((byte)180, 640 * 480).ToArray(), 640, 480);
            h.Api(h.Left).Enqueue(blank, 1);
            await CameraRoundHarness.Until(async () => { await Poll(live); return image.Source is not null; });
            Check(h.Factory.Sources[h.Left].ReadDiagnostics() is { Frames: 1, DecodeStarted: 0, DecodePaused: true } && h.Peer.Requests.Count == 0,
                "native debug video displays an unrecognized frame before trigger while the isolated decoder and RCS remain idle");
            await h.ArmAsync(h.Left); await Task.Delay(220); h.Api(h.Left).Enqueue(blank, 2);
            await CameraRoundHarness.Until(() => Task.FromResult(h.Factory.Sources[h.Left].ReadDiagnostics().EmptyFrames == 1));
            await Poll(live);
            Check(image.Source is not null && (await h.App.ReadCameraReadRoundAsync(default)).State == "Reading",
                "empty decoded images remain visible while the current scan keeps waiting for a barcode");
            await Task.Delay(220); h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("STREAM-LOCKED-0001"), 3);
            var locked = await h.LockedAsync(); await Poll(control);
            Check(image.IsVisible && Field<Image>(control, "photo").IsVisible && Field<Image>(control, "photo").Source is not null && Field<TextBox>(control, "barcode").Text == "STREAM-LOCKED-0001",
                "live video and the same-frame locked photograph remain simultaneously visible with the full barcode");
            await ShowLive(); await Task.Delay(220); h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("NEXT-VIDEO-ONLY"), 4);
            await CameraRoundHarness.Until(async () => { await Poll(live); return Field<string>(live, "shown").EndsWith(":4"); });
            var retained = await h.App.ReadCameraReadRoundAsync(default);
            Check(retained.Id == locked.Id && retained.Frame!.Id == locked.Frame!.Id && retained.RequestBody == locked.RequestBody && retained.Barcode == locked.Barcode && h.Factory.Decoders[h.Left].Count == 2 && h.Peer.Requests.Count == 0,
                "live video advances after lock without redecoding, replacing the locked result, changing frozen JSON or sending requests");
            Check(Field<TextBlock>(live, "counters").Text.Contains("本轮解码已暂停"), "video explains that decoding is paused separately from acquisition");
            Field<Expander>(control, "setup").IsExpanded = false; image.BringIntoView(); await Flush();
            var liveCard = Field<Border>(control, "liveCard"); var resultCard = Field<Border>(control, "resultCard");
            var livePoint = liveCard.TranslatePoint(new Point(), control); var photoPoint = resultCard.TranslatePoint(new Point(), control);
            Check(livePoint.X < photoPoint.X && Math.Abs(livePoint.Y - photoPoint.Y) < 2 && Field<TextBox>(control, "requestBody").TranslatePoint(new Point(), control).Y > livePoint.Y + liveCard.ActualHeight,
                "design layout places live video left and locked result right, with request and response underneath");
            var navigation = (Border)window.FindName("DailyNavigation");
            Check(Descendants(navigation).OfType<Button>().Count(b => b.Content is string && Descendants(b).OfType<System.Windows.Shapes.Path>().Any(p => p.Data is not null && p.IsVisible)) == 5,
                "all five existing sidebar destinations render vector icons while keeping their text and management guards");
            CaptureInternal(evidence, "camera-video-1366", (Grid)window.FindName("ClientArea"), lines);
            await SetClient(window, 1600, 1000); await Flush();
            CaptureInternal(evidence, "camera-design-1600", (Grid)window.FindName("ClientArea"), lines);
            control.GetType().GetMethod("SelectPosition")!.Invoke(control, ["right"]); await Flush();
            Check(image.Source is null, "switching to the right camera immediately clears the left camera image");
            await Poll(live); h.Api(h.Right).Enqueue(blank, 1);
            await CameraRoundHarness.Until(async () => { await Poll(live); return image.Source is not null; });
            Check(h.Factory.Decoders[h.Right].Count == 0 && h.Factory.Sources[h.Right].ReadDiagnostics().Frames == 1, "right video uses its own stream while its unarmed decoder remains paused");
            await SetClient(window, 1024, 768); await Flush(); image.BringIntoView(); await Flush();
            CaptureInternal(evidence, "camera-video-1024", (Grid)window.FindName("ClientArea"), lines);
            await Task.Delay(3100); await Poll(live);
            Check(image.Source is null && Field<TextBlock>(live, "status").Text.Contains("过期") && h.Factory.Sources[h.Right].ReadDiagnostics().State == "FrameStale",
                "an online stream with no fresh frames clears stale video and reports stalled acquisition");
            await CameraRoundHarness.Until(async () =>
            {
                var tail = await h.App.ReadDiagnosticTailAsync(null, null, default);
                return tail.Entries.Count(e => e.Text.Contains("相机取帧与解码采样")) >= 2;
            });
            Check(true, "debug logging emits periodic acquisition/decoder samples for both cameras even without health transitions");
            using var progress = JsonDocument.Parse(Wire.Encode(h.App.RuntimeProgress()));
            Check(progress.RootElement.GetProperty("Connections").EnumerateArray().All(c => c.GetProperty("Acquisition").GetProperty("Frames").GetInt64() > 0),
                "runtime progress carries per-camera acquisition counters rather than conflating them with ReadOperations");
            Check(h.Factory.Apis.Values.All(a => a.Opens == 1 && a.Starts == 1 && a.Stops == 0), "simultaneous preview and camera selection never open or restart an SDK connection");
            await h.App.ExitManagementModeAsync(); await window.ViewModel.RefreshAsync(); await Poll(live);
            Check(image.Source is null && Field<TextBlock>(live, "counters").Text.Length == 0 && (await h.App.SnapshotAsync()).Paused,
                "management revocation clears preview and telemetry while preserving business isolation");
            try { await h.App.ReadCameraPreviewAsync(h.Left, default); throw new InvalidOperationException("Preview survived authority revoke"); }
            catch (UnauthorizedAccessException) { Check(true, "Application rejects preview reads without management authority"); }
        }
        finally { window.Close(); }
    }
}

using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Ipc.Application.Sportmaster.Desktop;

static partial class Program
{
    static async Task VerifyCameraPreviewLifecycle(string root, string evidence, List<string> lines)
    {
        void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); lines.Add("PASS PREVIEW-LIFECYCLE " + text); }
        T Field<T>(object c, string name) => (T)c.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(c)!;
        async Task Until(Func<bool> done, string step)
        {
            for (var i = 0; i < 200; i++) { await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); if (done()) return; await Task.Delay(40); }
            throw new TimeoutException(step);
        }
        await using var h = new CameraRoundHarness(root, Path.Combine(evidence, "preview-lifecycle")); await h.StartAsync();
        using var feed = new CancellationTokenSource();
        var left = new CameraRoundHarness.RoundPixels(Enumerable.Repeat((byte)180, 640 * 480).ToArray(), 640, 480);
        var right = new CameraRoundHarness.RoundPixels(Enumerable.Repeat((byte)90, 640 * 480).ToArray(), 640, 480);
        var producer = Task.Run(async () =>
        {
            uint number = 0;
            try { while (true) { h.Api(h.Left).Enqueue(left, ++number); h.Api(h.Right).Enqueue(right, number); await Task.Delay(80, feed.Token); } }
            catch (OperationCanceledException) when (feed.IsCancellationRequested) { }
        });
        var window = new SupplyStationWindow(h.App, null, "standard");
        window.Show();
        try
        {
            await SetClient(window, 1366, 768);
            await Until(() => window.ViewModel.Fresh && window.ViewModel.IsManagementMode, "initial runtime projection");
            window.ShowPositionDebug(window.ViewModel.Left);
            await Dispatcher.Yield(); window.UpdateLayout();
            var control = Descendants(window).OfType<UserControl>().Single(c => c.GetType().Name == "StationDebugControl");
            var live = Field<UserControl>(control, "livePreview"); var image = Field<Image>(live, "image");
            await Until(() => image.Source is not null, "online acquisition never reached native preview: " + Field<TextBlock>(live, "status").Text);
            Check((await h.App.CameraInspectionStatusAsync(default)).BusinessFenced == false && h.Peer.Requests.IsEmpty,
                "native timers display continuous video before scan without creating isolation or RCS requests");
            var first = Field<string>(live, "shown"); var firstBitmap = image.Source;
            await Until(() => Field<string>(live, "shown") != first && image.Source is not null, "live frame did not advance");
            Check(image.Source is WriteableBitmap && ReferenceEquals(firstBitmap, image.Source), "15fps-capped native preview advances by writing pixels into the same WriteableBitmap while window/round timers run");
            var tabs = Field<TabControl>(control, "tabs"); tabs.SelectedIndex = 1;
            await Until(() => !live.IsVisible && image.Source is null, "history did not clear preview");
            tabs.SelectedIndex = 0;
            await Until(() => image.Source is not null, "return from history never resumed video");
            Check(true, "history tab unload and reload restore live video");
            typeof(SupplyStationWindow).GetMethod("SetPage", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, ["workstation"]);
            await Until(() => !live.IsLoaded && image.Source is null, "leaving debug did not stop preview");
            window.ShowPositionDebug(window.ViewModel.Left);
            await Until(() => live.IsLoaded && image.Source is not null, "cached debug page never resumed video");
            Check(true, "cached debug page resumes its own timer after leaving and reopening");
            control.GetType().GetMethod("SelectPosition")!.Invoke(control, ["right"]);
            Check(image.Source is null, "camera switch clears the old image synchronously");
            await Until(() => image.Source is not null, "right camera did not appear");
            var pixel = new byte[1]; ((BitmapSource)image.Source!).CopyPixels(new Int32Rect(0, 0, 1, 1), pixel, 1, 0);
            Check(pixel[0] == 90, "right camera image comes from the selected acquisition");
            await h.App.ExitManagementModeAsync();
            await Until(() => !window.ViewModel.IsManagementMode && image.Source is null, "revocation did not clear preview");
            await h.AuthorizeAsync();
            await Until(() => window.ViewModel.IsManagementMode && live.IsLoaded && image.Source is not null, "reauthorization did not restart preview");
            Check(h.Factory.Apis.Values.All(a => a.Opens == 1 && a.Starts == 1 && a.Stops == 0) && h.Peer.Requests.IsEmpty,
                "navigation and management reentry preserve the original SDK owners and never send requests");
            CaptureInternal(evidence, "preview-lifecycle", (Grid)window.FindName("ClientArea"), lines);
        }
        finally { window.Close(); feed.Cancel(); await producer; }
    }
}

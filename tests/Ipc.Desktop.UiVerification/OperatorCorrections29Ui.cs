using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Platform;

static partial class Program
{
    static async Task VerifyOperatorCorrections29(string root, string evidence, List<string> lines)
    {
        void Check(bool ok, string text) { if (!ok) throw new InvalidDataException(text); lines.Add("PASS OP29-WPF " + text); }
        T Field<T>(object c, string name) => (T)c.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(c)!;
        using var reserve = new TcpListener(IPAddress.Loopback, 0); reserve.Start(); var port = ((IPEndPoint)reserve.LocalEndpoint).Port; reserve.Stop();
        await using var h = new CameraRoundHarness(root, Path.Combine(evidence, "offline-debug29"), rcsEndpoint: $"http://127.0.0.1:{port}/rcs/sorter/api", heartbeatSeconds: 1);
        await h.StartAsync();
        var blank = new CameraRoundHarness.RoundPixels(Enumerable.Repeat((byte)220, 640 * 240).ToArray(), 640, 240);
        h.Api(h.Left).Enqueue(blank, 1);
        await CameraRoundHarness.Until(() => Task.FromResult(h.Api(h.Left).Releases >= 1));
        var passive = await h.App.ReadCameraAcquisitionAsync(h.Left, default);
        using (var noPreview = h.Factory.Sources[h.Left].ReadPreviewLease())
            Check(passive.Diagnostics?.Frames == 1 && noPreview is null, "passive acquisition diagnostic reads actual frames without requesting a thumbnail");
        h.Api(h.Left).NextLostPackets = 1; h.Api(h.Left).Enqueue(blank, 2); h.Api(h.Left).Enqueue(blank, 3);
        await CameraRoundHarness.Until(() => Task.FromResult(h.Api(h.Left).Releases >= 3));
        var type = typeof(SupplyStationWindow).Assembly.GetType("Ipc.Application.Sportmaster.Desktop.CameraAcquisitionPresentation", true)!;
        var describe = type.GetMethod("Describe", BindingFlags.NonPublic | BindingFlags.Static)!;
        string Describe(CameraAcquisitionResult value) => (string)describe.Invoke(null, new object[] { value })!;
        passive = await h.App.ReadCameraAcquisitionAsync(h.Left, default);
        var status = Describe(passive);
        Check(status.Contains("取帧正常") && status.Contains("丢弃 1 帧") && !status.Contains("尚未取得") && !status.Contains("LostPackets") && !status.Contains("SDK") && passive.Diagnostics!.LastFrameRejection!.Contains("LostPackets"),
            "fresh acquisition and historical rejected frame use truthful Chinese while raw SDK evidence is retained");
        Check(Describe(passive with { Diagnostics = passive.Diagnostics! with { LastFrameAgeMs = 4000 } }).Contains("最近未收到") &&
            Describe(passive with { Diagnostics = passive.Diagnostics! with { State = "Faulted" } }).Contains("已停止"),
            "stale and faulted acquisition cannot be reported as current healthy video");
        Check(Describe(passive with { Epoch = Wire.Id() }).Contains("连接确认已过期") &&
            Describe(passive with { Deferred = true }).Contains("待配置"), "reconnect identity and deferred state do not borrow old frame counters");
        Check(Describe(passive with { Connection = passive.Connection! with { ValidUntil = DateTimeOffset.UtcNow.AddSeconds(-1) } }).Contains("仍有新帧"),
            "fresh acquisition is still reported when the separate connection confirmation has expired");
        var window = new SupplyStationWindow(h.App, null, "standard") { ConfirmDiscardDrafts = _ => true };
        window.Show();
        async Task Flush() { await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout(); }
        async Task Until(Func<bool> ready)
        { for (var n = 0; n < 200; n++) { await window.ViewModel.RefreshAsync(); await Flush(); if (ready()) return; await Task.Delay(25); } throw new TimeoutException("OP29 native state did not converge"); }
        try
        {
            await SetClient(window, 1366, 768);
            await Until(() => window.ViewModel.Fresh && window.ViewModel.IsManagementMode);
            await Until(() => window.ViewModel.Snapshot!.Health.Single(s => s.InstanceId == h.Config.Wcs!.InstanceId).Ready == false);
            Check(window.ViewModel.Snapshot!.Health.Single(s => s.InstanceId == h.Config.Wcs!.InstanceId).Ready == false, "fixture uses a genuinely unavailable loopback RCS with both camera acquisitions online");
            window.ShowPositionDebug(window.ViewModel.Left); await Flush();
            var debug = Descendants(window).OfType<UserControl>().Single(c => c.GetType().Name == "StationDebugControl");
            var trigger = Field<Button>(debug, "trigger"); var columns = Field<Grid>(debug, "columns");
            foreach (var size in new[] { (1366,768), (1092,614), (800,420) })
            {
                await SetClient(window, size.Item1, size.Item2); await Flush();
                Check(trigger.IsEnabled && trigger.ActualHeight >= 44 && Inside(trigger, (Grid)window.FindName("ClientArea")), "offline-RCS scan action remains visible, enabled and touchable at " + size.Item1);
                var scroll = (ScrollViewer)VisualTreeHelperParent(columns, typeof(ScrollViewer))!;
                scroll.ScrollToBottom(); await Flush();
                Check(Inside(trigger, (Grid)window.FindName("ClientArea")), "scrolling request details cannot hide the scan action at " + size.Item1);
                scroll.ScrollToTop(); await Flush();
                CaptureInternal(evidence, "op29-debug-video-first-" + size.Item1, (Grid)window.FindName("ClientArea"), lines);
            }
            await SetClient(window, 1366, 768); await Flush();
            var requestPanel = (FrameworkElement)Field<TextBox>(debug, "requestBarcode");
            Check(columns.TranslatePoint(new Point(), debug).Y < requestPanel.TranslatePoint(new Point(), debug).Y, "live video and locked result precede request/response controls again");
            trigger.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await CameraRoundHarness.Until(async () => (await h.App.ReadCameraReadRoundAsync(default)).State == "Reading");
            h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-OFFLINE-29"), 4);
            var locked = await h.LockedAsync();
            await Until(() => Field<TextBox>(debug, "barcode").Text == "SYNTH-OFFLINE-29" && Field<System.Windows.Controls.Image>(debug, "photo").Source is not null);
            Check(locked.State == "Locked" && locked.Frame is { ImageAvailable: true } && locked.SendAttempts == 0 && h.Peer.Requests.IsEmpty &&
                Field<System.Windows.Controls.Canvas>(debug, "marks").Children.Count > 0,
                "actual MVS/software decoding locks the matching image and green annotation with RCS offline and no request sent");
            Check(Ipc.Application.ParcelFlow.Desktop.ChineseInterfaceText.Message("QueryUnsupported · 原接口不提供结果查询；原请求和回包事实保留，不会重发。").StartsWith("当前接口不支持结果查询", StringComparison.Ordinal),
                "unsupported query receipt does not leak its code-layer status into the operator message");
            CaptureInternal(evidence, "op29-debug-offline-locked", (Grid)window.FindName("ClientArea"), lines);
        }
        finally { window.Close(); await Flush(); }
    }
    static DependencyObject? VisualTreeHelperParent(DependencyObject child, Type target)
    {
        for (var parent = System.Windows.Media.VisualTreeHelper.GetParent(child); parent is not null; parent = System.Windows.Media.VisualTreeHelper.GetParent(parent))
            if (target.IsInstanceOfType(parent)) return parent;
        return null;
    }
}

using System.Reflection;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Rectangle = System.Windows.Shapes.Rectangle;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Platform;

static partial class Program
{
    static async Task VerifyCameraRoundUi(string root, string evidence, List<string> lines)
    {
        void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); lines.Add("PASS ROUND-UI " + message); }
        await using var h = new CameraRoundHarness(root, Path.Combine(evidence, "rounds")); await h.StartAsync();
        var app = DispatchProxy.Create<ICameraRoundUiFixture, CameraRoundUiProbe>(); var probe = (CameraRoundUiProbe)(object)app; probe.Inner = h.App;
        var window = new SupplyStationWindow(app, null, "standard") { ConfirmDiscardDrafts = _ => true, Title = "扫码闭环 · 合成端口" };
        window.Show(); // WPF requires a visible owner before creating its auxiliary window.
        var second = new SupplyStationWindow(h.App, null, "standard", owner: window) { ConfirmDiscardDrafts = _ => true, Title = "第二窗口 · 同一Application" };
        TaskCompletionSource? heldArm = null;
        TaskCompletionSource? heldImage = null;
        second.Show(); var vm = window.ViewModel;
        async Task Flush() { await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout(); second.UpdateLayout(); }
        async Task Until(Func<bool> done)
        { for (var i = 0; i < 150; i++) { await vm.RefreshAsync(); await second.ViewModel.RefreshAsync(); await Flush(); if (done()) return; await Task.Delay(35); } throw new TimeoutException("Round native UI did not converge"); }
        T Field<T>(UserControl c, string name) => (T)c.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(c)!;
        Task Poll(UserControl c) => (Task)c.GetType().GetMethod("PollAsync")!.Invoke(c, null)!;
        void ClickField(UserControl c, string name) => Field<Button>(c, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        try
        {
            await SetClient(window, 1366, 768); await SetClient(second, 1366, 768);
            await Until(() => vm.Fresh && vm.IsManagementMode && second.ViewModel.IsManagementMode);
            window.ShowPositionDebug(vm.Left); second.ShowPositionDebug(second.ViewModel.Left); await Flush();
            var control = Descendants(window).OfType<UserControl>().Single(c => c.GetType().Name == "StationDebugControl");
            var other = Descendants(second).OfType<UserControl>().Single(c => c.GetType().Name == "StationDebugControl");
            Field<DispatcherTimer>(control, "timer").Stop(); Field<DispatcherTimer>(other, "timer").Stop();
            Field<DispatcherTimer>(Field<UserControl>(control, "livePreview"), "timer").Stop();
            Field<DispatcherTimer>(Field<UserControl>(other, "livePreview"), "timer").Stop();
            // Drive deterministic native snapshots explicitly; an extra harness
            // reader must not contend with two independent 500ms host timers.
            ((DispatcherTimer)typeof(SupplyStationWindow).GetField("timer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!).Stop();
            ((DispatcherTimer)typeof(SupplyStationWindow).GetField("timer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(second)!).Stop();
            await vm.RefreshAsync(); await second.ViewModel.RefreshAsync(); await Task.Delay(60);
            Check(!Descendants(control).OfType<TextBlock>().Any(t => t.Text.Contains("调试说明")), "camera debug does not require a free-text reason");
            Field<CheckBox>(control, "scene").IsChecked = true;
            Check(Field<TextBlock>(control, "imageHint").IsVisible && Field<TextBlock>(control, "imageHint").Text.Contains("等待"), "empty photo explains how to begin without showing a fictitious frame");
            var visual = Field<StackPanel>(control, "visual"); var fullCode = Field<TextBox>(control, "barcode");
            Check(visual.Children.Contains(fullCode) && fullCode.TranslatePoint(new Point(), visual).Y < Field<ScrollViewer>(control, "viewport").TranslatePoint(new Point(), visual).Y,
                "complete selectable barcode is above its photo in the right-hand visual column");
            probe.LoseBeginReply = true;
            ClickField(control, "trigger");
            await Until(() => !Field<bool>(control, "busy") && (Field<CameraInspectionStatus>(control, "scope")?.BusinessFenced ?? false));
            Check((await h.App.ListCameraInspectionSessionsAsync(default)).Length == 1 && h.Peer.Requests.IsEmpty, "lost begin receipt is recovered as the same durable isolated scope");
            ClickField(control, "trigger");
            await Until(() => Equals(Field<Button>(control, "trigger").Content, "扫码中…"));
            Check(Field<Image>(control, "photo").Source is null && Field<TextBox>(control, "barcode").Text.Length == 0 && h.Peer.Requests.IsEmpty,
                "trigger enters a clean reading round without a photo/request or HTTP call");
            var isolated = await h.App.SnapshotAsync();
            var denied = await h.App.SubmitParcelBarcodeAsync(new(Wire.Id(), isolated.RuntimeId, isolated.Configuration.Revision, "left", "ISOLATED-MANUAL",
                ParcelBarcodeInputKind.Keyboard, null, null), default);
            Check(denied.Status == "Rejected" && denied.Detail.Contains("调试隔离") && h.Peer.Requests.IsEmpty, "active debug isolation still blocks ordinary manual supply without sending");
            Check(Field<TextBlock>(control, "imageHint").Text.Contains("正在读取"), "reading placeholder describes the current round");
            var pixels = CameraRoundHarness.Barcode("UI-ROUND-A"); h.Api(h.Left).Enqueue(pixels, 1); await h.LockedAsync(); await Poll(control); await Poll(other); await Flush();
            var a = Field<CameraReadRound>(control, "current");
            var bitmap = (BitmapSource)Field<Image>(control, "photo").Source!; var bytes = new byte[bitmap.PixelWidth * bitmap.PixelHeight]; bitmap.CopyPixels(bytes, bitmap.PixelWidth, 0);
            Check(bytes.SequenceEqual(pixels.Data) && Field<TextBox>(control, "barcode").Text == "UI-ROUND-A" && Equals(Field<Button>(control, "trigger").Content, "再次扫码"),
                "native photo pixels/full code and again-scan label belong to the locked original");
            var symbol = a.Frame!.Symbols.Single(); var box = Field<Canvas>(control, "marks").Children.OfType<Rectangle>().Single();
            Check(Canvas.GetLeft(box) == symbol.Corners.Min(p => p.X) && Canvas.GetTop(box) == symbol.Corners.Min(p => p.Y) &&
                box.Width == symbol.Corners.Max(p => p.X) - symbol.Corners.Min(p => p.X) && ((SolidColorBrush)box.Stroke).Color == (Color)ColorConverter.ConvertFromString("#28D76F"),
                "green native rectangle uses actual same-frame recognition coordinates");
            Check(Field<TextBox>(control, "requestBarcode").Text == "UI-ROUND-A" && Field<TextBox>(control, "requestBody").Text == a.RequestBody &&
                Field<TextBox>(control, "requestBody").IsReadOnly && h.Peer.Requests.IsEmpty && Field<Button>(control, "send").IsEnabled, "native JSON auto-fill is frozen and explicit send is the only enabled delivery action");
            h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("NEXT-MUST-NOT-REPLACE"), 2); await Task.Delay(120); await Poll(control);
            Check(Field<Image>(control, "photo").Source == bitmap && Field<TextBox>(control, "barcode").Text == "UI-ROUND-A" &&
                Field<TextBox>(control, "requestBody").Text == a.RequestBody && !Field<TextBlock>(control, "imageHint").IsVisible && h.Peer.Requests.IsEmpty,
                "next parcel cannot replace the locked photo/barcode/request and no automatic send occurs");
            CaptureInternal(evidence, "round-locked-native", (Grid)window.FindName("ClientArea"), lines);
            probe.FailRoundRead = true; await Poll(control);
            Check(!Field<Button>(control, "send").IsEnabled, "failed authoritative polling cannot re-enable send from a cached projection");
            probe.FailRoundRead = false; await Poll(control); probe.CancelRoundRead = true; await Poll(control);
            Check(!Field<Button>(control, "send").IsEnabled, "cancelled authoritative read also keeps cached send disabled");
            probe.CancelRoundRead = false; await vm.RefreshAsync(); await Poll(control);
            Check(Field<Button>(control, "send").IsEnabled, "successful authoritative read plus fresh host snapshot restores eligible send");
            ClickField(control, "send");
            await Until(() => Field<TextBox>(control, "responseBody").Text.StartsWith("状态：Accepted"));
            ClickField(control, "send"); await Until(() => Equals(Field<Button>(control, "send").Content, "核对发送结果"));
            Check(h.Peer.Requests.Count == 1 && h.Peer.Requests.Single() == a.RequestBody, "click-send and check-result never create a second HTTP request");
            await Poll(other); ClickField(other, "trigger");
            await Until(() => Equals(Field<Button>(other, "trigger").Content, "扫码中…"));
            h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("UI-ROUND-B"), 3); await h.LockedAsync(); await Poll(other); await Poll(control);
            Check(Field<TextBox>(control, "barcode").Text == "UI-ROUND-A" && Field<TextBox>(other, "barcode").Text == "UI-ROUND-B" && !Field<Button>(control, "send").IsEnabled,
                "another window can own the next round while this window retains its prior photo/code and cannot send it");
            // Force a real multi-window CAS rejection after the first window captured its baseline.
            heldArm = new(TaskCreationOptions.RunContinuationsAsynchronously); probe.HoldArm = heldArm; probe.ArmEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            ClickField(control, "trigger"); await probe.ArmEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Check(Field<Image>(control, "photo").Source is null && Field<TextBox>(control, "requestBody").Text.Length == 0, "again-scan clears old display while the command is still waiting");
            await h.ArmAsync(h.Right); h.Api(h.Right).Enqueue(CameraRoundHarness.Barcode("UI-ROUND-C"), 1); await h.LockedAsync();
            heldArm.TrySetResult(); await Until(() => !Field<bool>(control, "busy")); await Poll(control);
            ClickField(control, "trigger"); await Until(() => !Field<bool>(control, "busy"));
            var retry = await h.App.ReadCameraReadRoundAsync(default);
            Check(retry.State == "Reading" && retry.DeviceId == h.Left, "explicit retry after a known stale CAS rejection uses the newly observed round instead of trapping the old command");
            h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("UI-ROUND-D"), 4); await h.LockedAsync(); await Poll(control);
            Check(Field<TextBox>(control, "barcode").Text == "UI-ROUND-D", "retry recovers the intended camera and next frame");
            heldImage = new(TaskCreationOptions.RunContinuationsAsynchronously); probe.HoldImage = heldImage; probe.ImageEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            ClickField(control, "trigger"); await Until(() => Equals(Field<Button>(control, "trigger").Content, "扫码中…"));
            h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("UI-ROUND-E"), 5); var e = await h.LockedAsync();
            var late = Poll(control); await probe.ImageEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)); ClickField(control, "right");
            heldImage.TrySetResult(); await late; await Flush();
            Check(Field<Image>(control, "photo").Source is null && Field<TextBox>(control, "barcode").Text.Length == 0,
                "late original-image completion cannot repaint after changing camera/selection generation");
            ClickField(control, "left"); await Poll(control); await Flush();
            Descendants(control).OfType<Expander>().Single(x => Equals(x.Header, "照片缩放与条码标注")).IsExpanded = true; await Flush();
            Descendants(control).OfType<Button>().Single(b => Equals(b.Content, "1:1")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Flush();
            Check(Math.Abs(Field<double>(control, "zoom") - 1 / VisualTreeHelper.GetDpi(control).DpiScaleX) < .0001,
                "native 1:1 maps image pixels to device pixels at the actual monitor DPI");
            await SetClient(window, 1024, 768); await Flush(); CaptureInternal(evidence, "round-1024-native", (Grid)window.FindName("ClientArea"), lines);
            await SetClient(window, 800, 420); await Flush();
            Field<Button>(control, "send").BringIntoView(); await Flush();
            Check(Inside(Field<Button>(control, "send"), (Grid)window.FindName("ClientArea")), "800x420 scroll route brings the real send action into the usable client viewport");
            CaptureInternal(evidence, "round-800-native", (Grid)window.FindName("ClientArea"), lines);
            var opens = h.Factory.Apis.Values.Sum(x => x.Opens); second.Close();
            Check(h.Factory.Apis.Values.Sum(x => x.Opens) == opens && h.Factory.Apis.Values.All(x => x.Starts == 1 && x.Stops == 0), "window close does not stop or recreate the shared acquisition owner");
            await vm.RefreshAsync(); await Poll(control);
            lines.Add("DIAG final-send ui=" + Field<Button>(control, "send").IsEnabled + "; fresh=" + vm.Fresh + "; authoritative=" + Field<bool>(control, "authoritative") +
                "; application=" + (await h.App.ReadCameraReadRoundAsync(default)).CanSend + "; feedback=" + Field<TextBlock>(control, "feedback").Text);
            lines.Add("DIAG final-round " + Wire.Encode(await h.App.ReadCameraReadRoundAsync(default)));
            var diagnostic = await h.App.SnapshotAsync();
            lines.Add("DIAG final-dependencies " + Wire.Encode(new { diagnostic.State, diagnostic.BlockingReasons, diagnostic.Devices, diagnostic.Health,
                Operations = diagnostic.Operations.Select(o => new { o.Id, o.PositionId, o.State, Stage = o.Wcs?.State, Attempts = o.Wcs?.Attempts, o.RecoveryRequired }) }));
            await Until(() => Field<Button>(control, "send").IsEnabled);
            h.Peer.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously); ClickField(control, "send");
            await CameraRoundHarness.Until(() => Task.FromResult(h.Peer.Requests.Count == 2));
            await h.App.ExitManagementModeAsync(); await vm.RefreshAsync(); await Flush();
            Check(Field<Image>(control, "photo").Source is null && Field<TextBox>(control, "barcode").Text.Length == 0 &&
                (await h.App.SnapshotAsync()).Paused, "management revoke clears sensitive local evidence and retains the business fence");
            await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Any(o => o.Id == e.OperationId && o.Wcs?.State == StageState.Unknown));
            h.Peer.Hold.TrySetResult(); h.Peer.Hold = null; await h.AuthorizeAsync();
            Check((await h.App.SnapshotAsync()).Operations.Single(o => o.Id == e.OperationId).Wcs is { State: StageState.Unknown, Attempts: 1 } pending &&
                pending.Request.IntegrationRequest!.Body == e.RequestBody && h.Peer.Requests.Count == 2,
                "native in-flight revoke retains original Unknown/JSON without an extra HTTP request");
        }
        finally { heldArm?.TrySetResult(); heldImage?.TrySetResult(); window.Close(); second.Close(); }
    }
}
public interface ICameraRoundUiFixture : IHistoryProjectionFixture, ICameraInspection, ICameraReadRounds { }
public class CameraRoundUiProbe : ProjectionProbe
{
    public bool FailRoundRead, CancelRoundRead, LoseBeginReply;
    public TaskCompletionSource? HoldArm, ArmEntered, HoldImage, ImageEntered;
    protected override object? Invoke(MethodInfo? method, object?[]? arguments)
    {
        if (method!.Name == nameof(ICameraReadRounds.ReadCameraReadRoundAsync) && FailRoundRead) return Task.FromException<CameraReadRound>(new IOException("Synthetic authoritative round read failed"));
        if (method.Name == nameof(ICameraReadRounds.ReadCameraReadRoundAsync) && CancelRoundRead) return Task.FromCanceled<CameraReadRound>(new CancellationToken(true));
        if (method.Name == nameof(ICameraReadRounds.ArmCameraReadAsync) && HoldArm is not null) return Arm((ArmCameraRead)arguments![0]!, (CancellationToken)arguments[1]!);
        if (method.Name == nameof(ICameraInspection.BeginCameraInspectionAsync) && LoseBeginReply) { LoseBeginReply = false; return Begin((BeginCameraInspection)arguments![0]!, (CancellationToken)arguments[1]!); }
        if (method.Name == nameof(ICameraInspection.ReadCameraOriginalAsync) && HoldImage is not null) return Image((string)arguments![0]!, (CancellationToken)arguments[1]!);
        return base.Invoke(method, arguments);
    }
    async Task<CameraReadRound> Arm(ArmCameraRead command, CancellationToken token)
    { var held = HoldArm; HoldArm = null; ArmEntered?.TrySetResult(); await held!.Task; return await ((ICameraReadRounds)Inner).ArmCameraReadAsync(command, token); }
    async Task<CameraInspectionStatus> Begin(BeginCameraInspection command, CancellationToken token)
    { await ((ICameraInspection)Inner).BeginCameraInspectionAsync(command, token); throw new IOException("Synthetic lost begin response after durable entry"); }
    async Task<CameraOriginalImage?> Image(string id, CancellationToken token)
    { var held = HoldImage; HoldImage = null; var image = await ((ICameraInspection)Inner).ReadCameraOriginalAsync(id, token); ImageEntered?.TrySetResult(); await held!.Task; return image; }
}

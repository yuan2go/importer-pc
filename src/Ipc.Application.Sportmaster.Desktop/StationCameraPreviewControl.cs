using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.Sportmaster.Desktop;

// Passive, latest-frame projection of the existing Application acquisition owner.
internal sealed class StationCameraPreviewControl : UserControl
{
    readonly SupplyStationViewModel vm;
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    readonly Image image = new() { Stretch = Stretch.Uniform, IsHitTestVisible = false };
    readonly TextBlock hint = new() { Text = "等待相机实时画面", Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new(24) };
    readonly TextBlock status = Label(), counters = Label();
    CancellationTokenSource? lifetime;
    string device = "", shown = "";
    DateTimeOffset? capturedAt;
    long generation;
    bool polling;
    public StationCameraPreviewControl(SupplyStationViewModel vm)
    {
        this.vm = vm;
        var body = new StackPanel(); var screen = new Grid { Height = 286, Background = new SolidColorBrush(Color.FromRgb(37, 43, 51)) };
        screen.Children.Add(image); screen.Children.Add(hint); body.Children.Add(screen); body.Children.Add(status);
        body.Children.Add(new Expander { Header = "取帧与解码诊断", Content = counters, Margin = new(0, 4, 0, 0) });
        screen.ToolTip = "实时预览持续显示；不改变现有作业状态或本轮锁定结果。";
        Content = body; AutomationProperties.SetName(screen, "所选相机实时视频，与本轮锁定照片独立");
        timer.Tick += async (_, _) => await PollAsync();
        Loaded += async (_, _) => { lifetime = new(); timer.Start(); await PollAsync(); };
        Unloaded += (_, _) => Revoke();
        IsVisibleChanged += async (_, _) => { generation++; Clear("等待当前相机的新画面"); if (IsVisible) await PollAsync(); };
    }
    public void SelectDevice(string id) { if (device == id) return; device = id; generation++; Clear("等待所选相机的新画面"); counters.Text = ""; }
    public async Task PollAsync()
    {
        if (!vm.IsManagementMode) { Clear("管理模式已结束"); counters.Text = ""; return; }
        if (capturedAt is { } at && DateTimeOffset.UtcNow - at > TimeSpan.FromSeconds(3)) Clear("画面已过期，等待新帧");
        if (polling || lifetime is null || !IsVisible || string.IsNullOrEmpty(device)) return;
        if (vm.Application is not ICameraDiagnostics api) { Clear("当前来源不提供实时视频"); return; }
        var version = generation; var selected = device; var token = lifetime.Token; polling = true;
        try
        {
            var value = await vm.ServiceAsync(() => api.ReadCameraPreviewAsync(selected, token));
            if (token.IsCancellationRequested || version != generation || !IsVisible || !vm.IsManagementMode) return;
            status.Text = value.Status; counters.Text = Describe(value.Diagnostics);
            if (value.Frame is not { } frame) { Clear(value.Status); return; }
            if (frame.DeviceId != selected || frame.Width <= 0 || frame.Height <= 0 || frame.Width > 640 || frame.Height > 480 ||
                frame.Gray8.LongLength != (long)frame.Width * frame.Height || frame.CapturedAt > DateTimeOffset.UtcNow ||
                DateTimeOffset.UtcNow - frame.CapturedAt > TimeSpan.FromSeconds(3)) { Clear("画面身份或时间无效，等待当前连接的新帧"); return; }
            var key = frame.Epoch + ":" + frame.FrameNumber;
            if (shown != key)
            {
                var bitmap = BitmapSource.Create(frame.Width, frame.Height, 96, 96, PixelFormats.Gray8, null, frame.Gray8, frame.Width);
                bitmap.Freeze(); image.Source = bitmap; shown = key; capturedAt = frame.CapturedAt;
            }
            hint.Visibility = Visibility.Collapsed;
            status.Text = "实时视频 · 帧 " + frame.FrameNumber + " · " + frame.CapturedAt.ToLocalTime().ToString("HH:mm:ss.fff");
        }
        catch (OperationCanceledException) { if (version == generation) Clear("预览已停止"); }
        catch (Exception error) { if (version == generation) { Clear("预览读取失败：" + error.Message); counters.Text = ""; } }
        finally { polling = false; }
    }
    static string Describe(ObservationDiagnostics? d)
    {
        if (d is null) return "当前来源未提供取帧与解码计数。";
        var state = d.State switch
        {
            "WaitingForFrame" => "尚未取得完整图像", "FrameStale" => "取帧已停滞", "Decoding" => "正在解码",
            "DecodePaused" => "本轮解码已暂停，实时预览继续", "NoBarcode" => "已解码，尚未发现条码",
            "Reading" => "解码链路运行中", "Faulted" => "采集异常", "Stopped" => "采集已停止",
            "DecoderUnavailable" => "解码器不可用", _ => "正在取帧"
        };
        return state + "\n取帧 " + d.Frames + " · 无数据 " + d.NoData + " · 解码完成 " + d.DecodeCompleted + "/" + d.DecodeStarted +
            " · 空结果 " + d.EmptyFrames + "\n候选 " + d.Candidates + " · 上报 " + d.Delivered + " · 拒收 " + d.Rejected +
            (d.Failure is null ? "" : "\n" + d.Failure);
    }
    void Clear(string message) { image.Source = null; shown = ""; capturedAt = null; hint.Text = message; hint.Visibility = Visibility.Visible; status.Text = message; }
    public void Revoke() { generation++; timer.Stop(); lifetime?.Cancel(); lifetime?.Dispose(); lifetime = null; Clear("预览已停止"); counters.Text = ""; }
    static TextBlock Label() => new() { TextWrapping = TextWrapping.Wrap, FontSize = UiTypography.Label, Margin = new(0, 5, 0, 0) };
}

using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Ipc.Application.Contracts;

namespace Ipc.Application.ParcelFlow.Desktop;

// A frozen original identity, never a live-camera preview or a barcode lookup.
public sealed class ParcelPictureWindow : Window
{
    readonly ParcelViewModel vm;
    readonly ParcelPictureQuery query;
    readonly string barcode;
    readonly CancellationTokenSource lifetime = new();
    CancellationTokenSource? reading;
    readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 8) };
    readonly TextBox provenance = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new(0), MaxHeight = 100, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    readonly ScrollViewer viewport = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = Brushes.DarkSlateGray };
    readonly Canvas scene = new() { ClipToBounds = true };
    readonly Image image = new() { Stretch = Stretch.Fill, IsHitTestVisible = false };
    readonly CheckBox annotations = new() { Content = "识别位置", IsChecked = true, VerticalAlignment = VerticalAlignment.Center, Margin = new(12, 0, 12, 0) };
    readonly List<Polygon> polygons = [];
    CameraFrameMetadata? frame;
    double zoom = 1;
    bool fit = true, closed;
    long generation;
    Point? pan;
    double startX, startY;
    readonly string? operatorSession;
    public static void Open(Window owner, ParcelViewModel vm, ParcelPictureQuery query, string barcode)
    {
        if (vm.Application is not IParcelWorkstation) { vm.Message = "当前业务未提供包裹图片查看能力。"; return; }
        try { new ParcelPictureWindow(vm, query, barcode) { Owner = owner }.ShowDialog(); }
        catch (Exception error) { vm.Message = "图片窗口未能打开：" + error.Message; }
    }
    ParcelPictureWindow(ParcelViewModel vm, ParcelPictureQuery query, string barcode)
    {
        this.vm = vm; this.query = query; this.barcode = barcode;
        operatorSession = vm.Snapshot?.CurrentOperator?.SessionId;
        Title = "包裹图片"; Width = Math.Min(1100, SystemParameters.WorkArea.Width); Height = Math.Min(760, SystemParameters.WorkArea.Height);
        MinWidth = 640; MinHeight = 420; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        UiTypography.Apply(this); FontSize = UiTypography.Body; Background = Brushes.White;
        var body = new DockPanel { Margin = new(16) };
        var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); body.Children.Add(top);
        top.Children.Add(new TextBox { Text = barcode, IsReadOnly = true, FontFamily = UiTypography.Code, FontSize = UiTypography.PageTitle,
            TextWrapping = TextWrapping.Wrap, MaxHeight = 80, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, BorderThickness = new(0) });
        var actions = new WrapPanel { Margin = new(0, 8, 0, 0) }; top.Children.Add(actions);
        Add(actions, "适应窗口", () => { fit = true; Fit(); });
        Add(actions, "原始1:1", () => Scale(1 / VisualTreeHelper.GetDpi(this).DpiScaleX));
        Add(actions, "放大", () => Scale(zoom * 1.25)); Add(actions, "缩小", () => Scale(zoom / 1.25));
        actions.Children.Add(annotations); annotations.Checked += (_, _) => DrawAnnotations(); annotations.Unchecked += (_, _) => DrawAnnotations();
        var reload = new Button { Content = "刷新原图", MinHeight = 44, Padding = new(12, 6, 12, 6) };
        reload.Click += async (_, _) => await LoadOriginalAsync(); actions.Children.Add(reload);
        top.Children.Add(status);
        var bottom = new DockPanel { Margin = new(0, 8, 0, 0) }; DockPanel.SetDock(bottom, Dock.Bottom); body.Children.Add(bottom);
        var close = new Button { Content = "关闭", IsCancel = true, MinHeight = 44, MinWidth = 88, Padding = new(12, 6, 12, 6) };
        close.Click += (_, _) => Close(); DockPanel.SetDock(close, Dock.Right); bottom.Children.Add(close);
        var detail = new Expander { Header = "图片来源与完整性", Content = provenance }; bottom.Children.Add(detail);
        viewport.Content = scene; scene.Children.Add(image); body.Children.Add(viewport); Content = body;
        System.Windows.Automation.AutomationProperties.SetName(viewport, "包裹原图，Ctrl加滚轮缩放，左键拖动平移");
        viewport.SizeChanged += (_, _) => { if (fit) Fit(); };
        viewport.ScrollChanged += (_, e) => { if (fit && (e.ViewportWidthChange != 0 || e.ViewportHeightChange != 0)) Fit(); };
        viewport.PreviewMouseWheel += (_, e) => { if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return; e.Handled = true; Scale(zoom * (e.Delta > 0 ? 1.25 : .8)); };
        viewport.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (frame is null || e.OriginalSource is System.Windows.Controls.Primitives.Thumb) return;
            pan = e.GetPosition(viewport); startX = viewport.HorizontalOffset; startY = viewport.VerticalOffset; viewport.CaptureMouse(); e.Handled = true;
        };
        viewport.MouseMove += (_, e) => { if (pan is not { } start || !viewport.IsMouseCaptured) return; var p = e.GetPosition(viewport); viewport.ScrollToHorizontalOffset(startX + start.X - p.X); viewport.ScrollToVerticalOffset(startY + start.Y - p.Y); };
        viewport.MouseLeftButtonUp += (_, _) => { pan = null; viewport.ReleaseMouseCapture(); };
        viewport.LostMouseCapture += (_, _) => pan = null;
        vm.PropertyChanged += SessionChanged;
        Loaded += async (_, _) => await LoadOriginalAsync();
        Closed += (_, _) => { closed = true; generation++; lifetime.Cancel(); reading?.Cancel(); ClearImage(); vm.PropertyChanged -= SessionChanged; lifetime.Dispose(); };
    }
    void SessionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(vm.Snapshot) || vm.Snapshot?.CurrentOperator?.SessionId == operatorSession) return;
        generation++; reading?.Cancel(); ClearImage(); status.Text = "操作员会话已改变，请关闭后从当前授权会话重新打开。";
    }
    void Add(Panel panel, string label, Action action)
    {
        var button = new Button { Content = label, MinHeight = 44, Padding = new(12, 6, 12, 6), Margin = new(0, 0, 6, 0) };
        button.Click += (_, _) => action(); panel.Children.Add(button);
    }
    void ClearImage()
    {
        image.Source = null; frame = null; provenance.Clear(); foreach (var polygon in polygons) scene.Children.Remove(polygon); polygons.Clear(); scene.Width = scene.Height = 0;
    }
    async Task LoadOriginalAsync()
    {
        if (closed || vm.Application is not IParcelWorkstation service || vm.Snapshot?.CurrentOperator?.SessionId != operatorSession) return;
        var version = ++generation; reading?.Cancel(); var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); reading = cancellation;
        ClearImage(); status.Text = "正在读取指定包裹原图…";
        try
        {
            var result = await vm.ServiceAsync(() => service.ReadParcelPictureAsync(query, cancellation.Token));
            if (closed || version != generation || cancellation.IsCancellationRequested) return;
            status.Text = result.Detail;
            var original = result.Image;
            if (original is null && result.Archived is { } archived)
            {
                original = await vm.ServiceAsync(() => Task.FromResult(DecodeArchived(archived)));
                if (closed || version != generation || cancellation.IsCancellationRequested) return;
            }
            if (original is null) return;
            frame = original.Metadata;
            var bitmap = BitmapSource.Create(frame.Width, frame.Height, 96, 96, PixelFormats.Gray8, null, original.Gray8, frame.Width); bitmap.Freeze();
            image.Source = bitmap; image.Width = scene.Width = frame.Width; image.Height = scene.Height = frame.Height;
            provenance.Text = "作业：" + (result.OperationId ?? "本次未准入观察") + "\n观察：" + result.ObservationId + "\n相机：" + frame.DeviceId +
                " · 周期：" + frame.Epoch + " · 帧：" + frame.FrameNumber + "\n采集：" + frame.CapturedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff zzz") +
                "\n像素：" + frame.Width + " × " + frame.Height + " · SHA256：" + frame.OriginalSha256;
            DrawAnnotations(); fit = true; Fit();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error) { if (!closed && version == generation) { ClearImage(); status.Text = "原图未能取得：" + error.Message; } }
        finally { if (ReferenceEquals(reading, cancellation)) reading = null; cancellation.Dispose(); }
    }
    static CameraOriginalImage DecodeArchived(ArchivedParcelPicture archived)
    {
        var data = archived.Png; var info = archived.Metadata;
        if (data.Length < 33 || !data.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ||
            System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(16, 4)) != info.Width ||
            System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(20, 4)) != info.Height || (long)info.Width * info.Height > 33554432)
            throw new InvalidDataException("留档PNG像素头与原帧尺寸不符。");
        using var stream = new System.IO.MemoryStream(data, false);
        var decoded = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames.Single();
        if (decoded.PixelWidth != info.Width || decoded.PixelHeight != info.Height) throw new InvalidDataException("解码PNG尺寸不符。");
        BitmapSource gray = decoded.Format == PixelFormats.Gray8 ? decoded : new FormatConvertedBitmap(decoded, PixelFormats.Gray8, null, 0);
        var pixels = new byte[checked(info.Width * info.Height)]; gray.CopyPixels(pixels, info.Width, 0);
        if (Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(pixels)) != info.RawSha256) throw new InvalidDataException("留档原始像素SHA256不符。");
        var source = info.Acquisition; var numberText = source.SourceEventId.StartsWith("mvs.frame.", StringComparison.Ordinal) ? source.SourceEventId[10..] : "";
        _ = long.TryParse(numberText, out var number);
        return new(new("business-original:" + archived.OperationId, "business-original", source.DeviceId, source.Epoch, number,
            source.ObservedAt, info.Width, info.Height, [], 0, null, source.SourceEventId, null, info.RawSha256!, true,
            "原业务PNG留档；未包含识别几何时不绘制示意框", OperationId: archived.OperationId), pixels);
    }
    void Fit()
    {
        if (frame is null || viewport.ViewportWidth <= 0 || viewport.ViewportHeight <= 0) return;
        zoom = Math.Clamp(Math.Min(viewport.ViewportWidth / frame.Width, viewport.ViewportHeight / frame.Height), .02, 16); ApplyScale();
    }
    void Scale(double value) { fit = false; zoom = Math.Clamp(value, .02, 16); ApplyScale(); }
    void ApplyScale()
    {
        scene.LayoutTransform = new ScaleTransform(zoom, zoom);
        foreach (var polygon in polygons) polygon.StrokeThickness = 2 / zoom;
    }
    void DrawAnnotations()
    {
        foreach (var polygon in polygons) scene.Children.Remove(polygon); polygons.Clear();
        if (annotations.IsChecked != true || frame is null) return;
        foreach (var symbol in frame.Symbols.Where(s => s.Corners.Length == 4))
        {
            var polygon = new Polygon { Points = new PointCollection(symbol.Corners.Select(p => new Point(p.X, p.Y))),
                Stroke = Brushes.OrangeRed, StrokeThickness = 2 / zoom, Fill = Brushes.Transparent, IsHitTestVisible = false };
            polygons.Add(polygon); scene.Children.Add(polygon);
        }
    }
}

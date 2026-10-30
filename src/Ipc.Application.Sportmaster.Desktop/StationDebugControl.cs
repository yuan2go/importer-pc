using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.Sportmaster.Desktop;

// A projection of the Application's one-shot round, never a local camera/sender.
internal sealed class StationDebugControl : UserControl
{
    readonly SupplyStationViewModel vm;
    readonly ICameraInspection? inspection;
    readonly ICameraReadRounds? rounds;
    readonly StationDebugEvidenceControl evidence;
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    readonly Button left = Action("左相机"), right = Action("右相机"), trigger = Action("触发扫码"), send = Action("发送请求"), end = Action("确认清场并结束调试");
    readonly TextBlock state = Text("点击触发扫码开始本轮读取"), feedback = Text(""), photoCaption = Text("本轮读码照片"), result = Text("本轮尚未发送请求。"), camera = Text("左相机");
    readonly TextBox barcode = Value(), requestBarcode = Value(), requestBody = Value(), responseBody = Value(), reason = new() { MaxLength = 1024, MinHeight = 44 };
    readonly CheckBox scene = new() { Content = "已停止真实投放，并核对两位当前包裹", Margin = new(0, 8, 0, 8) };
    readonly Image photo = new() { Stretch = Stretch.Fill, IsHitTestVisible = false };
    readonly Canvas marks = new() { IsHitTestVisible = false, ClipToBounds = true };
    readonly Grid imageLayer = new() { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, ClipToBounds = true };
    readonly ScrollViewer viewport = new() { MinHeight = 280, Height = 420, Background = Brush("#252B33"), HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    readonly CheckBox overlay = new() { Content = "绿色框显示条码位置", IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
    readonly Grid columns = new();
    readonly TabControl tabs = new();
    readonly StackPanel reading = new(), visual = new();
    readonly Expander setup;
    CancellationTokenSource? lifetime;
    CameraInspectionStatus? scope;
    CameraReadRound? current;
    CameraReadRound? observed;
    CameraFrameMetadata? displayedFrame;
    BeginCameraInspection? beginCommand;
    ArmCameraRead? armCommand;
    SendCameraRound? sendCommand;
    string position = "left", loadedFrame = "";
    bool active, busy, polling, fit = true, wasFenced, authoritative;
    long generation;
    double zoom = 1;
    Point? pan;
    double panX, panY;
    SupplyStationPositionViewModel Position => position == "left" ? vm.Left : vm.Right;
    ICameraInspection Inspection => inspection ?? throw new NotSupportedException("当前业务不提供相机隔离与原帧证据。");
    ICameraReadRounds Rounds => rounds ?? throw new NotSupportedException("当前业务不提供按轮扫码及手动请求。");
    public StationDebugControl(SupplyStationViewModel vm, Func<string, Task> settings, Func<string, Task> details)
    {
        this.vm = vm; inspection = vm.Application as ICameraInspection; rounds = vm.Application as ICameraReadRounds;
        evidence = new(vm, settings, details);
        Resources.MergedDictionaries.Add(new() { Source = new Uri("/Ipc.Application.ParcelFlow.Desktop;component/ParameterSettingsTheme.xaml", UriKind.Relative) });
        FontFamily = UiTypography.Interface; FontSize = UiTypography.Body; Background = Brushes.White; Foreground = Brush("#202833");
        var body = new StackPanel { Margin = new(20, 12, 20, 16) };
        tabs.Items.Add(new TabItem { Header = "扫码调试", Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } });
        tabs.Items.Add(new TabItem { Header = "历史证据", Content = evidence }); Content = tabs;
        var header = new StackPanel(); body.Children.Add(header);
        header.Children.Add(new TextBlock { Text = "调试", FontSize = UiTypography.PageTitle, FontWeight = UiTypography.Emphasis });
        header.Children.Add(state); header.Children.Add(feedback); header.Children.Add(Flow(left, right, camera));
        var safety = new StackPanel(); safety.Children.Add(Text("调试说明（至少5字）")); safety.Children.Add(reason); safety.Children.Add(scene);
        safety.Children.Add(Text("本轮读取期间隔离两位自动供包。扫码只填写请求，点击发送才调用已配置RCS；软件隔离不是安全急停。")); safety.Children.Add(end);
        setup = new() { Header = "隔离、操作说明与清场", IsExpanded = true, Content = safety, Margin = new(0, 8, 0, 12) }; reading.Children.Add(setup);
        columns.ColumnDefinitions.Add(new() { Width = new(4, GridUnitType.Star) }); columns.ColumnDefinitions.Add(new() { Width = new(6, GridUnitType.Star) });
        columns.RowDefinitions.Add(new() { Height = GridLength.Auto }); columns.RowDefinitions.Add(new() { Height = GridLength.Auto });
        columns.Children.Add(reading); columns.Children.Add(visual); Grid.SetColumn(visual, 1); visual.Margin = new(20, 0, 0, 0);
        body.Children.Add(columns);
        reading.Children.Add(Heading("本轮扫码")); reading.Children.Add(trigger); reading.Children.Add(Text("完整条码"));
        barcode.FontFamily = UiTypography.Code; barcode.FontSize = UiTypography.Barcode; barcode.MinHeight = 56; barcode.MaxHeight = 120; reading.Children.Add(barcode);
        reading.Children.Add(Heading("请求")); reading.Children.Add(Text("请求条码")); requestBarcode.FontFamily = UiTypography.Code; reading.Children.Add(requestBarcode);
        reading.Children.Add(Text("请求内容 · 与实际发送的冻结报文一致")); requestBody.FontFamily = UiTypography.Code; requestBody.Height = 210; requestBody.MaxHeight = 280; reading.Children.Add(requestBody);
        reading.Children.Add(Text("此按钮将发送本轮测试请求到当前配置的RCS。发送后的原请求和未知结果保留，不自动重发。")); reading.Children.Add(send);
        reading.Children.Add(Heading("请求结果")); reading.Children.Add(result); responseBody.FontFamily = UiTypography.Code; responseBody.Height = 150; reading.Children.Add(responseBody);
        visual.Children.Add(Heading("本轮读码照片")); imageLayer.Children.Add(photo); imageLayer.Children.Add(marks); viewport.Content = imageLayer; visual.Children.Add(viewport); visual.Children.Add(photoCaption);
        var adapt = Action("适应窗口"); var actual = Action("1:1"); var more = Action("放大"); var less = Action("缩小");
        visual.Children.Add(Flow(adapt, actual, more, less, overlay));
        adapt.Click += (_, _) => { fit = true; ApplyZoom(); }; actual.Click += (_, _) => { fit = false; zoom = 1 / VisualTreeHelper.GetDpi(viewport).DpiScaleX; ApplyZoom(); };
        more.Click += (_, _) => Scale(1.25); less.Click += (_, _) => Scale(.8); overlay.Checked += (_, _) => DrawMarks(); overlay.Unchecked += (_, _) => DrawMarks();
        viewport.SizeChanged += (_, _) => { if (fit) ApplyZoom(); };
        viewport.PreviewMouseWheel += (_, e) => { if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return; Scale(e.Delta > 0 ? 1.25 : .8); e.Handled = true; };
        viewport.PreviewMouseLeftButtonDown += (_, e) => { if (displayedFrame is null || e.OriginalSource is System.Windows.Controls.Primitives.Thumb) return; pan = e.GetPosition(viewport); panX = viewport.HorizontalOffset; panY = viewport.VerticalOffset; viewport.CaptureMouse(); e.Handled = true; };
        viewport.MouseMove += (_, e) => { if (pan is not { } point || !viewport.IsMouseCaptured) return; var now = e.GetPosition(viewport); viewport.ScrollToHorizontalOffset(panX + point.X - now.X); viewport.ScrollToVerticalOffset(panY + point.Y - now.Y); };
        viewport.MouseLeftButtonUp += (_, _) => { pan = null; viewport.ReleaseMouseCapture(); };
        SizeChanged += (_, _) => LayoutColumns();
        left.Click += (_, _) => SelectPosition("left"); right.Click += (_, _) => SelectPosition("right");
        trigger.Click += async (_, _) => await Run(TriggerAsync); send.Click += async (_, _) => await Run(SendAsync);
        end.Click += async (_, _) => await Run(async t =>
        {
            if (scene.IsChecked != true || scope?.SessionId is not { } id) throw new InvalidOperationException("请确认测试件已移除并核对两位原责任后结束隔离。");
            await vm.ServiceAsync(() => Inspection.EndCameraInspectionAsync(new(Wire.Id(), id, true, true, Reason()), t)); scene.IsChecked = false; beginCommand = null;
        });
        AutomationProperties.SetName(barcode, "本轮完整条码，可选择复制"); AutomationProperties.SetName(requestBody, "本轮冻结请求内容"); AutomationProperties.SetName(viewport, "同帧读码照片，绿色条码定位框");
        AutomationProperties.SetLiveSetting(state, AutomationLiveSetting.Polite); AutomationProperties.SetLiveSetting(feedback, AutomationLiveSetting.Assertive);
        timer.Tick += async (_, _) => await PollAsync();
        Loaded += async (_, _) => { active = true; lifetime = new(); timer.Start(); LayoutColumns(); await PollAsync(); };
        Unloaded += (_, _) => Deactivate();
    }
    string Reason() => reason.Text.Trim().Length >= 5 ? reason.Text : throw new InvalidOperationException("请填写至少5字的调试说明，并确认已停止真实投放。");
    async Task TriggerAsync(CancellationToken token)
    {
        if (!vm.Fresh || vm.Snapshot is not { } snapshot) throw new InvalidOperationException("请先恢复当前运行状态读取。");
        generation++; ClearRoundDisplay();
        if (scope is not { BusinessFenced: true, State: "Active", SingleReadRounds: true })
        {
            if (scope?.BusinessFenced == true) throw new InvalidOperationException("已有其他或中断的隔离会话，请核对并确认清场结束后再开始按轮扫码。");
            if (scene.IsChecked != true) throw new InvalidOperationException("请先确认停止真实投放并核对两位实体；只有隔离生效后才能放入测试件。");
            beginCommand ??= new(Wire.Id(), snapshot.RuntimeId, snapshot.Configuration.Revision, CameraInspectionMode.Isolated, Reason(), new(), true) { SingleReadRounds = true };
            var entered = await vm.ServiceAsync(() => Inspection.BeginCameraInspectionAsync(beginCommand, token));
            if (!active || token.IsCancellationRequested || !vm.IsManagementMode) return;
            scope = entered;
            beginCommand = null;
            // The begin receipt is followed by an authoritative status read, including the round mode.
            var confirmed = await vm.ServiceAsync(() => Inspection.CameraInspectionStatusAsync(token));
            if (!active || token.IsCancellationRequested || !vm.IsManagementMode) return;
            scope = confirmed;
        }
        if (scope?.SessionId is not { } session) throw new InvalidOperationException("未取得当前实机隔离身份。");
        beginCommand = null; // An authoritative active scope settles a lost begin receipt.
        armCommand ??= new(Wire.Id(), snapshot.RuntimeId, snapshot.Configuration.Revision, session, Position.ReaderId, observed?.Id ?? current?.Id);
        CameraReadRound value;
        try { value = await vm.ServiceAsync(() => Rounds.ArmCameraReadAsync(armCommand, token)); }
        catch (CameraReadRoundConflictException) { armCommand = null; throw; }
        if (!active || token.IsCancellationRequested || !vm.IsManagementMode) return;
        armCommand = null; sendCommand = null; observed = current = value; authoritative = true; setup.IsExpanded = false; feedback.Text = ""; await PublishRoundAsync(value, token);
    }
    async Task SendAsync(CancellationToken token)
    {
        if (!authoritative || !vm.Fresh || current is not { Id: { } id, RequestHash: { } hash } value || vm.Snapshot is not { } snapshot) throw new InvalidOperationException("请先恢复权威轮次读取，再核对本轮冻结请求。");
        if (sendCommand is null && (!value.CanSend || loadedFrame != value.Frame?.Id)) throw new InvalidOperationException("先取得本轮对应原图和可发送请求；守卫关闭时不可发送。");
        sendCommand ??= new(Wire.Id(), snapshot.RuntimeId, id, hash);
        var returned = await vm.ServiceAsync(() => Rounds.SendCameraRoundAsync(sendCommand, token));
        if (!active || token.IsCancellationRequested || !vm.IsManagementMode) return;
        current = returned; authoritative = true; await PublishRoundAsync(current, token);
    }
    async Task Run(Func<CancellationToken, Task> action)
    {
        if (!active || busy) return; busy = true; var ownership = lifetime; Buttons();
        try { await vm.RequireManagementModeAsync(); await action(lifetime!.Token); }
        catch (OperationCanceledException) { if (active && ReferenceEquals(ownership, lifetime)) { authoritative = false; feedback.Text = "等待已取消；请核对本轮和原隔离，不能推断请求未发出或已恢复生产。"; } }
        catch (Exception error) { if (active && ReferenceEquals(ownership, lifetime)) { authoritative = false; feedback.Text = error.Message; } }
        finally { busy = false; if (active) { Buttons(); await PollAsync(); } }
    }
    public async Task PollAsync()
    {
        if (!active || busy || polling || tabs.SelectedIndex == 1 || !vm.IsManagementMode || rounds is null || inspection is null) return;
        polling = true; var version = generation; var token = lifetime!.Token;
        try
        {
            var isolation = await vm.ServiceAsync(() => Inspection.CameraInspectionStatusAsync(token));
            var value = await vm.ServiceAsync(() => Rounds.ReadCameraReadRoundAsync(token));
            if (!active || busy || version != generation || !vm.IsManagementMode) return;
            scope = isolation; observed = value; authoritative = true;
            if (current?.Frame is not null && current.Id is not null && value.Id != current.Id)
            { state.Text = "其他窗口已开始另一轮；本窗保留原轮次照片和请求。点击再次扫码明确开始本窗新一轮。"; Buttons(); return; }
            current = value; await PublishRoundAsync(value, token);
        }
        catch (OperationCanceledException) { if (active && version == generation) authoritative = false; }
        catch (Exception error) { if (active && version == generation) { authoritative = false; state.Text = "调试状态读取失败；原隔离和原请求保留。"; feedback.Text = error.Message; send.IsEnabled = false; } }
        finally { polling = false; if (active && version == generation) Buttons(); }
    }
    async Task PublishRoundAsync(CameraReadRound value, CancellationToken token)
    {
        if (!active || !vm.IsManagementMode || token.IsCancellationRequested) return;
        camera.Text = Position.Heading + " · " + Position.ReaderId;
        var sameCamera = value.DeviceId == Position.ReaderId;
        state.Text = scope?.BusinessFenced == true ? (scope.State == "Active" ? "实机隔离已生效 · " : "隔离保持 · ") + value.Detail : "点击触发扫码开始本轮读取；不会自动发送请求。";
        if (!sameCamera) { ClearRoundDisplay(); Buttons(); return; }
        SetText(barcode, value.Barcode ?? ""); SetText(requestBarcode, value.Frame is null ? "" : value.Barcode ?? "");
        SetText(requestBody, value.RequestBody ?? ""); SetText(responseBody, value.Result ?? "");
        result.Text = value.OperationId is null ? "本轮尚未准备可发送请求。" : "原作业：" + value.OperationId + "\n请求编号：" + value.RequestId + "\n目标：" + value.Target;
        if (value.Frame is { ImageAvailable: true } frame && loadedFrame != frame.Id)
        {
            var version = generation; var raw = await vm.ServiceAsync(() => Inspection.ReadCameraOriginalAsync(frame.Id, token));
            if (!active || version != generation || token.IsCancellationRequested || current?.Id != value.Id || !vm.IsManagementMode) return;
            if (raw is null) { ClearPhoto(); photoCaption.Text = "本轮原图已缺失或轮替，不使用其他帧补配；可在历史证据核对。"; return; }
            if (raw.Metadata.Id != frame.Id || raw.Metadata.ObservationId != frame.ObservationId || raw.Gray8.LongLength != (long)frame.Width * frame.Height)
                throw new InvalidDataException("原图与本轮身份不一致，拒绝显示。");
            displayedFrame = raw.Metadata; loadedFrame = frame.Id;
            var bitmap = BitmapSource.Create(frame.Width, frame.Height, 96, 96, PixelFormats.Gray8, null, raw.Gray8, frame.Width); bitmap.Freeze(); photo.Source = bitmap;
            photo.Width = imageLayer.Width = marks.Width = frame.Width; photo.Height = imageLayer.Height = marks.Height = frame.Height;
            fit = true; ApplyZoom(); DrawMarks(); photoCaption.Text = "已锁定本轮照片 · 帧 " + frame.FrameNumber + " · " + frame.CapturedAt.ToLocalTime().ToString("HH:mm:ss.fff") + "\n下一件不会覆盖。";
        }
        if (value.Frame is null) ClearPhoto(); Buttons();
    }
    void ClearPhoto() { photo.Source = null; marks.Children.Clear(); displayedFrame = null; loadedFrame = ""; imageLayer.Width = imageLayer.Height = 0; }
    void ClearRoundDisplay() { ClearPhoto(); barcode.Clear(); requestBarcode.Clear(); requestBody.Clear(); responseBody.Clear(); result.Text = "本轮尚未发送请求。"; photoCaption.Text = "等待本轮对应读码照片…"; }
    void Buttons()
    {
        var fenced = scope?.BusinessFenced == true;
        if (fenced && !wasFenced) scene.IsChecked = false;
        wasFenced = fenced;
        scene.Content = fenced ? "已移除两位测试件，并核对原实体与原请求责任" : "已停止真实投放，并核对两位当前包裹";
        var waiting = observed?.State is "Preparing" or "Reading" or "Sealing" or "Sending";
        trigger.Content = armCommand is not null || beginCommand is not null ? "核对触发" : current?.State is "Preparing" or "Reading" or "Sealing" ? "扫码中…" : current?.Id is null ? "触发扫码" : "再次扫码";
        trigger.IsEnabled = active && !busy && !waiting && vm.IsManagementMode;
        send.Content = sendCommand is null ? "发送请求" : "核对发送结果";
        send.IsEnabled = active && !busy && authoritative && vm.Fresh && vm.IsManagementMode && current?.Id == observed?.Id && current?.DeviceId == Position.ReaderId &&
            (sendCommand is not null || current is { CanSend: true, Frame: { } frame } && loadedFrame == frame.Id);
        end.IsEnabled = active && !busy && scope?.BusinessFenced == true && observed?.State != "Sending";
        left.IsEnabled = right.IsEnabled = !busy; left.Background = position == "left" ? Brush("#E8F1FF") : Brushes.White; right.Background = position == "right" ? Brush("#E8F1FF") : Brushes.White;
    }
    void LayoutColumns()
    {
        var narrow = ActualWidth < 1000; columns.ColumnDefinitions[0].Width = narrow ? new(1, GridUnitType.Star) : new(4, GridUnitType.Star);
        columns.ColumnDefinitions[1].Width = narrow ? new(0) : new(6, GridUnitType.Star); Grid.SetColumn(visual, narrow ? 0 : 1); Grid.SetRow(visual, narrow ? 1 : 0); visual.Margin = narrow ? new(0, 20, 0, 0) : new(20, 0, 0, 0);
        viewport.Height = narrow ? 320 : 420;
    }
    void Scale(double factor) { fit = false; zoom = Math.Clamp(zoom * factor, .02, 16); ApplyZoom(); }
    void ApplyZoom()
    {
        if (displayedFrame is not { } frame) return;
        if (fit && viewport.ActualWidth > 0) zoom = Math.Clamp(Math.Min(Math.Max(1, viewport.ActualWidth - 20) / frame.Width, Math.Max(1, viewport.ActualHeight - 20) / frame.Height), .02, 16);
        imageLayer.LayoutTransform = new ScaleTransform(zoom, zoom); DrawMarks();
    }
    void DrawMarks()
    {
        marks.Children.Clear(); if (overlay.IsChecked != true || displayedFrame is null) return;
        foreach (var symbol in displayedFrame.Symbols.Where(s => s.Text == current?.Barcode && s.Corners.Length == 4))
        {
            var x = symbol.Corners.Min(p => p.X); var y = symbol.Corners.Min(p => p.Y);
            var box = new Rectangle { Width = Math.Max(1, symbol.Corners.Max(p => p.X) - x), Height = Math.Max(1, symbol.Corners.Max(p => p.Y) - y), Stroke = Brush("#28D76F"), StrokeThickness = 2 / zoom };
            Canvas.SetLeft(box, x); Canvas.SetTop(box, y); marks.Children.Add(box);
        }
    }
    public void SelectPosition(string value) { position = value == "right" ? "right" : "left"; generation++; authoritative = false; ClearRoundDisplay(); camera.Text = Position.Heading + " · " + Position.ReaderId; evidence.SelectPosition(position); Buttons(); }
    public void Revoke() { Deactivate(); evidence.Revoke(); reason.Clear(); scene.IsChecked = false; current = observed = null; scope = null; beginCommand = null; armCommand = null; sendCommand = null; }
    void Deactivate() { active = false; authoritative = false; generation++; timer.Stop(); lifetime?.Cancel(); lifetime?.Dispose(); lifetime = null; ClearRoundDisplay(); }
    static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color));
    static TextBlock Text(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = UiTypography.Label, Margin = new(0, 4, 0, 6) };
    static TextBlock Heading(string text) => new() { Text = text, FontSize = UiTypography.Group, FontWeight = UiTypography.Emphasis, Margin = new(0, 12, 0, 8) };
    static TextBox Value() => new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 44, Margin = new(0, 0, 0, 8), Padding = new(8) };
    static void SetText(TextBox box, string value) { if (box.Text != value) box.Text = value; }
    static Button Action(string text) => new() { Content = text, MinHeight = 44, Padding = new(12, 6, 12, 6), Margin = new(0, 0, 8, 6), HorizontalAlignment = HorizontalAlignment.Left };
    static WrapPanel Flow(params UIElement[] controls) { var panel = new WrapPanel(); foreach (var control in controls) panel.Children.Add(control); return panel; }
}

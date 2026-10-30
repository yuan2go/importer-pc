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
    readonly StationCameraPreviewControl livePreview;
    readonly Grid jsonColumns = new();
    readonly Border liveCard, resultCard;
    readonly TextBlock readStatus = Text("尚未开始扫码"), photoState = Text("等待扫码"), fillStatus = Text("");
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    readonly Button left = Action("左相机"), right = Action("右相机"), trigger = Action("触发扫码"), prepare = Action("准备请求"), send = Action("发送请求"), end = Action("确认清场并结束调试");
    readonly TextBlock state = Text("点击触发扫码开始本轮读取"), feedback = Text(""), photoCaption = Text("本轮读码照片"), result = Text("本轮尚未发送请求。"), camera = Text("左相机");
    readonly TextBox barcode = Value(), requestBarcode = Value(), requestBody = Value(), responseBody = Value();
    readonly CheckBox scene = new() { Content = "已停止真实投放，并核对两位当前包裹", Margin = new(0, 8, 0, 8) };
    readonly Image photo = new() { Stretch = Stretch.Fill, IsHitTestVisible = false };
    readonly Canvas marks = new() { IsHitTestVisible = false, ClipToBounds = true };
    readonly Grid imageLayer = new() { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, ClipToBounds = true };
    readonly ScrollViewer viewport = new() { MinHeight = 180, Height = 206, Background = Brush("#252B33"), HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    readonly CheckBox overlay = new() { Content = "绿色框显示条码位置", IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock imageHint = new() { Text = "等待本轮扫码照片\n点击“触发扫码”开始读取。", Foreground = Brush("#E0E5EB"),
        TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, MaxWidth = 320, Margin = new(24),
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
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
    PrepareManualRcsRequest? prepareCommand;
    readonly Dictionary<string, string> manualDrafts = [];
    bool updatingRequest, requestDirty;
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
        livePreview = new(vm); livePreview.SelectDevice(Position.ReaderId);
        Resources.MergedDictionaries.Add(new() { Source = new Uri("/Ipc.Application.ParcelFlow.Desktop;component/ParameterSettingsTheme.xaml", UriKind.Relative) });
        FontFamily = UiTypography.Interface; FontSize = UiTypography.Body; FontWeight = UiTypography.Regular;
        Background = Brush("#F3F5F8"); Foreground = Brush("#18283D");
        var body = new StackPanel { Margin = new(20, 16, 20, 16) };
        tabs.Items.Add(new TabItem { Header = "扫码调试", Content = new ScrollViewer { Content = body, Background = Background,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } });
        tabs.Items.Add(new TabItem { Header = "历史证据", Content = evidence }); Content = tabs;
        var header = new DockPanel { LastChildFill = true };
        trigger.Background = Brush("#245FD3"); trigger.Foreground = Brushes.White; trigger.BorderBrush = trigger.Background;
        readStatus.Foreground = Brush("#9A6511"); readStatus.VerticalAlignment = VerticalAlignment.Center;
        var scanActions = Flow(readStatus, trigger); DockPanel.SetDock(scanActions, Dock.Right); header.Children.Add(scanActions);
        var title = new TextBlock { Text = "调试", FontSize = UiTypography.PageTitle, FontWeight = UiTypography.Emphasis,
            VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 20, 6) };
        header.Children.Add(Flow(title, left, right)); body.Children.Add(header);
        camera.Foreground = Brush("#68788E"); state.Foreground = Brush("#68788E");
        body.Children.Add(camera); body.Children.Add(state); body.Children.Add(feedback);
        var safety = new StackPanel(); safety.Children.Add(scene);
        safety.Children.Add(Text("本轮读取期间隔离两位自动供包。扫码只填写请求，点击发送才调用已配置RCS；软件隔离不是安全急停。")); safety.Children.Add(end);
        setup = new() { Header = "隔离确认与结束调试", IsExpanded = false, Content = safety, Margin = new(0, 4, 0, 12) }; body.Children.Add(setup);
        columns.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); columns.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        columns.RowDefinitions.Add(new() { Height = GridLength.Auto }); columns.RowDefinitions.Add(new() { Height = GridLength.Auto });
        liveCard = Panel(reading); resultCard = Panel(visual); resultCard.Margin = new(14, 0, 0, 0);
        columns.Children.Add(liveCard); columns.Children.Add(resultCard); Grid.SetColumn(resultCard, 1); body.Children.Add(columns);
        reading.Children.Add(Heading("实时画面")); reading.Children.Add(livePreview);
        var resultHeading = new DockPanel(); DockPanel.SetDock(photoState, Dock.Right); resultHeading.Children.Add(photoState); resultHeading.Children.Add(Heading("本次读码")); visual.Children.Add(resultHeading);
        barcode.FontFamily = UiTypography.Code; barcode.FontSize = UiTypography.Barcode; barcode.MinHeight = 48; barcode.MaxHeight = 96;
        barcode.Background = Brush("#F3FBF7"); barcode.BorderBrush = Brush("#C7E4D8"); barcode.Margin = new(0, 0, 0, 8);
        barcode.ToolTip = "本轮完整条码，可选择复制"; visual.Children.Add(barcode);
        imageLayer.Children.Add(photo); imageLayer.Children.Add(marks); viewport.Content = imageLayer;
        var photoArea = new Grid(); photoArea.Children.Add(viewport); photoArea.Children.Add(imageHint); visual.Children.Add(photoArea); visual.Children.Add(photoCaption);
        var adapt = Action("适应窗口"); var actual = Action("1:1"); var more = Action("放大"); var less = Action("缩小");
        visual.Children.Add(new Expander { Header = "照片缩放与条码标注", Content = Flow(adapt, actual, more, less, overlay), Margin = new(0, 4, 0, 0) });
        var requestPanel = new StackPanel(); var requestCard = Panel(requestPanel); requestCard.Margin = new(0, 14, 0, 0); body.Children.Add(requestCard);
        var requestHeader = new DockPanel();
        requestBarcode.FontFamily = UiTypography.Code; requestBarcode.Width = 220; requestBarcode.MaxHeight = 64; requestBarcode.Margin = new(8, 0, 10, 6);
        requestBarcode.IsReadOnly = false; requestBarcode.MaxLength = 4096;
        requestBarcode.ToolTip = "可手工输入完整条码；准备请求不要求配置或连接相机。";
        send.Foreground = Brush("#245FD3"); send.BorderBrush = Brush("#BFCFED");
        var entry = Flow(Text("请求条码"), requestBarcode, prepare, send); DockPanel.SetDock(entry, Dock.Right); requestHeader.Children.Add(entry); requestHeader.Children.Add(Heading("请求调试")); requestPanel.Children.Add(requestHeader);
        fillStatus.Foreground = Brush("#17805C"); requestPanel.Children.Add(fillStatus);
        requestPanel.Children.Add(Text("扫码自动填入，或手工输入条码后准备请求；不要求配置或连接相机。点击发送才调用RCS。"));
        jsonColumns.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); jsonColumns.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        jsonColumns.RowDefinitions.Add(new() { Height = GridLength.Auto }); jsonColumns.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var requestJson = new StackPanel(); var responseJson = new StackPanel { Margin = new(14, 0, 0, 0) };
        requestJson.Children.Add(Text("请求 · 实际发送的冻结报文")); requestBody.FontFamily = UiTypography.Code; requestBody.Height = 150; requestBody.Background = Brush("#F7F9FC"); requestJson.Children.Add(requestBody);
        responseJson.Children.Add(result); responseBody.FontFamily = UiTypography.Code; responseBody.Height = 150; responseBody.Background = Brush("#F7F9FC"); responseJson.Children.Add(responseBody);
        jsonColumns.Children.Add(requestJson); jsonColumns.Children.Add(responseJson); Grid.SetColumn(responseJson, 1); requestPanel.Children.Add(jsonColumns);
        body.Children.Add(Text("调试会话 · 单次扫码 · 实时预览与本轮结果独立"));
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
        prepare.Click += async (_, _) => await Run(PrepareManualAsync);
        requestBarcode.TextChanged += (_, _) =>
        {
            if (updatingRequest) return;
            requestDirty = true; manualDrafts[position] = requestBarcode.Text; Buttons();
        };
        end.Click += async (_, _) => await Run(async t =>
        {
            if (scene.IsChecked != true || scope?.SessionId is not { } id) throw new InvalidOperationException("请确认测试件已移除并核对两位原责任后结束隔离。");
            await vm.ServiceAsync(() => Inspection.EndCameraInspectionAsync(new(Wire.Id(), id, true, true, "确认清场并结束调试"), t)); scene.IsChecked = false; beginCommand = null;
        });
        AutomationProperties.SetName(barcode, "本轮完整条码，可选择复制"); AutomationProperties.SetName(requestBody, "本轮冻结请求内容"); AutomationProperties.SetName(viewport, "同帧读码照片，绿色条码定位框");
        AutomationProperties.SetLiveSetting(state, AutomationLiveSetting.Polite); AutomationProperties.SetLiveSetting(feedback, AutomationLiveSetting.Assertive);
        timer.Tick += async (_, _) => await PollAsync();
        Loaded += async (_, _) => { active = true; lifetime = new(); timer.Start(); LayoutColumns(); await PollAsync(); };
        Unloaded += (_, _) => Deactivate();
    }
    async Task PrepareManualAsync(CancellationToken token)
    {
        if (!vm.Fresh || vm.Snapshot is not { } snapshot) throw new InvalidOperationException("请先恢复当前运行状态读取。");
        prepareCommand ??= new(Wire.Id(), snapshot.RuntimeId, snapshot.Configuration.Revision, position, requestBarcode.Text, observed?.Id);
        CameraReadRound value;
        try { value = await vm.ServiceAsync(() => Rounds.PrepareManualRcsRequestAsync(prepareCommand, token)); }
        catch (CameraReadRoundConflictException) { prepareCommand = null; throw; }
        catch (ArgumentException) { prepareCommand = null; throw; }
        if (!active || token.IsCancellationRequested || !vm.IsManagementMode) return;
        if (value.Id != prepareCommand.CommandId || !value.ManualRequest) throw new InvalidOperationException("原准备结果尚未确认，请核对原轮次；不重复创建请求。");
        requestDirty = requestBarcode.Text != prepareCommand.Barcode;
        if (!requestDirty) manualDrafts.Remove(position);
        prepareCommand = null; sendCommand = null;
        current = observed = value; authoritative = true; feedback.Text = ""; await PublishRoundAsync(value, token);
    }
    async Task TriggerAsync(CancellationToken token)
    {
        if (!vm.Fresh || vm.Snapshot is not { } snapshot) throw new InvalidOperationException("请先恢复当前运行状态读取。");
        generation++; ClearRoundDisplay();
        if (scope is not { BusinessFenced: true, State: "Active", SingleReadRounds: true })
        {
            if (scope?.BusinessFenced == true) throw new InvalidOperationException("已有其他或中断的隔离会话，请核对并确认清场结束后再开始按轮扫码。");
            if (scene.IsChecked != true) { setup.IsExpanded = true; setup.BringIntoView(); throw new InvalidOperationException("请先确认停止真实投放并核对两位实体；只有隔离生效后才能放入测试件。"); }
            beginCommand ??= new(Wire.Id(), snapshot.RuntimeId, snapshot.Configuration.Revision, CameraInspectionMode.Isolated, "单次扫码调试", new(), true) { SingleReadRounds = true };
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
        armCommand = null; sendCommand = null; requestDirty = false; manualDrafts.Remove(position); observed = current = value; authoritative = true; setup.IsExpanded = false; feedback.Text = ""; await PublishRoundAsync(value, token);
    }
    async Task SendAsync(CancellationToken token)
    {
        if (!authoritative || !vm.Fresh || current is not { Id: { } id, RequestHash: { } hash } value || vm.Snapshot is not { } snapshot) throw new InvalidOperationException("请先恢复权威轮次读取，再核对本轮冻结请求。");
        if (sendCommand is null && (!value.CanSend || requestDirty || !value.ManualRequest && loadedFrame != value.Frame?.Id)) throw new InvalidOperationException("请先准备当前条码的请求；扫码请求还须核对本轮原图。");
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
        camera.Text = Position.Heading + " · " + Position.DeviceLabel + "    " + vm.RcsServiceStatus;
        var sameCamera = value.DeviceId == Position.ReaderId;
        state.ToolTip = value.Detail;
        var detail = !sameCamera ? "所选相机尚未触发扫码。" : value.State == "Locked" && value.CanSend ? "结果已锁定，等待手动发送或再次扫码。" : value.Detail;
        state.Text = value.ManualRequest ? value.Detail : scope?.BusinessFenced == true ? (scope.State == "Active" ? "实机隔离已生效 · " : "隔离保持 · ") + detail : "点击触发扫码开始本轮读取；不会自动发送请求。";
        if (!sameCamera) { ClearRoundDisplay(); Buttons(); return; }
        SetText(barcode, value.Barcode ?? "");
        if (!requestDirty) { updatingRequest = true; try { SetText(requestBarcode, value.Barcode ?? ""); } finally { updatingRequest = false; } }
        SetText(requestBody, value.RequestBody ?? ""); SetText(responseBody, value.Result ?? "");
        result.Text = value.OperationId is null ? "响应 · 尚未准备请求" : value.State == "Sending" ? "响应 · 正在发送" : value.CanSend ? "响应 · 等待手动发送" : "响应 · 原请求结果";
        result.ToolTip = value.OperationId is null ? null : "原作业：" + value.OperationId + "\n请求编号：" + value.RequestId + "\n目标：" + value.Target;
        send.ToolTip = value.Target is null ? "等待本轮请求准备完成" : "发送到当前RCS：" + value.Target;
        if (value.Frame is { ImageAvailable: true } frame && loadedFrame != frame.Id)
        {
            var version = generation; var raw = await vm.ServiceAsync(() => Inspection.ReadCameraOriginalAsync(frame.Id, token));
            if (!active || version != generation || token.IsCancellationRequested || current?.Id != value.Id || !vm.IsManagementMode) return;
            if (raw is null) { ClearPhoto(); photoCaption.Text = "本轮原图已缺失或轮替，不使用其他帧补配；可在历史证据核对。"; return; }
            if (raw.Metadata.Id != frame.Id || raw.Metadata.ObservationId != frame.ObservationId || raw.Gray8.LongLength != (long)frame.Width * frame.Height)
                throw new InvalidDataException("原图与本轮身份不一致，拒绝显示。");
            displayedFrame = raw.Metadata; loadedFrame = frame.Id;
            var bitmap = BitmapSource.Create(frame.Width, frame.Height, 96, 96, PixelFormats.Gray8, null, raw.Gray8, frame.Width); bitmap.Freeze(); photo.Source = bitmap; imageHint.Visibility = Visibility.Collapsed;
            photo.Width = imageLayer.Width = marks.Width = frame.Width; photo.Height = imageLayer.Height = marks.Height = frame.Height;
            fit = true; ApplyZoom(); DrawMarks(); photoCaption.Text = "已锁定本轮照片 · 帧 " + frame.FrameNumber + " · " + frame.CapturedAt.ToLocalTime().ToString("HH:mm:ss.fff") + "\n下一件不会覆盖。";
        }
        if (value.Frame is null) ClearPhoto(); Buttons();
    }
    void ClearPhoto() { photo.Source = null; imageHint.Visibility = Visibility.Visible; marks.Children.Clear(); displayedFrame = null; loadedFrame = ""; imageLayer.Width = imageLayer.Height = 0; }
    void ClearRoundDisplay() { ClearPhoto(); barcode.Clear(); updatingRequest = true; try { SetText(requestBarcode, manualDrafts.GetValueOrDefault(position, "")); } finally { updatingRequest = false; } requestDirty = manualDrafts.ContainsKey(position); requestBody.Clear(); responseBody.Clear(); result.ToolTip = send.ToolTip = state.ToolTip = null; result.Text = "本轮尚未发送请求。"; photoCaption.Text = "等待本轮对应读码照片…"; imageHint.Text = "等待本轮扫码照片\n点击“触发扫码”开始读取。"; }
    void Buttons()
    {
        if (photo.Source is null && current?.DeviceId == Position.ReaderId)
            imageHint.Text = current.ManualRequest ? "本次为手工输入请求，无扫码照片。" : current.State is "Preparing" or "Reading" or "Sealing" ? "正在读取本轮条码…\n读到单码后自动锁定照片。" :
                current.Frame is not null ? "本轮照片不可用\n请核对原图留存状态。" : "等待本轮扫码照片\n点击“触发扫码”开始读取。";
        readStatus.Text = current?.DeviceId != Position.ReaderId ? "尚未开始扫码" : current?.ManualRequest == true ? "人工请求" : current?.State switch
        { "Reading" or "Preparing" or "Sealing" => "正在读取", "Locked" => "读码已暂停", "Sending" => "正在发送", "Faulted" => "读取异常", _ => "尚未开始扫码" };
        photoState.Text = current?.ManualRequest == true ? "人工请求，无扫码照片" : displayedFrame is null ? "等待扫码" : "结果已锁定";
        if (current?.ManualRequest == true) photoCaption.Text = "本次为手工输入请求。";
        fillStatus.Text = requestDirty ? "输入已更改，请准备当前条码的请求。" : current?.DeviceId == Position.ReaderId && current.RequestBody is not null ?
            (current.ManualRequest ? "来自手工输入 · " : "来自本轮扫码 · ") + Position.Heading + " · 已填入请求" : "可扫码或手工输入条码。";
        feedback.Visibility = string.IsNullOrWhiteSpace(feedback.Text) ? Visibility.Collapsed : Visibility.Visible;
        var fenced = scope?.BusinessFenced == true;
        if (fenced && !wasFenced) scene.IsChecked = false;
        wasFenced = fenced;
        scene.Content = fenced ? "已移除两位测试件，并核对原实体与原请求责任" : "已停止真实投放，并核对两位当前包裹";
        var waiting = observed?.State is "Preparing" or "Reading" or "Sealing" or "Sending";
        trigger.Content = armCommand is not null || beginCommand is not null ? "核对触发" : current?.State is "Preparing" or "Reading" or "Sealing" ? "扫码中…" : current?.Id is null || current.ManualRequest ? "触发扫码" : "再次扫码";
        trigger.IsEnabled = active && !busy && !waiting && vm.IsManagementMode;
        prepare.Content = prepareCommand is null ? "准备请求" : "核对准备结果";
        prepare.IsEnabled = active && !busy && !waiting && vm.Fresh && vm.IsManagementMode && !string.IsNullOrWhiteSpace(requestBarcode.Text);
        send.Content = sendCommand is null ? "发送请求" : "核对发送结果";
        send.IsEnabled = active && !busy && authoritative && vm.Fresh && vm.IsManagementMode && current?.Id == observed?.Id && current?.DeviceId == Position.ReaderId &&
            (sendCommand is not null || !requestDirty && current is { CanSend: true } && (current.ManualRequest || current.Frame is { } frame && loadedFrame == frame.Id));
        end.IsEnabled = active && !busy && scope?.BusinessFenced == true && observed?.State != "Sending";
        left.IsEnabled = right.IsEnabled = !busy; left.Background = position == "left" ? Brush("#E8F1FF") : Brushes.White; right.Background = position == "right" ? Brush("#E8F1FF") : Brushes.White;
    }
    void LayoutColumns()
    {
        var narrow = ActualWidth < 720;
        columns.ColumnDefinitions[1].Width = narrow ? new(0) : new(1, GridUnitType.Star);
        Grid.SetColumn(resultCard, narrow ? 0 : 1); Grid.SetRow(resultCard, narrow ? 1 : 0); resultCard.Margin = narrow ? new(0, 14, 0, 0) : new(14, 0, 0, 0);
        jsonColumns.ColumnDefinitions[1].Width = narrow ? new(0) : new(1, GridUnitType.Star);
        var response = (FrameworkElement)jsonColumns.Children[1]; Grid.SetColumn(response, narrow ? 0 : 1); Grid.SetRow(response, narrow ? 1 : 0);
        response.Margin = narrow ? new(0, 10, 0, 0) : new(14, 0, 0, 0);
        viewport.Height = narrow ? 240 : 206;
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
    public void SelectPosition(string value) { position = value == "right" ? "right" : "left"; generation++; authoritative = false; ClearRoundDisplay(); livePreview.SelectDevice(Position.ReaderId); camera.Text = Position.Heading + " · " + Position.DeviceLabel + "    " + vm.RcsServiceStatus; evidence.SelectPosition(position); Buttons(); }
    public void Revoke() { Deactivate(); evidence.Revoke(); manualDrafts.Clear(); prepareCommand = null; scene.IsChecked = false; current = observed = null; scope = null; beginCommand = null; armCommand = null; sendCommand = null; }
    void Deactivate() { active = false; authoritative = false; generation++; timer.Stop(); livePreview.Revoke(); lifetime?.Cancel(); lifetime?.Dispose(); lifetime = null; ClearRoundDisplay(); }
    static Border Panel(UIElement child) => new() { Child = child, Background = Brushes.White, BorderBrush = Brush("#DFE5ED"), BorderThickness = new(1), CornerRadius = new(7), Padding = new(12, 8, 12, 10) };
    static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color));
    static TextBlock Text(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = UiTypography.Label, Margin = new(0, 4, 0, 6) };
    static TextBlock Heading(string text) => new() { Text = text, FontSize = UiTypography.Group, FontWeight = UiTypography.Emphasis, Margin = new(0, 4, 0, 8) };
    static TextBox Value() => new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 44, Margin = new(0, 0, 0, 8), Padding = new(8) };
    static void SetText(TextBox box, string value) { if (box.Text != value) box.Text = value; }
    static Button Action(string text) => new() { Content = text, MinHeight = 44, Padding = new(12, 6, 12, 6), Margin = new(0, 0, 8, 6), HorizontalAlignment = HorizontalAlignment.Left };
    static WrapPanel Flow(params UIElement[] controls) { var panel = new WrapPanel(); foreach (var control in controls) panel.Children.Add(control); return panel; }
}

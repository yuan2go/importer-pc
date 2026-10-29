using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Platform;
using Microsoft.Win32;

namespace Ipc.Application.Sportmaster.Desktop;

// Bounded visual pages over Application-owned, persistent cursor queries. No
// device connection, source mode, request or diagnostic database is owned by UI.
internal sealed class StationDebugControl : UserControl
{
    readonly SupplyStationViewModel vm;
    readonly Func<string, Task> settings, details;
    readonly ICameraInspection? api;
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(750) };
    readonly TextBlock state = Label("正在读取调试状态"), feedback = Label(""), coverage = Label(""), caption = Label("请选择记录查看对应原图。"), result = Label(""), identity = Label("");
    readonly Border banner = new() { Padding = new(12, 8, 12, 8), CornerRadius = new(6) };
    readonly Button left = Button("左相机"), right = Button("右相机"), observe = Button("开始生产观察"), isolate = Button("进入实机隔离"), stop = Button("停止关联采集"), resume = Button("确认清场并结束隔离");
    readonly Button older = Button("前一页"), newer = Button("后一页"), latest = Button("回到最新"), record = Button("查看原作业"), saveImage = Button("导出本帧原图 PNG"), saveSession = Button("导出会话证据"), purge = Button("清理已结束会话");
    readonly TextBox reason = new() { MaxLength = 1024, MinWidth = 200 }, duration = Number("10"), imageBudget = Number("1024"), recordBudget = Number("200000"), sessionId = new() { MaxLength = 64, MinWidth = 220 };
    readonly CheckBox cleared = new() { Content = "已停止真实投放、移除两位测试件并核对当前实体与原责任", Margin = new(0, 8, 0, 8) }, overlay = new() { Content = "显示识别位置", IsChecked = true }, follow = new() { Content = "跟随最新记录", IsChecked = true };
    readonly ComboBox sessions = new() { MinWidth = 220, MaxWidth = 520, DisplayMemberPath = nameof(CameraInspectionSession.Label) }, symbols = new() { MinWidth = 160, DisplayMemberPath = nameof(RecognitionSymbol.Text) };
    readonly ObservableCollection<Row> businessRows = [], frameRows = [];
    readonly ListBox business = List(), frames = List();
    readonly TabControl tabs = new();
    readonly TextBox inputEvidence = ReadOnly(), requestEvidence = ReadOnly(), sample = new() { AcceptsReturn = true, Height = 72, MaxLength = 16384, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    readonly TextBlock sampleResult = Label("每行一个候选；仅测试当前运行规则，不保存、不产生业务请求。");
    readonly Expander simulation = new() { Header = "模拟输入（仅Simulation）", Margin = new(0, 8, 0, 8), Visibility = Visibility.Collapsed };
    readonly TextBox simulationCode = new() { MaxLength = 256, MinWidth = 200 };
    readonly Button emit = Button("提交模拟读码"), pause = Button("暂停画面");
    readonly Image image = new() { Stretch = Stretch.Fill };
    readonly Canvas marks = new() { IsHitTestVisible = false, ClipToBounds = true };
    readonly Grid imageLayer = new() { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, ClipToBounds = true };
    readonly ScrollViewer viewport = new() { Height = 270, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = ColorBrush("#18212D") };
    readonly TextBlock zoomText = Label("适应窗口");
    readonly Expander setup;
    CancellationTokenSource? lifetime, selectionCancellation;
    CameraInspectionStatus? status;
    CameraOriginalImage? original;
    CameraFrameMetadata? selectedFrame;
    Row? selection;
    string position = "left", viewedSession = "", lastSessionState = "";
    bool active, polling, busy, selecting, fit = true, businessInitialized, framesInitialized, historical;
    long generation, selectionGeneration, commandGeneration, businessCursor, frameCursor, detailRevision = -1;
    int watchOffset;
    long businessOlder = long.MaxValue, businessNewer;
    double scale = 1;
    SupplyStationPositionViewModel Position => position == "left" ? vm.Left : vm.Right;
    ListBox CurrentList => tabs.SelectedIndex == 0 ? business : frames;
    ObservableCollection<Row> CurrentRows => tabs.SelectedIndex == 0 ? businessRows : frameRows;
    sealed class Row : INotifyPropertyChanged
    {
        public CameraBusinessRow? Business { get; init; }
        public CameraFrameMetadata? Frame { get; init; }
        public long Sequence => Business?.Sequence ?? Frame!.Sequence;
        public string Key => Business?.ObservationId ?? Frame!.Id;
        public string? LinkedOperation { get; set; }
        public string? OperationId => LinkedOperation ?? Business?.OperationId ?? Frame?.OperationId;
        CameraOperationStatus? operation;
        public CameraOperationStatus? Operation { get => operation; set { operation = value; PropertyChanged?.Invoke(this, new(nameof(Label))); } }
        public string Label => (Business?.ObservedAt ?? Frame!.CapturedAt).ToLocalTime().ToString("HH:mm:ss.fff") + "  ·  " +
            Short(Business?.SelectedCode ?? Business?.Candidates.FirstOrDefault() ?? Frame?.Symbols.FirstOrDefault()?.Text ?? "未读到码", 80) + "  ·  " +
            (Business is { } b ? InputText(b.Outcome) : Frame?.IsolatedSessionId is null ? "关联帧" : "隔离测试") +
            (Operation is { } op ? "  ·  " + ResultText(op) : OperationId is null ? "" : "  ·  等待原结果刷新");
        public event PropertyChangedEventHandler? PropertyChanged;
    }
    public StationDebugControl(SupplyStationViewModel vm, Func<string, Task> settings, Func<string, Task> details)
    {
        this.vm = vm; this.settings = settings; this.details = details; api = vm.Application as ICameraInspection;
        Resources.MergedDictionaries.Add(new() { Source = new Uri("/Ipc.Application.ParcelFlow.Desktop;component/ParameterSettingsTheme.xaml", UriKind.Relative) });
        FontFamily = UiTypography.Interface; FontSize = UiTypography.Body; FontWeight = FontWeights.Normal; Foreground = ColorBrush("#18212D"); Background = Brushes.White;
        var root = new DockPanel(); var header = new StackPanel { Margin = new(24, 14, 24, 8) };
        header.Children.Add(new TextBlock { Text = "相机与业务调试", FontSize = UiTypography.PageTitle, FontWeight = UiTypography.Emphasis }); banner.Child = state; header.Children.Add(banner);
        var actions = Wrap(left, right, observe, isolate, stop, resume); header.Children.Add(actions); header.Children.Add(new ScrollViewer { Content = feedback, MaxHeight = 76, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        left.Click += (_, _) => SelectPosition("left"); right.Click += (_, _) => SelectPosition("right");
        observe.Click += async (_, _) => await Begin(CameraInspectionMode.ObserveProduction); isolate.Click += async (_, _) => await Begin(CameraInspectionMode.Isolated);
        stop.Click += async (_, _) => await Run(async t => { if (status?.SessionId is { } id) { var value = await Api.StopCameraInspectionCaptureAsync(id, Reason(), t); if (PublishCommand) status = value; } });
        resume.Click += async (_, _) => await Run(async t =>
        {
            if (status?.SessionId is not { } id || cleared.IsChecked != true) { setup.IsExpanded = true; cleared.Focus(); cleared.BringIntoView(); throw new InvalidOperationException("请先确认两位测试件已移除并核对原包裹责任。"); }
            var value = await Api.EndCameraInspectionAsync(new(Wire.Id(), id, true, true, Reason()), t); if (PublishCommand) { status = value; cleared.IsChecked = false; }
        });
        var body = new StackPanel { Margin = new(24, 0, 24, 16) }; var options = new StackPanel();
        options.Children.Add(Label("操作原因（至少5字，保留本机审计）")); options.Children.Add(reason); AutomationProperties.SetName(reason, "调试操作原因");
        options.Children.Add(Wrap(Field("期限 / 分钟", duration), Field("原图配额 / MiB", imageBudget), Field("记录上限", recordBudget))); options.Children.Add(cleared);
        options.Children.Add(Label("隔离作用于两位新供包；只有明确显示“隔离已生效”后才可放入测试件。已有请求先排空，原责任保留。停止采集、切页、撤权及重启均不自动恢复供包。软件隔离不是安全急停。"));
        setup = new() { Header = "调试原因、资源限额与清场确认", Content = options, IsExpanded = true, Margin = new(0, 0, 0, 8) }; body.Children.Add(setup);
        var parameter = Button("本相机参数"); parameter.Click += async (_, _) => await Run(_ => settings(Position.ReaderId)); body.Children.Add(Wrap(parameter, identity));
        var workspace = new WrapPanel(); var visual = new StackPanel { Margin = new(0, 0, 20, 0) }; var records = new StackPanel(); workspace.Children.Add(visual); workspace.Children.Add(records);
        workspace.SizeChanged += (_, _) => { var split = workspace.ActualWidth >= 760; visual.Width = Math.Max(240, split ? workspace.ActualWidth * .48 - 20 : workspace.ActualWidth - 20); records.Width = Math.Max(240, split ? workspace.ActualWidth * .52 : workspace.ActualWidth); };
        imageLayer.Children.Add(image); imageLayer.Children.Add(marks); viewport.Content = imageLayer; visual.Children.Add(Title("对应原图与识别位置")); visual.Children.Add(viewport); visual.Children.Add(caption);
        var fitButton = Button("适应窗口"); var actual = Button("1:1"); var less = Button("−"); var more = Button("＋");
        fitButton.Click += (_, _) => { fit = true; ApplyZoom(); }; actual.Click += (_, _) => { fit = false; scale = 1 / VisualTreeHelper.GetDpi(viewport).DpiScaleX; ApplyZoom(); }; less.Click += (_, _) => Zoom(.8); more.Click += (_, _) => Zoom(1.25);
        visual.Children.Add(Wrap(fitButton, actual, less, more, zoomText, overlay)); visual.Children.Add(symbols); visual.Children.Add(saveImage);
        overlay.Checked += (_, _) => RenderMarks(); overlay.Unchecked += (_, _) => RenderMarks(); symbols.SelectionChanged += (_, _) => RenderMarks(); viewport.SizeChanged += (_, _) => { if (fit) ApplyZoom(); };
        viewport.PreviewMouseWheel += (_, e) => { if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) { Zoom(e.Delta > 0 ? 1.25 : .8); e.Handled = true; } };
        saveImage.Click += async (_, _) => await Run(async t =>
        {
            if (selectedFrame is not { } frame) throw new InvalidOperationException("请选择可用原图。");
            var dialog = new SaveFileDialog { Title = "导出对应原图及身份JSON", Filter = "无损PNG原图|*.png", InitialDirectory = "D:\\", FileName = frame.Id + ".png", OverwritePrompt = false };
            if (dialog.ShowDialog(Window.GetWindow(this)) == true) { var value = await Api.ExportCameraOriginalAsync(frame.Id, dialog.FileName, t); Say("原图已导出，SHA256：" + value.Sha256); }
        });
        business.ItemsSource = businessRows; frames.ItemsSource = frameRows; tabs.Items.Add(new TabItem { Header = "业务扫描（原审计）", Content = business }); tabs.Items.Add(new TabItem { Header = "关联帧 / 未读码", Content = frames }); tabs.SelectedIndex = 0;
        tabs.SelectionChanged += async (_, e) => { if (!ReferenceEquals(e.Source, tabs)) return; selection = null; selectionGeneration++; ClearImage(); await PollAsync(); };
        business.SelectionChanged += async (_, _) => await SelectRow(business.SelectedItem as Row); frames.SelectionChanged += async (_, _) => await SelectRow(frames.SelectedItem as Row);
        records.Children.Add(Title("连续扫描与原请求结果")); records.Children.Add(tabs); records.Children.Add(coverage); records.Children.Add(Wrap(older, newer, latest, follow, pause)); records.Children.Add(result); records.Children.Add(record);
        pause.Click += (_, _) => { follow.IsChecked = follow.IsChecked != true; pause.Content = follow.IsChecked == true ? "暂停画面" : "继续画面"; };
        older.Click += async (_, _) => await Page(true); newer.Click += async (_, _) => await Page(false); latest.Click += async (_, _) => { historical = false; follow.IsChecked = true; if (tabs.SelectedIndex == 0) businessInitialized = false; else framesInitialized = false; await PollAsync(); };
        record.Click += async (_, _) => await Run(_ => selection?.OperationId is { } id ? details(id) : Task.CompletedTask);
        var originals = new TabControl(); originals.Items.Add(new TabItem { Header = "原输入预览", Content = inputEvidence }); originals.Items.Add(new TabItem { Header = "原请求与回执预览", Content = requestEvidence });
        records.Children.Add(new Expander { Header = "原始输入与请求证据（只读）", Content = originals }); body.Children.Add(workspace);
        sessions.SelectionChanged += (_, _) => { if (sessions.SelectedItem is CameraInspectionSession item && item.Id != viewedSession) ChangeSession(item.Id); };
        var loadSession = Button("读取会话ID"); loadSession.Click += async (_, _) => { ChangeSession(sessionId.Text.Trim()); tabs.SelectedIndex = 1; await PollAsync(); };
        body.Children.Add(new Expander { Header = "会话历史与证据导出", Content = Wrap(sessions, sessionId, loadSession, saveSession, purge), Margin = new(0, 8, 0, 8) });
        saveSession.Click += async (_, _) => await Run(async t =>
        {
            var id = viewedSession.Length == 0 ? status?.SessionId : viewedSession; if (id is null) throw new InvalidOperationException("请先选择会话。");
            var dialog = new SaveFileDialog { Title = "导出原图、识别记录与覆盖清单", Filter = "诊断证据ZIP|*.zip", InitialDirectory = "D:\\", FileName = "camera-inspection-" + id + ".zip", OverwritePrompt = false };
            if (dialog.ShowDialog(Window.GetWindow(this)) == true) { var value = await Api.ExportCameraInspectionAsync(id, dialog.FileName, t); Say($"已导出 {value.Frames} 帧记录，缺失/轮替原图 {value.MissingImages} 张。SHA256：{value.Sha256}"); }
        });
        purge.Click += async (_, _) => await Run(async t =>
        {
            if (viewedSession.Length == 0) throw new InvalidOperationException("请先选择已结束会话。");
            if (MessageBox.Show(Window.GetWindow(this), "将清理该已结束会话的诊断原图与帧记录，保留会话审计和业务原件。请先导出需要保留的证据。", "清理诊断会话", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
            await Api.PurgeCameraInspectionAsync(viewedSession, Reason(), t); if (PublishCommand) { frameRows.Clear(); framesInitialized = false; ClearImage(); Say("诊断集合已清理，业务原件与责任保留。"); }
        });
        var test = Button("测试当前运行筛选规则"); var local = new StackPanel(); local.Children.Add(sample); local.Children.Add(test); local.Children.Add(sampleResult);
        sample.TextChanged += (_, _) => sampleResult.Text = "候选已变化，请重新测试。此操作不产生业务请求。";
        test.Click += async (_, _) => await Run(async t =>
        {
            if (!vm.Fresh || vm.Snapshot is not { } snapshot) throw new InvalidOperationException("等待当前运行规则读取成功后再测试。"); var text = sample.Text; var policy = snapshot.Configuration.Effective.BarcodeFilter;
            var value = await Task.Run(() => vm.Application.TestBarcodeFilterAsync(policy ?? new(""), text.Replace("\r", "").Split('\n')), t);
            if (PublishCommand && sample.Text == text && vm.Snapshot?.Configuration.Effective.BarcodeFilter == policy) sampleResult.Text = value.Detail + "\n选中条码：" + (value.SelectedCode ?? "无") + " · 命中 " + value.Matches;
        });
        body.Children.Add(new Expander { Header = "本地筛选测试（不外呼）", Content = local });
        var synthetic = new StackPanel(); synthetic.Children.Add(Label("沿原Application模拟准入提交，不代表真实相机或客户RCS验收；生产模式不显示此入口。")); synthetic.Children.Add(simulationCode); synthetic.Children.Add(emit); simulation.Content = synthetic; body.Children.Add(simulation);
        emit.Click += async (_, _) => await Run(async _ =>
        {
            if (!vm.Fresh || vm.Snapshot?.Configuration.Effective.Mode != RunMode.Simulation) throw new InvalidOperationException("当前不是可用的Simulation输入模式。");
            var p = Position; p.ParcelCode = simulationCode.Text; p.NewParcel(); await vm.EmitPositionAsync(p);
        });
        root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }); Content = root;
        timer.Tick += async (_, _) => await PollAsync(); Loaded += async (_, _) => { active = true; lifetime = new(); timer.Start(); await PollAsync(); }; Unloaded += (_, _) => Deactivate(false); SelectPosition("left");
    }
    ICameraInspection Api => api ?? throw new InvalidOperationException("当前组合未提供完整关联调试契约，请使用同源内置SM装配。");
    string Reason()
    {
        if (reason.Text.Trim().Length >= 5) return reason.Text.Trim();
        setup.IsExpanded = true; reason.Focus(); reason.BringIntoView(); throw new InvalidOperationException("请填写至少5字的调试操作原因。");
    }
    bool PublishCommand => active && vm.IsManagementMode && generation == commandGeneration;
    void Say(string text) { if (PublishCommand) feedback.Text = text; }
    async Task Begin(CameraInspectionMode mode) => await Run(async t =>
    {
        var why = Reason(); if (!vm.Fresh || vm.Snapshot is not { } snapshot) throw new InvalidOperationException("请先等待最新业务状态。");
        if (mode == CameraInspectionMode.Isolated && cleared.IsChecked != true) { setup.IsExpanded = true; cleared.Focus(); cleared.BringIntoView(); throw new InvalidOperationException("进入实机隔离前，请确认已停止真实投放并核对两位当前实体。"); }
        if (!int.TryParse(duration.Text, out var minutes) || !int.TryParse(imageBudget.Text, out var mib) || !int.TryParse(recordBudget.Text, out var count)) throw new InvalidOperationException("资源限额必须为整数。");
        var warning = mode == CameraInspectionMode.Isolated ? "将阻断两位新供包，等待原在途请求排空。只有显示“实机隔离已生效”后才可放入测试件。结束前须清除测试件并显式确认。" : "生产观察保持正常业务。放入测试码可能产生真实RCS请求，仅用于正常生产观察。";
        if (MessageBox.Show(Window.GetWindow(this), warning, "开始关联采集", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
        var value = await Api.BeginCameraInspectionAsync(new(Wire.Id(), snapshot.RuntimeId, snapshot.Configuration.Revision, mode, why, new(minutes, mib, count), cleared.IsChecked == true), t);
        if (!PublishCommand) return; status = value; ChangeSession(value.SessionId ?? ""); commandGeneration = generation; cleared.IsChecked = false; setup.IsExpanded = false; tabs.SelectedIndex = 1;
    });
    async Task Run(Func<CancellationToken, Task> action)
    {
        if (!active || busy) return; busy = true; commandGeneration = generation; SetButtons();
        try { await vm.RequireManagementModeAsync(); await action(lifetime!.Token); }
        catch (OperationCanceledException) { Say("等待已取消；若已提交隔离，必须刷新核对原会话，不能推断已恢复供包。"); }
        catch (Exception error) { Say("操作未完成：" + error.Message + "\n请核对当前状态和原会话。"); }
        finally { busy = false; if (active) { SetButtons(); await PollAsync(); } }
    }
    public void Revoke() => Deactivate(true);
    void Deactivate(bool clear)
    {
        active = false; generation++; selectionGeneration++; timer.Stop(); lifetime?.Cancel(); lifetime?.Dispose(); lifetime = null; selectionCancellation?.Cancel(); selectionCancellation?.Dispose(); selectionCancellation = null;
        ClearImage(); inputEvidence.Clear(); requestEvidence.Clear();
        if (clear) { selection = null; status = null; lastSessionState = ""; businessInitialized = framesInitialized = false; historical = false; businessCursor = frameCursor = 0; businessRows.Clear(); frameRows.Clear(); sample.Clear(); simulationCode.Clear(); reason.Clear(); cleared.IsChecked = false; sessionId.Clear(); viewedSession = ""; sessions.ItemsSource = null; state.Text = "管理模式已结束；隔离由Application继续保持。"; feedback.Text = result.Text = sampleResult.Text = identity.Text = ""; }
    }
    public void SelectPosition(string value)
    {
        position = value; generation++; selectionGeneration++; businessInitialized = framesInitialized = false; businessCursor = frameCursor = 0; businessOlder = long.MaxValue; businessNewer = 0; historical = false;
        selection = null; businessRows.Clear(); frameRows.Clear(); ClearImage(); inputEvidence.Clear(); requestEvidence.Clear(); result.Text = "";
        left.SetResourceReference(StyleProperty, value == "left" ? "ParameterPrimaryAction" : "ParameterAction"); right.SetResourceReference(StyleProperty, value == "right" ? "ParameterPrimaryAction" : "ParameterAction");
    }
    void ChangeSession(string id)
    {
        generation++; selectionGeneration++; viewedSession = id; sessionId.Text = id; framesInitialized = false; frameCursor = 0; frameRows.Clear(); selection = null;
        ClearImage(); inputEvidence.Clear(); requestEvidence.Clear(); result.Text = "";
    }
    void SetButtons()
    {
        var ready = active && vm.IsManagementMode && api is not null && !busy;
        emit.IsEnabled = ready && vm.Fresh && Position.CanAdmit && vm.Snapshot?.Configuration.Effective.Mode == RunMode.Simulation;
        observe.IsEnabled = isolate.IsEnabled = ready && status?.BusinessFenced != true && status?.State is not ("Active" or "Preparing"); stop.IsEnabled = ready && status?.State == "Active"; resume.IsEnabled = ready && status?.BusinessFenced == true;
        saveImage.IsEnabled = ready && selectedFrame?.ImageAvailable == true; saveSession.IsEnabled = ready && (viewedSession.Length != 0 || status?.SessionId is not null); purge.IsEnabled = ready && viewedSession.Length != 0; record.IsEnabled = ready && selection?.OperationId is not null;
    }
    bool Current(long version) => active && generation == version && vm.IsManagementMode;
    bool Selected(long version) => active && selectionGeneration == version && vm.IsManagementMode;
    internal async Task PollAsync()
    {
        if (!active || polling) return; if (!vm.IsManagementMode) { Revoke(); return; } polling = true; var version = generation; var t = lifetime!.Token;
        try
        {
            var current = await Api.CameraInspectionStatusAsync(t); if (!Current(version)) return; status = current;
            banner.Background = ColorBrush(current.BusinessFenced ? "#FFF2D5" : "#F2F5F8");
            state.Text = (current.BusinessFenced ? "实机隔离 · 两位新供包已阻断" : current.State == "Active" ? "生产观察 · 正常业务仍运行" : "关联调试 · " + SessionText(current.State)) +
                $"\n{current.Detail}\n帧记录 {current.Frames:N0} · 原图 {current.ImageBytes / 1048576d:N1} MiB · 轮替 {current.EvictedImages:N0} · 诊断接收缺口 {current.MissingFrames:N0}" + (current.ExpiresAt is { } expires ? " · 截止 " + expires.ToLocalTime().ToString("HH:mm:ss") : "");
            identity.Text = (position == "left" ? vm.LeftConnectionDiagnosis : vm.RightConnectionDiagnosis) + "  ·  " + Position.CameraAddress + "  ·  " + Position.ReaderIdentity;
            simulation.Visibility = vm.Snapshot?.Configuration.Effective.Mode == RunMode.Simulation ? Visibility.Visible : Visibility.Collapsed;
            var stateKey = current.SessionId + ":" + current.State;
            if (stateKey != lastSessionState)
            {
                var items = await Api.ListCameraInspectionSessionsAsync(t); if (!Current(version)) return; lastSessionState = stateKey; var chosen = viewedSession; sessions.ItemsSource = items; sessions.SelectedItem = items.FirstOrDefault(x => x.Id == chosen);
                if (viewedSession.Length == 0 && current.SessionId is { } id) { viewedSession = id; sessionId.Text = id; }
            }
            if (!historical)
            {
                if (tabs.SelectedIndex == 0)
                {
                    var page = await Api.ReadCameraBusinessAsync(Position.ReaderId, businessCursor, businessInitialized ? null : long.MaxValue, 200, t); if (!Current(version)) return;
                    if (!businessInitialized) businessRows.Clear(); Merge(businessRows, page.Rows.Select(x => new Row { Business = x }));
                    businessCursor = businessInitialized ? page.Cursor : page.HighWater; businessOlder = businessRows.Count == 0 ? page.FirstSequence : businessRows.Min(x => x.Sequence); businessNewer = page.LastSequence == 0 ? businessCursor : page.LastSequence; businessInitialized = true;
                    coverage.Text = $"可视区最近 {businessRows.Count} 条；审计游标 {businessCursor:N0}/{page.HighWater:N0}。" + (page.HasMore ? "按页追赶；原审计不受显示容量限制。" : "本次已追平。") + "\n" + page.Coverage;
                }
                else if (viewedSession.Length != 0)
                {
                    var page = await Api.ReadCameraFramesAsync(viewedSession, Position.ReaderId, frameCursor, framesInitialized ? null : long.MaxValue, 200, t); if (!Current(version)) return;
                    if (!framesInitialized) frameRows.Clear(); Merge(frameRows, page.Rows.Select(x => new Row { Frame = x }));
                    frameCursor = framesInitialized ? page.Cursor : page.Rows.Length == 0 ? page.HighWater : page.Rows.Max(x => x.Sequence); framesInitialized = true;
                    coverage.Text = $"可视区最近 {frameRows.Count} 帧；持久游标 {frameCursor:N0}/{page.HighWater:N0}。完整记录可分页/导出；轮替原图明确标记。";
                }
            }
            var allWatched = businessRows.Concat(frameRows).Select(x => x.OperationId).OfType<string>().Distinct().ToArray();
            var watched = allWatched.Skip(watchOffset).Concat(allWatched.Take(watchOffset)).Take(63)
                .Prepend(selection?.OperationId ?? "").Where(x => x.Length != 0).Distinct().Take(64).ToArray();
            watchOffset = allWatched.Length == 0 ? 0 : (watchOffset + 63) % allWatched.Length;
            if (watched.Length != 0)
            {
                var ops = await Api.ReadCameraOperationsAsync(watched, t); if (!Current(version)) return; var map = ops.ToDictionary(x => x.Id);
                foreach (var row in businessRows.Concat(frameRows)) if (row.OperationId is { } id && map.TryGetValue(id, out var op)) row.Operation = op;
            }
            if (follow.IsChecked == true && !historical && CurrentRows.FirstOrDefault() is { } newest && CurrentList.SelectedItem != newest)
            { selecting = true; CurrentList.SelectedItem = newest; selecting = false; }
            if (selection?.OperationId is { } selectedId && detailRevision != vm.Snapshot?.Revision) await RefreshOperation(selectedId, selectionGeneration, t);
            if (selection is { } selected && selected.OperationId is null && (selected.Business?.ObservationId ?? selected.Frame?.ObservationId) is { } observation)
            {
                var selectionVersion = selectionGeneration; var frame = await Api.FindObservationFrameAsync(observation, t); if (!Current(version) || !Selected(selectionVersion)) return;
                if (frame is not null) { if (original is null) await LoadFrame(frame, selectionVersion, t); if (frame.BusinessSequence > 0) { await LoadInput(frame.BusinessSequence, selected, selectionVersion, t); if (selected.OperationId is { } linkedId) await RefreshOperation(linkedId, selectionVersion, t); } }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (Current(version)) { feedback.Text = "调试读取中断：" + error.Message + "。保留游标，恢复后继续；不能据此推断业务或隔离结果。"; ClearImage(); } }
        finally { polling = false; if (active) SetButtons(); }
    }
    static void Merge(ObservableCollection<Row> target, IEnumerable<Row> rows)
    { foreach (var row in rows.OrderBy(x => x.Sequence)) if (!target.Any(x => x.Key == row.Key)) target.Insert(0, row); while (target.Count > 500) target.RemoveAt(target.Count - 1); }
    async Task Page(bool backwards)
    {
        if (!active || busy || polling || tabs.SelectedIndex != 0 && CurrentRows.Count == 0) return; historical = true; follow.IsChecked = false; var version = generation; var rows = CurrentRows; var t = lifetime!.Token;
        try
        {
            if (tabs.SelectedIndex == 0) { var boundary = backwards ? businessOlder : businessNewer; if (backwards && boundary <= 0) { coverage.Text = "已到原审计起点。"; return; } var page = await Api.ReadCameraBusinessAsync(Position.ReaderId, backwards ? 0 : boundary, backwards ? boundary : null, 200, t); if (!Current(version)) return; if (page.FirstSequence == 0) { coverage.Text = "已到此方向的审计边界，保留当前页。"; return; } businessOlder = page.FirstSequence; businessNewer = page.LastSequence; rows.Clear(); Merge(rows, page.Rows.Select(x => new Row { Business = x })); }
            else { var page = await Api.ReadCameraFramesAsync(viewedSession, Position.ReaderId, backwards ? 0 : rows.Max(x => x.Sequence), backwards ? rows.Min(x => x.Sequence) : null, 200, t); if (!Current(version)) return; if (page.Rows.Length == 0) { coverage.Text = "已到此方向的记录边界，保留当前页。"; return; } rows.Clear(); Merge(rows, page.Rows.Select(x => new Row { Frame = x })); }
            coverage.Text = $"历史分页：本页 {rows.Count} 条；新数据继续持久记录。点击“回到最新”追赶。"; if (rows.Count != 0) CurrentList.SelectedIndex = 0;
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (Current(version)) feedback.Text = "分页未完成：" + error.Message; }
    }
    async Task SelectRow(Row? row)
    {
        if (!active || row is null || !ReferenceEquals(CurrentList.SelectedItem, row)) return; if (!selecting) follow.IsChecked = false;
        selection = row; var version = ++selectionGeneration; detailRevision = -1; ClearImage(); inputEvidence.Clear(); requestEvidence.Clear(); result.Text = "";
        selectionCancellation?.Cancel(); selectionCancellation?.Dispose(); selectionCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime!.Token); var t = selectionCancellation.Token;
        try
        {
            if (row.Business is { } input) { await LoadInput(input.Sequence, row, version, t); var frame = await Api.FindObservationFrameAsync(input.ObservationId, t); if (!Selected(version)) return; if (frame is not null) await LoadFrame(frame, version, t); else caption.Text = "此输入没有已入库关联图像；可能未在采集范围或仍在写入。不会借用其他帧。"; }
            else if (row.Frame is { } frame) { await LoadFrame(frame, version, t); if (frame.BusinessSequence > 0) await LoadInput(frame.BusinessSequence, row, version, t); else inputEvidence.Text = frame.Symbols.Length == 0 ? "此帧未识别到候选，仅保留原图及解码事实，不建立业务输入或作业。" : "正在等待关联输入入库；隔离输入不建立业务请求。"; }
            if (row.OperationId is { } id) await RefreshOperation(id, version, t);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (Selected(version)) { ClearImage(); caption.Text = "对应证据不可读：" + error.Message; } }
        finally { if (active) SetButtons(); }
    }
    async Task LoadInput(long sequence, Row row, long version, CancellationToken t)
    { var input = await Api.ReadCameraBusinessDetailAsync(sequence, t); if (!Selected(version)) return; inputEvidence.Text = input is null ? "原输入已不在可读范围。" : Protect(Wire.Encode(input), 32768); row.LinkedOperation = input?.OperationId; }
    async Task LoadFrame(CameraFrameMetadata frame, long version, CancellationToken t)
    {
        if (selection?.Business is { } input && (frame.ObservationId != input.ObservationId || frame.DeviceId != input.DeviceId || frame.Epoch != input.Epoch || frame.SourceEventId != input.SourceEventId))
            throw new InvalidDataException("帧号、相机代次与原输入的关联不一致，拒绝显示替代图。");
        var raw = await Api.ReadCameraOriginalAsync(frame.Id, t); if (!Selected(version)) return; selectedFrame = frame;
        if (raw is null) { caption.Text = $"帧 {frame.FrameNumber} · {frame.CapturedAt.ToLocalTime():HH:mm:ss.fff}\n{frame.ImageState}。原图不可用，不使用替代帧。"; selectedFrame = frame with { ImageAvailable = false }; return; }
        if (raw.Metadata.Id != frame.Id || raw.Metadata.DeviceId != Position.ReaderId || raw.Gray8.LongLength != (long)frame.Width * frame.Height) throw new InvalidDataException("原图与选中记录身份不一致。");
        original = raw; var bitmap = BitmapSource.Create(frame.Width, frame.Height, 96, 96, PixelFormats.Gray8, null, raw.Gray8, frame.Width); bitmap.Freeze(); image.Source = bitmap;
        imageLayer.Width = image.Width = marks.Width = frame.Width; imageLayer.Height = image.Height = marks.Height = frame.Height; symbols.ItemsSource = frame.Symbols; symbols.SelectedIndex = frame.Symbols.Length > 0 ? 0 : -1;
        caption.Text = $"原始 Mono8 · 帧 {frame.FrameNumber} · 主机接收时间 {frame.CapturedAt.ToLocalTime():HH:mm:ss.fff} · {frame.Width}×{frame.Height}\n识别 {frame.Symbols.Length} 个候选 · 可标注 {frame.Symbols.Count(x => x.Corners.Length == 4)} 个 · 解码 {frame.DecodeMilliseconds:N1} ms · 前序视频跳帧 {frame.VideoFramesSkippedBefore}\n原始像素 SHA256：{frame.OriginalSha256}"; ApplyZoom();
    }
    async Task RefreshOperation(string id, long version, CancellationToken t)
    {
        var value = await Api.ReadCameraOperationDetailAsync(id, t); if (!Selected(version)) return;
        if (value is null) { result.Text = "原作业不可读取，不能推断成功。"; return; } detailRevision = value.Status.Revision;
        result.Text = ResultText(value.Status) + " · 原请求 " + (value.Status.RequestId ?? "尚未建立") + " · 发送尝试 " + value.Status.Attempts;
        var text = ParcelViewModel.Handover(value.Original, true); if (value.Original.Wcs?.Request.IntegrationRequest is { } request) text += "\n\n原请求正文（只读）\n" + Protect(request.Body, 8192);
        if (value.Original.Wcs?.Replies.LastOrDefault()?.HttpResponse is { } response)
        { var length = Math.Min(response.BodyBase64.Length, 10920); length -= length % 4; try { text += $"\n\n原响应 HTTP {response.StatusCode} · 完整接收 {response.Complete}\n" + Encoding.UTF8.GetString(Convert.FromBase64String(response.BodyBase64[..length])); } catch (FormatException) { text += "\n原响应编码不可显示，请核对原件。"; } }
        requestEvidence.Text = Protect(text, 32768);
    }
    void ClearImage() { image.Source = null; marks.Children.Clear(); original = null; selectedFrame = null; symbols.ItemsSource = null; imageLayer.Width = imageLayer.Height = 0; caption.Text = "请选择记录查看对应原图；不借用最新画面替代历史帧。"; }
    void Zoom(double factor) { fit = false; scale = Math.Clamp(scale * factor, .02, 16); ApplyZoom(); }
    void ApplyZoom()
    { if (original is null) return; if (fit) scale = Math.Clamp(Math.Min(Math.Max(1, viewport.ViewportWidth - 8) / original.Metadata.Width, Math.Max(1, viewport.ViewportHeight - 8) / original.Metadata.Height), .02, 16); imageLayer.LayoutTransform = new ScaleTransform(scale, scale); var physicalScale = scale * VisualTreeHelper.GetDpi(viewport).DpiScaleX; zoomText.Text = (physicalScale * 100).ToString("N0") + "%"; RenderOptions.SetBitmapScalingMode(image, physicalScale >= 1 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality); RenderMarks(); }
    void RenderMarks()
    {
        marks.Children.Clear(); if (original is null || overlay.IsChecked != true) return;
        for (var i = 0; i < original.Metadata.Symbols.Length; i++)
        { var symbol = original.Metadata.Symbols[i]; if (symbol.Corners.Length != 4) continue; var color = symbols.SelectedIndex == i ? Brushes.Yellow : Brushes.Cyan; marks.Children.Add(new Polygon { Points = new PointCollection(symbol.Corners.Select(p => new Point(p.X, p.Y))), Stroke = color, StrokeThickness = 2 / scale, Fill = Brushes.Transparent }); var label = new TextBlock { Text = (i + 1) + " " + symbol.Format + " · " + Short(symbol.Text, 32), Foreground = Brushes.Black, Background = color, FontSize = 14 / scale }; Canvas.SetLeft(label, Math.Clamp(symbol.Corners.Min(p => p.X), 0, Math.Max(0, original.Metadata.Width - 1))); Canvas.SetTop(label, Math.Clamp(symbol.Corners.Min(p => p.Y) - 20 / scale, 0, Math.Max(0, original.Metadata.Height - 1))); marks.Children.Add(label); }
    }
    static string InputText(string value) => value switch { "Admitted" => "已准入", "Rejected" => "未准入", "Blocked" => "已阻断", "Duplicate" => "重复输入事件", _ => value };
    static string ResultText(CameraOperationStatus value) => value.BusinessAccepted ? "RCS业务已接受 · " + StateText(value.State) : value.StageState switch { "Unknown" => "原请求结果未知", "Rejected" => "RCS业务拒绝", "Prepared" => "请求已准备", _ => "业务状态 " + StateText(value.State) };
    static string StateText(string value) => value switch { "SlotResolved" => "已分配格口", "WcsUnknown" => "原请求结果未知", "PhysicalCompleted" => "物理完成", "Prepared" => "任务已准备", "Abandoned" => "已放弃", "WcsRejected" => "请求被拒绝", _ => value };
    static string SessionText(string value) => value switch { "Idle" => "尚未采集", "Preparing" => "正在排空/切换", "Transitioning" => "正在切换", "Active" => "关联采集中", "Closed" => "会话已结束", "Expired" => "期限已到", "Faulted" => "采集故障", "Suspended" => "采集已停止", "RecoveryFenced" => "隔离待清场", "Unavailable" => "诊断集合不可用", "Stopped" => "采集已停止", "Interrupted" => "上次会话中断", _ => value };
    static string Short(string value, int length) => value.Length > length ? value[..length] + "…" : value;
    static string Protect(string value, int limit) => DiagnosticText.Protect(value[..Math.Min(value.Length, limit)]) + (value.Length > limit ? "\n[本页显示截断，原件保留]" : "");
    static Brush ColorBrush(string color) => (Brush)new BrushConverter().ConvertFromString(color)!;
    static Button Button(string text) => new() { Content = text, MinHeight = 44, Padding = new(12, 6, 12, 6), Margin = new(0, 0, 8, 6) };
    static TextBlock Label(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = UiTypography.Label, Foreground = ColorBrush("#52606F"), Margin = new(0, 4, 0, 6) };
    static TextBlock Title(string text) => new() { Text = text, FontSize = UiTypography.Group, FontWeight = UiTypography.Emphasis, Margin = new(0, 8, 0, 8) };
    static TextBox Number(string text) => new() { Text = text, MaxLength = 6, MinWidth = 80, MaxWidth = 120 };
    static TextBox ReadOnly() => new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Height = 190, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    static WrapPanel Wrap(params UIElement[] controls) { var row = new WrapPanel { Margin = new(0, 8, 0, 0) }; foreach (var control in controls) row.Children.Add(control); return row; }
    static FrameworkElement Field(string text, UIElement control) { var field = new StackPanel { Margin = new(0, 0, 16, 0) }; var label = Label(text); AutomationProperties.SetLabeledBy(control, label); AutomationProperties.SetName(control, text); field.Children.Add(label); field.Children.Add(control); return field; }
    static ListBox List()
    {
        var list = new ListBox { Height = 260, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        var text = new FrameworkElementFactory(typeof(TextBlock)); text.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(Row.Label))); text.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap); text.SetValue(MarginProperty, new Thickness(4, 6, 4, 6));
        list.ItemTemplate = new DataTemplate { VisualTree = text }; ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        VirtualizingPanel.SetIsVirtualizing(list, true); VirtualizingPanel.SetVirtualizationMode(list, VirtualizationMode.Recycling); ScrollViewer.SetCanContentScroll(list, true); return list;
    }
}

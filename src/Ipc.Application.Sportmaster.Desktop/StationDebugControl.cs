using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Platform;

namespace Ipc.Application.Sportmaster.Desktop;

// A view of one existing Application, never an acquisition or request owner.
internal sealed class StationDebugControl : UserControl
{
    readonly SupplyStationViewModel vm;
    readonly Func<string, Task> settings;
    readonly Func<string, Task> details;
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(750) };
    readonly Image image = new() { Stretch = Stretch.Uniform, MaxHeight = 200 };
    readonly TextBlock previewStatus = Label("等待图像"), connection = Label("等待连接状态"), identity = Label(""), feedback = Label("");
    readonly TextBox evidence = ReadOnly("选择一条扫描，查看对应的原请求与回执。"), sample = new() { AcceptsReturn = true, Height = 84, MaxLength = 16384, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    readonly TextBlock sampleResult = Label("每行一个候选；只使用当前运行规则，本地测试不创建作业或请求。");
    readonly ListBox scans = new() { Height = 96, HorizontalContentAlignment = HorizontalAlignment.Stretch, DisplayMemberPath = nameof(ScanRow.Label) };
    readonly ObservableCollection<ScanRow> rows = [];
    readonly Button pause = new() { Content = "暂停画面" }, openRecord = new() { Content = "查看原作业记录", IsEnabled = false };
    readonly Button left = new() { Content = "左相机", MinWidth = 120 }, right = new() { Content = "右相机", MinWidth = 120 };
    readonly StackPanel simulation = new();
    readonly TextBox simulationCode = new() { MaxLength = 256, MinWidth = 200 };
    readonly Button emit = new() { Content = "提交模拟读码" };
    CancellationTokenSource? cancellation;
    bool active, polling, paused, testing;
    long generation;
    string position = "left";
    Guid? runtime;
    string? lastFrame;
    string? clearedInput;
    SupplyStationPositionViewModel Position => position == "left" ? vm.Left : vm.Right;
    // Do not retain full operation/HTTP payload graphs for each sampled row.
    sealed record ScanRow(string InputId, string Label, string? OperationId, string Evidence);
    static ScanRow Row(ParcelInputSnapshot input, ParcelOperation? operation) => new(input.Observation.Id,
        input.RecordedAt.ToLocalTime().ToString("HH:mm:ss.fff") + "  ·  " +
        Bounded(input.BarcodeSelection?.SelectedCode ?? string.Join(" / ", input.Observation.Codes.Take(3))) + "  ·  " +
        (input.Outcome switch { ParcelInputOutcome.Admitted => "已准入", ParcelInputOutcome.Blocked => "已阻断", ParcelInputOutcome.Rejected => "未准入", _ => "重复输入事件" }),
        operation?.Id, EvidenceText(input, operation));

    public StationDebugControl(SupplyStationViewModel vm, Func<string, Task> settings, Func<string, Task> details)
    {
        this.vm = vm; this.settings = settings; this.details = details;
        Resources.MergedDictionaries.Add(new() { Source = new Uri("/Ipc.Application.ParcelFlow.Desktop;component/ParameterSettingsTheme.xaml", UriKind.Relative) });
        Background = Brushes.White; FontWeight = FontWeights.Normal; Foreground = new SolidColorBrush(Color.FromRgb(24, 33, 45));
        var root = new DockPanel();
        var header = new StackPanel { Margin = new(24, 16, 24, 12) };
        header.Children.Add(new TextBlock { Text = "调试", FontSize = UiTypography.PageTitle, FontWeight = UiTypography.Emphasis });
        header.Children.Add(Label("查看相机图像、扫描判定与原请求。生产采集继续运行，切页不重建连接。"));
        var tabs = new WrapPanel { Margin = new(0, 12, 0, 0) }; tabs.Children.Add(left); tabs.Children.Add(right);
        left.Click += (_, _) => SelectPosition("left"); right.Click += (_, _) => SelectPosition("right"); header.Children.Add(tabs);
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var body = new StackPanel { Margin = new(24, 0, 24, 16) };
        var configuration = new Button { Content = "相机参数", VerticalAlignment = VerticalAlignment.Center, Margin = new(12, 0, 0, 0) };
        configuration.Click += async (_, _) => { if (vm.IsManagementMode) await settings(Position.ReaderId); };
        var device = new DockPanel(); DockPanel.SetDock(configuration, Dock.Right); device.Children.Add(configuration);
        var facts = new StackPanel(); facts.Children.Add(connection); facts.Children.Add(identity); device.Children.Add(facts);
        body.Children.Add(new Border { Child = device, Background = new SolidColorBrush(Color.FromRgb(245, 247, 250)), Padding = new(12, 6, 12, 6), CornerRadius = new(6) });
        var workspace = new WrapPanel { Margin = new(0, 12, 0, 0) };
        var visualColumn = new StackPanel { Margin = new(0, 0, 20, 0) }; var resultColumn = new StackPanel();
        workspace.Children.Add(visualColumn); workspace.Children.Add(resultColumn); body.Children.Add(workspace);
        workspace.SizeChanged += (_, _) =>
        {
            var split = workspace.ActualWidth >= 720;
            visualColumn.Width = Math.Max(240, split ? workspace.ActualWidth * .48 - 20 : workspace.ActualWidth - 20);
            resultColumn.Width = Math.Max(240, split ? workspace.ActualWidth * .52 : workspace.ActualWidth);
        };
        var picture = new Grid { Height = 200, Background = new SolidColorBrush(Color.FromRgb(24, 33, 45)), Margin = new(0, 0, 0, 8) };
        picture.Children.Add(image); AutomationProperties.SetName(image, "当前相机缩略图，与扫描列表不保证同帧");
        var pictureActions = new WrapPanel(); pictureActions.Children.Add(pause);
        pause.Click += (_, _) => { paused = !paused; generation++; pause.Content = paused ? "继续画面" : "暂停画面"; previewStatus.Text = paused ? "画面已暂停 · 相机采集与业务继续；此图不可作为当前结果。" : "等待新图像"; };
        visualColumn.Children.Add(Section("相机画面", picture, previewStatus, pictureActions,
            Label("按需缩略预览，最多每秒 2 帧。时间和帧号用于核对图像；下方扫描来自独立业务快照。")));
        scans.ItemsSource = rows; scans.SelectionChanged += (_, _) => ShowSelection();
        AutomationProperties.SetName(scans, "本相机最近采样扫描，最多50条，不替代完整作业记录");
        resultColumn.Children.Add(Section("扫描与判定", Label("仅收集本页打开期间的快照采样，最多 50 条，最新在上；完整记录请查看作业记录。"), scans));
        var clear = new Button { Content = "清空本页采样", HorizontalAlignment = HorizontalAlignment.Left };
        clear.Click += (_, _) => { clearedInput = Position.Input?.Observation.Id; rows.Clear(); ShowSelection(); }; resultColumn.Children.Add(clear);
        openRecord.Click += async (_, _) => { if (vm.IsManagementMode && scans.SelectedItem is ScanRow { OperationId: { } id }) await details(id); };
        resultColumn.Children.Add(Section("原请求与回执", Label("HTTP 成功不代表业务接受。未知原请求不重发；新扫描是否准入由现行业务守卫决定。"), evidence, openRecord));
        var test = new Button { Content = "测试当前运行筛选规则", HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(sample, "本地筛选测试候选");
        sample.TextChanged += (_, _) => sampleResult.Text = "测试内容已变化，请重新测试。不会发送业务请求。";
        test.Click += async (_, _) =>
        {
            if (testing || !active || !vm.Fresh || !vm.IsManagementMode || vm.Snapshot is not { } s) return;
            testing = true; test.IsEnabled = false;
            var version = generation; var text = sample.Text; var policy = s.Configuration.Effective.BarcodeFilter;
            try
            {
                await vm.RequireManagementModeAsync();
                var result = await Task.Run(() => vm.Application.TestBarcodeFilterAsync(policy ?? new(""), text.Replace("\r", "").Split('\n')));
                if (active && vm.IsManagementMode && version == generation && sample.Text == text && vm.Snapshot?.Configuration.Effective.BarcodeFilter == policy)
                    sampleResult.Text = result.Detail + "\n选中条码：" + (result.SelectedCode ?? "无") + " · 有效候选 " + result.Matches;
            }
            catch (Exception error) { if (active && vm.IsManagementMode && version == generation) sampleResult.Text = "测试未完成：" + error.Message; }
            finally { testing = false; test.IsEnabled = true; }
        };
        visualColumn.Children.Add(new Expander { Header = "本地筛选测试（不外呼）", Content = Section(null, sample, test, sampleResult), Margin = new(0, 8, 0, 8) });
        simulation.Children.Add(Label("仅模拟模式：通过现有 Application 准入提交模拟输入，不代表真实相机或客户 RCS 验收。"));
        simulation.Children.Add(simulationCode); simulation.Children.Add(emit);
        emit.Click += async (_, _) =>
        {
            if (!active || !vm.IsManagementMode || vm.Snapshot?.Configuration.Effective.Mode != RunMode.Simulation) return;
            try
            {
                await vm.RequireManagementModeAsync();
                var p = Position; p.ParcelCode = simulationCode.Text; p.NewParcel(); await vm.EmitPositionAsync(p);
            }
            catch (Exception error) { if (active && vm.IsManagementMode) feedback.Text = "模拟输入未提交：" + error.Message; }
        };
        visualColumn.Children.Add(new Expander { Header = "模拟输入", Content = simulation, Margin = new(0, 8, 0, 8) });
        body.Children.Add(feedback);
        root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }); Content = root;
        timer.Tick += async (_, _) => await PollAsync();
        Loaded += async (_, _) => { active = true; cancellation = new(); timer.Start(); await PollAsync(); };
        Unloaded += (_, _) => Deactivate(false);
        SelectPosition("left");
    }
    public void Revoke() => Deactivate(true);
    void Deactivate(bool clear)
    {
        active = false; generation++; timer.Stop(); cancellation?.Cancel(); cancellation?.Dispose(); cancellation = null;
        image.Source = null; lastFrame = null; previewStatus.Text = "预览已停止";
        if (clear) { rows.Clear(); sample.Clear(); sampleResult.Text = "管理模式已结束，测试结果已清除。"; simulationCode.Clear(); evidence.Clear(); feedback.Text = ""; identity.Text = ""; connection.Text = ""; }
    }
    public void SelectPosition(string value)
    {
        position = value; generation++; rows.Clear(); clearedInput = null; image.Source = null; lastFrame = null; paused = false; pause.Content = "暂停画面";
        left.SetResourceReference(StyleProperty, position == "left" ? "ParameterPrimaryAction" : "ParameterAction");
        right.SetResourceReference(StyleProperty, position == "right" ? "ParameterPrimaryAction" : "ParameterAction"); ShowSelection();
    }
    internal async Task PollAsync()
    {
        if (!active || polling) return;
        if (!vm.IsManagementMode) { Revoke(); return; }
        var p = Position; var version = generation;
        connection.Text = position == "left" ? vm.LeftConnectionDiagnosis : vm.RightConnectionDiagnosis;
        identity.Text = p.CameraAddress + "  ·  " + p.ReaderIdentity;
        feedback.Text = vm.TaskFeedback;
        ((FrameworkElement)simulation.Parent).SetValue(VisibilityProperty, vm.Snapshot?.Configuration.Effective.Mode == RunMode.Simulation ? Visibility.Visible : Visibility.Collapsed);
        emit.IsEnabled = p.CanAdmit;
        if (!vm.Fresh) { image.Source = null; lastFrame = null; previewStatus.Text = "状态读取中断，图像不可用；列表保留为历史采样。"; return; }
        if (runtime != vm.Snapshot?.RuntimeId) { runtime = vm.Snapshot?.RuntimeId; rows.Clear(); image.Source = null; lastFrame = null; }
        if (p.Input is { } input && input.Observation.Id != clearedInput)
        {
            var found = rows.FirstOrDefault(x => x.InputId == input.Observation.Id);
            var current = Row(input, p.Operation?.Id == input.OperationId ? p.Operation : null);
            if (found is null)
            {
                rows.Insert(0, current);
                if (rows.Count > 50) rows.RemoveAt(rows.Count - 1);
                if (scans.SelectedItem is null) scans.SelectedIndex = 0;
            }
            else if (found != current)
            {
                var selected = ReferenceEquals(scans.SelectedItem, found); var row = current;
                rows[rows.IndexOf(found)] = row; if (selected) scans.SelectedItem = row;
            }
        }
        if (paused) return;
        if (vm.Application is not ICameraDiagnostics service) { previewStatus.Text = "当前组合未提供图像预览；扫描与原请求仍可核对。"; return; }
        polling = true;
        try
        {
            var result = await service.ReadCameraPreviewAsync(p.ReaderId, cancellation!.Token);
            if (!active || version != generation || !vm.IsManagementMode || !vm.Fresh) return;
            previewStatus.Text = result.Status;
            if (result.Frame is not { } frame) { image.Source = null; lastFrame = null; return; }
            if (frame.Width is < 1 or > 640 || frame.Height is < 1 or > 480 || frame.Gray8.Length != frame.Width * frame.Height || frame.DeviceId != p.ReaderId)
                throw new InvalidOperationException("图像尺寸或相机身份不匹配。");
            var key = frame.Epoch + ":" + frame.FrameNumber;
            if (lastFrame != key)
            {
                var bitmap = BitmapSource.Create(frame.Width, frame.Height, 96, 96, PixelFormats.Gray8, null, frame.Gray8, frame.Width); bitmap.Freeze(); image.Source = bitmap; lastFrame = key;
            }
            previewStatus.Text += $" · {frame.CapturedAt.ToLocalTime():HH:mm:ss.fff} · 帧 {frame.FrameNumber} · {frame.Width}×{frame.Height}";
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (active && version == generation) { image.Source = null; lastFrame = null; previewStatus.Text = "预览不可用：" + error.Message; } }
        finally { polling = false; }
    }
    void ShowSelection()
    {
        openRecord.IsEnabled = scans.SelectedItem is ScanRow { OperationId: not null };
        evidence.Text = scans.SelectedItem is ScanRow row ? row.Evidence : "尚无选中扫描。相机产生新输入后可核对；历史记录请到作业记录查看。";
    }
    static string EvidenceText(ParcelInputSnapshot input, ParcelOperation? op)
    {
        var text = $"扫描身份：{input.Observation.Id}\n时间：{input.Observation.ObservedAt:O}\n候选：{string.Join(" / ", input.Observation.Codes)}\n判定：{input.Reason}\n";
        if (op is null) text += "本次输入未建立对应作业，没有可显示的请求。";
        else
        {
            text += ParcelViewModel.Handover(op, true);
            if (op.Wcs?.Request.IntegrationRequest is { } request) text += "\n\n原请求正文（只读）\n" + Bounded(request.Body);
            if (op.Wcs?.Replies.LastOrDefault()?.HttpResponse is { } response)
            {
                var length = Math.Min(10920, response.BodyBase64.Length); length -= length % 4;
                try { text += $"\n\n原响应 · HTTP {response.StatusCode} · 完整接收：{response.Complete}\n" + Bounded(System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(response.BodyBase64[..length]))) + (length < response.BodyBase64.Length ? "\n[显示截断，原件保留]" : ""); }
                catch (FormatException) { text += "\n原回执编码无法显示，请核对原作业。"; }
            }
        }
        return text.Length > 24576 ? text[..24576] + "\n[本页摘要截断，请打开原作业记录]" : text;
    }
    static string Bounded(string text) => DiagnosticText.Protect(text[..Math.Min(text.Length, 8192)]) + (text.Length > 8192 ? "\n[显示截断，原件保留]" : "");
    static TextBlock Label(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = UiTypography.Label, Foreground = new SolidColorBrush(Color.FromRgb(74, 87, 104)), Margin = new(0, 4, 0, 6) };
    static TextBox ReadOnly(string text) => new() { Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Height = 140, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = UiTypography.Interface };
    static FrameworkElement Section(string? title, params UIElement[] children)
    {
        var body = new StackPanel { Margin = new(0, 12, 0, 8) };
        if (title is not null) body.Children.Add(new TextBlock { Text = title, FontSize = UiTypography.Group, FontWeight = UiTypography.Emphasis, Margin = new(0, 0, 0, 8) });
        foreach (var child in children) body.Children.Add(child);
        return body;
    }
}

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Platform;
namespace Ipc.Application.Sportmaster.Desktop;

internal sealed class StationExceptionsControl : UserControl
{
    sealed record Row(StationExceptionItem Fact)
    {
        public string Key => Fact.Key;
        public string Time => Fact.ObservedAt.ToLocalTime().ToString("MM-dd HH:mm:ss zzz");
        public string Position => StationExceptionText.Position(Fact.Position);
        public string Barcode => Fact.Barcode;
        public string Reason => StationExceptionText.Label(Fact.ReasonCode);
        public string Suggestion => Fact.ReasonCode switch {
            "RcsUnknown" or "RcsTimeout" or "RcsConflict" => "凭原请求交接核对",
            "RcsNotDelivered" => "连接恢复后重新扫描",
            "RcsRejected" => "按原始原因处理",
            "BarcodeRule" or "MultipleCodes" => "核对条码与筛选规则",
            _ => "核对本次观察原因" };
    }
    readonly SupplyStationViewModel vm;
    readonly Func<string, Task> details, recovery;
    readonly ComboBox category = new() { ItemsSource = new[] { "全部格口未确认", "最近50件RCS拒绝", "最近50次识别或准入异常" }, SelectedIndex = 0, MinHeight = 44, MinWidth = 200 };
    readonly TextBlock counts = Text(""), status = Text("正在读取异常"), hint = Text("选中一条记录查看原因和处理建议。"), detail = Text("");
    readonly DataGrid grid = new() { AutoGenerateColumns = false, IsReadOnly = true, EnableRowVirtualization = true, EnableColumnVirtualization = true, CanUserAddRows = false, MinHeight = 160, Height = 320, SelectionMode = DataGridSelectionMode.Single };
    readonly Button next, first, show, restore, copy;
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(2) };
    CancellationTokenSource? lifetime;
    bool busy;
    long generation;
    long busyGeneration;
    long? before, nextSequence;
    public StationExceptionsControl(SupplyStationViewModel vm, Func<string, Task> details, Func<string, Task> recovery)
    {
        this.vm = vm; this.details = details; this.recovery = recovery; FontFamily = UiTypography.Interface; FontSize = UiTypography.Body;
        var body = new StackPanel { Margin = new(24, 20, 24, 16) }; Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        body.Children.Add(new TextBlock { Text = "异常处理", FontSize = UiTypography.PageTitle, FontWeight = UiTypography.Emphasis });
        body.Children.Add(Text("格口未确认的包裹勿重复上件。先凭原请求交接核对；当前内置RCS不提供查询，恢复不会重发。")); body.Children.Add(counts);
        var actions = new WrapPanel(); actions.Children.Add(category); actions.Children.Add(Button("刷新异常", () => Refresh()));
        first = Button("最新一页", () => { before = null; return Refresh(); }); next = Button("下一页未确认", () => { before = nextSequence; return Refresh(); }); actions.Children.Add(first); actions.Children.Add(next); body.Children.Add(actions); body.Children.Add(status);
        foreach (var (label, path) in new[] { ("观察时间（本机时区）", "Time"), ("投递位", "Position"), ("条码", "Barcode"), ("状态 / 原因", "Reason"), ("建议操作", "Suggestion") }) grid.Columns.Add(new DataGridTextColumn { Header = label, Binding = new Binding(path), Width = DataGridLength.Auto });
        var workspace = new Grid(); workspace.ColumnDefinitions.Add(new()); workspace.ColumnDefinitions.Add(new()); workspace.RowDefinitions.Add(new()); workspace.RowDefinitions.Add(new()); body.Children.Add(workspace); workspace.Children.Add(grid);
        var inspector = new StackPanel(); inspector.Children.Add(hint); inspector.Children.Add(detail); workspace.Children.Add(inspector);
        var selected = new WrapPanel(); show = Button("查看详情与核对…", Show); restore = Button("打开恢复操作…", Restore); copy = Button("复制交接信息", Copy); selected.Children.Add(show); selected.Children.Add(restore); selected.Children.Add(copy); inspector.Children.Add(selected);
        workspace.SizeChanged += (_, _) => { var wide = workspace.ActualWidth >= 1000; workspace.ColumnDefinitions[0].Width = new(wide ? 3 : 1, GridUnitType.Star); workspace.ColumnDefinitions[1].Width = wide ? new(2, GridUnitType.Star) : new(0); Grid.SetColumn(inspector, wide ? 1 : 0); Grid.SetRow(inspector, wide ? 0 : 1); inspector.Margin = wide ? new(20, 0, 0, 0) : new(0, 12, 0, 0); };
        grid.SelectionChanged += (_, _) => Select();
        category.SelectionChanged += async (_, _) => { before = null; if (IsLoaded) await Run(() => Refresh()); };
        Loaded += async (_, _) => { lifetime = new(); generation++; vm.PropertyChanged += Changed; await Run(() => Refresh()); timer.Start(); };
        Unloaded += (_, _) => { generation++; timer.Stop(); lifetime?.Cancel(); lifetime?.Dispose(); lifetime = null; vm.PropertyChanged -= Changed; };
        timer.Tick += async (_, _) => { if (!busy) await Run(() => Refresh()); };
        AutomationProperties.SetName(category, "异常类别"); AutomationProperties.SetName(grid, "异常记录，选中后在下方查看原请求和处理建议");
        Select();
    }
    void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e) { if (e.PropertyName is nameof(vm.IsManagementMode) or nameof(vm.Fresh) or nameof(vm.Busy)) Select(); }
    async Task Refresh()
    {
        if (vm.Application is not IStationExceptions api) throw new NotSupportedException("当前业务不提供异常查询");
        var version = generation; var kind = (StationExceptionKind)category.SelectedIndex; var cursor = before; var token = lifetime!.Token;
        var value = await api.ReadExceptionsAsync(new(kind, cursor), token);
        if (!IsLoaded || version != generation || kind != (StationExceptionKind)category.SelectedIndex || cursor != before) return;
        var selected = (grid.SelectedItem as Row)?.Key;
        var scroll = FindScroll(grid); var offset = scroll?.VerticalOffset ?? 0;
        var rows = value.Items.Select(x => new Row(x)).ToArray(); grid.ItemsSource = rows; grid.SelectedItem = rows.FirstOrDefault(x => x.Key == selected);
        scroll?.ScrollToVerticalOffset(offset); nextSequence = value.NextSequence;
        counts.Text = $"格口未确认 {value.UnconfirmedCount:N0} · 保留明细中的RCS拒绝 {value.RejectedCount:N0} · 识别/准入异常 {value.ObservationCount:N0}";
        status.Text = $"更新于 {value.ReadAt.ToLocalTime():HH:mm:ss zzz} · {(kind == StationExceptionKind.Unconfirmed ? "未确认每页最多100件，可逐页查看全部" : "仅显示最近50条；更多记录请到查询页")} · 当前 {rows.Length} 条" + (rows.Length == 0 ? "，暂无此类异常。" : "");
        Select();
    }
    void Select()
    {
        var row = (grid.SelectedItem as Row)?.Fact;
        show.IsEnabled = row?.OperationId is not null && vm.CanStartOperation && !busy;
        restore.IsEnabled = row?.OperationId is not null && row.ReasonCode is "RcsUnknown" or "RcsTimeout" or "RcsConflict" or "RcsNotDelivered" && vm.IsManagementMode && vm.CanStartOperation && !busy;
        copy.IsEnabled = row is not null && !busy;
        next.IsEnabled = nextSequence is not null && category.SelectedIndex == 0 && !busy; first.IsEnabled = before is not null && !busy;
        hint.Text = row is null ? "选中一条记录查看原因和处理建议。" : StationExceptionText.Guidance(row);
        detail.Text = row is null ? "" : Handover(row);
    }
    static string Handover(StationExceptionItem r) => $"观察时间：{r.ObservedAt.ToLocalTime():O}\n投递位：{StationExceptionText.Position(r.Position)} · 条码：{r.Barcode}\n类型：{StationExceptionText.Label(r.ReasonCode)}\n作业号：{r.OperationId ?? "未准入，无作业"}\n原请求号：{r.RequestId ?? "无原请求"}" + (r.ResponseCode is null && r.ResponseMessage is null ? "" : $"\nRCS原始原因：{DiagnosticText.Protect(r.ResponseCode ?? "")} · {DiagnosticText.Protect(r.ResponseMessage ?? "")}") + "\n" + StationExceptionText.Guidance(r);
    async Task Show() { var id = ((grid.SelectedItem as Row)?.Fact.OperationId) ?? throw new InvalidOperationException("本次观察未创建作业，请核对观察原因"); await details(id); }
    async Task Restore() { var id = ((grid.SelectedItem as Row)?.Fact.OperationId) ?? throw new InvalidOperationException("请先选择原作业"); await vm.RequireManagementModeAsync(); await recovery(id); }
    Task Copy() { if ((grid.SelectedItem as Row)?.Fact is not { } row) throw new InvalidOperationException("请先选择一条异常"); Clipboard.SetText(Handover(row)); status.Text = "已复制所选原记录的交接信息。"; return Task.CompletedTask; }
    async Task Run(Func<Task> action)
    {
        if (busy && busyGeneration == generation || lifetime is null) return; var operationGeneration = generation; busyGeneration = operationGeneration; busy = true; category.IsEnabled = false; Select();
        try { await action(); }
        catch (OperationCanceledException) { }
        catch (Exception e) { if (IsLoaded && operationGeneration == generation) status.Text = "异常读取或操作未完成，上次结果保留，请刷新：" + DiagnosticText.Protect(e.Message); }
        finally { if (busyGeneration == operationGeneration) { busy = false; if (IsLoaded) { category.IsEnabled = true; Select(); } } }
    }
    Button Button(string label, Func<Task> run) { var b = new Button { Content = label, MinHeight = 44, Padding = new(12, 4, 12, 4), Margin = new(0, 4, 8, 4) }; b.Click += async (_, _) => await Run(run); return b; }
    static TextBlock Text(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new(0, 6, 0, 6) };
    static ScrollViewer? FindScroll(DependencyObject p) { for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(p); i++) { var c = System.Windows.Media.VisualTreeHelper.GetChild(p, i); if (c is ScrollViewer s) return s; if (FindScroll(c) is { } found) return found; } return null; }
}

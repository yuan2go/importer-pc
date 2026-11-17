using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Diagnostics;
using Ipc.Platform;
using Ipc.Presentation.Wpf;

namespace Ipc.Operations.Desktop;

public partial class OperationsWindow : Window
{
    readonly OperationsInstallation installation;
    readonly OperationsClient client;
    readonly CancellationTokenSource lifetime = new();
    readonly DispatcherTimer freshness = new() { Interval = TimeSpan.FromMilliseconds(250) };
    Task observation = Task.CompletedTask, queryTask = Task.CompletedTask;
    CancellationTokenSource? queryStop;
    SoftwareLogReader? logs;
    SoftwareLogQuery? applied;
    string? selection, logDirectory;
    int offset;
    bool canClose, closing;
    long observedTicks;
    OperationsOverview? current;
    sealed record Area(string Directory, string Label);
    sealed record PositionCard(string Title, string Status, string Detail);

    public OperationsWindow(OperationsInstallation installation)
    {
        InitializeComponent(); UiTypography.Apply(this);
        this.installation = installation; client = new(installation);
        InstallationText.Text = "本站数据：" + installation.Location.DataRoot + "\n业务版本 1.0.0 · 来源 " + installation.Location.SourceCommit;
        LogAreas.DisplayMemberPath = nameof(Area.Label);
        LogAreas.SelectionChanged += FiltersChanged; TimeRange.SelectionChanged += FiltersChanged; Level.SelectionChanged += FiltersChanged;
        SourceFilter.TextChanged += FiltersChanged; Keyword.TextChanged += FiltersChanged;
        KeyEvents.Checked += FiltersChanged; KeyEvents.Unchecked += FiltersChanged;
        Loaded += async (_, _) =>
        {
            observation = Observe();
            freshness.Tick += (_, _) =>
            {
                if (current is not null && (Stopwatch.GetElapsedTime(observedTicks) > OperationsProtocol.Freshness ||
                    DateTimeOffset.UtcNow - current.CapturedAt > OperationsProtocol.Freshness ||
                    current.Devices.Any(d => d.Ready && d.ValidUntil <= DateTimeOffset.UtcNow)))
                    Invalidate("工作站数据已过期，正在重新核验连接。");
            };
            freshness.Start();
            try
            {
                var directories = await Task.Run(installation.LogDirectories, lifetime.Token);
                if (closing) return;
                LogAreas.ItemsSource = directories.Select(p =>
                {
                    var parts = System.IO.Path.GetRelativePath(installation.Location.DataRoot, p).Split(System.IO.Path.DirectorySeparatorChar);
                    return new Area(p, parts[1] + " · " + (parts[0] == "Production" ? "生产日志" : "模拟日志"));
                }).ToArray();
                if (directories.Length > 0) LogAreas.SelectedIndex = 0;
                else LogStatus.Text = "当前安装尚无保留的运行日志。日志空白不能证明未发生业务。";
            }
            catch (OperationCanceledException) when (closing) { }
            catch (Exception error) { LogStatus.Text = "无法读取日志范围：" + DiagnosticText.Protect(error.Message); }
        };
        Closing += ClosingWindow;
    }

    async Task Observe()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            do
            {
                try
                {
                    var value = await Task.Run(() => client.ReadAsync(lifetime.Token), lifetime.Token);
                    if (closing) return;
                    current = value; observedTicks = Stopwatch.GetTimestamp(); ShowOverview(value);
                }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { return; }
                catch (Exception error)
                {
                    Invalidate("未连接工作站，仅可查看落盘日志。请核对工作站是否已打开；未连接不代表已停机。");
                    TechnicalText.Text = "本机连接诊断：\n" + DiagnosticText.Protect(error.ToString());
                }
            } while (await timer.WaitForNextTickAsync(lifetime.Token));
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
    }
    void Invalidate(string reason)
    {
        current = null; ConnectionText.Text = reason;
        OverviewStatus.Text = "实时状态不可用"; OverviewSummary.Text = "请在工作站核对实际供包状态。工具不启动或接管业务。";
        PositionCards.ItemsSource = null; DeviceRows.ItemsSource = null; StorageText.Text = ""; TechnicalText.Clear();
    }
    void ShowOverview(OperationsOverview value)
    {
        ConnectionText.Text = "已核验本机工作站 · 只读权限 · 状态时间 " + value.CapturedAt.ToLocalTime().ToString("HH:mm:ss");
        OverviewStatus.Text = value.Paused ? "工作站当前软件暂停" : "工作站运行中，按各位实际准入供包";
        OverviewSummary.Text = $"站点 {value.Station} · 当前可见作业 {value.VisibleOperations} · 当前可见未确认结果 {value.UnconfirmedOperations} · " +
            (value.PendingConfiguration ? "有待生效设置" : "无待生效设置") +
            $"\n实际执行请求 {value.ExecutingRequests?.ToString() ?? "待确认"} · 历史保管未确认 {value.HistoricalUnknown?.ToString() ?? "待确认"}。原请求责任保留，查看不会重发。";
        PositionCards.ItemsSource = value.Positions.Select(p => new PositionCard(p.Id == "left" ? "左投递位" : p.Id == "right" ? "右投递位" : "投递位",
            (p.Control == "Disabled" ? "已停用" : p.Control == "Disabling" ? "停用中" : p.Admit ? "允许接收包裹" : "当前暂不接收包裹") +
            (p.Manual ? " · 允许人工输入" : " · 人工输入受限"),
            "小车：" + (p.Vehicle ?? "待确认") + " · " + VehicleStatus(p.VehicleStatus) +
                "\n" + PositionNotice(p) + "\n当前轮次：" + (p.RoundId ?? "无") + "\n当前任务：" + (p.OperationId ?? "无"))).ToArray();
        var now = DateTimeOffset.UtcNow;
        DeviceRows.ItemsSource = value.Devices.Select(d => d.Name + "：" +
            (d.Ready && d.ValidUntil > now && d.ConfirmedAt <= now ? "连接确认有效" : "连接待确认") + " · 确认时间 " + d.ConfirmedAt.ToLocalTime().ToString("HH:mm:ss")).ToArray();
        StorageText.Text = value.FreeStorageBytes is { } free ?
            $"工作站最近确认的剩余空间：{free / (1024d * 1024 * 1024):F1} GiB · 最低保留 {value.MinimumFreeBytes / (1024d * 1024 * 1024):F1} GiB。空间数值不代表数据库或全部执行责任已核验。" :
            "存储空间：工作站尚未提供有效数值。请按实际阻断原因核对存储。";
        TechnicalText.Text = "运行实例：" + value.RuntimeId + "\n快照修订：" + value.Revision + "\n" + string.Join("\n", value.Reasons) +
            string.Join("", value.Positions.Select(p => "\n" + (p.Id == "left" ? "左位" : "右位") + "：" + string.Join("；", p.Reasons)));
    }
    static string VehicleStatus(string? value) => value switch { "IDLE" => "空闲", "TASKING" => "工作中", _ => "状态待确认" };
    static string PositionNotice(OperationsPosition position)
    {
        var notices = position.Reasons.Select(r => Enum.TryParse<ParcelInputBlockReason>(r, out var reason) ? ParcelInputReasons.ActionableNotice(reason) : null)
            .OfType<string>().Distinct().ToArray();
        return notices.Length > 0 ? string.Join("\n", notices) : position.Admit ? "按工作站提示正常供包。" : "请查看工作站提示及下方技术详情中的阻断原因。";
    }

    async void QueryClick(object sender, RoutedEventArgs e) => await RunQuery(0, fresh: true);
    async void PreviousClick(object sender, RoutedEventArgs e) => await RunQuery(Math.Max(0, offset - 200), fresh: false);
    async void NextClick(object sender, RoutedEventArgs e) => await RunQuery(offset + 200, fresh: false);
    void CancelClick(object sender, RoutedEventArgs e) => queryStop?.Cancel();
    void FiltersChanged(object sender, RoutedEventArgs e)
    {
        if (selection is null || !queryTask.IsCompleted) return;
        logs?.Release(selection); selection = null; applied = null;
        LogGrid.ItemsSource = null; LogDetail.Clear(); LogNotices.Clear(); PageText.Text = "";
        PreviousButton.IsEnabled = NextButton.IsEnabled = false;
        LogStatus.Text = "筛选条件尚未应用，请点击查询。";
    }
    async Task RunQuery(int requestedOffset, bool fresh)
    {
        if (!queryTask.IsCompleted || closing) return;
        queryTask = Query(requestedOffset, fresh); await queryTask;
    }
    async Task Query(int requestedOffset, bool fresh)
    {
        if (LogAreas.SelectedItem is not Area area) { LogStatus.Text = "没有可查询的运行日志范围。"; return; }
        QueryButton.IsEnabled = PreviousButton.IsEnabled = NextButton.IsEnabled = false; CancelQuery.IsEnabled = true;
        Filters.IsEnabled = LogAreas.IsEnabled = false;
        LogGrid.ItemsSource = null; LogDetail.Clear(); LogNotices.Clear(); PageText.Text = ""; LogStatus.Text = "正在读取固定日志范围…";
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); queryStop = stop;
        try
        {
            if (fresh)
            {
                logs?.Dispose(); logs = null; selection = null; logDirectory = area.Directory;
                var now = DateTimeOffset.Now;
                applied = new(TimeRange.SelectedIndex switch { 1 => new DateTimeOffset(now.Date, now.Offset), 2 => now.AddHours(-24), _ => now.AddHours(-1) },
                    now, Keyword.Text, Level: Level.SelectedIndex == 0 ? "" : ((ComboBoxItem)Level.SelectedItem).Content.ToString()!, Source: SourceFilter.Text)
                    { KeyEvents = KeyEvents.IsChecked == true };
                logs = SoftwareLogReader.OperationalReadOnly(logDirectory, installation.RequireRead);
            }
            if (logs is null || applied is null) throw new InvalidOperationException("请先查询日志。");
            var reader = logs;
            var fixedQuery = applied; var fixedSelection = selection;
            var page = await Task.Run(() => reader.QueryAsync(fixedQuery, fixedSelection, requestedOffset, stop.Token), stop.Token);
            if (stop.IsCancellationRequested) reader.Release(page.Selection);
            stop.Token.ThrowIfCancellationRequested();
            selection = page.Selection; offset = requestedOffset;
            LogGrid.ItemsSource = page.Entries.Select(entry => entry with { At = entry.At?.ToLocalTime() }).ToArray();
            PageText.Text = $"第 {offset / 200 + 1} 页 · 本页 {page.Entries.Length} 条";
            PreviousButton.IsEnabled = offset > 0; NextButton.IsEnabled = page.HasMore;
            LogStatus.Text = "已读取 · 本次时间至 " + applied.ToExclusive.ToLocalTime().ToString("HH:mm:ss") + "。选择记录查看完整原文，支持多选复制。";
            LogNotices.Text = "本次固定范围：" + logDirectory + "\n" + string.Join("\n", page.Notices);
        }
        catch (OperationCanceledException) { LogStatus.Text = "查询已取消，未显示本次结果。"; }
        catch (Exception error) { LogStatus.Text = "日志读取未完成，请重新查询：" + DiagnosticText.Protect(error.Message); }
        finally { queryStop = null; CancelQuery.IsEnabled = false; QueryButton.IsEnabled = Filters.IsEnabled = LogAreas.IsEnabled = !closing; }
    }
    void LogSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        LogDetail.Text = string.Join(Environment.NewLine, LogGrid.SelectedItems.OfType<SoftwareLogEntry>().Select(r => r.Text));
        LogDetail.Visibility = string.IsNullOrEmpty(LogDetail.Text) ? Visibility.Collapsed : Visibility.Visible;
    }
    void CopyClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(LogDetail.Text)) return;
        try { Clipboard.SetText(LogDetail.Text); }
        catch (System.Runtime.InteropServices.COMException) { LogStatus.Text = "剪贴板暂不可用，请稍后重试。"; }
    }
    async void ClosingWindow(object? sender, CancelEventArgs e)
    {
        if (canClose) return;
        e.Cancel = true; if (closing) return;
        closing = true; IsEnabled = false; freshness.Stop(); lifetime.Cancel(); queryStop?.Cancel();
        await Task.WhenAll(observation, queryTask);
        logs?.Dispose(); lifetime.Dispose(); canClose = true; Close();
    }
}

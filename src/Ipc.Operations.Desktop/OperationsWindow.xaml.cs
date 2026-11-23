using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Diagnostics;
using Ipc.Platform;
using Ipc.Presentation.Wpf;
using Ipc.Application.Sportmaster.Desktop;

namespace Ipc.Operations.Desktop;

public partial class OperationsWindow : Window
{
    readonly OperationsInstallation installation;
    readonly OperationsClient client;
    readonly OperationsControlClient control;
    readonly OperationsWorkspaceControl workspace;
    ContentControl? workspaceHost;
    readonly CancellationTokenSource lifetime = new();
    readonly DispatcherTimer freshness = new() { Interval = TimeSpan.FromMilliseconds(250) };
    Task observation = Task.CompletedTask, discovery = Task.CompletedTask, queryTask = Task.CompletedTask;
    CancellationTokenSource? queryStop;
    SoftwareLogReader? logs;
    SoftwareLogQuery? applied;
    string? selection, logDirectory;
    int offset;
    bool canClose, closing;
    long observedTicks;
    OperationsOverview? current;
    sealed record Area(string Directory, string Label);
    sealed record PositionCard(string Title, string Status, bool AllowsSupply,
        string Vehicle, string Round, string Operation, string Manual, string Notice);
    sealed record DeviceCard(string Title, string Status, string Detail, bool Confirmed = false);

    public OperationsWindow(OperationsInstallation installation)
    {
        InitializeComponent(); UiTypography.Apply(this);
        this.installation = installation; client = new(installation);
        control = new(installation);
        workspace = new(new OperationsRemoteApplication(control, new(installation.Location.ConfigurationDirectory, installation.Location.DataRoot)),
            () => control.ConnectAsync(lifetime.Token), () => control.Connected, OfflineMaintenance);
        AuthorizationStatusHost.Content = workspace.AuthorizationStatus;
        AuthorizationActionHost.Content = workspace.AuthorizationAction;
        AuthorizationDetailsHost.Content = workspace.AuthorizationDetails;
        WorkspaceNoticeHost.Content = workspace.Feedback;
        workspace.NavigationRequested += (_, target) =>
        {
            Pages.SelectedIndex = target switch { "debug" => 2, "parameters" => 3, "maintenance" => 4, _ => 1 };
            if (target == "logs") OnlineLogMode.IsChecked = true;
        };
        OnlineLogMode.Checked += (_, _) => ShowOnlineLogs();
        OfflineLogMode.Checked += (_, _) =>
        {
            OnlineLogs.Visibility = Visibility.Collapsed;
            OfflineLogs.Visibility = Visibility.Visible;
        };
        var location = installation.Location;
        var source = string.IsNullOrWhiteSpace(location.SourceCommit) ? "未记录" : location.SourceCommit;
        VersionText.Text = "业务版本 1.0.0";
        InstallationText.Text = $"{VersionText.Text}\n来源提交：{source}\n本站数据：{location.DataRoot}\n启动配置：{location.ConfigurationDirectory}";
        ConnectionDetails.Text = "正在核验本机连接…";
        ShowDeviceCards(null, DateTimeOffset.UtcNow);
        LogAreas.SelectionChanged += FiltersChanged; TimeRange.SelectionChanged += FiltersChanged; Level.SelectionChanged += FiltersChanged;
        SourceFilter.TextChanged += FiltersChanged; Keyword.TextChanged += FiltersChanged;
        KeyEvents.Checked += FiltersChanged; KeyEvents.Unchecked += FiltersChanged;
        Loaded += (_, _) =>
        {
            if (closing) return;
            observation = Observe();
            freshness.Tick += (_, _) =>
            {
                if (current is not null && (Stopwatch.GetElapsedTime(observedTicks) > OperationsProtocol.Freshness ||
                    DateTimeOffset.UtcNow - current.CapturedAt > OperationsProtocol.Freshness ||
                    current.Devices.Any(d => d.Ready && d.ValidUntil <= DateTimeOffset.UtcNow)))
                    Invalidate("状态已过期", "工作站数据已过期，正在重新核验连接。");
            };
            freshness.Start();
            discovery = DiscoverLogs();
        };
        Closing += ClosingWindow;
        Closed += (_, _) => lifetime.Dispose();
    }

    void AttachWorkspace(ContentControl host, string page)
    {
        if (workspaceHost is not null && workspaceHost != host) workspaceHost.Content = null;
        workspaceHost = host; host.Content = workspace; workspace.ShowPage(page);
    }
    void PageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, Pages) || workspace is null) return;
        if (Pages.SelectedIndex == 1 && OnlineLogs.Visibility == Visibility.Visible) { ShowOnlineLogs(); return; }
        if (Pages.SelectedItem is not TabItem { Tag: string page }) return;
        AttachWorkspace(page switch { "debug" => DeviceDebugPage, "parameters" => ProfessionalPage, _ => MaintenancePage }, page);
    }
    void ShowOnlineLogs()
    {
        OnlineLogs.Visibility = Visibility.Visible; OfflineLogs.Visibility = Visibility.Collapsed;
        AttachWorkspace(OnlineLogs, "logs");
    }
    async Task OfflineMaintenance()
    {
        if (MessageBox.Show(this, "将从同版受保护维护缓存打开维护中心，并关闭本工具释放程序文件。维护程序仍核验工作站已真正关闭；不会替您停止生产。", "打开离线维护",
            MessageBoxButton.OKCancel, MessageBoxImage.Information, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
        await workspace.CloseAsync(activeHandoff: true);
        try { await Task.Run(() => WindowsDeploymentSetup.LaunchOperationsMaintenance("sportmaster")); }
        catch { workspace.ResumeAfterFailedHandoff(); throw; }
        _ = Dispatcher.BeginInvoke(new Action(Close));
    }

    async Task DiscoverLogs()
    {
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
                    Invalidate("未连接工作站", "实时状态不可用。可查看离线运行日志。请核对工作站是否已打开；未连接不代表已停机。");
                    TechnicalText.Text = "本机连接诊断：\n" + DiagnosticText.Protect(error.ToString());
                }
            } while (await timer.WaitForNextTickAsync(lifetime.Token));
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
    }
    void SetConnectionStatus(string label, string detail, bool verified)
    {
        ConnectionText.Text = label;
        ConnectionText.ToolTip = ConnectionDetails.Text = detail;
        ConnectionText.SetResourceReference(TextBlock.ForegroundProperty, verified ? "Success" : "Warning");
        ConnectionPill.SetResourceReference(Border.BackgroundProperty, verified ? "SuccessBackground" : "WarningBackground");
    }

    void Invalidate(string label, string reason)
    {
        current = null;
        SetConnectionStatus(label, reason, verified: false);
        StationText.Text = "站点待核验";
        StationText.ClearValue(ToolTipProperty);
        OverviewStatus.Text = "实时状态不可用";
        OverviewStatus.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        VisibleOperationsText.Text = UnconfirmedOperationsText.Text = PendingOverviewText.Text = "待确认";
        OverviewSummary.Text = reason + " 工具不启动或接管业务。";
        PositionCards.ItemsSource = null;
        ShowDeviceCards(null, DateTimeOffset.UtcNow);
        TechnicalText.Clear();
    }

    void ShowOverview(OperationsOverview value)
    {
        SetConnectionStatus("已核验工作站", "本机只读状态连接已核验 · 状态时间 " +
            value.CapturedAt.ToLocalTime().ToString("HH:mm:ss"), verified: true);
        StationText.Text = "站点 " + value.Station;
        StationText.ToolTip = StationText.Text;
        OverviewStatus.Text = value.Paused ? "软件暂停" : "运行中";
        OverviewStatus.SetResourceReference(TextBlock.ForegroundProperty, value.Paused ? "Warning" : "Ink");
        VisibleOperationsText.Text = value.VisibleOperations + " 件";
        UnconfirmedOperationsText.Text = $"{value.UnconfirmedOperations} / {value.HistoricalUnknown?.ToString() ?? "待确认"}";
        PendingOverviewText.Text = value.PendingConfiguration ? "有待生效设置" : "无待生效设置";
        OverviewSummary.Text = $"实际执行请求 {value.ExecutingRequests?.ToString() ?? "待确认"}。按各投递位实际准入供包，原请求责任保留，查看不会重发。";

        PositionCards.ItemsSource = new[]
        {
            MakePositionCard(value, "left", "左投递位"),
            MakePositionCard(value, "right", "右投递位")
        };
        var now = DateTimeOffset.UtcNow;
        ShowDeviceCards(value, now);
        TechnicalText.Text = "运行实例：" + value.RuntimeId + "\n快照修订：" + value.Revision + "\n" + string.Join("\n", value.Reasons) +
            string.Join("", value.Positions.Select(p => "\n" + (p.Id == "left" ? "左位" : "右位") + "：" + string.Join("；", p.Reasons))) +
            string.Join("", value.Devices.Where(d => d.Name is not ("左相机" or "右相机" or "RCS")).Select(d =>
                "\n" + d.Name + "：" + (HasCurrentConfirmation(d, now) ? "连接确认有效" : "连接待确认") +
                " · 确认时间 " + d.ConfirmedAt.ToLocalTime().ToString("HH:mm:ss")));
    }

    static PositionCard MakePositionCard(OperationsOverview overview, string id, string title)
    {
        var position = overview.Positions.FirstOrDefault(p => p.Id == id);
        if (position is null) return new(title, "状态待确认", false, "待确认", "待确认", "待确认", "待确认", "工作站尚未提供本位状态。");
        var status = position.Control switch
        {
            "Disabled" => "已停用",
            "Disabling" => "停用中",
            _ => position.Admit ? "允许接收包裹" : "当前暂不接收包裹"
        };
        return new(title, status, position.Admit && position.Control is not ("Disabled" or "Disabling"),
            (position.Vehicle ?? "待确认") + " · " + VehicleStatus(position.VehicleStatus),
            position.RoundId ?? "无", position.OperationId ?? "无", position.Manual ? "允许" : "受限", PositionNotice(position));
    }

    void ShowDeviceCards(OperationsOverview? overview, DateTimeOffset now)
    {
        DeviceCard Connection(string name)
        {
            var device = overview?.Devices.FirstOrDefault(d => d.Name == name);
            if (device is null) return new(name, "连接待确认", "尚无设备确认。");
            var confirmed = HasCurrentConfirmation(device, now);
            return new(name, confirmed ? "连接确认有效" : "连接待确认",
                "确认时间 " + device.ConfirmedAt.ToLocalTime().ToString("HH:mm:ss"), confirmed);
        }

        var storage = overview?.FreeStorageBytes is { } free
            ? new DeviceCard("存储", $"剩余 {free / (1024d * 1024 * 1024):F1} GiB",
                $"最低保留 {overview.MinimumFreeBytes / (1024d * 1024 * 1024):F1} GiB。空间数值不代表数据库或全部执行责任已核验。")
            : new DeviceCard("存储", "剩余空间待确认", "工作站尚未提供有效数值。请按实际阻断原因核对存储。");
        DeviceRows.ItemsSource = new[] { Connection("左相机"), Connection("右相机"), Connection("RCS"), storage };
    }

    static bool HasCurrentConfirmation(OperationsDevice device, DateTimeOffset now) =>
        device.Ready && device.ValidUntil > now && device.ConfirmedAt <= now;

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
        closing = true; IsEnabled = false;
        try
        {
            await workspace.CloseAsync();
            freshness.Stop(); lifetime.Cancel(); queryStop?.Cancel();
            await Task.WhenAll(observation, discovery, queryTask);
            await control.DisposeAsync();
            logs?.Dispose(); canClose = true;
            // Even an already-completed drain must return from the first Closing
            // event before WPF accepts another Close (e.g. before Loaded ran).
            await Dispatcher.InvokeAsync(Close);
        }
        catch (Exception error)
        {
            canClose = false; closing = false; IsEnabled = true;
            Invalidate("查看已停止", "读取资源尚未确认释放。请再次关闭窗口；生产状态未被修改。");
            TechnicalText.Text = DiagnosticText.Protect(error.ToString());
        }
    }
}

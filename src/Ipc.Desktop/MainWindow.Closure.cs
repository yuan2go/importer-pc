using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Ipc.Platform;
using Ipc.Supply;

namespace Ipc.Desktop;

public sealed record HistoryStatusChoice(string? Value, string Label);
public sealed record ObservationReviewRow(ObservationReviewSelection Selection)
{
    public string Label => $"{Selection.Scope} · {Selection.Source} · 代次 {Selection.SourceEpoch} · 证据版本 {Selection.EvidenceVersion}";
    public override string ToString() => Label;
}

public partial class MainWindow
{
    CancellationTokenSource soundStop = new();
    Task? soundTask;
    readonly ObservableCollection<ObservationReviewRow> sourceReviews = [];
    long historyGeneration;
    bool updatingHistory, navigatingDirectDetail;
    string? historySessionId;
    Ipc.Supply.HistoryPage? loadedHistory;
    void StartNotificationPlayer()
    {
        if (!ownsLifetime || soundTask is not null || Runtime.Application is null) return;
        soundTask = new OperatorNotificationPlayer(Runtime.Application,
            cue => model.SoundStatus = $"{(cue.Lane == "left" ? "左" : "右")}通道 · {OperatorText.Operation(cue.Status)}",
            error => { model.SoundStatus = "提示音待处理"; model.LastResult = error; }).RunAsync(soundStop.Token);
    }
    void ApplyClosureProjection(StationSnapshot current)
    {
        var now = DateTimeOffset.UtcNow;
        model.Station = "工位 " + current.StationId;
        string Health(string name, DeviceState? health) => name + " " +
            (health?.IsValid(now) == true ? "就绪" : health is null ? "未取得" : OperatorText.Health(health.Health.ToString()) + "/" + health.ValidityReason(now)) +
            " · " + (health?.ConfirmedAt?.ToLocalTime().ToString("HH:mm:ss") ?? "未确认");
        var service = current.State.IntegrationServiceHealth;
        var wcs = service is null ? Health("WCS", current.State.IntegrationHealth) : "WCS " + (service.IsValid(now) ? "就绪" : service.ValidityReason(now)) + " · " + service.ConfirmedAt.ToLocalTime().ToString("HH:mm:ss");
        model.HealthSummary = string.Join("   ", Health("CAM0", current.State.Devices.GetValueOrDefault("left")),
            Health("CAM1", current.State.Devices.GetValueOrDefault("right")), wcs);
        model.OtherLane = $"CAM0：{(current.State.Lanes.GetValueOrDefault("left") is { } l ? OperatorText.Operation(l.Status) : "等待实体")}\nCAM1：{(current.State.Lanes.GetValueOrDefault("right") is { } r ? OperatorText.Operation(r.Status) : "等待实体")}";
        var selected = ObservationReviews.SelectedItems.Cast<ObservationReviewRow>().Select(x => x.Selection).ToHashSet();
        var rows = current.State.Blocks.Where(x => x.Code == "ObservationReview")
            .Select(x => new ObservationReviewRow(new(x.Code, x.Scope, x.Source, x.SourceEpoch, x.EvidenceVersion))).ToArray();
        if (!sourceReviews.Select(x => x.Selection).SequenceEqual(rows.Select(x => x.Selection)))
        {
            sourceReviews.Clear(); foreach (var row in rows) sourceReviews.Add(row);
            foreach (var row in sourceReviews.Where(x => selected.Contains(x.Selection))) ObservationReviews.SelectedItems.Add(row);
        }
        ObservationReviews.ItemsSource = sourceReviews;
    }
    async void NavigateToHistoryDetail()
    {
        navigatingDirectDetail = true;
        try { await NavigateToViewAsync("history"); ShowHistoryDetail(); }
        finally { navigatingDirectDetail = false; }
    }
    public async Task SubmitLaneAsync(LaneViewModel lane)
    {
        if (lane.Current is not { } operation || !ReviewFits(lane) || !IsWorkstationActive) { model.LastResult="请完整核对本通道当前Item、任务和数量"; return; }
        // Capture this owner and station revision before any risk dialog. The application CAS
        // rejects changed observations/owners; normal manual supply needs no repeated dialog.
        await SendAsync("Submit", operation.Id, confirm: operation.Status == OperationStatus.SplitRequired ?
            $"{lane.Label} · 作业 {operation.Id} / r{operation.Revision}\nItem {lane.Barcode}\n{lane.KeyTask}\n{lane.Quantities}\n{lane.SplitProgress} / {lane.Remove}\n已核对实际保留件与移除量，确认按本版拆包结果提交供包？" : null);
    }
    async Task ReviewSelectedSourcesAsync()
    {
        var selected = ObservationReviews.SelectedItems.Cast<ObservationReviewRow>().Select(x => x.Selection).ToArray();
        if (selected.Length == 0) { model.LastResult = "请选择已检查的具体来源与证据版本"; return; }
        var evidence = new ObservationReviewEvidence(selected, ReviewEvidence.Text);
        await SendAsync("ReviewObservations", value: Wire.Encode(evidence),
            confirm: "仅确认所选来源及当前证据版本已核对？新证据、其它来源、外部预留与实体责任不会被解除。");
    }
    async void ReconnectSourceClick(object sender, RoutedEventArgs e)
    {
        if (!CanCommand("ReconnectSource", out var reason)) { model.LastResult = reason; return; }
        var lane = Selected(EngineeringLane);
        var binding = Runtime.Configuration?.Devices.SingleOrDefault(x => x.Role == lane);
        if (binding is null || !Confirmation.Ask(this, "重连所选模拟来源？原实体许可将因连接代次变化而失效，旧请求身份与未决结果保留。")) return;
        await RunAsync(() => Runtime.RestartSourceAsync(binding.DeviceId));
    }
    void InitializeHistoryFilters()
    {
        HistoryStatusChoice[] Choices<T>(Func<T, string> label) where T : struct, Enum =>
            new[] { new HistoryStatusChoice(null, "全部") }.Concat(Enum.GetValues<T>().Select(x => new HistoryStatusChoice(x.ToString(), label(x)))).ToArray();
        SessionStatusFilter.ItemsSource = Choices<SessionStatus>(OperatorText.Session);
        OperationStatusFilter.ItemsSource = Choices<OperationStatus>(OperatorText.Operation);
        RequestStatusFilter.ItemsSource = Choices<RequestStatus>(OperatorText.Request);
        SessionStatusFilter.SelectedIndex = OperationStatusFilter.SelectedIndex = RequestStatusFilter.SelectedIndex = 0;
        HistoryTimezone.Text = "本地时区：" + TimeZoneInfo.Local.DisplayName + "；时间范围按所选日期的偏移转换";
    }
    static T? HistoryStatus<T>(ComboBox box) where T : struct, Enum =>
        (box.SelectedItem as HistoryStatusChoice)?.Value is { } value ? Enum.Parse<T>(value) : null;
    DateTimeOffset? HistoryDate(DatePicker day, TextBox time)
    {
        if (!string.IsNullOrWhiteSpace(day.Text) && day.SelectedDate is null) throw new ArgumentException("请选择有效日期");
        return HistoryTimeRange.Parse(day.SelectedDate, time.Text, TimeZoneInfo.Local);
    }
    HistoryQuery BuildHistoryQuery()
    {
        var from = HistoryDate(HistoryFromDate, HistoryFrom); var until = HistoryDate(HistoryUntilDate, HistoryUntil);
        HistoryTimeRange.Validate(from, until);
        return new(
            Entity: HistoryEntityBox.SelectedIndex == 1 ? HistoryEntity.Operations : HistoryEntity.Sessions,
            SessionId: historySessionId, BoxBarcode: FilterBox.Text.Trim(), ItemBarcode: ItemFilter.Text.Trim(), Sku: SkuFilter.Text.Trim(), TaskId: TaskFilter.Text.Trim(),
            SessionStatus: HistoryStatus<SessionStatus>(SessionStatusFilter), OperationStatus: HistoryStatus<OperationStatus>(OperationStatusFilter),
            RequestStatus: HistoryStatus<RequestStatus>(RequestStatusFilter), Lane: HistoryLaneBox.SelectedIndex switch { 1 => "left", 2 => "right", _ => null },
            FromInclusive: from, UntilExclusive: until, Offset: historyOffset, Limit: 50);
    }
    bool historyDetailOpen;
    void ShowHistoryDetail()
    { historyDetailOpen = true; ApplyHistoryLayout(); }
    void ApplyHistoryLayout()
    {
        HistoryList.Visibility = historyDetailOpen ? Visibility.Collapsed : Visibility.Visible;
        HistoryDetail.Visibility = historyDetailOpen ? Visibility.Visible : Visibility.Collapsed;
    }
    void BackToHistoryClick(object sender, RoutedEventArgs e)
    { detailGeneration++; historyDetailOpen = false; ApplyHistoryLayout(); HistoryGrid.Focus(); }
    async Task LoadHistoryAsync(HistoryQuery? requestedQuery = null)
    {
        if (Runtime.Application is null || closing) return;
        if (!BeginHistoricalRead("作业历史", out var readActor)) return;
        var generation = ++historyGeneration;
        detailGeneration++;
        var selected = (HistoryGrid.SelectedItem as HistoryQueryRow)?.Id;
        var scroll = Children<ScrollViewer>(HistoryGrid).FirstOrDefault();
        var vertical = scroll?.VerticalOffset ?? 0; var horizontal = scroll?.HorizontalOffset ?? 0;
        HistoryFeedback.Text = "正在查询；已有列表为上次结果";
        HistoryNext.IsEnabled = HistoryPrevious.IsEnabled = false;
        try
        {
            var query = requestedQuery ?? BuildHistoryQuery();
            var page = await Runtime.Application.QueryHistoryAsync(query).WaitAsync(lifetime.Token);
            if (closing || generation != historyGeneration || !ReadIdentityMatches(readActor)) return;
            HistoryGrid.IsEnabled = true;
            ReadAccessFeedback.Text="历史为本次有效身份取得的只读快照；退出或过期后须重新查询。";
            loadedHistory = page;
            updatingHistory = true;
            try
            {
                var rows = page.Items.Select(x => new HistoryQueryRow(x)).ToArray();
                HistoryGrid.ItemsSource = rows;
                HistoryGrid.SelectedItem = rows.FirstOrDefault(x => x.Id == selected);
                if (HistoryGrid.SelectedItem is null) { detailGeneration++; DetailTree.Items.Clear(); }
            }
            finally { updatingHistory = false; }
            if (scroll is not null) { scroll.ScrollToVerticalOffset(vertical); scroll.ScrollToHorizontalOffset(horizontal); }
            HistoryFeedback.Text = page.TotalCount == 0 ? "所选条件暂无记录" :
                $"已取得 {page.Items.Length} / {page.TotalCount} 条 · 偏移 {page.Query.Offset}" +
                (page.Query.SessionId is null ? "" : " · 限定箱会话 " + page.Query.SessionId) +
                (HistoryGrid.SelectedItem is null ? "" : "；所选详情保留原阅读快照，可点击记录刷新详情");
            HistoryPrevious.IsEnabled = page.Query.Offset > 0; HistoryNext.IsEnabled = page.HasNext;
        }
        catch (OperationCanceledException) when (closing) { }
        catch (Exception ex)
        {
            if (closing || generation != historyGeneration) return;
            loadedHistory = null; HistoryGrid.IsEnabled = false;
            HistoryFeedback.Text = "查询失败：" + ex.Message + "；下方保留的是旧结果，未作为本次查询成功";
        }
    }
    async void HistoryClick(object sender, RoutedEventArgs e) { historySessionId = null; historyOffset = 0; await LoadHistoryAsync(); }
    async void NextClick(object sender, RoutedEventArgs e) { if (loadedHistory?.HasNext != true) return; historyOffset = loadedHistory.Query.Offset + loadedHistory.Query.Limit; await LoadHistoryAsync(loadedHistory.Query with { Offset = historyOffset }); }
    async void PreviousClick(object sender, RoutedEventArgs e) { if (loadedHistory is null) return; historyOffset = Math.Max(0, loadedHistory.Query.Offset - loadedHistory.Query.Limit); await LoadHistoryAsync(loadedHistory.Query with { Offset = historyOffset }); }
    async void HistorySelected(object sender, SelectionChangedEventArgs e)
    {
        if (updatingHistory) return;
        switch ((sender as DataGrid)?.SelectedItem)
        {
            case HistoryQueryRow row when row.IsSession: await ShowSessionAsync(row.SessionId); break;
            case HistoryQueryRow row: await ShowDetailAsync(row.Id, row.Operation); break;
            case HistoryRow recent: await ShowDetailAsync(recent.Id, recent.Operation); break;
            default: detailGeneration++; break;
        }
    }
    async void SelectedRecordDetailClick(object sender, RoutedEventArgs e)
    {
        if (HistoryGrid.SelectedItem is not HistoryQueryRow row) { model.LastResult = "请选择记录"; return; }
        if (row.IsSession) await ShowSessionAsync(row.SessionId); else await ShowDetailAsync(row.Id, row.Operation);
    }
    async void SessionOperationsClick(object sender, RoutedEventArgs e)
    {
        if (HistoryGrid.SelectedItem is not HistoryQueryRow row) return;
        historySessionId = row.SessionId; historyOffset = 0; HistoryEntityBox.SelectedIndex = 1;
        FilterBox.Text = ItemFilter.Text = SkuFilter.Text = TaskFilter.Text = ""; HistoryFromDate.SelectedDate = HistoryUntilDate.SelectedDate = null; HistoryFrom.Text = HistoryUntil.Text = "00:00";
        historyDetailOpen = false; ApplyHistoryLayout();
        SessionStatusFilter.SelectedIndex = OperationStatusFilter.SelectedIndex = RequestStatusFilter.SelectedIndex = HistoryLaneBox.SelectedIndex = 0;
        await LoadHistoryAsync();
    }
    async void SelectedSessionClick(object sender, RoutedEventArgs e)
    { if (HistoryGrid.SelectedItem is HistoryQueryRow row) await ShowSessionAsync(row.SessionId); }
    async Task ShowSessionAsync(string sessionId)
    {
        if (Runtime.Application is null || closing) return;
        if (!BeginHistoricalRead("箱会话详情", out var readActor)) return;
        var generation = ++detailGeneration;
        try
        {
            var detail = await Runtime.Application.SessionDetailsAsync(sessionId).WaitAsync(lifetime.Token);
            if (closing || generation != detailGeneration || !ReadIdentityMatches(readActor)) return;
            DetailTree.IsEnabled=true; ReadAccessFeedback.Text="箱关闭链为本次有效身份取得的只读快照。";
            RenderDetails(sessionId, null, detail.Requests);
            if (DetailTree.Items[0] is TreeViewItem root)
            {
                root.Header = $"箱 {detail.Session.Barcode} · {OperatorText.Session(detail.Session.Status)} · {HistoryQueryRow.FormatDate(detail.OpenedAt)}";
                root.Items.Insert(0, new TextBlock { Text = $"作业 {detail.OperationCount} · 唯一接受量 {detail.AcceptedQuantity}；可导航全部作业，以下为验箱与关闭链。", TextWrapping = TextWrapping.Wrap });
                var audit = new TreeViewItem { Header = "箱会话审计", IsExpanded = false };
                foreach (var entry in detail.Audit) audit.Items.Add($"{entry.At:O} · {entry.Kind} · {entry.Actor} · {entry.Evidence}");
                root.Items.Add(audit);
            }
            NavigateToHistoryDetail();
        }
        catch (OperationCanceledException) when (closing) { }
        catch (Exception ex) { if (!closing && generation == detailGeneration) MarkDetailReadFailure("箱会话详情读取失败：" + ex.Message); }
    }
}

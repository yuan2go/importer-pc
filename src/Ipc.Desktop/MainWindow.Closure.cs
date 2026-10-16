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
    string activeLane = "left";
    LaneViewModel ActiveLane => activeLane == "left" ? left : right;
    CancellationTokenSource soundStop = new();
    Task? soundTask;
    readonly ObservableCollection<ObservationReviewRow> sourceReviews = [];
    long historyGeneration;
    bool updatingHistory, navigatingDirectDetail;
    string? historySessionId;
    Ipc.Supply.HistoryPage? loadedHistory;
    bool ReviewFits => ActionReviewScroll is not null && ActionReviewScroll.ViewportHeight > 0 &&
        ActionReviewScroll.ScrollableHeight < 0.5 && ActionReviewScroll.ScrollableWidth < 0.5 &&
        ClientArea.ActualWidth >= 800 && ClientArea.ActualHeight >= 420;

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
        model.HealthSummary = string.Join("   ", Health("CAM0", current.State.Devices.GetValueOrDefault("left")),
            Health("CAM1", current.State.Devices.GetValueOrDefault("right")), Health("WCS", current.State.IntegrationHealth));
        var other = current.State.Lanes.GetValueOrDefault(activeLane == "left" ? "right" : "left");
        model.OtherLane = $"{(activeLane == "left" ? "右" : "左")}通道：{(other is null ? "等待实体" : OperatorText.Operation(other.Status))}";
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
    void NavigateToHistoryDetail()
    {
        navigatingDirectDetail = true;
        try { Pages.SelectedItem = HistoryPage; }
        finally { navigatingDirectDetail = false; }
    }
    void OpenRecoveryClick(object sender, RoutedEventArgs e) => Pages.SelectedItem = RecoveryPage;
    void SelectLaneClick(object sender, RoutedEventArgs e)
    {
        activeLane = (string)((Button)sender).CommandParameter;
        PinnedReview.DataContext = ActiveLane;
        if (snapshot is not null) ApplyClosureProjection(snapshot);
        UpdateGuards();
    }
    void ReviewScrollChanged(object sender, ScrollChangedEventArgs e) => UpdateGuards();
    async Task ConfirmActiveSubmitAsync(LaneViewModel lane)
    {
        if (lane.Lane != activeLane || lane.Current is not { } operation) return;
        await SendAsync("Submit", operation.Id, confirm:
            $"{lane.Label} · 作业 {operation.Id} / r{operation.Revision}\nItem {lane.Barcode}\n{lane.KeyTask}\n{lane.Quantities}\n{lane.SplitProgress}\n确认当前实体与数量后提交供包？");
    }
    async void PinnedScanClick(object sender, RoutedEventArgs e) => await SendAsync("ScanRetained", ActiveLane.Current?.Id, ActiveLane.RetainedDraft.TrimEnd('\r', '\n', '\t'));
    async void PinnedClearClick(object sender, RoutedEventArgs e) => await SendAsync("ClearSplit", ActiveLane.Current?.Id, "CONFIRM", "清空当前作业尚未提交的保留件计数？历史与实体责任保留。");
    async void PinnedRetainedKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Tab) || Keyboard.Modifiers != ModifierKeys.None) return;
        e.Handled = true;
        if (ActiveLane.CanScan && ActiveLane.RetainedDraft.Length > 0)
            await SendAsync("ScanRetained", ActiveLane.Current?.Id, ActiveLane.RetainedDraft.TrimEnd('\r', '\n', '\t'));
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
    }
    static T? HistoryStatus<T>(ComboBox box) where T : struct, Enum =>
        (box.SelectedItem as HistoryStatusChoice)?.Value is { } value ? Enum.Parse<T>(value) : null;
    static DateTimeOffset? HistoryDate(TextBox field)
    {
        var text = field.Text.Trim(); if (text.Length == 0) return null;
        if (!Regex.IsMatch(text, @"(?:Z|[+-]\d{2}:\d{2})$") ||
            !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw new ArgumentException("日期必须包含完整日期和时区，例如2026-10-16T00:00:00+08:00");
        return date;
    }
    HistoryQuery BuildHistoryQuery() => new(
        Entity: HistoryEntityBox.SelectedIndex == 1 ? HistoryEntity.Operations : HistoryEntity.Sessions,
        SessionId: historySessionId, BoxBarcode: FilterBox.Text.Trim(), ItemBarcode: ItemFilter.Text.Trim(), Sku: SkuFilter.Text.Trim(),
        SessionStatus: HistoryStatus<SessionStatus>(SessionStatusFilter), OperationStatus: HistoryStatus<OperationStatus>(OperationStatusFilter),
        RequestStatus: HistoryStatus<RequestStatus>(RequestStatusFilter), Lane: HistoryLaneBox.SelectedIndex switch { 1 => "left", 2 => "right", _ => null },
        FromInclusive: HistoryDate(HistoryFrom), UntilExclusive: HistoryDate(HistoryUntil), Offset: historyOffset, Limit: 50);
    async Task LoadHistoryAsync(HistoryQuery? requestedQuery = null)
    {
        if (Runtime.Application is null || closing) return;
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
            if (closing || generation != historyGeneration) return;
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
            loadedHistory = null;
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
    async void SessionOperationsClick(object sender, RoutedEventArgs e)
    {
        if (HistoryGrid.SelectedItem is not HistoryQueryRow row) return;
        historySessionId = row.SessionId; historyOffset = 0; HistoryEntityBox.SelectedIndex = 1;
        FilterBox.Text = ItemFilter.Text = SkuFilter.Text = HistoryFrom.Text = HistoryUntil.Text = "";
        SessionStatusFilter.SelectedIndex = OperationStatusFilter.SelectedIndex = RequestStatusFilter.SelectedIndex = HistoryLaneBox.SelectedIndex = 0;
        await LoadHistoryAsync();
    }
    async void SelectedSessionClick(object sender, RoutedEventArgs e)
    { if (HistoryGrid.SelectedItem is HistoryQueryRow row) await ShowSessionAsync(row.SessionId); }
    async Task ShowSessionAsync(string sessionId)
    {
        if (Runtime.Application is null || closing) return;
        var generation = ++detailGeneration;
        try
        {
            var detail = await Runtime.Application.SessionDetailsAsync(sessionId).WaitAsync(lifetime.Token);
            if (closing || generation != detailGeneration) return;
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
        catch (Exception ex) { if (!closing && generation == detailGeneration) HistoryFeedback.Text = "箱会话详情读取失败：" + ex.Message; }
    }
}

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Ipc.Platform;
using Ipc.Product;
using Ipc.Supply;
using Microsoft.Win32;

namespace Ipc.Desktop;
public partial class MainWindow : Window
{
    public ProductRuntime Runtime { get; }
    readonly bool ownsLifetime;
    readonly StationViewModel model = new();
    readonly LaneViewModel left = new("left"), right = new("right");
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    readonly ObservableCollection<Alarm> alarms = [];
    StationSnapshot? snapshot;
    bool refreshing, closing, compact;
    int historyOffset, alarmHistoryOffset;
    Alarm? selectedAlarm;
    readonly Dictionary<string, OperationStatus> audible = [];
    DateTimeOffset lastSound;
    public MainWindow(ProductRuntime runtime, bool ownsLifetime = false)
    {
        Runtime = runtime; this.ownsLifetime = ownsLifetime; InitializeComponent(); DataContext = model;
        Title = "供包参考工位 · " + (runtime.Mode?.ToString() ?? "NotReady");
        if (runtime.Mode != RunMode.Simulation) { LeftLane.IsEnabled = false; RightLane.IsEnabled = false; }
        if (runtime.Application is null) model.GlobalStatus = "NotReady · " + string.Join("; ", runtime.Diagnostics);
        LeftLane.DataContext = left; RightLane.DataContext = right; AlarmGrid.ItemsSource = alarms;
        ConfigDraft.Text = runtime.Configuration is null ? "" : Wire.Encode(runtime.Configuration);
        compact = runtime.Presentation.Layout == "compact";
        timer.Tick += async (_, _) => await RefreshAsync();
        Loaded += async (_, _) => { timer.Start(); await RefreshAsync(); };
        Closed += (_, _) => timer.Stop();
    }
    async Task RefreshAsync()
    {
        if (refreshing) return; refreshing = true;
        try
        {
            if (Runtime.Application is null) { model.GlobalStatus = "NotReady · " + string.Join("; ", Runtime.Diagnostics); return; }
            snapshot = await Runtime.Application.SnapshotAsync(); left.Apply(snapshot); right.Apply(snapshot);
            if (ownsLifetime)
                foreach (var op in snapshot.State.Lanes.Values)
                {
                    if (audible.GetValueOrDefault(op.Id, OperationStatus.Observed) != op.Status && DateTimeOffset.UtcNow - lastSound > TimeSpan.FromMilliseconds(800))
                    {
                        if (op.Status == OperationStatus.Accepted) { System.Media.SystemSounds.Asterisk.Play(); lastSound = DateTimeOffset.UtcNow; }
                        else if (op.Status is OperationStatus.Rejected or OperationStatus.ValidationRejected) { System.Media.SystemSounds.Hand.Play(); lastSound = DateTimeOffset.UtcNow; }
                        else if (op.Status == OperationStatus.SplitRequired) { System.Media.SystemSounds.Question.Play(); lastSound = DateTimeOffset.UtcNow; }
                    }
                    audible[op.Id] = op.Status;
                }
            model.Identity = snapshot.State.Actor?.Id ?? "未登录";
            model.Context = snapshot.State.Session is null ? "等待箱" : $"箱 {snapshot.State.Session.Barcode} · {snapshot.State.Session.Status}";
            var reasons = snapshot.Admit.Reasons.Concat(snapshot.OperationGuards.Where(x => snapshot.State.Lanes[x.Key].Status is OperationStatus.AwaitingAction or OperationStatus.SplitRequired or OperationStatus.SubmitPending).SelectMany(x => x.Value.Reasons.Select(r => x.Key + ":" + r))).Concat(snapshot.RuntimeFaults).Distinct().ToArray();
            model.GlobalStatus = "SIMULATION · " + (reasons.Length == 0 ? "可继续入场" : "阻断：" + string.Join("；", reasons));
            model.Devices = string.Join("\n", snapshot.State.Devices.Select(x => $"{x.Key}: {x.Value.DeviceId} / {x.Value.Health} / epoch {x.Value.Epoch}"));
            model.AlarmSummary = $"活动报警 {snapshot.ActiveAlarmCount} · 已读仍计入活动";
            model.RecoveryHint = snapshot.OverloadRecovery.Allowed ? "已排空；检查来源和实体后可确认过载。其它阻断仍保留。" : string.Join("；", snapshot.OverloadRecovery.Reasons);
            OverloadButton.IsEnabled = snapshot.OverloadRecovery.Allowed;
            // Keep active identity/selection stable; resolved occurrences leave only this collection.
            foreach (var old in alarms.Where(x => !snapshot.Alarms.Any(a => a.Id == x.Id)).ToArray()) alarms.Remove(old);
            foreach (var alarm in snapshot.Alarms.Reverse())
            {
                var old = alarms.FirstOrDefault(x => x.Id == alarm.Id);
                if (old is null) alarms.Add(alarm); else if (old != alarm) alarms[alarms.IndexOf(old)] = alarm;
            }
            var selectedRequest = (RecoveryGrid.SelectedItem as RequestRecoveryRow)?.Id;
            var recoveryRows = snapshot.Recovery.Select(x => new RequestRecoveryRow(x)).ToArray();
            if (RecoveryGrid.ItemsSource is not RequestRecoveryRow[] oldRows || Wire.Encode(oldRows) != Wire.Encode(recoveryRows))
            {
                RecoveryGrid.ItemsSource = recoveryRows;
                RecoveryGrid.SelectedItem = recoveryRows.FirstOrDefault(x => x.Id == selectedRequest);
            }
            CancelPreparedButton.IsEnabled = (RecoveryGrid.SelectedItem as RequestRecoveryRow)?.Recovery.LocalTermination.Allowed == true;
        }
        catch (Exception ex) { model.GlobalStatus = "状态读取失败 · 禁止业务：" + ex.Message; }
        finally { refreshing = false; }
    }
    public async Task RunAsync(Func<Task> action)
    {
        try { await action(); model.LastResult = "操作已处理，请核对当前状态"; }
        catch (Exception ex) { model.LastResult = "操作未完成：" + ex.Message; }
        await RefreshAsync();
    }
    public async Task SendAsync(string kind, string? owner = null, string? value = null, string? confirm = null)
    {
        var revision = snapshot?.State.Revision ?? 0;
        if (confirm is not null && !Confirmation.Ask(this, confirm)) return;
        // Confirmation binds to the snapshot the operator reviewed. A changing revision returns Conflict.
        try { var receipt = await Runtime.CommandAsync(new(Wire.Id(), revision, kind, owner, value)); model.LastResult = receipt.Status + " · " + receipt.Reason; }
        catch (Exception ex) { model.LastResult = "命令失败：" + ex.Message; }
        await RefreshAsync();
    }
    async void OperatorClick(object sender, RoutedEventArgs e) => await SendAsync("Login", value: "operator");
    async void MaintenanceClick(object sender, RoutedEventArgs e) => await SendAsync("Login", value: "maintenance");
    async void LogoutClick(object sender, RoutedEventArgs e) => await SendAsync("Logout", confirm: "登出将阻断新业务；原作业责任与未决请求仍保留。确认登出？");
    async void OpenClick(object sender, RoutedEventArgs e) => await SendAsync("OpenContext", value: model.BoxDraft);
    async void PauseClick(object sender, RoutedEventArgs e) => await SendAsync("Pause", confirm: "暂停新入场？已交给网络的请求仍需接收结果。软件暂停不是安全急停。");
    async void ResumeClick(object sender, RoutedEventArgs e) => await SendAsync("Resume");
    async void CheckClick(object sender, RoutedEventArgs e) => await SendAsync("CheckClose", confirm: "建立两通道关闭屏障并核对剩余需求？");
    async void CancelCloseClick(object sender, RoutedEventArgs e) => await SendAsync("CancelClose");
    async void ConfirmCloseClick(object sender, RoutedEventArgs e) => await SendAsync("ConfirmClose", snapshot?.State.Session?.CloseCheckRequest, "CONFIRM", $"按当前检查关闭箱？剩余需求：{snapshot?.State.Session?.CloseRemaining}。确认只代表提交关闭请求。");
    void SecondViewClick(object sender, RoutedEventArgs e) => new MainWindow(Runtime) { Owner = this }.Show();
    async void LayoutClick(object sender, RoutedEventArgs e) { compact = !compact; ApplyLayout(); await RunAsync(() => Runtime.SavePresentationAsync(new("workstation", compact ? "compact" : "standard"))); }
    void WindowSizeChanged(object sender, SizeChangedEventArgs e) => ApplyLayout();
    void ApplyLayout()
    {
        if (LeftLane is null) return;
        var margin = compact || ActualWidth < 1280 ? 4 : 16;
        LeftLane.Margin = new Thickness(margin); RightLane.Margin = new Thickness(margin);
        LeftLane.FontSize = RightLane.FontSize = compact ? 14 : 16;
    }
    void WindowKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter && Keyboard.FocusedElement is not TextBox { AcceptsReturn: true }) e.Handled = true; }
    async void TabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source is not TabControl) return;
        if (HistoryGrid is not null && HistoryGrid.ItemsSource is null) await LoadHistoryAsync();
    }
    async Task LoadHistoryAsync() => await RunAsync(async () => { if (Runtime.Application is not null) HistoryGrid.ItemsSource = await Runtime.Application.HistoryAsync(FilterBox.Text.Length == 0 ? null : FilterBox.Text, historyOffset, 50); });
    async void HistoryClick(object sender, RoutedEventArgs e) { historyOffset = 0; await LoadHistoryAsync(); }
    async void NextClick(object sender, RoutedEventArgs e) { historyOffset += 50; await LoadHistoryAsync(); }
    async void PreviousClick(object sender, RoutedEventArgs e) { historyOffset = Math.Max(0, historyOffset - 50); await LoadHistoryAsync(); }
    async void HistorySelected(object sender, SelectionChangedEventArgs e) { if (HistoryGrid.SelectedItem is SupplyOperation op) await ShowDetailAsync(op.Id); }
    public async Task ShowDetailAsync(string owner)
    {
        await RunAsync(async () => { if (Runtime.Application is not null) { DetailText.Text = Wire.Encode(await Runtime.Application.DetailsAsync(owner)); Pages.SelectedIndex = 1; } });
    }
    async void AlarmSelected(object sender, SelectionChangedEventArgs e)
    {
        if ((sender as DataGrid)?.SelectedItem is not Alarm alarm || alarm.OwnerId is null || Runtime.Application is null) return;
        selectedAlarm = alarm;
        var details = await Runtime.Application.DetailsAsync(alarm.OwnerId);
        if (selectedAlarm?.Id != alarm.Id) return;
        RequestIdBox.Text = details.FirstOrDefault(x => x.Request.Status == RequestStatus.Unknown)?.Request.Id ?? (alarm.Scope == "request" ? alarm.OwnerId : "");
        if (alarm.Code.StartsWith("LocalDispositionRequired", StringComparison.Ordinal)) model.LastResult = "保留原记录与实体；待取得预留/实体处置证据，当前版本不提供放行命令。";
        else if (details.Length == 0) model.LastResult = "来源诊断：" + alarm.Source + " / " + alarm.Scope + " / " + alarm.Code;
    }
    async void AlarmDetailClick(object sender, RoutedEventArgs e)
    {
        if (selectedAlarm is not Alarm alarm || alarm.OwnerId is null || Runtime.Application is null) return;
        await RunAsync(async () =>
        {
            var details = await Runtime.Application.DetailsAsync(alarm.OwnerId);
            if (details.Length != 0) { DetailText.Text = Wire.Encode(details); Pages.SelectedIndex = 1; }
            else { Pages.SelectedIndex = 3; model.LastResult = "来源诊断：" + alarm.OwnerId + " / " + alarm.Scope + " / " + alarm.Code; }
        });
    }
    async void ReconcileClick(object sender, RoutedEventArgs e) => await RunAsync(() => Runtime.ReconcileAsync(RequestIdBox.Text));
    async void AckClick(object sender, RoutedEventArgs e) { if (selectedAlarm is Alarm alarm) await SendAsync("AckAlarm", alarm.Id); }
    async Task LoadAlarmHistoryAsync() => await RunAsync(async () => { if (Runtime.Application is not null) AlarmHistoryGrid.ItemsSource = await Runtime.Application.AlarmHistoryAsync(alarmHistoryOffset, 50); });
    async void AlarmHistoryClick(object sender, RoutedEventArgs e) { alarmHistoryOffset = 0; await LoadAlarmHistoryAsync(); }
    async void AlarmNextClick(object sender, RoutedEventArgs e) { alarmHistoryOffset += 50; await LoadAlarmHistoryAsync(); }
    async void AlarmPreviousClick(object sender, RoutedEventArgs e) { alarmHistoryOffset = Math.Max(0, alarmHistoryOffset - 50); await LoadAlarmHistoryAsync(); }
    void RecoverySelected(object sender, SelectionChangedEventArgs e)
    {
        if (RecoveryGrid.SelectedItem is RequestRecoveryRow row) { RequestIdBox.Text = row.Id; model.LastResult = row.Reasons + " · " + row.NextStep; }
        CancelPreparedButton.IsEnabled = (RecoveryGrid.SelectedItem as RequestRecoveryRow)?.Recovery.LocalTermination.Allowed == true;
    }
    async void CancelPreparedClick(object sender, RoutedEventArgs e)
    {
        if (RecoveryGrid.SelectedItem is RequestRecoveryRow row)
            await SendAsync("CancelPrepared", row.Id, EvidenceBox.Text, $"仅终结原请求 {row.Id}？此前预留与实体保持待处置，不撤销远端动作。");
    }
    async void CloseDetailClick(object sender, RoutedEventArgs e) { if (snapshot?.State.Session is not null) await ShowDetailAsync(snapshot.State.Session.Id); }
    async void RecordEvidenceClick(object sender, RoutedEventArgs e) => await SendAsync("RecordReconciliation", RequestIdBox.Text, EvidenceBox.Text);
    static string Selected(ComboBox box) => ((ComboBoxItem)box.SelectedItem).Content.ToString()!;
    async void FaultClick(object sender, RoutedEventArgs e) => await SendAsync("SetFixtureFault", value: Selected(FaultBox));
    async void BlockClick(object sender, RoutedEventArgs e) => await SendAsync("SetSyntheticBlock", value: Selected(BlockBox));
    async void UnblockClick(object sender, RoutedEventArgs e) => await SendAsync("ClearSyntheticBlock", value: Selected(BlockBox));
    async void SaveConfigClick(object sender, RoutedEventArgs e) { await SendAsync("SaveConfig", value: ConfigDraft.Text); await LoadConfigs(); }
    async Task LoadConfigs() => await RunAsync(async () => { if (Runtime.Application is not null) ConfigsList.ItemsSource = await Runtime.Application.ConfigsAsync(); });
    async void ConfigsClick(object sender, RoutedEventArgs e) => await LoadConfigs();
    async void ActivateClick(object sender, RoutedEventArgs e) { if (ConfigsList.SelectedItem is ConfigRevision revision) { await SendAsync("ActivateConfig", revision.Id, confirm: "激活此修订需要受控退出并重启，当前有效配置保留到激活成功。确认？"); await LoadConfigs(); } }
    async void ExportClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "JSON|*.json", FileName = "ipc-diagnostics-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + ".json" };
        if (dialog.ShowDialog(this) == true) await RunAsync(() => Runtime.ExportAsync(dialog.FileName, snapshot?.State.Session?.Id));
    }
    async void BackupClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "SQLite|*.db", FileName = "ipc-backup-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + ".db" };
        if (dialog.ShowDialog(this) == true && Runtime.Application is not null) await RunAsync(() => Runtime.Application.BackupAsync(dialog.FileName));
    }
    async void ReviewClick(object sender, RoutedEventArgs e) => await RunAsync(() => Runtime.Application!.ReviewObservationsAsync(ReviewEvidence.Text));
    async void OverloadClick(object sender, RoutedEventArgs e) => await RunAsync(() => Runtime.ClearOverloadAsync(ReviewEvidence.Text));
    async void QuarantineClick(object sender, RoutedEventArgs e) => await SendAsync("ClearQuarantine", value: ReviewEvidence.Text, confirm: "已核对原外部账与实体库存，解除恢复隔离？未决请求将继续阻断。");
    async void WindowClosing(object? sender, CancelEventArgs e)
    {
        if (!ownsLifetime || closing) return;
        e.Cancel = true;
        if (!Confirmation.Ask(this, "退出将停止采集和发送。未决结果与原责任保留，重启后需登录和核对。确认退出？")) return;
        closing = true; timer.Stop();
        try { await ((App)Application.Current).StopRuntimeAsync(); Application.Current.Shutdown(); }
        catch (Exception ex) { closing = false; timer.Start(); model.GlobalStatus = "停止失败，保留诊断：" + ex.Message; }
    }
}

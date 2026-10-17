using System.IO;
using System.Text.Json;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Ipc.Platform;
using Ipc.Supply;
using Microsoft.Win32;

namespace Ipc.Desktop;
public partial class MainWindow : Window
{
    public IStationRuntime Runtime { get; }
    readonly bool ownsLifetime;
    readonly StationViewModel model = new();
    readonly LaneViewModel left = new("left"), right = new("right");
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    readonly CancellationTokenSource lifetime = new();
    readonly string windowIdentity = Wire.Id();
    bool busy, ready, smallArea;
    long detailGeneration;
    internal ConfigurationDraft Draft { get; } = new();
    ObservableCollection<DeviceDraft> deviceDrafts => Draft.Devices;
    string currentView = "workstation";
    readonly ObservableCollection<Alarm> alarms = [];
    StationSnapshot? snapshot;
    bool refreshing, closing, compact;
    int historyOffset, alarmHistoryOffset;
    Alarm? selectedAlarm;

    public MainWindow(IStationRuntime runtime, bool ownsLifetime = false)
    {
        Runtime = runtime; this.ownsLifetime = ownsLifetime; InitializeComponent(); DataContext = model;
        // WPF dimensions are effective units. Fit the initial window to the available work
        // area before responsive layout measures its client area (including at 150% DPI).
        Width = Math.Min(Width, SystemParameters.WorkArea.Width);
        Height = Math.Min(Height, SystemParameters.WorkArea.Height);
        Title = "供包参考工位 · " + (runtime.Mode?.ToString() ?? "NotReady");
        RuntimeIdentity.Text = $"运行实例 {runtime.RuntimeId} · 所有窗口共享此业务实例与发送器";
        ModeBadge.Text = runtime.Mode == RunMode.Simulation ? "SIMULATION" : "PRODUCTION · 未就绪";
        if (runtime.Application is null) model.GlobalStatus = "未就绪 · " + string.Join("；", runtime.RuntimeDiagnostics);
        LeftLane.DataContext = left; RightLane.DataContext = right; AlarmGrid.ItemsSource = alarms;
        InitializeHistoryFilters();
        LoadConfigForm(runtime.Configuration);
        ExtensionsControl.Initialize(this);
        DriverColumn.ItemsSource = runtime.InstalledDrivers;
        compact = runtime.Presentation.Layout == "compact"; currentView = runtime.Presentation.View;
        ConfigurePresentationAccess();
        timer.Tick += async (_, _) => { UpdateGuards(); await RefreshAsync(); };
        Loaded += async (_, _) => { ApplyLayout(); timer.Start(); StartNotificationPlayer(); await RefreshAsync(); if (currentView != "workstation") await NavigateToViewAsync(currentView); };
        Closed += async (_, _) => { closing = true; timer.Stop(); soundStop.Cancel(); lifetime.Cancel(); lifetime.Dispose(); if (!ownsLifetime) try { await Runtime.SetWindowAreaAsync(windowIdentity, false); } catch (Exception ex) { Runtime.ReportPresentationDiagnostic("Closed window protection retained: " + ex.Message); } };
    }
    async Task RefreshAsync()
    {
        if (refreshing || closing) return; refreshing = true;
        try
        {
            if (Runtime.Application is null) { model.GlobalStatus = "NotReady · " + string.Join("; ", Runtime.RuntimeDiagnostics); UpdateEntryProjection(); return; }
            await Runtime.SetWindowAreaAsync(windowIdentity, smallArea).WaitAsync(lifetime.Token);
            var next = await Runtime.Application.SnapshotAsync().WaitAsync(lifetime.Token);
            if (closing) return;
            snapshot = next; ready = true; ObserveReadIdentity(); UpdateGuards();
            model.Identity = snapshot.State.Actor is { } actor && actor.ExpiresAt > DateTimeOffset.UtcNow ? $"{actor.Id} · 模拟{(actor.Role == SimulationRole.Maintenance ? "维护员" : "操作员")}" : "模拟身份 · 未登录 / 已过期";
            ApplyClosureProjection(snapshot);
            var session = snapshot.State.Session;
            model.Context = session is null ? "未取得 · 请校验箱码" : $"{session.Barcode}   /   {OperatorText.Known(session.Context?.BoxWave)}   ·   {OperatorText.Session(session.Status)}";
            model.BoxBarcode = OperatorText.Known(session?.Barcode);
            model.BoxWave = session is null || session.Status is SessionStatus.Closed or SessionStatus.Rejected ? "未取得 · 等待新箱校验" : OperatorText.Known(session.Context?.BoxWave);
            model.BoxResult = snapshot.BoxResult is { } result ? $"{OperatorText.Kind(result.Stage)} · {OperatorText.Request(result.Status)}\n{OperatorText.Reason(result.Reason)}\n原请求 {result.RequestId}" : "尚无箱请求结果";
            model.SessionStatus = session is null ? "等待校验新箱" : OperatorText.Session(session.Status);
            UpdateEntryProjection();
            model.ContextSource = session?.Context?.Provenance is { } p ? $"来源 {p.Source} · 版本 {p.Version} · 证据 {p.EvidenceId} · {p.AcquiredAt:O}" : "箱语义来源 / 版本 / 证据：未取得";
            model.Accepted = session is null || session.Status is SessionStatus.Closed or SessionStatus.Rejected ? "—" : snapshot.AcceptedQuantity.ToString();
            model.Projection = $"投影 v{snapshot.ProjectionVersion} · 取得于 {snapshot.GeneratedAt.ToLocalTime():HH:mm:ss.fff} · 业务修订 {snapshot.State.Revision}";
            var recent = snapshot.RecentOperations.Select(x => new HistoryRow(x)).ToArray();
            if (RecentGrid.ItemsSource is not HistoryRow[] oldRecent || !oldRecent.Select(x => (x.Id,x.Status)).SequenceEqual(recent.Select(x => (x.Id,x.Status)))) RecentGrid.ItemsSource = recent;
            var reasons = snapshot.Admit.Reasons.Concat(snapshot.OperationGuards.Where(x => snapshot.State.Lanes[x.Key].Status is OperationStatus.AwaitingAction or OperationStatus.SplitRequired or OperationStatus.SubmitPending).SelectMany(x => x.Value.Reasons.Select(r => x.Key + ":" + r))).Concat(snapshot.RuntimeFaults).Distinct().ToArray();
            model.GlobalStatus = (reasons.Length == 0 ? "● 就绪 · 按当前操作指引继续" : "! 业务受控阻断 · " + OperatorText.Reasons(reasons));
            StatusBanner.Background = new System.Windows.Media.SolidColorBrush(reasons.Length == 0 ? System.Windows.Media.Color.FromRgb(237,247,241) : System.Windows.Media.Color.FromRgb(255,245,223));
            model.Devices = string.Join("\n", snapshot.State.Devices.Select(x => $"{x.Key}: {x.Value.DeviceId} / {OperatorText.Health(x.Value.Health.ToString())} / 代次 {x.Value.Epoch}"));
            model.AlarmSummary = $"活动报警 {snapshot.ActiveAlarmCount} · 已读仍计入活动";
            model.RecoveryHint = $"过载证据版本 {snapshot.OverloadVersion} · " + (snapshot.OverloadRecovery.Allowed ? "已排空；检查来源和实体后可确认过载。其它阻断仍保留。" : OperatorText.Reasons(snapshot.OverloadRecovery.Reasons));

            // Keep active identity/selection stable; resolved occurrences leave only this collection.
            var selectedActiveAlarm = (AlarmGrid.SelectedItem as Alarm)?.Id;
            foreach (var old in alarms.Where(x => !snapshot.Alarms.Any(a => a.Id == x.Id)).ToArray()) alarms.Remove(old);
            foreach (var alarm in snapshot.Alarms.Reverse())
            {
                var old = alarms.FirstOrDefault(x => x.Id == alarm.Id);
                if (old is null) alarms.Add(alarm); else if (old != alarm) alarms[alarms.IndexOf(old)] = alarm;
            }
            if(selectedActiveAlarm is not null)AlarmGrid.SelectedItem=alarms.FirstOrDefault(x=>x.Id==selectedActiveAlarm);
            var selectedRequest = (RecoveryGrid.SelectedItem as RequestRecoveryRow)?.Id;
            var recoveryRows = snapshot.Recovery.Select(x => new RequestRecoveryRow(x)).ToArray();
            if (RecoveryGrid.ItemsSource is not RequestRecoveryRow[] oldRows || Wire.Encode(oldRows) != Wire.Encode(recoveryRows))
            {
                RecoveryGrid.ItemsSource = recoveryRows;
                RecoveryGrid.SelectedItem = recoveryRows.FirstOrDefault(x => x.Id == selectedRequest);
            }
            UpdateGuards();
            await EnsureLayoutPauseAsync();
        }
        catch (OperationCanceledException) when (closing) { }
        catch (Exception ex) { ready = false; model.GlobalStatus = "状态读取失败 · 未就绪：" + ex.Message; UpdateGuards(); }
        finally { refreshing = false; }
    }
    public void ReportInput(string reason) => model.LastResult = reason;
    public async Task RunAsync(Func<Task> action)
    {
        if (busy || closing) return;
        busy = true; UpdateGuards(); model.LastResult = "正在执行，请勿重复操作…";
        try { await action(); if (!closing) model.LastResult = "操作已处理，请核对当前状态"; }
        catch (OperationCanceledException) when (closing) { }
        catch (Exception ex) { if (!closing) model.LastResult = "操作未完成：" + ex.Message; }
        finally { busy = false; if (!closing) { await RefreshAsync(); UpdateGuards(); } }
    }
    public async Task<CommandReceipt?> SendAsync(string kind, string? owner = null, string? value = null, string? confirm = null, long? expectedRevision = null, ConfigRevision? activation = null, string? commandId = null)
    {
        if (busy || closing) return null;
        if (kind is "Submit" or "ScanRetained" or "ClearSplit" && (LaneForOwner(owner) is not { } reviewed || !ReviewFits(reviewed) || !IsWorkstationActive))
        { model.LastResult = "请先在当前通道完整核对条码、任务和数量；区域不足时扩大窗口"; return null; }
        if (!CanCommand(kind, out var reason, activation)) { model.LastResult = "当前不能执行：" + reason; return null; }
        var revision = expectedRevision ?? snapshot?.State.Revision ?? 0;
        var evidenceVersion = kind == "ClearOverload" ? snapshot?.OverloadVersion : null;
        if (confirm is not null && !Confirmation.Ask(this, confirm, ConfirmationLabel(kind), kind == "ConfirmClose" || kind == "Submit")) return null;
        busy = true; UpdateGuards(); model.LastResult = "正在执行，请勿重复操作…";
        try { var receipt = await Runtime.CommandAsync(new(commandId ?? Wire.Id(), revision, kind, owner, value, EvidenceVersion:evidenceVersion)); if (kind == "Resume" && receipt.Status == "Persisted") layoutPauseRequired = false; if (!closing) model.LastResult = $"{(receipt.Status == "Persisted" ? "已处理" : "命令反馈")} · {receipt.Status} · {OperatorText.Reason(receipt.Reason)}"; return receipt; }
        catch (Exception ex) { if (!closing) model.LastResult = "命令失败：" + ex.Message; return null; }
        finally { busy = false; if (!closing) { await RefreshAsync(); UpdateGuards(); } }
    }
    bool Fresh => ready && snapshot is not null && DateTimeOffset.UtcNow - snapshot.GeneratedAt < TimeSpan.FromSeconds(3);
    bool CanCommand(string kind, out string reason, ConfigRevision? activation = null)
    {
        reason = "";
        if (closing || busy) reason = "正在执行或关闭";
        else if (smallArea && kind != "Login") reason = "有效客户区不足 1280 × 680 DIP";
        else if (!Fresh) reason = "投影未就绪或已过期，请等待重新读取";
        else if (kind == "Login") { if (Runtime.Mode != RunMode.Simulation) reason = "当前模式不允许模拟身份"; }
        else if (snapshot!.State.Actor is null || snapshot.State.Actor.ExpiresAt <= DateTimeOffset.UtcNow) reason = "请取得有效身份";
        else if (layoutPauseRequired && kind is "OpenContext" or "Submit" or "ScanRetained" or "Enter" or "Read" or "NoCode" or "MultiCode") reason = "窗口保护暂停尚未人工恢复";
        else if (kind is "SaveConfig" or "ActivateConfig" or "Reconcile" or "RecordReconciliation" or "SetFixtureFault" or "SetSyntheticBlock" or "ClearSyntheticBlock" or "ReviewObservations" or "ClearOverload" or "ClearQuarantine" or "CancelPrepared" or "Backup" or "Revalidate" or "ReconnectSource")
        { if (snapshot.State.Actor.Role != SimulationRole.Maintenance) reason = "需要维护身份"; }
        if (reason.Length == 0 && snapshot is not null)
        {
            var session = snapshot.State.Session;
            if (kind == "OpenContext" && session is not null && session.Status is not (SessionStatus.Closed or SessionStatus.Rejected)) reason = "当前箱尚未关闭或处置";
            if (kind == "CheckClose" && (!snapshot.Close.Allowed || session?.Status != SessionStatus.Active)) reason = session?.Status != SessionStatus.Active ? "当前箱不在作业中" : OperatorText.Reasons(snapshot.Close.Reasons);
            if (kind is "CancelClose" or "ConfirmClose" && session?.Status != SessionStatus.AwaitingShortageDecision) reason = "当前没有可确认的少货关闭检查";
            if (kind == "CancelPrepared" && (RecoveryGrid.SelectedItem as RequestRecoveryRow)?.Recovery.LocalTermination.Allowed != true) reason = "需选择具备持久未交付证明的原请求";
            if (kind == "ClearOverload" && !snapshot.OverloadRecovery.Allowed) reason = OperatorText.Reasons(snapshot.OverloadRecovery.Reasons);
            if (kind == "ActivateConfig")
            {
                var guard = ConfigurationActivation.Evaluate(snapshot.State, activation, Runtime.Configuration!.Version, snapshot.Recovery.Length != 0, DateTimeOffset.UtcNow);
                if (!guard.Allowed) reason = string.Join("；", guard.Reasons);
            }
            if (kind is "Enter" or "Read" or "NoCode" or "MultiCode" or "Depart" or "Reset" or "Replay" or "Revalidate" or "ReconnectSource" && Runtime.Mode != RunMode.Simulation) reason = "仅模拟模式允许工程事件";
            if(kind == "AckAlarm" && (selectedAlarm is null || selectedAlarm.AckedBy is not null))reason="请选择尚未已读的报警";
            if(kind == "Reconcile" && RequestIdBox.Text.Trim().Length==0)reason="请选择或输入原请求身份";
            if(kind is "RecordReconciliation" or "CancelPrepared" && (EvidenceBox.Text.Length is <8 or >1000))reason="请填写 8～1000 字符的检查依据";
            if(kind is "ClearOverload" or "ClearQuarantine" or "ReviewObservations" && (ReviewEvidence.Text.Length is <8 or >1000))reason="请填写 8～1000 字符的来源或实体检查依据";
            if(kind == "Pause" && snapshot.State.Blocks.Any(x=>x.Code=="OperatorPaused"))reason="工位已人工暂停";
            if(kind == "Resume" && !snapshot.State.Blocks.Any(x=>x.Code=="OperatorPaused"))reason="当前无人工暂停；恢复不解除其它阻断";
            var current = snapshot.State.Lanes.GetValueOrDefault(Selected(EngineeringLane));
            if (kind == "Enter" && (!snapshot.Admit.Allowed || current is not null && current.Occupancy != Occupancy.Clear)) reason = !snapshot.Admit.Allowed ? OperatorText.Reasons(snapshot.Admit.Reasons) : "上一实体尚未复位";
            if (kind is "Read" or "NoCode" or "MultiCode" && (current?.Occupancy != Occupancy.Occupied || current.Status is not (OperationStatus.Observed or OperationStatus.ValidationRejected or OperationStatus.Rejected))) reason = "需有在位、允许读码的实体；先解决原请求";
            if (kind == "Depart" && (current is null || current.Status is not (OperationStatus.Accepted or OperationStatus.Rejected or OperationStatus.ValidationRejected or OperationStatus.Cancelled))) reason = "原请求或草稿尚未处置，不得提供离场事件";
            if (kind == "Reset" && current?.Occupancy != Occupancy.Departed) reason = "先提供对应实体的离场证据";
            if (kind == "Revalidate" && current?.Occupancy != Occupancy.Occupied) reason = "仅可核对当前在位实体";
            if (kind == "Replay" && Runtime.Configuration?.Devices.FirstOrDefault(x=>x.Role==Selected(EngineeringLane))?.Driver != "replay") reason = "此通道未绑定回放驱动";

        }
        return reason.Length == 0;
    }
    static IEnumerable<T> Children<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent,i);
            if (child is T found) yield return found;
            foreach (var nested in Children<T>(child)) yield return nested;
        }
    }
    void UpdateGuards()
    {
        if (LeftLane is null || EngineeringControls is null) return;
        ObserveReadIdentity();
        if (snapshot is not null)
        {
            var available = Fresh && !busy && !smallArea && !layoutPauseRequired && IsWorkstationActive;
            left.Apply(snapshot, available, HasIdentity, ReviewFits(left), busy);
            right.Apply(snapshot, available, HasIdentity, ReviewFits(right), busy);
        }
        foreach (var button in Children<Button>(this).Where(x=>x.Tag is string))
        {
            button.IsEnabled = CanCommand((string)button.Tag, out var why, (string)button.Tag == "ActivateConfig" && ConfigsList.IsEnabled ? ConfigsList.SelectedItem as ConfigRevision : null);
            button.ToolTip = button.IsEnabled ? "执行时由应用层复核身份、对象和版本" : why;
            ToolTipService.SetShowOnDisabled(button,true);
        }
        EngineeringControls.IsEnabled = HasIdentity && !smallArea;
        ExtensionsControl.SetAccess(HasIdentity && !busy && snapshot?.State.Actor?.Role == SimulationRole.Maintenance);
        if (!Fresh && snapshot is not null)
        {
            model.GlobalStatus = "投影已失效 · 新业务暂停，等待重新读取";
            StatusBanner.Background = (System.Windows.Media.Brush)FindResource("WarningBackground");
        }
        model.BoxAvailability = !Fresh ? "当前不可用 · 投影未取得或已过期；等待重新读取" :
            busy ? "正在执行 · 等待当前命令结果" : snapshot is null ? "等待当前投影" :
            "当前入场：" + (snapshot.Admit.Allowed ? "具备条件" : OperatorText.Reasons(snapshot.Admit.Reasons)) +
            "；结束检查：" + (snapshot.Close.Allowed ? "具备条件" : OperatorText.Reasons(snapshot.Close.Reasons));
        CanCommand("CheckClose", out var closeWhy); CanCommand("ConfirmClose", out var confirmWhy);
        model.CommandFeedback = snapshot?.State.Session?.Status == SessionStatus.AwaitingShortageDecision ? confirmWhy : closeWhy;
        CanCommand("Pause", out var pauseWhy); CanCommand("Resume", out var resumeWhy);
        model.ControlFeedback = string.Join("；", new[] { pauseWhy, resumeWhy }.Where(x => x.Length > 0).Distinct());
        ApplyReadControlGuards();
    }
    async void OperatorClick(object sender, RoutedEventArgs e) => await SendAsync("Login", value: "operator");
    async void MaintenanceClick(object sender, RoutedEventArgs e) => await SendAsync("Login", value: "maintenance");
    async void LogoutClick(object sender, RoutedEventArgs e) => await SendAsync("Logout", confirm: "登出将阻断新业务；原作业责任与未决请求仍保留。确认登出？");
    async void OpenClick(object sender, RoutedEventArgs e) => await SendAsync("OpenContext", value: model.BoxDraft);
    async void PauseClick(object sender, RoutedEventArgs e) => await SendAsync("Pause", confirm: "暂停新入场？已交给网络的请求仍需接收结果。软件暂停不是安全急停。");
    async void ResumeClick(object sender, RoutedEventArgs e) => await SendAsync("Resume");
    async void CheckClick(object sender, RoutedEventArgs e) => await SendAsync("CheckClose");
    async void CancelCloseClick(object sender, RoutedEventArgs e) => await SendAsync("CancelClose");
    async void ConfirmCloseClick(object sender, RoutedEventArgs e)
    {
        if (!CanCommand("ConfirmClose", out var why)) { model.LastResult=why; return; }
        var session = snapshot?.State.Session;
        if (session is null) return;
        var revision=snapshot!.State.Revision;
        var owner=session.CloseCheckRequest;
        if (session.CloseRemaining is null) { model.LastResult="关闭检查数量未取得；不能按零缺失或少货强制结束。"; return; }
        if (session.CloseRemaining == 0) { await SendAsync("ConfirmClose",owner,"CONFIRM",expectedRevision:revision); return; }
        var message=$"箱 {session.Barcode} / 会话 {session.Id}\n当前关闭检查 {owner}\n缺失基础SKU总量 {session.CloseRemaining?.ToString() ?? "未取得"}；分SKU明细：参考合同未提供。\n两通道及原请求仍由应用层复核。请核对箱与工位、缺失事实及数量；提交后等待原关闭回执，不会直接清空箱。";
        var confirmed=Confirmation.Ask(this,message,"确认强制结束",true,"取消，继续供包");
        await SendAsync(confirmed?"ConfirmClose":"CancelClose",owner,confirmed?"CONFIRM":null,expectedRevision:revision);
    }
    async void BoxKeyDown(object sender, KeyEventArgs e) { if (e.Key is Key.Enter or Key.Tab && Keyboard.Modifiers == ModifierKeys.None && model.BoxDraft.Length > 0) { e.Handled = true; await SendAsync("OpenContext", value: model.BoxDraft.TrimEnd('\r','\n','\t')); } }
    void SecondViewClick(object sender, RoutedEventArgs e) => new MainWindow(Runtime) { Owner = this }.Show();
    async void LayoutClick(object sender, RoutedEventArgs e)
    {
        var proposed = compact ? "standard" : "compact";
        if (!Runtime.AllowedLayouts.Contains(proposed)) { model.LastResult = "此布局未获产品允许"; return; }
        await RunAsync(async () => { await Runtime.SavePresentationAsync(new(currentView, proposed)); compact = proposed == "compact"; ApplyLayout(); });
    }
    void WindowSizeChanged(object sender, SizeChangedEventArgs e) => ApplyLayout();
    void ApplyLayout() => ApplyWorkstationLayout();
    void TabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReferenceEquals(e.Source,Pages) && IsLoaded) UpdateGuards();
    }
    public async Task ShowDetailAsync(string owner, SupplyOperation? operation = null)
    {
        if (Runtime.Application is null || closing) return;
        if (!BeginHistoricalRead("作业详情", out var readActor)) return;
        var reviewedStateRevision=snapshot!.State.Revision;
        var generation = ++detailGeneration;
        try
        {
            var details = await Runtime.Application.DetailsAsync(owner).WaitAsync(lifetime.Token);
            if (closing || generation != detailGeneration || !ReadIdentityMatches(readActor)) return;
            DetailTree.IsEnabled = true; ReadAccessFeedback.Text = "详情为本次有效身份取得的只读快照；查看不会发送业务请求。";
            RenderDetails(owner, operation, details, reviewedStateRevision); NavigateToHistoryDetail();
        }
        catch (OperationCanceledException) when (closing) { }
        catch(Exception ex) { if (!closing && generation == detailGeneration) MarkDetailReadFailure("详情读取失败：" + ex.Message); }
    }
    void RenderDetails(string owner, SupplyOperation? operation, RequestDetail[] details, long? reviewedStateRevision = null)
    {
        DetailTree.Items.Clear();
        var root = new TreeViewItem { Header = "作业 / 会话  " + owner, IsExpanded = true };
        void Add(ItemsControl parent, string text) => parent.Items.Add(new TextBox { Text=text, IsReadOnly=true, IsTabStop=false, AcceptsReturn=true, TextWrapping=TextWrapping.Wrap, Margin=new Thickness(4), Padding=new Thickness(0), BorderThickness=new Thickness(0), Background=System.Windows.Media.Brushes.Transparent, MinHeight=0, MaxWidth=700 });
        if (operation is not null)
        {
            Add(root,$"作业 {operation.Id} / r{operation.Revision}\n{OperatorText.Operation(operation.Status)} · {operation.Occupancy}\n箱 {OperatorText.Known(operation.BoxBarcode)} / 箱波次 {OperatorText.Known(operation.BoxWave)}\nItem {OperatorText.Known(operation.Barcode)} / SKU {OperatorText.Known(operation.Task?.Sku)}\nItem 波次 {OperatorText.Known(operation.Task?.ItemWave)} / 任务 {OperatorText.Known(operation.Task?.TaskId)}\n格口 {OperatorText.Known(operation.Task?.Destination)} / 数量 {operation.Supply?.ToString() ?? "未取得"} {OperatorText.Known(operation.Task?.Unit)}\n{OperatorText.Reason(operation.Reason)}");
            if (reviewedStateRevision is { } reviewedRevision && snapshot!.State.Revision == reviewedRevision &&
                CanCommand("CancelDraft", out _) && LaneForOwner(operation.Id)?.Current is { } current &&
                current.Revision == operation.Revision && Wire.Encode(current) == Wire.Encode(operation) &&
                current.Status is OperationStatus.AwaitingAction or OperationStatus.SplitRequired or OperationStatus.ValidationRejected or OperationStatus.Rejected)
            {
                var dispose = new Button { Content = "处置当前未提交草稿" };
                dispose.Click += async (_, _) =>
                {
                    if (!Fresh || snapshot!.State.Revision != reviewedRevision || LaneForOwner(operation.Id)?.Current?.Revision != operation.Revision)
                    { dispose.IsEnabled = false; model.LastResult = "详情核对对象或版本已变化；请重新打开当前作业详情后再处置。旧阅读快照不授权当前草稿。"; return; }
                    await SendAsync("CancelDraft", operation.Id, confirm: $"处置作业 {operation.Id} / r{operation.Revision} 的未提交草稿？原请求、接受账、预留与实体责任保留。", expectedRevision: reviewedRevision);
                };
                root.Items.Add(dispose);
            }
            if(operation.CurrentInput is { } input) Add(root,$"本次观察 {input.ObservationId} / {input.ObservedAt:O} / 码集合 {string.Join(" / ",input.Codes)} / 包装量 {input.PackageQuantity?.ToString() ?? "未取得"}");
            if(operation.PreviousConfirmed is { } prior) Add(root,$"历史证实结果 r{prior.Revision}（不授权本次输入） · Item {prior.Barcode} / 数量 {prior.Supply?.ToString() ?? "未取得"} / SKU {OperatorText.Known(prior.Task?.Sku)}");
            if(operation.Task?.Provenance is {} p) Add(root,$"语义来源 {p.Source} / {p.Version} / {p.EvidenceId} / {p.AcquiredAt:O}");
        }
        foreach(var detail in details)
        {
            var request=detail.Request;
            var node=new TreeViewItem { Header=$"请求 · {OperatorText.Kind(request.Kind)} · {OperatorText.Request(request.Status)} · {request.Id}", IsExpanded=false }; root.Items.Add(node);
            Add(node,$"原请求 {request.Id}\n原哈希 {request.PayloadHash}\n合同 {request.Binding.Contract} / 目标版本 {request.Binding.Revision}\n固定目标 {request.Binding.Endpoint}\n创建 {request.CreatedAt:O}");
            var attempts=new TreeViewItem { Header=$"发送尝试 ({detail.Attempts.Length})", IsExpanded=true }; node.Items.Add(attempts);
            foreach(var attempt in detail.Attempts) Add(attempts,$"#{attempt.Ordinal} {attempt.Id}\n{attempt.StartedAt:O} → {attempt.EndedAt?.ToString("O") ?? "未结束"}\n{attempt.Disposition} / {attempt.Error}");
            var responses=new TreeViewItem { Header=$"响应证据 ({detail.Responses.Length})", IsExpanded=true }; node.Items.Add(responses);
            foreach(var response in detail.Responses)
            {
                Add(responses,$"{(response.Result=="Accepted"?"已接受":response.Result=="Rejected"?"已拒绝":response.Result)} · {OperatorText.Reason(response.Reason)}\n证据 {response.EvidenceId} / 数量 {response.Quantity?.ToString() ?? "未取得"} / 剩余 {response.Remaining?.ToString() ?? "未取得"}\n原请求 {response.RequestId} / 哈希 {response.PayloadHash}");
                Add(responses,$"箱波次 {OperatorText.Known(response.Context?.BoxWave)}\nSKU {OperatorText.Known(response.Task?.Sku)} / Item 波次 {OperatorText.Known(response.Task?.ItemWave)}\n任务 {OperatorText.Known(response.Task?.TaskId)} / 格口 {OperatorText.Known(response.Task?.Destination)} / 单位 {OperatorText.Known(response.Task?.Unit)}");
                foreach(var source in new[]{response.Context?.Provenance,response.Task?.Provenance}.Where(x=>x is not null).Distinct())
                    Add(responses,$"来源 {source!.Source} / 版本 {source.Version}\n语义证据 {source.EvidenceId} / 取得时间 {source.AcquiredAt:O}");
            }
            var audit=new TreeViewItem { Header=$"审计 ({detail.Audit.Length})", IsExpanded=true }; node.Items.Add(audit);
            var receipts = new TreeViewItem { Header=$"每次接收责任 ({detail.Receipts.Length})", IsExpanded=detail.Receipts.Any(x=>x.Status is not ("Committed" or "NoResponse")) }; node.Items.Add(receipts);
            foreach(var received in detail.Receipts) Add(receipts,$"接收 {received.Id} · {received.Origin} · {received.StartedAt:O}\n{received.Status} · 原请求 {received.RequestId} / Attempt {received.AttemptId}\n{(received.Response is null ? "未取得原件，不可恢复原内容" : "原件已持久接管 · SHA " + received.ContentSha256)}");
            foreach(var conflict in detail.Conflicts)
                Add(responses,$"持续核对冲突 {conflict.Id} · 原EvidenceId {conflict.OriginalEvidenceId}\n{conflict.ReceivedAt:O} · 来源 {conflict.Source} · Attempt {conflict.AttemptId}\n{Wire.Encode(conflict.Response)}");
            foreach(var entry in detail.Audit) Add(audit,$"{entry.At:O} · {entry.Actor} · {entry.Kind}\n命令 {entry.CommandId} / {entry.Evidence}");
            var payload=new TreeViewItem { Header="高级诊断 · 原始固定报文", IsExpanded=false }; payload.Items.Add(new TextBox {Text=request.Payload,IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MaxWidth=700,MaxHeight=220,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}); node.Items.Add(payload);
        }
        if(details.Length==0) Add(root,"尚无请求记录；观察事实与任务快照不会被补造为外部接受。");
        DetailTree.Items.Add(root);
    }
    async void AlarmSelected(object sender, SelectionChangedEventArgs e)
    {
        selectedAlarm = (sender as DataGrid)?.SelectedItem as Alarm;
        if (selectedAlarm is not Alarm alarm) return;
        RequestIdBox.Text = "";
        if (!BeginHistoricalRead("报警关联请求", out var readActor)) return;
        if (alarm.OwnerId is null || Runtime.Application is null) { model.LastResult = "来源诊断：" + alarm.Source + " / " + alarm.Scope + " / " + alarm.Code; return; }
        try
        {
            var details = await Runtime.Application.DetailsAsync(alarm.OwnerId).WaitAsync(lifetime.Token);
            if (closing || selectedAlarm?.Id != alarm.Id || !ReadIdentityMatches(readActor)) return;
            RequestIdBox.Text = details.FirstOrDefault(x => x.Request.Status == RequestStatus.Unknown)?.Request.Id ?? (alarm.Scope == "request" ? alarm.OwnerId : "");
            if (alarm.Code.StartsWith("LocalDispositionRequired", StringComparison.Ordinal)) model.LastResult = "保留原记录与实体；待取得预留/实体处置证据，当前版本不提供放行命令。";
            else if (details.Length == 0) model.LastResult = "来源诊断：" + alarm.Source + " / " + alarm.Scope + " / " + alarm.Code;
        }
        catch(OperationCanceledException) when(closing) { }
        catch(Exception ex) { if(!closing)model.LastResult="关联请求读取失败："+ex.Message; }
    }
    async void AlarmDetailClick(object sender, RoutedEventArgs e)
    {
        if (selectedAlarm is not Alarm alarm || alarm.OwnerId is null || Runtime.Application is null) return;
        if (!BeginHistoricalRead("报警关联详情", out var readActor)) return;
        var generation = ++detailGeneration;
        await RunAsync(async () =>
        {
            var details = await Runtime.Application.DetailsAsync(alarm.OwnerId).WaitAsync(lifetime.Token);
            if(closing || generation != detailGeneration || !ReadIdentityMatches(readActor)) return;
            if (details.Length != 0) { DetailTree.IsEnabled=true; RenderDetails(alarm.OwnerId, null, details); NavigateToHistoryDetail(); }
            else { await NavigateToViewAsync("diagnostics"); model.LastResult = "来源诊断：" + alarm.OwnerId + " / " + alarm.Scope + " / " + alarm.Code; }
        });
    }
    async void ReconcileClick(object sender, RoutedEventArgs e) => await RunAsync(() => Runtime.ReconcileAsync(RequestIdBox.Text));
    async void AckClick(object sender, RoutedEventArgs e) { if (selectedAlarm is Alarm alarm) await SendAsync("AckAlarm", alarm.Id); }
    async Task LoadAlarmHistoryAsync()
    {
        if (!BeginHistoricalRead("报警历史", out var readActor) || Runtime.Application is null) return;
        var generation = ++alarmHistoryGeneration;
        AlarmHistoryFeedback.Text = "正在查询；已有列表为上次快照";
        try
        {
            var rows = await Runtime.Application.AlarmHistoryAsync(alarmHistoryOffset, 50).WaitAsync(lifetime.Token);
            if (closing || generation != alarmHistoryGeneration || !ReadIdentityMatches(readActor)) return;
            AlarmHistoryGrid.ItemsSource = rows; AlarmHistoryGrid.IsEnabled = true;
            AlarmHistoryFeedback.Text = rows.Length == 0 ? "此页暂无已恢复报警" : $"本次取得 {rows.Length} 条 · 偏移 {alarmHistoryOffset}";
        }
        catch (OperationCanceledException) when (closing) { }
        catch (Exception ex) { if (!closing && generation == alarmHistoryGeneration) { AlarmHistoryGrid.IsEnabled = false; AlarmHistoryFeedback.Text = "报警历史查询失败：" + ex.Message + "；已有列表为旧快照"; } }
    }
    async void AlarmHistoryClick(object sender, RoutedEventArgs e) { alarmHistoryOffset = 0; await LoadAlarmHistoryAsync(); }
    async void AlarmNextClick(object sender, RoutedEventArgs e) { alarmHistoryOffset += 50; await LoadAlarmHistoryAsync(); }
    async void AlarmPreviousClick(object sender, RoutedEventArgs e) { alarmHistoryOffset = Math.Max(0, alarmHistoryOffset - 50); await LoadAlarmHistoryAsync(); }
    void RecoverySelected(object sender, SelectionChangedEventArgs e)
    {
        if (RecoveryGrid.SelectedItem is RequestRecoveryRow row) { RequestIdBox.Text = row.Id; model.LastResult = row.Reasons + " · " + row.NextStep; }
        UpdateGuards();
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
    void LoadConfigForm(StationConfig? config)
    {
        if(config is null) return;
        Draft.Replace(config);
        foreach (var pair in new (TextBox Field, string Key)[] { (StationIdField,"StationId"),(VersionField,"Version"),(EndpointField,"Endpoint"),
            (BindingVersionField,"BindingVersion"),(ContractField,"Contract"),(TimeoutField,"Timeout"),(MinimumFreeField,"MinimumFree"),
            (FixtureDemandField,"FixtureDemand"),(MaximumPackageField,"MaximumPackage"),(PolicyField,"Policy"),(EventCapacityField,"EventCapacity"),
            (CommandCapacityField,"CommandCapacity"),(OutboxCapacityField,"OutboxCapacity") })
            pair.Field.SetBinding(TextBox.TextProperty, new System.Windows.Data.Binding("["+pair.Key+"]") { Source=Draft,Mode=System.Windows.Data.BindingMode.TwoWay,UpdateSourceTrigger=System.Windows.Data.UpdateSourceTrigger.PropertyChanged });
        DeviceConfigGrid.ItemsSource=deviceDrafts; ConfigDraft.Text=Wire.Encode(config);
    }
    StationConfig BuildDraft()
    {
        DeviceConfigGrid.CommitEdit(DataGridEditingUnit.Cell,true); DeviceConfigGrid.CommitEdit(DataGridEditingUnit.Row,true);
        return Draft.Build();
    }
    StationConfig PreviewDraft()
    {
        var draft=BuildDraft(); var original=Runtime.Configuration ?? throw new InvalidDataException("尚无有效配置");
        var errors=Runtime.ValidateConfiguration(draft).Concat(draft.StationId!=original.StationId||draft.Mode!=original.Mode?new[]{"站点 ID 与模式绑定当前部署，禁止在此更名或切换"}:Array.Empty<string>()).Concat(draft.Version==original.Version?new[]{"请填写新的配置版本"}:Array.Empty<string>()).ToArray();
        ConfigDiff.ItemsSource = ConfigurationDifferences(original,draft);
        ConfigDraft.Text=Wire.Encode(draft); ConfigFeedback.Text=errors.Length==0?"本地校验通过；保存时后台复核。原有效配置保持到重启激活成功。":"校验未通过："+string.Join("；",errors);
        if(errors.Length>0) throw new InvalidDataException(ConfigFeedback.Text); return draft;
    }
    static string[] ConfigurationDifferences(StationConfig original, StationConfig draft)
    {
        static Dictionary<string,string> Flatten(StationConfig config)
        {
            var values = new Dictionary<string,string>();
            void Visit(JsonElement value,string path)
            {
                if(value.ValueKind==JsonValueKind.Object) foreach(var property in value.EnumerateObject()) Visit(property.Value,path.Length==0?property.Name:path+"."+property.Name);
                else if(value.ValueKind==JsonValueKind.Array) {var index=0;foreach(var child in value.EnumerateArray())Visit(child,$"{path}[{index++}]");}
                else values[path]=value.ToString();
            }
            using var json=JsonDocument.Parse(Wire.Encode(config)); Visit(json.RootElement,""); return values;
        }
        var old=Flatten(original); var next=Flatten(draft);
        var changes=old.Keys.Union(next.Keys).Where(key=>old.GetValueOrDefault(key)!=next.GetValueOrDefault(key))
            .Select(key=>$"{key}：{old.GetValueOrDefault(key,"未取得")} → {next.GetValueOrDefault(key,"未取得")}").ToArray();
        return changes.Length==0 ? ["没有配置差异"] : new[]{"生效时机：受控重启成功后；失败保留当前有效配置。"}.Concat(changes).ToArray();
    }
    void PreviewConfigClick(object sender,RoutedEventArgs e) { try{PreviewDraft();}catch(Exception ex){ConfigFeedback.Text=ex.Message;} }
    void ImportConfigClick(object sender,RoutedEventArgs e) { try{Draft.Import(ConfigDraft.Text, Runtime.ValidateConfiguration); DriverColumn.ItemsSource = Runtime.InstalledDrivers.Concat(Draft.Devices.Select(x=>x.Driver)).Distinct().ToArray(); ExtensionsControl.ResetDraft(); ConfigFeedback.Text="已导入表单；尚未保存或激活。";}catch(Exception ex){ConfigFeedback.Text="导入失败，保留表单："+ex.Message;} }
    async void SaveConfigClick(object sender, RoutedEventArgs e) { try{var draft=PreviewDraft();await SendAsync("SaveConfig",value:Wire.Encode(draft));await LoadConfigs();}catch(Exception ex){ConfigFeedback.Text=ex.Message;} }
    async Task LoadConfigs()
    {
        if (Runtime.Application is null || closing || !BeginHistoricalRead("配置修订", out var readActor)) return;
        try
        {
            var rows = await Runtime.Application.ConfigsAsync().WaitAsync(lifetime.Token);
            if (!closing && ReadIdentityMatches(readActor)) { ConfigsList.ItemsSource=rows; ConfigsList.IsEnabled=true; ConfigFeedback.Text="已读取当前修订；草稿不会自动生效。"; UpdateGuards(); }
        }
        catch(OperationCanceledException) when(closing) { }
        catch(Exception ex) { if(!closing) { ConfigsList.IsEnabled=false; ConfigFeedback.Text="修订读取失败："+ex.Message+"；已有列表为旧快照"; } }
    }
    async void ConfigsClick(object sender, RoutedEventArgs e) => await LoadConfigs();
    async void ActivateClick(object sender, RoutedEventArgs e) { if (ConfigsList.SelectedItem is ConfigRevision revision) { await SendAsync("ActivateConfig", revision.Id, activation: revision, confirm: "将选中修订标记为待激活？需要受控退出和重启。失败保留原有效配置，原请求仍绑定原合同与目标。"); await LoadConfigs(); } }
    async void EventClick(object sender,RoutedEventArgs e) => await EmitEventAsync((string)((Button)sender).Tag);
    async void EngineeringKeyDown(object sender,KeyEventArgs e) { if(e.Key is Key.Enter or Key.Tab && Keyboard.Modifiers == ModifierKeys.None && EngineeringBarcode.Text.Length>0){e.Handled=true;await EmitEventAsync("Read");} }
    async Task EmitEventAsync(string kind)
    {
        if(!CanCommand(kind,out var why)){model.LastResult=why;return;}
        if(kind is "Depart" or "Reset" or "Revalidate" && !Confirmation.Ask(this,"确认已提供对应实体的模拟物理事件？原业务结果和实体责任继续保留。"))return;
        await RunAsync(async()=> {if(!int.TryParse(EngineeringQuantity.Text,out var quantity))throw new InvalidDataException("模拟包装量必须是整数"); var code=EngineeringBarcode.Text.TrimEnd('\r','\n','\t'); var codes=kind switch{"NoCode"=>Array.Empty<string>(),"MultiCode"=>new[]{code,"999999"},_=>new[]{code}}; await Runtime.EmitAsync(Selected(EngineeringLane),kind is "NoCode" or "MultiCode"?"Read":kind,codes,quantity);});
    }
    async void ReplayClick(object sender,RoutedEventArgs e) { if(CanCommand("Replay",out var why))await RunAsync(()=>Runtime.ReplayAsync(Selected(EngineeringLane)));else model.LastResult=why; }
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
    async void ReviewClick(object sender, RoutedEventArgs e) => await ReviewSelectedSourcesAsync();
    async void OverloadClick(object sender, RoutedEventArgs e) => await SendAsync("ClearOverload",value:ReviewEvidence.Text,confirm:$"确认已检查当前过载证据版本 {snapshot?.OverloadVersion} 的来源与实体？新拒收会使本次确认失效，其它阻断继续保留。");
    async void QuarantineClick(object sender, RoutedEventArgs e) => await SendAsync("ClearQuarantine", value: ReviewEvidence.Text, confirm: "已核对原外部账与实体库存，解除恢复隔离？未决请求将继续阻断。");
    async void WindowClosing(object? sender, CancelEventArgs e)
    {
        if (!ownsLifetime || closing) return;
        e.Cancel = true;
        if (!Confirmation.Ask(this, "退出将停止采集和发送。未决结果与原责任保留，重启后需登录和核对。确认退出？")) return;
        closing = true; timer.Stop(); soundStop.Cancel();
        try { if(soundTask is not null) await soundTask; await ((App)Application.Current).StopRuntimeAsync(); Application.Current.Shutdown(); }
        catch (Exception ex)
        {
            closing = false; ready = false; timer.Start();
            soundStop.Dispose(); soundStop = new(); soundTask = null; StartNotificationPlayer();
            model.GlobalStatus = "停止或清理失败，业务保持阻断，保留诊断：" + ex.Message;
        }
    }
}

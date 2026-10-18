using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Ipc.Platform;
using Ipc.Supply;

namespace Ipc.Desktop;

/// <summary>Window-owned navigation, drafts and focus. The runtime remains the sole business owner.</summary>
public partial class MainWindow
{
    const double MinimumClientWidth = 1280, MinimumClientHeight = 680;
    IInputElement? drawerReturnFocus;
    bool entryRequested, recoveryEntryAcknowledged, layoutPauseRequired, layoutPauseAcknowledged;
    Actor? observedReadActor;
    long readIdentityGeneration;
    readonly BoxEntryFocusPolicy boxEntryFocus = new();
    long alarmHistoryGeneration;
    bool IsDrawerOpen => DrawerOverlay?.Visibility == Visibility.Visible;
    bool IsWorkstationActive => !IsDrawerOpen && EntryOverlay?.Visibility != Visibility.Visible;
    bool HasIdentity => Fresh && snapshot?.State.Actor is { } actor && actor.ExpiresAt > DateTimeOffset.UtcNow;
    LaneViewModel? LaneForOwner(string? owner) => owner is null ? null : left.Current?.Id == owner ? left : right.Current?.Id == owner ? right : null;
    public void ReviewGeometryChanged() => UpdateGuards();
    bool ReviewFits(LaneViewModel lane) => !smallArea && (lane == left ? LeftLane : RightLane).ReviewFits;
    static string ConfirmationLabel(string kind) => kind switch
    {
        SupplyCommands.ConfirmClose => "确认强制结束", SupplyCommands.ClearSplit => "清空并重新计数", SupplyCommands.CancelDraft => "处置未提交草稿",
        SupplyCommands.Pause => "暂停新入场", SupplyCommands.Submit => "确认拆包并供包", SupplyCommands.Logout => "确认登出", _ => "确认此操作"
    };
    void ConfigurePresentationAccess()
    {
        bool Allowed(string view) => Runtime.AllowedViews.Contains(view);
        NavHistory.Visibility = Allowed("history") ? Visibility.Visible : Visibility.Collapsed;
        NavRecovery.Visibility = Allowed("recovery") || Allowed("alarms") ? Visibility.Visible : Visibility.Collapsed;
        NavDevices.Visibility = Allowed("diagnostics") || Allowed("maintenance") ? Visibility.Visible : Visibility.Collapsed;
        NavEngineering.Visibility = Runtime.Mode == RunMode.Simulation && Allowed("engineering") ? Visibility.Visible : Visibility.Collapsed;
        foreach (TabItem page in Pages.Items)
            page.Visibility = Runtime.AllowedViews.Any(view => ViewRegistry.Pages.GetValueOrDefault(view) == page.Name) ? Visibility.Visible : Visibility.Collapsed;
    }
    async Task NavigateToViewAsync(string view)
    {
        if (closing) return;
        if (!Runtime.AllowedViews.Contains(view)) { model.LastResult="此产品未安装或不允许该视图："+view; return; }
        if (view == "workstation") { CloseDrawer(); return; }
        if (FindName(ViewRegistry.Resolve(view)) is not TabItem page) return;
        if (!IsDrawerOpen) drawerReturnFocus = Keyboard.FocusedElement;
        DrawerOverlay.Visibility = Visibility.Visible;
        WorkstationPage.IsEnabled = SharedContext.IsEnabled = ShellHeader.IsEnabled = ShellFooter.IsEnabled = false; EntryOverlay.IsEnabled=false;
        Pages.SelectedItem = page;
        currentView=view;
        (DrawerTitle.Text,DrawerDescription.Text) = view switch
        {
            "history" => ("作业记录与原请求详情", "PAGE-06 · 业务作业、原请求、网络尝试、回执与审计分层"),
            "recovery" or "alarms" => ("报警、核对与恢复", "PAGE-01 / 07 · 已读不等于恢复；Unknown保留原请求"),
            "extensions" => ("组件与部署管理", "受控签名包 / 实例与角色绑定 / 统一部署修订 / 重启生效"),
            "engineering" => ("工程控制 · SIMULATION", "仅合成观察及故障注入；不控制真实设备，不写生产WCS"),
            _ => ("设备与设置", "PAGE-08 · 只读诊断 / 维护配置草稿 / 受控重启生效")
        };
        ApplyWorkstationLayout();
        _ = Dispatcher.InvokeAsync(() => DrawerClose.Focus());
        try { await Runtime.SavePresentationAsync(new(view, compact ? "compact" : "standard")); }
        catch (Exception ex) { model.LastResult="页面偏好保存失败："+ex.Message; }
        if (view=="history" && HistoryGrid.ItemsSource is null && !navigatingDirectDetail) await LoadHistoryAsync();
        if (view=="maintenance" && ConfigsList.ItemsSource is null) await LoadConfigs();
        if (view=="extensions") await LoadExtensionsAsync();
        UpdateGuards();
    }
    void CloseDrawer()
    {
        DrawerOverlay.Visibility=Visibility.Collapsed;
        WorkstationPage.IsEnabled=ShellHeader.IsEnabled=ShellFooter.IsEnabled=true; EntryOverlay.IsEnabled=true;
        currentView="workstation";
        UpdateEntryProjection(); UpdateGuards();
        if (drawerReturnFocus is DependencyObject target && target is UIElement { IsVisible:true,IsEnabled:true }) Keyboard.Focus(drawerReturnFocus);
        else if (EntryOverlay.Visibility==Visibility.Visible) EntryContinueButton.Focus();
        drawerReturnFocus=null;
    }
    async void CloseDrawerClick(object sender,RoutedEventArgs e)
    {
        CloseDrawer();
        try { await Runtime.SavePresentationAsync(new("workstation", compact ? "compact" : "standard")); }
        catch(Exception ex) { model.LastResult="页面偏好保存失败："+ex.Message; }
    }
    void WindowPreviewKeyDown(object sender,KeyEventArgs e)
    {
        if (e.Key != Key.Escape || !IsDrawerOpen) return;
        e.Handled=true; CloseDrawer();
    }
    async void OpenHistoryClick(object sender,RoutedEventArgs e) => await NavigateToViewAsync("history");
    async void OpenRecoveryClick(object sender,RoutedEventArgs e)
    {
        recoveryEntryAcknowledged=true;
        await NavigateToViewAsync(Runtime.AllowedViews.Contains("recovery") ? "recovery" : "alarms");
    }
    async void OpenDevicesClick(object sender,RoutedEventArgs e) => await NavigateToViewAsync(Runtime.AllowedViews.Contains("diagnostics") ? "diagnostics" : "maintenance");
    async void OpenMaintenanceClick(object sender,RoutedEventArgs e) => await NavigateToViewAsync("maintenance");
    async void OpenExtensionsClick(object sender,RoutedEventArgs e) => await NavigateToViewAsync("extensions");
    async void OpenEngineeringClick(object sender,RoutedEventArgs e) => await NavigateToViewAsync("engineering");
    void OpenIdentityClick(object sender,RoutedEventArgs e) { CloseDrawer(); entryRequested=true; UpdateEntryProjection(); }
    void ContinueEntryClick(object sender,RoutedEventArgs e)
    {
        if (!HasIdentity) { model.LastResult="请先取得有效参考身份"; return; }
        entryRequested=false; recoveryEntryAcknowledged=true; boxEntryFocus.Request(); UpdateEntryProjection(); UpdateGuards();
    }
    void MaximizeClick(object sender,RoutedEventArgs e) => WindowState=WindowState.Maximized;
    void UpdateEntryProjection()
    {
        if (EntryOverlay is null) return;
        var session=snapshot?.State.Session;
        var recovery = snapshot?.Requests.Any(x=>x.Status is RequestStatus.Unknown or RequestStatus.Dispatching) == true || snapshot?.State.Quarantined==true;
        EntryOverlay.Visibility = !ready || !HasIdentity || entryRequested || recovery && !recoveryEntryAcknowledged ? Visibility.Visible : Visibility.Collapsed;
        if (!IsDrawerOpen) SharedContext.IsEnabled = EntryOverlay.Visibility != Visibility.Visible;
        IdentityEntry.Visibility = Runtime.Mode == RunMode.Simulation ? Visibility.Visible : Visibility.Collapsed;
        EntryTitle.Text = !ready ? "启动自检 / NotReady" : !HasIdentity ? "登录供包工作台" : recovery ? "恢复旧箱与原请求" : "当前模拟身份";
        EntryDescription.Text = !ready ? "装配或状态读取未就绪。保留原始诊断，不以连接或假进度代替就绪。" : !HasIdentity ? "身份未取得或已过期。当前诊断可见；历史、详情和业务命令需要有效身份。" : recovery ? "发现原请求或恢复隔离；先核对原请求、接受账及实体，不生成替代ID。" : "切换参考角色会被应用层再次校验；未完成责任不会因登出而清除。";
        StartupChecks.Text = $"配置：{Runtime.Configuration?.Version ?? "未取得"} · 模式 {Runtime.Mode?.ToString() ?? "未取得"}\n运行所有者：{Runtime.RuntimeId}\n当前箱：{session?.Barcode ?? "未取得"} / {(session is null ? "无活动箱" : OperatorText.Session(session.Status))}\n原请求：{snapshot?.Requests.Count(x=>x.Status is RequestStatus.Unknown or RequestStatus.Dispatching or RequestStatus.Prepared).ToString() ?? "未取得"} 项未决\n{model.HealthSummary}\n" + (Runtime.RuntimeDiagnostics.Count>0 ? string.Join("；",Runtime.RuntimeDiagnostics) : "实际来源、存储与业务守卫见持续状态；没有真实SDK或现场准入结论。");
        EntryContinueButton.IsEnabled=HasIdentity;
        EntryRecoveryButton.IsEnabled=ready;
        var canOpen = session is null || session.Status is SessionStatus.Closed or SessionStatus.Rejected;
        BoxEntryPanel.Visibility=canOpen ? Visibility.Visible : Visibility.Collapsed;
        CurrentBoxPanel.Visibility=canOpen ? Visibility.Collapsed : Visibility.Visible;
        CloseDecisionButton.Content=session?.CloseRemaining == 0 ? "确认正常关闭" : session?.CloseRemaining is null ? "关闭检查数量未取得" : "查看少货关闭检查";
        CloseDecisionButton.Visibility=session?.Status == SessionStatus.AwaitingShortageDecision ? Visibility.Visible : Visibility.Collapsed;
        CloseButton.Visibility=session?.Status == SessionStatus.AwaitingShortageDecision ? Visibility.Collapsed : Visibility.Visible;
        boxEntryFocus.Observe(session?.Id, canOpen, HasIdentity ? snapshot?.State.Actor : null);
        TryFocusBoxInput();
    }
    bool TryFocusBoxInput()
    {
        bool Available() => !closing && !busy && IsLoaded && HasIdentity && !smallArea && !layoutPauseRequired && IsWorkstationActive && BoxInput.IsVisible && BoxInput.IsEnabled;
        if (!boxEntryFocus.TryTake(Available(), out var ticket)) return false;
        var actor = snapshot!.State.Actor; var sessionId = snapshot.State.Session?.Id;
        _ = Dispatcher.InvokeAsync(() =>
        {
            // A drawer, identity change or new box may arrive before this queued handoff.
            if (!boxEntryFocus.IsCurrent(ticket) || !Available() || snapshot is not { } current || current.State.Actor != actor || current.State.Session?.Id != sessionId || !BoxInput.Focus()) { boxEntryFocus.Retry(ticket); return; }
            BoxInput.SelectAll(); // Retain the draft, but the next scanner input replaces it.
        }, DispatcherPriority.Input);
        return true;
    }
    void ApplyWorkstationLayout()
    {
        if (LaneGrid is null || SmallAreaWarning is null) return;
        var insufficient = ClientArea.ActualWidth < MinimumClientWidth || ClientArea.ActualHeight < MinimumClientHeight;
        // The first measure often happens before Loaded. Latch the protection request
        // whenever actual measured space is insufficient, including that first measure.
        // It remains latched after enlarging until an explicit successful Resume.
        if (ClientArea.ActualWidth > 0 && ClientArea.ActualHeight > 0 && insufficient) layoutPauseRequired=true;
        smallArea=insufficient;
        SmallAreaWarning.Visibility=smallArea ? Visibility.Visible : Visibility.Collapsed;
        var dense = compact || ClientArea.ActualWidth <=1550 || ClientArea.ActualHeight<=880;
        // Both lanes remain separate and simultaneously visible at every admitted size.
        LeftLane.ApplyDensity(dense); RightLane.ApplyDensity(dense);
        RecentGrid.Visibility=dense ? Visibility.Collapsed : Visibility.Visible;
        LaneGrid.Margin=new Thickness(dense ? 18 : 24,4,dense ? 18 : 24,8);
        DrawerPanel.Width=Math.Max(320,Math.Min(1180,ClientArea.ActualWidth-70));
        if (HistoryBody is not null) ApplyHistoryLayout();
        LayoutButton.Content=$"{(compact ? "紧凑偏好" : "标准偏好")} · 双通道";
        UpdateGuards();
    }
    Task EnsureLayoutPauseAsync()
    {
        // SetWindowAreaAsync owns the persistent application pause, even before login.
        // An explicit Resume in another valid window is shared authority; do not
        // recreate a pause from this window's already-acknowledged local latch.
        if (snapshot?.State.Blocks.Any(x => x.Code == "OperatorPaused") == true) layoutPauseAcknowledged = true;
        else if (layoutPauseAcknowledged && !smallArea && Fresh)
        { layoutPauseRequired = false; layoutPauseAcknowledged = false; }
        return Task.CompletedTask;
    }
    void ObserveReadIdentity()
    {
        var actor=HasIdentity ? snapshot?.State.Actor : null;
        if (Equals(observedReadActor,actor)) return;
        var hadIdentity=observedReadActor is not null;
        observedReadActor=actor; readIdentityGeneration++;
        historyGeneration++; detailGeneration++; alarmHistoryGeneration++; loadedHistory=null;
        var feedback=actor is null ? "历史 / 详情访问已失效：未登录、已登出或已过期；可见旧结果仅为既有阅读快照。" : hadIdentity ? "身份已更换；旧结果失效，请重新查询。" : "已取得有效身份；已有结果仍为旧快照，需重新查询。";
        ReadAccessFeedback.Text=HistoryFeedback.Text=AlarmHistoryFeedback.Text=feedback;
        ConfigFeedback.Text=feedback;
        HistoryGrid.IsEnabled=AlarmHistoryGrid.IsEnabled=DetailTree.IsEnabled=ConfigsList.IsEnabled=false;
    }
    bool BeginHistoricalRead(string context,out Actor actor)
    {
        ObserveReadIdentity();
        actor=snapshot?.State.Actor!;
        if (HasIdentity) return true;
        var feedback=$"{context}未读取：有效身份未取得或已失效。已有结果是旧快照，当前诊断不受影响。";
        model.LastResult=ReadAccessFeedback.Text=HistoryFeedback.Text=AlarmHistoryFeedback.Text=feedback;
        DetailTree.IsEnabled=false; return false;
    }
    bool ReadIdentityMatches(Actor actor)
    {
        var matches=HasIdentity && Equals(snapshot?.State.Actor,actor);
        if (!matches) ObserveReadIdentity();
        return matches;
    }
    void ApplyReadControlGuards()
    {
        if (!HasIdentity) { HistoryPrevious.IsEnabled=HistoryNext.IsEnabled=false; }
    }
    void MarkDetailReadFailure(string message)
    {
        DetailTree.IsEnabled=false;
        HistoryFeedback.Text=ReadAccessFeedback.Text=model.LastResult=message+"；已有详情是旧阅读快照，未作为本次读取成功。";
    }
}

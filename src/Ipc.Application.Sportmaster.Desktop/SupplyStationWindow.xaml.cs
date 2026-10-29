using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;

namespace Ipc.Application.Sportmaster.Desktop;

public partial class SupplyStationWindow : Window
{
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    readonly Grid work = new();
    readonly HistoryPage history = new();
    readonly MaintenancePage maintenance = new();
    FrameworkElement? diagnostics;
    readonly Func<Task>? stopOwner;
    readonly SupplyStationWindow root;
    readonly HashSet<SupplyStationWindow> windows;
    Task pendingRefresh = Task.CompletedTask;
    bool compact, closing, closeReady, closed, pendingMaintenanceLoad;
    long closeGeneration;
    public SupplyStationViewModel ViewModel { get; }

    public SupplyStationWindow(IParcelApplication application, string? view, string? layout, Func<Task>? stopOwner = null, SupplyStationWindow? owner = null)
    {
        InitializeComponent(); ProductBrand.Apply(this); UiTypography.Apply(this);
        ViewModel = new(application); DataContext = ViewModel; this.stopOwner = stopOwner;
        ViewModel.RequestActivationAsync = RequestActivation;
        ViewModel.RequestRetentionAsync = RequestRetention;
        WindowOpeningPreference.Attach(this, "sportmaster", message => ViewModel.Message = message);
        // Cached pages retain their binding source while detached from PageHost.
        // Inherited DataContext would transiently clear/rebuild the draft controls.
        maintenance.DataContext = ViewModel; history.DataContext = ViewModel;
        maintenance.DisplayPreferencesRequested += (_, _) => Layout_Click(this, new RoutedEventArgs());
        maintenance.DiagnosticsRequested += (_, target) => ShowObjectDiagnostics(target);
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ViewModel.IsManagementMode)) EnforceManagementModeView();
            if (e.PropertyName == nameof(ViewModel.Busy) && !ViewModel.Busy && pendingMaintenanceLoad)
                Dispatcher.BeginInvoke(new Action(async () => await LoadRequestedMaintenanceAsync()));
        };
        ClientArea.SizeChanged += ViewportChanged;
        root = owner?.root ?? this; windows = owner?.windows ?? []; windows.Add(this);
        if (owner is not null) Owner = owner;
        work.ColumnDefinitions.Add(new()); work.ColumnDefinitions.Add(new());
        var left = new SupplyStationPosition { DataContext = ViewModel.Left };
        var right = new SupplyStationPosition { DataContext = ViewModel.Right };
        Grid.SetColumn(right, 1); work.Children.Add(left); work.Children.Add(right);
        BuildOperatorWorkspace();
        try { compact = layout is null ? Ipc.Platform.PresentationPreferences.Read("sportmaster", "supply-station") ?? false : layout == "compact"; }
        catch (Exception error) when (error is System.IO.IOException or System.Text.Json.JsonException or UnauthorizedAccessException) { ViewModel.Message = "展示偏好读取失败，原文件保留：" + error.GetType().Name; }
        ApplyLayout();
        var initialPage = view?.EndsWith("-history", StringComparison.Ordinal) == true ? "history" : view?.EndsWith("-maintenance", StringComparison.Ordinal) == true ? "maintenance" : "workstation";
        if (initialPage == "maintenance" && !ViewModel.IsManagementMode) initialPage = "workstation";
        SetPage(initialPage);
        timer.Tick += (_, _) =>
        {
            ViewModel.RefreshFreshness();
            if (pendingRefresh.IsCompleted && !closing && !closed) pendingRefresh = RefreshAsync();
        };
        Loaded += async (_, _) =>
        {
            if (ActualWidth > SystemParameters.WorkArea.Width || ActualHeight > SystemParameters.WorkArea.Height) WindowState = WindowState.Maximized;
            pendingRefresh = RefreshAsync(); await pendingRefresh;
            if (closing || closed) return;
            UpdateViewport();
            timer.Start();
            if (initialPage == "maintenance") { pendingMaintenanceLoad = true; await LoadRequestedMaintenanceAsync(); }
            if (initialPage == "history") await ViewModel.RunAsync(() => ViewModel.OpenHistoryAsync(), "读取作业记录");
        };
        Closing += ClosingWindow;
        Closed += (_, _) => { closed = true; timer.Stop(); windows.Remove(this); };
        var progress = new DispatcherProgressMonitor(application, Dispatcher); Closed += (_, _) => progress.Dispose();
    }
    async Task RefreshAsync()
    {
        try { await ViewModel.RefreshDisplayAsync(); EnforceManagementModeView(); }
        catch (Exception) { /* RefreshDisplayAsync retains the failure and invalidates this window's projection. */ }
    }
    void SetPage(string page)
    {
        if (page is "maintenance" or "diagnostics" or "logs" && !ViewModel.IsManagementMode) { ViewModel.Message = "请先进入管理模式。"; page = "workstation"; }
        if (page != "maintenance") pendingMaintenanceLoad = false;
        ViewModel.SelectedPage = page; ApplyLayout();
        PageHost.Content = page switch { "history" => history, "maintenance" => maintenance, "diagnostics" => diagnostics ??= CreateDiagnostics(), "logs" => logs ??= new SoftwareLogControl((IWorkstationAdministration)ViewModel.Application), _ => workContent };
    }
    void DismissGlobalFeedback_Click(object sender, RoutedEventArgs e) => ViewModel.DismissGlobalOperationFeedback();
    void Workstation_Click(object sender, RoutedEventArgs e) => SetPage("workstation");
    async void History_Click(object sender, RoutedEventArgs e) { SetPage("history"); if (!ViewModel.HistoryLoaded) await ViewModel.RunAsync(() => ViewModel.OpenHistoryAsync(), "作业记录"); }
    async void Maintenance_Click(object sender, RoutedEventArgs e)
    { SetPage("maintenance"); pendingMaintenanceLoad = !ViewModel.HasLoadedConfiguration; await LoadRequestedMaintenanceAsync(); }
    async Task LoadRequestedMaintenanceAsync()
    {
        if (!pendingMaintenanceLoad) return;
        if (closing || closed || ViewModel.SelectedPage != "maintenance") { pendingMaintenanceLoad = false; return; }
        if (ViewModel.Busy) return;
        pendingMaintenanceLoad = false; // A failure stays visible; never start an automatic retry loop.
        await ViewModel.RunAsync(ViewModel.EnsureMaintenanceLoadedAsync, "读取参数配置");
    }
    void Layout_Click(object sender, RoutedEventArgs e)
    {
        compact = !compact; ApplyLayout();
        try { Ipc.Platform.PresentationPreferences.Save("sportmaster", "supply-station", compact); }
        catch (Exception error) when (error is System.IO.IOException or System.Text.Json.JsonException or UnauthorizedAccessException) { ViewModel.Message = "本窗口布局已切换；展示偏好未保存：" + error.GetType().Name; }
    }
    void ApplyLayout()
    {
        ViewModel.IsCompact = compact || ClientArea.ActualWidth < 1280 || ClientArea.ActualHeight < 680;
        ViewModel.IsTight = ClientArea.ActualHeight < 540;
        FontSize = UiTypography.Body; work.Margin = ViewModel.IsCompact ? new Thickness(0) : new Thickness(4);
    }
    void ViewportChanged(object sender, SizeChangedEventArgs e) => UpdateViewport();
    void UpdateViewport()
    {
        if (closing || closed || ClientArea.ActualWidth <= 0 || ClientArea.ActualHeight <= 0) return;
        ApplyLayout();
        ViewModel.SetViewport(ClientArea.ActualWidth, ClientArea.ActualHeight);
    }
    void Maximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Maximized;
    public async Task ShowDetailsAsync(ParcelOperation operation) { SetPage("history"); await ViewModel.RunAsync(() => ViewModel.OpenHistoryAsync(operation.Id), "读取原作业详情"); }
    public Func<int, bool>? ConfirmDiscardDrafts { get; set; }
    public Func<string, bool>? ConfirmOwnerExit { get; set; }
    public Func<Task>? ReopenOwnerAsync { get; set; }
    public Func<Task>? OpenRetentionOwnerAsync { get; set; }
    bool retentionRequested;
    async Task RequestRetention()
    {
        try { await ViewModel.RequireManagementModeAsync(); await ViewModel.PauseAsync(); await ViewModel.RefreshAsync(); if (ViewModel.Snapshot?.Paused != true || !ViewModel.Fresh) throw new InvalidOperationException("暂停尚未确认"); if (root.OpenRetentionOwnerAsync is null) throw new InvalidOperationException("当前宿主没有独立维护入口"); root.retentionRequested = true; root.Close(); }
        catch (Exception error) { ViewModel.Message = "尚未进入到期维护：" + error.Message; }
    }
    bool activationRequested;
    async Task RequestActivation()
    {
        try
        {
            if (root.ReopenOwnerAsync is null) throw new InvalidOperationException("此宿主没有受控重开入口；可稍后从原合法入口打开。");
            await ViewModel.CheckActivationAsync(); root.activationRequested = true; root.Close();
        }
        catch (Exception error) { ViewModel.Message = "尚未进入配置生效流程：" + error.Message; }
    }
    async void ClosingWindow(object? sender, CancelEventArgs e)
    {
        if (closeReady || closed) return;
        e.Cancel = true;
        if (closing) return;
        closing = true;
        var generation = ++closeGeneration;
        var affected = stopOwner is null ? new[] { this } : windows.ToArray();
        foreach (var window in affected) { if (window != this) window.closeGeneration++; window.closing = true; window.timer.Stop(); window.IsEnabled = false; }
        try
        {
            // Close cannot re-enter a native WPF Closing event even when every drain task is already complete.
            await System.Windows.Threading.Dispatcher.Yield();
            await Task.WhenAll(affected.SelectMany(x => new[] { x.pendingRefresh, x.ViewModel.DrainAsync() })).WaitAsync(TimeSpan.FromSeconds(5));
            // A root close owns the entire group, including an auxiliary close already draining.
            if (generation != closeGeneration || this != root && root.closing) return;
            if (stopOwner is not null && !(ConfirmOwnerExit?.Invoke(ViewModel.ExitImpact(activationRequested)) ?? ConfigurationCloseGuard.ConfirmExit(this, ViewModel.ExitImpact(activationRequested)))) { RestoreAfterClose(); return; }
            var dirtyWindows = affected.Where(x => !x.closed && windows.Contains(x) && ConfigurationCloseGuard.HasUnsaved(x.ViewModel, x.maintenance)).ToArray();
            var dirty = dirtyWindows.Length;
            if (dirty != 0 && !(ConfirmDiscardDrafts?.Invoke(dirty) ?? await ConfigurationCloseGuard.ReviewUnsavedAsync(this, dirty,
                dirty == 1 ? dirtyWindows[0].ViewModel.SaveIncompleteEditorAsync : null, "关闭")))
            {
                ViewModel.Message = "已取消关闭；未保存草稿、选择与配置修订已保留。";
                RestoreAfterClose(); return;
            }
            if (activationRequested || retentionRequested) await ViewModel.RequireManagementModeAsync();
            if (stopOwner is not null) await stopOwner();
            if (activationRequested && ReopenOwnerAsync is not null) await ReopenOwnerAsync();
            if (retentionRequested && OpenRetentionOwnerAsync is not null) await OpenRetentionOwnerAsync();
            closeReady = true;
            foreach (var child in affected.Where(x => x != this && !x.closed && windows.Contains(x))) { child.closeReady = true; child.Close(); }
            Close();
        }
        catch (Exception ex)
        {
            ViewModel.Message = "停止未完成，仍持有资源：" + ex.Message;
            // The root owns recovery while it is draining the same child. A child
            // failure must not restart polling underneath an in-progress owner stop.
            if (generation != closeGeneration || this != root && root.closing) return;
            RestoreAfterClose();
        }
        void RestoreAfterClose()
        {
            activationRequested = false;
            retentionRequested = false;
            closing = false;
            foreach (var window in affected.Where(x => !x.closed && windows.Contains(x)))
            { window.closing = false; window.IsEnabled = true; window.timer.Start(); }
        }
    }
}

using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Platform;

namespace Ipc.Application.Sportmaster.Desktop;

public partial class SupplyStationWindow : Window
{
    partial void InitializePresentationFeedback();
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    readonly Grid work = new();
    readonly HistoryPage history = new();
    readonly MaintenancePage maintenance = new();
    readonly Func<Task>? stopOwner;
    readonly SupplyStationWindow root;
    readonly HashSet<SupplyStationWindow> windows;
    Task pendingRefresh = Task.CompletedTask;
    bool compact, closing, closeReady, closed;
    long closeGeneration;
    public SupplyStationViewModel ViewModel { get; }

    public SupplyStationWindow(IParcelApplication application, string? view, string? layout, Func<Task>? stopOwner = null, SupplyStationWindow? owner = null)
    {
        InitializeComponent(); ProductBrand.Apply(this); UiTypography.Apply(this);
        maintenance.Audience = SmParameterAudience.Field;
        TestInstallationLabel.Visibility = InstallationIdentity.IsTest ? Visibility.Visible : Visibility.Collapsed;
        ViewModel = new(application); DataContext = ViewModel; InitializePresentationFeedback(); this.stopOwner = stopOwner;
        ViewModel.ParameterAudience = SmParameterAudience.Field;
        ViewModel.RequireOwnedManagementSession = true;
        InitializeAudio();
        ViewModel.RequestActivationAsync = RequestActivation;
        WindowOpeningPreference.Attach(this, "sportmaster", message => ViewModel.Message = message);
        // Cached pages retain their binding source while detached from PageHost.
        // Inherited DataContext would transiently clear/rebuild the draft controls.
        maintenance.CameraPreviewFactory = instance => { var preview = new StationCameraPreviewControl(ViewModel, 110, false); preview.SelectDevice(ViewModel.ReaderDrafts.FirstOrDefault(r => r.Capability.InstanceId == instance)?.Original.DeviceId ?? instance); return preview; };
        maintenance.DataContext = ViewModel; history.DataContext = ViewModel;
        maintenance.DisplayPreferencesRequested += (_, _) => Layout_Click(this, new RoutedEventArgs());
        history.TechnicalLogNavigation = false; history.TechnicalMaintenance = false;
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ViewModel.IsManagementMode)) EnforceManagementModeView();
        };
        ClientArea.SizeChanged += ViewportChanged;
        root = owner?.root ?? this; windows = owner?.windows ?? []; windows.Add(this);
        if (owner is not null) Owner = owner;
        work.ColumnDefinitions.Add(new()); work.ColumnDefinitions.Add(new());
        Grid.SetIsSharedSizeScope(work, true);
        var left = new SupplyStationPosition { DataContext = ViewModel.Left };
        var right = new SupplyStationPosition { DataContext = ViewModel.Right };
        Grid.SetColumn(right, 1); work.Children.Add(left); work.Children.Add(right);
        BuildOperatorWorkspace();
        try { compact = layout is null ? Ipc.Platform.PresentationPreferences.Read("sportmaster", "supply-station") ?? false : layout == "compact"; }
        catch (Exception error) when (error is System.IO.IOException or System.Text.Json.JsonException or UnauthorizedAccessException) { ViewModel.Message = "展示偏好未能读取，原文件保留；请检查本机设置文件与访问权限。"; }
        ApplyLayout();
        var initialPage = view?.EndsWith("-history", StringComparison.Ordinal) == true ? "history" : "workstation";
        SetPage(initialPage);
        timer.Tick += (_, _) =>
        {
            UpdateStationClock();
            ViewModel.RefreshFreshness();
            EnforceManagementModeView();
            if (ViewModel.Application is not IParcelPresentationSource && pendingRefresh.IsCompleted && !closing && !closed) pendingRefresh = RefreshAsync();
        };
        Loaded += async (_, _) =>
        {
            UpdateStationClock();
            if (ActualWidth > SystemParameters.WorkArea.Width || ActualHeight > SystemParameters.WorkArea.Height) WindowState = WindowState.Maximized;
            pendingRefresh = RefreshAsync(); await pendingRefresh;
            if (closing || closed) return;
            UpdateViewport();
            RestoreBarcodeFocus();
            timer.Start();
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
        if (page is not ("workstation" or "history")) throw new ArgumentException("工作站仅提供作业与记录；现场设置使用独立非模态窗口。");
        ViewModel.SelectedPage = page; ApplyLayout();
        PageHost.Content = page == "history" ? PositionHistory() : workContent;
    }
    void UpdateStationClock()
    {
        var now = DateTimeOffset.Now;
        StationClock.Text = now.ToString("yyyy-MM-dd HH:mm:ss");
        StationClock.ToolTip = now.ToString("yyyy-MM-dd HH:mm:ss zzz");
    }
    void DismissGlobalFeedback_Click(object sender, RoutedEventArgs e) => ViewModel.DismissGlobalOperationFeedback();
    async void ResumeSupply_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.CanResumeInViewport) await ViewModel.RunAsync(() => ViewModel.CommandAsync("Resume"), "恢复供包");
    }
    async void RcsConnection_Click(object sender, RoutedEventArgs e) => await ViewModel.CheckRcsAsync();
    void Workstation_Click(object sender, RoutedEventArgs e) => SetPage("workstation");
    async void History_Click(object sender, RoutedEventArgs e) { SetPage("history"); if (!ViewModel.HistoryLoaded) await ViewModel.RunAsync(() => ViewModel.OpenHistoryAsync(), "作业记录"); }
    void Layout_Click(object sender, RoutedEventArgs e)
    {
        compact = !compact; ApplyLayout();
        try { Ipc.Platform.PresentationPreferences.Save("sportmaster", "supply-station", compact); }
        catch (Exception error) when (error is System.IO.IOException or System.Text.Json.JsonException or UnauthorizedAccessException) { ViewModel.Message = "本窗口布局已切换；展示偏好未保存，请检查本机设置文件与访问权限。"; }
    }
    void ApplyLayout()
    {
        ViewModel.ChuteScale = Math.Min(ClientArea.ActualWidth / 1366, ClientArea.ActualHeight / 768);
        var constrained = ClientArea.ActualWidth < 1280 || ClientArea.ActualHeight < 680;
        ViewModel.IsCompact = compact || constrained;
        ViewModel.LayoutDescription = "布局偏好：" + (compact ? "紧凑" : "标准") + (constrained ? "；作业窗口小于 1280×680 DIP，实际使用紧凑布局。放大窗口后按偏好显示。" : "；作业窗口当前按偏好显示。");
        ViewModel.IsTight = ClientArea.ActualHeight < 680;
        ApplyNavigation();
        FontSize = UiTypography.Body; work.Margin = ViewModel.IsCompact ? new Thickness(0) : new Thickness(4);
    }
    void ViewportChanged(object sender, SizeChangedEventArgs e) => UpdateViewport();
    void UpdateViewport()
    {
        if (closing || closed || ClientArea.ActualWidth <= 0 || ClientArea.ActualHeight <= 0) return;
        ApplyLayout();
        ViewModel.SetViewport(ClientArea.ActualWidth, ClientArea.ActualHeight);
    }
    void MinimizeFullScreen_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    void ExitFullScreen_Click(object sender, RoutedEventArgs e) => Close();
    void Maximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Maximized;
    public Task ShowDetailsAsync(ParcelOperation operation) => ShowDetailsAsync(operation.Id);
    public async Task ShowDetailsAsync(string operationId) { SetPage("history"); await ViewModel.RunAsync(() => ViewModel.OpenHistoryAsync(operationId), "读取作业详情"); history.ShowTaskDetails(); }
    public Func<int, bool>? ConfirmDiscardDrafts { get; set; }
    public Func<string, bool>? ConfirmOwnerExit { get; set; }
    public Func<Task>? ReopenOwnerAsync { get; set; }
    bool activationRequested;
    async Task RequestActivation()
    {
        try
        {
            if (root.ReopenOwnerAsync is null) throw new InvalidOperationException("无法从这里重新打开，请稍后从桌面图标重新打开软件。");
            await ViewModel.CheckActivationAsync(); root.activationRequested = true; root.Close();
        }
        catch (Exception error) { ViewModel.Message = "尚未进入配置生效流程：" + error.Message; }
    }
    bool ActivateSettingsWindow()
    {
        if (settingsWindow is not { } settings) return false;
        if (!settings.IsVisible) settings.Show();
        settings.Activate(); return true;
    }
    async void ClosingWindow(object? sender, CancelEventArgs e)
    {
        if (closeReady || closed) return;
        e.Cancel = true;
        if (!activationRequested && ActivateSettingsWindow()) { ViewModel.Message = "请先关闭设置窗口，再退出供包台。未保存输入保留。"; return; }
        if (closing) return;
        closing = true;
        var generation = ++closeGeneration;
        var affected = stopOwner is null ? new[] { this } : windows.ToArray();
        foreach (var window in affected) { if (window != this) window.closeGeneration++; window.closing = true; window.timer.Stop(); window.IsEnabled = false; if (window.settingsWindow is { } settings) settings.IsEnabled = false; }
        var ownerStopped = false;
        try
        {
            // Close cannot re-enter a native WPF Closing event even when every drain task is already complete.
            await System.Windows.Threading.Dispatcher.Yield();
            await Task.WhenAll(affected.SelectMany(x => new[] { x.pendingRefresh, x.ViewModel.DrainAsync() })).WaitAsync(TimeSpan.FromSeconds(5));
            // A root close owns the entire group, including an auxiliary close already draining.
            if (generation != closeGeneration || this != root && root.closing) return;
            if (stopOwner is not null && !(ConfirmOwnerExit?.Invoke(ViewModel.ExitImpact(activationRequested)) ?? ConfirmStationExit())) { RestoreAfterClose(); return; }
            var dirtyWindows = affected.Where(x => !x.closed && windows.Contains(x) && ConfigurationCloseGuard.HasUnsaved(x.ViewModel, x.maintenance)).ToArray();
            var dirty = dirtyWindows.Length;
            if (dirty != 0 && !(ConfirmDiscardDrafts?.Invoke(dirty) ?? await ConfigurationCloseGuard.ReviewUnsavedAsync(ManagementDialogOwner, dirty,
                dirty == 1 ? dirtyWindows[0].ViewModel.SaveIncompleteEditorAsync : null, "关闭")))
            {
                ViewModel.Message = "已取消关闭；未保存草稿、选择与配置修订已保留。";
                RestoreAfterClose(); return;
            }
            if (activationRequested)
            {
                await ViewModel.RequireManagementModeAsync();
                foreach (var window in affected)
                    if (window.settingsWindow is { } settings) await settings.CloseForOwnerStopAsync();
            }
            if (stopOwner is not null) { await stopOwner(); ownerStopped = true; }
            if (activationRequested && ReopenOwnerAsync is not null) await ReopenOwnerAsync();
            closeReady = true;
            foreach (var child in affected.Where(x => x != this && !x.closed && windows.Contains(x))) { child.closeReady = true; child.Close(); }
            Close();
        }
        catch (Exception ex)
        {
            if (ownerStopped)
            {
                MessageBox.Show(this, "供包台已关闭，后续维护入口未能打开。请重新打开软件核对。", "维护入口未打开");
                closeReady = true;
                foreach (var child in affected.Where(x => x != this && !x.closed && windows.Contains(x))) { child.closeReady = true; child.Close(); }
                Close(); return;
            }
            ViewModel.Message = "停止未完成，仍持有资源：" + ex.Message;
            // The root owns recovery while it is draining the same child. A child
            // failure must not restart polling underneath an in-progress owner stop.
            if (generation != closeGeneration || this != root && root.closing) return;
            RestoreAfterClose();
        }
        void RestoreAfterClose()
        {
            activationRequested = false;
            closing = false;
            foreach (var window in affected.Where(x => !x.closed && windows.Contains(x)))
            { window.closing = false; window.IsEnabled = true; if (window.settingsWindow is { } settings) settings.IsEnabled = true; window.timer.Start(); }
        }
    }
}

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
    Task pendingViewportPause = Task.CompletedTask;
    bool compact, closing, closeReady, closed;
    long closeGeneration;
    public SupplyStationViewModel ViewModel { get; }

    public SupplyStationWindow(IParcelApplication application, string? view, string? layout, Func<Task>? stopOwner = null, SupplyStationWindow? owner = null)
    {
        InitializeComponent(); ProductBrand.Apply(this);
        ViewModel = new(application); DataContext = ViewModel; this.stopOwner = stopOwner;
        // Cached pages retain their binding source while detached from PageHost.
        // Inherited DataContext would transiently clear/rebuild the draft controls.
        maintenance.DataContext = ViewModel; history.DataContext = ViewModel;
        maintenance.DisplayPreferencesRequested += (_, _) => Layout_Click(this, new RoutedEventArgs());
        ClientArea.SizeChanged += ViewportChanged;
        root = owner?.root ?? this; windows = owner?.windows ?? []; windows.Add(this);
        if (owner is not null) Owner = owner;
        work.ColumnDefinitions.Add(new()); work.ColumnDefinitions.Add(new());
        var left = new SupplyStationPosition { DataContext = ViewModel.Left };
        var right = new SupplyStationPosition { DataContext = ViewModel.Right };
        Grid.SetColumn(right, 1); work.Children.Add(left); work.Children.Add(right);
        try { compact = layout is null ? Ipc.Platform.PresentationPreferences.Read("sportmaster", "supply-station") ?? false : layout == "compact"; }
        catch (Exception error) when (error is System.IO.IOException or System.Text.Json.JsonException or UnauthorizedAccessException) { ViewModel.Message = "展示偏好读取失败，原文件保留：" + error.GetType().Name; }
        ApplyLayout();
        var initialPage = view?.EndsWith("-history", StringComparison.Ordinal) == true ? "history" : view?.EndsWith("-maintenance", StringComparison.Ordinal) == true ? "maintenance" : "workstation";
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
            if (initialPage == "maintenance") await ViewModel.RunAsync(ViewModel.EnsureMaintenanceLoadedAsync, "读取维护配置");
            if (initialPage == "history") await ViewModel.RunAsync(() => ViewModel.OpenHistoryAsync(), "读取作业记录");
        };
        Closing += ClosingWindow;
        Closed += (_, _) => { closed = true; timer.Stop(); windows.Remove(this); };
    }
    async Task RefreshAsync()
    {
        try { await ViewModel.RefreshDisplayAsync(); }
        catch (Exception) { /* RefreshDisplayAsync retains the failure and invalidates this window's projection. */ }
    }
    void SetPage(string page)
    {
        ViewModel.SelectedPage = page; ApplyLayout();
        PageHost.Content = page switch { "history" => history, "maintenance" => maintenance, "diagnostics" => diagnostics ??= CreateDiagnostics(), _ => work };
    }
    void Workstation_Click(object sender, RoutedEventArgs e) => SetPage("workstation");
    void Support_Click(object sender, RoutedEventArgs e) => new ProductSupportWindow(ViewModel.Application, ViewModel.Selected?.Id) { Owner = this }.Show();
    async void History_Click(object sender, RoutedEventArgs e) { SetPage("history"); if (!ViewModel.HistoryLoaded) await ViewModel.RunAsync(() => ViewModel.OpenHistoryAsync(), "作业记录"); }
    async void Maintenance_Click(object sender, RoutedEventArgs e) { SetPage("maintenance"); await ViewModel.RunAsync(ViewModel.EnsureMaintenanceLoadedAsync, "参数配置"); }
    void Window_Click(object sender, RoutedEventArgs e)
    {
        if (!root.closing) new SupplyStationWindow(ViewModel.Application, null, compact ? "compact" : "standard", owner: root).Show();
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
        FontSize = ViewModel.IsSettingsWorkspace || ViewModel.IsCompact ? 14 : 16; work.Margin = ViewModel.IsCompact ? new Thickness(0) : new Thickness(4);
    }
    void ViewportChanged(object sender, SizeChangedEventArgs e) => UpdateViewport();
    void UpdateViewport()
    {
        if (closing || closed || ClientArea.ActualWidth <= 0 || ClientArea.ActualHeight <= 0) return;
        ApplyLayout();
        if (ViewModel.SetViewport(ClientArea.ActualWidth, ClientArea.ActualHeight)) QueueViewportPause();
    }
    void QueueViewportPause()
    {
        if (!closing && !closed && pendingViewportPause.IsCompleted) pendingViewportPause = ViewModel.PauseAsync();
    }
    void Maximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Maximized;
    void ViewportPause_Click(object sender, RoutedEventArgs e) => QueueViewportPause();
    void ViewportAcknowledge_Click(object sender, RoutedEventArgs e) => ViewModel.AcknowledgeViewport();
    public async Task ShowDetailsAsync(ParcelOperation operation) { SetPage("history"); await ViewModel.RunAsync(() => ViewModel.OpenHistoryAsync(operation.Id), "读取原作业详情"); }
    async void Pause_Click(object sender, RoutedEventArgs e) => await ViewModel.PauseAsync();
    async void Resume_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.CanResumeInViewport) await ViewModel.RunAsync(() => ViewModel.CommandAsync("Resume"), "恢复新入场");
    }
    public Func<int, bool>? ConfirmDiscardDrafts { get; set; }
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
            await Task.WhenAll(affected.SelectMany(x => new[] { x.pendingRefresh, x.pendingViewportPause, x.ViewModel.DrainAsync() })).WaitAsync(TimeSpan.FromSeconds(5));
            // A root close owns the entire group, including an auxiliary close already draining.
            if (generation != closeGeneration || this != root && root.closing) return;
            var dirtyWindows = affected.Where(x => !x.closed && windows.Contains(x) && ConfigurationCloseGuard.HasUnsaved(x.ViewModel, x.maintenance)).ToArray();
            var dirty = dirtyWindows.Length;
            if (dirty != 0 && !(ConfirmDiscardDrafts?.Invoke(dirty) ?? await ConfigurationCloseGuard.ReviewUnsavedAsync(this, dirty,
                dirty == 1 ? dirtyWindows[0].ViewModel.SaveIncompleteEditorAsync : null, "关闭")))
            {
                ViewModel.Message = "已取消关闭；未保存草稿、选择与配置修订已保留。";
                RestoreAfterClose(); return;
            }
            if (stopOwner is not null) await stopOwner();
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
            closing = false;
            foreach (var window in affected.Where(x => !x.closed && windows.Contains(x)))
            { window.closing = false; window.IsEnabled = true; window.timer.Start(); }
        }
    }
}

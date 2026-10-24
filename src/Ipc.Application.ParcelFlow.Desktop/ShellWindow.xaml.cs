using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
namespace Ipc.Application.ParcelFlow.Desktop;

public partial class ShellWindow : Window
{
    static readonly DependencyPropertyKey SelectedWorkspacePropertyKey = DependencyProperty.RegisterReadOnly(nameof(SelectedWorkspace), typeof(string), typeof(ShellWindow), new PropertyMetadata("work"));
    public static readonly DependencyProperty SelectedWorkspaceProperty = SelectedWorkspacePropertyKey.DependencyProperty;
    public string SelectedWorkspace => (string)GetValue(SelectedWorkspaceProperty);
    public static readonly DependencyProperty ShowWorkspaceFeedbackProperty = DependencyProperty.Register(nameof(ShowWorkspaceFeedback), typeof(bool), typeof(ShellWindow), new PropertyMetadata(false));
    public bool ShowWorkspaceFeedback { get => (bool)GetValue(ShowWorkspaceFeedbackProperty); private set => SetValue(ShowWorkspaceFeedbackProperty, value); }
    void SelectWorkspace(UserControl content, string workspace)
    {
        if (SelectedWorkspace != workspace) ShowWorkspaceFeedback = false;
        vm.SetFeedbackContext(workspace);
        PageHost.Content = content; SetValue(SelectedWorkspacePropertyKey, workspace);
    }
    void CloseRestrictedWorkspace()
    {
        if (vm.IsManagementMode || !ReferenceEquals(PageHost.Content, maintenance) && PageHost.Content is not SoftwareLogControl) return;
        (PageHost.Content as SoftwareLogControl)?.Revoke(); SelectWorkspace(work, "work");
    }
    readonly ParcelViewModel vm;
    readonly Func<ParcelViewModel> newViewModel;
    readonly Func<UserControl> workstation;
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    readonly UserControl work;
    readonly HistoryPage history = new();
    readonly MaintenancePage maintenance = new();
    readonly ShellWindow root;
    readonly HashSet<ShellWindow> windows;
    Task pendingRefresh = Task.CompletedTask, pendingViewportPause = Task.CompletedTask;
    bool compact, closing, closeReady, closed, pendingMaintenanceLoad;
    long closeGeneration;
    public Func<Task>? StopOwnerAsync { get; set; }
    public ShellWindow(ParcelViewModel vm, Func<ParcelViewModel> newViewModel, Func<UserControl> workstation, string? view = null, string? layout = null, ShellWindow? owner = null)
    {
        InitializeComponent(); ProductBrand.Apply(this); UiTypography.Apply(this); this.vm = vm; this.newViewModel = newViewModel; this.workstation = workstation; DataContext = vm; work = workstation();
        maintenance.DataContext = vm; history.DataContext = vm; work.DataContext = vm;
        maintenance.DisplayPreferencesRequested += (_, _) => Layout_Click(this, new RoutedEventArgs());
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ParcelViewModel.Feedback)) ShowWorkspaceFeedback = true;
            if (e.PropertyName == nameof(ParcelViewModel.IsManagementMode)) CloseRestrictedWorkspace();
            if (e.PropertyName == nameof(ParcelViewModel.Busy) && !vm.Busy && pendingMaintenanceLoad)
                Dispatcher.BeginInvoke(new Action(async () => await LoadRequestedMaintenanceAsync()));
        };
        root = owner?.root ?? this; windows = owner?.windows ?? []; windows.Add(this);
        if (owner is not null) Owner = owner;
        var initialPage = view?.EndsWith("-history", StringComparison.Ordinal) == true ? "history" : view?.EndsWith("-maintenance", StringComparison.Ordinal) == true ? "maintenance" : "work";
        SelectWorkspace(initialPage == "history" ? history : initialPage == "maintenance" ? maintenance : work, initialPage);
        try { compact = layout is null ? Ipc.Platform.PresentationPreferences.Read(vm.PresentationId, "classic") ?? false : layout == "compact"; }
        catch (Exception error) when (error is System.IO.IOException or System.Text.Json.JsonException or UnauthorizedAccessException) { vm.Message = "展示偏好读取失败，原文件保留：" + error.GetType().Name; }
        ApplyLayout();
        ClientArea.SizeChanged += (_, _) => UpdateViewport();
        timer.Tick += (_, _) => { vm.RefreshFreshness(); if (!closing && !closed && pendingRefresh.IsCompleted) pendingRefresh = RefreshAsync(); };
        Loaded += async (_, _) =>
        {
            if (ActualWidth > SystemParameters.WorkArea.Width || ActualHeight > SystemParameters.WorkArea.Height) WindowState = WindowState.Maximized;
            pendingRefresh = RefreshAsync(); await pendingRefresh;
            if (closing || closed) return;
            UpdateViewport(); timer.Start();
            if (initialPage == "history") await vm.RunAsync(() => vm.LoadHistoryAsync(), "读取作业记录");
            if (initialPage == "maintenance") { pendingMaintenanceLoad = true; await LoadRequestedMaintenanceAsync(); }
        };
        Closed += (_, _) => { closed = true; timer.Stop(); windows.Remove(this); }; Closing += ClosingOwner;
        var progress = new DispatcherProgressMonitor(vm.Application, Dispatcher); Closed += (_, _) => progress.Dispose();
    }
    async Task RefreshAsync()
    { try { await vm.RefreshAsync(); CloseRestrictedWorkspace(); } catch (Exception) { /* Snapshot reader retains and marks its original failure. */ } }
    void UpdateViewport()
    {
        if (closing || closed || ClientArea.ActualWidth <= 0 || ClientArea.ActualHeight <= 0) return;
        ApplyLayout(); if (vm.SetViewport(ClientArea.ActualWidth, ClientArea.ActualHeight)) QueueViewportPause();
    }
    void QueueViewportPause() { if (!closing && !closed && pendingViewportPause.IsCompleted) pendingViewportPause = vm.PauseAsync(); }
    void Maximize_Click(object s, RoutedEventArgs e) => WindowState = WindowState.Maximized;
    void ViewportPause_Click(object s, RoutedEventArgs e) => QueueViewportPause();
    void ViewportAcknowledge_Click(object s, RoutedEventArgs e) => vm.AcknowledgeViewport();
    void DismissGlobalFeedback_Click(object sender, RoutedEventArgs e) => vm.DismissGlobalOperationFeedback();
    void Workstation_Click(object s, RoutedEventArgs e) => SelectWorkspace(work, "work");
    async void ManagementMode_Click(object s, RoutedEventArgs e) { if (vm.Application is not Ipc.Application.Contracts.IWorkstationAdministration mode) return; try { await vm.RefreshManagementModeAsync(); if(vm.IsManagementMode) await mode.ExitManagementModeAsync(); else new ManagementModeWindow(mode) { Owner = this }.ShowDialog(); await vm.RefreshManagementModeAsync(); } catch(Exception error) { vm.Message=error.Message; } }
    void Logs_Click(object s, RoutedEventArgs e) { if (vm.IsManagementMode && vm.Application is Ipc.Application.Contracts.IWorkstationAdministration admin) SelectWorkspace(new SoftwareLogControl(admin), "logs"); }
    async void History_Click(object s, RoutedEventArgs e) { SelectWorkspace(history, "history"); await vm.RunAsync(() => vm.LoadHistoryAsync(), "作业记录"); }
    async void Maintenance_Click(object s, RoutedEventArgs e)
    { if (!vm.IsManagementMode) { vm.Message = "请先进入管理模式。"; return; } SelectWorkspace(maintenance, "maintenance"); pendingMaintenanceLoad = !vm.HasLoadedConfiguration; await LoadRequestedMaintenanceAsync(); }
    async Task LoadRequestedMaintenanceAsync()
    {
        if (!pendingMaintenanceLoad) return;
        if (closing || closed || !ReferenceEquals(PageHost.Content, maintenance)) { pendingMaintenanceLoad = false; return; }
        if (vm.Busy) return;
        pendingMaintenanceLoad = false;
        await vm.RunAsync(vm.EnsureMaintenanceLoadedAsync, "读取维护配置");
    }
    void Layout_Click(object s, RoutedEventArgs e)
    {
        compact = !compact; ApplyLayout();
        try { Ipc.Platform.PresentationPreferences.Save(vm.PresentationId, "classic", compact); }
        catch (Exception error) when (error is System.IO.IOException or System.Text.Json.JsonException or UnauthorizedAccessException) { vm.Message = "本窗口布局已切换；展示偏好未保存，原文件保留：" + error.GetType().Name; }
    }
    void ApplyLayout() { vm.IsCompact = compact || ClientArea.ActualWidth < 1280 || ClientArea.ActualHeight < 680; FontSize = UiTypography.Body; }
    async void Pause_Click(object s, RoutedEventArgs e) => await vm.PauseAsync();
    async void Resume_Click(object s, RoutedEventArgs e) { if (vm.CanResumeInViewport) await vm.RunAsync(() => vm.CommandAsync("Resume"), "恢复新入场"); }
    async void Reconcile_Click(object s, RoutedEventArgs e) { if (vm.CanUseWorkspace) await vm.RunAsync(vm.ReconcileAsync, "核对原请求"); }
    async void Login_Click(object s, RoutedEventArgs e) { var proof = LoginProof.Password; LoginProof.Clear(); await vm.RunAsync(() => vm.LoginAsync(proof), "验证身份"); }
    async void Logout_Click(object s, RoutedEventArgs e) => await vm.RunAsync(vm.LogoutAsync, "登出");
    async void Continue_Click(object s, RoutedEventArgs e) { if (vm.CanUseWorkspace) await vm.RunAsync(vm.ContinueAsync, "继续未尝试原阶段"); }
    public Func<int, bool>? ConfirmDiscardDrafts { get; set; }
    async void ClosingOwner(object? sender, CancelEventArgs e)
    {
        if (closeReady || closed) return;
        e.Cancel = true; if (closing) return; closing = true;
        var generation = ++closeGeneration;
        var affected = StopOwnerAsync is null ? new[] { this } : windows.ToArray();
        foreach (var window in affected) { if (window != this) window.closeGeneration++; window.closing = true; window.timer.Stop(); window.IsEnabled = false; }
        try
        {
            // Close cannot re-enter a native WPF Closing event even when every drain task is already complete.
            await System.Windows.Threading.Dispatcher.Yield();
            await Task.WhenAll(affected.SelectMany(x => new[] { x.pendingRefresh, x.pendingViewportPause, x.vm.DrainAsync() })).WaitAsync(TimeSpan.FromSeconds(5));
            // A root close owns the entire group, including an auxiliary close already draining.
            if (generation != closeGeneration || this != root && root.closing) return;
            var dirty = affected.Count(x => !x.closed && windows.Contains(x) && ConfigurationCloseGuard.HasUnsaved(x.vm, x.maintenance));
            if (dirty != 0 && !(ConfirmDiscardDrafts?.Invoke(dirty) ?? ConfigurationCloseGuard.ConfirmDiscard(this, dirty)))
            {
                vm.Message = "已取消关闭；未保存草稿、选择与配置修订已保留。";
                RestoreAfterClose(); return;
            }
            if (StopOwnerAsync is not null) await StopOwnerAsync();
            closeReady = true;
            foreach (var child in affected.Where(x => x != this && !x.closed && windows.Contains(x))) { child.closeReady = true; child.Close(); }
            Close();
        }
        catch (Exception error)
        {
            vm.Message = "停止未完成，仍持有资源：" + error.Message;
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

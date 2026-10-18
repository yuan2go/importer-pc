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
    readonly Func<Task>? stopOwner;
    readonly SupplyStationWindow root;
    readonly HashSet<SupplyStationWindow> windows;
    Task pendingRefresh = Task.CompletedTask;
    Task pendingViewportPause = Task.CompletedTask;
    bool compact, closing, closeReady, closed;
    public SupplyStationViewModel ViewModel { get; }

    public SupplyStationWindow(IParcelApplication application, string? view, string? layout, Func<Task>? stopOwner = null, SupplyStationWindow? owner = null)
    {
        InitializeComponent();
        ViewModel = new(application); DataContext = ViewModel; this.stopOwner = stopOwner;
        ClientArea.SizeChanged += ViewportChanged;
        root = owner?.root ?? this; windows = owner?.windows ?? []; windows.Add(this);
        if (owner is not null) Owner = owner;
        work.ColumnDefinitions.Add(new()); work.ColumnDefinitions.Add(new());
        var left = new SupplyStationPosition { DataContext = ViewModel.Left };
        var right = new SupplyStationPosition { DataContext = ViewModel.Right };
        Grid.SetColumn(right, 1); work.Children.Add(left); work.Children.Add(right);
        compact = layout == "compact"; ApplyLayout();
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
            if (initialPage == "maintenance") await ViewModel.RunAsync(ViewModel.LoadCapabilitiesAsync);
            if (initialPage == "history") await ViewModel.RunAsync(() => ViewModel.OpenHistoryAsync());
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
        ViewModel.SelectedPage = page;
        PageHost.Content = page switch { "history" => history, "maintenance" => maintenance, _ => work };
    }
    void Workstation_Click(object sender, RoutedEventArgs e) => SetPage("workstation");
    async void History_Click(object sender, RoutedEventArgs e) { SetPage("history"); await ViewModel.RunAsync(() => ViewModel.OpenHistoryAsync()); }
    async void Maintenance_Click(object sender, RoutedEventArgs e) { SetPage("maintenance"); await ViewModel.RunAsync(ViewModel.LoadCapabilitiesAsync); }
    void Window_Click(object sender, RoutedEventArgs e)
    {
        if (!root.closing) new SupplyStationWindow(ViewModel.Application, null, compact ? "compact" : "standard", owner: root).Show();
    }
    void Layout_Click(object sender, RoutedEventArgs e) { compact = !compact; ApplyLayout(); }
    void ApplyLayout() { FontSize = compact ? 14 : 16; work.Margin = compact ? new Thickness(0) : new Thickness(4); }
    void ViewportChanged(object sender, SizeChangedEventArgs e) => UpdateViewport();
    void UpdateViewport()
    {
        if (closing || closed || ClientArea.ActualWidth <= 0 || ClientArea.ActualHeight <= 0) return;
        if (ViewModel.SetViewport(ClientArea.ActualWidth, ClientArea.ActualHeight)) QueueViewportPause();
    }
    void QueueViewportPause()
    {
        if (!closing && !closed && pendingViewportPause.IsCompleted) pendingViewportPause = ViewModel.PauseAsync();
    }
    void Maximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Maximized;
    void ViewportPause_Click(object sender, RoutedEventArgs e) => QueueViewportPause();
    void ViewportAcknowledge_Click(object sender, RoutedEventArgs e) => ViewModel.AcknowledgeViewport();
    public async Task ShowDetailsAsync(ParcelOperation operation) { SetPage("history"); await ViewModel.RunAsync(() => ViewModel.OpenHistoryAsync(operation.Id)); }
    async void Pause_Click(object sender, RoutedEventArgs e) => await ViewModel.PauseAsync();
    async void Resume_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.CanResumeInViewport) await ViewModel.RunAsync(() => ViewModel.CommandAsync("Resume"));
    }
    async void Login_Click(object sender, RoutedEventArgs e)
    {
        var proof = LoginProof.Password; LoginProof.Clear();
        if (ViewModel.Fresh) await ViewModel.RunAsync(() => ViewModel.LoginAsync(proof));
    }
    async void Logout_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Fresh) await ViewModel.RunAsync(ViewModel.LogoutAsync);
    }
    async void ClosingWindow(object? sender, CancelEventArgs e)
    {
        if (closeReady || closed) return;
        e.Cancel = true;
        if (closing) return;
        closing = true;
        var affected = stopOwner is null ? new[] { this } : windows.ToArray();
        foreach (var window in affected) { window.closing = true; window.timer.Stop(); window.IsEnabled = false; }
        try
        {
            await Task.WhenAll(affected.SelectMany(x => new[] { x.pendingRefresh, x.pendingViewportPause })).WaitAsync(TimeSpan.FromSeconds(5));
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
            if (this != root && root.closing) return;
            closing = false;
            foreach (var window in affected.Where(x => !x.closed && windows.Contains(x)))
            { window.closing = false; window.IsEnabled = true; window.timer.Start(); }
        }
    }
}

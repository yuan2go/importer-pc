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
    bool compact, closing, closeReady;
    public SupplyStationViewModel ViewModel { get; }

    public SupplyStationWindow(IParcelApplication application, string? view, string? layout, Func<Task>? stopOwner = null, SupplyStationWindow? owner = null)
    {
        InitializeComponent();
        ViewModel = new(application); DataContext = ViewModel; this.stopOwner = stopOwner;
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
            if (pendingRefresh.IsCompleted && !closing) pendingRefresh = RefreshAsync();
        };
        Loaded += async (_, _) =>
        {
            pendingRefresh = RefreshAsync(); await pendingRefresh;
            if (closing) return;
            timer.Start();
            if (initialPage == "maintenance") await ViewModel.RunAsync(ViewModel.LoadCapabilitiesAsync);
        };
        Closing += ClosingWindow;
        Closed += (_, _) => { timer.Stop(); windows.Remove(this); };
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
    void History_Click(object sender, RoutedEventArgs e) => SetPage("history");
    async void Maintenance_Click(object sender, RoutedEventArgs e) { SetPage("maintenance"); await ViewModel.RunAsync(ViewModel.LoadCapabilitiesAsync); }
    void Window_Click(object sender, RoutedEventArgs e)
    {
        if (!root.closing) new SupplyStationWindow(ViewModel.Application, null, compact ? "compact" : "standard", owner: root).Show();
    }
    void Layout_Click(object sender, RoutedEventArgs e) { compact = !compact; ApplyLayout(); }
    void ApplyLayout() { FontSize = compact ? 14 : 16; work.Margin = compact ? new Thickness(0) : new Thickness(4); }
    public void ShowDetails(ParcelOperation operation) { ViewModel.Selected = operation; SetPage("history"); }
    async void Pause_Click(object sender, RoutedEventArgs e) => await ViewModel.PauseAsync();
    async void Resume_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Fresh) await ViewModel.RunAsync(() => ViewModel.CommandAsync("Resume"));
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
        if (closeReady) return;
        e.Cancel = true;
        if (closing) return;
        closing = true;
        var affected = stopOwner is null ? new[] { this } : windows.ToArray();
        foreach (var window in affected) { window.timer.Stop(); window.IsEnabled = false; }
        try
        {
            await Task.WhenAll(affected.Select(x => x.pendingRefresh)).WaitAsync(TimeSpan.FromSeconds(5));
            if (stopOwner is not null) await stopOwner();
            closeReady = true;
            foreach (var child in affected.Where(x => x != this)) { child.closeReady = true; child.Close(); }
            Close();
        }
        catch (Exception ex)
        {
            ViewModel.Message = "停止未完成，仍持有资源：" + ex.Message; closing = false;
            foreach (var window in affected) { window.IsEnabled = true; window.timer.Start(); }
        }
    }
}

using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
namespace Ipc.Application.ParcelFlow.Desktop;

public partial class ShellWindow : Window
{
    readonly ParcelViewModel vm;
    readonly Func<ParcelViewModel> newViewModel;
    readonly Func<UserControl> workstation;
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    readonly UserControl work;
    readonly HistoryPage history = new();
    readonly MaintenancePage maintenance = new();
    bool refreshing, compact, closeReady;
    public Func<Task>? StopOwnerAsync { get; set; }
    public ShellWindow(ParcelViewModel vm, Func<ParcelViewModel> newViewModel, Func<UserControl> workstation, string? view = null, string? layout = null)
    {
        InitializeComponent(); this.vm = vm; this.newViewModel = newViewModel; this.workstation = workstation; DataContext = vm; work = workstation();
        PageHost.Content = view?.EndsWith("-history", StringComparison.Ordinal) == true ? history : view?.EndsWith("-maintenance", StringComparison.Ordinal) == true ? maintenance : work;
        compact = layout == "compact"; ApplyLayout();
        timer.Tick += Refresh; Loaded += async (_, _) => { await vm.RunAsync(vm.RefreshAsync); timer.Start(); };
        Closed += (_, _) => timer.Stop(); Closing += ClosingOwner;
    }
    async void Refresh(object? sender, EventArgs e)
    {
        if (refreshing || vm.Busy) return;
        refreshing = true;
        try { await vm.RefreshAsync(); } catch (Exception ex) { vm.Message = "状态读取失败：" + ex.Message; }
        finally { refreshing = false; }
    }
    void Workstation_Click(object s, RoutedEventArgs e) => PageHost.Content = work;
    void History_Click(object s, RoutedEventArgs e) => PageHost.Content = history;
    void Maintenance_Click(object s, RoutedEventArgs e) { vm.LoadConfigurationDraft(); PageHost.Content = maintenance; }
    void Window_Click(object s, RoutedEventArgs e) => new ShellWindow(newViewModel(), newViewModel, workstation, layout: compact ? "compact" : "standard") { Owner = this }.Show();
    void Layout_Click(object s, RoutedEventArgs e) { compact = !compact; ApplyLayout(); }
    void ApplyLayout() { FontSize = compact ? 14 : 16; PageHost.Margin = new Thickness(0, compact ? 8 : 12, 0, compact ? 8 : 12); }
    async void Pause_Click(object s, RoutedEventArgs e) => await vm.RunAsync(() => vm.CommandAsync("Pause"));
    async void Resume_Click(object s, RoutedEventArgs e) => await vm.RunAsync(() => vm.CommandAsync("Resume"));
    async void Reconcile_Click(object s, RoutedEventArgs e) => await vm.RunAsync(vm.ReconcileAsync);
    async void ClosingOwner(object? sender, CancelEventArgs e)
    {
        if (closeReady || StopOwnerAsync is null) return;
        e.Cancel = true; timer.Stop(); IsEnabled = false;
        try { await StopOwnerAsync(); closeReady = true; Close(); }
        catch (Exception ex) { vm.Message = "停止未完成，仍持有资源：" + ex.Message; IsEnabled = true; timer.Start(); }
    }
}

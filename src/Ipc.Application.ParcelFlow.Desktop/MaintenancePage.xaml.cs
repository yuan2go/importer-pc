using System.Windows;
using System.Windows.Controls;
namespace Ipc.Application.ParcelFlow.Desktop;
public partial class MaintenancePage : UserControl
{
    public MaintenancePage() => InitializeComponent();
    void Load_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) vm.LoadConfigurationDraft(); }
    async void Save_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.SaveConfigurationAsync); }
    async void Reconnect_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.ReconnectAsync); }
    async void Overload_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(() => vm.CommandAsync("AcknowledgeOverload")); }
    async void Backup_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.BackupAsync); }
}

using System.Windows;
using System.Windows.Controls;
namespace Ipc.Application.ParcelFlow.Desktop;
public partial class HistoryPage : UserControl
{
    public HistoryPage() => InitializeComponent();
    async void Refresh_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(() => vm.LoadHistoryAsync()); }
    async void Next_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(() => vm.LoadHistoryAsync(true)); }
    async void Query_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.QuerySelectedAsync); }
    async void Continue_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.ContinueSelectedAsync); }
}

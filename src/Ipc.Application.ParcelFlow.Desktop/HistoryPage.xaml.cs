using System.Windows;
using System.Windows.Controls;
namespace Ipc.Application.ParcelFlow.Desktop;
public partial class HistoryPage : UserControl
{
    public HistoryPage() => InitializeComponent();
    async void Query_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.ReconcileAsync); }
    async void Continue_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(() => vm.CommandAsync("ContinuePrepared", vm.Selected?.Id)); }
}

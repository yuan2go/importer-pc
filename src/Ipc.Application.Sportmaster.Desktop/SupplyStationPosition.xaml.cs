using System.Windows;
using System.Windows.Controls;

namespace Ipc.Application.Sportmaster.Desktop;

public partial class SupplyStationPosition : UserControl
{
    public SupplyStationPosition() => InitializeComponent();
    SupplyStationWindow? Host => Window.GetWindow(this) as SupplyStationWindow;
    void New_Click(object sender, RoutedEventArgs e) { if (DataContext is SupplyStationPositionViewModel p) p.NewParcel(); }
    async void Emit_Click(object sender, RoutedEventArgs e) { if (Host is { } h && DataContext is SupplyStationPositionViewModel p) await h.ViewModel.EmitPositionAsync(p); }
    void Details_Click(object sender, RoutedEventArgs e) { if (Host is { } h && DataContext is SupplyStationPositionViewModel { Operation: { } op }) h.ShowDetails(op); }
    async void Reconcile_Click(object sender, RoutedEventArgs e) { if (Host is { } h && DataContext is SupplyStationPositionViewModel p) await h.ViewModel.ReconcilePositionAsync(p); }
    async void Continue_Click(object sender, RoutedEventArgs e) { if (Host is { } h && DataContext is SupplyStationPositionViewModel p) await h.ViewModel.ContinuePositionAsync(p); }
}

using System.Windows;
using System.Windows.Controls;
using Ipc.Application.ParcelFlow.Desktop;
namespace Ipc.Application.DirectPlc.Desktop;
public partial class Workstation : UserControl
{
    public Workstation() => InitializeComponent();
    void New_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) vm.NewParcel(); }
    async void Emit_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(() => { vm.Emit(); return Task.CompletedTask; }); }
    async void Weight_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(() => { vm.EmitWeight(); return Task.CompletedTask; }); }
}

using System.Windows;
using System.Windows.Controls;
using Ipc.Application.ParcelFlow.Desktop;
namespace Ipc.Application.Sportmaster.Desktop;
public partial class Workstation : UserControl
{
    public Workstation() => InitializeComponent();
    void Position_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm && s is Button { Tag: string position }) vm.SelectedPositionId = position; }
    void New_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) vm.NewParcel(); }
    async void Emit_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(() => { vm.Emit(); return Task.CompletedTask; }, "模拟相机读码"); }
    async void Weight_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(() => { vm.EmitWeight(); return Task.CompletedTask; }, "同包裹模拟实测重量"); }
}

using System.Windows;
using System.Windows.Controls;
namespace Ipc.Application.ParcelFlow.Desktop;
public partial class HistoryPage : UserControl
{
    public HistoryPage() => InitializeComponent();
    void Copy_Click(object s, RoutedEventArgs e)
    {
        if (DataContext is not ParcelViewModel vm) return;
        try { Clipboard.SetText(vm.Details); vm.Message = "已复制完整原作业、请求及回执JSON。"; }
        catch (Exception error) { vm.Message = "复制失败，可选择详情文本复制：" + error.Message; }
    }
    async void Refresh_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(() => vm.LoadHistoryAsync(), "读取最新50条"); }
    async void Next_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(() => vm.LoadHistoryAsync(true), "下一页"); }
    async void Query_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.QuerySelectedAsync, "核对所选原请求"); }
    async void Continue_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.ContinueSelectedAsync, "继续所选未尝试阶段"); }
}

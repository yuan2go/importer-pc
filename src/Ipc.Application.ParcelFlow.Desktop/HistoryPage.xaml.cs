using System.Windows;
using System.Windows.Controls;
namespace Ipc.Application.ParcelFlow.Desktop;
public partial class HistoryPage : UserControl
{
    public HistoryPage()
    {
        InitializeComponent();
        SizeChanged += (_, _) => FitQueryViewport();
        HistoryQueryActions.SizeChanged += (_, _) => FitQueryViewport();
        HistoryStatusLine.SizeChanged += (_, _) => FitQueryViewport();
    }
    void FitQueryViewport()
    {
        var shortPage = ActualHeight < 500;
        HistoryLayout.Margin = shortPage ? new(12, 8, 12, 8) : new(20, 16, 20, 12);
        HistoryHeading.FontSize = UiTypography.Body;
        var header = double.IsFinite(HistoryRows.ColumnHeaderHeight) ? HistoryRows.ColumnHeaderHeight : 36;
        var row = double.IsFinite(HistoryRows.RowHeight) ? HistoryRows.RowHeight : 40;
        var fixedHeight = HistoryLayout.Margin.Top + HistoryLayout.Margin.Bottom +
            HistoryQueryActions.ActualHeight + HistoryQueryActions.Margin.Top + HistoryQueryActions.Margin.Bottom +
            HistoryStatusLine.ActualHeight + HistoryStatusLine.Margin.Top + HistoryStatusLine.Margin.Bottom;
        // Global receipts and custody reasons change the page's available height.
        // Keep the real header and one full result row; filters retain their own scroll.
        var filterBudget = Math.Max(0, ActualHeight - fixedHeight - header - row - 2);
        HistoryFilterScroll.MaxHeight = Math.Min(shortPage ? 72 : Math.Min(240, ActualHeight * .35), filterBudget);
    }
    void ClearFilters_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ParcelViewModel vm) return;
        vm.ClearHistoryFilters();
    }
    void Today_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) { var day = new DateTimeOffset(DateTime.Today); vm.HistoryFrom = day.ToString("O"); vm.HistoryToExclusive = day.AddDays(1).ToString("O"); } }
    void LastDay_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) { var now = DateTimeOffset.Now; vm.HistoryFrom = now.AddDays(-1).ToString("O"); vm.HistoryToExclusive = now.ToString("O"); } }
    void AnyTime_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) vm.HistoryFrom = vm.HistoryToExclusive = ""; }
    void Copy_Click(object s, RoutedEventArgs e)
    {
        if (DataContext is not ParcelViewModel vm) return;
        try { Clipboard.SetText(vm.Details); vm.Message = "已复制完整原作业、请求及回执JSON。"; }
        catch (Exception error) { vm.Message = "复制失败，可选择详情文本复制：" + error.Message; }
    }
    async void Refresh_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(() => vm.LoadHistoryAsync(), "读取最新50条"); }
    async void Next_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(() => vm.LoadHistoryAsync(true), "下一页"); }
    async void Export_Click(object s, RoutedEventArgs e)
    {
        if (DataContext is not ParcelViewModel vm) return;
        var picker = new Microsoft.Win32.SaveFileDialog { FileName = "operation-history.csv", Filter = "受控文本CSV|*.csv" };
        if (picker.ShowDialog(Window.GetWindow(this)) == true) await vm.RunAsync(() => vm.ExportHistoryAsync(picker.FileName), "导出当前筛选");
    }
    void CancelExport_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) vm.CancelHistoryExport(); }
    async void Query_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.QuerySelectedAsync, "核对所选原请求"); }
    async void Continue_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.ContinueSelectedAsync, "继续所选未尝试阶段"); }
}

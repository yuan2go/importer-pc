using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
namespace Ipc.Application.ParcelFlow.Desktop;

public partial class HistoryPage : UserControl
{
    public static readonly DependencyProperty DetailsOpenProperty = DependencyProperty.Register(
        nameof(DetailsOpen), typeof(bool), typeof(HistoryPage), new PropertyMetadata(false));
    public bool DetailsOpen { get => (bool)GetValue(DetailsOpenProperty); private set => SetValue(DetailsOpenProperty, value); }
    Window? inputOwner;
    public void ShowTaskDetails() => DetailsOpen = DataContext is ParcelViewModel { Selected: not null };
    void StatusDetails_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ParcelViewModel vm || sender is not FrameworkElement { DataContext: Ipc.Application.Contracts.ParcelHistoryItem item }) return;
        vm.SelectedHistory = item;
        ShowTaskDetails();
    }
    void OwnerMouseDown(object sender, MouseButtonEventArgs e)
    {
        for (var source = e.OriginalSource as DependencyObject; source is not null;
            source = source is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source))
            if (ReferenceEquals(source, HistoryDetailPanel) || source is Button { Name: "HistoryStatusDetailsAction" }) return;
        DetailsOpen = false;
    }
    void OwnerKeyDown(object sender, KeyEventArgs e)
    { if (DetailsOpen && e.Key == Key.Escape) { DetailsOpen = false; e.Handled = true; HistoryRows.Focus(); } }
    public event EventHandler<string>? LogsRequested;
    void Logs_Click(object sender, RoutedEventArgs e) { if (DataContext is ParcelViewModel { IsManagementMode: true, Selected: { } op }) LogsRequested?.Invoke(this, op.Id); }
    async void BarcodeQuery_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    { if (e.Key == System.Windows.Input.Key.Enter && DataContext is ParcelViewModel vm && !vm.Busy) { e.Handled = true; await vm.RunAsync(() => vm.LoadHistoryAsync(), "查询完整条码"); } }
    public HistoryPage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            ArrangeTaskFilters(); UpdateDetailsVisibility();
            inputOwner = Window.GetWindow(this);
            if (inputOwner is not null) { inputOwner.PreviewMouseDown += OwnerMouseDown; inputOwner.PreviewKeyDown += OwnerKeyDown; }
        };
        Unloaded += (_, _) =>
        {
            if (inputOwner is not null) { inputOwner.PreviewMouseDown -= OwnerMouseDown; inputOwner.PreviewKeyDown -= OwnerKeyDown; inputOwner = null; }
            DetailsOpen = false;
            if (DataContext is ParcelViewModel vm) vm.SetDetailsVisibility(false, false);
        };
        DataContextChanged += (_, e) => { DetailsOpen = false; if (e.OldValue is ParcelViewModel old) old.SetDetailsVisibility(false, false); UpdateDetailsVisibility(); };
        StructuredDetailsPanel.IsVisibleChanged += (_, _) => UpdateDetailsVisibility();
        RawDetailsText.IsVisibleChanged += (_, _) => UpdateDetailsVisibility();
        SizeChanged += (_, _) => FitQueryViewport();
        AllHistoryFilters.SizeChanged += (_, _) => FitQueryViewport();
        HistoryQueryActions.SizeChanged += (_, _) => FitQueryViewport();
        HistoryStatusLine.SizeChanged += (_, _) => FitQueryViewport();
        ((FrameworkElement)HistoryStatusLine.Parent).SizeChanged += (_, _) => FitQueryViewport();
        HistoryDetailPanel.SizeChanged += (_, _) => FitQueryViewport();
    }
    void Picture_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ParcelViewModel vm || Window.GetWindow(this) is not { } owner) return;
        var op = sender is FrameworkElement { DataContext: Ipc.Application.Contracts.ParcelHistoryItem row } ? row.Operation : vm.Selected;
        if (op is not null) ParcelPictureWindow.Open(owner, vm, new(OperationId: op.Id), op.ParcelCode);
    }
    void UpdateDetailsVisibility()
    { if (DataContext is ParcelViewModel vm) vm.SetDetailsVisibility(IsLoaded && StructuredDetailsPanel.IsVisible, IsLoaded && RawDetailsText.IsVisible); }
    void FitQueryViewport()
    {
        var shortPage = ActualHeight < 500;
        HistoryLayout.Margin = shortPage ? new(12, 8, 12, 8) : new(20, 16, 20, 12);
        HistoryHeading.FontSize = UiTypography.Body;
        HistoryHeading.Visibility = shortPage ? Visibility.Collapsed : Visibility.Visible;
        var header = double.IsFinite(HistoryRows.ColumnHeaderHeight) ? HistoryRows.ColumnHeaderHeight : 36;
        var row = double.IsFinite(HistoryRows.RowHeight) ? HistoryRows.RowHeight : 40;
        var fixedHeight = HistoryLayout.Margin.Top + HistoryLayout.Margin.Bottom + (HistoryHeading.IsVisible ? HistoryHeading.ActualHeight + HistoryHeading.Margin.Bottom : 0) +
            HistoryQueryActions.ActualHeight + HistoryQueryActions.Margin.Top + HistoryQueryActions.Margin.Bottom + (AllHistoryFilters.IsVisible ? 32 : 0) +
            ((FrameworkElement)HistoryStatusLine.Parent).ActualHeight +
            (HistoryDetailPanel.IsVisible && Grid.GetRow(HistoryDetailPanel) == 1
                ? HistoryDetailPanel.ActualHeight + HistoryDetailPanel.Margin.Top + HistoryDetailPanel.Margin.Bottom : 0);
        // Global receipts and custody reasons change the page's available height.
        // Keep the real header and one full result row; filters retain their own scroll.
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this).DpiScaleY;
        var resultHeight = Math.Ceiling((header + row + HistoryRows.BorderThickness.Top + HistoryRows.BorderThickness.Bottom) * dpi) / dpi;
        // Header/cell borders snap independently at fractional DPI; reserve six pixels.
        var filterBudget = Math.Max(0, ActualHeight - fixedHeight - resultHeight - 6 / dpi);
        HistoryFilterScroll.MaxHeight = Math.Min(shortPage ? 96 : 160, filterBudget);
    }
    void ClearFilters_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ParcelViewModel vm) return;
        vm.ClearHistoryFilters();
    }
    void PositionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is ParcelViewModel vm && e.AddedItems.OfType<ParcelViewModel.HistoryPositionChoice>().FirstOrDefault() is { } choice) vm.HistoryPosition = choice.Id;
    }
    void Today_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) { var day = new DateTimeOffset(DateTime.Today); vm.HistoryFrom = day.ToString("O"); vm.HistoryToExclusive = new DateTimeOffset(DateTime.Today.AddDays(1)).ToString("O"); } }
    void LastDay_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) { var now = DateTimeInput.SecondNow(); vm.HistoryFrom = now.AddDays(-1).ToString("O"); vm.HistoryToExclusive = now.ToString("O"); } }
    void LastWeek_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) { var now = DateTimeInput.SecondNow(); vm.HistoryFrom = now.AddDays(-7).ToString("O"); vm.HistoryToExclusive = now.ToString("O"); } }
    void AnyTime_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) vm.HistoryFrom = vm.HistoryToExclusive = ""; }
    async void Copy_Click(object s, RoutedEventArgs e)
    {
        if (DataContext is not ParcelViewModel vm) return;
        try { await vm.RequireManagementModeAsync(); Clipboard.SetText(await vm.FullDetailsAsync()); vm.Message = "已复制作业、请求与 RCS 返回的完整数据。"; }
        catch (Exception error) { vm.Message = "复制失败，可选择详情文本复制：" + error.Message; }
    }
    async void Refresh_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(() => vm.LoadHistoryAsync(), "读取最新50条"); }
    async void First_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.FirstHistoryAsync, "同一范围首屏"); }
    async void Previous_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.PreviousHistoryAsync, "上一页"); }
    async void Next_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(() => vm.LoadHistoryAsync(true), "下一页"); }
    async void Export_Click(object s, RoutedEventArgs e)
    {
        if (DataContext is not ParcelViewModel vm) return;
        var picker = new Microsoft.Win32.SaveFileDialog { FileName = "operation-history.csv", Filter = "CSV 表格|*.csv" };
        if (picker.ShowDialog(Window.GetWindow(this)) == true) await vm.RunAsync(() => vm.ExportHistoryAsync(picker.FileName), "导出当前筛选");
    }
    void CancelExport_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) vm.CancelHistoryExport(); }
    void CancelQuery_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) vm.CancelHistoryRead(); }
    async void Query_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.QuerySelectedAsync, "查询所选作业的结果"); }
    async void Continue_Click(object s, RoutedEventArgs e) { if (DataContext is ParcelViewModel vm) await vm.RunAsync(vm.ContinueSelectedAsync, "继续处理所选作业"); }
}

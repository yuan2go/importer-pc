using System.Windows;
using System.Windows.Controls;
using Ipc.Application.Contracts;
namespace Ipc.Application.ParcelFlow.Desktop;

public partial class HistoryPage
{
    bool taskFiltersArranged;
    void ArrangeTaskFilters()
    {
        if (taskFiltersArranged || DataContext is not ParcelViewModel { Snapshot.ApplicationId: "sportmaster" }) return;
        taskFiltersArranged = true;
        // Reparent existing editors; each filter still has one binding and one query contract.
        foreach (var group in PrimaryHistoryFilters.Children.OfType<StackPanel>().Reverse().ToArray())
        {
            PrimaryHistoryFilters.Children.Remove(group);
            group.Children.OfType<TextBlock>().First().Visibility = Visibility.Collapsed;
            if (group.Children.OfType<TextBox>().FirstOrDefault() is { } barcode)
            {
                group.Children.Remove(barcode);
                var entry = new Grid(); entry.Children.Add(barcode);
                var hint = new TextBlock { Text = "扫码或输入完整条码", IsHitTestVisible = false, Margin = new(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = System.Windows.Media.Brushes.SlateGray };
                void Watermark() => hint.Visibility = barcode.Text.Length == 0 && !barcode.IsKeyboardFocused ? Visibility.Visible : Visibility.Collapsed;
                barcode.TextChanged += (_, _) => Watermark(); barcode.IsKeyboardFocusWithinChanged += (_, _) => Watermark(); Watermark();
                entry.Children.Add(hint); group.Children.Add(entry);
            }
            group.Margin = new(0, 0, 8, 4); HistoryQueryActions.Children.Insert(0, group);
        }
        foreach (var button in HistoryTimePresets.Children.OfType<Button>().Where(b => !Equals(b.Content, "不限时间")).ToArray())
        { HistoryTimePresets.Children.Remove(button); button.MinHeight = 44; HistoryQueryActions.Children.Add(button); }
        AllHistoryFilters.Header = "更多筛选与精确时间";
        var unknown = new Button { Content = "筛选格口未确认", MinHeight = 44, Margin = new(0, 0, 8, 4) };
        unknown.Click += async (_, _) => { if (DataContext is ParcelViewModel vm) { vm.HistoryState = nameof(ParcelState.WcsUnknown); await vm.RunAsync(() => vm.LoadHistoryAsync(), "按原状态查询格口未确认作业"); } };
        if (AllHistoryFilters.Content is ScrollViewer { Content: Panel more }) more.Children.Add(unknown);
    }
}

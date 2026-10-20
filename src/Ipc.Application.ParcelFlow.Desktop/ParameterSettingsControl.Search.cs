using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace Ipc.Application.ParcelFlow.Desktop;

public sealed partial class ParameterSettingsControl
{
    readonly TextBox search = new() { MinHeight = 44, MinWidth = 160, FontSize = 15, VerticalContentAlignment = VerticalAlignment.Center };
    readonly StackPanel searchItems = new();
    readonly TextBlock searchCount = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new(12, 0, 0, 0), Foreground = Brush("#586B80") };
    readonly ScrollViewer searchResults = new() { Visibility = Visibility.Collapsed, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = System.Windows.Media.Brushes.White };
    FrameworkElement BuildSearch()
    {
        var bar = new Grid { Margin = new(0, 0, 0, 8) };
        bar.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var input = new Grid();
        input.Children.Add(search);
        var hint = Text("搜索参数名称、编码或对象  ·  Ctrl+F"); hint.IsHitTestVisible = false; hint.Margin = new(14, 0, 8, 0); hint.VerticalAlignment = VerticalAlignment.Center;
        input.Children.Add(hint);
        AutomationProperties.SetName(search, "搜索参数");
        AutomationProperties.SetHelpText(search, "按名称、字段键、分类或对象搜索。回车定位第一个结果，Esc 返回表单；输入与草稿不会改变。");
        searchResults.Content = searchItems;
        AutomationProperties.SetName(searchResults, "参数搜索结果");
        AutomationProperties.SetLiveSetting(searchCount, AutomationLiveSetting.Polite);
        var clear = new Button { Content = "清除", MinHeight = 44, Visibility = Visibility.Collapsed };
        clear.Click += (_, _) => { search.Clear(); search.Focus(); };
        search.TextChanged += (_, _) => { hint.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; clear.Visibility = search.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible; RefreshSearch(); };
        search.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && searchItems.Children.OfType<Button>().FirstOrDefault() is { } first)
            { first.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); e.Handled = true; }
        };
        Grid.SetColumn(clear, 1); Grid.SetColumn(searchCount, 2);
        bar.Children.Add(input); bar.Children.Add(clear); bar.Children.Add(searchCount); return bar;
    }
    void RefreshSearch()
    {
        searchItems.Children.Clear();
        var query = search.Text.Trim();
        categories.Visibility = query.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        searchResults.Visibility = query.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        searchCount.Text = "";
        if (query.Length == 0) return;
        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var hits = locations.Where(x => categoryScope is null || x.Value.Page.Category == categoryScope)
            .GroupBy(x => x.Value.Control).Select(g => g.First())
            .Where(x => terms.All(term => (x.Value.Label + " " + x.Key + " " + x.Value.Page.Title + " " +
                ParameterOwnership.CategoryName(x.Value.Page.Category) + " " + x.Value.Help).Contains(term, StringComparison.OrdinalIgnoreCase))).ToArray();
        searchCount.Text = hits.Length + " 项";
        if (hits.Length == 0)
            searchItems.Children.Add(Text("未找到匹配参数。可换用中文名称、英文键或设备名称；清除搜索返回全部对象。"));
        foreach (var hit in hits)
        {
            var stack = new StackPanel();
            var label = Text(hit.Value.Label); label.Foreground = Brush("#183047"); label.FontWeight = FontWeights.SemiBold;
            stack.Children.Add(label);
            stack.Children.Add(Text(ParameterOwnership.CategoryName(hit.Value.Page.Category) + " / " + hit.Value.Page.Title + "  ·  " + hit.Key));
            var button = new Button { Content = stack, HorizontalContentAlignment = HorizontalAlignment.Stretch, MinHeight = 64, Margin = new(0, 0, 0, 4) };
            AutomationProperties.SetName(button, "定位参数 " + hit.Value.Label + " · " + hit.Value.Page.Title);
            button.Click += (_, _) => NavigateTo(hit.Key); searchItems.Children.Add(button);
        }
    }
}

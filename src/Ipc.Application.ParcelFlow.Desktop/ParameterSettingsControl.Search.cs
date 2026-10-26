using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Data;

namespace Ipc.Application.ParcelFlow.Desktop;

public sealed partial class ParameterSettingsControl
{
    readonly TextBox search = new() { MinHeight = 38, FontSize = UiTypography.Body, VerticalContentAlignment = VerticalAlignment.Center };
    readonly TextBlock searchCount = new() { Margin = new(4, 8, 0, 4), Foreground = Brush("#5A6878"), FontSize = UiTypography.Label };
    readonly ListBox searchResults = new() { Visibility = Visibility.Collapsed, BorderThickness = new(0), Background = System.Windows.Media.Brushes.Transparent, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    sealed record SearchHit(string Path, string Title, string Context);
    FrameworkElement BuildSearch()
    {
        var bar = new StackPanel { Margin = new(0, 0, 0, 8) };
        var input = new Grid(); input.Children.Add(search);
        var hint = Text("搜索设置  Ctrl+F"); hint.IsHitTestVisible = false; hint.FontSize = UiTypography.Label; hint.Margin = new(10, 0, 8, 0); hint.VerticalAlignment = VerticalAlignment.Center; input.Children.Add(hint);
        AutomationProperties.SetName(search, "搜索参数"); AutomationProperties.SetAutomationId(search, "ParameterSearch");
        AutomationProperties.SetHelpText(search, "按名称、用途、技术键或实例搜索。回车定位首项，下方向键选择结果，退出键清除搜索。草稿保留。");
        searchResults.ItemTemplate = (DataTemplate)Resources["ParameterSearchTemplate"];
        ScrollViewer.SetHorizontalScrollBarVisibility(searchResults, ScrollBarVisibility.Disabled);
        AutomationProperties.SetName(searchResults, "参数搜索结果"); AutomationProperties.SetLiveSetting(searchCount, AutomationLiveSetting.Polite);
        var clear = new Button { Content = "清除搜索", MinHeight = 36, Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Left, Style = (Style)Resources["ParameterQuietAction"], Margin = new(0, 4, 0, 0) };
        clear.Click += (_, _) => { search.Clear(); search.Focus(); };
        search.TextChanged += (_, _) => { hint.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; clear.Visibility = search.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible; RefreshSearch(); };
        search.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter && searchResults.Items.OfType<SearchHit>().FirstOrDefault() is { } first) { e.Handled = true; await NavigateSearchAsync(first.Path); }
            else if (e.Key == Key.Down && searchResults.Items.Count > 0) { searchResults.SelectedIndex = 0; ((ListBoxItem?)searchResults.ItemContainerGenerator.ContainerFromIndex(0))?.Focus(); e.Handled = true; }
        };
        searchResults.PreviewKeyDown += async (_, e) => { if (e.Key == Key.Enter && searchResults.SelectedItem is SearchHit hit) { e.Handled = true; await NavigateSearchAsync(hit.Path); } };
        searchResults.PreviewMouseLeftButtonUp += async (_, e) =>
        {
            if (ItemsControl.ContainerFromElement(searchResults, e.OriginalSource as DependencyObject) is ListBoxItem { Content: SearchHit hit }) { e.Handled = true; await NavigateSearchAsync(hit.Path); }
        };
        bar.Children.Add(input); bar.Children.Add(clear); bar.Children.Add(searchCount); return bar;
    }
    void RefreshSearch()
    {
        var query = search.Text.Trim(); directory.Visibility = query.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        searchResults.Visibility = query.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        searchCount.Text = ""; searchCount.Visibility = query.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (query.Length == 0) { searchResults.ItemsSource = null; return; }
        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var hits = locations.Where(x => x.Value.Page.Available && (categoryScope is null || x.Value.Page.Category == categoryScope) && x.Value.Applicable?.Invoke() != false)
            .GroupBy(x => x.Value.Control).Select(g => new { First = g.First(), Keys = string.Join(" ", g.Select(x => x.Key)) })
            .Where(x => terms.All(term => (x.First.Value.Label + " " + x.Keys + " " + x.First.Value.Page.Title + " " + ParameterOwnership.CategoryName(x.First.Value.Page.Category) + " " + x.First.Value.Help).Contains(term, StringComparison.OrdinalIgnoreCase)))
            .Select(x => new SearchHit(x.First.Key, x.First.Value.Label, ParameterOwnership.CategoryName(x.First.Value.Page.Category) + " / " + x.First.Value.Page.Title)).ToArray();
        searchCount.Text = hits.Length == 0 ? "没有匹配项，试试名称或技术键。" : hits.Length + " 项匹配";
        searchCount.TextWrapping = TextWrapping.Wrap; searchResults.ItemsSource = hits;
    }
}

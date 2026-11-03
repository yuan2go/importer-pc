using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Data;

namespace Ipc.Application.ParcelFlow.Desktop;

public sealed partial class ParameterSettingsControl
{
    readonly TextBox search = new() { MinHeight = 38, FontSize = UiTypography.Body, VerticalContentAlignment = VerticalAlignment.Center };
    readonly TextBlock searchCount = Themed(new TextBlock { Margin = new(4, 8, 0, 4), FontSize = UiTypography.Label }, TextBlock.ForegroundProperty, "Muted");
    readonly ListBox searchResults = new() { Visibility = Visibility.Collapsed, BorderThickness = new(0), Background = System.Windows.Media.Brushes.Transparent, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    sealed record SearchHit(string Path, string Title, string Context);
    FrameworkElement BuildSearch()
    {
        var bar = new StackPanel { Margin = new(0, 0, 0, 8) };
        var input = new Grid(); search.Padding = new(36, 7, 10, 7); input.Children.Add(search);
        var glass = Themed(new System.Windows.Shapes.Path
        {
            Data = System.Windows.Media.Geometry.Parse("M 6.5,1 A 5.5,5.5 0 1 1 6.49,1 Z M 10.5,10.5 L 15,15"),
            StrokeThickness = 1.5,
            Width = 15,
            Height = 15,
            Stretch = System.Windows.Media.Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new(12, 0, 0, 0),
            IsHitTestVisible = false
        }, System.Windows.Shapes.Shape.StrokeProperty, "Muted");
        input.Children.Add(glass);
        var hint = Text("搜索设置  Ctrl+F"); hint.IsHitTestVisible = false; hint.FontSize = UiTypography.Label; hint.Margin = new(36, 0, 8, 0); hint.VerticalAlignment = VerticalAlignment.Center; input.Children.Add(hint);
        AutomationProperties.SetName(search, "搜索参数"); AutomationProperties.SetAutomationId(search, "ParameterSearch");
        AutomationProperties.SetHelpText(search, "按名称或用途搜索。回车跳到第一项，按 Esc 清除。");
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
        searchCount.Text = hits.Length == 0 ? "没有找到，换个关键词试试。" : hits.Length + " 项匹配";
        searchCount.TextWrapping = TextWrapping.Wrap; searchResults.ItemsSource = hits;
    }
}

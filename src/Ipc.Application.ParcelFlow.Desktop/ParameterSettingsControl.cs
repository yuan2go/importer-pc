using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Documents;
namespace Ipc.Application.ParcelFlow.Desktop;

// Same complete draft in startup and daily settings. Polling never rebuilds controls.
public sealed partial class ParameterSettingsControl : UserControl
{
    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(nameof(Model), typeof(ParameterEditorModel), typeof(ParameterSettingsControl), new PropertyMetadata(null, ModelChanged));
    public ParameterEditorModel? Model { get => (ParameterEditorModel?)GetValue(ModelProperty); set => SetValue(ModelProperty, value); }
    readonly TabControl categories = new() { Name = "ParameterCategories" };
    readonly StackPanel errors = new();
    readonly Dictionary<string, Location> locations = [];
    readonly List<Action> unsubscribe = [];
    readonly List<ObjectPage> pages = [];
    readonly List<ListBox> navigators = [];
    sealed record ObjectPage(int Category, string Title, StackPanel Body, ScrollViewer Scroll)
    { public override string ToString() => Title; }
    sealed record Location(ObjectPage Page, FrameworkElement Control, TextBlock Error, string Label, string Help);
    int? categoryScope;
    public event EventHandler? MaintenanceRequested;
    public ParameterSettingsControl()
    {
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Ipc.Application.ParcelFlow.Desktop;component/ParameterSettingsTheme.xaml", UriKind.Relative) });
        var root = new DockPanel { Background = Brush("#F1F4F7") };
        var searchBar = BuildSearch(); DockPanel.SetDock(searchBar, Dock.Top); root.Children.Add(searchBar);
        var summary = new ScrollViewer { Content = errors, MaxHeight = 110, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        DockPanel.SetDock(summary, Dock.Top); root.Children.Add(summary);
        var body = new Grid(); body.Children.Add(categories); body.Children.Add(searchResults); root.Children.Add(body); Content = root;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { search.Focus(); search.SelectAll(); e.Handled = true; }
            else if (e.Key == Key.Escape && search.Text.Length > 0) { search.Clear(); e.Handled = true; }
        };
    }
    public void ShowCategory(int? index)
    {
        categoryScope = index;
        foreach (TabItem tab in categories.Items) tab.Visibility = index is null || categories.Items.IndexOf(tab) == index ? Visibility.Visible : Visibility.Collapsed;
        if (index is not null) categories.SelectedIndex = index.Value;
        RefreshSearch();
    }
    public bool NavigateTo(string path)
    {
        if (!locations.TryGetValue(path, out var location)) return false;
        search.Clear();
        if (categoryScope is not null && categoryScope != location.Page.Category) ShowCategory(location.Page.Category);
        categories.SelectedIndex = location.Page.Category;
        navigators[location.Page.Category].SelectedItem = location.Page;
        for (DependencyObject? parent = location.Control; parent is not null; parent = LogicalTreeHelper.GetParent(parent) ?? (parent is Visual ? VisualTreeHelper.GetParent(parent) : null))
            if (parent is Expander details) details.IsExpanded = true;
        UpdateLayout();
        location.Control.BringIntoView();
        location.Control.Focus();
        return true;
    }
    static void ModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (ParameterSettingsControl)d;
        if (e.OldValue is ParameterEditorModel old) old.ValidationChanged -= view.ShowIssues;
        if (e.NewValue is ParameterEditorModel next) next.ValidationChanged += view.ShowIssues;
        view.Render();
    }
    static TextBlock Text(string value, bool heading = false) => new()
    {
        Text = value, TextWrapping = TextWrapping.Wrap, FontSize = heading ? 20 : 14,
        Foreground = Brush(heading ? "#183047" : "#586B80"),
        FontWeight = heading ? FontWeights.SemiBold : FontWeights.Normal, Margin = new(0, heading ? 16 : 4, 0, 8)
    };
    static SolidColorBrush Brush(string color) => (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
    void Category(string title)
    {
        var grid = new Grid(); TextElement.SetFontWeight(grid, FontWeights.Normal); grid.ColumnDefinitions.Add(new() { Width = new(176) }); grid.ColumnDefinitions.Add(new());
        var nav = new ListBox { BorderThickness = new(0, 0, 1, 0), BorderBrush = Brush("#CCD6E1"), Background = Brush("#F7F9FC"), Padding = new(8), HorizontalContentAlignment = HorizontalAlignment.Stretch };
        nav.ItemTemplate = (DataTemplate)Resources["ParameterObjectTemplate"];
        ScrollViewer.SetHorizontalScrollBarVisibility(nav, ScrollBarVisibility.Disabled);
        AutomationProperties.SetName(nav, title + "对象");
        var content = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
        nav.SelectionChanged += (_, _) => { if (nav.SelectedItem is ObjectPage page) content.Content = page.Scroll; };
        grid.SizeChanged += (_, _) => grid.ColumnDefinitions[0].Width = new(grid.ActualWidth < 760 ? 136 : 176);
        Grid.SetColumn(content, 1); grid.Children.Add(nav); grid.Children.Add(content);
        categories.Items.Add(new TabItem { Header = title, MinHeight = 44, Content = grid }); navigators.Add(nav);
    }
    ObjectPage Page(int category, string title, string guidance)
    {
        var body = new StackPanel { Margin = new(20, 0, 20, 16), MaxWidth = 1120, HorizontalAlignment = HorizontalAlignment.Stretch };
        body.Children.Add(Text(title, true)); if (guidance.Length > 0) body.Children.Add(Text(guidance));
        var scroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var page = new ObjectPage(category, title, body, scroll); pages.Add(page); navigators[category].Items.Add(page); return page;
    }
    ParameterFormPanel Form(ObjectPage page, string? title = null)
    {
        if (title is not null) page.Body.Children.Add(Text(title, true));
        var form = new ParameterFormPanel(); page.Body.Children.Add(form); return form;
    }
    void Field(Panel form, string label, FrameworkElement control, string? help, string path, ObjectPage page)
    {
        var field = new StackPanel { Margin = new(0, 2, 16, 8) };
        var caption = Text(label); caption.Foreground = Brush("#263B50"); caption.FontSize = 15;
        field.Children.Add(caption); field.Children.Add(control);
        AutomationProperties.SetName(control, path); AutomationProperties.SetHelpText(control, help ?? label);
        if (help is not null) field.Children.Add(Text(help));
        var error = Text(""); error.Foreground = Brush("#B02D35"); error.Visibility = Visibility.Collapsed; field.Children.Add(error);
        form.Children.Add(field); locations[path] = new(page, control, error, label, help ?? "");
    }
    TextBox Input(Panel form, string label, object source, string property, string path, ObjectPage page, string? help = null, bool multiline = false)
    {
        var box = new TextBox { MinHeight = multiline ? 72 : 44, MaxLength = 65536, AcceptsReturn = multiline, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontSize = 16 };
        box.SetBinding(TextBox.TextProperty, new Binding(property) { Source = source, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        Field(form, label, box, help, path, page); return box;
    }
    void Choice(Panel form, string label, object source, string property, Array values, string path, ObjectPage page, string? help = null)
    {
        var choice = new ComboBox { ItemsSource = values, MinHeight = 44 };
        choice.SetBinding(ComboBox.SelectedItemProperty, new Binding(property) { Source = source, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        Field(form, label, choice, help, path, page);
    }
    void Readonly(ObjectPage page, string label, string value)
    {
        var row = new DockPanel { Margin = new(0, 0, 0, 8), LastChildFill = true };
        var caption = Text(label); caption.Margin = new(0, 10, 16, 0); DockPanel.SetDock(caption, Dock.Left); row.Children.Add(caption);
        var box = new TextBox { Text = value, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MinHeight = 36, BorderThickness = new(0), Background = Brushes.Transparent };
        AutomationProperties.SetName(box, label); row.Children.Add(box); page.Body.Children.Add(row);
    }
    void Render()
    {
        var selected = categories.SelectedIndex;
        var objects = navigators.Select(n => (n.SelectedItem as ObjectPage)?.Title).ToArray();
        foreach (var action in unsubscribe) action(); unsubscribe.Clear();
        categories.Items.Clear(); locations.Clear(); errors.Children.Clear(); pages.Clear(); navigators.Clear();
        if (Model is not { } m) { errors.Children.Add(Text("正在读取完整配置与已安装描述…")); return; }
        foreach (var name in new[] { "系统", "业务模块", "驱动", "组件" }) Category(name);
        BuildSystem(m); BuildBusiness(m); BuildCapabilities(m);
        foreach (var draft in m.Readers.Concat(m.Components))
        {
            PropertyChangedEventHandler handler = (_, e) => { if (e.PropertyName == nameof(CapabilityDraft.Provider)) Render(); };
            draft.PropertyChanged += handler; unsubscribe.Add(() => draft.PropertyChanged -= handler);
        }
        for (var i = 0; i < navigators.Count; i++) navigators[i].SelectedItem = pages.FirstOrDefault(p => p.Category == i && objects.ElementAtOrDefault(i) == p.Title) ?? pages.FirstOrDefault(p => p.Category == i);
        categories.SelectedIndex = Math.Clamp(selected, 0, 3); ShowCategory(categoryScope); ShowIssues(this, EventArgs.Empty); RefreshSearch();
    }
    void ShowIssues(object? sender, EventArgs e)
    {
        errors.Children.Clear();
        foreach (var item in locations.Values.Distinct()) { item.Control.ClearValue(Control.BorderBrushProperty); item.Error.Text = ""; item.Error.Visibility = Visibility.Collapsed; }
        foreach (var issue in Model?.Issues ?? [])
        {
            var found = locations.TryGetValue(issue.Path, out var location);
            var button = new Button { Content = new TextBlock { Text = issue.Category + " · " + issue.Message + (found ? "  → 定位" : "（完整候选检查，请查看维护详情）"), TextWrapping = TextWrapping.Wrap }, Style = (Style)Resources["ParameterResultAction"], Foreground = Brush("#B02D35"), Margin = new(4), MinHeight = 44 };
            if (found)
            {
                location!.Control.SetValue(Control.BorderBrushProperty, Brush("#B02D35")); location.Error.Text += issue.Message + "\n"; location.Error.Visibility = Visibility.Visible;
                button.Click += (_, _) => NavigateTo(issue.Path);
            }
            errors.Children.Add(button);
        }
    }
}

// Wide forms have two columns; compact forms retain readable controls and scroll.
public sealed class ParameterFormPanel : Panel
{
    static int Columns(double width) => double.IsFinite(width) && width >= 720 ? 2 : 1;
    protected override Size MeasureOverride(Size available)
    {
        var columns = Columns(available.Width); var width = double.IsFinite(available.Width) ? available.Width : 640; double height = 0;
        for (var i = 0; i < InternalChildren.Count; i += columns)
        {
            double row = 0;
            for (var j = i; j < Math.Min(i + columns, InternalChildren.Count); j++) { InternalChildren[j].Measure(new(width / columns, double.PositiveInfinity)); row = Math.Max(row, InternalChildren[j].DesiredSize.Height); }
            height += row;
        }
        return new(width, height);
    }
    protected override Size ArrangeOverride(Size final)
    {
        var columns = Columns(final.Width); double y = 0;
        for (var i = 0; i < InternalChildren.Count; i += columns)
        {
            var count = Math.Min(columns, InternalChildren.Count - i); double height = 0;
            for (var j = 0; j < count; j++) height = Math.Max(height, InternalChildren[i + j].DesiredSize.Height);
            for (var j = 0; j < count; j++) InternalChildren[i + j].Arrange(new Rect(j * final.Width / columns, y, final.Width / columns, height));
            y += height;
        }
        return final;
    }
}

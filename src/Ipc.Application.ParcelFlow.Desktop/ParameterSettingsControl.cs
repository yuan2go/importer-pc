using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Documents;
namespace Ipc.Application.ParcelFlow.Desktop;

// One directory over one complete draft, shared by startup and daily settings.
// Snapshot refreshes change bindings only; page switches retain every editor.
public sealed partial class ParameterSettingsControl : UserControl
{
    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(nameof(Model), typeof(ParameterEditorModel), typeof(ParameterSettingsControl), new PropertyMetadata(null, ModelChanged));
    public ParameterEditorModel? Model { get => (ParameterEditorModel?)GetValue(ModelProperty); set => SetValue(ModelProperty, value); }
    public bool FirstConfiguration { get; set; }
    readonly ListBox directory = new() { Name = "ParameterDirectory", BorderThickness = new(0), Background = Brushes.Transparent, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    readonly ContentControl pageHost = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    readonly StackPanel errors = new();
    readonly Dictionary<string, Location> locations = [];
    readonly List<Action> unsubscribe = [];
    readonly List<ObjectPage> pages = [];
    ICollectionView? directoryView;
    ObjectPage? reviewPage;
    FrameworkElement? reviewContent;
    public int SelectedCategory => (directory.SelectedItem as ObjectPage)?.Category ?? 0;
    public event EventHandler? SelectedPageChanged;
    readonly Dictionary<int, string> lastPages = [];
    sealed class ObjectPage(int category, string title, StackPanel body, ScrollViewer scroll, StackPanel technical) : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public int Category { get; } = category;
        public string Title { get => title; set { if (title == value) return; title = value; PropertyChanged?.Invoke(this, new(nameof(Title))); } }
        public StackPanel Body { get; } = body;
        public ScrollViewer Scroll { get; } = scroll;
        public StackPanel Technical { get; } = technical;
        public TextBlock Summary { get; } = new() { Foreground = Brush("#B42318"), TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 16), Visibility = Visibility.Collapsed };
        public TextBox Diagnostic { get; } = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new(0), Background = Brushes.Transparent, FontSize = UiTypography.Label };
        public string GroupTitle => Category == 4 ? "检查" : ParameterOwnership.CategoryName(Category);
        public int Order => Category == 4 ? 4 : Category == 1 ? 0 : Category == 0 ? 1 : Category;
        public override string ToString() => Title;
    }
    sealed record Location(ObjectPage Page, FrameworkElement Control, TextBlock Error, string Label, string Help, Func<bool>? Applicable = null, FrameworkElement? Row = null);
    int? categoryScope;
    public event EventHandler? MaintenanceRequested;
    public event EventHandler? BackupRequested;
    public event EventHandler? DisplayPreferencesRequested;
    public ParameterSettingsControl()
    {
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Ipc.Application.ParcelFlow.Desktop;component/ParameterSettingsTheme.xaml", UriKind.Relative) });
        FontFamily = UiTypography.Interface; FontSize = UiTypography.Body; FontWeight = FontWeights.Normal; Foreground = Brush("#25384B");
        var root = new Grid { Background = Brushes.White };
        root.ColumnDefinitions.Add(new() { Width = new(224) }); root.ColumnDefinitions.Add(new());
        var rail = new DockPanel { LastChildFill = true, Margin = new(12, 16, 12, 12) };
        var searchBar = BuildSearch(); DockPanel.SetDock(searchBar, Dock.Top); rail.Children.Add(searchBar);
        var lists = new Grid(); lists.Children.Add(directory); lists.Children.Add(searchResults); rail.Children.Add(lists);
        root.Children.Add(new Border { Background = Brush("#F5F6F8"), BorderBrush = Brush("#E1E5EB"), BorderThickness = new(0, 0, 1, 0), Child = rail });
        Grid.SetColumn(pageHost, 1); root.Children.Add(pageHost);
        directory.ItemTemplate = (DataTemplate)Resources["ParameterObjectTemplate"];
        directory.GroupStyle.Add((GroupStyle)Resources["ParameterDirectoryGroup"]);
        ScrollViewer.SetHorizontalScrollBarVisibility(directory, ScrollBarVisibility.Disabled);
        AutomationProperties.SetName(directory, "设置目录");
        AutomationProperties.SetHelpText(directory, "系统、业务模块、驱动、组件。使用上下方向键选择页面，制表键进入表单。");
        directory.SelectionChanged += DirectorySelectionChanged;
        Unloaded += (_, _) => navigationGeneration++;
        root.SizeChanged += (_, _) => root.ColumnDefinitions[0].Width = new(root.ActualWidth < 960 ? 192 : 224);
        Content = root;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { search.Focus(); search.SelectAll(); e.Handled = true; }
            else if (e.Key == Key.Escape && search.Text.Length > 0) { search.Clear(); search.Focus(); e.Handled = true; }
        };
    }
    // Startup and ordinary maintenance share this directory. Startup changes
    // the category order and adds its real review content, never another tab bar.
    public void SelectCategory(int category)
    {
        search.Clear(); categoryScope = null; directoryView?.Refresh();
        SelectPageCore(pages.FirstOrDefault(p => p.Category == category && p.Title == lastPages.GetValueOrDefault(category)) ?? pages.FirstOrDefault(p => p.Category == category));
        if (directory.SelectedItem is { } item) directory.ScrollIntoView(item);
    }
    public void SetReviewContent(FrameworkElement content)
    {
        if (reviewContent?.Parent is Panel previous) previous.Children.Remove(reviewContent);
        reviewContent = content;
        EnsureReviewPage(); reviewPage!.Body.Children.Add(content);
    }
    public void ShowReview() { EnsureReviewPage(); SelectCategory(4); }
    void EnsureReviewPage()
    {
        if (reviewPage is not null) return;
        reviewPage = Page(4, FirstConfiguration ? "检查与完成" : "检查结果", "检查当前完整候选；保存与运行状态分别核对。");
        if (errors.Parent is Panel old) old.Children.Remove(errors);
        reviewPage.Body.Children.Add(errors);
        reviewPage.Technical.Children.Add(reviewPage.Diagnostic);
        reviewPage.Body.Children.Add(new Expander { Header = "诊断详情", Content = reviewPage.Technical, Margin = new(0, 16, 0, 0) });
        directoryView?.Refresh();
    }
    public void ShowCategory(int? index)
    {
        if (index is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(index));
        // Filtering a WPF collection can automatically select its first item.
        // Resolve the remembered destination before Refresh changes selection.
        var selected = directory.SelectedItem as ObjectPage;
        var destination = selected is not null && (index is null || selected.Category == index) ? selected :
            pages.FirstOrDefault(p => p.Category == index && lastPages.GetValueOrDefault(p.Category) == p.Title) ?? pages.FirstOrDefault(p => index is null || p.Category == index);
        categoryScope = index; directoryView?.Refresh();
        SelectPageCore(destination);
        RefreshSearch();
    }
    public bool ShowPage(int category, string title)
    {
        var page = pages.FirstOrDefault(p => p.Category == category && p.Title == title);
        if (page is null) return false;
        search.Clear();
        if (categoryScope is not null && categoryScope != category) ShowCategory(category);
        SelectPageCore(page); return true;
    }
    public bool FocusFirstIssue()
    {
        foreach (var issue in Model?.Issues ?? []) if (NavigateTo(issue.Path)) return true;
        if (errors.Children.OfType<Button>().FirstOrDefault() is { } first) return first.Focus();
        return false;
    }
    public bool NavigateTo(string path)
    {
        if (!locations.TryGetValue(path, out var location) || location.Applicable?.Invoke() == false) return false;
        ShowPage(location.Page.Category, location.Page.Title);
        for (DependencyObject? parent = location.Control; parent is not null; parent = LogicalTreeHelper.GetParent(parent) ?? (parent is Visual ? VisualTreeHelper.GetParent(parent) : null))
            if (parent is Expander details) details.IsExpanded = true;
        UpdateLayout(); location.Control.BringIntoView(); location.Control.Focus(); return true;
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
        Text = value, TextWrapping = TextWrapping.Wrap, FontSize = heading ? UiTypography.Group : UiTypography.Label,
        Foreground = Brush(heading ? "#1C3044" : "#5A6878"),
        FontWeight = heading ? UiTypography.Emphasis : FontWeights.Normal, Margin = new(0, heading ? 16 : 4, 0, 8)
    };
    static SolidColorBrush Brush(string color) => (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
    ObjectPage Page(int category, string title, string guidance)
    {
        var body = new StackPanel { Margin = new(28, 20, 28, 20), MaxWidth = 968, HorizontalAlignment = HorizontalAlignment.Left };
        TextElement.SetFontWeight(body, FontWeights.Normal);
        var heading = Text(title, true); heading.FontSize = UiTypography.PageTitle; heading.Margin = new(0, 0, 0, 8); body.Children.Add(heading);
        if (guidance.Length > 0) { var note = Text(guidance); note.Margin = new(0, 0, 0, 12); body.Children.Add(note); }
        var scroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalContentAlignment = HorizontalAlignment.Left, Focusable = false };
        scroll.SizeChanged += (_, _) => body.Width = Math.Min(968, Math.Max(0, scroll.ActualWidth - 76));
        var page = new ObjectPage(category, title, body, scroll, new StackPanel());
        AutomationProperties.SetLiveSetting(page.Summary, AutomationLiveSetting.Polite);
        body.Children.Add(page.Summary);
        heading.SetBinding(TextBlock.TextProperty, new Binding(nameof(ObjectPage.Title)) { Source = page });
        pages.Add(page); return page;
    }
    ParameterFormPanel Form(ObjectPage page, string? title = null)
    {
        if (title is not null) { var heading = Text(title, true); if (page.Body.Children.Count <= 2) heading.Margin = new(0, 0, 0, 12); page.Body.Children.Add(heading); }
        var form = new ParameterFormPanel(); page.Body.Children.Add(form); return form;
    }
    ParameterFormPanel AdvancedForm(ObjectPage page, string title = "高级设置")
    {
        var form = new ParameterFormPanel();
        page.Body.Children.Add(new Expander { Header = title, Content = form, Margin = new(0, 16, 0, 8), Padding = new(0, 8, 0, 0) }); return form;
    }
    void Field(Panel form, string label, FrameworkElement control, string? help, string path, ObjectPage page)
    {
        var focusedPage = page.Title is "左相机" or "右相机" or "供包台标识";
        var field = new Grid { Margin = new(0, 0, 0, focusedPage ? 12 : 18) };
        field.ColumnDefinitions.Add(new() { Width = new(148) }); field.ColumnDefinitions.Add(new());
        field.RowDefinitions.Add(new() { Height = GridLength.Auto }); field.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var caption = Text(label); caption.VerticalAlignment = VerticalAlignment.Top; caption.Foreground = Brush("#202833"); caption.Margin = new(0, 8, 12, 0);
        var value = new StackPanel(); Grid.SetColumn(value, 1); field.Children.Add(caption); field.Children.Add(value);
        var line = new DockPanel { LastChildFill = true };
        var unit = path.Split('/').Last() switch { "TimeoutMs" or "SessionLifetimeMs" => "毫秒", "TargetEfficiencyDraft" => "件/小时", "ResidentHistory" or "QueueCapacity" => "条", "MaximumOperations" => "个", "MinimumFreeMiB" => "MiB", "DiagnosticLogDays" => "天", "BackupIntervalMinutes" => "分钟", "BackupMaximumSets" => "份", "heartbeat-interval-seconds" => "秒", _ => "" };
        if (unit.Length > 0) { line.MaxWidth = (double.IsFinite(control.MaxWidth) ? control.MaxWidth : 240) + 64; line.HorizontalAlignment = HorizontalAlignment.Left; line.SetBinding(WidthProperty, new Binding(nameof(ActualWidth)) { Source = value }); var suffix = Text(unit); suffix.Width = 54; suffix.Margin = new(10, 8, 0, 0); DockPanel.SetDock(suffix, Dock.Right); line.Children.Add(suffix); }
        line.SizeChanged += (_, _) => { if (unit.Length > 0) line.MaxWidth = (double.IsFinite(control.MaxWidth) ? control.MaxWidth : 240) + 64; };
        var inputHost = new Grid(); inputHost.Children.Add(control); line.Children.Add(inputHost); value.Children.Add(line);
        if (control is TextBox or ComboBox or PasswordBox)
        {
            // The field's layout slot supplies the width; the editor's cap keeps
            // short values compact. Empty text and one digit use the same box.
            control.HorizontalAlignment = HorizontalAlignment.Left;
            control.SetBinding(WidthProperty, new Binding(nameof(ActualWidth)) { Source = inputHost, Mode = BindingMode.OneWay });
            if (control is ComboBox) control.MaxWidth = 400;
        }
        // Preserve stable automation paths used by support and native verification.
        AutomationProperties.SetName(control, path); AutomationProperties.SetAutomationId(control, path);
        AutomationProperties.SetLabeledBy(control, caption); AutomationProperties.SetHelpText(control, help ?? label);
        // Routine schema hints remain available to assistive technology and in
        // technical information, without adding a repeated line to every field.
        var routine = help is not null && (help == "选择已声明的可用值" || help.StartsWith("最多 ", StringComparison.Ordinal) && help.EndsWith(" 个字符", StringComparison.Ordinal));
        if (routine) control.ToolTip = help;
        else if (!string.IsNullOrEmpty(help))
        {
            var hint = Text(help); hint.FontSize = UiTypography.Label; hint.Margin = new(0, 4, 0, 0);
            // Identity/connection explanations are on demand. Entity admission and
            // required constraints remain inline; errors always stay next to the field.
            if (focusedPage && path.Split('/').Last() is "device.ip" or "device.serial" or "bcr-code" or "left-bcr-code" or "right-bcr-code" or "left-station-code" or "right-station-code" or "StationCode")
            {
                control.ToolTip = help;
                page.Technical.Children.Add(Text(label.TrimEnd(' ', '*') + "：" + help));
            }
            else value.Children.Add(hint);
        }
        var error = Text(""); error.Foreground = Brush("#B42318"); error.Margin = new(0, 6, 0, 0); error.Visibility = Visibility.Collapsed; value.Children.Add(error);
        form.Children.Add(field); locations[path] = new(page, control, error, label, help ?? "", Row: field);
        field.SizeChanged += (_, _) =>
        {
            var compact = field.ActualWidth < 500;
            field.ColumnDefinitions[0].Width = compact ? new(0) : new(148);
            Grid.SetColumn(caption, compact ? 1 : 0); Grid.SetRow(value, compact ? 1 : 0);
            caption.Margin = compact ? new(0, 0, 0, 6) : new(0, 8, 12, 0);
        };
        // Refresh an already-shown local error after an actual edit and blur.
        // Do not reflow the form while typing or treat this as authority validation.
        void Edited()
        {
            editRevision++;
            var model = Model;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.DataBind, new Action(() => { if (ReferenceEquals(Model, model)) model?.RevalidateAfterEdit(); }));
        }
        if (control is TextBox text) text.TextChanged += (_, _) => Edited();
        else if (control is PasswordBox password) password.PasswordChanged += (_, _) => Edited();
        else if (control is ComboBox choice) choice.SelectionChanged += (_, _) => Edited();
    }
    TextBox Input(Panel form, string label, object source, string property, string path, ObjectPage page, string? help = null, bool multiline = false)
    {
        var box = new TextBox { MinHeight = 40, MaxLength = 65536, AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, FontSize = UiTypography.Body, VerticalContentAlignment = multiline ? VerticalAlignment.Top : VerticalAlignment.Center };
        box.SetBinding(TextBox.TextProperty, new Binding(property) { Source = source, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        box.MaxWidth = multiline || property is "Target" or "BarcodePattern" or "BackupDirectory" ? 760 :
            property is "QueueCapacity" or "TimeoutMs" or "MaximumOperations" or "ResidentHistory" or "MinimumFreeMiB" or "DiagnosticLogDays" or "SessionLifetimeMs" or "BackupIntervalMinutes" or "BackupMaximumSets" or "Precision" or "ConfiguredWeight" or "MaximumAgeMs" or "WaitTimeoutMs" or "ImageBytes" or "TotalBytes" or "MaximumPending" ? 240 : 400;
        Field(form, label, box, help, path, page); return box;
    }
    void Choice(Panel form, string label, object source, string property, Array values, string path, ObjectPage page, string? help = null)
    {
        var choice = new ComboBox { ItemsSource = values, ItemTemplate = ChineseChoiceTextConverter.CreateTemplate(), MinHeight = 40, FontSize = UiTypography.Body };
        choice.SetBinding(ComboBox.SelectedItemProperty, new Binding(property) { Source = source, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        Field(form, label, choice, help, path, page);
    }
    void Readonly(ObjectPage page, string label, string value)
    {
        var row = new StackPanel { Margin = new(0, 0, 24, 16) };
        var caption = Text(label); caption.Margin = new(0, 0, 0, 4); row.Children.Add(caption);
        var box = new TextBox { Text = value, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MinHeight = 28, Padding = new(0), BorderThickness = new(0), Background = Brushes.Transparent };
        AutomationProperties.SetName(box, label); row.Children.Add(box); page.Body.Children.Add(row);
    }
    void Technical(ObjectPage page, string label, string path, string? detail = null)
    {
        var text = new TextBox { IsReadOnly = true, Text = label + " · " + path + (string.IsNullOrWhiteSpace(detail) ? "" : "\n" + detail),
            TextWrapping = TextWrapping.Wrap, BorderThickness = new(0), Background = Brushes.Transparent, Margin = new(0, 0, 0, 10), Padding = new(0), MinHeight = 28, FontSize = UiTypography.Label };
        page.Technical.Children.Add(text);
        if (!locations.ContainsKey(path))
        {
            var error = Text(""); error.Visibility = Visibility.Collapsed; page.Technical.Children.Add(error);
            locations[path] = new(page, text, error, label, detail ?? "");
            AutomationProperties.SetName(text, path);
        }
    }
    void Render()
    {
        var selected = directory.SelectedItem as ObjectPage;
        navigationGeneration++; currentPage = null;
        var offsets = pages.ToDictionary(p => (p.Category, p.Title), p => p.Scroll.VerticalOffset);
        foreach (var action in unsubscribe) action(); unsubscribe.Clear();
        runtimeUpdates.Clear();
        directory.ItemsSource = null; pageHost.Content = null; locations.Clear(); errors.Children.Clear(); pages.Clear();
        reviewPage = null;
        if (reviewContent?.Parent is Panel reviewParent) reviewParent.Children.Remove(reviewContent);
        if (Model is not { } m) { pageHost.Content = Text("正在读取设置与已安装模块…"); return; }
        BuildSystem(m); BuildBusiness(m); BuildCapabilities(m);
        foreach (var page in pages)
        {
            page.Technical.Children.Add(page.Diagnostic);
            if (page.Technical.Children.Count > 0) page.Body.Children.Add(new Expander { Header = "参数说明与诊断详情", Content = page.Technical, Margin = new(0, 16, 0, 0), Padding = new(0, 8, 0, 0) });
            if (offsets.TryGetValue((page.Category, page.Title), out var offset)) page.Scroll.ScrollToVerticalOffset(offset);
        }
        foreach (var draft in m.Readers.Concat(m.Components))
        {
            PropertyChangedEventHandler handler = (_, e) => { if (e.PropertyName == nameof(CapabilityDraft.Provider)) Render(); };
            draft.PropertyChanged += handler; unsubscribe.Add(() => draft.PropertyChanged -= handler);
        }
        if (FirstConfiguration || m.Issues.Count > 0) { EnsureReviewPage(); if (reviewContent is not null) reviewPage!.Body.Children.Add(reviewContent); }
        directoryView = new ListCollectionView(pages);
        if (FirstConfiguration) directoryView.SortDescriptions.Add(new SortDescription(nameof(ObjectPage.Order), ListSortDirection.Ascending));
        directoryView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ObjectPage.GroupTitle)));
        directoryView.Filter = value => value is ObjectPage p && (categoryScope is null || p.Category == categoryScope);
        directory.ItemsSource = directoryView;
        SelectPageCore(pages.FirstOrDefault(p => p.Category == selected?.Category && p.Title == selected.Title && (categoryScope is null || p.Category == categoryScope)) ?? (FirstConfiguration ? OrderedPages() : pages.ToArray()).FirstOrDefault(p => categoryScope is null || p.Category == categoryScope));
        ShowIssues(this, EventArgs.Empty); RefreshSearch();
    }
    void ShowIssues(object? sender, EventArgs e)
    {
        if (reviewPage is null && Model?.Issues.Count > 0) EnsureReviewPage();
        errors.Children.Clear();
        foreach (var item in locations.Values.Distinct()) { item.Control.ClearValue(Control.BorderBrushProperty); item.Error.Text = ""; item.Error.Visibility = Visibility.Collapsed; }
        foreach (var page in pages) { page.Summary.Text = ""; page.Summary.Visibility = Visibility.Collapsed; page.Diagnostic.Text = ""; }
        foreach (var issue in Model?.Issues ?? [])
        {
            var found = locations.TryGetValue(issue.Path, out var location);
            var message = IssueMessage(issue, location);
            var context = found ? issue.Category + " / " + location!.Page.Title + " / " + location.Label.TrimEnd(' ', '*') : issue.Category;
            var button = new Button { Content = new TextBlock { Text = context + "\n" + message + (found ? "  定位字段" : ""), TextWrapping = TextWrapping.Wrap }, Style = (Style)Resources["ParameterResultAction"], Foreground = Brush("#B42318"), Margin = new(0, 2, 0, 2), MinHeight = 44 };
            if (found)
            {
                location!.Control.SetValue(Control.BorderBrushProperty, Brush("#B42318")); location.Error.Text += (location.Error.Text.Length > 0 ? "\n" : "") + message; location.Error.Visibility = Visibility.Visible;
                location.Page.Diagnostic.Text += issue.Path + " · " + Ipc.Platform.DiagnosticText.Protect(issue.Message) + "\n";
                button.Click += (_, _) => NavigateTo(issue.Path);
            }
            else if (reviewPage is not null) reviewPage.Diagnostic.Text += Ipc.Platform.DiagnosticText.Protect(issue.Message) + "\n";
            errors.Children.Add(found ? button : Text(context + "\n" + message));
        }
        foreach (var page in pages)
        {
            var count = (Model?.Issues ?? []).Count(i => locations.TryGetValue(i.Path, out var entry) && ReferenceEquals(entry.Page, page));
            page.Summary.Text = page.Category == 4 ? errors.Children.Count == 0 ? "当前没有字段错误；正式保存仍需核对完整配置。" : "有 " + errors.Children.Count + " 项需要修改" : "有 " + count + " 项需要修改";
            page.Summary.Visibility = count > 0 || page.Category == 4 && errors.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (page.Category != 4 && count > 0)
            {
                var first = (Model?.Issues ?? []).First(i => locations.TryGetValue(i.Path, out var entry) && ReferenceEquals(entry.Page, page));
                var locate = new Hyperlink(new Run("定位首项")); locate.Click += (_, _) => NavigateTo(first.Path);
                page.Summary.Inlines.Add(new Run("    ")); page.Summary.Inlines.Add(locate);
            }
        }
    }
    string IssueMessage(ParameterIssue issue, Location? location)
    {
        var label = location?.Label.TrimEnd(' ', '*') ?? "此参数";
        if (issue.Message.EndsWith(": required", StringComparison.Ordinal)) return (issue.Path.EndsWith("/specified-chute", StringComparison.Ordinal) ? "请选择" : "请填写") + label + "。";
        if (issue.Message.Contains(": invalid value", StringComparison.Ordinal))
        {
            var parameter = Model?.Readers.Concat(Model.Components).SelectMany(d => d.Parameters.Select(p => (Path: d.InstanceId + "/" + p.Field.Key, Parameter: p))).FirstOrDefault(x => x.Path == issue.Path).Parameter;
            return parameter?.Field.Type == "integer" ? "请填写 " + parameter.Field.Minimum + "～" + parameter.Field.Maximum + " 范围内的整数。" : "请检查" + label + "的格式、长度或可选值。";
        }
        var original = issue.Message;
        if (location is not null)
        {
            var key = issue.Path.Split('/').Last();
            if (key.Length > 0) original = original.Replace(key, label, StringComparison.Ordinal);
            foreach (var draft in (Model?.Readers ?? []).Concat(Model?.Components ?? []))
                if (original.StartsWith(draft.Role + " · ", StringComparison.Ordinal)) original = original[(draft.Role.Length + 3)..];
        }
        var text = ChineseInterfaceText.ParameterMessage(original);
        return text.Any(c => c is >= '\u4e00' and <= '\u9fff') ? text : "配置检查未通过，请展开诊断详情核对原因。";
    }
}

// One aligned field per row at every width; narrow fields stack label and input.
public sealed class ParameterFormPanel : Panel
{
    static int Columns(double width) => 1;
    protected override Size MeasureOverride(Size available)
    {
        var columns = InternalChildren.Count <= 1 ? 1 : Columns(available.Width); var width = double.IsFinite(available.Width) ? available.Width : 640; double height = 0;
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
        var columns = InternalChildren.Count <= 1 ? 1 : Columns(final.Width); double y = 0;
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

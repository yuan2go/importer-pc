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
    readonly WrapPanel firstStepBar = new() { Margin = new(12, 8, 12, 8), Visibility = Visibility.Collapsed };
    readonly TextBlock remainingSteps = new() { TextWrapping = TextWrapping.Wrap, Margin = new(4, 12, 4, 8), FontSize = UiTypography.Label };
    readonly ListBox directory = new() { Name = "ParameterDirectory", BorderThickness = new(0), Background = Brushes.Transparent, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    readonly ContentControl pageHost = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    Grid? settingsRoot;
    Border? directoryRail;
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
        public Func<bool>? Applicable { get; set; }
        public bool Available => Applicable?.Invoke() != false;
        public TextBlock Summary { get; } = Themed(new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 16), Visibility = Visibility.Collapsed }, TextBlock.ForegroundProperty, "Danger");
        public TextBox Diagnostic { get; } = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new(0), Background = Brushes.Transparent, FontSize = UiTypography.Label };
        public string? TaskGroup { get; set; }
        public string GroupTitle => TaskGroup ?? (Category == 4 ? "检查" : ParameterOwnership.CategoryName(Category));
        public int Order => Category;
        public int TaskOrder => TaskGroup switch { "本站接入" => 0, "相机" => 1, "分拣规则" => 2, "系统维护" => 3, _ => 4 };
        int issueCount;
        public string DisplayTitle => Title + (issueCount == 0 ? "" : "（" + issueCount + "）");
        public void SetIssueCount(int count) { if (issueCount == count) return; issueCount = count; PropertyChanged?.Invoke(this, new(nameof(DisplayTitle))); }
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
        FontFamily = UiTypography.Interface; FontSize = UiTypography.Body; FontWeight = FontWeights.Normal; SetResourceReference(ForegroundProperty, "Ink");
        // Settings content sits on the theme's page background; each setting is a white card on it.
        var root = Themed(new Grid(), Panel.BackgroundProperty, "Background");
        settingsRoot = root;
        root.ColumnDefinitions.Add(new() { Width = new(240) }); root.ColumnDefinitions.Add(new());
        var rail = new DockPanel { LastChildFill = true, Margin = new(12, 16, 12, 12) };
        root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new());
        Grid.SetColumnSpan(firstStepBar, 2); root.Children.Add(firstStepBar);
        Grid.SetRow(pageHost, 1);
        var names = new[] { "本站信息", "业务规则", "左右相机", "RCS 连接", "检查与完成" };
        for (var step = 0; step < names.Length; step++)
        {
            var index = step;
            var button = new Button { Content = (step + 1) + " " + names[step], Tag = names[step], MinHeight = 44, Margin = new(0, 0, 8, 4) };
            AutomationProperties.SetName(button, "首次配置第" + (step + 1) + "步：" + names[step]);
            button.Click += async (_, _) => { var target = OrderedPages().FirstOrDefault(p => p.Category == index); if (target is not null) await RequestPageAsync(target); };
            firstStepBar.Children.Add(button);
        }
        DockPanel.SetDock(remainingSteps, Dock.Bottom); rail.Children.Add(remainingSteps);
        var searchBar = BuildSearch(); DockPanel.SetDock(searchBar, Dock.Top); rail.Children.Add(searchBar);
        var lists = new Grid(); lists.Children.Add(directory); lists.Children.Add(searchResults); rail.Children.Add(lists);
        var railBorder = new Border { BorderThickness = new(0, 0, 1, 0), Child = rail }; Grid.SetRow(railBorder, 1); root.Children.Add(railBorder);
        directoryRail = railBorder;
        railBorder.SetResourceReference(Border.BackgroundProperty, "Surface"); railBorder.SetResourceReference(Border.BorderBrushProperty, "Line");
        Grid.SetColumn(pageHost, 1); root.Children.Add(pageHost);
        directory.ItemTemplate = (DataTemplate)Resources["ParameterObjectTemplate"];
        directory.GroupStyle.Add((GroupStyle)Resources["ParameterDirectoryGroup"]);
        ScrollViewer.SetHorizontalScrollBarVisibility(directory, ScrollBarVisibility.Disabled);
        AutomationProperties.SetName(directory, "设置目录");
        AutomationProperties.SetHelpText(directory, "系统、业务模块、驱动、组件。使用上下方向键选择页面，制表键进入表单。");
        directory.SelectionChanged += DirectorySelectionChanged;
        Unloaded += (_, _) => navigationGeneration++;
        root.SizeChanged += (_, _) => UpdateDirectoryVisibility();
        Content = root;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control && directoryRail?.Visibility == Visibility.Visible) { search.Focus(); search.SelectAll(); e.Handled = true; }
            else if (e.Key == Key.Escape && search.Text.Length > 0) { search.Clear(); search.Focus(); e.Handled = true; }
        };
    }
    // Startup and ordinary maintenance share this directory. Startup changes
    // the category order and adds its real review content, never another tab bar.
    public void SelectCategory(int category)
    {
        search.Clear(); var priorSelecting = selectingPage; selectingPage = true;
        try
        {
            categoryScope = null; directoryView?.Refresh();
            SelectPageCore(pages.FirstOrDefault(p => p.Available && p.Category == category && p.Title == lastPages.GetValueOrDefault(category)) ?? pages.FirstOrDefault(p => p.Available && p.Category == category));
        }
        finally { selectingPage = priorSelecting; }
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
        reviewPage = Page(4, FirstConfiguration ? "检查与完成" : "检查结果", "检查全部设置。保存后需重启软件才会生效。");
        if (errors.Parent is Panel old) old.Children.Remove(errors);
        if (UsesFirstReadiness)
        {
            reviewPage.Body.Children.Add(ReadinessCard());
            if (!readinessHooked) { readinessHooked = true; SelectedPageChanged += (_, _) => { if (currentPage?.Category == 4) RefreshReadiness(); }; }
        }
        reviewPage.Body.Children.Add(errors);
        reviewPage.Technical.Children.Add(reviewPage.Diagnostic);
        reviewPage.Body.Children.Add(new Expander { Header = "诊断详情", Content = reviewPage.Technical, Margin = new(0, 16, 0, 0) });
        var priorSelecting = selectingPage; selectingPage = true;
        try { directoryView?.Refresh(); }
        finally { selectingPage = priorSelecting; }
    }
    public void ShowCategory(int? index)
    {
        if (index is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(index));
        // Filtering a WPF collection can automatically select its first item.
        // Resolve the remembered destination before Refresh changes selection.
        var selected = directory.SelectedItem as ObjectPage;
        var destination = selected is not null && selected.Available && (index is null || selected.Category == index) ? selected :
            pages.FirstOrDefault(p => p.Available && p.Category == index && lastPages.GetValueOrDefault(p.Category) == p.Title) ?? pages.FirstOrDefault(p => p.Available && (index is null || p.Category == index));
        var priorSelecting = selectingPage; selectingPage = true;
        try
        {
            // Filtering can synchronously select the view's new current item.
            // This is programmatic navigation, not a user's forward-step intent.
            categoryScope = index; directoryView?.Refresh();
            SelectPageCore(destination); RefreshSearch();
        }
        finally { selectingPage = priorSelecting; }
    }
    public bool ShowPage(int category, string title)
    {
        var page = pages.FirstOrDefault(p => p.Available && p.Category == category && p.Title == title);
        if (page is null && Model?.ApplicationId == "sportmaster")
            page = pages.FirstOrDefault(p => p.Available && (title is "左相机" or "右相机" && p.Title == "左右相机" || title is "供包台标识" or "RCS 服务" && p.Title == "站点编号与 RCS"));
        if (page is null) return false;
        search.Clear();
        if (categoryScope is not null && categoryScope != page.Category) ShowCategory(page.Category);
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
        if (!locations.TryGetValue(path, out var location) || !location.Page.Available || location.Applicable?.Invoke() == false) return false;
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
    static TextBlock Text(string value, bool heading = false) => Themed(new TextBlock
    {
        Text = value,
        TextWrapping = TextWrapping.Wrap,
        FontSize = heading ? UiTypography.Group : UiTypography.Label,
        FontWeight = heading ? UiTypography.Emphasis : FontWeights.Normal,
        Margin = new(0, heading ? 24 : 4, 0, 8)
    }, TextBlock.ForegroundProperty, heading ? "Ink" : "Muted");
    // Colours come from the application theme (Ipc.Desktop/Themes/Workstation.xaml): Ink, Muted, Line,
    // Surface, Background, Soft, Accent*, Success, Warning, Danger.
    static T Themed<T>(T element, DependencyProperty property, string key) where T : FrameworkElement
    { element.SetResourceReference(property, key); return element; }
    static Border Card(UIElement? child = null)
    {
        var card = new Border { BorderThickness = new(1), CornerRadius = new(4), Padding = new(16, 12, 16, 12), Margin = new(0, 0, 0, 4), SnapsToDevicePixels = true, Child = child };
        card.SetResourceReference(Border.BackgroundProperty, "Surface"); card.SetResourceReference(Border.BorderBrushProperty, "Line");
        return card;
    }
    ObjectPage Page(int category, string title, string guidance)
    {
        var body = new StackPanel { Margin = new(32, 24, 32, 32), MaxWidth = 960, HorizontalAlignment = HorizontalAlignment.Left };
        TextElement.SetFontWeight(body, FontWeights.Normal);
        var heading = Text(title, true); heading.FontSize = UiTypography.PageTitle; heading.Margin = new(0, 0, 0, 4); body.Children.Add(heading);
        if (guidance.Length > 0) { var note = Text(guidance); note.Margin = new(0, 0, 0, 8); body.Children.Add(note); }
        var scroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalContentAlignment = HorizontalAlignment.Left, Focusable = false };
        scroll.SizeChanged += (_, _) => body.Width = Math.Min(960, Math.Max(0, scroll.ActualWidth - 76));
        var page = new ObjectPage(category, title, body, scroll, new StackPanel());
        AutomationProperties.SetLiveSetting(page.Summary, AutomationLiveSetting.Polite);
        body.Children.Add(page.Summary);
        heading.SetBinding(TextBlock.TextProperty, new Binding(nameof(ObjectPage.Title)) { Source = page });
        pages.Add(page); return page;
    }
    // A section: an optional heading, then its settings as cards.
    ParameterFormPanel Form(ObjectPage page, string? title = null)
    {
        if (title is not null) { var heading = Text(title, true); if (page.Body.Children.Count <= 3) heading.Margin = new(0, 16, 0, 8); page.Body.Children.Add(heading); }
        var form = new ParameterFormPanel(); page.Body.Children.Add(form); return form;
    }
    (TextBlock Heading, ParameterFormPanel Form) Section(ObjectPage page, string title)
    {
        var heading = Text(title, true); page.Body.Children.Add(heading);
        var form = new ParameterFormPanel(); page.Body.Children.Add(form); return (heading, form);
    }
    ParameterFormPanel AdvancedForm(ObjectPage page, string title = "高级设置")
    {
        var form = new ParameterFormPanel();
        page.Body.Children.Add(new Expander { Header = title, Content = form, Margin = new(0, 24, 0, 4), Padding = new(0, 4, 0, 0) }); return form;
    }
    // One card per setting: its name and explanation on the left, the control on the right.
    // Wide values (addresses, patterns, lists) and narrow windows put the control under the text.
    void Field(Panel form, string label, FrameworkElement control, string? help, string path, ObjectPage page)
    {
        var field = new Grid();
        var card = Card(field);
        field.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); field.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        for (var row = 0; row < 3; row++) field.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var caption = Themed(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, FontSize = UiTypography.Body }, TextBlock.ForegroundProperty, "Ink");
        var describe = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; describe.Children.Add(caption); field.Children.Add(describe);
        var line = new DockPanel { LastChildFill = true, VerticalAlignment = VerticalAlignment.Center };
        var unit = path.Split('/').Last() switch { "TimeoutMs" or "SessionLifetimeMs" or "request-timeout-ms" => "毫秒", "TargetEfficiencyDraft" => "件/小时", "ResidentHistory" or "QueueCapacity" => "条", "MaximumOperations" => "个", "MinimumFreeMiB" => "MiB", "DiagnosticLogDays" or "HistoryDays" => "天", "BackupIntervalMinutes" => "分钟", "BackupMaximumSets" => "份", "heartbeat-interval-ms" => "毫秒", _ => "" };
        if (unit.Length > 0) { var suffix = Text(unit); suffix.Margin = new(8, 0, 0, 0); suffix.VerticalAlignment = VerticalAlignment.Center; DockPanel.SetDock(suffix, Dock.Right); line.Children.Add(suffix); }
        line.Children.Add(control); field.Children.Add(line);
        // Preserve stable automation paths used by support and native verification.
        AutomationProperties.SetName(control, label.TrimEnd(' ', '*')); AutomationProperties.SetAutomationId(control, path);
        AutomationProperties.SetLabeledBy(control, caption); AutomationProperties.SetHelpText(control, help ?? label);
        // Routine schema hints stay available as tooltip and to assistive technology
        // without adding a repeated line to every field.
        var routine = help is not null && (help == "从列表中选择" || help.StartsWith("最多 ", StringComparison.Ordinal) && help.EndsWith(" 个字符", StringComparison.Ordinal));
        if (routine) control.ToolTip = help;
        else if (!string.IsNullOrEmpty(help))
        {
            var hint = Text(help); hint.Margin = new(0, 2, 0, 0);
            describe.Children.Add(hint);
        }
        var error = Text(""); error.SetResourceReference(TextBlock.ForegroundProperty, "Danger"); error.Margin = new(0, 6, 0, 0); error.Visibility = Visibility.Collapsed; field.Children.Add(error);
        form.Children.Add(card); locations[path] = new(page, control, error, label, help ?? "", Row: card);
        var draft = Model?.Readers.Concat(Model.Components).FirstOrDefault(d => path.StartsWith(d.InstanceId + "/", StringComparison.Ordinal));
        if (draft is not null && path != draft.InstanceId + "/ConfigureLater" && !path.EndsWith("-station-code", StringComparison.Ordinal))
        {
            var cameraCode = path.Split('/').Last() is "left-bcr-code" or "right-bcr-code";
            var camera = cameraCode ? Model!.Readers.SingleOrDefault(r => path.EndsWith("/" + r.Role + "-bcr-code", StringComparison.Ordinal)) : null;
            void RefreshRequirement() => caption.Text = draft.ConfigureLater || cameraCode && (!draft.Bound || camera is null || camera.ConfigureLater) ? label.TrimEnd(' ', '*') : label;
            PropertyChangedEventHandler changed = (_, e) => { if (e.PropertyName is nameof(CapabilityDraft.ConfigureLater) or nameof(CapabilityDraft.Bound)) RefreshRequirement(); };
            draft.PropertyChanged += changed; unsubscribe.Add(() => draft.PropertyChanged -= changed); RefreshRequirement();
            if (camera is not null) { camera.PropertyChanged += changed; unsubscribe.Add(() => camera.PropertyChanged -= changed); }
        }
        // The control's cap sizes the field (its creator may still lower it, so it is read on every layout):
        // short numbers 200, ordinary values 320, wide values the full card width.
        void Arrange()
        {
            var cap = control.MaxWidth;
            var input = control is TextBox or PasswordBox or ComboBox or Slider;
            var wide = control is TextBox box && (box.AcceptsReturn || box.IsReadOnly || !double.IsFinite(cap) || cap >= 760) || !input && control is not CheckBox && control.Tag as string != "inline";
            var stacked = wide || card.ActualWidth is > 0 and < 600;
            Grid.SetColumnSpan(describe, stacked ? 2 : 1); describe.Margin = stacked ? new(0) : new(0, 0, 24, 0);
            Grid.SetRow(line, stacked ? 1 : 0); Grid.SetColumn(line, stacked ? 0 : 1); Grid.SetColumnSpan(line, stacked ? 2 : 1);
            line.Margin = stacked ? new(0, 8, 0, 0) : new(0); line.HorizontalAlignment = wide ? HorizontalAlignment.Stretch : stacked ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            Grid.SetRow(error, stacked ? 2 : 1); Grid.SetColumnSpan(error, 2);
            if (input)
            {
                control.HorizontalAlignment = wide ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
                if (wide) { control.Width = double.NaN; control.MaxWidth = double.PositiveInfinity; }
                else control.Width = Math.Min(cap, cap <= 240 ? 200 : 320);
            }
        }
        Arrange(); card.SizeChanged += (_, e) => { if (e.WidthChanged) Arrange(); };
        TrackEdits(control);
    }
    // Refresh an already-shown local error after an actual edit and blur.
    // Do not reflow the form while typing or treat this as authority validation.
    void TrackEdits(FrameworkElement control)
    {
        void Edited()
        {
            editRevision++; checkedPages.Clear(); RefreshStepBar();
            var model = Model;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.DataBind, new Action(() => { if (ReferenceEquals(Model, model)) model?.RevalidateAfterEdit(); }));
        }
        control.LostKeyboardFocus += (_, _) => { if (Model is { } editor && !navigating) editor.ValidateFields(); };
        if (control is TextBox text) text.TextChanged += (_, _) => Edited();
        else if (control is PasswordBox password) password.PasswordChanged += (_, _) => Edited();
        else if (control is ComboBox choice) choice.SelectionChanged += (_, _) => Edited();
        else if (control is CheckBox toggle) { toggle.Checked += (_, _) => Edited(); toggle.Unchecked += (_, _) => Edited(); }
    }
    TextBox Input(Panel form, string label, object source, string property, string path, ObjectPage page, string? help = null, bool multiline = false)
    {
        var box = new TextBox
        {
            MinHeight = 40,
            MaxLength = 65536,
            AcceptsReturn = multiline,
            TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            FontSize = UiTypography.Body,
            VerticalContentAlignment = multiline ? VerticalAlignment.Top : VerticalAlignment.Center
        };
        box.SetBinding(TextBox.TextProperty, new Binding(property) { Source = source, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        box.MaxWidth = multiline || property is "Target" or "BarcodePattern" or "BackupDirectory" ? 760 :
            property is "QueueCapacity" or "TimeoutMs" or "MaximumOperations" or "ResidentHistory" or "MinimumFreeMiB" or "DiagnosticLogDays" or "HistoryDays" or "SessionLifetimeMs" or "BackupIntervalMinutes" or "BackupMaximumSets" or "Precision" or "ConfiguredWeight" or "MaximumAgeMs" or "WaitTimeoutMs" or "ImageBytes" or "TotalBytes" or "MaximumPending" ? 240 : 400;
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
        var row = new StackPanel();
        var caption = Text(label); caption.Margin = new(0, 0, 0, 4); row.Children.Add(caption);
        var box = new TextBox { Text = value, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MinHeight = 28, Padding = new(0), BorderThickness = new(0), Background = Brushes.Transparent };
        AutomationProperties.SetName(box, label); row.Children.Add(box); page.Body.Children.Add(Card(row));
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
        if (Model is not { } m) { pageHost.Content = Text("正在读取设置…"); return; }
        firstStepBar.Visibility = FirstConfiguration ? Visibility.Visible : Visibility.Collapsed;
        remainingSteps.Visibility = FirstConfiguration ? Visibility.Visible : Visibility.Collapsed;
        BuildSystem(m); BuildBusiness(m); BuildCapabilities(m);
        if (m.ApplicationId == "sportmaster") ArrangeSportmasterPages();
        foreach (var page in pages)
        {
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
        else if (m.ApplicationId == "sportmaster") directoryView.SortDescriptions.Add(new SortDescription(nameof(ObjectPage.TaskOrder), ListSortDirection.Ascending));
        directoryView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ObjectPage.GroupTitle)));
        directoryView.Filter = value => value is ObjectPage p && p.Available && (categoryScope is null || p.Category == categoryScope);
        var priorSelecting = selectingPage; selectingPage = true;
        try
        {
            directory.ItemsSource = directoryView;
            SelectPageCore(pages.FirstOrDefault(p => p.Available && p.Category == selected?.Category && p.Title == selected.Title && (categoryScope is null || p.Category == categoryScope)) ?? OrderedPages().FirstOrDefault(p => categoryScope is null || p.Category == categoryScope));
        }
        finally { selectingPage = priorSelecting; }
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
            var button = new Button { Content = new TextBlock { Text = context + "\n" + message + (found ? "  定位字段" : ""), TextWrapping = TextWrapping.Wrap }, Style = (Style)Resources["ParameterResultAction"], Margin = new(0, 2, 0, 2), MinHeight = 44 };
            button.SetResourceReference(ForegroundProperty, "Danger");
            if (found)
            {
                location!.Control.SetResourceReference(Control.BorderBrushProperty, "Danger"); location.Error.Text += (location.Error.Text.Length > 0 ? "\n" : "") + message; location.Error.Visibility = Visibility.Visible;
                location.Page.Diagnostic.Text += location.Label.TrimEnd(' ', '*') + "：" + message + "\n";
                button.Click += (_, _) => NavigateTo(issue.Path);
            }
            else if (reviewPage is not null) reviewPage.Diagnostic.Text += message + "\n";
            errors.Children.Add(found ? button : Text(context + "\n" + message));
        }
        RefreshStepBar();
        foreach (var page in pages)
        {
            var count = (Model?.Issues ?? []).Count(i => locations.TryGetValue(i.Path, out var entry) && ReferenceEquals(entry.Page, page));
            page.SetIssueCount(count);
            page.Summary.Text = page.Category == 4 ? errors.Children.Count == 0 ? "当前页没有错误。" : "有 " + errors.Children.Count + " 项需要修改" : "有 " + count + " 项需要修改";
            page.Summary.Visibility = count > 0 || page.Category == 4 && errors.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (page.Category != 4 && count > 0)
            {
                var first = (Model?.Issues ?? []).First(i => locations.TryGetValue(i.Path, out var entry) && ReferenceEquals(entry.Page, page));
                var label = locations[first.Path].Label.TrimEnd(' ', '*');
                var locate = new Hyperlink(new Run("前往修改：" + label)); locate.Click += (_, _) => NavigateTo(first.Path);
                page.Summary.Inlines.Add(new Run("    ")); page.Summary.Inlines.Add(locate);
            }
        }
    }
    string IssueMessage(ParameterIssue issue, Location? location)
    {
        var label = location?.Label.TrimEnd(' ', '*') ?? "此参数";
        if (issue.Message.EndsWith(": required", StringComparison.Ordinal)) return "请填写" + label + "。";
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
        return text.Any(c => c is >= '\u4e00' and <= '\u9fff') ? text : "设置检查未通过，请按标出的字段修正。";
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

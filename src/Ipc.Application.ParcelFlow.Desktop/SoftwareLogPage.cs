using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Ipc.Application.Contracts;
using Microsoft.Win32;

namespace Ipc.Application.ParcelFlow.Desktop;

public sealed class SoftwareLogControl : UserControl
{
    readonly IWorkstationAdministration service;
    readonly TextBox from = Input(DateTimeOffset.Now.AddDays(-1).ToString("O")), to = Input(DateTimeOffset.Now.AddMinutes(1).ToString("O")), keyword = Input(""), source = Input(""), context = Input("10");
    readonly CheckBox regex = new() { Content = "使用正则表达式（有执行时限）", MinHeight = 44 };
    readonly ComboBox level = new() { ItemsSource = new[] { "全部", "信息", "警告", "错误", "无法解析" }, SelectedIndex = 0, MinHeight = 44, MinWidth = 100 };
    readonly DataGrid results = new() { IsReadOnly = true, AutoGenerateColumns = false, SelectionMode = DataGridSelectionMode.Single, EnableRowVirtualization = true, EnableColumnVirtualization = true, MinHeight = 32 };
    readonly TextBox detail = new() { FontFamily = UiTypography.Code, FontSize = UiTypography.Log, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 32 };
    readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    readonly StackPanel filters = new();
    readonly TextBox effectiveRange = new() { IsReadOnly = true, BorderThickness = new(0), Padding = new(0), MinHeight = 0, Background = System.Windows.Media.Brushes.Transparent, FontSize = UiTypography.Label, MaxHeight = 44, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    readonly Expander advancedFilters;
    readonly Button searchButton, previousButton, nextButton, contextButton, exportButton, rangeButton, cancel, locateExport;
    string? exportedFile;
    readonly Expander detailPanel, diagnostics;
    readonly TextBox diagnosticText = new() { FontFamily = UiTypography.Code, FontSize = UiTypography.Log, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new(0) };
    readonly TextBlock empty = new() { Text = "填写条件后查询日志", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = System.Windows.Media.Brushes.SlateGray };
    readonly LogSummaryConverter summary = new();
    CancellationTokenSource? pending, formatting;
    long detailVersion, viewVersion;
    string? selection;
    SoftwareLogQuery? query;
    int offset;
    bool more, busy, compact;
    public SoftwareLogControl(IWorkstationAdministration service)
    {
        this.service = service;
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Ipc.Application.ParcelFlow.Desktop;component/QueryWorkspaceTheme.xaml", UriKind.Relative) });
        Background = System.Windows.Media.Brushes.White; FontFamily = UiTypography.Interface; FontSize = UiTypography.Body; FontWeight = FontWeights.Normal;
        var root = new DockPanel { Margin = new(20, 16, 20, 12) }; Content = root;
        var header = new DockPanel(); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var heading = new StackPanel(); DockPanel.SetDock(heading, Dock.Top); header.Children.Add(heading);
        var title = new TextBlock { Text = "日志查询", Style = (Style)FindResource("QueryHeading") }; heading.Children.Add(title);
        var hint = new TextBlock { Text = "默认最近24小时，每页200条；选择记录查看原文；超出呈现预算可完整导出。", Style = (Style)FindResource("QueryHint"), Margin = new(0, 0, 0, 10) }; title.ToolTip = hint.Text;
        var actions = new WrapPanel { Margin = new(0, 6, 0, 4) }; DockPanel.SetDock(actions, Dock.Bottom); header.Children.Add(actions);
        Button Action(string label, Func<Task> action)
        {
            var button = new Button { Content = label, Margin = new(0, 0, 8, 4), Style = (Style)FindResource("ParameterAction") };
            button.Click += async (_, _) => await Run(action); actions.Children.Add(button); return button;
        }
        searchButton = Action("查询", Search);
        searchButton.Style = (Style)FindResource("ParameterPrimaryAction");
        previousButton = Action("上一页", () => Load(Math.Max(0, offset - 200)));
        nextButton = Action("下一页", () => Load(offset + 200));
        cancel = new Button { Content = "取消当前任务", Margin = new(0, 0, 0, 4) }; cancel.Click += (_, _) => pending?.Cancel(); actions.Children.Add(cancel);
        var headerScroll = new ScrollViewer { Content = filters, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; header.Children.Add(headerScroll);
        var search = new WrapPanel(); filters.Children.Add(search);
        FrameworkElement Field(string label, FrameworkElement element, double width)
        {
            var group = new Grid { Margin = new(0, 0, 12, 8), Width = width };
            group.ColumnDefinitions.Add(new()); group.ColumnDefinitions.Add(new());
            group.RowDefinitions.Add(new() { Height = GridLength.Auto }); group.RowDefinitions.Add(new() { Height = GridLength.Auto });
            var caption = new TextBlock { Text = label, Style = (Style)FindResource("QueryHint") };
            group.Children.Add(caption); group.Children.Add(element);
            void ArrangeField()
            {
                var shortPage = ActualHeight < 500;
                group.ColumnDefinitions[0].Width = shortPage ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
                group.ColumnDefinitions[1].Width = shortPage ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
                Grid.SetRow(element, shortPage ? 0 : 1); Grid.SetColumn(element, shortPage ? 1 : 0);
                caption.Margin = shortPage ? new(0, 0, 8, 0) : new(0, 0, 0, 4);
                caption.VerticalAlignment = VerticalAlignment.Center;
            }
            SizeChanged += (_, _) => ArrangeField(); ArrangeField();
            System.Windows.Automation.AutomationProperties.SetName(element, "日志" + label); return group;
        }
        search.Children.Add(Field("关键词", keyword, 260)); search.Children.Add(Field("级别", level, 120));
        keyword.ToolTip = "普通关键词；高级筛选可启用正则表达式";
        keyword.KeyDown += async (_, e) => { if (e.Key == System.Windows.Input.Key.Enter && !busy) { e.Handled = true; await Run(Search); } };
        DateTimeInput Calendar(TextBox value)
        {
            var input = new DateTimeInput(); input.SetBinding(DateTimeInput.TimestampProperty, new Binding(nameof(TextBox.Text)) { Source = value, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }); return input;
        }
        var extra = new StackPanel(); var fields = new WrapPanel(); extra.Children.Add(fields);
        fields.Children.Add(Field("开始时间（含）", Calendar(from), 440)); fields.Children.Add(Field("结束时间（不含）", Calendar(to), 440)); fields.Children.Add(Field("来源", source, 160));
        var quick = new WrapPanel(); extra.Children.Add(quick);
        foreach (var label in new[] { "今天", "最近24小时", "最近7天" })
        {
            var button = new Button { Content = label, Margin = new(0, 0, 8, 8) };
            button.Click += (_, _) => { var now = DateTimeOffset.Now; from.Text = (label == "今天" ? new DateTimeOffset(DateTime.Today) : now.AddDays(label == "最近7天" ? -7 : -1)).ToString("O"); to.Text = (label == "今天" ? new DateTimeOffset(DateTime.Today).AddDays(1) : now).ToString("O"); };
            quick.Children.Add(button);
        }
        extra.Children.Add(regex); var contextFields = new WrapPanel(); contextFields.Children.Add(Field("前后上下文条数", context, 140)); extra.Children.Add(contextFields);
        advancedFilters = new Expander { Header = "时间、更多筛选、上下文与导出", Content = new ScrollViewer { Content = extra, MaxHeight = 110, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
        advancedFilters.HeaderTemplate = (DataTemplate)FindResource("QueryDisclosureHeader");
        DockPanel.SetDock(advancedFilters, Dock.Bottom); header.Children.Insert(header.Children.Count - 1, advancedFilters);
        DockPanel.SetDock(effectiveRange, Dock.Bottom); header.Children.Insert(header.Children.Count - 2, effectiveRange);
        System.Windows.Automation.AutomationProperties.SetName(effectiveRange, "日志当前查询范围");
        var extraActions = new WrapPanel(); extra.Children.Add(extraActions);
        contextButton = Action("查看原始上下文", LoadContext); exportButton = Action("导出全部匹配…", () => Export(false)); rangeButton = Action("导出时间范围…", () => Export(true));
        foreach (var button in new[] { contextButton, exportButton, rangeButton }) { actions.Children.Remove(button); extraActions.Children.Add(button); }
        locateExport = Action("定位导出文件", () =>
        {
            if (exportedFile is not null) { var start = new System.Diagnostics.ProcessStartInfo("explorer.exe") { UseShellExecute = true }; start.ArgumentList.Add("/select," + exportedFile); System.Diagnostics.Process.Start(start); }
            return Task.CompletedTask;
        });
        actions.Children.Remove(locateExport); extraActions.Children.Add(locateExport);
        var footer = new StackPanel { Margin = new(0, 6, 0, 0) }; DockPanel.SetDock(footer, Dock.Bottom); root.Children.Insert(0, footer);
        status.Style = (Style)FindResource("QueryHint"); status.MaxHeight = 48; status.TextTrimming = TextTrimming.CharacterEllipsis; status.SetBinding(ToolTipProperty, new Binding(nameof(TextBlock.Text)) { Source = status }); footer.Children.Add(status);
        System.Windows.Automation.AutomationProperties.SetLiveSetting(status, System.Windows.Automation.AutomationLiveSetting.Polite);
        diagnostics = new Expander { Header = "诊断详情", Content = new ScrollViewer { Content = diagnosticText, MaxHeight = 90, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, Visibility = Visibility.Collapsed }; footer.Children.Add(diagnostics);
        detailPanel = new Expander { Header = "完整日志与上下文", Content = detail, Visibility = Visibility.Collapsed, Margin = new(0, 8, 0, 0) }; DockPanel.SetDock(detailPanel, Dock.Bottom); root.Children.Add(detailPanel);
        detailPanel.HeaderTemplate = (DataTemplate)FindResource("QueryDisclosureHeader");
        results.Style = (Style)FindResource("QueryResultsGrid");
        var cellStyle = (Style)FindResource("QueryCellText");
        results.Columns.Add(new DataGridTextColumn { Header = "时间（本地）", Binding = new Binding("At") { Converter = new LocalTimestampConverter(), StringFormat = "yyyy-MM-dd HH:mm:ss.fff" }, Width = 238, ElementStyle = cellStyle });
        results.Columns.Add(new DataGridTextColumn { Header = "来源", Binding = new Binding("Source"), Width = 130, ElementStyle = cellStyle });
        results.Columns.Add(new DataGridTextColumn { Header = "级别", Binding = new Binding("Level"), Width = 82, ElementStyle = cellStyle });
        results.Columns.Add(new DataGridTextColumn { Header = "日志内容", Binding = new Binding("Text") { Converter = summary }, Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 80, ElementStyle = (Style)FindResource("QueryCodeText") });
        var panel = new Grid(); panel.Children.Add(results); panel.Children.Add(empty); root.Children.Add(panel);
        results.SizeChanged += (_, _) => UpdateEmpty();
        SizeChanged += (_, _) =>
        {
            var shortPage = ActualHeight < 500;
            if (shortPage && !compact) detailPanel.IsExpanded = false;
            compact = shortPage; root.Margin = compact ? new(12, 8, 12, 8) : new(20, 16, 20, 12);
            results.RowHeight = 40; results.ColumnHeaderHeight = 36;
            title.FontSize = UiTypography.Group;
            UpdateDetailLayout();
            headerScroll.MaxHeight = compact ? 52 : Math.Min(260, ActualHeight * .42);
            footer.Margin = compact ? new(0) : new(0, 6, 0, 0);
            detailPanel.Margin = compact ? new(0, 4, 0, 0) : new(0, 8, 0, 0);
            detail.MaxHeight = compact ? 60 : ActualHeight * .30; status.MaxHeight = compact ? 24 : 48;
        };
        // At the minimum viewport, the selected original is the reading task.
        // Keep actions/range and a full result row; collapse only the keyword strip,
        // preserving its controls and draft. Closing context restores it immediately.
        void UpdateDetailLayout() => headerScroll.Visibility = compact && detailPanel.IsExpanded ? Visibility.Collapsed : Visibility.Visible;
        detailPanel.Expanded += (_, _) => { if (compact) advancedFilters.IsExpanded = false; UpdateDetailLayout(); };
        detailPanel.Collapsed += (_, _) => UpdateDetailLayout();
        advancedFilters.Expanded += (_, _) => { if (compact) detailPanel.IsExpanded = false; };
        results.SelectionChanged += async (_, _) =>
        {
            if (results.SelectedItem is SoftwareLogEntry entry) await ShowOriginal(entry);
            else ClearDetail();
            UpdateActions();
        };
        results.MouseDoubleClick += async (_, _) => { if (results.SelectedItem is SoftwareLogEntry) await Run(LoadContext); };
        results.KeyDown += async (_, e) => { if (e.Key == System.Windows.Input.Key.Enter && results.SelectedItem is SoftwareLogEntry && !busy) { e.Handled = true; await Run(LoadContext); } };
        foreach (var box in new[] { from, to, keyword, source }) box.TextChanged += (_, _) => InvalidateSelection();
        regex.Checked += (_, _) => InvalidateSelection(); regex.Unchecked += (_, _) => InvalidateSelection(); level.SelectionChanged += (_, _) => InvalidateSelection();
        Unloaded += (_, _) => Revoke(); status.Text = "尚未查询。"; UpdateActions();
    }
    void UpdateActions()
    {
        filters.IsEnabled = !busy; advancedFilters.IsEnabled = !busy; searchButton.IsEnabled = !busy;
        effectiveRange.Text = selection is not null && query is not null
            ? "已应用范围 " + query.From.ToString("yyyy-MM-dd HH:mm:ss zzz") + " 至 " + query.ToExclusive.ToString("yyyy-MM-dd HH:mm:ss zzz") + "（不含）" +
                " · " + (string.IsNullOrEmpty(query.Level) ? "全部级别" : query.Level) + " · " + (string.IsNullOrEmpty(query.Source) ? "全部来源" : query.Source) +
                (string.IsNullOrEmpty(query.Keyword) ? "" : " · " + (query.Regex ? "正则：" : "关键词：") + query.Keyword)
            : "待查询（本地） " + from.Text + " 至 " + to.Text + "（不含）；尚无有效查询快照";
        previousButton.IsEnabled = !busy && selection is not null && offset > 0; nextButton.IsEnabled = !busy && selection is not null && more;
        contextButton.IsEnabled = !busy && selection is not null && results.SelectedItem is SoftwareLogEntry;
        exportButton.IsEnabled = rangeButton.IsEnabled = !busy && selection is not null;
        locateExport.IsEnabled = !busy && exportedFile is not null;
        cancel.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        UpdateEmpty();
    }
    // At minimum height the status line owns the empty message; never paint it over column headers.
    void UpdateEmpty() => empty.Visibility = results.Items.Count == 0 && results.ActualHeight >= 96 ? Visibility.Visible : Visibility.Collapsed;
    void InvalidateSelection()
    {
        if (selection is { } original && service is ISoftwareLogSelectionLifetime lifetime) lifetime.ReleaseLogSelection(original);
        viewVersion++; pending?.Cancel(); summary.Values = new Dictionary<string, string>(); selection = null; more = false; offset = 0; results.ItemsSource = null; ClearDetail();
        status.Text = "筛选已改变，请重新查询。"; empty.Text = "填写条件后查询日志"; UpdateActions();
    }
    void ClearDetail()
    {
        // Visibility alone does not raise Collapsed; restore compact filters as
        // well as clearing the previous record when its selection is revoked.
        detailVersion++; formatting?.Cancel();
        detailPanel.IsExpanded = false;
        detail.Clear(); detailPanel.Visibility = Visibility.Collapsed;
    }
    public void Revoke()
    {
        InvalidateSelection(); status.Text = "查询已关闭。进入本页后请重新查询。";
        exportedFile = null; query = null; UpdateActions();
        diagnosticText.Clear(); diagnostics.IsExpanded = false; diagnostics.Visibility = Visibility.Collapsed;
    }
    static TextBox Input(string text) => new() { Text = text, MinHeight = 44, VerticalContentAlignment = VerticalAlignment.Center };
    async Task Search()
    {
        var candidate = ReadQuery();
        if (selection is { } original && service is ISoftwareLogSelectionLifetime lifetime) lifetime.ReleaseLogSelection(original);
        query = candidate; selection = null; more = false; offset = 0;
        results.ItemsSource = null; ClearDetail(); UpdateActions();
        await Load(0);
    }
    SoftwareLogQuery ReadQuery()
    {
        static bool Parse(string value, out DateTimeOffset at) => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out at) && System.Text.RegularExpressions.Regex.IsMatch(value, @"(Z|[+-]\d{2}:\d{2})$");
        if (!Parse(from.Text, out var start) || !Parse(to.Text, out var end) || start >= end) throw new InvalidOperationException("请选择有效的开始、结束和UTC偏移；结束不含且须晚于开始。");
        return new(start, end, keyword.Text, regex.IsChecked == true, level.SelectedIndex == 0 ? "" : level.Text, source.Text);
    }
    int ContextSize() => int.TryParse(context.Text, out var value) && value is >= 0 and <= 200 ? value : throw new InvalidOperationException("上下文条数须为0～200。");
    async Task Run(Func<Task> action)
    {
        if (busy) return;
        var version = viewVersion;
        var owner = new CancellationTokenSource(); pending = owner; busy = true; diagnosticText.Clear(); diagnostics.Visibility = Visibility.Collapsed; diagnostics.IsExpanded = false;
        status.Text = "正在处理…"; empty.Text = "正在查询…"; UpdateActions();
        try
        {
            if (!(await service.ManagementModeStatusAsync()).Authorized) throw new UnauthorizedAccessException("请重新进入管理模式。");
            pending.Token.ThrowIfCancellationRequested(); await action();
        }
        catch (OperationCanceledException) when (version != viewVersion) { }
        catch (Exception) when (version != viewVersion) { /* A revoked page cannot publish an old failure. */ }
        catch (OperationCanceledException) { status.Text = "任务已取消或超时；请核对结果后再操作。"; }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException) { status.Text = "正则执行超时，请缩短表达式或改用普通关键词。"; }
        catch (Exception error)
        {
            status.Text = error is UnauthorizedAccessException ? "管理权限已失效，请重新进入管理模式。"
                : error.Message is "请选择有效的开始、结束和UTC偏移；结束不含且须晚于开始。" or "上下文条数须为0～200。" ? error.Message
                : "操作未完成，请核对筛选条件或查看诊断详情。";
            diagnosticText.Text = Ipc.Platform.DiagnosticText.Protect(error.ToString()); diagnostics.Visibility = Visibility.Visible;
        }
        finally { if (ReferenceEquals(pending, owner)) pending = null; owner.Dispose(); busy = false; empty.Text = selection is null ? "尚未取得查询结果" : "当前范围内没有匹配日志"; UpdateActions(); }
    }
    async Task Load(int requestedOffset)
    {
        var page = await service.QueryLogsAsync(query ?? throw new InvalidOperationException("请先查询。"), selection, requestedOffset, pending!.Token);
        var token = pending.Token;
        var summaries = await Task.Run(() =>
        {
            var values = new Dictionary<string, string>();
            foreach (var entry in page.Entries) { token.ThrowIfCancellationRequested(); values.TryAdd(entry.Text, SoftwareLogText.Summary(entry.Text)); }
            return values;
        }, token);
        if (!(await service.ManagementModeStatusAsync()).Authorized) throw new UnauthorizedAccessException();
        token.ThrowIfCancellationRequested(); selection = page.Selection; offset = requestedOffset; more = page.HasMore;
        summary.Values = summaries; results.SelectedItem = null; results.ItemsSource = page.Entries; ClearDetail();
        status.Text = "第" + (offset / 200 + 1) + "页 · " + page.Entries.Length + "条" + (more ? " · 可继续下一页" : " · 已到末页") + "\n" + string.Join("\n", page.Notices);
    }
    async Task ShowOriginal(SoftwareLogEntry entry)
    {
        var version = ++detailVersion; formatting?.Cancel();
        var owner = new CancellationTokenSource(); formatting = owner; var token = owner.Token;
        detail.Text = "正在整理原文…"; detailPanel.Visibility = Visibility.Visible;
        try
        {
            var text = await Task.Run(() => SoftwareLogText.Format([entry], token), token);
            var authorized = (await service.ManagementModeStatusAsync()).Authorized;
            token.ThrowIfCancellationRequested();
            if (version != detailVersion || !ReferenceEquals(results.SelectedItem, entry)) return;
            if (!authorized) { Revoke(); return; }
            detail.Text = text;
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (version == detailVersion) detail.Text = "原文整理未完成，请重新选择记录或导出。\n" + Ipc.Platform.DiagnosticText.Protect(error.Message);
        }
        finally { if (ReferenceEquals(formatting, owner)) formatting = null; owner.Dispose(); }
    }
    async Task LoadContext()
    {
        if (selection is null || results.SelectedItem is not SoftwareLogEntry entry) throw new InvalidOperationException("请先选择一条命中日志。");
        formatting?.Cancel(); var version = ++detailVersion; var token = pending!.Token;
        var size = ContextSize(); var result = await service.LogContextAsync(selection, entry.Location, size, size, token);
        var text = await Task.Run(() => SoftwareLogText.Format(result.Entries, token), token);
        if (!(await service.ManagementModeStatusAsync()).Authorized) throw new UnauthorizedAccessException();
        token.ThrowIfCancellationRequested();
        if (version != detailVersion || !ReferenceEquals(results.SelectedItem, entry)) return;
        detail.Text = text; detailPanel.IsExpanded = true; status.Text = string.Join("\n", result.Notices);
    }
    static string OriginalRecord(SoftwareLogEntry entry) => SoftwareLogText.Original(entry, CancellationToken.None);
    async Task Export(bool range)
    {
        if (selection is null) throw new InvalidOperationException("请先查询以固定本次日志范围。");
        var picker = new SaveFileDialog { FileName = "software-logs-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".jsonl", Filter = "软件日志|*.jsonl" };
        if (picker.ShowDialog(Window.GetWindow(this)) != true) return;
        var originalSelection = selection;
        if (MessageBox.Show(Window.GetWindow(this), (range ? "导出固定时间范围内全部原始记录，包括不匹配关键词/级别/来源的记录。" : "导出固定选择中全部匹配记录及所选相邻上下文。") + "\n" + effectiveRange.Text + "\n目标：" + picker.FileName, "核对日志导出范围", MessageBoxButton.OKCancel, MessageBoxImage.Information, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
        var count = await service.ExportLogsAsync(originalSelection, picker.FileName, range, ContextSize(), pending!.Token); pending.Token.ThrowIfCancellationRequested();
        if (!(await service.ManagementModeStatusAsync()).Authorized) throw new UnauthorizedAccessException();
        exportedFile = picker.FileName; status.Text = "已完整导出" + count + "条原始记录：" + exportedFile + "。可定位文件；已有文件未覆盖。";
    }
}

// The table is a readable projection; original JSON and multiline values remain in detail/export.
sealed class LogSummaryConverter : IValueConverter
{
    public IReadOnlyDictionary<string, string> Values { get; set; } = new Dictionary<string, string>();
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is string text && Values.TryGetValue(text, out var summary) ? summary : "原文摘要尚未取得";
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

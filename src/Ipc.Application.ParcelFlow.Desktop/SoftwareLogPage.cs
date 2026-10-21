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
    readonly TextBox from = Input(DateTime.Now.AddDays(-1).ToString("yyyy-MM-dd HH:mm")), to = Input(DateTime.Now.AddMinutes(1).ToString("yyyy-MM-dd HH:mm")), keyword = Input(""), source = Input(""), context = Input("10");
    readonly CheckBox regex = new() { Content = "使用正则表达式（有执行时限）", MinHeight = 44 };
    readonly ComboBox level = new() { ItemsSource = new[] { "全部", "信息", "警告", "错误", "无法解析" }, SelectedIndex = 0, MinHeight = 44, MinWidth = 100 };
    readonly DataGrid results = new() { IsReadOnly = true, AutoGenerateColumns = false, SelectionMode = DataGridSelectionMode.Single, EnableRowVirtualization = true, EnableColumnVirtualization = true, MinHeight = 32 };
    readonly TextBox detail = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 32 };
    readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    readonly WrapPanel controls = new();
    CancellationTokenSource? pending;
    string? selection;
    SoftwareLogQuery? query;
    int offset;
    bool more, busy;
    public SoftwareLogControl(IWorkstationAdministration service)
    {
        this.service = service; var root = new DockPanel { Margin = new(20, 12, 20, 12) }; Content = root;
        var header = new StackPanel();
        var headerScroll = new ScrollViewer { Content = header, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        DockPanel.SetDock(headerScroll, Dock.Top); root.Children.Add(headerScroll);
        SizeChanged += (_, _) => headerScroll.MaxHeight = Math.Max(100, Math.Min(240, ActualHeight * .48));
        var search = new WrapPanel(); header.Children.Add(search);
        keyword.Width = 250; keyword.Margin = new(0, 8, 12, 4); keyword.ToolTip = "普通关键词；默认查询最近24小时"; System.Windows.Automation.AutomationProperties.SetName(keyword, "日志关键词"); search.Children.Add(keyword); search.Children.Add(controls);
        var fields = new WrapPanel();
        void Field(string label, FrameworkElement element, double width = 200)
        { var group = new StackPanel { Margin = new(0, 10, 12, 8), Width = width }; group.Children.Add(new TextBlock { Text = label }); group.Children.Add(element); fields.Children.Add(group); }
        Field("开始时间（本地）", from); Field("结束时间（不含）", to);
        var advanced = new WrapPanel();
        advanced.Children.Add(regex); advanced.Children.Add(new TextBlock { Text = "级别", VerticalAlignment = VerticalAlignment.Center, Margin = new(12, 0, 8, 0) }); advanced.Children.Add(level);
        advanced.Children.Add(new TextBlock { Text = "来源", VerticalAlignment = VerticalAlignment.Center, Margin = new(12, 0, 8, 0) }); source.Width = 130; advanced.Children.Add(source);
        advanced.Children.Add(new TextBlock { Text = "前后上下文条数", VerticalAlignment = VerticalAlignment.Center, Margin = new(12, 0, 8, 0) }); context.Width = 60; advanced.Children.Add(context);
        var extra = new StackPanel(); extra.Children.Add(fields); extra.Children.Add(advanced); var extraActions = new WrapPanel(); extra.Children.Add(extraActions);
        header.Children.Add(new Expander { Header = "时间、更多筛选、上下文与导出", Content = extra });
        var cancel = new Button { Content = "取消当前任务", MinHeight = 44, Margin = new(0, 4, 0, 4) }; cancel.Click += (_, _) => pending?.Cancel(); var footer = new DockPanel { Margin = new(0, 4, 0, 0) }; DockPanel.SetDock(cancel, Dock.Left); footer.Children.Add(cancel);
        status.Margin = new(12, 0, 0, 0); status.MaxHeight = 42; status.TextTrimming = TextTrimming.CharacterEllipsis; footer.Children.Add(status); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Insert(0, footer);
        status.SetBinding(ToolTipProperty, new Binding(nameof(TextBlock.Text)) { Source = status });
        Button Action(string label, Func<Task> action)
        { var b = new Button { Content = label, MinHeight = 44, Margin = new(0, 8, 8, 4) }; b.Click += async (_, _) => await Run(action); controls.Children.Add(b); return b; }
        Action("查询", async () => { query = ReadQuery(); selection = null; offset = 0; await Load(); });
        Action("上一页", async () => { if (selection is null || offset == 0) return; offset = Math.Max(0, offset - 200); await Load(); });
        Action("下一页", async () => { if (selection is null || !more) return; offset += 200; await Load(); });
        foreach (var button in new[] { Action("查看原始上下文", LoadContext), Action("导出全部匹配…", () => Export(false)), Action("导出时间范围…", () => Export(true)) })
        { controls.Children.Remove(button); extraActions.Children.Add(button); }
        results.Columns.Add(new DataGridTextColumn { Header = "时间", Binding = new Binding("At") { StringFormat = "yyyy-MM-dd HH:mm:ss.fff zzz" }, Width = 220 });
        results.Columns.Add(new DataGridTextColumn { Header = "来源", Binding = new Binding("Source"), Width = 100 });
        results.Columns.Add(new DataGridTextColumn { Header = "级别", Binding = new Binding("Level"), Width = 70 });
        results.Columns.Add(new DataGridTextColumn { Header = "原日志记录", Binding = new Binding("Text"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        var panels = new Grid(); panels.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) }); panels.RowDefinitions.Add(new() { Height = GridLength.Auto }); panels.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) }); panels.Children.Add(results);
        var splitter = new GridSplitter { Height = 6, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Center }; Grid.SetRow(splitter, 1); panels.Children.Add(splitter); Grid.SetRow(detail, 2); panels.Children.Add(detail); root.Children.Add(panels);
        results.MouseDoubleClick += async (_, _) => await Run(LoadContext);
        foreach (var box in new[] { from, to, keyword, source }) box.TextChanged += (_, _) => InvalidateSelection();
        regex.Checked += (_, _) => InvalidateSelection(); regex.Unchecked += (_, _) => InvalidateSelection(); level.SelectionChanged += (_, _) => InvalidateSelection();
        Unloaded += (_, _) => Revoke(); status.Text = "默认查询最近24小时。每页200条；双击查看上下文。展开可修改时间、筛选及导出全部。";
    }
    void InvalidateSelection() { pending?.Cancel(); selection = null; results.ItemsSource = null; detail.Clear(); }
    public void Revoke() { pending?.Cancel(); selection = null; results.ItemsSource = null; detail.Clear(); status.Text = "查询已关闭；重新登录管理员后查询。"; }
    static TextBox Input(string text) => new() { Text = text, MinHeight = 44, VerticalContentAlignment = VerticalAlignment.Center };
    SoftwareLogQuery ReadQuery()
    {
        if (!DateTimeOffset.TryParse(from.Text, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out var start) || !DateTimeOffset.TryParse(to.Text, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out var end)) throw new InvalidOperationException("请按 年-月-日 时:分 填写本地时间。");
        return new(start, end, keyword.Text, regex.IsChecked == true, level.SelectedIndex == 0 ? "" : level.Text, source.Text);
    }
    int ContextSize() => int.TryParse(context.Text, out var value) && value is >= 0 and <= 200 ? value : throw new InvalidOperationException("上下文条数须为0～200。");
    async Task Run(Func<Task> action)
    {
        if (busy) return; pending?.Dispose(); pending = new(); busy = true; controls.IsEnabled = false;
        try { if (!(await service.AdministratorStatusAsync()).Authorized) throw new UnauthorizedAccessException("请重新登录管理员。"); await action(); }
        catch (OperationCanceledException) { status.Text = "任务已取消或超时；未提交导出文件。"; }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException) { status.Text = "正则执行超时，请缩短表达式或改用普通关键词。"; }
        catch (Exception error) { status.Text = error.Message; }
        finally { busy = false; controls.IsEnabled = true; }
    }
    async Task Load()
    {
        var page = await service.QueryLogsAsync(query ?? throw new InvalidOperationException("请先查询。"), selection, offset, pending!.Token);
        pending.Token.ThrowIfCancellationRequested(); selection = page.Selection; more = page.HasMore; results.ItemsSource = page.Entries; detail.Clear();
        status.Text = "第" + (offset / 200 + 1) + "页 · " + page.Entries.Length + "条" + (more ? " · 可继续下一页" : " · 已到末页") + "\n" + string.Join("\n", page.Notices);
    }
    async Task LoadContext()
    {
        if (selection is null || results.SelectedItem is not SoftwareLogEntry entry) throw new InvalidOperationException("请先选择一条命中日志。");
        var size = ContextSize(); var result = await service.LogContextAsync(selection, entry.Location, size, size, pending!.Token); pending.Token.ThrowIfCancellationRequested();
        detail.Text = string.Join("\n\n", result.Entries.Select(e => "[" + e.Location.File + ":" + e.Location.Line + "]\n" + e.Text.Replace("\\r\\n", "\r\n").Replace("\\n", "\n"))); status.Text = string.Join("\n", result.Notices);
    }
    async Task Export(bool range)
    {
        if (selection is null) throw new InvalidOperationException("请先查询以固定本次日志范围。");
        var picker = new SaveFileDialog { FileName = "software-logs-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".jsonl", Filter = "软件日志|*.jsonl" };
        if (picker.ShowDialog(Window.GetWindow(this)) != true) return;
        var count = await service.ExportLogsAsync(selection, picker.FileName, range, ContextSize(), pending!.Token); status.Text = "已导出" + count + "条原始记录（含所选上下文）。";
    }
}

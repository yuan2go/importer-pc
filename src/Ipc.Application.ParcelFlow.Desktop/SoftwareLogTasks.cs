using System.Windows;
using System.Windows.Controls;
using Ipc.Application.Contracts;
namespace Ipc.Application.ParcelFlow.Desktop;

public sealed partial class SoftwareLogControl
{
    readonly HashSet<string> selectedLevels = [], selectedSources = [];
    // Key events (errors, warnings, camera, RCS, settings, storage) are the default view; "全部日志" shows everything.
    readonly CheckBox keyEvents = new() { Content = "关键事件", MinHeight = 44, IsChecked = true };
    readonly Button exportMenu = new() { Content = "导出…", MinHeight = 44 };
    string? operationFilter;
    long operationNavigation;
    int? relativeHours = 1;
    bool updatingRelativeRange;
    void RefreshRelativeRange()
    {
        if (relativeHours is not { } hours) return;
        var now = DateTimeInput.SecondNow();
        updatingRelativeRange = true;
        try
        {
            from.Text = (hours == 0 ? new DateTimeOffset(DateTime.Today) : now.AddHours(-hours)).ToString("O");
            to.Text = now.AddSeconds(1).ToString("O");
        }
        finally { updatingRelativeRange = false; }
    }
    public event EventHandler<string>? OperationRequested;
    public async Task ShowOperationAsync(string operation)
    {
        if (!Guid.TryParseExact(operation, "N", out _)) throw new ArgumentException("作业身份无效");
        var navigation = ++operationNavigation; pending?.Cancel();
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        while (busy)
        {
            if (System.Diagnostics.Stopwatch.GetElapsedTime(started) > TimeSpan.FromSeconds(5)) throw new TimeoutException("原日志查询尚未退出，请稍后再打开此作业日志。");
            await Task.Delay(20);
            if (navigation != operationNavigation || diagnosticsClosed) throw new OperationCanceledException("日志导航已改变");
        }
        relativeHours = null;
        operationFilter = operation; keyword.Clear(); keyEvents.IsChecked = false;
        selectedLevels.Clear(); selectedSources.Clear(); level.SelectedIndex = 0; source.Clear();
        from.Text = DateTimeInput.SecondNow().AddDays(-7).ToString("O"); to.Text = DateTimeInput.SecondNow().AddMinutes(1).ToString("O");
        await Run(Search);
    }
    void InitializeTaskFilters(WrapPanel search, WrapPanel actions, Expander timeFilters, WrapPanel extraActions)
    {
        timeFilters.IsExpanded = false;
        if (timeFilters.Content is Panel originalTime && originalTime.Children.OfType<WrapPanel>().FirstOrDefault(p => p.Children.OfType<Button>().Any()) is { } duplicatePresets) originalTime.Children.Remove(duplicatePresets);
        if (keyword.Parent is Panel keywordParent) keywordParent.Children.Remove(keyword);
        if (level.Parent is FrameworkElement levelGroup && levelGroup.Parent is Panel levelParent)
        {
            levelParent.Children.Remove(levelGroup);
            if (advancedFilters.Content is Panel advanced) advanced.Children.Insert(0, levelGroup);
        }
        search.Children.Clear(); keyword.Width = 240; keyword.Margin = new(0, 0, 8, 4); search.Children.Add(keyword);
        if (timeFilters.Parent is Panel timeParent) timeParent.Children.Remove(timeFilters);
        if (advancedFilters.Content is Panel extra) extra.Children.Insert(0, timeFilters);
        advancedFilters.Header = "更多筛选";
        search.Children.Add(keyEvents); keyEvents.Checked += (_, _) => InvalidateSelection(); keyEvents.Unchecked += (_, _) => InvalidateSelection();
        foreach (var (name, hours) in new[] { ("最近1小时", 1), ("今天", 0), ("最近24小时", 24) })
        {
            var preset = new Button { Content = name, MinHeight = 44, Margin = new(4, 0, 4, 4) };
            preset.Click += (_, _) => { relativeHours = hours; RefreshRelativeRange(); InvalidateSelection(); };
            search.Children.Add(preset);
        }
        var multi = new WrapPanel(); search.Children.Add(multi);
        void Select(string label, string[] values, HashSet<string> selected)
        {
            var button = new Button { Content = label, MinHeight = 44, Margin = new(4, 0, 4, 4) }; var menu = new ContextMenu(); button.ContextMenu = menu;
            foreach (var value in values)
            {
                var item = new MenuItem { Header = value, IsCheckable = true, StaysOpenOnClick = true };
                item.Click += (_, _) => { if (item.IsChecked) selected.Add(value); else selected.Remove(value); button.Content = label + (selected.Count == 0 ? "：全部" : "：" + selected.Count); InvalidateSelection(); }; menu.Items.Add(item);
            }
            button.Click += (_, _) => { foreach (var item in menu.Items.OfType<MenuItem>()) item.IsChecked = selected.Contains((string)item.Header); menu.PlacementTarget = button; menu.IsOpen = true; }; multi.Children.Add(button);
        }
        Select("级别多选", ["信息", "警告", "错误", "调试", "无法解析"], selectedLevels);
        Select("来源多选", ["RCS请求", "RCS结果", "相机识码", "相机通信", "扫码与准入", "参数", "管理模式", "存储", "诊断日志"], selectedSources);
        var all = new Button { Content = "全部日志", MinHeight = 44 }; all.Click += (_, _) => { operationFilter = null; keyEvents.IsChecked = false; selectedLevels.Clear(); selectedSources.Clear(); level.SelectedIndex = 0; source.Clear(); InvalidateSelection(); }; multi.Children.Add(all);
        extraActions.Children.Remove(exportButton); extraActions.Children.Remove(rangeButton); exportButton.Visibility = rangeButton.Visibility = Visibility.Collapsed;
        var exports = new ContextMenu();
        foreach (var (label, allInRange) in new[] { ("导出筛选结果…", false), ("导出该时段全部日志…", true) })
        { var item = new MenuItem { Header = label }; item.Click += async (_, _) => await Run(() => Export(allInRange)); exports.Items.Add(item); }
        exportMenu.Click += (_, _) => { exports.PlacementTarget = exportMenu; exports.IsOpen = true; }; actions.Children.Add(exportMenu);
        var open = new Button { Content = "打开该作业记录", MinHeight = 44, IsEnabled = false };
        open.Click += (_, _) => { if (results.SelectedItem is SoftwareLogEntry { OperationId: { } id }) OperationRequested?.Invoke(this, id); };
        results.SelectionChanged += (_, _) => open.IsEnabled = results.SelectedItem is SoftwareLogEntry { OperationId: not null } && OperationRequested is not null;
        actions.Children.Add(open);
    }
}

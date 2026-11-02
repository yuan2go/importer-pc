using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Platform;
using Microsoft.Win32;
namespace Ipc.Application.Sportmaster.Desktop;

internal sealed class StationStatisticsControl : UserControl
{
    readonly SupplyStationViewModel vm;
    readonly IStationOperations? policyApi;
    readonly IStationSessionStatistics? api;
    readonly ComboBox range = new() { ItemsSource = new[] { "本班", "上一班", "今天", "近7天" }, SelectedIndex = 0, MinWidth = 140, MinHeight = 44 };
    readonly TextBlock feedback = Text("正在读取统计"), scope = Text(""), notices = Text(""), reasons = Text(""), policyState = Text("");
    readonly TextBlock[] values = Enumerable.Range(0, 7).Select(_ => Text("—")).ToArray();
    readonly DataGrid grid = new() { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, EnableRowVirtualization = true, EnableColumnVirtualization = true, Height = 260, MinHeight = 120 };
    readonly StationHourlyChart chart = new();
    readonly ComboBox timezone = new() { ItemsSource = TimeZoneInfo.GetSystemTimeZones().Select(z => z.Id).ToArray(), MinWidth = 240, MinHeight = 44 };
    readonly CheckBox automatic = new() { Content = "空闲时自动清理过期记录", MinHeight = 44 };
    readonly TextBox cleanupHour = new() { MinHeight = 40 }, backupLimit = new() { MinHeight = 40 }, target = new() { MinHeight = 40 };
    readonly Expander editor;
    readonly Button refresh, save, export, applyTarget;
    CancellationTokenSource? lifetime;
    bool busy, loadedPolicy;
    long generation, policyRevision;
    long busyGeneration;
    string savedPolicySignature = "", savedTarget = "";
    StationOperationsPolicy? policy;
    StationSessionReport? last;
    string PolicySignature() => Ipc.Platform.Wire.Encode(new { TimeZone = timezone.SelectedItem, Automatic = automatic.IsChecked, Hour = cleanupHour.Text, Sets = backupLimit.Text });
    public bool HasUnsavedChanges => loadedPolicy && (PolicySignature() != savedPolicySignature || target.Text != savedTarget);
    public StationStatisticsControl(SupplyStationViewModel vm)
    {
        this.vm = vm; policyApi = vm.Application as IStationOperations; api = vm.Application as IStationSessionStatistics;
        FontFamily = UiTypography.Interface; FontSize = UiTypography.Body;
        var body = new StackPanel { Margin = new(24, 20, 24, 16) };
        Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        body.Children.Add(new TextBlock { Text = "统计与班次", FontSize = UiTypography.PageTitle, FontWeight = UiTypography.Emphasis });
        body.Children.Add(Text("打开供包台开始一班，完全关闭后结束；页面切换和软件暂停不换班。软件处理次数不等于实物落格件数。"));
        var actions = new WrapPanel { Margin = new(0, 8, 0, 8) }; body.Children.Add(actions); actions.Children.Add(range);
        refresh = Button("刷新统计", Load); export = Button("导出当前快照CSV…", Export); actions.Children.Add(refresh); actions.Children.Add(export);
        actions.Children.Add(Button("取消读取或导出", () => { lifetime?.Cancel(); return Task.CompletedTask; }, false));
        body.Children.Add(scope); body.Children.Add(feedback);
        var metrics = new WrapPanel(); body.Children.Add(metrics);
        var labels = new[] { "识别次数", "请求次数", "取得格口", "拒绝次数", "格口未确认", "取得格口比例", "平均取得格口/小时" };
        for (var i = 0; i < values.Length; i++)
        {
            var box = new StackPanel { MinWidth = 128, MinHeight = 100, Margin = new(0, 8, 12, 12) }; var label = Text(labels[i]); box.Children.Add(label); values[i].FontSize = 24; values[i].FontWeight = UiTypography.Emphasis; box.Children.Add(values[i]); metrics.Children.Add(box);
            AutomationProperties.SetLabeledBy(values[i], label);
            values[i].SetBinding(AutomationProperties.NameProperty, new System.Windows.Data.Binding(nameof(TextBlock.Text)) { Source = values[i], StringFormat = labels[i] + "：{0}" });
        }
        metrics.SizeChanged += (_, _) => { var columns = metrics.ActualWidth >= 980 ? 7 : metrics.ActualWidth >= 600 ? 4 : 2; foreach (FrameworkElement child in metrics.Children) child.Width = Math.Max(128, metrics.ActualWidth / columns - 12); };
        body.Children.Add(Text("每小时取得格口 · 左位蓝色 / 右位绿色 · 虚线为本机目标（次/小时），按两位合计参考"));
        body.Children.Add(chart);
        body.Children.Add(Text("异常原因（识别/准入观察与 RCS 请求分开计数）")); body.Children.Add(reasons); body.Children.Add(notices);
        body.Children.Add(Text("小时明细（含时区偏移，可用键盘选择和复制）"));
        foreach (var (label, path) in new[] { ("时间", "Time"), ("投递位", "Position"), ("识别次数", "Recognitions"), ("请求次数", "Requests"), ("取得格口", "Assigned"), ("拒绝", "Rejected"), ("格口未确认", "Unknown"), ("人工请求", "ManualRequests") })
            grid.Columns.Add(new DataGridTextColumn { Header = label, Binding = new Binding(path), Width = DataGridLength.Auto });
        body.Children.Add(grid);
        var form = new StackPanel(); form.Children.Add(Text("目标取得格口次数/小时（本机显示，选填1～100000，不控制供包）")); form.Children.Add(target);
        applyTarget = Button("应用本机目标", ApplyTarget); form.Children.Add(applyTarget);
        form.Children.Add(Text("站点时区（用于今天、近7天和小时标签）")); form.Children.Add(timezone); form.Children.Add(automatic);
        form.Children.Add(Text("清理起始小时（0～23）")); form.Children.Add(cleanupHour); form.Children.Add(Text("自动维护完整备份上限（1～100份）")); form.Children.Add(backupLimit);
        form.Children.Add(Text("清理前创建完整备份，仅清理已结且超过保留期的明细；保留统计汇总、防重及未确认请求。备份达到份数上限时停止清理。"));
        save = Button("保存时区与清理设置…", Save); form.Children.Add(save); form.Children.Add(policyState);
        editor = new Expander { Header = "目标与自动维护设置（管理模式）", Content = form, Margin = new(0, 16, 0, 0) }; body.Children.Add(editor);
        range.SelectionChanged += async (_, _) => { if (IsLoaded && !busy) await Run(Load); };
        Loaded += async (_, _) => { lifetime = new(); generation++; vm.PropertyChanged += Changed; UpdatePermissions(); await Run(Load); };
        Unloaded += (_, _) => { generation++; lifetime?.Cancel(); lifetime?.Dispose(); lifetime = null; vm.PropertyChanged -= Changed; editor.IsExpanded = false; };
        AutomationProperties.SetName(range, "统计时间范围"); AutomationProperties.SetName(timezone, "站点时区"); AutomationProperties.SetName(cleanupHour, "自动清理起始小时"); AutomationProperties.SetName(backupLimit, "自动维护备份上限"); AutomationProperties.SetName(target, "目标取得格口次数每小时");
    }
    void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    { if (e.PropertyName == nameof(vm.IsManagementMode)) UpdatePermissions(); if (e.PropertyName == nameof(vm.TargetEfficiencyPerHour)) chart.Target = vm.TargetEfficiencyPerHour; }
    void UpdatePermissions() { editor.IsEnabled = vm.IsManagementMode && !busy; if (!vm.IsManagementMode) editor.IsExpanded = false; }
    async Task Load()
    {
        if (api is null || policyApi is null) throw new NotSupportedException("当前业务不提供运行班次统计");
        var token = lifetime!.Token; var version = generation; var selectedRange = (StationReportRange)range.SelectedIndex;
        var snapshot = await vm.Application.SnapshotAsync().WaitAsync(token);
        if (!loadedPolicy)
        {
            policy = snapshot.Configuration.Pending?.OperationsPolicy ?? snapshot.Configuration.Effective.OperationsPolicy ?? await policyApi.ReadOperationsPolicyAsync().WaitAsync(token);
            token.ThrowIfCancellationRequested(); policyRevision = snapshot.Configuration.Revision;
            timezone.SelectedItem = policy.TimeZoneId; automatic.IsChecked = policy.AutomaticHistoryCleanup; cleanupHour.Text = policy.CleanupHour.ToString(CultureInfo.InvariantCulture); backupLimit.Text = policy.MaximumBackupSets.ToString(CultureInfo.InvariantCulture);
            target.Text = vm.TargetEfficiencyDraft; savedTarget = target.Text; loadedPolicy = true; savedPolicySignature = PolicySignature();
        }
        var value = await api.ReadSessionStatisticsAsync(new(selectedRange), token);
        if (version != generation || !IsLoaded) return;
        last = value; var totals = value.Totals; var requested = totals.Sum(x => x.Requests); var assigned = totals.Sum(x => x.Assigned);
        var numbers = new[] { totals.Sum(x => x.Recognitions).ToString("N0"), requested.ToString("N0"), assigned.ToString("N0"), totals.Sum(x => x.Rejected).ToString("N0"), totals.Sum(x => x.Unknown).ToString("N0"), requested == 0 ? "—" : (assigned * 100d / requested).ToString("F1") + "%", value.EffectiveElapsedMilliseconds is > 0 ? (assigned * 3600000d / value.EffectiveElapsedMilliseconds.Value).ToString("F1") : "—" };
        for (var i = 0; i < values.Length; i++) values[i].Text = numbers[i];
        var zone = TimeZoneInfo.FindSystemTimeZoneById(value.TimeZoneId);
        scope.Text = value.Shift is { } s ? $"班次 #{s.Sequence} · {TimeZoneInfo.ConvertTime(s.OpenedAt, zone):yyyy-MM-dd HH:mm:ss zzz} 开始 · {(s.Status == "Open" ? "运行中" : s.Status == "Closed" ? "已正常关闭" : "上次运行中断，结束时间未确认")}" : value.Range == StationReportRange.PreviousShift ? "尚无上一班记录" : $"{TimeZoneInfo.ConvertTime(value.From, zone):yyyy-MM-dd HH:mm zzz} ～ {TimeZoneInfo.ConvertTime(value.ToExclusive, zone):yyyy-MM-dd HH:mm zzz}";
        feedback.Text = $"读取于 {TimeZoneInfo.ConvertTime(value.ReadAt, zone):HH:mm:ss zzz} · 站点时区 {value.TimeZoneId} · CSV导出使用本次快照";
        notices.Text = string.Join("\n", value.Notices);
        string ReasonGroup(bool rcs) { var rows = value.Reasons.Where(x => (x.Scope == "rcs") == rcs).OrderBy(x => x.Scope).ThenBy(x => x.Code).ToArray(); return (rcs ? "RCS请求" : "识别与准入观察") + "\n" + (rows.Length == 0 ? "当前范围没有此类异常记录。" : string.Join("\n", rows.Select(x => $"{StationExceptionText.Label(x.Code)}：{x.Count:N0} 次"))); }
        reasons.Text = ReasonGroup(false) + "\n\n" + ReasonGroup(true);
        grid.ItemsSource = value.Buckets.GroupBy(x => new { Hour = TimeZoneInfo.ConvertTime(x.Minute, zone).ToString("yyyy-MM-dd HH:00 zzz"), x.Position }).Select(g => new { Time = g.Key.Hour, Position = StationExceptionText.Position(g.Key.Position), Recognitions = g.Sum(x => x.Recognitions), Requests = g.Sum(x => x.Requests), Assigned = g.Sum(x => x.Assigned), Rejected = g.Sum(x => x.Rejected), Unknown = g.Sum(x => x.Unknown), ManualRequests = g.Sum(x => x.ManualRequests) }).ToArray();
        chart.Set(value.Buckets, zone, vm.TargetEfficiencyPerHour);
    }
    async Task ApplyTarget()
    {
        await vm.RequireManagementModeAsync(); vm.TargetEfficiencyDraft = target.Text;
        if (!vm.ApplyStationDisplay()) throw new InvalidOperationException(vm.DisplayPreferenceStatus);
        savedTarget = target.Text = vm.TargetEfficiencyDraft; chart.Target = vm.TargetEfficiencyPerHour; policyState.Text = "本机目标已应用，不改变业务配置或供包速度。";
    }
    async Task Save()
    {
        await vm.RequireManagementModeAsync();
        if (!int.TryParse(cleanupHour.Text, out var hour) || !int.TryParse(backupLimit.Text, out var sets)) throw new InvalidOperationException("清理时刻和备份上限须填写整数");
        var updated = policy! with { TimeZoneId = timezone.SelectedItem as string ?? "", AutomaticHistoryCleanup = automatic.IsChecked == true, CleanupHour = hour, MaximumBackupSets = sets };
        var receipt = await policyApi!.SaveOperationsPolicyAsync(policyRevision, updated);
        if (lifetime?.IsCancellationRequested != false) return;
        policyState.Text = receipt.Status + " · " + receipt.Reason + " 时区与清理设置保存后需重启生效。";
        if (receipt.Status == "PendingRestart") { savedPolicySignature = PolicySignature(); policy = updated; policyRevision = (await vm.Application.SnapshotAsync()).Configuration.Revision; }
    }
    async Task Export()
    {
        var value = last ?? throw new InvalidOperationException("请先读取统计");
        if (value.SelectionId is null) throw new InvalidOperationException("本次快照不能导出，请刷新或缩小范围");
        var dialog = new SaveFileDialog { Filter = "CSV统计|*.csv", FileName = "station-statistics-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".csv", InitialDirectory = "D:\\" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        feedback.Text = "正在导出选定快照…";
        var path = await api!.ExportSessionStatisticsAsync(value.SelectionId, dialog.FileName, lifetime!.Token);
        if (IsLoaded) feedback.Text = "统计及快照校验回执已导出：" + path;
    }
    async Task Run(Func<Task> action)
    {
        if (busy && busyGeneration == generation || lifetime is null) return;
        if (lifetime.IsCancellationRequested) { lifetime.Dispose(); lifetime = new(); }
        var operationGeneration = generation; busyGeneration = operationGeneration;
        busy = true; refresh.IsEnabled = export.IsEnabled = range.IsEnabled = false; UpdatePermissions();
        try { await action(); }
        catch (OperationCanceledException) { if (IsLoaded && operationGeneration == generation) feedback.Text = "操作已取消；上次读取保留。请刷新后继续。"; }
        catch (Exception e) { if (IsLoaded && operationGeneration == generation) feedback.Text = "操作未完成：" + DiagnosticText.Protect(e.Message) + "；上次读取保留。"; }
        finally { if (busyGeneration == operationGeneration) { busy = false; if (IsLoaded) { refresh.IsEnabled = export.IsEnabled = range.IsEnabled = true; UpdatePermissions(); } } }
    }
    Button Button(string label, Func<Task> action, bool serialized = true) { var b = new Button { Content = label, MinHeight = 44, Margin = new(0, 4, 8, 4), Padding = new(12, 4, 12, 4), HorizontalAlignment = HorizontalAlignment.Left }; b.Click += async (_, _) => { if (serialized) await Run(action); else await action(); }; return b; }
    static TextBlock Text(string value) => new() { Text = value, TextWrapping = TextWrapping.Wrap, Margin = new(0, 4, 0, 4) };
}

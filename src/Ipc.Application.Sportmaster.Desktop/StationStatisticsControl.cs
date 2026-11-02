using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;
using Microsoft.Win32;

namespace Ipc.Application.Sportmaster.Desktop;

internal sealed class StationStatisticsControl : UserControl
{
    readonly SupplyStationViewModel vm;
    readonly IStationOperations? api;
    readonly ComboBox range = new() { ItemsSource = new[] { "本班", "今天", "近7天" }, SelectedIndex = 0, MinWidth = 140, MinHeight = 44 };
    readonly TextBlock result = Text("正在读取本站统计"), rates = Text(""), feedback = Text(""), policyState = Text("");
    readonly DataGrid grid = Table(), shifts = Table();
    readonly ObservableCollection<ShiftEdit> shiftRows = [];
    readonly ComboBox timezone = new() { ItemsSource = TimeZoneInfo.GetSystemTimeZones().Select(z => z.Id).ToArray(), MinWidth = 240, MinHeight = 44 };
    readonly CheckBox automatic = new() { Content = "空闲时自动清理过期记录", MinHeight = 44 };
    readonly TextBox cleanupHour = new() { Text = "3", MinWidth = 80 }, backupLimit = new() { Text = "7", MinWidth = 80 };
    readonly Expander editor;
    readonly Button refresh, save;
    CancellationTokenSource? lifetime;
    long generation, policyRevision;
    bool busy, loadedPolicy;
    string savedPolicySignature = "";
    string PolicySignature() => Ipc.Platform.Wire.Encode(new { TimeZone = timezone.SelectedItem, Shifts = shiftRows.ToArray(), Automatic = automatic.IsChecked, Hour = cleanupHour.Text, Sets = backupLimit.Text });
    public bool HasUnsavedChanges => loadedPolicy && PolicySignature() != savedPolicySignature;
    StationStatistics? last;
    StationOperationsPolicy? policy;
    public sealed class ShiftEdit
    {
        public string Name { get; set; } = "";
        public string Start { get; set; } = "00:00";
        public string End { get; set; } = "24:00";
    }
    public StationStatisticsControl(SupplyStationViewModel vm)
    {
        this.vm = vm; api = vm.Application as IStationOperations;
        FontFamily = UiTypography.Interface; FontSize = UiTypography.Body;
        var root = new DockPanel { Margin = new(24, 20, 24, 16) }; Content = root;
        var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        header.Children.Add(new TextBlock { Text = "统计与班次", FontSize = UiTypography.PageTitle, FontWeight = UiTypography.Emphasis });
        header.Children.Add(Text("识别、请求、取得格口分别计数，同一条码多次识别分别计算。以下是软件处理次数，不等于实际落格件数。"));
        var actions = new WrapPanel { Margin = new(0, 12, 0, 8) }; header.Children.Add(actions); actions.Children.Add(range);
        refresh = Button("刷新统计", Load); actions.Children.Add(refresh); actions.Children.Add(Button("导出CSV…", Export));
        header.Children.Add(result); header.Children.Add(rates); header.Children.Add(feedback);
        foreach (var (caption, path) in new[] { ("时间（站点时区）", "Time"), ("投递位", "Position"), ("识别次数", "Recognitions"), ("请求次数", "Requests"), ("取得格口", "Assigned"), ("拒绝", "Rejected"), ("格口未确认", "Unknown"), ("人工请求", "ManualRequests") })
            grid.Columns.Add(new DataGridTextColumn { Header = caption, Binding = new Binding(path), Width = DataGridLength.Auto });
        root.Children.Add(grid);
        var form = new StackPanel(); form.Children.Add(Text("站点时区")); form.Children.Add(timezone);
        shifts.IsReadOnly = false; shifts.CanUserAddRows = true; shifts.CanUserDeleteRows = true; shifts.ItemsSource = shiftRows; shifts.Height = 150;
        foreach (var (caption, path) in new[] { ("班次名称", "Name"), ("开始 HH:mm", "Start"), ("结束 HH:mm", "End") })
            shifts.Columns.Add(new DataGridTextColumn { Header = caption, Binding = new Binding(path) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        form.Children.Add(shifts); form.Children.Add(Text("班次覆盖24小时，不能重叠或留空；跨日班次可写22:00～06:00。"));
        form.Children.Add(automatic); form.Children.Add(Text("清理起始小时（0～23）")); form.Children.Add(cleanupHour); form.Children.Add(Text("自动维护完整备份上限（1～100份）")); form.Children.Add(backupLimit);
        form.Children.Add(Text("只清理超过保留天数且已处理完的记录。清理前自动完整备份，统计汇总和防重记录保留；备份份数达到上限时停止清理，并提示转存备份。"));
        save = Button("保存班次与清理设置…", Save); form.Children.Add(save); form.Children.Add(policyState);
        editor = new Expander { Header = "班次与自动维护设置（管理模式）", Content = new ScrollViewer { Content = form, MaxHeight = 320, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, Margin = new(0, 8, 0, 0) };
        DockPanel.SetDock(editor, Dock.Bottom); root.Children.Insert(1, editor);
        Loaded += async (_, _) => { lifetime = new(); generation++; await Run(Load); };
        Unloaded += (_, _) => { generation++; lifetime?.Cancel(); lifetime?.Dispose(); lifetime = null; };
        vm.PropertyChanged += OnViewModelChanged;
        Unloaded += (_, _) => { editor.IsExpanded = false; };
        AutomationProperties.SetName(range, "统计时间范围"); AutomationProperties.SetName(timezone, "站点时区");
        AutomationProperties.SetName(cleanupHour, "自动清理起始小时"); AutomationProperties.SetName(backupLimit, "自动维护备份上限");
    }
    void OnViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(vm.IsManagementMode)) return;
        editor.IsEnabled = vm.IsManagementMode;
        if (!vm.IsManagementMode) editor.IsExpanded = false;
    }
    async Task Load()
    {
        if (api is null) throw new NotSupportedException("当前版本不提供本站统计");
        var token = lifetime!.Token; var version = generation;
        var snapshot = await vm.Application.SnapshotAsync(); token.ThrowIfCancellationRequested();
        policy = await api.ReadOperationsPolicyAsync(); token.ThrowIfCancellationRequested();
        if (!loadedPolicy)
        {
            loadedPolicy = true; policyRevision = snapshot.Configuration.Revision; timezone.SelectedItem = policy.TimeZoneId;
            foreach (var row in policy.Shifts) shiftRows.Add(new() { Name = row.Name, Start = Clock(row.StartMinute), End = Clock(row.EndMinute) });
            automatic.IsChecked = policy.AutomaticHistoryCleanup; cleanupHour.Text = policy.CleanupHour.ToString(CultureInfo.InvariantCulture); backupLimit.Text = policy.MaximumBackupSets.ToString(CultureInfo.InvariantCulture); savedPolicySignature = PolicySignature();
        }
        var now = DateTimeOffset.UtcNow; var to = new DateTimeOffset(now.UtcDateTime.AddTicks(-(now.UtcTicks % TimeSpan.TicksPerMinute)), TimeSpan.Zero).AddMinutes(1);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(policy.TimeZoneId);
        var local = TimeZoneInfo.ConvertTime(now, zone).Date;
        var from = range.SelectedIndex == 0 ? StationShiftSchedule.Current(policy, now).From : new DateTimeOffset(local.AddDays(range.SelectedIndex == 2 ? -6 : 0), zone.GetUtcOffset(local));
        var value = await api.ReadStatisticsAsync(from, to, token);
        if (version != generation || !IsLoaded) return; last = value;
        result.Text = $"识别 {value.Buckets.Sum(x => x.Recognitions)} 次 · 请求 {value.Buckets.Sum(x => x.Requests)} 次 · 取得格口 {value.Buckets.Sum(x => x.Assigned)} 次 · 拒绝 {value.Buckets.Sum(x => x.Rejected)} 次 · 格口未确认 {value.Buckets.Sum(x => x.Unknown)} 次";
        var requests = value.Buckets.Sum(x => x.Requests); var assigned = value.Buckets.Sum(x => x.Assigned);
        var elapsed = now - from;
        rates.Text = $"平均取得格口速度：{(elapsed.TotalHours > 0 ? assigned / elapsed.TotalHours : 0):F1} 次/小时 · 取得格口比例：{(requests == 0 ? "—" : (assigned * 100d / requests).ToString("F1", CultureInfo.CurrentCulture) + "%")}（包含暂停时段，按请求结果计）";
        grid.ItemsSource = value.Buckets.GroupBy(x => new { Hour = TimeZoneInfo.ConvertTime(x.Minute, zone).ToString("yyyy-MM-dd HH:00 zzz"), x.Position }).Select(g => new {
            Time = g.Key.Hour, Position = g.Key.Position == "left" ? "左位" : g.Key.Position == "right" ? "右位" : g.Key.Position, Recognitions = g.Sum(x => x.Recognitions), Requests = g.Sum(x => x.Requests), Assigned = g.Sum(x => x.Assigned),
            Rejected = g.Sum(x => x.Rejected), Unknown = g.Sum(x => x.Unknown), ManualRequests = g.Sum(x => x.ManualRequests) }).ToArray();
        feedback.Text = $"{TimeZoneInfo.ConvertTime(from, zone):yyyy-MM-dd HH:mm} ～ {TimeZoneInfo.ConvertTime(to, zone):yyyy-MM-dd HH:mm}；站点时区：{policy.TimeZoneId}";
        editor.IsEnabled = vm.IsManagementMode;
    }
    async Task Save()
    {
        await vm.RequireManagementModeAsync(); shifts.CommitEdit(); shifts.CommitEdit(DataGridEditingUnit.Row, true);
        if (!int.TryParse(cleanupHour.Text, out var hour) || !int.TryParse(backupLimit.Text, out var sets)) throw new InvalidOperationException("清理时刻和备份上限须填写整数");
        var updated = new StationOperationsPolicy(timezone.SelectedItem as string ?? "", shiftRows.Select(s => new StationShift(s.Name, Minute(s.Start, false), Minute(s.End, true))).ToArray(), automatic.IsChecked == true, hour, sets);
        var receipt = await api!.SaveOperationsPolicyAsync(policyRevision, updated);
        if (lifetime?.IsCancellationRequested != false) return;
        policyState.Text = receipt.Status + " · " + receipt.Reason + " 班次与清理设置保存后，需重启软件生效。";
        if (receipt.Status == "PendingRestart") { savedPolicySignature = PolicySignature(); policyRevision = (await vm.Application.SnapshotAsync()).Configuration.Revision; }
    }
    async Task Export()
    {
        var value = last ?? throw new InvalidOperationException("请先读取统计");
        var dialog = new SaveFileDialog { Filter = "CSV统计|*.csv", FileName = "station-statistics.csv", InitialDirectory = "D:\\" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        var text = new StringBuilder("UTC时间,投递位,识别次数,请求次数,格口取得次数,拒绝次数,未确认次数,人工请求次数\r\n");
        foreach (var row in value.Buckets) text.AppendLine(FormattableString.Invariant($"{row.Minute:O},{row.Position},{row.Recognitions},{row.Requests},{row.Assigned},{row.Rejected},{row.Unknown},{row.ManualRequests}"));
        var temporary = dialog.FileName + ".partial-" + Guid.NewGuid().ToString("N");
        try { await File.WriteAllTextAsync(temporary, text.ToString(), new UTF8Encoding(true), lifetime!.Token); File.Move(temporary, dialog.FileName, false); feedback.Text = "统计已导出：" + dialog.FileName; }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    async Task Run(Func<Task> action)
    {
        if (busy || lifetime is null) return; busy = true; refresh.IsEnabled = save.IsEnabled = false;
        try { await action(); }
        catch (OperationCanceledException) { if (IsLoaded) feedback.Text = "操作已取消；如已提交，请确认当前设置是否已保存。"; }
        catch (Exception e) { if (IsLoaded) feedback.Text = "操作未完成：" + e.Message; }
        finally { busy = false; if (IsLoaded) { refresh.IsEnabled = true; save.IsEnabled = vm.IsManagementMode; } }
    }
    Button Button(string name, Func<Task> action) { var b = new Button { Content = name, MinHeight = 44, Margin = new(8, 0, 0, 4), Padding = new(12, 4, 12, 4) }; b.Click += async (_, _) => await Run(action); return b; }
    static DataGrid Table() => new() { AutoGenerateColumns = false, IsReadOnly = true, EnableRowVirtualization = true, EnableColumnVirtualization = true, CanUserAddRows = false, MinHeight = 80 };
    static TextBlock Text(string value) => new() { Text = value, TextWrapping = TextWrapping.Wrap, Margin = new(0, 6, 0, 6) };
    static string Clock(int value) => $"{value / 60:00}:{value % 60:00}";
    static int Minute(string value, bool end)
    {
        if (end && value == "24:00") return 1440;
        if (!TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)) throw new InvalidOperationException("班次时间须采用HH:mm格式");
        return time.Hour * 60 + time.Minute;
    }
}

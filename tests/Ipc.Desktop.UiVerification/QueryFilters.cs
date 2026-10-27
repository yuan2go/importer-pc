using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Platform;

static partial class Program
{
    static async Task VerifyQueryFilters(string root, string evidence, List<string> lines)
    {
        void Check(bool ok, string reason) { if (!ok) throw new InvalidOperationException(reason); lines.Add("PASS QUERY01 " + reason); }
        var folder = Path.Combine(evidence, "query-config"); Directory.CreateDirectory(folder);
        var config = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(root, "config/sportmaster/business.json")));
        ExtensionStore.AtomicWrite(Path.Combine(folder, "business.json"), config);
        var launch = new BusinessLaunch(folder, Path.Combine(evidence, "query-data"), null, null);
        await using var runtime = new ParcelRuntime(launch, new SportmasterFlow(), new CommercialProductization.SyntheticFactory(launch)) { RuntimeIdentityPreflight = () => { } };
        await runtime.StartAsync(default); await ManagementModeFixture.Authorize(runtime);
        var app = DispatchProxy.Create<IHistoryProjectionFixture, ProjectionProbe>(); ((ProjectionProbe)(object)app).Inner = runtime;
        var window = new SupplyStationWindow(app, null, "standard") { ConfirmDiscardDrafts = _ => true }; window.Show();
        var vm = window.ViewModel; var client = (Grid)window.FindName("ClientArea");
        async Task Flush() { await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout(); }
        async Task Until(Func<bool> ready) { for (var i = 0; i < 200; i++) { await vm.RefreshAsync(); await Flush(); if (ready()) return; await Task.Delay(25); } throw new TimeoutException("Query UI did not converge"); }
        await SetClient(window, 1366, 768); await Until(() => vm.Fresh && vm.IsManagementMode);
        runtime.EmitReferenceAt("left", "query-left", "000123456", "query-event"); await Until(() => vm.Left.SlotAvailable);
        Click(window, "作业记录"); await Until(() => vm.HistoryLoaded && !vm.HistoryLoading && !vm.Busy);
        var history = (HistoryPage)((ContentControl)window.FindName("PageHost")).Content;
        Check(vm.HistoryItems.Length == 1 && vm.CanExportHistory, "history still reads its real fixed selection and export boundary");
        var inputs = Descendants(history).OfType<DateTimeInput>().ToArray(); Check(inputs.Length == 2, "history has exactly two shared time controls");
        var input = inputs[0]; var calendar = Descendants(input).OfType<DatePicker>().Single(); var clock = Descendants(input).OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "时间");
        const string original = "2026-10-19T23:59:59.1234567+05:30"; input.Timestamp = original; await Flush();
        var local = DateTimeOffset.Parse(original).ToLocalTime();
        Check(input.Timestamp == original && vm.HistoryFrom == original && calendar.SelectedDate == local.Date && clock.Text == local.ToString("HH:mm:ss"), "nonlocal offset and fractional seconds survive local rendering without query mutation");
        Check(calendar.ActualHeight == 44 && clock.ActualHeight == 44, "date and time fields have the same native 44 DIP height");
        calendar.ApplyTemplate(); ((Button)calendar.Template.FindName("PART_Button", calendar)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Flush();
        Check(calendar.IsDropDownOpen && ((Popup)calendar.Template.FindName("PART_Popup", calendar)).Child is Calendar, "calendar button opens the native calendar"); calendar.IsDropDownOpen = false;
        var more = Descendants(input).OfType<Button>().Single(b => Equals(b.Content, "更多"));
        var precise = (MenuItem)more.ContextMenu.Items[1]; precise.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); await Flush();
        var raw = Descendants(input).OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "精确时间原文");
        Check(raw.IsVisible && raw.Text == original, "precision input remains reachable with the unchanged offset and fractional value");
        precise.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        var fixedSelection = vm.AppliedHistoryRange; clock.Text = "25:99:00";
        bool rejected = false; try { await vm.LoadHistoryAsync(); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected && vm.HistoryFiltersChanged && !vm.CanExportHistory && vm.AppliedHistoryRange == fixedSelection, "invalid time cannot reuse or broaden the previous query/export selection");
        clock.Text = "09:10:11"; Check(DateTimeOffset.TryParse(vm.HistoryFrom, out _), "correcting the invalid clock restores a valid explicit instant");
        calendar.Text = "not-a-date"; await Flush(); var invalid = input.Timestamp; clock.Text = "10:11:12";
        Check(input.Timestamp == invalid && calendar.SelectedDate is null && invalid.StartsWith("无效日期"), "invalid typed date cannot revive a previous day through a clock edit");
        calendar.SelectedDate = DateTime.Today; Check(DateTimeOffset.TryParse(vm.HistoryFrom, out _), "explicit calendar selection corrects the invalid date");
        var eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        Check(!DateTimeInput.TryLocalTime(new(2026, 3, 8, 2, 30, 0), eastern, out _) && !DateTimeInput.TryLocalTime(new(2026, 11, 1, 1, 30, 0), eastern, out _), "DST nonexistent and ambiguous wall times require explicit precision input");
        Check(DateTimeInput.TryLocalTime(new(2026, 7, 1, 12, 0, 0), eastern, out var summer) && summer.Offset == TimeSpan.FromHours(-4), "local edit resolves the offset for the selected date");
        Click(history, "今天"); Click(history, "查询"); await Until(() => !vm.HistoryLoading && !vm.Busy && vm.CanExportHistory); await Flush();
        Check(vm.HistoryRangeSummary.Contains("本机时间") && !vm.HistoryRangeSummary.Contains("固定序号") && !vm.HistoryRangeSummary.Contains("+08:00"), "default applied history summary hides engineering boundaries while retaining the technical selection");
        Check(vm.HistoryItems.Length == 1, "today query includes the synthetic observation");
        foreach (var size in new[] { (1366d, 768d), (800d, 420d) })
        {
            await SetClient(window, size.Item1, size.Item2); await Flush();
            var actions = (WrapPanel)history.FindName("HistoryQueryActions");
            Check(Inside(actions, client) && Descendants(actions).OfType<Button>().Where(b => b.IsVisible).All(b => b.ActualHeight >= 44), "history query actions remain visible at " + size.Item1);
            CaptureInternal(evidence, "query-history-" + (int)size.Item1, client, lines);
        }
        await SetClient(window, 1366, 768); Click(window, "日志查询"); await Until(() => vm.SelectedPage == "logs");
        var logs = (SoftwareLogControl)((ContentControl)window.FindName("PageHost")).Content;
        var ranges = Descendants(logs).OfType<DateTimeInput>().ToArray(); Check(ranges.Length == 2, "log page reuses the same two local time controls");
        var applied = (TextBox)typeof(SoftwareLogControl).GetField("effectiveRange", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(logs)!;
        Check(applied.Text.Contains("待查询") && !applied.Text.Contains("T") && !applied.Text.Contains("+08:00"), "pending log range is readable and does not claim a completed query");
        Click(logs, "今天"); Click(logs, "查询");
        var queryButton = Descendants(logs).OfType<Button>().Single(b => Equals(b.Content, "查询"));
        for (var i=0; i<200 && !queryButton.IsEnabled; i++) await Task.Delay(25); await Flush();
        var query = (SoftwareLogQuery)typeof(SoftwareLogControl).GetField("query", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(logs)!;
        Check(query.From.EqualsExact(DateTimeOffset.Parse(ranges[0].Timestamp)) && query.ToExclusive.EqualsExact(DateTimeOffset.Parse(ranges[1].Timestamp)), "log query retains the exact canonical endpoints behind local input");
        Check(applied.Text.Contains("已应用范围"), "successful log query replaces pending wording");
        foreach (var size in new[] { (1366d, 768d), (800d, 420d) })
        {
            await SetClient(window, size.Item1, size.Item2); await Flush();
            Check(Inside(queryButton, client), "log query action remains reachable at " + size.Item1);
            Check(!Descendants(logs).OfType<TextBlock>().Any(t => t.IsVisible && t.Text.Contains("UTC偏移")), "default query controls do not expose offset jargon");
            CaptureInternal(evidence, "query-logs-" + (int)size.Item1, client, lines);
        }
        ranges[0].Timestamp = "invalid-time"; await Flush();
        Check(applied.Text.Contains("时间无效") && applied.Text.Contains("待查询"), "invalid log endpoint is explained without restoring an earlier instant");
        Check(!((Button)typeof(SoftwareLogControl).GetField("exportButton", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(logs)!).IsEnabled, "editing log time revokes the old fixed export selection");
        window.Close(); await Task.Delay(100);
        lines.Add("Windows native WPF, synthetic records and current local timezone; no system timezone changed. Real field users, touch and site validation NOT_RUN.");
    }
}

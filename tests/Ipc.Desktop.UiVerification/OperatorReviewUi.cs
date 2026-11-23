using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Assembly;
using Ipc.Platform;

static partial class Program
{
    static async Task VerifyOperatorReviewUi(string root, string evidence, List<string> lines)
    {
        void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); lines.Add("PASS OPERATOR-REVIEW " + text); }
        await using var h = new CameraRoundHarness(root, System.IO.Path.Combine(evidence, "operator-review"));
        await h.StartAsync(authorizeManagement: false);
        const string settingsPassword = "Synthetic-Operator-Review-Settings";
        await h.App.ConfigureManagementPasswordAsync(settingsPassword);
        var paceFile = Path.Combine(evidence, "user-preferences", "presentation", "sportmaster.pace.json");
        Directory.CreateDirectory(Path.GetDirectoryName(paceFile)!);
        File.WriteAllText(paceFile, "{broken");
        var window = new SupplyStationWindow(h.App, null, "standard"); window.Show(); var vm = window.ViewModel;
        async Task Flush() { await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout(); }
        async Task Until(Func<bool> done) { for (var i = 0; i < 200; i++) { await vm.RefreshAsync(); await Flush(); if (done()) return; await Task.Delay(25); } throw new TimeoutException("Operator review UI did not converge"); }
        void Page(string page) => typeof(SupplyStationWindow).GetMethod("SetPage", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, [page]);
        try
        {
            await SetClient(window, 1536, 816); await Until(() => vm.Fresh);
            var bell = Descendants(window).OfType<System.Windows.Controls.Primitives.ToggleButton>()
                .Single(b => b.IsVisible && AutomationProperties.GetName(b) == "本机偏好通知");
            Check(vm.PreferenceNoticeText.Contains("效率显示偏好未能读取") && !vm.WorkFeedback.Contains("效率显示偏好") &&
                File.ReadAllText(paceFile) == "{broken", "corrupt local preferences are retained and reported through the header bell");
            bell.IsChecked = true; await Flush();
            var popover = ((Grid)bell.Parent).Children.OfType<System.Windows.Controls.Primitives.Popup>().Single();
            Check(popover.IsOpen && Descendants(popover.Child).OfType<TextBox>().Single().Text.Contains("效率显示偏好未能读取"),
                "header bell opens the readable preference warning");
            bell.IsChecked = false;
            File.WriteAllText(paceFile, Wire.Encode(new { Schema = 1, Compact = true }));
            PresentationPreferences.RefreshSaved("sportmaster", "pace"); await Flush();
            Check(!bell.IsVisible && !vm.HasPreferenceNotices, "confirmed preference recovery clears the header notice");
            var nav = (Border)window.FindName("DailyNavigation");
            var settingsEntry = (Button)window.FindName("StationSettingsEntry");
            var navigationButtons = Descendants(nav).OfType<Button>().ToArray();
            var labels = navigationButtons.Select(button => button.Content?.ToString()).ToArray();
            Check(navigationButtons.Contains(settingsEntry) && settingsEntry is { Content: "设置", IsVisible: true, IsEnabled: true } &&
                labels.Count(label => label == "设置") == 1 &&
                labels.Contains("管理中心") && !labels.Any(label => label is "设备与诊断" or "调试" or "日志" or "维护中心"),
                "daily navigation exposes one read-only settings entry and keeps management pages inside the management center");
            var ownerHandle = new WindowInteropHelper(window).Handle;
            _ = window.Dispatcher.BeginInvoke(new Action(() => settingsEntry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent))));
            await Until(() => Application.Current.Windows.OfType<StationSettingsWindow>().Any(w => w.IsVisible));
            var readOnlySettings = Application.Current.Windows.OfType<StationSettingsWindow>().Single(w => w.IsVisible);
            try
            {
                var settingsInputs = Descendants(readOnlySettings).OfType<TextBox>().ToArray();
                Check(readOnlySettings.Owner == window && ManagementOwnerEnabled(ownerHandle) && !vm.IsManagementMode &&
                    readOnlySettings.FindName("AuthorizeEdit") is Button { IsVisible: true } &&
                    settingsInputs.Any(input => input.IsVisible) && settingsInputs.All(input => input.IsReadOnly) &&
                    !Descendants(readOnlySettings).OfType<MaintenancePage>().Any(),
                    "daily settings opens as an owned modeless read-only window without granting management access");
                var edit = (Button)readOnlySettings.FindName("AuthorizeEdit");
                _ = window.Dispatcher.BeginInvoke(new Action(() => edit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent))));
                await Until(() => Application.Current.Windows.OfType<ManagementModeWindow>().Any(w => w.IsVisible &&
                    Descendants(w).OfType<Button>().Any(b => Equals(b.Content, "进入管理中心") && b.IsEnabled)));
                var password = Application.Current.Windows.OfType<ManagementModeWindow>().Single(w => w.IsVisible);
                Descendants(password).OfType<PasswordBox>().Single(p => p.Name == "ManagementPassword").Password = settingsPassword;
                Click(password, "进入管理中心");
                await Until(() => !password.IsVisible && vm.HasLoadedConfiguration && Descendants(readOnlySettings).OfType<MaintenancePage>().Any());
                var stationEditor = Descendants(readOnlySettings).OfType<ParameterSettingsControl>().Single();
                var directory = Descendants(stationEditor).OfType<ListBox>().Single(l => l.Name == "ParameterDirectory");
                Check(directory.Items.Count == 3 && directory.Items.OfType<object>().Select(x => x.ToString())
                    .SequenceEqual(["本站与 RCS", "左右相机", "显示与提示音"]) &&
                    !Descendants(stationEditor).OfType<TextBox>().Any(b => b.IsVisible && AutomationProperties.GetAutomationId(b) == "ParameterSearch") &&
                    Descendants(readOnlySettings).OfType<Button>().Count(b => b.IsVisible && Equals(b.Content, "保存设置…")) == 1,
                    "authorizing the same settings window shows three direct pages and one save button");
                Check(stationEditor.ShowPage(2, "左右相机"), "station settings opens the paired camera cards");
                await Flush(); readOnlySettings.UpdateLayout();
                Check(Descendants(stationEditor).OfType<TextBox>().Count(b => b.IsVisible && AutomationProperties.GetAutomationId(b).EndsWith("-bcr-code", StringComparison.Ordinal)) == 2,
                    "both camera codes are editable on their camera page");
                CaptureInternal(evidence, "review-station-settings-cameras", readOnlySettings, lines);
            }
            finally { readOnlySettings.Close(); await Until(() => !readOnlySettings.IsVisible); }
            if (!(await h.App.ManagementModeStatusAsync()).Active) await h.App.EnterManagementModeAsync(settingsPassword);
            await vm.RefreshManagementModeAsync(); await Flush();
            var clock = (TextBlock)window.FindName("StationClock"); var before = clock.Text; await Task.Delay(1100); await Flush();
            Check(clock.IsVisible && clock.Text != before && clock.Text.Contains(DateTime.Now.ToString("yyyy-MM-dd")), "header clock shows the real local date and advances without a business request");
            var opens = h.Api(h.Right).Opens; var epoch = h.Factory.Sources[h.Right].Epoch;
            h.Peer.Mode = "malformed";
            h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("REVIEW-UNKNOWN"), 1);
            await Until(() => vm.Snapshot?.Operations.Any(o => o.Wcs?.State == StageState.Unknown) == true);
            var unknownId = vm.Snapshot!.Operations.Single(o => o.Wcs?.State == StageState.Unknown).Id;
            // Diagnostics offers record review when an unresolved request has entered historical custody.
            await h.TrayAsync(h.Left, TrayPresence.Absent);
            await Until(() => vm.Snapshot?.Capacity?.HistoricalUnknown == 1);
            Check((await h.App.OperationAsync(unknownId))?.Wcs?.State == StageState.Unknown,
                "qualified removal retains the unresolved original in history");
            Page("diagnostics");
            await Until(() => Application.Current.Windows.OfType<ManagementCenterWindow>().Any(w => w.IsVisible));
            var management = Application.Current.Windows.OfType<ManagementCenterWindow>().Single(w => w.IsVisible);
            var managementNavigation = (WrapPanel)management.FindName("ManagementNavigation");
            Check(management.Owner == window && !ManagementOwnerEnabled(ownerHandle) &&
                managementNavigation.Children.OfType<Button>().Where(button => button.IsVisible).Select(button => button.Content?.ToString())
                    .SequenceEqual(["运行总览", "日志中心", "设备调试", "专业参数", "维护中心"]),
                "the original owned modal management center shows the five operating and maintenance tabs in order");
            var managementClient = (Grid)management.FindName("ClientArea");
            var managementPage = (ContentControl)management.FindName("ManagementPageHost");
            await Flush();
            Check(Descendants(managementPage).OfType<Button>().Any(b => b.IsVisible && Equals(b.Content, "去记录核对")) && ((Border)window.FindName("PendingConfigurationBanner")).Visibility == Visibility.Collapsed,
                "an unresolved request exposes record review without a false pending-settings notice");
            var cards = Descendants(managementPage).OfType<Border>().Where(b => AutomationProperties.GetAutomationId(b).StartsWith("DiagnosticCard.", StringComparison.Ordinal)).ToArray();
            Check(cards.Length == 4 && cards.All(b => b.IsVisible), "four native cards expose left camera, right camera, RCS and storage");
            Check(!Descendants(managementPage).OfType<Button>().Any(b => Equals(b.Content, "刷新体检") || Equals(b.Content, "检查并重启…")), "diagnostics uses automatic updates and no always-present restart action");
            Check(Descendants(managementPage).OfType<Expander>().Where(e => e.Header is "技术详情" or "维护工具").All(e => !e.IsExpanded) &&
                Descendants(managementPage).OfType<Expander>().Count(e => e.Header is "技术详情" or "维护工具") == 2 && !Descendants(managementPage).OfType<Expander>().Any(e => e.Header is "详情与原始证据" or "日志与性能详情"),
                "one technical section and the maintenance tools, both folded");
            var peerCount = h.Peer.Requests.Count;
            await Task.Delay(5300); await Flush();
            Check(h.Peer.Requests.Count == peerCount && h.Api(h.Right).Opens == opens && h.Factory.Sources[h.Right].Epoch == epoch, "automatic diagnostic polling creates no parcel request or replacement camera owner");
            var tools = Descendants(managementPage).OfType<Expander>().Single(e => Equals(e.Header, "维护工具")); tools.IsExpanded = true; await Flush();
            Check(Descendants(tools).OfType<Button>().Single(b => Equals(b.Content, "取消导出")).Visibility == Visibility.Collapsed && Descendants(tools).OfType<Button>().Any(b => Equals(b.Content, "创建完整备份…")) && Descendants(tools).OfType<Button>().Any(b => Equals(b.Content, "离线维护…")), "maintenance retains backup and offline handoff while hiding idle export cancellation");
            Check(!Descendants(tools).OfType<Button>().Any(b => Equals(b.Content, "检查软件更新")) &&
                !Descendants(tools).OfType<TextBlock>().Any(t => t.Text is "软件更新" or "未配置新版本说明。" or "正在读取新版本说明…"),
                "maintenance has no software update action, status or empty section");
            tools.IsExpanded = false;
            Click(management, "维护中心");
            await SetClient(management, 1280, 760); await Flush();
            var danger = Descendants(managementPage).OfType<TextBlock>().Single(t => t.Text == "高危离线维护");
            var offlineEntry = Descendants(managementPage).OfType<Button>().Single(b => Equals(b.Content, "离线维护…"));
            Check(Inside(danger, managementClient) && Inside(offlineEntry, managementClient) &&
                Descendants(managementPage).OfType<ScrollViewer>().First().ScrollableHeight == 0,
                "the offline maintenance section and normal-stop handoff fully fit in 760 DIP");
            CaptureInternal(evidence, "review-operations-maintenance-760", managementClient, lines);
            Click(management, "运行总览"); await Flush();
            foreach (var size in new[] { (1536d, 816d), (800d, 420d) })
            {
                await SetClient(window, size.Item1, size.Item2);
                await SetClient(management, size.Item1, size.Item2); await Flush();
                tools.IsExpanded = true; tools.BringIntoView(); await Flush();
                tools.BringIntoView(); await Flush();
                CaptureInternal(evidence, "review-maintenance-without-updates-" + size.Item1, managementClient, lines);
                tools.IsExpanded = false; await Flush();
                Check(Inside(clock, (Grid)window.FindName("ClientArea")), "clock stays inside the header at " + size.Item1);
                CaptureInternal(evidence, "review-diagnostics-" + size.Item1, managementClient, lines);
            }
            Click(management, "专业参数");
            await Until(() => vm.SelectedPage == "maintenance" && vm.HasLoadedConfiguration && !vm.Busy);
            Check(((Border)window.FindName("OperationalStatusBar")).Visibility == Visibility.Collapsed, "daily settings contains no duplicate operational status strip");
            var settings = Descendants(managementPage).OfType<MaintenancePage>().Single();
            Check(!settings.NavigateToParameter("TimeoutMs"), "SM cannot navigate to the misleading generic timeout editor");
            Check(settings.NavigateToParameter(vm.Snapshot!.Configuration.Effective.Wcs!.InstanceId + "/request-timeout-ms"), "actual RCS request timeout remains reachable");
            var timeout = Descendants(settings).OfType<TextBox>().Single(b => AutomationProperties.GetAutomationId(b).EndsWith("/request-timeout-ms", StringComparison.Ordinal));
            Check((AutomationProperties.GetLabeledBy(timeout) as TextBlock)?.Text.StartsWith("取格口请求超时", StringComparison.Ordinal) == true && !(AutomationProperties.GetLabeledBy(timeout) as TextBlock)!.Text.Contains("毫秒"), "timeout label stays concise and the unit belongs beside its input");
            await h.App.ExitManagementModeAsync(); await vm.RefreshManagementModeAsync(); await Flush();
            Check(management.IsVisible && ((Border)management.FindName("AuthorizationGate")).IsVisible &&
                managementPage.Content is null && cards.All(b => !b.IsVisible),
                "management expiry removes protected diagnostics and settings and keeps the locked window available to close");
            Click(management, "关闭");
            await Until(() => !management.IsVisible && ManagementOwnerEnabled(ownerHandle));
            Check(vm.SelectedPage == "workstation" && h.Api(h.Right).Opens == opens && h.Factory.Sources[h.Right].Epoch == epoch,
                "closing the locked window returns to the workstation without replacing the camera owner");
        }
        finally { window.Close(); }

        await using var factory = new ParcelCapabilityFactory(new(System.IO.Path.Combine(root, "config/sportmaster"), evidence, null, null));
        var draft = new StartupConfigurationDraft(new SportmasterFlow().InitialConfiguration(), factory.Catalog());
        var model = draft.CreateParameterEditor((p, c) => Task.FromResult(Ipc.Application.ParcelFlow.BarcodeFiltering.Select(p, c)));
        var editor = new ParameterSettingsControl { FirstConfiguration = true, Model = model };
        var first = new Window { Content = editor, Width = 1100, Height = 760 }; first.Show();
        try
        {
            var openedRcsStep = editor.ShowPage(0, "本站与 RCS");
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); first.UpdateLayout();
            var rcsTargetId = model.Components.Single(ParameterOwnership.IsRcs).InstanceId + "/Target";
            Check(openedRcsStep && Descendants(editor).OfType<TextBox>().Any(box => box.IsVisible &&
                AutomationProperties.GetAutomationId(box) == rcsTargetId), "first configuration opens the actual RCS step and shows its address editor");
            Check(!Descendants(editor).OfType<ListBox>().Any(b => b.IsVisible), "single-page first-configuration step hides the redundant left rail");
            var currentSteps = Descendants(editor).OfType<Button>().Where(b => AutomationProperties.GetItemStatus(b) == "当前步骤").ToArray();
            Check(currentSteps.Length == 1 && currentSteps[0] is { IsVisible: true, Content: "1  本站与 RCS" },
                "step bar exposes exactly one highlighted and accessible current step");
            Check(!Descendants(editor).OfType<TextBlock>().Any(t => t.Text.Contains("请求编号范围") || t.Text.Contains("请求编号区间")), "RCS configuration has no allocated-range instruction");
            CaptureInternal(evidence, "review-first-rcs", editor, lines);
        }
        finally { first.Close(); }
    }
}

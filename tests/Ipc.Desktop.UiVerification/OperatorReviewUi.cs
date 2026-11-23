using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Assembly;

static partial class Program
{
    static async Task VerifyOperatorReviewUi(string root, string evidence, List<string> lines)
    {
        void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); lines.Add("PASS OPERATOR-REVIEW " + text); }
        await using var h = new CameraRoundHarness(root, System.IO.Path.Combine(evidence, "operator-review"));
        await h.StartAsync();
        var windows = new NativeWindowOwners();
        var window = windows.Track(new SupplyStationWindow(h.App, null, "standard")); window.Show(); var vm = window.ViewModel;
        async Task Flush() { await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout(); }
        async Task Until(Func<bool> done) { for (var i = 0; i < 200; i++) { await vm.RefreshAsync(); await Flush(); if (done()) return; await Task.Delay(25); } throw new TimeoutException("Operator review UI did not converge"); }
        try
        {
            await SetClient(window, 1536, 816); await Until(() => vm.Fresh);
            if (!(await h.App.ManagementModeStatusAsync()).Active) await ManagementModeFixture.Authorize(h.App);
            await vm.RefreshManagementModeAsync(); await Flush();
            var nav = (Border)window.FindName("DailyNavigation");
            var labels = Descendants(nav).OfType<Button>().Select(b => b.Content?.ToString()).ToArray();
            Check(labels.SequenceEqual(new[] { "作业", "记录", "设置" }), "daily navigation exposes only work, records and independent settings in order");
            var entry = (Button)window.FindName("StationSettingsEntry");
            entry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => window.OwnedWindows.OfType<StationSettingsWindow>().Any(w => w.IsVisible));
            var stationSettings = window.OwnedWindows.OfType<StationSettingsWindow>().Single(w => w.IsVisible);
            Check(window.IsEnabled && ((ScrollViewer)stationSettings.FindName("ReadOnlyBody")).IsVisible &&
                !((ContentControl)stationSettings.FindName("EditorHost")).IsVisible,
                "station entry opens modeless read-only settings even with a foreign existing grant");
            stationSettings.Close();
            await Until(() => !stationSettings.IsVisible);
            var clock = (TextBlock)window.FindName("StationClock"); var before = clock.Text; await Task.Delay(1100); await Flush();
            Check(clock.IsVisible && clock.Text != before && clock.Text.Contains(DateTime.Now.ToString("yyyy-MM-dd")), "header clock shows the real local date and advances without a business request");
            var opens = h.Api(h.Right).Opens; var epoch = h.Factory.Sources[h.Right].Epoch;
            h.Peer.Mode = "malformed";
            h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("REVIEW-UNKNOWN"), 1);
            await Until(() => vm.Snapshot?.Operations.Any(o => o.Wcs?.State == StageState.Unknown) == true);
            var operations = windows.Track(new OperationsViewFixture(h.App));
            operations.Show();
            await operations.AdoptFixtureGrantAsync();
            await operations.ViewModel.RefreshAsync();
            operations.Workspace.ShowPage("maintenance");
            operations.UpdateLayout();
            var tools = Descendants(operations).OfType<Border>().Single(c => AutomationProperties.GetAutomationId(c) == "Operations.Maintenance.Daily");
            var responsibility = Descendants(operations).OfType<Border>().Single(c => AutomationProperties.GetAutomationId(c) == "Operations.Maintenance.Responsibilities");
            Check(Descendants(responsibility).OfType<Button>().Any(b => b.IsVisible && b.IsEnabled && Equals(b.Content, "核对原业务记录与请求责任…")) &&
                ((Border)window.FindName("PendingConfigurationBanner")).Visibility == Visibility.Collapsed,
                "an unresolved request has an enabled original record review entry in Operations without a false pending-settings notice");
            operations.Navigate("diagnostics"); await Flush(); operations.UpdateLayout();
            var cards = Descendants(operations).OfType<Border>().Where(b => AutomationProperties.GetAutomationId(b).StartsWith("DiagnosticCard.", StringComparison.Ordinal)).ToArray();
            Check(cards.Length == 4 && cards.All(b => b.IsVisible), "four native cards expose left camera, right camera, RCS and storage");
            Check(!Descendants(operations).OfType<Button>().Any(b => Equals(b.Content, "刷新体检") || Equals(b.Content, "检查并重启…")), "diagnostics uses automatic updates and no always-present restart action");
            var technical = Descendants(operations).OfType<Expander>().Where(e => Equals(e.Header, "技术详情")).ToArray();
            Check(technical is [{ IsExpanded: false }] && !Descendants(operations).OfType<Expander>().Any(e => e.Header is "详情与原始证据" or "日志与性能详情"),
                "one folded technical section in diagnostics; maintenance uses its separate Operations page");
            var peerCount = h.Peer.Requests.Count;
            await Task.Delay(5300); await Flush();
            Check(h.Peer.Requests.Count == peerCount && h.Api(h.Right).Opens == opens && h.Factory.Sources[h.Right].Epoch == epoch, "automatic diagnostic polling creates no parcel request or replacement camera owner");
            operations.Workspace.ShowPage("maintenance");
            operations.UpdateLayout();
            Check(Descendants(tools).OfType<Button>().Single(b => Equals(b.Content, "取消导出")).Visibility == Visibility.Collapsed &&
                Descendants(tools).OfType<Button>().Any(b => Equals(b.Content, "创建完整备份…")) &&
                Descendants(operations).OfType<Button>().Any(b => AutomationProperties.GetAutomationId(b) == "Operations.Maintenance.Offline"),
                "maintenance retains backup and offline handoff while hiding idle export cancellation");
            Check(!Descendants(operations).OfType<Button>().Any(b => Equals(b.Content, "检查软件更新")) &&
                !Descendants(operations).OfType<TextBlock>().Any(t => t.Text is "软件更新" or "未配置新版本说明。" or "正在读取新版本说明…"),
                "maintenance has no software update action, status or empty section");
            foreach (var size in new[] { (1536d, 816d), (800d, 420d) })
            {
                await SetClient(window, size.Item1, size.Item2);
                await SetClient(operations, size.Item1, size.Item2);
                operations.Workspace.ShowPage("maintenance"); operations.UpdateLayout();
                CaptureInternal(evidence, "review-maintenance-without-updates-" + size.Item1, (Grid)operations.FindName("ClientArea"), lines);
                operations.Navigate("diagnostics"); await Flush(); operations.UpdateLayout();
                Check(Inside(clock, (Grid)window.FindName("ClientArea")), "clock stays inside the station header at " + size.Item1);
                CaptureInternal(evidence, "review-diagnostics-" + size.Item1, (Grid)operations.FindName("ClientArea"), lines);
            }
            await operations.ViewModel.EnsureMaintenanceLoadedAsync();
            operations.Navigate("maintenance"); operations.UpdateLayout();
            var settings = operations.Parameters;
            Check(settings.IsVisible && !Descendants(settings).OfType<Border>().Any(b => b.Name == "OperationalStatusBar"),
                "professional settings contains no duplicate workstation operational status strip");
            Check(!settings.NavigateToParameter("TimeoutMs"), "SM cannot navigate to the misleading generic timeout editor");
            Check(settings.NavigateToParameter(operations.ViewModel.Snapshot!.Configuration.Effective.Wcs!.InstanceId + "/request-timeout-ms"), "actual RCS request timeout remains reachable");
            var timeout = Descendants(settings).OfType<TextBox>().Single(b => AutomationProperties.GetAutomationId(b).EndsWith("/request-timeout-ms", StringComparison.Ordinal));
            Check((AutomationProperties.GetLabeledBy(timeout) as TextBlock)?.Text.StartsWith("取格口请求超时", StringComparison.Ordinal) == true && !(AutomationProperties.GetLabeledBy(timeout) as TextBlock)!.Text.Contains("毫秒"), "timeout label stays concise and the unit belongs beside its input");
            operations.Navigate("diagnostics"); operations.UpdateLayout();
            await h.App.ExitManagementModeAsync();
            await operations.ViewModel.RefreshManagementModeAsync();
            await vm.RefreshManagementModeAsync(); await Flush();
            Check(vm.SelectedPage == "workstation" && cards.All(b => !b.IsEnabled) &&
                h.Api(h.Right).Opens == opens && h.Factory.Sources[h.Right].Epoch == epoch,
                "management expiry locks diagnostic controls without touching camera ownership");
        }
        finally { await windows.DisposeAsync(); }

        await using var factory = new ParcelCapabilityFactory(new(System.IO.Path.Combine(root, "config/sportmaster"), evidence, null, null));
        var draft = new StartupConfigurationDraft(new SportmasterFlow().InitialConfiguration(), factory.Catalog());
        var editor = new ParameterSettingsControl { FirstConfiguration = true, Model = draft.CreateParameterEditor((p, c) => Task.FromResult(Ipc.Application.ParcelFlow.BarcodeFiltering.Select(p, c))) };
        var first = new Window { Content = editor, Width = 1100, Height = 760 }; first.Show();
        try
        {
            Check(editor.ShowPage(0, "本站与 RCS"), "first configuration opens the actual RCS step"); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); first.UpdateLayout();
            Check(!Descendants(editor).OfType<ListBox>().Any(b => b.IsVisible), "single-page first-configuration step hides the redundant left rail");
            Check(Descendants(editor).OfType<Button>().Count(b => AutomationProperties.GetItemStatus(b) == "当前步骤") == 1 && Descendants(editor).OfType<Button>().Any(b => AutomationProperties.GetItemStatus(b) == "当前步骤" && AutomationProperties.GetAutomationId(b) == "FirstSetup.Category.0"), "step bar exposes exactly one highlighted and accessible current step");
            Check(!Descendants(editor).OfType<TextBlock>().Any(t => t.Text.Contains("请求编号范围") || t.Text.Contains("请求编号区间")), "RCS configuration has no allocated-range instruction");
            CaptureInternal(evidence, "review-first-rcs", editor, lines);
        }
        finally { first.Close(); }
    }
}

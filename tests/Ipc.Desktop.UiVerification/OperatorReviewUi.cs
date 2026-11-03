using System.Reflection;
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
        var window = new SupplyStationWindow(h.App, null, "standard"); window.Show(); var vm = window.ViewModel;
        async Task Flush() { await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout(); }
        async Task Until(Func<bool> done) { for (var i = 0; i < 200; i++) { await vm.RefreshAsync(); await Flush(); if (done()) return; await Task.Delay(25); } throw new TimeoutException("Operator review UI did not converge"); }
        void Page(string page) => typeof(SupplyStationWindow).GetMethod("SetPage", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, [page]);
        try
        {
            await SetClient(window, 1536, 816); await Until(() => vm.Fresh);
            if (!(await h.App.ManagementModeStatusAsync()).Active) await ManagementModeFixture.Authorize(h.App);
            await vm.RefreshManagementModeAsync(); await Flush();
            var nav = (Border)window.FindName("DailyNavigation");
            var labels = Descendants(nav).OfType<Button>().Select(b => b.Content?.ToString()).ToArray();
            Check(!labels.Contains("维护中心") && Array.IndexOf(labels, "设备与诊断") < Array.IndexOf(labels, "设置") && Array.IndexOf(labels, "设置") < Array.IndexOf(labels, "调试") && Array.IndexOf(labels, "调试") < Array.IndexOf(labels, "日志"), "daily navigation has devices/settings/debug/logs in order without a maintenance center");
            var clock = (TextBlock)window.FindName("StationClock"); var before = clock.Text; await Task.Delay(1100); await Flush();
            Check(clock.IsVisible && clock.Text != before && clock.Text.Contains(DateTime.Now.ToString("yyyy-MM-dd")), "header clock shows the real local date and advances without a business request");
            var opens = h.Api(h.Right).Opens; var epoch = h.Factory.Sources[h.Right].Epoch;
            h.Peer.Mode = "malformed";
            h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("REVIEW-UNKNOWN"), 1);
            await Until(() => vm.Snapshot?.Operations.Any(o => o.Wcs?.State == StageState.Unknown) == true);
            Page("diagnostics"); await Flush();
            Check(Descendants(window).OfType<Button>().Any(b => b.IsVisible && Equals(b.Content, "去记录核对")) && ((Border)window.FindName("PendingConfigurationBanner")).Visibility == Visibility.Collapsed,
                "an unresolved request exposes record review without a false pending-settings notice");
            var cards = Descendants(window).OfType<Border>().Where(b => AutomationProperties.GetAutomationId(b).StartsWith("DiagnosticCard.", StringComparison.Ordinal)).ToArray();
            Check(cards.Length == 4 && cards.All(b => b.IsVisible), "four native cards expose left camera, right camera, RCS and storage");
            Check(!Descendants(window).OfType<Button>().Any(b => Equals(b.Content, "刷新体检") || Equals(b.Content, "检查并重启…")), "diagnostics uses automatic updates and no always-present restart action");
            Check(Descendants(window).OfType<Expander>().Where(e => e.Header is "详情与原始证据" or "维护工具" or "日志与性能详情").All(e => !e.IsExpanded), "technical evidence and maintenance tools start folded");
            var peerCount = h.Peer.Requests.Count;
            await Task.Delay(5300); await Flush();
            Check(h.Peer.Requests.Count == peerCount && h.Api(h.Right).Opens == opens && h.Factory.Sources[h.Right].Epoch == epoch, "automatic diagnostic polling creates no parcel request or replacement camera owner");
            var tools = Descendants(window).OfType<Expander>().Single(e => Equals(e.Header, "维护工具")); tools.IsExpanded = true; await Flush();
            Check(Descendants(tools).OfType<Button>().Single(b => Equals(b.Content, "取消导出")).Visibility == Visibility.Collapsed && Descendants(tools).OfType<Button>().Any(b => Equals(b.Content, "创建完整备份…")) && Descendants(tools).OfType<Button>().Any(b => Equals(b.Content, "离线维护…")), "maintenance retains backup and offline handoff while hiding idle export cancellation");
            tools.IsExpanded = false;
            foreach (var size in new[] { (1536d, 816d), (800d, 420d) })
            {
                await SetClient(window, size.Item1, size.Item2); await Flush();
                Check(Inside(clock, (Grid)window.FindName("ClientArea")), "clock stays inside the header at " + size.Item1);
                CaptureInternal(evidence, "review-diagnostics-" + size.Item1, (Grid)window.FindName("ClientArea"), lines);
            }
            Page("maintenance"); await vm.RunAsync(vm.EnsureMaintenanceLoadedAsync); await Flush();
            Check(((Border)window.FindName("OperationalStatusBar")).Visibility == Visibility.Collapsed, "daily settings contains no duplicate operational status strip");
            var settings = Descendants(window).OfType<MaintenancePage>().Single();
            Check(!settings.NavigateToParameter("TimeoutMs"), "SM cannot navigate to the misleading generic timeout editor");
            Check(settings.NavigateToParameter(vm.Snapshot!.Configuration.Effective.Wcs!.InstanceId + "/request-timeout-ms"), "actual RCS request timeout remains reachable");
            var timeout = Descendants(settings).OfType<TextBox>().Single(b => AutomationProperties.GetAutomationId(b).EndsWith("/request-timeout-ms", StringComparison.Ordinal));
            Check((AutomationProperties.GetLabeledBy(timeout) as TextBlock)?.Text.StartsWith("取格口请求超时", StringComparison.Ordinal) == true && !(AutomationProperties.GetLabeledBy(timeout) as TextBlock)!.Text.Contains("毫秒"), "timeout label stays concise and the unit belongs beside its input");
            await h.App.ExitManagementModeAsync(); await vm.RefreshManagementModeAsync(); await Flush();
            Check(vm.SelectedPage == "workstation" && cards.All(b => !b.IsVisible), "management expiry closes the diagnostic controls without touching camera ownership");
        }
        finally { window.Close(); }

        await using var factory = new ParcelCapabilityFactory(new(System.IO.Path.Combine(root, "config/sportmaster"), evidence, null, null));
        var draft = new StartupConfigurationDraft(new SportmasterFlow().InitialConfiguration(), factory.Catalog());
        var editor = new ParameterSettingsControl { FirstConfiguration = true, Model = draft.CreateParameterEditor((p, c) => Task.FromResult(Ipc.Application.ParcelFlow.BarcodeFiltering.Select(p, c))) };
        var first = new Window { Content = editor, Width = 1100, Height = 760 }; first.Show();
        try
        {
            Check(editor.ShowPage(3, "RCS 服务"), "first configuration opens the actual RCS step"); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); first.UpdateLayout();
            Check(!Descendants(editor).OfType<ListBox>().Any(b => b.IsVisible), "single-page first-configuration step hides the redundant left rail");
            Check(Descendants(editor).OfType<Button>().Count(b => AutomationProperties.GetItemStatus(b) == "当前步骤") == 1 && Descendants(editor).OfType<Button>().Any(b => AutomationProperties.GetItemStatus(b) == "当前步骤" && b.Content.ToString()!.Contains("4 RCS")), "step bar exposes exactly one highlighted and accessible current step");
            Check(Descendants(editor).OfType<TextBlock>().Any(t => t.Text.StartsWith("请求编号范围（由 RCS 分配）", StringComparison.Ordinal)) && Descendants(editor).OfType<TextBlock>().Any(t => t.Text == "填 RCS 分给本供包台的编号段，不能和其他供包台重叠。"), "request range uses the allocated-range instruction");
            CaptureInternal(evidence, "review-first-rcs", editor, lines);
        }
        finally { first.Close(); }
    }
}

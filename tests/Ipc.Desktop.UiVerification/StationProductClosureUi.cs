using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Assembly;
using Ipc.Platform;

static partial class Program
{
    static async Task VerifyStationProductClosure(string root, string evidence, List<string> lines)
    {
        void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); lines.Add("PASS PC21-WPF " + message); }
        async Task Flush() => await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        async Task Until(Func<bool> ready)
        { for (var i = 0; i < 300; i++) { await Flush(); if (ready()) return; await Task.Delay(25); } throw new TimeoutException("PC21 native state"); }
        await using var h = new ManualSupplyHarness(root, Path.Combine(evidence, "pc21"), deferred: true);
        await h.StartAsync(); await ManagementModeFixture.Authorize(h.App);
        var window = new SupplyStationWindow(h.App, null, "standard"); window.Show(); await Until(() => window.ViewModel.Fresh);
        async Task ClickReady(string label)
        {
            // RunAsync retains ownership through its final snapshot refresh. A page can
            // already be visible while that operation is still correctly fenced.
            await Until(() => window.ViewModel.CanStartOperation);
            var button = Descendants(window).OfType<Button>().First(b => b.IsVisible && Equals(b.Content, label));
            if (!button.IsEnabled) throw new InvalidOperationException("Action is disabled: " + label);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        bool HasText(string value) => Descendants(window).OfType<TextBlock>().Any(t => t.IsVisible && t.Text.Contains(value));
        try
        {
            await SetClient(window, 1366, 768); OpenRetainedStationPageForVerification(window, "statistics"); await Flush(); await Until(() => HasText("识别次数") && HasText("运行中"));
            Check(Descendants(window).OfType<DataGrid>().Any(g => g.IsVisible && g.Columns.Count == 8), "statistics uses actual bounded WPF data grid and explicit fact labels");
            Check(!HasText("本班件数"), "statistics never labels request facts as physical parcel count");
            CaptureInternal(evidence, "pc21-statistics-1366", (Grid)window.FindName("ClientArea"), lines);
            await SetClient(window, 800, 420); await Flush();
            CaptureInternal(evidence, "pc21-statistics-800", (Grid)window.FindName("ClientArea"), lines);
            var settings = Descendants(window).OfType<Expander>().Single(x => Equals(x.Header, "目标与自动维护设置（管理模式）"));
            settings.IsExpanded = true; await Flush();
            Check(settings.IsEnabled, "management mode permits editing timezone, cleanup and local target");
            var target = Descendants(settings).OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "目标取得格口次数每小时");
            var originalTarget = target.Text;
            target.Text = "1234"; window.ConfirmDiscardDrafts = _ => false; window.Close(); await Task.Delay(150); await Flush();
            Check(window.IsVisible && target.Text == "1234", "closing can be cancelled without losing an unsaved local target draft");
            target.Text = originalTarget; window.ConfirmDiscardDrafts = null;

            await h.App.ExitManagementModeAsync(); await window.ViewModel.RefreshManagementModeAsync(); await Flush();
            Check(!settings.IsExpanded && !settings.IsEnabled, "authority revocation immediately collapses and disables policy editing");
            await SetClient(window, 1366, 768); OpenRetainedStationPageForVerification(window, "exceptions"); await Flush(); await Until(() => HasText("格口未确认的包裹勿重复上件"));
            Check(Descendants(window).OfType<Button>().Any(b => Equals(b.Content, "查看详情与核对…")), "exceptions retain operation-specific evidence and guarded recovery entry");
            CaptureInternal(evidence, "pc21-exceptions-1366", (Grid)window.FindName("ClientArea"), lines);
            await ManagementModeFixture.Authorize(h.App); await window.ViewModel.RefreshManagementModeAsync(); await ClickReady("维护中心");
            await Until(() => HasText("自动清理："));
            Check(HasText("恢复备份、换机、修复或卸载") && HasText("创建完整备份"), "maintenance center exposes online tasks and explicit offline handoff");
            Check(!Descendants(window).OfType<Button>().Any(b => b.IsVisible && Equals(b.Content, "处理异常")) && !HasText("自动清理与班次"),
                "maintenance center exposes no hidden-page shortcut");
            await ClickReady("打开诊断"); await Until(() => window.ViewModel.SelectedPage == "diagnostics" && window.ViewModel.CanStartOperation &&
                Descendants(window).OfType<Button>().Any(b => b.IsVisible && b.IsEnabled && Equals(b.Content, "刷新体检"))); await ClickReady("刷新体检");
            await Until(() => HasText("体检只读取现有相机和心跳数据") && HasText("存储剩余") && (HasText("等待 RCS 确认") || HasText("只表示 RCS 可连接") || HasText("组件未就绪")));
            Check(!HasText("RCS：当前可用") && (HasText("等待 RCS 确认") || HasText("只表示 RCS 可连接") || HasText("组件未就绪")), "RCS health summary distinguishes local readiness, remote HTTP confirmation and business acceptance");
            Check(HasText("存储剩余"), "diagnostic health reads actual current storage and confirms passive probe semantics");
            Check(!Descendants(window).OfType<Button>().Any(b => b.IsVisible && Equals(b.Content, "处理异常")),
                "diagnostics exposes no hidden exception-page shortcut");
            CaptureInternal(evidence, "pc21-diagnostics-1366", (Grid)window.FindName("ClientArea"), lines);
            await ClickReady("日志");
            await Until(() => Descendants(window).OfType<SoftwareLogControl>().Any());
            var log = Descendants(window).OfType<SoftwareLogControl>().Single();
            var selectionField = typeof(SoftwareLogControl).GetField("selection", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            await Until(() => selectionField.GetValue(log) is string);
            Check(Descendants(log).OfType<DataGrid>().Any(g => g.ItemsSource is not null), "log page executes its initial bounded query without a manual search action");
            await ClickReady("作业"); await Flush();
            // Historical evidence is reachable from the debug screen; its removed
            // isolation module must not survive in another tab.
            var method = typeof(SupplyStationWindow).GetMethod("Debug_Click", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            method.Invoke(window, [window, new RoutedEventArgs()]); await Flush();
            foreach (var tab in Descendants(window).OfType<TabItem>().Where(t => t.Header?.ToString()?.Contains("历史") == true).ToArray()) tab.IsSelected = true;
            await Flush();
            Check(!Descendants(window).OfType<Button>().Any(b => Equals(b.Content, "确认清场并结束调试") || Equals(b.Content, "开始实机调试") || Equals(b.Content, "开始生产观察")), "historical evidence contains none of the retired manual isolation commands");
            window.Close(); await Flush();
        }
        finally { if (window.IsVisible) window.Close(); }

        var launch = new BusinessLaunch(Path.Combine(evidence, "first-config"), Path.Combine(evidence, "first-data"), null, null);
        await using var factory = new ParcelCapabilityFactory(launch);
        var draft = new StartupConfigurationDraft(new SportmasterFlow().InitialConfiguration(), factory.Catalog());
        var model = draft.CreateParameterEditor((p, codes) => Task.FromResult(BarcodeFiltering.Select(p, codes)));
        var editor = new ParameterSettingsControl { FirstConfiguration = true, Model = model };
        var client = new Grid(); client.Children.Add(editor);
        var first = new Window { Content = client, Width = 1100, Height = 740 }; first.Show(); await Flush();
        try
        {
            var steps = Descendants(first).OfType<Button>().Where(c => AutomationProperties.GetName(c).StartsWith("首次配置第", StringComparison.Ordinal)).ToArray();
            Check(steps.Length == 5 && steps.All(s => s.IsVisible) && editor.PageProgress.StartsWith("第 1 / 5 步"), "first configuration has five visible stage buttons and starts with station information");
            draft.Policy.StationCode = ""; await Flush();
            steps[4].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Until(() => !editor.IsNavigating);
            Check(!editor.IsReviewPage && model.Issues.Count > 0, "jumping to completion cannot bypass invalid earlier stage fields");
            CaptureInternal(evidence, "pc21-first-configuration-1100", client, lines);
        }
        finally { first.Close(); }
    }
}

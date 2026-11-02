using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster.Desktop;

static partial class Program
{
    static async Task VerifyUiSpec24(string root, string evidence, List<string> lines)
    {
        void Check(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); lines.Add("PASS UI24-WPF " + message); }
        async Task Until(Func<bool> valid) { for (var i = 0; i < 400; i++) { await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); if (valid()) return; await Task.Delay(25); } throw new TimeoutException("UI24 native wait"); }
        await using var h = new ManualSupplyHarness(root, Path.Combine(evidence, "ui24"), deferred: true);
        await h.StartAsync(); await ManagementModeFixture.Authorize(h.App);
        var admitted = await h.App.SubmitParcelBarcodeAsync(await h.Command("left", "SYNTH-UI24"), default);
        await CameraRoundHarness.Until(async () => (await h.App.OperationAsync(admitted.OperationId!))?.Wcs?.State == StageState.Accepted);
        var window = new SupplyStationWindow(h.App, null, "standard"); window.Show();
        try
        {
            await Until(() => window.ViewModel.Fresh && window.ViewModel.CanStartOperation);
            foreach (var (width, height) in new[] { (1366, 768), (1092, 614), (800, 420) })
            {
                await SetClient(window, width, height); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
                var badge = (TextBlock)window.FindName("PaceBadge");
                Check(badge.IsVisible && badge.ActualHeight > 0, "pace remains a visible read-only software fact at " + width);
                var cards = Descendants(window).OfType<SupplyStationPosition>().ToArray();
                foreach (var card in cards)
                {
                    var button = (Button)card.FindName("SubmitBarcode");
                    var bounds = button.TransformToAncestor(window).TransformBounds(new Rect(button.RenderSize));
                    Check(button.ActualHeight >= 60 && bounds.Bottom <= window.ActualHeight, "primary touch action retains its height and remains in client at " + width);
                }
                CaptureInternal(evidence, "ui24-work-" + width, (Grid)window.FindName("ClientArea"), lines);
            }
            await SetClient(window, 1366, 768);
            await Until(() => ((TextBlock)window.FindName("PaceBadge")).Text.Contains("本班取得格口 1 次"));
            Check(h.Peer.Requests.Count == 1, "persisted pace reads the one accepted request without sending another");
            var menu = Descendants(window).OfType<ManagementModeMenu>().Single();
            Check(Descendants(menu).OfType<TextBlock>().Any(t => t.Text.StartsWith("管理 · 剩")), "management countdown projects server expiry");
            await window.ViewModel.EnsureMaintenanceLoadedAsync();
            var settings = Descendants(window).OfType<Button>().First(b => b.IsVisible && Equals(b.Content, "设置")); settings.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => window.ViewModel.SelectedPage == "maintenance" && window.ViewModel.HasLoadedConfiguration && window.ViewModel.CanStartOperation);
            var editor = Descendants(window).OfType<ParameterSettingsControl>().Single();
            var model = editor.Model!; var component = model.Components.Single(ParameterOwnership.IsRcs);
            Check(editor.NavigateTo(component.InstanceId + "/Target") && editor.CurrentPageTitle == "站点编号与 RCS", "RCS target shares the station task page without another candidate");
            // The harness intentionally carries old separate request/heartbeat station values.
            // Resolve them explicitly in the real common editor; never weaken its guard.
            Descendants(editor).OfType<TextBox>().Single(b => AutomationProperties.GetAutomationId(b) == component.InstanceId + "/left-station-code").Text = h.Config.StationCode;
            var first = Descendants(editor).OfType<TextBox>().Single(b => AutomationProperties.GetAutomationId(b) == component.InstanceId + "/request-id-base");
            var last = Descendants(editor).OfType<TextBox>().Single(b => AutomationProperties.GetAutomationId(b) == component.InstanceId + "/request-id-count");
            var originalFirst = first.Text; var originalLast = last.Text;
            first.Text = "0"; Check(!model.ValidateFields(), "invalid inclusive start cannot silently save the old valid candidate");
            first.Text = originalFirst; last.Text = originalLast; Check(model.ValidateFields(), "restoring inclusive endpoints restores the one complete valid draft; " + string.Join(";", model.Issues.Select(i => i.Path + "=" + i.Message)) + " endpoints=" + first.Text + ".." + last.Text);
            Check(editor.ShowPage(2, "左相机") && editor.CurrentPageTitle == "左右相机", "legacy navigation resolves to the combined camera page");
            Check(Descendants(editor).OfType<TextBox>().Count(b => AutomationProperties.GetAutomationId(b).EndsWith("/device.ip", StringComparison.Ordinal)) == 2, "both independent camera IP editors are in the same real native page");
            CaptureInternal(evidence, "ui24-cameras-1366", (Grid)window.FindName("ClientArea"), lines);
            await SetClient(window, 800, 420); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); CaptureInternal(evidence, "ui24-cameras-800", (Grid)window.FindName("ClientArea"), lines);
            await SetClient(window, 1366, 768);
            model.Policy.TimeoutMs = (int.Parse(model.Policy.TimeoutMs) + 1).ToString();
            Check(await window.ViewModel.PrepareConfigurationReviewAsync(), "actual edited candidate reaches native save review");
            var review = new ConfigurationSaveReviewWindow(window, window.ViewModel); review.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var changes = Descendants(review).OfType<DataGrid>().Single(g => AutomationProperties.GetName(g) == "本次设置更改");
            Check(changes.Items.OfType<ConfigurationChangeItem>().Any() && changes.Items.OfType<ConfigurationChangeItem>().All(c => c.Activation == "重启后生效"), "populated review classifies the whole connection candidate consistently");
            Check(Descendants(review).OfType<Button>().Any(b => Equals(b.Content, "现场调试")), "change reason has explicit presets and keeps required audit input");
            CaptureInternal(evidence, "ui24-save-review", (FrameworkElement)review.Content, lines); review.Close();
            model.Policy.TimeoutMs = h.Config.TimeoutMs.ToString();
            await window.ShowDetailsAsync(admitted.OperationId!); await Until(() => window.ViewModel.Selected?.Id == admitted.OperationId);
            Check(Descendants(window).OfType<DataGrid>().Any(g => g.Columns.Any(c => Equals(c.Header, "已归档") && c.IsReadOnly)), "archive column is explicit read-only fact");
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Check(Descendants(window).OfType<TextBox>().Any(t => t.IsVisible && AutomationProperties.GetName(t) == "历史完整条码"), "history scanner input is visible without opening advanced filters");
            CaptureInternal(evidence, "ui24-history-1366", (Grid)window.FindName("ClientArea"), lines);
            await SetClient(window, 800, 420); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
            var historyGrid = Descendants(window).OfType<DataGrid>().Single(g => g.Columns.Any(c => Equals(c.Header, "已归档")));
            Check(Descendants(historyGrid).OfType<DataGridRow>().Any(r => r.ActualHeight >= 39 && r.TransformToAncestor(window).TransformBounds(new Rect(r.RenderSize)).Bottom <= window.ActualHeight), "small history viewport preserves an actual complete result row");
            CaptureInternal(evidence, "ui24-history-800", (Grid)window.FindName("ClientArea"), lines);
            await SetClient(window, 1366, 768);
            var link = Descendants(window).OfType<Button>().Single(b => Equals(b.Content, "查看该作业的日志")); link.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => window.ViewModel.SelectedPage == "logs");
            await Until(() => Descendants(window).OfType<DataGrid>().Any(g => g.Items.OfType<SoftwareLogEntry>().Any(e => e.OperationId == admitted.OperationId)));
            Check(Descendants(window).OfType<DataGrid>().SelectMany(g => g.Items.OfType<SoftwareLogEntry>()).All(e => e.OperationId == admitted.OperationId), "record-to-log jump pins the structured original operation rather than a barcode search");
            Check(Descendants(window).OfType<Button>().Any(b => Equals(b.Content, "导出…")), "log export uses one explicit selection entry");
            CaptureInternal(evidence, "ui24-logs-1366", (Grid)window.FindName("ClientArea"), lines);
            await SetClient(window, 800, 420); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); CaptureInternal(evidence, "ui24-logs-800", (Grid)window.FindName("ClientArea"), lines);
            Check(!Descendants(window).OfType<CheckBox>().Any(b => b.IsVisible && AutomationProperties.GetAutomationId(b) == "DebugLoggingMode"), "SM detailed-log switch belongs to diagnostics rather than logs");
        }
        finally { window.ConfirmDiscardDrafts = _ => true; window.Close(); await Task.Delay(100); }
        var desktop = System.Reflection.Assembly.LoadFrom(Environment.GetEnvironmentVariable("IPC_FROZEN_DESKTOP") ?? throw new InvalidOperationException("same-build desktop required"));
        var repairType = desktop.GetType("Ipc.Desktop.StartupRepairWindow", true)!;
        var launch = new Ipc.Platform.BusinessLaunch(Path.Combine(evidence, "scene-config"), Path.Combine(evidence, "scene-data"), null, null);
        ManagementModeFixture.AuthorizeRoot(launch.DataRoot);
        foreach (var recovery in new[] { false, true })
        {
            var scene = (Window)Activator.CreateInstance(repairType, [launch, null, false])!;
            repairType.GetProperty("SceneOnly")!.SetValue(scene, true);
            repairType.GetProperty("OpenRecoveryScenario")!.SetValue(scene, recovery);
            scene.Show();
            try
            {
                await Until(() => Descendants(scene).OfType<TabControl>().Any(t => t.Name == "StartupTasks")); scene.UpdateLayout();
                var tabs = Descendants(scene).OfType<TabControl>().Single(t => t.Name == "StartupTasks");
                Check(tabs.Items.OfType<TabItem>().Count(t => t.IsEnabled) == (recovery ? 1 : 2) && !Descendants(tabs).OfType<System.Windows.Controls.Primitives.TabPanel>().Any(), "offline scene exposes only its authorized route without the five expert tab headers: recovery=" + recovery);
                CaptureInternal(evidence, recovery ? "ui24-offline-recovery" : "ui24-offline-check", (FrameworkElement)scene.Content, lines);
            }
            finally { scene.Close(); }
        }
        var firstWindow = (Window)Activator.CreateInstance(repairType, [launch, null, true])!; firstWindow.Show();
        try
        {
            await Until(() => Descendants(firstWindow).OfType<ParameterSettingsControl>().Any());
            var setup = Descendants(firstWindow).OfType<ParameterSettingsControl>().Single(); setup.ShowReview(); firstWindow.UpdateLayout();
            Check(Descendants(setup).OfType<Button>().Count(b => b.Content is TextBlock t && t.Text.Contains("返回修改")) == 4, "first-run summary returns to each of the four actual candidate sections");
            Check(!Directory.Exists(launch.ConfigurationDirectory) || !File.Exists(Path.Combine(launch.ConfigurationDirectory, "business.json")), "reviewing draft and offline scenes never creates a saved configuration or starts devices");
            CaptureInternal(evidence, "ui24-first-run-review", (FrameworkElement)firstWindow.Content, lines);
        }
        finally { firstWindow.Close(); }

    }
}

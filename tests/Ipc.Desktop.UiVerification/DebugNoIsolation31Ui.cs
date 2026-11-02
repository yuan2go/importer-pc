using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Platform;

static partial class Program
{
    static async Task VerifyDebugNoIsolation31(string root, string evidence, List<string> lines)
    {
        void Check(bool ok, string reason) { if (!ok) throw new InvalidOperationException(reason); lines.Add("PASS DEBUG31-WPF " + reason); }
        await using var h = new ManualSupplyHarness(root, System.IO.Path.Combine(evidence, "legacy-save"), true, mode: RunMode.Simulation);
        ExtensionStore.AtomicWrite(System.IO.Path.Combine(h.Launch.ConfigurationDirectory, "business.json"), h.Config with { StationCode = "MANUAL-STATION" });
        await h.StartAsync(); await h.App.DisposeAsync();
        var directory = System.IO.Path.Combine(h.Launch.DataRoot, h.Config.Mode.ToString(), h.Config.StationId, h.Config.ApplicationId);
        using (var repository = new ParcelRepository(directory, "sportmaster"))
            repository.Commit(null, "CameraIsolationStarted", Wire.Encode(new { Id = Wire.Id(), Reason = "Synthetic legacy isolation across restart" }), paused: true);
        await h.StartAsync(); await ManagementModeFixture.Authorize(h.App);
        var window = new SupplyStationWindow(h.App, null, "standard") { ConfirmDiscardDrafts = _ => true }; window.Show();
        var vm = window.ViewModel;
        async Task Flush() { await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout(); }
        async Task Until(Func<bool> ready)
        { for (var n = 0; n < 200; n++) { await vm.RefreshAsync(); await Flush(); if (ready()) return; await Task.Delay(25); } throw new TimeoutException("DEBUG31 native state did not converge"); }
        ConfigurationSaveReviewWindow? review = null;
        try
        {
            await Until(() => vm.Fresh && vm.IsManagementMode); await SetClient(window, 1366, 768);
            Click(window, "参数设置");
            await Until(() => vm.HasLoadedConfiguration && !vm.Busy && Descendants(window).OfType<ParameterSettingsControl>().Any());
            h.Factory.RcsOffline = true;
            await Until(() => vm.Snapshot!.Health.Any(x => x.InstanceId == h.Config.Wcs!.InstanceId && !x.Ready));
            var runtimeStatus = vm.ParameterConnection(new(h.Config.Wcs!.InstanceId, null, "RCS 服务"));
            Check(runtimeStatus.Notice.Contains("核对当前运行地址") && !runtimeStatus.Notice.Contains("RCS client") &&
                vm.Snapshot!.Health.Single(x => x.InstanceId == h.Config.Wcs!.InstanceId).Detail.Contains("RCS client"),
                "offline service guidance uses Chinese and retains the original adapter detail in diagnostic facts");
            var editor = Descendants(window).OfType<ParameterSettingsControl>().Single();
            editor.NavigateTo(h.Config.Wcs!.InstanceId + "/Target"); await Flush();
            Check(!Descendants(editor).OfType<TextBox>().Any(t => t.Text.Contains("StationCode") || t.Text.Contains("left-bcr-code") || t.Text.Contains("right-bcr-code")),
                "expanded parameter requirements never expose internal station or camera property keys");
            var endpoint = "http://127.0.0.1:9/rcs/sorter/api";
            vm.CapabilityDrafts.Single(c => c.Kind == "integration").Target = endpoint;
            vm.ConfigurationReason = "合成RCS地址变更";
            var prepared = await vm.PrepareConfigurationReviewAsync();
            Check(prepared, "actual RCS endpoint candidate validates after legacy isolation recovery: " + vm.Message + " · " + vm.ParameterConfigurationSummary + " · " + string.Join("; ", vm.ParameterEditor!.Issues));
            Check(vm.ReviewedConfigurationChanges.All(c => System.Text.RegularExpressions.Regex.IsMatch(c.Label, "[\\p{IsCJKUnifiedIdeographs}]")) &&
                vm.ReviewedConfigurationChanges.Where(c => c.TechnicalPath.StartsWith("Reader.Parameters.")).All(c => c.Object == "左相机" && !c.Label.Contains("Parameters")),
                "all review labels including legacy device aliases use Chinese while exact technical paths remain internal");
            review = new(window, vm); review.Show(); await Flush();
            var save = Descendants(review).OfType<Button>().Single(b => b.Name == "ConfirmSaveChanges");
            foreach (var size in new[] { (880d, 740d), (800d, 560d), (665d, 420d) })
            {
                review.Width = size.Item1; review.Height = size.Item2; review.UpdateLayout(); await Flush();
                Check(save.IsEnabled && vm.IsManagementMode && vm.CanConfigure,
                    "authorized native save remains enabled with a real persisted old isolation at " + size.Item1);
                Check(!Descendants(review).OfType<Expander>().Any(e => e.IsVisible && Equals(e.Header, "需要时验证管理权限")) &&
                    !Descendants(review).OfType<TextBlock>().Any(t => t.IsVisible && t.Text.Contains("进入管理模式后即可保存")),
                    "save review never asks an already authorized operator to log in again");
                var client = (FrameworkElement)review.Content;
                var bounds = save.TransformToAncestor(client).TransformBounds(new Rect(save.RenderSize));
                Check(save.ActualHeight >= 44 && bounds.Left >= 0 && bounds.Right <= client.ActualWidth && bounds.Bottom <= client.ActualHeight,
                    "primary save remains visible and touchable in the review viewport");
                CaptureInternal(evidence, "debug31-save-review-" + size.Item1, review, lines);
            }
            var row = Descendants(review).OfType<DataGridRow>().Single(r => r.DataContext is ConfigurationChangeItem { Label: "连接目标" });
            Check(AutomationProperties.GetName(row) == "连接目标" && Equals(row.ToolTip, "连接目标"),
                "operator tooltip and accessibility name never expose the technical property path");
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => vm.Snapshot?.Configuration.Pending?.Wcs?.Target == endpoint && !vm.Busy);
            Check(vm.ConfigurationSubmissionOutcome == "PendingRestart" && vm.Snapshot!.Configuration.Effective.Wcs!.Target == h.Peer.Endpoint && h.Peer.Requests.IsEmpty,
                "native confirm actually saves Pending while retaining old Effective and sending no RCS request");
            Check(vm.ConfigurationReason == "合成RCS地址变更", "the operator change reason survives successful save");
            CaptureInternal(evidence, "debug31-save-confirmed", review, lines);
        }
        finally { review?.Close(); window.Close(); await Flush(); }
    }
}

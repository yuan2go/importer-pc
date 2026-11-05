using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Platform;

static partial class Program
{
    static async Task VerifyRcsNoDestinationUi(string root, string evidence, List<string> lines)
    {
        void Check(bool value, string text) { if (!value) throw new InvalidOperationException(text); lines.Add("PASS NO-DEST-UI " + text); }
        async Task Until(Func<bool> ready)
        {
            for (int i = 0; i < 250; i++) { await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); if (ready()) return; await Task.Delay(25); }
            throw new TimeoutException("RCS no-destination UI did not settle.");
        }
        await using (var h = new ManualSupplyHarness(root, Path.Combine(evidence, "no-dest-settings"), deferred: true))
        {
            ExtensionStore.AtomicWrite(Path.Combine(h.Launch.ConfigurationDirectory, "business.json"), h.Config with { StationCode = "MANUAL-STATION" });
            await h.StartAsync(); await ManagementModeFixture.Authorize(h.App);
            var window = new SupplyStationWindow(h.App, null, "standard") { ConfirmDiscardDrafts = _ => true };
            window.Show(); await window.ViewModel.RefreshAsync(); await SetClient(window, 1366, 768);
            var vm = window.ViewModel;
            window.ShowPositionDiagnostics(vm.Left);
            await Until(() => System.Windows.Application.Current.Windows.OfType<ManagementCenterWindow>().Any(w => w.IsVisible));
            var center = System.Windows.Application.Current.Windows.OfType<ManagementCenterWindow>().Single();
            Click(center, "设置"); await Until(() => vm.HasLoadedConfiguration && !vm.Busy);
            var editor = Descendants(center).OfType<ParameterSettingsControl>().Single();
            var rcs = editor.Model!.Components.Single(ParameterOwnership.IsRcs);
            var path = rcs.InstanceId + "/no-dest-to-exp";
            Check(editor.NavigateTo(path), "setting is directly reachable on the station/RCS page"); center.UpdateLayout();
            var choice = Descendants(editor).OfType<ComboBox>().Single(c => AutomationProperties.GetAutomationId(c) == path);
            for (var parent = System.Windows.Media.VisualTreeHelper.GetParent(choice); parent is not null; parent = System.Windows.Media.VisualTreeHelper.GetParent(parent))
                if (parent is Border card) { card.BringIntoView(); break; }
            center.UpdateLayout();
            var labels = choice.Items.Cast<object>().Select(i => (string)i.GetType().GetProperty("Label")!.GetValue(i)!).ToArray();
            Check(labels.SequenceEqual(new[] { "送往异常口（默认）", "留在原位，人工处理" }) && Equals(choice.SelectedValue, "true"), "new/omitted setting selects the default exception-chute behavior with clear Chinese options");
            Check(choice.IsVisible && choice.ActualHeight >= 44 && AutomationProperties.GetHelpText(choice).Contains("小车不动"), "setting remains visible outside folded protocol details and explains the stationary vehicle");
            CaptureInternal(evidence, "no-destination-default", (Grid)center.FindName("ClientArea"), lines);
            choice.SelectedValue = "false"; center.UpdateLayout();
            Check(vm.HasUnsavedConfiguration && vm.BuildStructuredConfiguration().Wcs!.Parameters["no-dest-to-exp"] == "false", "selecting manual handling changes the shared configuration draft");
            var change = vm.GetConfigurationChangeItems().Single(c => c.Label == "无可用格口时");
            Check(change.Before.Contains("送往异常口") && change.After.Contains("人工处理"), "save review describes both behaviors without exposing boolean literals");
            CaptureInternal(evidence, "no-destination-manual", (Grid)center.FindName("ClientArea"), lines);
            Check(await vm.PrepareConfigurationReviewAsync(), "full configuration review accepts the selected policy: " + string.Join("; ", editor.Model.Issues.Select(i => i.Path + " " + i.Message)) + " " + vm.Message);
            vm.ConfigurationReason = "合成验证：无可用格口时改为留在原位，人工处理";
            await vm.SaveCapabilitiesAsync();
            Check((vm.Snapshot!.Configuration.Pending ?? vm.Snapshot.Configuration.Effective).Wcs!.Parameters.GetValueOrDefault("no-dest-to-exp") == "false", "CAS save retains false in the configuration book: " + vm.Message);
            await SetClient(center, 800, 420); editor.NavigateTo(path); center.UpdateLayout();
            Check(choice.IsVisible && choice.ActualWidth > 100, "small management window retains the editable selector");
            CaptureInternal(evidence, "no-destination-settings-800", (Grid)center.FindName("ClientArea"), lines);
            center.Close(); await Until(() => !center.IsVisible); window.Close(); await Until(() => !window.IsVisible);
        }
        await using (var h = new ManualSupplyHarness(root, Path.Combine(evidence, "no-dest-result"), deferred: true))
        {
            var configuration = h.Config with { Wcs = h.Config.Wcs! with { Parameters = new(h.Config.Wcs!.Parameters) { ["no-dest-to-exp"] = "false" } } };
            ExtensionStore.AtomicWrite(Path.Combine(h.Launch.ConfigurationDirectory, "business.json"), configuration);
            h.Peer.Mode = "no-destination";
            await h.StartAsync();
            var window = new SupplyStationWindow(h.App, null, "standard") { ConfirmDiscardDrafts = _ => true };
            window.Show(); await SetClient(window, 1366, 768);
            var receipt = await h.App.SubmitParcelBarcodeAsync(await h.Command("left", "SYNTH-NO-DEST-UI"), default);
            await Until(() => window.ViewModel.Left.Operation?.Id == receipt.OperationId && window.ViewModel.Left.Operation?.State == ParcelState.WcsRejected);
            var position = window.ViewModel.Left;
            window.UpdateLayout();
            CaptureInternal(evidence, "no-destination-workstation", (Grid)window.FindName("ClientArea"), lines);
            var text = Descendants(window).Select(e => e switch { TextBlock t when t.IsVisible => t.Text, TextBox t when t.IsVisible => t.Text, _ => "" });
            Check(text.Any(t => t.Contains("合成异常：未找到可用格口")) && position.NextStep.Contains("取下包裹后人工判断") && !position.HasUncertainOriginal && !position.SlotAvailable,
                "workstation displays the original msg and manual removal instruction as a confirmed rejection: " + position.NextStep);
            window.Close(); await Until(() => !window.IsVisible);
        }
    }
}

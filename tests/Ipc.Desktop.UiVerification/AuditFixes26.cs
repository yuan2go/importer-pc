using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Platform;

static partial class Program
{
    static async Task VerifyAuditFixes26(string root, string evidence, List<string> lines)
    {
        void Check(bool ok, string text) { if (!ok) throw new InvalidDataException(text); lines.Add("PASS AUD26 " + text); }
        foreach (var code in new[] { "NoMatchingCode", "ExcludedCodes", "AmbiguousCodes", "IncompleteCandidates", "FilterTimeout", "InvalidPattern", "NoCode", "MultipleCodes", "InvalidCode", "JoinedCodeTooLong" })
        {
            var source = code + "; no operation or external intent created";
            var display = ChineseInterfaceText.Notice(source);
            Check(!display.Contains("no operation") && display.Contains("本件未发送"), "known local selection receipt is actionable Chinese: " + code);
        }
        const string external = "RCS_CUSTOM: NoMatchingCode; original remote identity";
        Check(ChineseInterfaceText.Notice(external) == external, "remote identifiers and unknown receipt content are not rewritten");
        Check(ChineseInterfaceText.Notice("BackupCreated · Consistent database plus configuration/receipt custody; COMPLETED.json identifies the whole recovery set").Contains("数据库和同名恢复目录"), "complete backup receipt explains the whole recovery set");
        Check(!ChineseInterfaceText.ParameterMessage("integration synthetic zone id: invalid value (value omitted)").Contains("invalid value"), "first-run validation summary does not expose raw field failure text");
        await using var h = new ManualSupplyHarness(root, Path.Combine(evidence, "activation26"), true);
        h.Config.Wcs!.Parameters!["request-timeout-ms"] = "1000";
        ExtensionStore.AtomicWrite(Path.Combine(h.Launch.ConfigurationDirectory, "business.json"), h.Config);
        await h.StartAsync(); await ManagementModeFixture.Authorize(h.App);
        h.Peer.Mode = "malformed";
        var admitted = await h.App.SubmitParcelBarcodeAsync(await h.Command("left", "SYNTH-AUD26-UNKNOWN"), default);
        await CameraRoundHarness.Until(async () => (await h.App.OperationAsync(admitted.OperationId!))?.Wcs?.State == StageState.Unknown);
        var before = await h.App.SnapshotAsync(); var original = await h.App.OperationAsync(admitted.OperationId!);
        var next = before.Configuration.Effective with { Wcs = before.Configuration.Effective.Wcs! with { Parameters = new(before.Configuration.Effective.Wcs!.Parameters!) { ["request-timeout-ms"] = "1200" } } };
        var saved = await h.App.StageConfigurationAsync(before.Configuration.Revision, next, "合成生效失败验证");
        Check(saved.Status == "PendingRestart", "unresolved original does not turn connection save into premature activation");
        await h.App.DisposeAsync(); await h.StartAsync(ready: false);
        var after = await h.App.SnapshotAsync();
        Check(after.Configuration.Status == "ActivationFailed" && after.Configuration.Effective.Wcs!.Parameters!["request-timeout-ms"] == "1000" && after.Configuration.Pending!.Wcs!.Parameters!["request-timeout-ms"] == "1200", "actual original responsibility guard retains old Effective and saved Pending on failed restart");
        Check(Wire.Encode((await h.App.OperationAsync(admitted.OperationId!))!.Wcs!.Request) == Wire.Encode(original!.Wcs!.Request) && h.Peer.Requests.Count == 1, "restart preserves original identity without resend");
        var window = new SupplyStationWindow(h.App, null, "standard"); window.Show();
        try
        {
            for (var i = 0; i < 200 && !window.ViewModel.Fresh; i++) { await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); await Task.Delay(25); }
            var text = window.ViewModel.ConfigurationActivationOutcome;
            Check(text.Contains("设置未生效") && text.Contains("1000") && text.Contains("1200") && text.Contains("重新保存") && text.Contains("异常处理"), "failure-first presentation includes running/pending values and actionable reconciliation/restaging");
            Check(window.ViewModel.DiagnosticConfiguration == text && window.ViewModel.ConfigurationSavedStatusText == text, "diagnostics and settings share the same failure-first outcome");
            foreach (var size in new[] { (1366,768), (1092,614), (800,420) })
            { await SetClient(window,size.Item1,size.Item2); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                foreach (var position in Descendants(window).OfType<SupplyStationPosition>())
                { var submit = (Button)position.FindName("SubmitBarcode"); var client = (Grid)window.FindName("ClientArea"); var bounds = submit.TransformToAncestor(client).TransformBounds(new Rect(submit.RenderSize)); Check(submit.ActualHeight >= 60 && bounds.Bottom <= client.ActualHeight, "activation notice preserves primary touch action in viewport at " + size.Item1); }
                CaptureInternal(evidence,"audit26-activation-failed-"+size.Item1,(Grid)window.FindName("ClientArea"),lines); }
            await SetClient(window,1366,768);
            Check(window.ViewModel.ConfigurationActivationActionText == "查看生效问题…" && ((Button)window.FindName("ActivationOutcomeAction")).IsEnabled, "failed activation offers inspection instead of a misleading disabled restart");
            await ManagementModeFixture.Authorize(h.App); await window.ViewModel.RefreshManagementModeAsync();
            ((Button)window.FindName("ActivationOutcomeAction")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (var i=0;i<200 && window.ViewModel.SelectedPage!="diagnostics";i++) { await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); await Task.Delay(25); }
            Check(window.ViewModel.SelectedPage == "diagnostics" && h.Peer.Requests.Count == 1, "failure inspection reaches authorized diagnostics without restarting or resending");
        }
        finally { window.ConfirmDiscardDrafts = _ => true; window.Close(); }
    }
}

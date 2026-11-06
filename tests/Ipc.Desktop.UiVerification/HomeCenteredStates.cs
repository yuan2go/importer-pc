using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Platform;
using Ipc.Presentation.Wpf;

static partial class Program
{
    static async Task VerifyHomeCenteredStates(string root, string evidence, List<string> lines)
    {
        void Check(bool yes, string message) { if (!yes) throw new InvalidOperationException(message); lines.Add("PASS HOME-CENTER " + message); }
        await using var h = new CameraRoundHarness(root, Path.Combine(evidence, "home"), heartbeatSeconds: 1);
        h.Peer.ReplyVehicleStates = true; await h.StartAsync(); await h.WaitForCameraAdmissionAsync();
        var application = DispatchProxy.Create<IHomeStateUiFixture, ProjectionProbe>();
        var probe = (ProjectionProbe)(object)application; probe.Inner = h.App;
        // Display-only Production wording with wholly synthetic ports and data.
        probe.Project = snapshot => snapshot with { Configuration = snapshot.Configuration with { Effective = snapshot.Configuration.Effective with { Mode = RunMode.Production } } };
        var stops = 0; var stopAttempts = 0;
        var window = new SupplyStationWindow(application, null, "standard", async () =>
        {
            if (++stopAttempts == 1) throw new System.IO.IOException("Synthetic owner release not confirmed");
            await h.App.StopAsync(default); stops++;
        }) { ConfirmOwnerExit = _ => true, ConfirmDiscardDrafts = _ => true }; window.Show();
        var vm = window.ViewModel; var client = (Grid)window.FindName("ClientArea");
        async Task Flush() { await vm.RefreshAsync(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout(); }
        async Task Until(Func<bool> ready, bool refresh = true)
        {
            for (var i = 0; i < 200; i++)
            {
                if (refresh) await Flush(); else await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                if (ready()) return; await Task.Delay(25);
            }
            throw new TimeoutException("Home state did not settle: " + vm.Left.CenteredStateTitle + "; " + vm.Left.DisplayAttentionReason);
        }
        try
        {
            await SetClient(window, 1366, 768); await Until(() => vm.Fresh && vm.RcsState == SupplyStationViewModel.RcsConnectionState.Online);
            var cards = Descendants(window).OfType<SupplyStationPosition>().ToArray();
            var left = cards.Single(c => ReferenceEquals(c.DataContext, vm.Left)); var right = cards.Single(c => ReferenceEquals(c.DataContext, vm.Right));
            var entry = (TextBox)left.FindName("BarcodeEntry"); var submit = (Button)left.FindName("SubmitBarcode");
            var center = (Border)left.FindName("CenteredSupplyPanel"); var result = (Border)left.FindName("ResultPanel");
            Check(center.IsVisible && vm.Left.CenteredStateTitle == "等待投包" && vm.Left.CenteredStateForeground == OperatorTokens.Success,
                "ready camera shows a green centered waiting title on the neutral result surface");
            var content = (StackPanel)left.FindName("CenteredSupplyContent");
            Check(Math.Abs(content.TranslatePoint(new Point(0, content.ActualHeight / 2), result).Y - result.ActualHeight / 2) <= 2,
                "the complete title and instruction group is vertically centered");
            Check(!((DockPanel)left.FindName("DetailedStatus")).IsVisible && !((TextBlock)left.FindName("NextStep")).IsVisible &&
                !((Button)left.FindName("CenteredEnableSupply")).IsVisible, "waiting has one instruction group and no start button or scattered status copy");
            var snapshot = await h.App.SnapshotAsync();
            await h.App.SetPositionEnabledAsync(new(Wire.Id(), snapshot.RuntimeId, "right", snapshot.Positions!.Single(p => p.Id == "right").SupplyControl!.Revision, false), default);
            await Until(() => vm.Right.SupplyDisabled);
            Check(((Border)right.FindName("CenteredSupplyPanel")).IsVisible && vm.Right.CenteredStateTitle == "已停用" &&
                ((Button)right.FindName("CenteredEnableSupply")).IsVisible, "disabled position uses the same centered structure and retains its enable action");
            Click(window, "收起菜单"); await Flush();
            var navigation = (Border)window.FindName("DailyNavigation"); var toggle = (Button)window.FindName("NavigationToggle");
            var handle = (Border)toggle.Template.FindName("NavigationHandle", toggle);
            var management = (Button)window.FindName("ManagementCenterEntry");
            lines.Add($"GEOMETRY rail={navigation.ActualWidth}; entry={management.ActualWidth}x{management.ActualHeight}; icon={Descendants(management).OfType<System.Windows.Shapes.Path>().Single().ActualWidth}; collapsed={window.IsNavigationCollapsed}");
            CaptureInternal(evidence, "home-centered-waiting", client, lines);
            Check(navigation.ActualWidth == 52 && management.ActualWidth == 44 && management.ActualHeight >= 44 &&
                Descendants(management).OfType<System.Windows.Shapes.Path>().Single().Width == 18 &&
                Descendants(management).OfType<System.Windows.Shapes.Path>().Single().ActualWidth <= 19,
                "collapsed rail has smaller icons and selection backgrounds while keeping usable targets");
            Check(Math.Abs(handle.ActualWidth - 18) <= 1 && handle.ActualHeight == 36 && toggle.FocusVisualStyle is null,
                "edge handle is visually small and does not inherit a large blue focus frame");
            PresentationPreferences.SaveWindowMode("sportmaster", PresentationPreferences.WindowOpeningMode.FullScreen); await Flush();
            var exit = (Button)window.FindName("ExitFullScreen");
            Check(exit.IsVisible && exit.ActualWidth >= 44 && exit.TranslatePoint(new Point(), client).X >
                ((Button)window.FindName("MinimizeFullScreen")).TranslatePoint(new Point(), client).X, "full-screen exit X sits beside minimize with a usable target");
            CaptureInternal(evidence, "home-centered-waiting", client, lines);
            entry.Text = "SYNTH-RCS-BARCODE"; h.Peer.HeartbeatUnavailable = true;
            await Until(() => vm.RcsState == SupplyStationViewModel.RcsConnectionState.Offline && vm.Left.CenteredStateTitle == "暂不能投包");
            Check(center.IsVisible && vm.Left.CenteredStateForeground == OperatorTokens.Danger && vm.Left.ResultBackground == OperatorTokens.Neutral,
                "RCS outage shows a dark red centered title without a red result background");
            Check(entry.Text == "SYNTH-RCS-BARCODE" && !submit.IsEnabled && vm.Left.CenteredStateInstruction == "请检查连接，恢复后再提交",
                "offline state retains the typed barcode, disables submit and directs the operator to restore RCS");
            Check(!((TextBlock)left.FindName("NextStep")).IsVisible && !((DockPanel)left.FindName("DetailedStatus")).IsVisible,
                "offline state hides the old rescan instruction and duplicate corner status");
            CaptureInternal(evidence, "home-centered-offline", client, lines);
            PresentationPreferences.SaveWindowMode("sportmaster", PresentationPreferences.WindowOpeningMode.Window);
            var feedback = (TextBlock)left.FindName("InputFeedback");
            foreach (var size in new[] { (1366d, 768d), (800d, 420d) })
            {
                await SetClient(window, size.Item1, size.Item2); await Flush();
                feedback.Text = "合成提交提示：当前不能提交。\n请检查连接，恢复后再提交。";
                feedback.Visibility = Visibility.Visible; window.UpdateLayout();
                var otherSubmit = (Button)right.FindName("SubmitBarcode");
                var footer = (Grid)left.FindName("PositionDeviceFooter"); var otherFooter = (Grid)right.FindName("PositionDeviceFooter");
                Check(Math.Abs(submit.TranslatePoint(new Point(), client).Y - otherSubmit.TranslatePoint(new Point(), client).Y) <= 1 &&
                    Math.Abs(submit.ActualHeight - otherSubmit.ActualHeight) <= 1 &&
                    Math.Abs(footer.TranslatePoint(new Point(), client).Y - otherFooter.TranslatePoint(new Point(), client).Y) <= 1,
                    "one-sided multiline feedback keeps buttons and device information aligned at " + size.Item1 + "x" + size.Item2);
                CaptureInternal(evidence, "home-feedback-aligned-" + size.Item1, client, lines);
                feedback.Text = ""; feedback.ClearValue(UIElement.VisibilityProperty); window.UpdateLayout();
                Check(Math.Abs(submit.TranslatePoint(new Point(), client).Y - otherSubmit.TranslatePoint(new Point(), client).Y) <= 1,
                    "clearing input feedback preserves the shared button row at " + size.Item1 + "x" + size.Item2);
            }
            await SetClient(window, 800, 420); await Flush();
            Check(Inside(submit, client) && Inside((Button)right.FindName("CenteredEnableSupply"), client),
                "submission and disabled enable action stay reachable in the smallest supported viewport");
            CaptureInternal(evidence, "home-centered-small", client, lines);
            h.Peer.HeartbeatUnavailable = false;
            await SetClient(window, 1366, 768); await Until(() => vm.Left.CanSubmitBarcode && submit.IsEnabled);
            Check(entry.Text == "SYNTH-RCS-BARCODE", "RCS recovery keeps the original input ready to submit");
            submit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Until(() => vm.Left.ShowConfirmedTask && !vm.Busy);
            Check(!center.IsVisible && vm.Left.Slot == "SYNTH-CHUTE", "a confirmed chute keeps its full result instead of the waiting panel");
            h.Peer.HeartbeatUnavailable = true; await Until(() => vm.RcsState == SupplyStationViewModel.RcsConnectionState.Offline);
            Check(!center.IsVisible && vm.Left.ShowConfirmedTask, "a later RCS outage never hides a confirmed current parcel result");
            PresentationPreferences.SaveWindowMode("sportmaster", PresentationPreferences.WindowOpeningMode.FullScreen); await Flush();
            exit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => stopAttempts == 1 && window.IsEnabled && vm.Message.Contains("停止未完成"), refresh: false);
            Check(window.IsVisible && stops == 0 && exit.IsEnabled, "failed release keeps the window open with a reason and allows the exit X to retry");
            exit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => stops == 1 && !window.IsVisible, refresh: false);
            Check(stops == 1 && !window.IsVisible, "exit X follows the existing owner stop and resource release flow exactly once");
        }
        finally { if (window.IsVisible) window.Close(); }
    }
}

public interface IHomeStateUiFixture : IParcelApplication, IParcelWorkstation, IStationPositionControl, IWorkstationAdministration { }

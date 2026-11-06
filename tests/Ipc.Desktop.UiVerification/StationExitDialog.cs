using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Application.Sportmaster.Desktop;

static partial class Program
{
    static async Task VerifyStationExitDialog(string root, string evidence, List<string> lines)
    {
        void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); lines.Add("PASS STATION-EXIT " + message); }
        await using var h = new CameraRoundHarness(root, Path.Combine(evidence, "exit"));
        await h.StartAsync(); await h.WaitForCameraAdmissionAsync();
        var application = DispatchProxy.Create<IHomeStateUiFixture, ProjectionProbe>();
        var projection = (ProjectionProbe)(object)application; projection.Inner = h.App;
        var pending = false; var hidden = false; var unknown = false;
        projection.Project = snapshot => snapshot with
        {
            HasPendingConfiguration = hidden ? null : pending,
            Operations = unknown ? snapshot.Operations.Select(o => o with { Wcs = o.Wcs is { } wcs ? wcs with { State = StageState.Unknown } : null }).ToArray() : snapshot.Operations
        };
        var attempts = 0; var stopped = 0;
        var window = new SupplyStationWindow(application, null, "standard", async () =>
        {
            if (++attempts == 1) throw new IOException("Synthetic retained camera handle");
            await h.App.StopAsync(default); stopped++;
        }) { ConfirmDiscardDrafts = _ => true };
        window.Show();
        async Task Until(Func<bool> ready)
        {
            for (var i = 0; i < 240; i++) { await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); if (ready()) return; await Task.Delay(25); }
            throw new TimeoutException("Station exit dialog did not settle");
        }
        async Task Confirm(Action<SupplyStationExitWindow> action)
        {
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _ = window.Dispatcher.BeginInvoke(new Action(async () =>
            {
                SupplyStationExitWindow? dialog = null;
                try
                {
                    await Until(() => System.Windows.Application.Current.Windows.OfType<SupplyStationExitWindow>().Any(w => w.IsVisible));
                    dialog = System.Windows.Application.Current.Windows.OfType<SupplyStationExitWindow>().Single(w => w.IsVisible);
                    dialog.UpdateLayout(); action(dialog); done.SetResult();
                }
                catch (Exception error) { dialog?.Close(); done.SetException(error); }
            }));
            window.Close(); await done.Task;
        }
        Button Button(SupplyStationExitWindow dialog, string name) => (Button)dialog.FindName(name);
        void ClickButton(SupplyStationExitWindow dialog, string name) => Button(dialog, name).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        string Text(SupplyStationExitWindow dialog) => string.Join("\n", Descendants(dialog).OfType<TextBlock>().Select(t => t.Text));
        try
        {
            await SetClient(window, 1366, 768); await window.ViewModel.RefreshAsync();
            await Confirm(dialog =>
            {
                Check(dialog.ActualWidth == 580 && dialog.ActualHeight == 340, "normal exit uses a compact 580 by 340 DIP native dialog");
                Check(dialog.Owner == window && !window.IsEnabled, "the owner is disabled while its confirmation is open");
                Check(Button(dialog, "CancelExitButton").IsKeyboardFocused && Button(dialog, "CancelExitButton").IsDefault,
                    "cancel receives keyboard focus and is the default action");
                Check(Text(dialog).Contains("确认退出供包台？") && Text(dialog).Contains("待生效配置：无"), "heading and effective pending-configuration status are readable");
                var body = (ScrollViewer)dialog.FindName("ExitBodyScroll");
                Check(body.ScrollableHeight < 1, "normal-size dialog shows all instructions without scrolling");
                var cancel = Button(dialog, "CancelExitButton"); var next = Button(dialog, "ContinueExitButton");
                Check(cancel.ActualHeight == 44 && next.ActualHeight == 44 && next.ActualWidth < 130, "compact actions retain a 44 DIP target");
                Check(next.Background == dialog.FindResource("Ink") && next.Foreground == dialog.FindResource("Surface"), "continue uses the shared dark gray and white theme brushes");
                CaptureInternal(evidence, "station-exit-compact", dialog, lines);
                dialog.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(dialog), Environment.TickCount, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            });
            await Until(() => window.IsEnabled);
            Check(attempts == 0 && window.IsVisible, "Escape cancels without calling owner stop");
            await Confirm(dialog => ClickButton(dialog, "CancelExitX")); await Until(() => window.IsEnabled);
            Check(attempts == 0 && window.IsVisible, "top-right X cancels without closing the station");
            await Confirm(dialog => ClickButton(dialog, "CancelExitButton")); await Until(() => window.IsEnabled);
            Check(attempts == 0, "cancel button also leaves the original runtime running");

            // Create one real synthetic operation, then project an unknown response for display only.
            h.Peer.HeartbeatUnavailable = false;
            await h.WaitForCameraAdmissionAsync();
            await window.ViewModel.RefreshAsync();
            await Until(() => window.ViewModel.Left.CanSubmitBarcode);
            var left = Descendants(window).OfType<SupplyStationPosition>().Single(c => ReferenceEquals(c.DataContext, window.ViewModel.Left));
            ((TextBox)left.FindName("BarcodeEntry")).Text = "SYNTH-EXIT-PARCEL";
            ((Button)left.FindName("SubmitBarcode")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            await Until(() => window.ViewModel.Left.ShowConfirmedTask && !window.ViewModel.Busy);
            await window.ViewModel.RefreshAsync();
            Check(window.ViewModel.ExitVisibleOperationCount > 0, "exit summary reads an actual synthetic operation");
            pending = true; unknown = true; await window.ViewModel.RefreshAsync();
            await Confirm(dialog =>
            {
                Check(Text(dialog).Contains("结果未知阶段：1") && Text(dialog).Contains("待生效配置：已保存"), "unknown result and saved pending configuration appear as plain text");
                Check(Text(dialog).Contains("已发送请求及未确认结果仍保留"), "exit never claims original requests or unknown results are cancelled");
                CaptureInternal(evidence, "station-exit-unknown-pending", dialog, lines);
                ClickButton(dialog, "CancelExitButton");
            }); await Until(() => window.IsEnabled);
            hidden = true; await h.App.ExitManagementModeAsync(); await window.ViewModel.RefreshAsync();
            await Until(() => window.ViewModel.ExitPendingConfiguration == "待确认");
            var project = projection.Project;
            projection.Project = snapshot => project!(snapshot) with { PublishedTicks = System.Diagnostics.Stopwatch.GetTimestamp() - 4 * System.Diagnostics.Stopwatch.Frequency };
            await window.ViewModel.RefreshAsync();
            await Until(() => !window.ViewModel.Fresh);
            await Confirm(dialog =>
            {
                Check(Text(dialog).Contains("状态读取：已失效") && Text(dialog).Contains("结果未知阶段：1"), "stale status is explicit and retains the previously visible unknown result");
                CaptureInternal(evidence, "station-exit-stale", dialog, lines);
                ClickButton(dialog, "CancelExitButton");
            }); await Until(() => window.IsEnabled);
            projection.Project = project; await window.ViewModel.RefreshAsync(); await Until(() => window.ViewModel.Fresh);
            await SetClient(window, 640, 420);
            await Confirm(dialog =>
            {
                Check(Text(dialog).Contains("待生效配置：待确认"), "withheld pending state is not reported as no pending configuration");
                Check(Button(dialog, "ContinueExitButton").IsVisible && Button(dialog, "CancelExitButton").IsVisible, "small station keeps both dialog actions visible");
                ClickButton(dialog, "ContinueExitButton");
            });
            await Until(() => attempts == 1 && window.IsEnabled);
            Check(stopped == 0 && window.IsVisible && window.ViewModel.Message.Contains("停止未完成"), "failed resource release keeps the station visible with its failure reason");
            await Confirm(dialog => ClickButton(dialog, "ContinueExitButton"));
            await Until(() => !window.IsVisible);
            Check(attempts == 2 && stopped == 1, "retry confirms again and closes only after the owner stop completes");
        }
        finally
        {
            foreach (var dialog in System.Windows.Application.Current.Windows.OfType<SupplyStationExitWindow>().ToArray()) dialog.Close();
            if (window.IsVisible)
            {
                window.ConfirmOwnerExit = _ => true;
                await Until(() => window.IsEnabled);
                window.Close(); await Until(() => !window.IsVisible || window.IsEnabled);
                if (window.IsVisible) { window.Close(); await Until(() => !window.IsVisible); }
            }
        }
    }
}

using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Application.Contracts;
using Ipc.Platform;

static partial class Program
{
    static async Task VerifyManualRcsDebugUi(string root, string evidence, List<string> lines)
    {
        void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); lines.Add("PASS MANUAL-DEBUG-UI " + message); }
        T Field<T>(object c, string name) => (T)c.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(c)!;
        await using var h = new ManualSupplyHarness(root, Path.Combine(evidence, "manual-debug"), deferred: true, rightCode: false, leftCode: false);
        await h.StartAsync(); await ManagementModeFixture.Authorize(h.App);
        var application = DispatchProxy.Create<ICameraRoundUiFixture, ManualDebugReturnProbe>();
        var probe = (ManualDebugReturnProbe)(object)application; probe.Inner = h.App;
        var window = new SupplyStationWindow(application, null, "standard") { Title = "人工请求调试 · 无相机合成验证" };
        window.Show(); var vm = window.ViewModel;
        async Task Flush() { await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout(); }
        async Task Until(Func<bool> done)
        { for (var i = 0; i < 180; i++) { await vm.RefreshAsync(); await Flush(); if (done()) return; await Task.Delay(35); } throw new TimeoutException("Manual debug UI did not converge"); }
        try
        {
            await SetClient(window, 1366, 768); await Until(() => vm.Fresh && vm.IsManagementMode);
            window.ShowPositionDebug(vm.Right); await Flush();
            var control = Descendants(window).OfType<UserControl>().Single(c => c.GetType().Name == "StationDebugControl");
            Field<DispatcherTimer>(window, "timer").Stop(); Field<DispatcherTimer>(control, "timer").Stop();
            Field<DispatcherTimer>(Field<UserControl>(control, "livePreview"), "timer").Stop();
            var entry = Field<TextBox>(control, "requestBarcode"); var prepare = Field<Button>(control, "prepare"); var send = Field<Button>(control, "send");
            Check(!entry.IsReadOnly && !Descendants(control).OfType<TextBlock>().Any(t => t.Text.Contains("调试说明")),
                "manual request input is editable and no debug explanation field is rendered");
            entry.Text = "0000011111"; prepare.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => send.IsEnabled && Field<TextBox>(control, "requestBody").Text.Contains("0000011111"));
            Check(h.Peer.Requests.IsEmpty && Field<TextBlock>(control, "photoState").Text.Contains("人工请求"),
                "native prepare works without camera configuration, photo or free-text reason and sends nothing");
            // Polling must not overwrite a newer user draft or allow it to send
            // an older frozen barcode by accident.
            entry.Text = "0000022222";
            await (Task)control.GetType().GetMethod("PollAsync")!.Invoke(control, null)!;
            Check(entry.Text == "0000022222" && !send.IsEnabled, "refresh preserves edited draft and prevents sending its previous frozen request");
            prepare.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => send.IsEnabled && Field<TextBox>(control, "requestBody").Text.Contains("0000022222"));
            send.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => !Field<bool>(control, "busy") && h.Peer.Requests.Count == 1 && Field<TextBox>(control, "responseBody").Text.Contains("Accepted"));
            Check(send.Content?.ToString() == "核对发送结果", "explicit native send displays the actual adapter response and changes to original-result lookup");
            send.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => !Field<bool>(control, "busy"));
            Check(h.Peer.Requests.Count == 1, "native original-result lookup does not send a second HTTP request");
            CaptureInternal(evidence, "manual-debug-no-camera", (Grid)window.FindName("ClientArea"), lines);
            Field<TextBox>(control, "responseBody").BringIntoView(); await Flush();
            CaptureInternal(evidence, "manual-debug-request-response", (Grid)window.FindName("ClientArea"), lines);
            await SetClient(window, 800, 420); prepare.BringIntoView(); await Flush();
            Check(Inside(prepare, (Grid)window.FindName("ClientArea")), "manual preparation remains reachable in compact layout");
            entry.Text = "UNSENT-ON-EXIT"; prepare.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => send.IsEnabled && Field<TextBox>(control, "requestBody").Text.Contains("UNSENT-ON-EXIT"));
            typeof(SupplyStationWindow).GetMethod("SetPage", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, ["workstation"]);
            await Until(() => vm.Right.CanAdmit);
            Check((await h.App.SnapshotAsync()).Operations.Single(o => o.ParcelCode == "UNSENT-ON-EXIT").Wcs is { State: Ipc.Application.Contracts.StageState.Cancelled, Attempts: 0 } && h.Peer.Requests.Count == 1,
                "leaving native debug cancels only its unsent test request and restores ordinary manual supply without an end panel");
            await LateReturn(reload: false, newer: false);
            await LateReturn(reload: true, newer: false);
            await LateReturn(reload: true, newer: true);

            async Task LateReturn(bool reload, bool newer)
            {
                window.ShowPositionDebug(vm.Right); await Flush();
                entry.Text = "LATE-" + Wire.Id();
                await Until(() => !Field<bool>(control, "busy") && Field<Button>(control, "prepare").IsEnabled);
                probe.Prepared = new(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = probe.Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
                try
                {
                    prepare.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var late = await probe.Prepared.Task.WaitAsync(TimeSpan.FromSeconds(10));
                    typeof(SupplyStationWindow).GetMethod("SetPage", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, ["workstation"]);
                    await Flush();
                    CameraReadRound? replacement = null;
                    if (newer)
                    {
                        var s = await h.App.SnapshotAsync();
                        replacement = await h.App.PrepareManualRcsRequestAsync(new(Wire.Id(), s.RuntimeId, s.Configuration.Revision, "right", "OTHER-WINDOW", late.Id), default);
                    }
                    if (reload) { window.ShowPositionDebug(vm.Right); await Flush(); }
                    release.TrySetResult();
                    await Until(() => !Field<bool>(control, "busy"));
                    var original = (await h.App.ReadCameraOperationDetailAsync(late.OperationId!, default))!.Original;
                    Check(original is { State: ParcelState.Abandoned, Wcs: { State: StageState.Cancelled, Attempts: 0 } } && h.Peer.Requests.Count == 1,
                        "durable preparation arriving after departure is retired by original command identity; reload=" + reload + "; newer=" + newer);
                    if (replacement is not null)
                    {
                        var current = await h.App.ReadCameraReadRoundAsync(default);
                        Check(current.Id == replacement.Id && current.CanSend, "late return cannot cancel another window's new round");
                        var s = await h.App.SnapshotAsync();
                        await h.App.CloseUnsentDebugRoundAsync(new(Wire.Id(), s.RuntimeId, replacement.Id!), default);
                    }
                    else Check((await h.App.SnapshotAsync()).Positions!.All(p => p.BarcodeInput?.Manual == true), "late cleanup restores ordinary manual admission");
                    typeof(SupplyStationWindow).GetMethod("SetPage", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, ["workstation"]);
                    await Flush();
                }
                finally { release.TrySetResult(); probe.Release = null; }
            }
        }
        finally { window.Close(); }
    }
}

// Delay only delivery of the real durable preparation result. Cancellation after
// persistence must not hide the actual outcome from the originating window.
public class ManualDebugReturnProbe : ProjectionProbe
{
    public TaskCompletionSource<CameraReadRound>? Prepared;
    public TaskCompletionSource? Release;
    protected override object? Invoke(MethodInfo? method, object?[]? arguments)
    {
        if (method!.Name == nameof(ICameraReadRounds.PrepareManualRcsRequestAsync) && Release is { } release)
            return Prepare((PrepareManualRcsRequest)arguments![0]!, (CancellationToken)arguments[1]!, release);
        return base.Invoke(method, arguments);
    }
    async Task<CameraReadRound> Prepare(PrepareManualRcsRequest command, CancellationToken token, TaskCompletionSource release)
    {
        var value = await ((ICameraReadRounds)Inner).PrepareManualRcsRequestAsync(command, token);
        Prepared!.TrySetResult(value); await release.Task; return value;
    }
}

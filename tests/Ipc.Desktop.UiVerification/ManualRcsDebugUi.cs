using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Ipc.Application.Sportmaster.Desktop;

static partial class Program
{
    static async Task VerifyManualRcsDebugUi(string root, string evidence, List<string> lines)
    {
        void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); lines.Add("PASS MANUAL-DEBUG-UI " + message); }
        T Field<T>(object c, string name) => (T)c.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(c)!;
        await using var h = new ManualSupplyHarness(root, Path.Combine(evidence, "manual-debug"), deferred: true, rightCode: false, leftCode: false);
        await h.StartAsync(); await ManagementModeFixture.Authorize(h.App);
        var window = new SupplyStationWindow(h.App, null, "standard") { Title = "人工请求调试 · 无相机合成验证" };
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
            send.BringIntoView(); await Flush();
            CaptureInternal(evidence, "manual-debug-request-response", (Grid)window.FindName("ClientArea"), lines);
            await SetClient(window, 800, 420); prepare.BringIntoView(); await Flush();
            Check(Inside(prepare, (Grid)window.FindName("ClientArea")), "manual preparation remains reachable in compact layout");
        }
        finally { window.Close(); }
    }
}

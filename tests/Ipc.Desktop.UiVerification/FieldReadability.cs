using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Presentation.Wpf;

static partial class Program
{
    static async Task VerifyFieldReadability(string root, string evidence, List<string> lines)
    {
        void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); lines.Add("PASS FIELD-READABILITY " + text); }
        await using var h = new ManualSupplyHarness(root, Path.Combine(evidence, "field-readability"), deferred: true);
        await h.StartAsync(); h.Peer.ReplySlot = "SIM-CHUTE-03"; h.Peer.ReplyWaveNo = "WAVE-20261103-01"; h.Peer.ReplyQuantity = "00012";
        var window = new SupplyStationWindow(h.App, null, "standard"); window.Show(); var vm = window.ViewModel;
        async Task Flush() { await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout(); }
        async Task Until(Func<bool> done) { for (var i = 0; i < 200; i++) { await Flush(); if (done()) return; await Task.Delay(20); } throw new TimeoutException("Field readability scene did not converge"); }
        try
        {
            await SetClient(window, 1536, 816); await Until(() => vm.Fresh && vm.Left.CanSubmitBarcode);
            var position = Descendants(window).OfType<SupplyStationPosition>().Single(p => ReferenceEquals(p.DataContext, vm.Left));
            var entry = (TextBox)position.FindName("BarcodeEntry"); window.Activate(); entry.Focus(); await Flush();
            entry.Text = "SYNTH-READABILITY-001"; await Flush();
            ((Button)position.FindName("SubmitBarcode")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => vm.Left.ShowConfirmedTask && !vm.Busy);
            var client = (Grid)window.FindName("ClientArea");
            foreach (var size in new[] { (1536d, 816d), (1092d, 614d), (800d, 420d) })
            {
                await SetClient(window, size.Item1, size.Item2); await Flush();
                var slot = (TextBox)position.FindName("CurrentSlot");
                Check(slot.Text == "SIM-CHUTE-03" && slot.FontSize <= 32 && Inside(slot, client), "long chute stays complete at reduced size: " + size.Item1);
                var transform = slot.TransformToAncestor(client);
                var scale = (transform.Transform(new Point(0, 1)) - transform.Transform(new Point())).Length;
                Check(slot.FontSize * scale >= 24, "actually drawn long chute stays at least 24 DIP: " + size.Item1);
                foreach (var name in new[] { "WaveValue", "TaskValue", "SkuValue", "QuantityValue" })
                {
                    var value = (TextBox)position.FindName(name);
                    Check(value.IsVisible && value.FontSize == UiTypography.TaskValue && value.FontSize == 20 && value.IsReadOnly && Inside(value, client), "larger selectable task value fits: " + name + " at " + size.Item1);
                }
                var labels = Descendants(position).OfType<TextBlock>().Where(t => t.Text is "波次号" or "任务号" or "商品编码" or "返回数量").ToArray();
                Check(labels.Length == 4 && labels.All(t => t.FontSize == 16), "all four task labels use the larger shared scale");
                CaptureInternal(evidence, "field-readability-" + (int)size.Item1, client, lines);
            }
        }
        finally { window.Close(); }
    }
}

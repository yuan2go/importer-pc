using System.Windows;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Ipc.Application.Sportmaster.Desktop;

static partial class Program
{
    static async Task VerifyPositionInputUi(string root, string evidence, List<string> lines)
    {
        void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); lines.Add("PASS POSITION-INPUT " + text); }
        await using var h = new CameraRoundHarness(root, Path.Combine(evidence, "position-input"), 15000);
        await h.StartAsync();
        var window = new SupplyStationWindow(h.App, null, "standard") { Title = "菜鸟供包台 · 合成输入验证" }; window.Show();
        try
        {
            await SetClient(window, 1536, 816); window.Activate();
            var vm = window.ViewModel;
            async Task Until(Func<bool> done)
            {
                for (var i = 0; i < 200; i++) { await vm.RefreshAsync(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); if (done()) return; await Task.Delay(20); }
                throw new TimeoutException("Position input state did not converge");
            }
            await Until(() => vm.Fresh && vm.ViewportReady);
            var left = Descendants(window).OfType<SupplyStationPosition>().Single(p => ReferenceEquals(p.DataContext, vm.Left));
            var right = Descendants(window).OfType<SupplyStationPosition>().Single(p => ReferenceEquals(p.DataContext, vm.Right));
            var leftEntry = (TextBox)left.FindName("BarcodeEntry"); var rightEntry = (TextBox)right.FindName("BarcodeEntry");
            h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-STEADY"), 1);
            await Until(() => vm.Left.ShowConfirmedTask);
            var outline = (Border)left.FindName("PositionOutline");
            var borderColors = new HashSet<string>(); var resultStates = new HashSet<string>(); var renders = 0;
            void Sample(object? sender, EventArgs args)
            {
                renders++; borderColors.Add(outline.BorderBrush.ToString());
                resultStates.Add(vm.Left.DisplayStatus + "/" + vm.Left.ShowConfirmedTask);
            }
            CompositionTarget.Rendering += Sample;
            var requests = h.Peer.Requests.Count;
            try
            {
                for (var i = 2; i <= 19; i++)
                {
                    h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-STEADY"), (uint)i);
                    await Task.Delay(160);
                }
                await Until(() => vm.Left.ShowConfirmedTask && vm.Left.Input?.Observation.SourceEventId == "mvs.frame.19");
            }
            finally { CompositionTarget.Rendering -= Sample; }
            lines.Add("INFO steady rendered frames=" + renders + "; borders=" + string.Join(",", borderColors) + "; results=" + string.Join(",", resultStates));
            Check(renders > 10 && borderColors.SetEquals([outline.BorderBrush.ToString()]), "continuous camera reads never flash the position outline");
            Check(resultStates.SetEquals(["投递格口/True"]), "a confirmed placement remains green across continuous reads without transient pending frames");
            Check(h.Peer.Requests.Count == requests, "continuous reads of one placement create no additional RCS requests");
            Check((int)typeof(SupplyStationPosition).GetProperty("SuccessCueCount", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(left)! == 1,
                "continued observations of one accepted operation do not repeat the success cue");
            h.Peer.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-CHANGED"), 20);
                await Until(() => vm.Left.IsProcessing && vm.Left.Barcode == "SYNTH-CHANGED");
                Check(!vm.Left.ShowConfirmedTask && vm.Left.DisplaySlot == "处理中…" && vm.Left.ResultBackground != Ipc.Presentation.Wpf.OperatorTokens.Success,
                    "a different code immediately invalidates the old chute while its real RCS response is held");
            }
            finally { h.Peer.Hold.TrySetResult(); h.Peer.Hold = null; }
            await Until(() => vm.Left.ShowConfirmedTask && vm.Left.Barcode == "SYNTH-CHANGED");
            var next = await h.ReplaceParcelAsync(h.Left, 21);
            await Until(() => !vm.Left.ShowConfirmedTask && vm.Left.Barcode == "");
            h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-CHANGED"), next);
            await Until(() => vm.Left.ShowConfirmedTask && vm.Left.Barcode == "SYNTH-CHANGED");
            Check(h.Peer.Requests.Count == requests + 2, "empty-tray release followed by the same code creates a distinct new placement and request");
            requests = h.Peer.Requests.Count;
            // Actual OS mouse input, including a hit in empty space and on a label.
            var rightOutline = (Border)right.FindName("PositionOutline");
            window.Activate(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            await BarcodeNativeKeyboard.ClickAsync(window, rightOutline, new Point(4, rightOutline.ActualHeight / 2));
            Check(vm.InputTargetPosition == "right" && rightEntry.IsKeyboardFocused, "clicking empty right-card space selects right and focuses barcode input");
            await BarcodeNativeKeyboard.SendAsync(window, rightEntry, "000731".Select(c => (ushort)c).Append((ushort)0x0D).ToArray());
            await Until(() => vm.Right.ShowConfirmedTask && vm.Right.Barcode == "000731");
            Check(vm.Left.Barcode == "SYNTH-CHANGED" && h.Peer.Requests.Count == requests + 1, "PDA input after card click submits one right request with leading zeros and leaves left unchanged");
            var leftTitle = Descendants(left).OfType<TextBlock>().Single(t => t.Text == "左投递位");
            window.Activate(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            await BarcodeNativeKeyboard.ClickAsync(window, leftTitle, new Point(8, leftTitle.ActualHeight / 2));
            Check(vm.InputTargetPosition == "left" && leftEntry.IsKeyboardFocused, "clicking the left camera-area title selects left without touching the input box");
            var rightSlot = (TextBox)right.FindName("CurrentSlot");
            window.Activate(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            await BarcodeNativeKeyboard.ClickAsync(window, rightSlot, new Point(12, rightSlot.ActualHeight / 2));
            Check(vm.InputTargetPosition == "right" && rightSlot.IsKeyboardFocusWithin, "clicking a selectable chute chooses right and keeps native text selection available");
            rightSlot.SelectAll(); var copied = rightSlot.SelectedText;
            Check(copied == vm.Right.DisplaySlot, "whole-card selection retains complete chute text selection for copying");
            await BarcodeNativeKeyboard.SendAsync(window, rightEntry, "000732".Select(c => (ushort)c).Append((ushort)0x0D).ToArray(), rightSlot);
            await Until(() => vm.Right.ShowConfirmedTask && vm.Right.Barcode == "000732");
            Check(h.Peer.Requests.Count == requests + 2 && rightEntry.IsKeyboardFocused, "scanner typing from the read-only chute reaches selected right entry before Enter and creates exactly one new request");
            CaptureInternal(evidence, "position-input-card-selection", (Grid)window.FindName("ClientArea"), lines);
        }
        finally { window.Close(); }
    }
}

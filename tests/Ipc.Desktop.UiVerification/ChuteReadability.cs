using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Presentation.Wpf;

static partial class Program
{
    static async Task VerifyChuteReadability(string root, string evidence, List<string> lines)
    {
        void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); lines.Add("PASS CHUTE-READABILITY " + text); }
        const string barcode = "00001234567890123456789012345678901234567890123456789012345678901234567890";
        const string task = "SYNTH-TASK-20261105-000000000000000000000000000000000000000000000001";
        var reason = string.Concat(Enumerable.Repeat("当前包裹未取得可用格口，请保留包裹和本条记录，联系维护人员核对原结果。", 5));
        await using var h = new ManualSupplyHarness(root, Path.Combine(evidence, "chute-readability"), deferred: true);
        await h.StartAsync();
        h.Peer.Mode = "reject";
        await h.App.SubmitParcelBarcodeAsync(await h.Command("right", "SYNTH-RIGHT"), default);
        h.Peer.Mode = "accept";
        var application = DispatchProxy.Create<IManualSupplyUiFixture, ProjectionProbe>();
        var probe = (ProjectionProbe)(object)application; probe.Inner = h.App;
        probe.Project = s => s with
        {
            Operations = s.Operations.Select(op => op.Wcs is not { } stage ? op : op with
            {
                Reason = op.PositionId == "right" ? reason : op.Reason,
                Wcs = stage with
                {
                    Failure = op.PositionId == "right" ? reason : stage.Failure,
                    Replies = stage.Replies.Select(reply => reply with
                    {
                        Destination = reply.Destination is not { } destination ? null : destination with
                        { TaskId = task, SkuId = "SYNTH-SKU-000000000000000000000000000000001", Message = op.PositionId == "right" ? reason : destination.Message }
                    }).ToArray()
                }
            }).ToArray()
        };
        var window = new SupplyStationWindow(application, null, "standard") { ConfirmDiscardDrafts = _ => true };
        window.Show(); var vm = window.ViewModel;
        async Task Flush() { await vm.RefreshAsync(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout(); }
        async Task Until(Func<bool> ready)
        { for (var i = 0; i < 200; i++) { await Flush(); if (ready()) return; await Task.Delay(20); } throw new TimeoutException("Readability state did not converge"); }
        double Rendered(TextBox value)
        { var t = value.TransformToAncestor(window); return value.FontSize * (t.Transform(new Point(0, 1)) - t.Transform(new Point())).Length; }
        Rect Bounds(FrameworkElement value) => value.TransformToAncestor(window).TransformBounds(new Rect(value.RenderSize));
        try
        {
            var work = SystemParameters.WorkArea;
            await SetClient(window, work.Width, work.Height); await Until(() => vm.Fresh && vm.Left.CanSubmitBarcode);
            var dpi = VisualTreeHelper.GetDpi(window);
            lines.Add($"ENVIRONMENT actual Windows DPI={dpi.DpiScaleX}/{dpi.DpiScaleY}; work area={work}; reading distance target=0.8-1m; synthetic content only");
            if (Environment.GetEnvironmentVariable("IPC_EXPECTED_DPI") is { } expected)
                Check(Math.Abs(dpi.DpiScaleX - double.Parse(expected, CultureInfo.InvariantCulture)) < .001, "actual HWND DPI matches the requested verification scale");
            var left = Descendants(window).OfType<SupplyStationPosition>().Single(p => ReferenceEquals(p.DataContext, vm.Left));
            var right = Descendants(window).OfType<SupplyStationPosition>().Single(p => ReferenceEquals(p.DataContext, vm.Right));
            var slot = (TextBox)left.FindName("CurrentSlot");
            foreach (var chute in new[] { "L-0123", "A" + new string('1', 15), "A" + new string('2', 16), "C" + new string('3', 31), "C" + new string('4', 32), "一二三四五六七八九十一二三四五六七八九十一二三四五六七八九十一二" })
            {
                h.Peer.ReplySlot = chute;
                await h.App.SubmitParcelBarcodeAsync(await h.Command("left", barcode), default);
                await Until(() => vm.Left.ShowConfirmedTask && vm.Left.Slot == chute);
                await SetClient(window, work.Width, work.Height); await Flush();
                Check(slot.Text == chute && Rendered(slot) >= (chute.Length <= 6 ? 56 : chute.Length <= 16 ? 32 : 24),
                    $"complete {chute.Length}-character chute retains readable rendered size: {Rendered(slot):F1} DIP, {Rendered(slot) * dpi.DpiScaleX:F1} device pixels");
                var panel = (Border)left.FindName("ResultPanel");
                var otherPanel = (Border)right.FindName("ResultPanel");
                Check(Math.Abs(Bounds(panel).Top - Bounds(otherPanel).Top) < 1 && Math.Abs(panel.ActualHeight - otherPanel.ActualHeight) < 1 &&
                    Math.Abs(Bounds((FrameworkElement)left.FindName("FixedActions")).Top - Bounds((FrameworkElement)right.FindName("FixedActions")).Top) < 1,
                    "accepted left and rejected right keep aligned results and primary actions with unequal barcode and task lengths");
                Check(Bounds(panel).Contains(Bounds(slot)), "wrapped chute is fully inside its result area");
                Check(vm.Left.DisplayNextStep.Length == 0 && !((TextBlock)left.FindName("NextStep")).IsVisible,
                    "confirmed chute has no redundant package-removal instruction or reserved instruction space");
                var entry = (TextBox)left.FindName("BarcodeEntry"); entry.SelectAll();
                Check(entry.SelectedText == barcode && entry.FontSize == 24 && entry.TextWrapping == TextWrapping.Wrap && entry.VerticalScrollBarVisibility == ScrollBarVisibility.Auto,
                    "long barcode preserves leading zeros and all characters, with readable wrapping, scrolling and selection");
                var taskValue = (TextBox)left.FindName("TaskValue"); taskValue.SelectAll();
                Check(taskValue.SelectedText == task && taskValue.FontSize == 22 && taskValue.TextWrapping == TextWrapping.Wrap && taskValue.VerticalScrollBarVisibility == ScrollBarVisibility.Auto,
                    "long task remains complete and selectable without shrinking its type");
                taskValue.ScrollToEnd(); await Flush();
                var last = taskValue.GetRectFromCharacterIndex(task.Length - 1);
                Check(!last.IsEmpty && last.Top >= -1 && last.Bottom <= taskValue.ActualHeight + 1, "last task character is reachable by native scrolling");
                taskValue.ScrollToHome();
                var reasonValue = (TextBox)right.FindName("AttentionReason");
                var reasonViewport = (ScrollViewer)right.FindName("PositionBody");
                Check(reasonValue.Text.Contains(reason, StringComparison.Ordinal) && reasonViewport.ViewportHeight >= reasonValue.FontSize * 1.4,
                    "full long rejection reason is retained with at least one complete readable line");
                reasonViewport.ScrollToBottom(); await Flush();
                var lastReason = reasonValue.GetRectFromCharacterIndex(reasonValue.Text.Length - 1);
                var visibleLast = reasonValue.TransformToAncestor(reasonViewport).TransformBounds(lastReason);
                Check(visibleLast.Top >= -1 && visibleLast.Bottom <= reasonViewport.ActualHeight + 1, "last reason character is reachable in its own scroll area");
                reasonViewport.ScrollToTop();
                var next = (TextBlock)right.FindName("NextStep");
                var formatted = new FormattedText(next.Text, CultureInfo.CurrentUICulture, next.FlowDirection,
                    new Typeface(next.FontFamily, next.FontStyle, next.FontWeight, next.FontStretch), next.FontSize, next.Foreground, dpi.PixelsPerDip)
                { MaxTextWidth = next.ActualWidth };
                Check(next.IsVisible && formatted.Height <= next.ActualHeight + 1 && Bounds(otherPanel).Contains(Bounds(next)),
                    "rejected parcel's next action is fully visible without clipping");
                foreach (var value in new[] { slot, entry, taskValue, reasonValue })
                {
                    var runs = GlyphRuns(value).ToArray();
                    Check(value.FontFamily.Source == UiTypography.Interface.Source && runs.Length > 0 && runs.All(r => r.GlyphTypeface.FontUri.ToString().Contains("notosanscjksc-", StringComparison.OrdinalIgnoreCase)),
                        "actual rendered operator text and identifiers use only shipped Noto faces");
                }
                CaptureInternal(evidence, "chute-" + chute.Length + (chute[0] > 127 ? "-cjk" : "-latin") + "-dpi-" + dpi.DpiScaleX, (Grid)window.FindName("ClientArea"), lines);
                await SetClient(window, 1092, 614); await Flush(); var smaller = Rendered(slot);
                await SetClient(window, 1536, 816); await Flush(); var larger = Rendered(slot);
                Check(larger > smaller + 1, $"chute grows with window space: {smaller:F1} -> {larger:F1} DIP");
                await SetClient(window, 3072, 1728); await Flush();
                var cap = chute.Length <= 6 ? 112 : chute.Length <= 16 ? 72 : 48;
                Check(Math.Abs(Rendered(slot) - cap) < 1, $"large synthetic backing surface stops at the {cap}-DIP maximum");
            }
            lines.Add("BOUNDARY native HWND and actual monitor DPI; oversized backing surfaces test caps, not a physical display. Target workstation, physical standing-distance acceptance, SDK/customer RCS NOT_RUN.");
        }
        finally { window.Close(); }
    }
}

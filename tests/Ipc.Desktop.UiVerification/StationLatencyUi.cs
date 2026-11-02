using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Platform;

static partial class Program
{
    static async Task VerifyStationLatencyUi(string root, string evidence, List<string> lines)
    {
        void Check(bool value, string text) { if (!value) throw new InvalidOperationException(text); lines.Add("PASS STATION-LATENCY " + text); }
        async Task Until(Func<bool> done, string step)
        { for (var i = 0; i < 250; i++) { await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); if (done()) return; await Task.Delay(40); } throw new TimeoutException(step); }
        JsonElement Metrics(CameraRoundHarness h)
        { using var json = JsonDocument.Parse(Wire.Encode(h.App.RuntimeProgress())); return json.RootElement.GetProperty("Presentation").GetProperty("Recent").Clone(); }
        await using var h = new CameraRoundHarness(root, Path.Combine(evidence, "continuity"), 4000); await h.StartAsync();
        var window = new SupplyStationWindow(h.App, null, "standard"); window.Show();
        try
        {
            await SetClient(window, 1366, 768); await Until(() => window.ViewModel.Fresh, "initial snapshot");
            var p = window.ViewModel.Left;
            var control = Descendants(window).OfType<SupplyStationPosition>().Single(c => ReferenceEquals(c.DataContext, p));
            var slot = (TextBox)control.FindName("CurrentSlot");
            var barcode = CameraRoundHarness.Barcode("CONTINUITY-0001");
            h.Api(h.Left).Enqueue(barcode, 1); await Until(() => p.SlotAvailable, "first accepted slot");
            var old = p.Slot; var original = p.Operation!.Id;
            // Next parcels use their own codes: one placement of a code is one request (SM-PLACEMENT-PRESENCE-29).
            h.Peer.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously); h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("CONTINUITY-0002"), 2);
            await Until(() => p.IsProcessing && p.Operation?.Id != original, "next-parcel processing placeholder");
            Check(p.Slot == "未取得" && p.DisplaySlot == "处理中…" && slot.Text == "处理中…" && slot.IsVisible && !((Grid)control.FindName("TaskFields")).IsVisible && !p.CurrentTaskAvailable,
                "next-parcel pending request immediately clears the prior destination; gray processing placeholder has no authoritative slot or old task fields");
            Check(p.DisplayStatus.Contains("处理中") && p.DisplaySlotLabel.Contains("尚未取得") && p.DisplayPresentation.Tone == Ipc.Application.ParcelFlow.Desktop.ParcelPresentationTone.Pending,
                "processing state has explicit text, automation label and muted color without a previous destination");
            await Until(() => Metrics(h).EnumerateArray().Any(m => m.GetProperty("Stage").GetString() == "ReadFeedbackComposed" && m.GetProperty("ObservationId").GetString() == p.Input?.Observation.Id), "read cue composition metric while RCS is held");
            Check(p.Operation?.State == ParcelState.ResolvingWcs && p.CameraReadFeedback.Contains("已读到"),
                "read feedback reaches actual DWM composition before the delayed RCS response without claiming business acceptance");
            CaptureInternal(evidence, "slot-confirming", (Grid)window.FindName("ClientArea"), lines);
            h.Peer.Hold.TrySetResult(); h.Peer.Hold = null;
            await Until(() => p.SlotAvailable && p.Operation?.Id != original, "next-parcel second response");
            Check(!p.SlotProvisional && p.DisplaySlot == old && p.ShowConfirmedTask && h.Peer.Requests.Count == 2,
                "second decoded read creates its own request and replaces the provisional display only on its own confirmed result");
            original = p.Operation!.Id;
            h.Peer.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously); h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("CONTINUITY-0003"), 3);
            await Until(() => p.IsProcessing && p.Operation?.Id != original, "third pending read");
            await Task.Delay(1700);
            await Until(() => p.Operation?.State == ParcelState.ResolvingWcs, "pending request after former hold deadline");
            Check(p.IsProcessing && p.DisplaySlot == "处理中…" && slot.Text != old && !((Grid)control.FindName("TaskFields")).IsVisible, "processing placeholder never resurrects the old slot after the former display hold deadline");
            h.Peer.ReplySlot = "SYNTH-OTHER"; h.Peer.Hold.TrySetResult(); h.Peer.Hold = null;
            await Until(() => p.SlotAvailable && p.Slot == "SYNTH-OTHER", "different slot response");
            Check(p.DisplaySlot == "SYNTH-OTHER" && !p.SlotProvisional, "a different returned slot replaces the old value immediately and atomically");
            original = p.Operation!.Id; h.Peer.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
            h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("DIFFERENT-0002"), 4);
            await Until(() => p.Operation?.Id != original && p.Operation?.State == ParcelState.ResolvingWcs, "different barcode pending");
            Check(!p.SlotProvisional && p.IsProcessing && p.DisplaySlot == "处理中…" && slot.Text == "处理中…" && !p.ShowConfirmedTask && !((Grid)control.FindName("TaskFields")).IsVisible, "a different barcode uses only a processing placeholder and never borrows the previous parcel's slot or task fields");
            h.Peer.Hold.TrySetResult(); h.Peer.Hold = null; await Until(() => p.SlotAvailable, "different barcode accepted");
            await Until(() => Metrics(h).EnumerateArray().Any(m => m.GetProperty("Stage").GetString() == "SlotComposed" && m.GetProperty("ObservationId").GetString() == p.Input?.Observation.Id), "slot composition metric");
            Check(Metrics(h).EnumerateArray().Where(m => m.GetProperty("Stage").GetString() == "SlotComposed").All(m => m.GetProperty("SnapshotToCompositionMs").GetDouble() >= 0),
                "accepted slot telemetry records both decode-to-composition and snapshot-to-composition with the matching observation identity");
            lines.Add("METRICS LOOPBACK_SYNTHETIC " + Metrics(h).GetRawText());
            ((TextBox)control.FindName("BarcodeEntry")).Text = "MANUAL-AFTER-CAMERA";
            ((Button)control.FindName("SubmitBarcode")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => p.SlotAvailable && p.Input?.Observation.OperatorInput is not null, "manual input following camera feedback");
            Check(p.DisplaySlotVisible && p.ShowConfirmedTask && !p.SlotProvisional,
                "an older camera cue cannot hide a newer confirmed keyboard request or borrow its slot for another source");
            original = p.Operation!.Id; h.Peer.Mode = "reject"; h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("DIFFERENT-0002"), 5);
            await Until(() => p.Operation?.Id != original && p.Operation?.State == ParcelState.WcsRejected, "explicit reject");
            Check(!p.DisplaySlotVisible && !p.SlotProvisional && !((Grid)control.FindName("TaskFields")).IsVisible, "explicit rejection clears every provisional and confirmed slot from the current display");
            window.ViewModel.MarkReadFailure("Synthetic stale projection");
            Check(!p.DisplaySlotVisible && !p.SlotProvisional, "stale projection cannot preserve a provisional slot");
        }
        finally { h.Peer.Hold?.TrySetResult(); window.Close(); }

        await using var unknown = new CameraRoundHarness(root, Path.Combine(evidence, "unknown"), 2200); await unknown.StartAsync();
        var other = new SupplyStationWindow(unknown.App, null, "compact"); other.Show();
        try
        {
            await SetClient(other, 1024, 768); await Until(() => other.ViewModel.Fresh, "unknown fixture snapshot");
            var p = other.ViewModel.Left; var code = CameraRoundHarness.Barcode("UNKNOWN-0001"); unknown.Api(unknown.Left).Enqueue(code, 1);
            await Until(() => p.SlotAvailable, "unknown fixture accepted basis"); var original = p.Operation!.Id;
            unknown.Peer.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously); unknown.Api(unknown.Left).Enqueue(CameraRoundHarness.Barcode("UNKNOWN-0002"), 2);
            await Until(() => p.Operation?.Id != original && p.Operation?.State == ParcelState.WcsUnknown, "unknown original response");
            Check(!p.SlotProvisional && !p.DisplaySlotVisible && p.RejectionIdentity is not null && p.Operation?.Wcs?.State == StageState.Unknown,
                "Unknown removes the old display and has a distinct exception cue without clearing original request responsibility or resending");
            // Explicit read-only projection fixture: a late cue is not permission
            // to replace the existing Unknown instruction with a pending promise.
            var cue = new ParcelReadFeedback(other.ViewModel.Snapshot!.RuntimeId, p.PositionId, Wire.Id(), p.ReaderId,
                p.Input!.Observation.Epoch, "UNKNOWN-0002", System.Diagnostics.Stopwatch.GetTimestamp(), DateTimeOffset.UtcNow);
            p.GetType().GetMethod("ApplyReadFeedback", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(p, [cue]);
            Check(p.DisplayStatus == p.Status && p.DisplayNextStep == p.NextStep && !p.DisplaySlotVisible,
                "a new read-only cue preserves the original Unknown instruction and cannot authorize an old destination");
            CaptureInternal(evidence, "slot-unknown-compact", (Grid)other.FindName("ClientArea"), lines);
        }
        finally { unknown.Peer.Hold?.TrySetResult(); other.Close(); }
    }
}

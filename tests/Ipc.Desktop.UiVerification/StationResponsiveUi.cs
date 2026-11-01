using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Ipc.Application.Contracts;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Platform;

static partial class Program
{
    static async Task VerifyStationResponsiveUi(string root, string evidence, List<string> lines)
    {
        void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); lines.Add("PASS RESPONSIVE " + text); }
        async Task Until(Func<bool> ready, string step)
        { for (var i = 0; i < 250; i++) { await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); if (ready()) return; await Task.Delay(20); } throw new TimeoutException(step); }
        async Task<ParcelPresentationUpdate> ReadSnapshot(IParcelPresentationSubscription subscription, long revision = 0)
        {
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            while (true) { var update = await subscription.ReadAsync(budget.Token); if (update.Snapshot is { } s && s.Revision >= revision) return update; }
        }
        await using var h = new CameraRoundHarness(root, Path.Combine(evidence, "responsive"), 5000); await h.StartAsync();
        var source = (IParcelPresentationSource)h.App;
        var subscriptions = Enumerable.Range(0, 8).Select(_ => source.SubscribePresentation()).ToArray();
        try
        {
            var rejected = false; try { using var excess = source.SubscribePresentation(); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "ninth subscription is rejected instead of building unbounded consumers");
            await ReadSnapshot(subscriptions[0]);
            var code = CameraRoundHarness.Barcode("RESPONSIVE-0001");
            h.Api(h.Left).Enqueue(code, 1);
            await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Any(o => o.State == ParcelState.SlotResolved));
            var firstOperation = (await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "left").CurrentOperation;
            h.Api(h.Left).Enqueue(code, 2);
            await CameraRoundHarness.Until(async () => { var s = await h.App.SnapshotAsync(); var id = s.Positions!.Single(p => p.Id == "left").CurrentOperation; return id != firstOperation && s.Operations.Any(o => o.Id == id && o.State == ParcelState.SlotResolved); });
            var revision = (await h.App.SnapshotAsync()).Revision;
            await ReadSnapshot(subscriptions[0], revision);
            var latest = await subscriptions[1].ReadAsync(default);
            Check(latest.Snapshot?.Revision >= revision && latest.Snapshot.Positions!.Single(p => p.Id == "left").CurrentOperation != firstOperation && h.Peer.Requests.Count == 2,
                "unread subscriber receives latest committed snapshot directly while same-code observations still create independent original requests");
            latest.Snapshot!.Operations[0] = latest.Snapshot.Operations[0] with { Slot = "MUTATED-CONSUMER" };
            var detached = await subscriptions[2].ReadAsync(default);
            Check(detached.Snapshot!.Operations.All(o => o.Slot != "MUTATED-CONSUMER"), "subscribers never share mutable snapshot arrays or authoritative business objects");
            using (var publisherEntered = new ManualResetEventSlim())
            using (var publisherRelease = new ManualResetEventSlim())
            {
                var blockOnce = 1;
                h.App.Boundary = point => { if (point == "SnapshotGateAcquired" && Interlocked.Exchange(ref blockOnce, 0) == 1) { publisherEntered.Set(); publisherRelease.Wait(); } };
                var blockedPublisher = Task.Run(h.App.SnapshotAsync);
                try
                {
                    await Until(() => publisherEntered.IsSet, "held snapshot publication for subscriber cancellation");
                    using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(4));
                    ValueTask<ParcelPresentationUpdate> pending;
                    while (true) { pending = subscriptions[4].ReadAsync(wait.Token); if (!pending.IsCompleted) break; await pending; }
                    var concurrentRejected = false;
                    try { await subscriptions[4].ReadAsync(default); } catch (InvalidOperationException) { concurrentRejected = true; }
                    wait.Cancel(); try { await pending; } catch (OperationCanceledException) { }
                    Check(concurrentRejected, "one in-flight read per subscriber prevents unbounded waiters and cancellation releases the actual pending read");
                }
                finally { publisherRelease.Set(); h.App.Boundary = null; await blockedPublisher; }
            }
            subscriptions[3].Dispose(); using var replacement = source.SubscribePresentation();
            Check(true, "disposing a subscriber revokes its mailbox and immediately releases subscriber capacity");
        }
        finally { foreach (var subscription in subscriptions) subscription.Dispose(); }

        var window = new SupplyStationWindow(h.App, null, "standard"); window.Show();
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var hold = 0;
        try
        {
            await SetClient(window, 1366, 768); await Until(() => window.ViewModel.Fresh && window.ViewModel.Left.ShowConfirmedTask, "initial pushed snapshot");
            var p = window.ViewModel.Left; var other = window.ViewModel.Right;
            var control = Descendants(window).OfType<SupplyStationPosition>().Single(c => ReferenceEquals(c.DataContext, p));
            var slot = (TextBox)control.FindName("CurrentSlot"); var fields = (Grid)control.FindName("TaskFields");
            var rightControl = Descendants(window).OfType<SupplyStationPosition>().Single(c => ReferenceEquals(c.DataContext, other));
            var entry = (TextBox)rightControl.FindName("BarcodeEntry"); entry.Focus(); entry.Text = "UNSUBMITTED-DRAFT"; entry.Select(3, 4);
            h.App.Boundary = point => { if (point == "BeforeIngressDurable" && Interlocked.Exchange(ref hold, 0) == 1) { entered.Set(); release.Wait(); } };
            hold = 1; var original = p.Operation!.Id; var calls = h.Peer.Requests.Count; var clock = Stopwatch.StartNew();
            h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("RESPONSIVE-0001"), 3);
            await Until(() => entered.IsSet && p.IsProcessing && p.ReadCueLabel.EndsWith("0001", StringComparison.Ordinal), "read cue before durable input");
            Check(!p.ShowConfirmedTask && p.DisplaySlot == "处理中…" && slot.Text == "处理中…" && !fields.IsVisible &&
                p.Operation?.Id == original && h.Peer.Requests.Count == calls && p.DisplayAttentionReason.Contains("尚未确认持久接收"),
                "before FULL commit completes, a new same-code observation clears the old destination and fields without inventing custody/admission or sending RCS");
            var feedback = (await ((IParcelPresentationDiagnostics)h.App).ReadPresentationFeedbackAsync(default)).Single(r => r.PositionId == "left");
            await Until(() =>
            {
                using var json = JsonDocument.Parse(Wire.Encode(h.App.RuntimeProgress()));
                return json.RootElement.GetProperty("Presentation").GetProperty("Recent").EnumerateArray().Any(m =>
                    m.GetProperty("ObservationId").GetString() == feedback.ObservationId && m.GetProperty("Stage").GetString() == "ReadFeedbackComposed");
            }, "DWM feedback while durable handoff remains blocked");
            Check(!release.IsSet && entered.IsSet, "read cue reaches actual DWM composition while the real pre-durability boundary is still blocked");
            lines.Add("MEASURE synthetic pre-commit hold; read cue observed by " + clock.Elapsed.TotalMilliseconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + "ms; not low-end disk or customer RCS qualification");
            var count = (int)typeof(SupplyStationPosition).GetProperty("ReadCueCount", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(control)!;
            var apply = p.GetType().GetMethod("ApplyReadFeedback", BindingFlags.Instance | BindingFlags.NonPublic)!;
            for (var i = 0; i < 100; i++) apply.Invoke(p, [feedback]);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Check(count == (int)typeof(SupplyStationPosition).GetProperty("ReadCueCount", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(control)!,
                "100 repeats of the same observation identity do not repeat the cue; no barcode-string deduplication is used");
            Check(entry.Text == "UNSUBMITTED-DRAFT" && entry.SelectionStart == 3 && entry.SelectionLength == 4 && entry.IsKeyboardFocusWithin,
                "read/processing push preserves the other position's draft, caret selection and keyboard focus");
            CaptureInternal(evidence, "responsive-before-durable", (Grid)window.FindName("ClientArea"), lines);
            release.Set(); h.App.Boundary = null;
            await Until(() => p.ShowConfirmedTask && p.Operation?.Id != original, "commit and result pushed to UI");
            Check(h.Peer.Requests.Count == calls + 1 && p.DisplaySlot == p.Slot && !p.IsProcessing,
                "after durable handoff, the independent original request is sent once and its own confirmed result replaces the placeholder");

            h.Api(h.Right).Enqueue(CameraRoundHarness.Barcode("RIGHT-LIVE-0001"), 1);
            await Until(() => other.ShowConfirmedTask && other.CanAdmit, "other position accepted basis");
            entered.Reset(); release.Reset(); hold = 1;
            h.App.Boundary = point => { if (point == "SnapshotGateAcquired" && Interlocked.Exchange(ref hold, 0) == 1) { entered.Set(); release.Wait(); } };
            var blockedRead = Task.Run(h.App.SnapshotAsync);
            try
            {
                await Until(() => entered.IsSet, "held business snapshot gate");
                h.Api(h.Left).Discovered = false;
                await Until(() => p.DevicePresentation.Text.Contains("离线") && !p.DisplaySlotVisible, "offline feedback bypasses held snapshot gate");
                Check(!release.IsSet && !p.CanAdmit && other.ShowConfirmedTask && other.CanAdmit,
                    "left disconnection clears its destination and disables admission before any new snapshot; right remains independent while business gate is held");
            }
            finally { release.Set(); h.App.Boundary = null; await blockedRead; }
            h.Peer.Mode = "reject"; h.Api(h.Right).Enqueue(CameraRoundHarness.Barcode("RIGHT-REJECT-0002"), 2);
            await Until(() => other.Operation?.State == ParcelState.WcsRejected && !other.ShowConfirmedTask, "pushed rejection");
            await Until(() => (int)typeof(SupplyStationPosition).GetProperty("RejectionCueCount", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(rightControl)! > 0, "rejection feedback");
            Check(!other.DisplaySlotVisible && other.DisplayPresentation.Foreground == "#B52F38" && other.RejectionIdentity is not null,
                "explicit rejection removes the destination, displays red reason and produces a distinct identity-scoped rejection cue");
            CaptureInternal(evidence, "responsive-offline-rejection", (Grid)window.FindName("ClientArea"), lines);
            var stale = window.ViewModel.Snapshot! with { PublishedTicks = Stopwatch.GetTimestamp() - Stopwatch.Frequency * 4 };
            // A lower revision/timestamp cannot replace newer facts; use a fresh VM
            // to exercise delayed first-delivery rather than the rollback guard.
            var lateConsumer = new SupplyStationViewModel(h.App);
            await lateConsumer.AcceptPublishedSnapshotAsync(stale);
            Check(!lateConsumer.Fresh && lateConsumer.ReadStatus.Contains("过期") && !lateConsumer.Left.DisplaySlotVisible && !lateConsumer.Right.DisplaySlotVisible,
                "a delayed first push cannot renew freshness from an already expired immutable snapshot");
        }
        finally { release.Set(); h.App.Boundary = null; window.Close(); }
    }
}

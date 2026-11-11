using Ipc.Application.Contracts;
using Ipc.Application.Sportmaster;
using Ipc.Platform;

// Field report: the work card showed a chute from the previous shift (seven hours earlier) as
// "按本次格口投递". A finished result is an instruction only for the parcel on the tray now.
// Real ZXing, synthetic MVS frames and the built-in RCS over loopback for the camera path; the
// manual-entry harness for keyboard input.
internal static class WorkCardCurrent
{
    static void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); Console.WriteLine("PASS CARD " + text); }
    sealed class Clock : TimeProvider
    {
        long ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref ticks);
        public void Advance(TimeSpan interval) => Interlocked.Add(ref ticks, interval.Ticks);
    }

    internal static async Task Run(string workspace, string root)
    {
        var folder = Path.Combine(root, "work-card-current");
        string? first;
        await using (var h = new CameraRoundHarness(workspace, folder) { PlacementReleaseMs = 5000, StationClock = new Clock() })
        {
            await h.StartAsync();
            await h.WaitForCameraAdmissionAsync();
            await h.TrayAsync(h.Left, TrayPresence.Present);
            uint frame = 0;
            async Task<string?> Current() => (await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "left").CurrentOperation;
            h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-CARD-A"), ++frame);
            await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Any(o => o.State == ParcelState.SlotResolved));
            first = (await h.App.SnapshotAsync()).Operations.Single().Id;
            Check(await Current() == first, "the chute stays on the card while the parcel rests on the tray");
            for (var i = 0; i < 3; i++)
            {
                var decoded = h.Factory.Decoders[h.Left].Count;
                h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-CARD-A"), ++frame);
                await CameraRoundHarness.Until(() => Task.FromResult(h.Factory.Decoders[h.Left].Count > decoded));
            }
            Check(await Current() == first && h.Peer.Requests.Count == 1, "repeated reads of the resting parcel keep its chute and send nothing");
            frame = await h.ReplaceParcelAsync(h.Left, frame + 1);
            await CameraRoundHarness.Until(async () => await Current() is null);
            Check((await h.App.HistoryAsync()).Items.Any(i => i.Operation.Id == first), "an empty tray returns the card to waiting; the job stays in the records");

            h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-CARD-B"), ++frame);
            await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Count(o => o.State == ParcelState.SlotResolved) == 2);
            Check(await Current() is { } second && second != first, "the next parcel shows its own chute");

            // Restart (a new shift): nothing finished before this run opened is shown as current.
            await h.App.DisposeAsync();
            await h.StartAsync();
            var s = await h.App.SnapshotAsync();
            Check(s.Positions!.All(p => p.CurrentOperation is null) && s.Inputs is { Length: 0 } && (await h.App.HistoryAsync()).Items.Count(i => i.Operation.State == ParcelState.SlotResolved) == 2,
                "after a restart both cards and barcode inputs are empty; earlier reads and chutes remain only in the records");
        }

        await using (var m = new ManualSupplyHarness(workspace, Path.Combine(root, "work-card-manual"), deferred: true))
        {
            await m.StartAsync();
            var clock = new Clock();
            m.App.StationTime = clock;
            var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            m.Peer.Hold = hold;
            var submission = m.App.SubmitParcelBarcodeAsync(await m.Command("right", "SYNTH-CARD-MANUAL"), default);
            try
            {
                await CameraRoundHarness.Until(() => Task.FromResult(m.Peer.Requests.Count == 1));
                clock.Advance(TimeSpan.FromSeconds(6));
                var pending = await m.App.SnapshotAsync();
                Check(pending.Operations.Single().State == ParcelState.ResolvingWcs &&
                    pending.Positions!.Single(p => p.Id == "right").CurrentOperation == pending.Operations.Single().Id,
                    "the display hold does not start while the manual request is in flight");
            }
            finally { hold.TrySetResult(); }

            var admitted = await submission;
            await CameraRoundHarness.Until(async () => (await m.App.OperationAsync(admitted.OperationId!))?.State == ParcelState.SlotResolved);
            var original = (await m.App.SnapshotAsync()).Operations.Single();
            var id = original.Id;
            Check(admitted.OperationId == id && original.State == ParcelState.SlotResolved &&
                (await m.App.SnapshotAsync()).Positions!.Single(p => p.Id == "right") is { CurrentOperation: not null },
                "a keyed-in parcel shows the confirmed chute without requiring removal confirmation");
            var draft = await m.Command("right", "SYNTH-CARD-NEXT");
            clock.Advance(TimeSpan.FromMilliseconds(2999));
            Check((await m.App.SnapshotAsync()).Positions!.Single(p => p.Id == "right").CurrentOperation == id,
                "the chute is still available 2999 ms after the result was committed");
            clock.Advance(TimeSpan.FromMilliseconds(1));
            var expired = await m.App.SnapshotAsync();
            var position = expired.Positions!.Single(p => p.Id == "right");
            Check(position.CurrentOperation is null && position.CurrentRoundId is null && position.AutomaticSuppressed && position.PlacementClearedAt is null &&
                expired.Inputs!.All(i => i.PositionId != "right") && position.BarcodeInput?.Manual == true && m.Peer.Requests.Count == 1,
                "at 3000 ms the card clears the chute and barcode without recording removal or sending anything");

            m.Peer.ReplySlot = "";
            var next = await m.App.SubmitParcelBarcodeAsync(draft with { ExpectedRoundId = null }, default);
            await CameraRoundHarness.Until(async () => (await m.App.OperationAsync(next.OperationId!))?.Wcs?.State == StageState.Unknown);
            Check(next.Status == "Admitted" && next.OperationId != id && m.Peer.Requests.Count == 2 &&
                Wire.Encode((await m.App.OperationAsync(id))?.Wcs) == Wire.Encode(original.Wcs),
                "a draft begun before the display expired submits once without changing the original result");
            clock.Advance(TimeSpan.FromSeconds(6));
            var unknown = await m.App.SnapshotAsync();
            var unresolved = unknown.Positions!.Single(p => p.Id == "right");
            Check(unresolved.CurrentOperation == next.OperationId && unresolved.ExpiredManualResult is null &&
                unresolved.BarcodeInput?.Manual == false && unknown.Operations.Any(o => o.Id == next.OperationId && o.State == ParcelState.WcsUnknown),
                "an unconfirmed manual request remains visible and fenced after the same interval");
        }
        await PausedCameraPicture(workspace, Path.Combine(root, "paused-current-picture"));
    }

    static async Task PausedCameraPicture(string workspace, string root)
    {
        await using var h = new CameraRoundHarness(workspace, root) { StationClock = new Clock() };
        await h.StartAsync(); await h.WaitForCameraAdmissionAsync();
        await h.TrayAsync(h.Left, TrayPresence.Present);
        var pixels = CameraRoundHarness.Barcode("SYNTH-CURRENT-PICTURE-A");
        h.Api(h.Left).Enqueue(pixels, 1);
        ParcelOperation? operation = null;
        await CameraRoundHarness.Until(async () => (operation = (await h.App.SnapshotAsync()).Operations.SingleOrDefault()) is { State: ParcelState.SlotResolved, OriginalFrame.State: "Saved" });
        var query = new ParcelPictureQuery(OperationId: operation!.Id);
        var first = await h.App.ReadParcelPictureAsync(query, default);
        Check(first.Archived is not null && first.Archived.Metadata.Acquisition == operation.Acquisition, "current-round original is archived with exact acquisition identity");
        var originalHash = Wire.Hash(Convert.ToBase64String(first.Archived!.Png));
        var before = await h.App.SnapshotAsync();
        Check((await h.App.CommandAsync(new(Wire.Id(), before.Revision, "Pause"))).Status == "Persisted", "picture fixture confirms the original software pause");
        h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-BLOCKED-PICTURE-B"), 2);
        await CameraRoundHarness.Until(() => Task.FromResult(h.Factory.Sources[h.Left].Timings.Snapshot().RecentFrames.Any(f => f.Number == 2)));
        var paused = await h.App.SnapshotAsync();
        Check(paused.Positions!.Single(p => p.Id == "left").CurrentOperation == operation.Id && h.Peer.Requests.Count == 1, "paused new observations neither replace the current round nor send a new request");
        first.Archived.Png[0] ^= 255;
        var reread = await h.App.ReadParcelPictureAsync(query, default);
        Check(Wire.Hash(Convert.ToBase64String(reread.Archived!.Png)) == originalHash, "returned picture bytes cannot mutate original evidence");
        await h.App.ReconnectDeviceAsync(h.Left);
        var reconnected = await h.App.ReadParcelPictureAsync(query, default);
        Check(Wire.Hash(Convert.ToBase64String(reconnected.Archived!.Png)) == originalHash, "reconnect cannot replace an exact round-owned archived image");
        var snapshot = await h.App.SnapshotAsync();
        var token = snapshot.Positions!.Single(p => p.Id == "left").PlacementRemovalOperation;
        Check(token is not null, "lost visual reliability permits explicit current-round removal");
        await h.App.CommandAsync(new(Wire.Id(), snapshot.Revision, "ConfirmPlacementRemoval", "left", token));
        Check((await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "left").CurrentRoundId is null, "explicit removal clears only the current projection");
        Check((await h.App.ReadParcelPictureAsync(query, default)).Archived is not null && h.Peer.Requests.Count == 1, "historical exact original remains readable and sends nothing");
        await h.App.DisposeAsync(); await h.StartAsync();
        Check((await h.App.SnapshotAsync()).Positions!.All(p => p.CurrentRoundId is null), "a new shift does not revive old picture or barcode projections");
    }
}

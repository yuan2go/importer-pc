using Ipc.Application.Contracts;
using Ipc.Application.Sportmaster;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Platform;

// SM-SAME-PLACEMENT-29, code changes at a position. A different code is still a new placement
// at once (fast parcel swap), but it must not reopen a code that was already requested in the
// same placement chain: only an empty tray ends the chain. One physical parcel can show two
// labels, or a label can decode differently under glare/occlusion, without being lifted.
// Real ZXing, synthetic MVS frames, built-in RCS over loopback HTTP; not field acceptance.
internal static class SamePlacementCodeChange
{
    static void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); Console.WriteLine("PASS PLACE29-CODES " + text); }
    internal static async Task Run(string workspace, string root)
    {
        await using var h = new CameraRoundHarness(workspace, Path.Combine(root, "same-placement-code-change")) { PlacementReleaseMs = 5000 };
        await h.StartAsync();
        var vm = new SupplyStationViewModel(h.App); vm.SetViewport(1366, 768);
        uint frame = 0;
        long Processed() => h.App.WorkerProgress().Single(p => p.Position == "left").Processed;
        async Task Frame(CameraRoundHarness.RoundPixels pixels)
        {
            var before = Processed();
            h.Api(h.Left).Enqueue(pixels, ++frame);
            await CameraRoundHarness.Until(() => Task.FromResult(Processed() > before));
        }
        Task Read(string code) => Frame(CameraRoundHarness.Barcode(code));
        async Task Empty(int frames)
        {
            for (var i = 0; i < frames; i++)
            {
                var decoded = h.Factory.Decoders[h.Left].Count;
                h.Api(h.Left).Enqueue(CameraRoundHarness.EmptyTray, ++frame);
                await CameraRoundHarness.Until(() => Task.FromResult(h.Factory.Decoders[h.Left].Count > decoded));
                await Task.Delay(SportmasterFlow.DefaultSamePlacementEmptyMinimumMs / 2);
            }
        }
        async Task<ParcelInputSnapshot> Latest() => (await h.App.SnapshotAsync()).Inputs!.Single(i => i.PositionId == "left");
        async Task<ParcelOperation> Resolved(string code, ParcelState state = ParcelState.SlotResolved)
        {
            await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Any(o => o.ParcelCode == code && o.State == state));
            return (await h.App.SnapshotAsync()).Operations.Where(o => o.ParcelCode == code && o.State == state).MaxBy(o => o.ObservedAt)!;
        }
        var since = 0;
        int Requests() => h.Peer.Requests.Count - since;
        void Section() => since = h.Peer.Requests.Count;

        // 1. Fast swap without an empty tray in between: the new code is requested at once.
        await Read("SYNTH-CODES-A");
        var a = await Resolved("SYNTH-CODES-A");
        await Read("SYNTH-CODES-B");
        var b = await Resolved("SYNTH-CODES-B");
        Check(Requests() == 2, "a different code without an empty tray is still a new placement and requests at once");

        // 2. Codes alternate with no empty tray (two labels on one parcel, occlusion or glare):
        // each code already requested in this placement keeps its original operation.
        await Read("SYNTH-CODES-A");
        var back = await Latest();
        Check(Requests() == 2 && ParcelInputReasons.IsSamePlacement(back) && back.OperationId == a.Id,
            $"returning to an already requested code without an empty tray sends no new request (requests={Requests()})");
        await Read("SYNTH-CODES-B"); await Read("SYNTH-CODES-A"); await Read("SYNTH-CODES-B");
        var flip = await Latest();
        Check(Requests() == 2 && ParcelInputReasons.IsSamePlacement(flip) && flip.OperationId == b.Id &&
            (await h.App.SnapshotAsync()).Operations.Length == 2,
            $"alternating A/B reads of a resting parcel never grow into a request storm (requests={Requests()})");
        await vm.RefreshAsync();
        Check(vm.Left.Barcode == "SYNTH-CODES-B" && !vm.Left.InputUnadmitted,
            "the work card treats the alternating read as the same placement, not as an unadmitted scan");

        // 3. Both labels in one frame: the multi-code rule rejects the frame and leaves the placement alone.
        Section();
        await Frame(CameraRoundHarness.Multiple("SYNTH-CODES-A", "SYNTH-CODES-B"));
        var multiple = await Latest();
        Check(multiple.Outcome == ParcelInputOutcome.Rejected && Requests() == 0, "a frame with both labels is rejected by the multi-code rule without a request");
        await Read("SYNTH-CODES-A");
        Check(Requests() == 0 && (await Latest()).OperationId == a.Id, "after a rejected multi-code frame the resting parcel still belongs to its placement");

        // 4. Weak label: two no-code frames, then the label decodes again.
        await Empty(2);
        await Read("SYNTH-CODES-B");
        Check(Requests() == 0 && (await Latest()).OperationId == b.Id, "a weak label lost for two frames and recovered sends no new request");

        // 5. Empty tray ends the whole placement: the same code on the next parcel is requested again.
        await Empty(3); Section();
        await Read("SYNTH-CODES-A");
        var a2 = await Resolved("SYNTH-CODES-A");
        Check(Requests() == 1 && a2.Id != a.Id,
            "after an empty tray the same code (two identical parcels in a row) requests a new chute");
        await Read("SYNTH-CODES-B");
        var b2 = await Resolved("SYNTH-CODES-B");
        Check(Requests() == 2 && b2.Id != b.Id, "after an empty tray a code from the previous placement is a new placement as well");

        // 6. A different code that is not admitted (software pause) must not erase the
        // resting parcel's placement, or the parcel is requested again after resuming.
        await Empty(3); Section();
        await Read("SYNTH-CODES-C");
        var c = await Resolved("SYNTH-CODES-C");
        async Task Command(string kind) { var s = await h.App.SnapshotAsync(); await h.App.CommandAsync(new(Wire.Id(), s.Revision, kind, null, null)); }
        await Command("Pause");
        await Read("SYNTH-CODES-D");
        var blocked = await Latest();
        Check(blocked.Outcome == ParcelInputOutcome.Blocked && Requests() == 1, "a different code read during a software pause is blocked and sends nothing");
        await h.AuthorizeAsync(); await Command("Resume");
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "left").Admit);
        await Read("SYNTH-CODES-C");
        Check(Requests() == 1 && (await Latest()).OperationId == c.Id,
            $"after resuming, the resting parcel keeps its original operation (requests={Requests()})");

        // 7. Unknown original request: a later code may be admitted, but the Unknown code itself
        // is never re-requested while the parcel rests; its original request stays as it was.
        await Empty(3); Section();
        h.Peer.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await Read("SYNTH-CODES-E");
        var e = await Resolved("SYNTH-CODES-E", ParcelState.WcsUnknown);
        h.Peer.Hold.TrySetResult(); h.Peer.Hold = null;
        Check(Requests() == 1 && e.Wcs is { State: StageState.Unknown, Attempts: 1 }, "an RCS timeout leaves one Unknown original request");
        await Read("SYNTH-CODES-F");
        await Resolved("SYNTH-CODES-F");
        await Read("SYNTH-CODES-E");
        var unknown = (await h.App.SnapshotAsync()).Operations.Single(o => o.Id == e.Id);
        Check(Requests() == 2 && (await Latest()).OperationId == e.Id && unknown.Wcs is { State: StageState.Unknown, Attempts: 1 } &&
            unknown.Wcs.Request.Id == e.Wcs!.Request.Id && unknown.Wcs.Request.PayloadHash == e.Wcs.Request.PayloadHash,
            $"the Unknown code read again is neither resent nor re-requested; its original request is unchanged (requests={Requests()})");

        // 8. Positions are independent: a code held by the left placement is requested on the right.
        Section();
        h.Api(h.Right).Enqueue(CameraRoundHarness.Barcode("SYNTH-CODES-F"), 1);
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Any(o => o.PositionId == "right" && o.State == ParcelState.SlotResolved));
        Check(Requests() == 1, "the same code on the other position is its own placement");
        await h.App.StopAsync(default);
        Check(h.Api(h.Left).Acquires == h.Api(h.Left).Releases && h.Api(h.Right).Acquires == h.Api(h.Right).Releases, "every SDK frame loan is returned");
    }
}

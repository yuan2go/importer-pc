using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Platform;

// SM-PLACEMENT-PRESENCE-29: one tray placement = one RCS request.
// Real MVS driver pipeline, real ZXing, SQLite and RCS adapter; only the native port
// and the loopback RCS peer are synthetic. Physical camera/customer RCS NOT_RUN.
internal static class PlacementPresence29
{
    static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); Console.WriteLine("PASS PLACEMENT29 " + message); }
    internal static async Task Run(string workspace, string root)
    {
        await using var h = new CameraRoundHarness(workspace, Path.Combine(root, "placement29"), 30000) { PlacementClearMs = 400 };
        h.Config.Wcs!.Parameters!["request-timeout-ms"] = "30000";
        ExtensionStore.AtomicWrite(Path.Combine(h.Launch.ConfigurationDirectory, "business.json"), h.Config);
        await h.StartAsync();
        var source = h.Factory.Sources[h.Left]; uint frame = 0;
        static CameraRoundHarness.RoundPixels Blank() => new(new byte[100 * 80], 100, 80);
        // Offers one frame and waits until it is decoded (and, with a code, recorded).
        async Task<ParcelSnapshot> Frame(string? code)
        {
            var number = ++frame; var decoded = source.ReadDiagnostics().DecodeCompleted;
            h.Api(h.Left).Enqueue(code is null ? Blank() : CameraRoundHarness.Barcode(code), number);
            await CameraRoundHarness.Until(async () => source.ReadDiagnostics().DecodeCompleted > decoded && (code is null ||
                (await h.App.SnapshotAsync()).Inputs?.SingleOrDefault(i => i.PositionId == "left")?.Observation.SourceEventId == "mvs.frame." + number));
            return await h.App.SnapshotAsync();
        }
        async Task Cleared() { await h.ClearPlacementAsync(h.Left, frame + 1); frame += 2; }
        static ParcelInputSnapshot Left(ParcelSnapshot s) => s.Inputs!.Single(i => i.PositionId == "left");
        async Task Settled(string code, ParcelState state, int count) => await CameraRoundHarness.Until(async () =>
            (await h.App.SnapshotAsync()).Operations.Count(o => o.ParcelCode == code && o.State == state) == count);

        // 1. Parcel placed, slot assigned; it stays under the lens until the cart leaves.
        await Frame("SYNTH-PLACE-29-A"); await Settled("SYNTH-PLACE-29-A", ParcelState.SlotResolved, 1);
        var first = (await h.App.SnapshotAsync()).Operations.Single(o => o.ParcelCode == "SYNTH-PLACE-29-A");
        ParcelSnapshot s = null!;
        for (var i = 0; i < 3; i++) s = await Frame("SYNTH-PLACE-29-A");
        Check(h.Peer.Requests.Count == 1 && s.Operations.Count(o => o.ParcelCode == "SYNTH-PLACE-29-A") == 1,
            "a parcel that stays under the lens after its slot is assigned creates no second operation or RCS request");
        Check(Left(s) is { Outcome: ParcelInputOutcome.Duplicate, SamePlacement: true } repeat && repeat.OperationId == first.Id && !ParcelPresentation.IsUnadmitted(repeat),
            "each repeated frame is still recorded as an observation of the placement's own operation, not as an unadmitted input");
        Check(s.Positions!.Single(p => p.Id == "left").Admit, "a held placement does not block the position; only the same parcel is not re-sent");

        // 2. One missed decode is not a removal.
        await Frame(null); s = await Frame("SYNTH-PLACE-29-A");
        Check(h.Peer.Requests.Count == 1 && Left(s).SamePlacement, "a no-code frame shorter than the clear interval keeps the placement");

        // 3. Cart carries the tray away; the same code placed later is a new placement.
        await Cleared();
        await Frame("SYNTH-PLACE-29-A"); await Settled("SYNTH-PLACE-29-A", ParcelState.SlotResolved, 2);
        Check(h.Peer.Requests.Count == 2, "after the reading area stayed empty for the clear interval, the same code is a new placement with one new request");

        // 4. A different parcel replaces the held one without an empty interval.
        h.Peer.Mode = "reject";
        await Frame("SYNTH-PLACE-29-B"); await Settled("SYNTH-PLACE-29-B", ParcelState.WcsRejected, 1);
        Check(h.Peer.Requests.Count == 3, "a different code is a new parcel and is sent at once");

        // 5. RCS rejected: the parcel left on the tray is not re-sent frame after frame.
        for (var i = 0; i < 3; i++) s = await Frame("SYNTH-PLACE-29-B");
        var rejected = s.Operations.Single(o => o.ParcelCode == "SYNTH-PLACE-29-B");
        Check(h.Peer.Requests.Count == 3 && Left(s).OperationId == rejected.Id && !ParcelPresentation.IsUnadmitted(Left(s)),
            "a rejected parcel left under the lens is not re-requested and the display keeps its own rejected result");

        // 6. Operator lifts the same parcel off and puts it back: exactly one new request.
        h.Peer.Mode = "assigned";
        await Cleared();
        await Frame("SYNTH-PLACE-29-B"); await Settled("SYNTH-PLACE-29-B", ParcelState.SlotResolved, 1);
        Check(h.Peer.Requests.Count == 4, "lifting the parcel off and placing it back requests a slot exactly once more");

        // 7. Frames queued while RCS answers belong to that placement, even when they are
        //    processed after the reading area has become empty.
        await Cleared();
        h.Peer.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        uint last;
        try
        {
            var decoded = source.ReadDiagnostics().DecodeCompleted;
            h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-PLACE-29-C"), ++frame);
            await CameraRoundHarness.Until(() => Task.FromResult(h.Peer.Requests.Count == 5));
            for (var i = 0; i < 2; i++)
            {
                decoded = source.ReadDiagnostics().DecodeCompleted;
                h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-PLACE-29-C"), ++frame);
                await CameraRoundHarness.Until(() => Task.FromResult(source.ReadDiagnostics().DecodeCompleted > decoded && source.Undelivered is null));
            }
            last = frame;
            await Cleared();
            Check(h.Peer.Requests.Count == 5, "frames of a parcel waiting for its RCS answer create no request while it is pending");
            h.Peer.Hold.TrySetResult(); h.Peer.Hold = null;
            await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Inputs?.SingleOrDefault(i => i.PositionId == "left")?.Observation.SourceEventId == "mvs.frame." + last);
        }
        finally { h.Peer.Hold?.TrySetResult(); h.Peer.Hold = null; }
        s = await h.App.SnapshotAsync();
        Check(h.Peer.Requests.Count == 5 && s.Operations.Count(o => o.ParcelCode == "SYNTH-PLACE-29-C") == 1 && Left(s).SamePlacement,
            "queued frames observed before the empty interval stay with their placement when processed afterwards");
        await Frame("SYNTH-PLACE-29-C"); await Settled("SYNTH-PLACE-29-C", ParcelState.SlotResolved, 2);
        Check(h.Peer.Requests.Count == 6, "a frame observed after that empty interval is a new placement");

        Check(source.Failure.Length == 0 && h.Api(h.Left).Acquires == h.Api(h.Left).Releases,
            "presence gating holds no SDK loan and never fences the camera");
    }
}

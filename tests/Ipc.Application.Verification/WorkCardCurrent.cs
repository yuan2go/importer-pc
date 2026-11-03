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
    sealed class Clock(DateTimeOffset start) : TimeProvider
    {
        public TimeSpan Offset;
        public override DateTimeOffset GetUtcNow() => start + Offset + (DateTimeOffset.UtcNow - start);
    }

    internal static async Task Run(string workspace, string root)
    {
        var folder = Path.Combine(root, "work-card-current");
        string? first;
        await using (var h = new CameraRoundHarness(workspace, folder) { PlacementReleaseMs = 5000 })
        {
            await h.StartAsync();
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
            Check(s.Positions!.All(p => p.CurrentOperation is null) && (await h.App.HistoryAsync()).Items.Count(i => i.Operation.State == ParcelState.SlotResolved) == 2,
                "after a restart both cards wait for a parcel; earlier chutes are only in the records");
        }

        // Manual entry has no empty-tray signal: its result stays for at most ten minutes.
        await using (var m = new ManualSupplyHarness(workspace, Path.Combine(root, "work-card-manual"), deferred: true))
        {
            await m.StartAsync();
            var admitted = await m.App.SubmitParcelBarcodeAsync(await m.Command("right", "SYNTH-CARD-MANUAL"), default);
            await CameraRoundHarness.Until(async () => (await m.App.SnapshotAsync()).Operations.Any(o => o.State == ParcelState.SlotResolved));
            var id = (await m.App.SnapshotAsync()).Operations.Single().Id;
            Check((await m.App.SnapshotAsync()).Positions!.Single(p => p.Id == "right").CurrentOperation == id, "a keyed-in parcel shows its chute");
            var clock = new Clock(DateTimeOffset.UtcNow); m.App.StationTime = clock;
            clock.Offset = TimeSpan.FromMinutes(9);
            Check((await m.App.SnapshotAsync()).Positions!.Single(p => p.Id == "right").CurrentOperation == id, "it is still shown nine minutes later");
            clock.Offset = TimeSpan.FromMinutes(11);
            Check((await m.App.SnapshotAsync()).Positions!.Single(p => p.Id == "right").CurrentOperation is null, "after ten minutes without new input the card returns to waiting");
        }
    }
}

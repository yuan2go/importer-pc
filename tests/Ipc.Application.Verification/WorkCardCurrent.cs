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
            await h.WaitForCameraAdmissionAsync();
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
        await PausedCameraPicture(workspace, Path.Combine(root, "paused-current-picture"));
    }

    static async Task PausedCameraPicture(string workspace, string root)
    {
        await using var h = new CameraRoundHarness(workspace, root);
        await h.StartAsync(); await h.WaitForCameraAdmissionAsync();
        var before = await h.App.SnapshotAsync();
        Check((await h.App.CommandAsync(new(Wire.Id(), before.Revision, "Pause"))).Status == "Persisted", "picture fixture confirms the original software pause");
        var pixels = CameraRoundHarness.Barcode("SYNTH-PAUSED-PICTURE-A");
        h.Api(h.Left).Enqueue(pixels, 1);
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Inputs!.Any(i => i.PositionId == "left" && i.Outcome == ParcelInputOutcome.Blocked));
        var input = (await h.App.SnapshotAsync()).Inputs!.Single(i => i.PositionId == "left");
        var query = new ParcelPictureQuery(ObservationId: input.Observation.Id);
        var image = (await h.App.ReadParcelPictureAsync(query, default)).Image;
        Check(image is not null && image.Gray8.SequenceEqual(pixels.Data) && image.Metadata.Width == pixels.Width &&
            image.Metadata.Height == pixels.Height && image.Metadata.SourceEventId == input.Observation.SourceEventId,
            "a paused unadmitted scan retains its exact full original without creating a job");
        image!.Gray8[0] ^= 255;
        Check((await h.App.ReadParcelPictureAsync(query, default)).Image!.Gray8.SequenceEqual(pixels.Data), "returned picture bytes cannot mutate the retained evidence");
        Check(input.Reason.Contains("Global software pause") && (await h.App.SnapshotAsync()).Operations.Length == 0 && h.Peer.Requests.Count == 0,
            "blocked input logs the actual pause reason and viewing a picture sends no request");
        h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-PAUSED-PICTURE-B"), 2);
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Inputs!.Any(i => i.Observation.Codes.SequenceEqual(new[] { "SYNTH-PAUSED-PICTURE-B" })));
        Check((await h.App.ReadParcelPictureAsync(query, default)).Image is null, "a replaced observation cannot borrow the new parcel picture");
        await h.App.DisposeAsync(); await h.StartAsync();
        Check((await h.App.ReadParcelPictureAsync(query, default)).Image is null && (await h.App.SnapshotAsync()).Inputs is { Length: 0 },
            "a new shift restores neither blocked parcel pictures nor previous barcode inputs");
    }
}

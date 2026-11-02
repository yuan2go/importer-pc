using Ipc.Application.Contracts;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Platform;

// SM-SAME-PLACEMENT-29: one placement on the supply tray is one RCS request.
// Real ZXing, synthetic MVS frames, built-in RCS over loopback HTTP and the SM work card.
internal static class SamePlacement29
{
    static void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); Console.WriteLine("PASS PLACE29 " + text); }
    internal static async Task Run(string workspace, string root)
    {
        // A window wide enough that loaded CI frame gaps never look like a lift.
        await using var h = new CameraRoundHarness(workspace, Path.Combine(root, "same-placement-29")) { PlacementReleaseMs = 1500 };
        await h.StartAsync();
        var vm = new SupplyStationViewModel(h.App); vm.SetViewport(1366, 768);
        uint frame = 0;
        long Processed() => h.App.WorkerProgress().Single(p => p.Position == "left").Processed;
        async Task Read(string code)
        {
            var before = Processed();
            h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode(code), ++frame);
            await CameraRoundHarness.Until(() => Task.FromResult(Processed() > before));
        }
        async Task<ParcelInputSnapshot> Latest() => (await h.App.SnapshotAsync()).Inputs!.Single(i => i.PositionId == "left");

        // 1. Placed parcel: first read requests a chute.
        await Read("SYNTH-PLACE-A");
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Any(o => o.State == ParcelState.SlotResolved));
        var first = (await h.App.SnapshotAsync()).Operations.Single();
        Check(h.Peer.Requests.Count == 1, "the first read of a placed parcel sends exactly one RCS request");

        // 2. It rests on the tray until the vehicle leaves: every frame is read, none is requested again.
        for (var i = 0; i < 6; i++) await Read("SYNTH-PLACE-A");
        var resting = await Latest();
        Check(h.Peer.Requests.Count == 1 && (await h.App.SnapshotAsync()).Operations.Length == 1 &&
            ParcelInputReasons.IsSamePlacement(resting) && resting.OperationId == first.Id,
            "six further reads of the resting parcel are recorded against the original operation without another request");
        await CameraRoundHarness.Until(async () => { await vm.RefreshAsync(); return vm.Left.ShowConfirmedTask; });
        Check(vm.Left.DisplaySlot == "SYNTH-CHUTE" && vm.Left.Status == "投递格口" && !vm.Left.InputUnadmitted && vm.Left.Barcode == "SYNTH-PLACE-A",
            "the work card keeps showing the chute of this placement instead of a repeated-scan notice");

        // 3. A different parcel on the next tray is a new placement immediately.
        await Read("SYNTH-PLACE-B");
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Count(o => o.State == ParcelState.SlotResolved) == 2);
        Check(h.Peer.Requests.Count == 2, "a different code at the position requests its own chute without waiting");

        // 4. RCS rejects; the parcel stays on the tray: no retry storm.
        h.Peer.Mode = "reject";
        await h.ReplaceParcelAsync();
        await Read("SYNTH-PLACE-C");
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Any(o => o.ParcelCode == "SYNTH-PLACE-C" && o.State == ParcelState.WcsRejected));
        var rejected = (await h.App.SnapshotAsync()).Operations.Single(o => o.ParcelCode == "SYNTH-PLACE-C");
        for (var i = 0; i < 4; i++) await Read("SYNTH-PLACE-C");
        Check(h.Peer.Requests.Count == 3 && (await Latest()).OperationId == rejected.Id,
            "after a rejection the resting parcel is not re-requested on every frame");
        await vm.RefreshAsync();
        Check(vm.Left.Status == "未分配格口" && !vm.Left.ShowConfirmedTask && vm.Left.DisplaySlot == "",
            "the work card keeps the rejection of this placement so the operator removes the parcel");

        // 5. Operator lifts the parcel and places it again: one new request.
        h.Peer.Mode = "assigned";
        await h.ReplaceParcelAsync();
        await Read("SYNTH-PLACE-C");
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Any(o => o.ParcelCode == "SYNTH-PLACE-C" && o.State == ParcelState.SlotResolved));
        for (var i = 0; i < 3; i++) await Read("SYNTH-PLACE-C");
        Check(h.Peer.Requests.Count == 4 && (await h.App.SnapshotAsync()).Operations.Count(o => o.ParcelCode == "SYNTH-PLACE-C") == 2,
            "re-placing the same parcel after the release window requests a new chute exactly once");

        // 6. Statistics count requests, not every frame of a resting parcel.
        var now = DateTimeOffset.UtcNow; var minute = new DateTimeOffset(now.UtcDateTime.AddTicks(-(now.UtcTicks % TimeSpan.TicksPerMinute)), TimeSpan.Zero);
        var stats = await ((IStationOperations)h.App).ReadStatisticsAsync(minute.AddMinutes(-2), minute.AddMinutes(2), default);
        Check(stats.Buckets.Sum(b => b.Requests) == 4, "station statistics record four requests for four placements");

        // 7. The right position is independent.
        h.Api(h.Right).Enqueue(CameraRoundHarness.Barcode("SYNTH-PLACE-C"), 1);
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Any(o => o.PositionId == "right" && o.State == ParcelState.SlotResolved));
        Check(h.Peer.Requests.Count == 5, "the same code on the other position is its own placement");
        await h.App.StopAsync(default);
        Check(h.Api(h.Left).Acquires == h.Api(h.Left).Releases && h.Api(h.Right).Acquires == h.Api(h.Right).Releases, "every SDK frame loan is returned");
    }
}

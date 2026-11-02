using Ipc.Application.Contracts;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Application.Sportmaster;
using Ipc.Platform;

// SM-SAME-PLACEMENT-29: one placement on the supply tray is one RCS request.
// Real ZXing, synthetic MVS frames, built-in RCS over loopback HTTP and the SM work card.
internal static class SamePlacement29
{
    static void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); Console.WriteLine("PASS PLACE29 " + text); }
    internal static async Task Run(string workspace, string root)
    {
        // A window wide enough that loaded CI frame gaps never look like a lift.
        // New sites start with common 1D symbologies; the schema and the decoder accept them.
        var initial = new SportmasterFlow().InitialConfiguration();
        Check(initial.Topology!.Readers.All(r => Wire.Decode<string[]>(r.Parameters!["code.formats"]).SequenceEqual(new[] { "Code128", "Code39", "EAN13" }) &&
                !ExtensionSchema.Validate(Ipc.Drivers.Hikrobot.Mvs.BundledMvs.ReaderDescriptor.Parameters, r.Parameters).Any(e => e.Contains("code.formats", StringComparison.Ordinal))) &&
            Ipc.Drivers.Hikrobot.Mvs.ReaderSettings.Read(initial.Topology.Readers[0].Parameters!).Formats.SequenceEqual(new[] { "Code128", "Code39", "EAN13" }),
            "new-site template selects Code128, Code39 and EAN13 explicitly and passes the reader schema");
        await using var h = new CameraRoundHarness(workspace, Path.Combine(root, "same-placement-29")) { PlacementReleaseMs = 5000 };
        var siteDecoder = new Ipc.Drivers.Hikrobot.Mvs.SoftwareDecoder("utf-8", () => { }, settings: Ipc.Drivers.Hikrobot.Mvs.ReaderSettings.Read(initial.Topology.Readers[0].Parameters!));
        siteDecoder.Prepare();
        try
        {
            var siteLabel = CameraRoundHarness.Barcode("JYMW02263");
            Check(siteDecoder.Decode(siteLabel.Data, siteLabel.Data.Length, siteLabel.Width, siteLabel.Height).SequenceEqual(new[] { "JYMW02263" }),
                "the new-site symbology set decodes a site-style Code128 label");
        }
        finally { siteDecoder.Release(); }
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
        var blank = new CameraRoundHarness.RoundPixels(Enumerable.Repeat((byte)180, 640 * 240).ToArray(), 640, 240);
        async Task Empty(int frames)
        {
            for (var i = 0; i < frames; i++)
            {
                var decoded = h.Factory.Decoders[h.Left].Count;
                h.Api(h.Left).Enqueue(blank, ++frame);
                await CameraRoundHarness.Until(() => Task.FromResult(h.Factory.Decoders[h.Left].Count > decoded));
                await Task.Delay(SportmasterFlow.DefaultSamePlacementEmptyMinimumMs / 2);
            }
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
        await Empty(3);
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
        // Site decode runs about 1.5 s per frame: a long gap without decoded frames is
        // no evidence the parcel left, even beyond the fallback window.
        await Task.Delay(h.PlacementReleaseMs + 1000);
        await Read("SYNTH-PLACE-C");
        Check(h.Peer.Requests.Count == 3 && ParcelInputReasons.IsSamePlacement(await Latest()),
            "a slow decode gap longer than the fallback window does not re-request the resting parcel");
        await Empty(3);
        await Read("SYNTH-PLACE-C");
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Any(o => o.ParcelCode == "SYNTH-PLACE-C" && o.State == ParcelState.SlotResolved));
        for (var i = 0; i < 3; i++) await Read("SYNTH-PLACE-C");
        Check(h.Peer.Requests.Count == 4 && (await h.App.SnapshotAsync()).Operations.Count(o => o.ParcelCode == "SYNTH-PLACE-C") == 2,
            "lifting the parcel (empty tray seen) and placing it again requests a new chute exactly once");

        // 6. Glare: one or two frames without a code while the parcel rests do not end the placement.
        await Empty(2);
        await Read("SYNTH-PLACE-C");
        Check(h.Peer.Requests.Count == 4 && ParcelInputReasons.IsSamePlacement(await Latest()),
            "two no-code frames on a resting parcel (glare) do not trigger another request");

        // 6b. Glare flicker: three quick no-code frames shorter than the minimum empty time.
        for (var i = 0; i < 3; i++)
        {
            var decoded = h.Factory.Decoders[h.Left].Count;
            h.Api(h.Left).Enqueue(blank, ++frame);
            await CameraRoundHarness.Until(() => Task.FromResult(h.Factory.Decoders[h.Left].Count > decoded));
        }
        await Read("SYNTH-PLACE-C");
        Check(h.Peer.Requests.Count == 4 && ParcelInputReasons.IsSamePlacement(await Latest()),
            "three no-code frames within the minimum empty time (glare while handling) do not trigger another request");

        // 7. Empty tray, then the same code again well inside the time window: the operator
        // lifted and quickly re-placed it, or the next vehicle brings an identical code.
        var lastRead = System.Diagnostics.Stopwatch.StartNew();
        await Empty(3);
        await Read("SYNTH-PLACE-C");
        var gap = lastRead.ElapsedMilliseconds;
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Count(o => o.ParcelCode == "SYNTH-PLACE-C" && o.State == ParcelState.SlotResolved) == 2);
        Check(h.Peer.Requests.Count == 5 && gap < h.PlacementReleaseMs,
            $"three no-code frames (empty tray) end the placement, so the same code {gap} ms later requests a new chute");
        for (var i = 0; i < 3; i++) await Read("SYNTH-PLACE-C");
        Check(h.Peer.Requests.Count == 5, "the re-placed parcel resting on the tray again requests only once");

        // 8. Statistics count requests, not every frame of a resting parcel.
        var now = DateTimeOffset.UtcNow; var minute = new DateTimeOffset(now.UtcDateTime.AddTicks(-(now.UtcTicks % TimeSpan.TicksPerMinute)), TimeSpan.Zero);
        var stats = await ((IStationOperations)h.App).ReadStatisticsAsync(minute.AddMinutes(-2), minute.AddMinutes(2), default);
        Check(stats.Buckets.Sum(b => b.Requests) == 5, "station statistics record five requests for five placements");

        // 9. The right position is independent.
        h.Api(h.Right).Enqueue(CameraRoundHarness.Barcode("SYNTH-PLACE-C"), 1);
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Any(o => o.PositionId == "right" && o.State == ParcelState.SlotResolved));
        Check(h.Peer.Requests.Count == 6, "the same code on the other position is its own placement");
        await h.App.StopAsync(default);
        Check(h.Api(h.Left).Acquires == h.Api(h.Left).Releases && h.Api(h.Right).Acquires == h.Api(h.Right).Releases, "every SDK frame loan is returned");
    }
}

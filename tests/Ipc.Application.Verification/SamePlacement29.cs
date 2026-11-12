using Ipc.Application.Contracts;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Application.Sportmaster;
using Ipc.Platform;
using Ipc.Drivers.Hikrobot.Mvs;

// SM-SAME-PLACEMENT-29: one placement on the supply tray is one RCS request.
// Real ZXing, synthetic MVS frames, built-in RCS over loopback HTTP and the SM work card.
internal static class SamePlacement29
{
    static void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); Console.WriteLine("PASS PLACE29 " + text); }
    internal static async Task Run(string workspace, string root)
    {
        // A window wide enough that loaded CI frame gaps never look like a lift.
        // New sites accept the complete native-reader result set.
        var initial = new SportmasterFlow().InitialConfiguration();
        Check(initial.Topology!.Readers.All(r => r.Parameters!["reader.engine"] == "mvid-native" && Wire.Decode<string[]>(r.Parameters["code.formats"]).SequenceEqual(new[] { "All" }) &&
                !ExtensionSchema.Validate(Ipc.Drivers.Hikrobot.Mvs.BundledMvs.ReaderDescriptor.Parameters, r.Parameters).Any(e => e.Contains("code.formats", StringComparison.Ordinal))) &&
            Ipc.Drivers.Hikrobot.Mvs.ReaderSettings.Read(initial.Topology.Readers[0].Parameters!).Formats.SequenceEqual(new[] { "All" }),
            "new-site template selects native reading with the complete code list and passes the reader schema");
        await using var h = new CameraRoundHarness(workspace, Path.Combine(root, "same-placement-29")) { PlacementReleaseMs = 5000, StationClock = new TimeStorageFaultChecks.ManualTime() };
        var siteDecoder = new Ipc.Drivers.Hikrobot.Mvs.SoftwareDecoder("utf-8", () => { }, settings: Ipc.Drivers.Hikrobot.Mvs.ReaderSettings.Read(initial.Topology.Readers[0].Parameters!));
        siteDecoder.Prepare();
        try
        {
            var siteLabel = CameraRoundHarness.Barcode("JYMW02263");
            Check(siteDecoder.Decode(siteLabel.Data, siteLabel.Data.Length, siteLabel.Width, siteLabel.Height).SequenceEqual(new[] { "JYMW02263" }),
                "the software decoder also accepts the complete symbology set for a site-style Code128 label");
        }
        finally { siteDecoder.Release(); }
        await h.StartAsync();
        await h.WaitForCameraAdmissionAsync();
        await h.TrayAsync(h.Left, TrayPresence.Present);
        var vm = new SupplyStationViewModel(h.App); vm.SetViewport(1366, 768);
        uint frame = 0;
        long Processed() => h.App.WorkerProgress().Single(p => p.Position == "left").Processed;
        async Task Read(string code)
        {
            var before = Processed();
            await Deliver(h, h.Left, CameraRoundHarness.Barcode(code), ++frame);
            await CameraRoundHarness.Until(() => Task.FromResult(Processed() > before));
        }
        var blank = new CameraRoundHarness.RoundPixels(Enumerable.Repeat((byte)180, 640 * 240).ToArray(), 640, 240);
        async Task Empty(int frames)
        {
            for (var i = 0; i < frames; i++)
            {
                await Deliver(h, h.Left, blank, ++frame);
                await Task.Delay(CameraRoundHarness.SyntheticBlankSpanMs / 2);
            }
            if (frames >= 3) { await h.TrayAsync(h.Left, TrayPresence.Absent); await SmRoundFixture.NextVehicleAsync(h.App, "left"); await h.TrayAsync(h.Left, TrayPresence.Present); }
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
            resting.OperationId == first.Id,
            "six further reads of the resting parcel are recorded against the original operation without another request");
        await CameraRoundHarness.Until(async () => { await vm.RefreshAsync(); return vm.Left.ShowConfirmedTask; });
        Check(vm.Left.DisplaySlot == "SYNTH-CHUTE" && vm.Left.Status == "投递格口" && !vm.Left.InputUnadmitted && vm.Left.Barcode == "SYNTH-PLACE-A",
            "the work card keeps showing the chute of this placement instead of a repeated-scan notice");

        // 3. A different parcel on the next tray is a new placement immediately.
        await Read("SYNTH-PLACE-B");
        Check(h.Peer.Requests.Count == 1, "a changed barcode without physical removal remains the same round");
        await Empty(3); await Read("SYNTH-PLACE-B");
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Count(o => o.State == ParcelState.SlotResolved) == 2);
        Check(h.Peer.Requests.Count == 2, "a qualified new placement requests its own chute without waiting for the old request");

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
        Check(h.Peer.Requests.Count == 3 && (await Latest()).OperationId is not null,
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
        Check(h.Peer.Requests.Count == 4 && (await Latest()).OperationId is not null,
            "two no-code frames on a resting parcel (glare) do not trigger another request");

        // 6b. Glare flicker: three quick no-code frames shorter than the minimum empty time.
        for (var i = 0; i < 3; i++)
        {
            await Deliver(h, h.Left, blank, ++frame);
        }
        await Read("SYNTH-PLACE-C");
        Check(h.Peer.Requests.Count == 4 && (await Latest()).OperationId is not null,
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

        // 10. The camera reconnects (GigE packet loss) while parcel C rests on the left tray: the new connection
        // reads the same code, which is still the same placement, and the card shows its chute again.
        var oldEpoch = h.Factory.Sources[h.Left].Epoch;
        await h.App.ReconnectDeviceAsync(h.Left);
        await CameraRoundHarness.Until(async () => h.Factory.Sources[h.Left].Epoch != oldEpoch &&
            (await h.App.SnapshotAsync()).Devices!.Single(d => d.DeviceId == h.Left).IsValid(DateTimeOffset.UtcNow));
        await Read("SYNTH-PLACE-C");
        var reread = await Latest();
        await vm.RefreshAsync();
        Check(h.Peer.Requests.Count == 6 && reread.OperationId is not null && reread.Observation.Epoch == oldEpoch &&
            vm.Left.ShowConfirmedTask && vm.Left.DisplaySlot == "SYNTH-CHUTE" && vm.Left.DisplayStatus != "等待重新扫码",
            "after reconnect the current round retains its original acquisition and no second request is made");
        await h.App.StopAsync(default);
        Check(h.Api(h.Left).Acquires == h.Api(h.Left).Releases && h.Api(h.Right).Acquires == h.Api(h.Right).Releases, "every SDK frame loan is returned");
        await StillScene(workspace, root);
    }

    // With "放稳后再识别" on (as shipped), the decoder missing a parcel that never moved is not an empty tray:
    // the frames keep the scene of the frame that read the code. A real lift changes the scene and still counts.
    static async Task StillScene(string workspace, string root)
    {
        await using var h = new CameraRoundHarness(workspace, Path.Combine(root, "same-placement-scene"), stillMs: "40") { PlacementReleaseMs = 5000, StationClock = new TimeStorageFaultChecks.ManualTime() };
        await h.StartAsync();
        await h.WaitForCameraAdmissionAsync();
        await h.TrayAsync(h.Left, TrayPresence.Present);
        uint frame = 0;
        var decoder = h.Factory.Decoders[h.Left];
        // Delivers the same picture until the camera has decoded `decodes` more frames of it.
        async Task<MvsFrameLatency[]> Show(CameraRoundHarness.RoundPixels picture, int decodes, int spacingMs)
        {
            var completed = new List<MvsFrameLatency>();
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (completed.Count < decodes && DateTime.UtcNow < deadline)
            {
                var delivered = await Deliver(h, h.Left, picture, ++frame);
                if (delivered.DecodeAttempted) completed.Add(delivered);
                await Task.Delay(spacingMs);
            }
            if (completed.Count < decodes) throw new TimeoutException("synthetic camera did not decode the still picture");
            return completed.ToArray();
        }
        async Task<ParcelInputSnapshot> Latest() => (await h.App.SnapshotAsync()).Inputs!.Single(i => i.PositionId == "left");
        var parcel = CameraRoundHarness.Barcode("SYNTH-SCENE-A");

        await Show(parcel, 1, 60);
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Any(o => o.State == ParcelState.SlotResolved));
        Check(h.Peer.Requests.Count == 1, "with the still check on, the resting parcel's first settled read requests once");
        var vm = new SupplyStationViewModel(h.App); vm.SetViewport(1366, 768);
        await vm.RefreshAsync();

        decoder.MissNext = int.MaxValue;
        await Show(parcel, 8, 150);
        await vm.RefreshAsync();
        Check(vm.Left.ShowConfirmedTask && vm.Left.Barcode == "SYNTH-SCENE-A" && vm.Left.CanViewCurrentPicture &&
            vm.Snapshot!.CameraReads.Single(c => c.PositionId == "left").Frame.Outcome == ObservationReadOutcome.NoCode,
            "missed decodes on an unchanged scene retain the current chute, barcode and original picture before any successful reread");
        var reopened = new SupplyStationViewModel(h.App); reopened.SetViewport(1366, 768); await reopened.RefreshAsync();
        Check(reopened.Left.ShowConfirmedTask, "a new work-card subscriber also retains the application-owned unchanged placement");
        decoder.MissNext = 0;
        decoder.RejectNext = int.MaxValue;
        await Show(parcel, 1, 60);
        await CameraRoundHarness.Until(async () => { await vm.RefreshAsync(); return vm.Snapshot!.CameraReads.Single(c => c.PositionId == "left").Frame.Outcome == ObservationReadOutcome.RejectedFrame; });
        Check(vm.Left.ShowConfirmedTask && h.Peer.Requests.Count == 1,
            "a rejected decode on the same unchanged scene preserves the current result without another request");
        decoder.RejectNext = 0;
        await Show(parcel, 1, 60);
        await CameraRoundHarness.Until(async () => (await Latest()).OperationId is not null);
        Check(h.Peer.Requests.Count == 1 && (await h.App.SnapshotAsync()).Operations.Length == 1,
            "eight missed decodes over more than a second on an unmoved scene do not end the placement: no second request");

        var oldEpoch = h.Factory.Sources[h.Left].Epoch;
        await h.App.ReconnectDeviceAsync(h.Left);
        await CameraRoundHarness.Until(() => Task.FromResult(h.Factory.Sources[h.Left].Epoch != oldEpoch));
        decoder = h.Factory.Decoders[h.Left]; decoder.MissNext = int.MaxValue;
        await Show(parcel, 1, 60);
        await vm.RefreshAsync();
        Check(vm.Left.ShowConfirmedTask && vm.Snapshot!.Positions!.Single(p => p.Id == "left").CurrentRoundId is not null,
            "a new camera epoch does not replace current-round authority or authorize another request");
        decoder.MissNext = 0;
        await Show(parcel, 1, 60);
        await CameraRoundHarness.Until(async () => { await vm.RefreshAsync(); return vm.Left.ShowConfirmedTask; });
        Check(h.Peer.Requests.Count == 1, "rereading after reconnect restores the original chute without another request");

        var empty = await Show(CameraRoundHarness.EmptyTray, 4, 300);
        Check(empty.Length >= CameraRoundHarness.SyntheticBlankFrames &&
            empty[^1].DeliveredAt - empty[0].DeliveredAt >= TimeSpan.FromMilliseconds(CameraRoundHarness.SyntheticBlankSpanMs),
            "empty-tray evidence contains enough completed frames spanning the actual minimum release time");
        await vm.RefreshAsync();
        Check(vm.Left.ShowConfirmedTask && h.Peer.Requests.Count == 1, "NoCode and changed scene alone cannot end the current round");
        await h.TrayAsync(h.Left, TrayPresence.Absent);
        await vm.RefreshAsync();
        Check(!vm.Left.ShowConfirmedTask && !vm.Left.CanViewCurrentPicture, "qualified removal clears current result and picture");
        await Show(parcel, 1, 60);
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Count(o => o.State == ParcelState.SlotResolved) == 2);
        Check(h.Peer.Requests.Count == 2, "lifting the parcel changes the scene, the empty tray is seen, and placing it again requests once");
        await h.App.StopAsync(default);
    }

    static async Task<MvsFrameLatency> Deliver(CameraRoundHarness h, string device, CameraRoundHarness.RoundPixels picture, uint number)
    {
        // A decode feedback event precedes durable handoff. Wait for this exact
        // frame's completed trace before changing the scene or reconnecting; no
        // queued synthetic frame may turn into an undelivered original at stop.
        h.Api(device).Enqueue(picture, number);
        MvsFrameLatency? completed = null;
        await CameraRoundHarness.Until(() =>
        {
            completed = h.Factory.Sources[device].Timings.Snapshot().RecentFrames.SingleOrDefault(f => f.Number == number);
            return Task.FromResult(completed is not null);
        });
        return completed!;
    }
}

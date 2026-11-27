using Ipc.Application.Contracts;
using Ipc.Platform;
using System.Diagnostics;
using System.Text.Json;
namespace Ipc.Verification;

internal static class SmArrivalCameraChecks
{
    static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); Console.WriteLine("PASS ARRIVALCAM " + message); }
    public static async Task Run(string workspace, string root, bool native)
    {
        // This fixture checks arrival and dispatch, with both timing paths in
        // the report. The dedicated deadline fixture covers short RCS budgets.
        await using var h = new CameraRoundHarness(workspace, Path.Combine(root, native ? "native" : "software"),
            nativeResults: native, barcodeFilter: new("", "BG-[0-9]+"), rcsTimeoutMs: 10000);
        await h.StartAsync();
        var api = h.Api(h.Left);
        uint frame = 1;
        var barcode = CameraRoundHarness.Barcode("SAME-CAMERA-001");
        barcode = barcode with { Data = barcode.Data.Select(p => p < 128 ? (byte)50 : (byte)180).ToArray() };
        var empty = new CameraRoundHarness.RoundPixels(Enumerable.Repeat((byte)180, barcode.Width * barcode.Height).ToArray(), barcode.Width, barcode.Height);
        RecognitionSymbol[] symbols = [new("SAME-CAMERA-001", "Code128", [new(0, 0), new(barcode.Width - 1, 0), new(barcode.Width - 1, barcode.Height - 1), new(0, barcode.Height - 1)], 0)];
        void Queue()
        {
            if (native) api.EnqueueNative(barcode, frame++, symbols);
            else api.Enqueue(barcode, frame++);
        }
        async Task Arrive(string vehicle)
        {
            h.Peer.Vehicles["TEST-LEFT-READER"] = vehicle;
            if (await SmRoundFixture.CompletedHeartbeatAsync(h.App) != RcsHeartbeatResult.Received)
                throw new InvalidOperationException("Vehicle change was not acknowledged: " + vehicle);
            await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "left").Arrival?.VehicleNo == vehicle);
        }
        Task Ready() => h.WaitForCameraAdmissionAsync(h.Left);
        async Task QueueReadyFrame()
        {
            await Ready();
            var before = h.Handled(h.Left);
            var number = frame;
            Queue();
            await h.WaitFrameAsync(h.Left, number, before);
        }
        async Task Accepted()
        {
            await CameraRoundHarness.Until(async () =>
            {
                var round = (await h.App.SnapshotAsync()).Rounds.SingleOrDefault(r => r.PositionId == "left");
                if (round?.RcsOutcome is "Unknown" or "Rejected" or "Cancelled")
                    throw new InvalidOperationException("Unexpected result for the synthetic vehicle: " + Wire.Encode(round));
                return round?.RcsOutcome == "Accepted";
            });
        }
        var source = h.Factory.Sources[h.Left];
        source.CalibrateEmptyTray();
        for (var sample = 0; sample < 19; sample++)
        {
            var before = h.Handled(h.Left); var targetFrame = frame;
            if (native) api.EnqueueNative(empty, frame++, []); else api.Enqueue(empty, frame++);
            await h.WaitFrameAsync(h.Left, targetFrame, before);
            await Task.Delay(100);
        }
        Check(source.ReadDiagnostics().TrayPresenceModel?.Calibrated == true, "actual stable frame span teaches a static reference: " + Wire.Encode(source.ReadDiagnostics().TrayPresenceModel));
        var reference = source.ReadDiagnostics().TrayPresenceModel!.ReferenceSha256;
        // Keep streaming the initial parcel if fixture handoff resets the frame span.
        for (var sample = 0; sample < 8 && (sample < 3 || source.TrayPresenceStatus != "包裹在位"); sample++)
        {
            var before = h.Handled(h.Left); var targetFrame = frame; Queue();
            await h.WaitFrameAsync(h.Left, targetFrame, before);
            await Task.Delay(70);
        }
        await CameraRoundHarness.Until(() => Task.FromResult(h.Peer.Requests.Count == 1));
        Check(source.ReadDiagnostics().TrayPresenceModel!.Presence == TrayPresence.Present,
            "calibrated acquisition confirms parcel on A before switching: native=" + native);
        var a = (await h.App.SnapshotAsync()).Rounds.Single(r => r.PositionId == "left");
        await CameraRoundHarness.Until(async () => (await h.App.OperationAsync(a.OperationId!))?.OriginalFrame?.State == "Saved");
        var operation = (await h.App.OperationAsync(a.OperationId!))!;
        Check(operation.Acquisition is { CaptureTicks: > 0, FrameNumber: > 0, ArrivalGeneration: not null } acquisition && acquisition.ArrivalGeneration == a.ArrivalGeneration,
            "capture identity and monotonic host acquisition time survive original-image persistence: native=" + native);

        using var acquireEntered = new ManualResetEventSlim(false);
        using var acquireRelease = new ManualResetEventSlim(false);
        api.BeforeAcquireReturn = () => { acquireEntered.Set(); acquireRelease.Wait(); };
        try
        {
            Queue();
            Check(acquireEntered.Wait(TimeSpan.FromSeconds(5)), "old SDK frame is borrowed at vehicle change: native=" + native);
            await Arrive("CAMERA-B");
            var s = await h.App.SnapshotAsync();
            Check(s.Rounds.All(r => r.PositionId != "left") && s.Positions!.Single(p => p.Id == "left").Arrival is { CameraBoundaryReady: false },
                "empty B inherits neither A's round nor ready capture while SDK is borrowed: native=" + native);
            var right = await h.App.SubmitParcelBarcodeAsync(new(Wire.Id(), s.RuntimeId, s.Configuration.Revision, "right", "RIGHT-MANUAL", ParcelBarcodeInputKind.Keyboard, null, null)
            { ExpectedArrivalGeneration = s.Positions!.Single(p => p.Id == "right").Arrival!.Generation, ExpectedPlacementBoundary = s.Positions!.Single(p => p.Id == "right").Arrival!.PlacementBoundary }, default);
            Check(right.Status == "Admitted", "SDK drain does not hold business gate or lock the other lane: native=" + native);
        }
        finally { api.BeforeAcquireReturn = null; acquireRelease.Set(); }
        await Ready();
        await CameraRoundHarness.Until(() => Task.FromResult(api.Releases == api.Acquires));
        await CameraRoundHarness.Until(() => Task.FromResult(h.Peer.Requests.Count == 2));
        Check(source.ReadDiagnostics().TrayPresenceModel is { Presence: TrayPresence.Unknown, Calibrated: true } model && model.ReferenceSha256 == reference,
            "new empty vehicle resets physical accumulation while preserving static reference: native=" + native);
        Check(h.Peer.Requests.Count == 2 && (await h.App.SnapshotAsync()).Rounds.All(r => r.PositionId != "left"),
            "old borrowed frame is retired normally without a new request or source failure: native=" + native);
        var bottom = CameraRoundHarness.Barcode("BG-001");
        var bottomCanvas = Enumerable.Repeat((byte)180, barcode.Width * barcode.Height).ToArray();
        for (var y = 0; y < bottom.Height; y++)
            for (var x = 0; x < bottom.Width; x++) bottomCanvas[y * barcode.Width + x] = bottom.Data[y * bottom.Width + x] < 128 ? (byte)50 : (byte)180;
        var emptyOtherCar = new CameraRoundHarness.RoundPixels(bottomCanvas, barcode.Width, barcode.Height);
        for (var sample = 0; sample < 3; sample++)
        {
            var target = frame;
            if (native) api.EnqueueNative(emptyOtherCar, frame++, [new("BG-001", "Code128", symbols[0].Corners, 0)]);
            else api.Enqueue(emptyOtherCar, frame++);
            await CameraRoundHarness.Until(() => Task.FromResult(source.Timings.Snapshot().RecentFrames.Any(f => f.Number == target && f.DecodeAttempted)));
            await Task.Delay(70);
        }
        Check(source.ReadDiagnostics().TrayPresenceModel!.Presence == TrayPresence.Present && (await h.App.SnapshotAsync()).Rounds.All(r => r.PositionId != "left"),
            "different empty-car bottom pattern cannot manufacture package presence or a timeout: native=" + native);
        Queue();
        await CameraRoundHarness.Until(() => Task.FromResult(h.Peer.Requests.Count == 3));
        Check((await h.App.SnapshotAsync()).Rounds.Single(r => r.PositionId == "left").Id != a.Id,
            "same-code new car succeeds without any empty-tray interval: native=" + native);

        // Hold the old decoder as a distinct boundary from the SDK loan.
        if (!native)
        {
            using var decoding = new ManualResetEventSlim(false);
            using var release = new ManualResetEventSlim(false);
            h.Factory.Decoders[h.Left].BeforeDecode = () => { decoding.Set(); release.Wait(); };
            try
            {
                Queue(); Check(decoding.Wait(TimeSpan.FromSeconds(5)), "old software decoder owns an active result");
                await Arrive("CAMERA-C");
                Check(!(await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "left").Arrival!.CameraBoundaryReady,
                    "new camera boundary awaits actual old decode completion");
            }
            finally { h.Factory.Decoders[h.Left].BeforeDecode = null; release.Set(); }
            await Ready();
            Check(h.Peer.Requests.Count == 3, "late old decoder cannot claim C and does not fence the source");
        }
        // Same-car physical removal and safe continuation through both real
        // acquisition implementations, independent of barcode recognition.
        await Arrive("CONTINUOUS-SAME-CAR"); await Ready();
        var occupiedBytes = (byte[])empty.Data.Clone();
        for (var y = 10; y < barcode.Height - 10; y++)
            for (var x = barcode.Width / 4; x < barcode.Width * 3 / 4; x++) occupiedBytes[y * barcode.Width + x] = 50;
        var unread = new CameraRoundHarness.RoundPixels(occupiedBytes, barcode.Width, barcode.Height);
        async Task Frames(CameraRoundHarness.RoundPixels pixels, RecognitionSymbol[] nativeCodes, int count)
        {
            for (var i = 0; i < count; i++)
            {
                var before = h.Handled(h.Left); var targetFrame = frame;
                if (native) api.EnqueueNative(pixels, frame++, nativeCodes); else api.Enqueue(pixels, frame++);
                await h.WaitFrameAsync(h.Left, targetFrame, before);
                await Task.Delay(100);
            }
        }
        await Frames(empty, [], 11); await Frames(unread, [], 4);
        var unreadRound = (await h.App.SnapshotAsync()).Rounds.Single(r => r.PositionId == "left");
        Check(unreadRound.OperationId is null && h.Peer.Requests.Count == 3, "E01 real image presence creates an unread parcel without HTTP: native=" + native);
        await Frames(empty, [], 2);
        Check((await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "left").CurrentRoundId == unreadRound.Id,
            "two empty frames are insufficient to end the unread round: native=" + native);
        await Frames(empty, [], 3); await Ready();
        Check((await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "left") is { CurrentRoundId: null, BarcodeInput.Camera: true } nextLane &&
            nextLane.Arrival!.Generation == unreadRound.ArrivalGeneration && h.Peer.Requests.Count == 3,
            "N03/E01 actual 300ms empty evidence closes unread parcel and readies same car: native=" + native);
        await Frames(unread, [], 14);
        Check((await h.App.SnapshotAsync()).Rounds.Single(r => r.PositionId == "left") is { AutoExpired: true, SafeContinuation: true },
            "E09 capture continuity survives the fast deadline without inventing a new parcel: native=" + native);
        await Frames(barcode, symbols, 8);
        await CameraRoundHarness.Until(() => Task.FromResult(h.Peer.Requests.Count == 4));
        Check((await h.App.SnapshotAsync()).Rounds.Single(r => r.PositionId == "left").Id != unreadRound.Id,
            "N01/E09 same-car next parcel automatically dispatches once after bounded continuation: native=" + native);
        api.FailNextClear = true;
        await Arrive("CAMERA-FAIL");
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "left").Arrival?.CaptureFailure is not null);
        Check(!(await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "left").Arrival!.CameraBoundaryReady,
            "failed SDK clear never fabricates readiness: native=" + native);
        await Arrive("CAMERA-AFTER-FAIL");
        await QueueReadyFrame();
        await Accepted();
        Check(h.Peer.Requests.Count == 5, "restored arrival dispatches once after the failed clear");
        Check(api.Opens == 1 && api.Destroys == 0 && api.Releases == api.Acquires,
            "new arrival recovers on original owner without per-car reconnect or leaked SDK loan: native=" + native);
        // Short measured run of the same-code automatic path. Timing comes
        // from the exact acquisition/result event, not this polling loop.
        List<object> samples = [];
        double[] captureToResult = new double[30], rcs = new double[30];
        for (var i = 0; i < captureToResult.Length; i++)
        {
            await Arrive("MEASURE-" + i);
            await QueueReadyFrame();
            await Accepted();
            var measured = (await h.App.SnapshotAsync()).Rounds.Single(r => r.PositionId == "left");
            var events = JsonSerializer.SerializeToElement(h.App.RuntimeProgress()).GetProperty("Flow").GetProperty("Recent");
            var result = events.EnumerateArray().Single(e => e.GetProperty("Kind").GetString() == "RcsSubmitCompleted" && e.GetProperty("OperationId").GetString() == measured.OperationId).Clone();
            captureToResult[i] = result.GetProperty("CaptureToEventMs").GetDouble(); rcs[i] = result.GetProperty("ElapsedMs").GetDouble();
            using var process = Process.GetCurrentProcess();
            samples.Add(new { Round = measured.Id, Result = result, Frames = source.Timings.Snapshot().RecentFrames.Where(f => f.ObservationId == result.GetProperty("ObservationId").GetString()).ToArray(),
                process.PrivateMemorySize64, process.HandleCount, CpuMs = process.TotalProcessorTime.TotalMilliseconds });
        }
        Check(h.Peer.Requests.Count == 35 && api.Opens == 1 && api.Destroys == 0 && api.Acquires == api.Releases,
            "30 measured same-code automatic arrivals each make exactly one POST with one camera owner and balanced SDK loans: native=" + native);
        static object Percentiles(double[] values)
        {
            var sorted = values.Order().ToArray();
            double At(double p) => sorted[(int)Math.Ceiling(p * sorted.Length) - 1];
            return new { Count = sorted.Length, P50 = At(.50), P95 = At(.95), P99 = At(.99) };
        }
        var measurement = new { Native = native, Scope = "Synthetic SDK and physical frames; actual acquisition pipeline, SQLite and loopback HTTP; short sample, not field throughput or 24h qualification",
            CaptureToDurableResultMs = Percentiles(captureToResult), RcsCallMs = Percentiles(rcs), Samples = samples };
        var output = Path.Combine(root, native ? "continuous-native-performance.json" : "continuous-software-performance.json");
        ExtensionStore.AtomicWrite(output, measurement);
        Console.WriteLine("MEASURE " + output + " " + Wire.Encode(measurement.CaptureToDurableResultMs));

        // A refusal must not close acquisition/decoding for later parcels.
        // Exercise actual images through each supported acquisition route.
        await Frames(empty, [], 11); await Ready();
        h.Peer.Mode = "reject";
        await Frames(barcode, symbols, 8);
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Rounds.Single(r => r.PositionId == "left").RcsOutcome == "Rejected");
        var refused = (await h.App.SnapshotAsync()).Rounds.Single(r => r.PositionId == "left");
        await Frames(empty, [], 11); await Ready();
        h.Peer.Mode = "assigned";
        await Frames(barcode, symbols, 8);
        await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Rounds.Single(r => r.PositionId == "left").RcsOutcome == "Accepted");
        var next = (await h.App.SnapshotAsync()).Rounds.Single(r => r.PositionId == "left");
        Check(h.Peer.Requests.Count == 37 && next.Id != refused.Id && next.ArrivalGeneration == refused.ArrivalGeneration,
            "refusal then real empty tray and replacement restore actual decoding and exactly one next HTTP without toggling or changing car: native=" + native);
    }
}

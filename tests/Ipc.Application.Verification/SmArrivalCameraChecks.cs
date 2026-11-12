using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Verification;

internal static class SmArrivalCameraChecks
{
    static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); Console.WriteLine("PASS ARRIVALCAM " + message); }
    public static async Task Run(string workspace, string root)
    {
        foreach (var native in new[] { false, true })
        {
            await using var h = new CameraRoundHarness(workspace, Path.Combine(root, native ? "native" : "software"), nativeResults: native, barcodeFilter: new("", "BG-[0-9]+"));
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
                await ((IStationRcsHeartbeat)h.App).CheckRcsHeartbeatAsync(default);
            }
            async Task Ready() => await CameraRoundHarness.Until(async () =>
                (await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "left").Arrival?.CameraBoundaryReady == true);
            var source = h.Factory.Sources[h.Left];
            source.CalibrateEmptyTray();
            for (var sample = 0; sample < 19; sample++)
            {
                var before = api.Acquires;
                if (native) api.EnqueueNative(empty, frame++, []); else api.Enqueue(empty, frame++);
                await CameraRoundHarness.Until(() => Task.FromResult(api.Acquires > before && api.Releases == api.Acquires));
                await Task.Delay(100);
            }
            Check(source.ReadDiagnostics().TrayPresenceModel?.Calibrated == true, "actual stable frame span teaches a static reference: " + Wire.Encode(source.ReadDiagnostics().TrayPresenceModel));
            var reference = source.ReadDiagnostics().TrayPresenceModel!.ReferenceSha256;
            for (var sample = 0; sample < 3; sample++)
            {
                var before = api.Acquires; Queue();
                await CameraRoundHarness.Until(() => Task.FromResult(api.Acquires > before && api.Releases == api.Acquires));
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
            Queue();
            Check(acquireEntered.Wait(TimeSpan.FromSeconds(5)), "old SDK frame is borrowed at vehicle change: native=" + native);
            try
            {
                await Arrive("CAMERA-B");
                var s = await h.App.SnapshotAsync();
                Check(s.Rounds.All(r => r.PositionId != "left") && s.Positions!.Single(p => p.Id == "left").Arrival is { CameraBoundaryReady: false },
                    "empty B inherits neither A's round nor ready capture while SDK is borrowed: native=" + native);
                var right = await h.App.SubmitParcelBarcodeAsync(new(Wire.Id(), s.RuntimeId, s.Configuration.Revision, "right", "RIGHT-MANUAL", ParcelBarcodeInputKind.Keyboard, null, null)
                { ExpectedArrivalGeneration = s.Positions!.Single(p => p.Id == "right").Arrival!.Generation }, default);
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
                Queue(); Check(decoding.Wait(TimeSpan.FromSeconds(5)), "old software decoder owns an active result");
                try
                {
                    await Arrive("CAMERA-C");
                    Check(!(await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "left").Arrival!.CameraBoundaryReady,
                        "new camera boundary awaits actual old decode completion");
                }
                finally { h.Factory.Decoders[h.Left].BeforeDecode = null; release.Set(); }
                await Ready();
                Check(h.Peer.Requests.Count == 3, "late old decoder cannot claim C and does not fence the source");
            }
            api.FailNextClear = true;
            await Arrive("CAMERA-FAIL");
            await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "left").Arrival?.CaptureFailure is not null);
            Check(!(await h.App.SnapshotAsync()).Positions!.Single(p => p.Id == "left").Arrival!.CameraBoundaryReady,
                "failed SDK clear never fabricates readiness: native=" + native);
            await Arrive("CAMERA-AFTER-FAIL"); await Ready();
            Queue(); await CameraRoundHarness.Until(() => Task.FromResult(h.Peer.Requests.Count == 4));
            Check(api.Opens == 1 && api.Destroys == 0 && api.Releases == api.Acquires,
                "new arrival recovers on original owner without per-car reconnect or leaked SDK loan: native=" + native);
        }
    }
}

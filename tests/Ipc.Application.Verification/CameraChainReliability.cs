using System.Runtime.InteropServices;
using Ipc.Application.Contracts;
using Ipc.Drivers.Hikrobot.Mvs;
using Ipc.Platform;
using ZXingCpp;

internal static class CameraChainReliability
{
    static void Check(bool ok, string detail) { if (!ok) throw new InvalidOperationException(detail); Console.WriteLine("PASS CHAIN20 " + detail); }
    public static async Task Run(string workspace, string root)
    {
        Console.WriteLine("Synthetic native API/loopback peer, actual ZXing/Application/SQLite. Physical camera/customer RCS NOT_RUN.");
        await CameraSingleFrameRecovery.Run(workspace, Path.Combine(root, "incomplete"));
        await using (var h = new CameraRoundHarness(workspace, Path.Combine(root, "content"), 30000))
        {
            var defaults = ReaderSettings.Read(new Dictionary<string, string>());
            Check(defaults.Formats.SequenceEqual(new[] { "All" }) && defaults.TryInvert, "omitted settings retain all formats and inversion");
            foreach (var value in new[] { "[]", "[\"All\",\"QRCode\"]", "[\"QR\"]", "[\"Code128\",\"Code128\"]" })
            {
                try { ReaderSettings.Read(new Dictionary<string, string> { ["code.formats"] = value }); throw new Exception("Invalid formats accepted"); }
                catch (InvalidDataException) { Check(true, "invalid configured format selection rejected: " + value); }
            }
            var choices = BundledMvs.ReaderDescriptor.Parameters.Single(f => f.Key == "code.formats");
            Check(ExtensionSchema.ValidateDefinition(BundledMvs.ReaderDescriptor.Parameters).Length == 0 && choices.Choices!.Length > 30,
                "format choices use the shared validated parameter schema");
            CameraRoundHarness.RoundPixels Qr(byte[] value)
            {
                using var code = new Barcode(value, new CreatorOptions(BarcodeFormat.QRCode));
                using var image = code.ToImage(new WriterOptions { Scale = 5, AddQuietZones = true });
                var data = new byte[image.Width * image.Height]; Marshal.Copy(image.Data, data, 0, data.Length); return new(data, image.Width, image.Height);
            }
            var qr = Qr(System.Text.Encoding.ASCII.GetBytes("SYNTH-QR"));
            var limited = new SoftwareDecoder("utf-8", () => { }, settings: new(["Code128"], false)); limited.Prepare();
            try { Check(limited.Decode(qr.Data, qr.Data.Length, qr.Width, qr.Height).Length == 0, "actual ZXing excludes QR when only Code128 is configured"); }
            finally { limited.Release(); }
            var decoder = new SoftwareDecoder("utf-8", () => { }, settings: new(["QRCode"], true)); decoder.Prepare();
            try
            {
                var low = qr.Data.Select(p => p == 0 ? (byte)70 : (byte)170).ToArray(); var before = low.ToArray();
                Check(decoder.Decode(low, low.Length, qr.Width, qr.Height).Single() == "SYNTH-QR" && low.SequenceEqual(before),
                    "configured QR decoding expands contrast on scratch pixels and preserves caller raw frame bytes");
                var invalid = Qr([0xFF, 0xFE, 0x80]);
                try { decoder.Decode(invalid.Data, invalid.Data.Length, invalid.Width, invalid.Height); throw new Exception("Invalid UTF-8 accepted"); }
                catch (MvsFrameRejection e) { Check(e.Reason == "InvalidEncoding", "invalid QR encoding is a typed single-frame rejection"); }
                Check(decoder.Decode(qr.Data, qr.Data.Length, qr.Width, qr.Height).Single() == "SYNTH-QR", "valid frame decodes after invalid bytes without recreating the decoder");
                var step = Math.Max(qr.Width, qr.Height) + 20; var side = step * 8;
                var many = Enumerable.Repeat((byte)255, side * side).ToArray();
                for (var row = 0; row < 8; row++) for (var col = 0; col < 8; col++) for (var y = 0; y < qr.Height; y++)
                    qr.Data.AsSpan(y * qr.Width, qr.Width).CopyTo(many.AsSpan((row * step + y) * side + col * step, qr.Width));
                try { decoder.Decode(many, many.Length, side, side); throw new Exception("Candidate limit produced a partial frame"); }
                catch (MvsFrameRejection e) { Check(e.Reason == "CandidateLimit", "64-symbol frame is rejected completely instead of partial candidates"); }
                Check(decoder.Decode(qr.Data, qr.Data.Length, qr.Width, qr.Height).Single() == "SYNTH-QR", "candidate overflow leaves decoder usable for the next frame");
            }
            finally { decoder.Release(); }
            await h.StartAsync(); var source = h.Factory.Sources[h.Left];
            var invalidFrame = Qr([0xFF, 0xFE, 0x80]); h.Api(h.Left).Enqueue(invalidFrame, 1);
            await CameraRoundHarness.Until(() => Task.FromResult(source.ReadDiagnostics().RejectedFrames == 1));
            Check(source.Failure.Length == 0 && h.Peer.Requests.IsEmpty && source.ReadDiagnostics().OriginalFrameCopies == 0,
                "driver rejects invalid encoding without POST, raw copy or camera fence");
            h.Api(h.Left).Enqueue(new(new byte[256 * 256], 256, 256), 2);
            await CameraRoundHarness.Until(() => Task.FromResult(source.ReadDiagnostics().EmptyFrames > 0));
            Check(source.ReadDiagnostics().OriginalFrameCopies == 0, "blank frame makes no archival full-frame copy");
            h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-AFTER-BAD-QR"), 3);
            await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Any(o => o.State == ParcelState.SlotResolved));
            Check(h.Peer.Requests.Count == 1 && source.Failure.Length == 0 && source.ReadDiagnostics().OriginalFrameCopies == 1,
                "next valid frame resolves on same camera and copies its original exactly once");
        }
        await Backpressure(workspace, Path.Combine(root, "capacity"), 64);
        await Backpressure(workspace, Path.Combine(root, "queue"), 1);
        await using (var h = new CameraRoundHarness(workspace, Path.Combine(root, "full-lane-stop"), 30000, 1))
        {
            h.Config.Wcs!.Parameters!["request-timeout-ms"] = "30000";
            ExtensionStore.AtomicWrite(Path.Combine(h.Launch.ConfigurationDirectory, "business.json"), h.Config);
            await h.StartAsync(); h.Peer.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                var source = h.Factory.Sources[h.Left];
                for (uint n = 1; n <= 3; n++)
                {
                    h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-STOP-FULL-LANE"), n);
                    await CameraRoundHarness.Until(() => Task.FromResult(source.ReadDiagnostics().DecodeCompleted >= n));
                    if (n < 3) await CameraRoundHarness.Until(() => Task.FromResult(source.Undelivered is null));
                }
                Check(source.Undelivered is not null && source.Failure.Length == 0, "full-lane source owns the third decoded observation before shutdown");
                await h.App.StopAsync(default).WaitAsync(TimeSpan.FromSeconds(6));
                Check(source.Undelivered is not null && h.Peer.Requests.Count == 1 && h.Api(h.Left).Stops == 1 && !h.Api(h.Left).HasFrame,
                    "shutdown cancels full-lane wait without deadlock, returns SDK loan and retains undelivered responsibility without another POST");
            }
            finally { h.Peer.Hold?.TrySetResult(); h.Peer.Hold = null; }
        }
        using (var pool = new MvsOriginalFrames())
        {
            pool.Enable(); var held = Enumerable.Range(0, 32).Select(_ => pool.Reserve(4096, 64, 64, default)!).ToArray();
            using var stop = new CancellationTokenSource(100);
            try { pool.Reserve(4096, 64, 64, stop.Token); throw new Exception("Pool quota ignored"); }
            catch (OperationCanceledException) { Check(pool.Count == 32, "full original pool wait cancels without allocating an extra frame or leaking ownership"); }
            foreach (var frame in held) frame.Dispose(); Check(pool.Count == 0, "all reserved frame owners release cleanly after cancellation");
        }
        await using (var h = new CameraRoundHarness(workspace, Path.Combine(root, "queue-age"), 1500))
        {
            await h.StartAsync();
            h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-WARMUP"), 1);
            await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Any(o => o.State == ParcelState.SlotResolved));
            h.Peer.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-SLOW"), 2);
                await CameraRoundHarness.Until(() => Task.FromResult(h.Peer.Requests.Count == 2));
                h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-QUEUED"), 3);
                await CameraRoundHarness.Until(() => Task.FromResult(h.Factory.Sources[h.Left].ReadDiagnostics().Delivered == 3));
                await Task.Delay(1900); h.Peer.Hold.TrySetResult(); h.Peer.Hold = null;
                await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Count(o => o.State == ParcelState.SlotResolved) == 3);
                Check(h.Peer.Requests.Count == 3, "fresh-at-ingress observation survives a 1.9-second queue wait under the 5-second RCS budget");
            }
            finally { h.Peer.Hold?.TrySetResult(); h.Peer.Hold = null; }
            var epoch = h.Factory.Sources[h.Left].Epoch;
            h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-ROLLBACK-DISCARDED"), 1);
            // SDK Start is earlier than Application accepting the new session's
            // health/identity. Offer the single synthetic frame only once that
            // real boundary is ready; a continuous camera supplies later frames.
            await CameraRoundHarness.Until(async () => h.Factory.Sources[h.Left].Epoch != epoch && h.Api(h.Left).Starts == 2 &&
                (await h.App.SnapshotAsync()).Devices!.Any(d => d.DeviceId == h.Left && d.Epoch == h.Factory.Sources[h.Left].Epoch && d.IsValid(DateTimeOffset.UtcNow)));
            h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-NEW-EPOCH"), 1);
            await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Any(o => o.ParcelCode == "SYNTH-NEW-EPOCH" && o.State == ParcelState.SlotResolved));
            Check(h.Peer.Requests.Count == 4 && h.Api(h.Left).Opens == 2, "frame-number rollback rebinds a fresh camera epoch without sending the rollback frame or replaying old requests");
        }
        await SportmasterRcs.ConnectionEvidenceChecks(workspace, Path.Combine(root, "connection"));
    }
    static async Task Backpressure(string workspace, string folder, int capacity)
    {
        await using var h = new CameraRoundHarness(workspace, folder, 30000, capacity);
        h.Config.Wcs!.Parameters!["request-timeout-ms"] = "30000";
        ExtensionStore.AtomicWrite(Path.Combine(h.Launch.ConfigurationDirectory, "business.json"), h.Config);
        await h.StartAsync(); var source = h.Factory.Sources[h.Left]; h.Peer.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            // A frame at a time: await the prior handoff before offering another,
            // until the bounded original pool or lane applies backpressure.
            var admitted = capacity == 1 ? 3 : 32;
            for (uint n = 1; n <= admitted; n++)
            {
                h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-REPEATED"), n);
                await CameraRoundHarness.Until(() => Task.FromResult(source.ReadDiagnostics().DecodeCompleted >= n));
                if (n < admitted) await CameraRoundHarness.Until(() => Task.FromResult(source.Undelivered is null));
            }
            if (capacity != 1)
            {
                h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-REPEATED"), 33);
                await CameraRoundHarness.Until(() => Task.FromResult(source.ReadDiagnostics().WaitingForOriginalCapacity));
            }
            await Task.Delay(150);
            Check(source.Failure.Length == 0 && h.Peer.Requests.Count == 1,
                $"bounded {(capacity == 1 ? "ingress lane" : "original frame pool")} waits without overload fence or duplicate send");
            Check(capacity != 1 || source.Undelivered is not null, "decoded observation retains delivery ownership while its lane is full");
            h.Peer.Hold.TrySetResult(); h.Peer.Hold = null;
            var expected = capacity == 1 ? 3 : 33;
            await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Count(o => o.State == ParcelState.SlotResolved) == expected);
            var operations = (await h.App.SnapshotAsync()).Operations;
            Check(source.Failure.Length == 0 && h.Peer.Requests.Count == expected && operations.Select(o => o.Wcs!.Request.IntegrationRequest!.RequestId).Distinct().Count() == expected && operations.All(o => o.Wcs!.Attempts == 1),
                $"all {expected} independent same-code reads each send once after drain, with distinct frozen request IDs");
        }
        finally { h.Peer.Hold?.TrySetResult(); h.Peer.Hold = null; }
    }
}

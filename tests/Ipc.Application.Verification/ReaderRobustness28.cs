using System.Diagnostics;
using System.Runtime.InteropServices;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Drivers.Hikrobot.Mvs;
using Ipc.Platform;
using ZXingCpp;

internal static class ReaderRobustness28
{
    static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); Console.WriteLine("PASS READER28 " + message); }
    internal static async Task Run(string workspace, string root)
    {
        await using var h = new CameraRoundHarness(workspace, Path.Combine(root, "reader28"));
        DecodeCases();
        await h.StartAsync();
        await SnapshotOrdering(h);
    }
    static void DecodeCases()
    {
        using var qr = new Barcode("SYNTH-SECONDARY-28", new CreatorOptions(BarcodeFormat.QRCode));
        using var qrImage = qr.ToImage(new WriterOptions { Scale = 5, AddQuietZones = true });
        var qrBytes = new byte[qrImage.Width * qrImage.Height]; Marshal.Copy(qrImage.Data, qrBytes, 0, qrBytes.Length);
        var weak = CameraRoundHarness.Barcode("SYNTH-WEAK-28");
        var width = Math.Max(weak.Width + qrImage.Width + 160, 1200); const int height = 500;
        var mixed = Enumerable.Repeat((byte)190, width * height).ToArray();
        Paste(mixed, width, qrBytes, qrImage.Width, qrImage.Height, width - qrImage.Width - 40, 40);
        Paste(mixed, width, weak.Data.Select(p => p == 0 ? (byte)60 : (byte)190).ToArray(), weak.Width, weak.Height, 40, height - weak.Height - 40);
        var decoder = new SoftwareDecoder("utf-8", () => { }, (_, _) => { }, r => r.Binarizer = Binarizer.BoolCast);
        decoder.Prepare();
        try
        {
            var before = mixed.ToArray();
            var codes = decoder.Decode(mixed, mixed.Length, width, height);
            Check(codes.Length == 2 && codes.Contains("SYNTH-SECONDARY-28") && codes.Contains("SYNTH-WEAK-28"),
                "a valid easy primary code does not suppress a second real threshold-only candidate");
            Check(BarcodeFiltering.Select(null, codes).Outcome == "MultipleCodes", "default strict policy rejects the complete mixed set instead of selecting its easy secondary code");
            Check(BarcodeFiltering.Select(new("SYNTH-WEAK-[0-9]+"), codes).SelectedCode == "SYNTH-WEAK-28", "an explicitly supplied business rule selects from the complete candidate set");
            Check(mixed.SequenceEqual(before), "all search profiles preserve every original input pixel");
        }
        finally { decoder.Release(); }
        var ordinary = new SoftwareDecoder("utf-8", () => { }); ordinary.Prepare();
        try
        {
            var a = CameraRoundHarness.Barcode("SYNTH-SAME-28");
            var same = CameraRoundHarness.Multiple("SYNTH-SAME-28", "SYNTH-SAME-28");
            Check(ordinary.Decode(a.Data, a.Data.Length, a.Width, a.Height).SequenceEqual(new[] { "SYNTH-SAME-28" }), "repeated searches of one printed pattern produce exactly one candidate");
            var separate = ordinary.Decode(same.Data, same.Data.Length, same.Width, same.Height);
            Check(separate.Length == 2 && separate.All(s => s == "SYNTH-SAME-28"), "equal barcode text at two separate positions remains two candidates");
            var stacked = Enumerable.Repeat((byte)255, a.Width * (a.Height * 2 + 80)).ToArray();
            Paste(stacked, a.Width, a.Data, a.Width, a.Height, 0, 0); Paste(stacked, a.Width, a.Data, a.Width, a.Height, 0, a.Height + 80);
            Check(ordinary.Decode(stacked, stacked.Length, a.Width, a.Height * 2 + 80).Length == 2, "a quiet gap between identical stacked codes prevents search-result merging");
            using (var ean = new Barcode("1234567890128", new CreatorOptions(BarcodeFormat.EAN13)))
            using (var eanImage = ean.ToImage(new WriterOptions { Scale = 6, AddQuietZones = true }))
            {
                var eanPixels = new byte[eanImage.Width * eanImage.Height]; Marshal.Copy(eanImage.Data, eanPixels, 0, eanPixels.Length);
                // Independent synthetic uneven illumination: searches can report a
                // short thick band and a larger band of this same printed label.
                for (var y = 0; y < eanImage.Height; y++) for (var x = 0; x < eanImage.Width; x++)
                    eanPixels[y * eanImage.Width + x] = eanPixels[y * eanImage.Width + x] < 128
                        ? (byte)(y < eanImage.Height / 3 ? 20 : 115) : (byte)230;
                var label = new CameraRoundHarness.RoundPixels(eanPixels, eanImage.Width, eanImage.Height);
                foreach (var angle in new[] { -2d, -.75, 0, .75, 1.5, 2.5 })
                {
                    var tilted = Rotate(label, angle); var unchanged = tilted.Data.ToArray();
                    Check(ordinary.Decode(tilted.Data, tilted.Data.Length, tilted.Width, tilted.Height).SequenceEqual(new[] { "1234567890128" }) && tilted.Data.SequenceEqual(unchanged),
                        "one unevenly lit EAN13 tilted " + angle + " degrees remains one physical candidate and preserves original pixels");
                }
                var two = Enumerable.Repeat((byte)255, label.Width * (label.Height * 2 + 80)).ToArray();
                Paste(two, label.Width, label.Data, label.Width, label.Height, 0, 0);
                Paste(two, label.Width, label.Data, label.Width, label.Height, 0, label.Height + 80);
                var both = ordinary.DecodeDetailed(two, two.Length, label.Width, label.Height * 2 + 80);
                Check(both.Length == 2 && both.All(c => c.Text == "1234567890128") && BarcodeFiltering.Select(null, both.Select(c => c.Text).ToArray()).Outcome == "MultipleCodes",
                    "two physically separate identical EAN13 labels remain two candidates and are rejected by strict multi-code policy");
                var ordered = both.OrderBy(c => c.Corners[0].Y).ToArray();
                var oversized = ordered[0] with { Corners = [ordered[0].Corners[0], ordered[0].Corners[1], ordered[1].Corners[2], ordered[1].Corners[3]] };
                var associate = typeof(SoftwareDecoder).GetMethod("SamePrintedRegion", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
                Check(!(bool)associate.Invoke(ordinary, [ordered[0], oversized, two, label.Width, label.Height * 2 + 80])!,
                    "even a containing search band cannot bridge the original-pixel quiet gap between separate identical labels");
            }
            foreach (var angle in new[] { 15, 30, 45, 70 })
            {
                var rotated = Rotate(weak, angle); var original = rotated.Data.ToArray();
                var read = ordinary.DecodeDetailed(rotated.Data, rotated.Data.Length, rotated.Width, rotated.Height);
                if (read.Length != 1 || read[0].Text != "SYNTH-WEAK-28") Console.WriteLine("DIAGNOSTIC angle=" + angle + " candidates=" + Wire.Encode(read));
                Check(read is [{ Text: "SYNTH-WEAK-28" }] && rotated.Data.SequenceEqual(original) &&
                    read[0].Corners.All(p => p.X >= 0 && p.X < rotated.Width && p.Y >= 0 && p.Y < rotated.Height),
                    angle + " degree synthetic label decodes once with corners in unchanged original-image coordinates");
            }
            foreach (var radius in new[] { 1, 2 })
            {
                var blurred = Blur(Rotate(weak, 25), radius); var original = blurred.Data.ToArray();
                Check(ordinary.Decode(blurred.Data, blurred.Data.Length, blurred.Width, blurred.Height).SequenceEqual(new[] { "SYNTH-WEAK-28" }) && blurred.Data.SequenceEqual(original),
                    "oblique label with independent Gaussian blur radius " + radius + " decodes once without changing source pixels");
            }
            using (var shortCode = new Barcode("48", new CreatorOptions(BarcodeFormat.ITF)))
            using (var shortImage = shortCode.ToImage(new WriterOptions { Scale = 4, AddQuietZones = true }))
            {
                var pixels = new byte[shortImage.Width * shortImage.Height]; Marshal.Copy(shortImage.Data, pixels, 0, pixels.Length);
                Check(ordinary.Decode(pixels, pixels.Length, shortImage.Width, shortImage.Height).SequenceEqual(new[] { "48" }),
                    "genuine short ITF remains readable: source-line guard does not impose payload length or business rules");
            }
            var oblique = Blur(Rotate(weak, 25), 1);
            var multiWidth = oblique.Width + qrImage.Width + 100; var multiHeight = Math.Max(oblique.Height, qrImage.Height) + 80;
            var multi = Enumerable.Repeat((byte)255, multiWidth * multiHeight).ToArray();
            Paste(multi, multiWidth, oblique.Data, oblique.Width, oblique.Height, 20, 20);
            Paste(multi, multiWidth, qrBytes, qrImage.Width, qrImage.Height, oblique.Width + 60, 40);
            var complete = ordinary.Decode(multi, multi.Length, multiWidth, multiHeight);
            Check(complete.Length == 2 && complete.Contains("SYNTH-WEAK-28") && complete.Contains("SYNTH-SECONDARY-28") && BarcodeFiltering.Select(null, complete).Outcome == "MultipleCodes",
                "oblique blurred linear label plus QR stays a complete ambiguous set, never an automatic easy-code selection");
            Check(ordinary.Decode(Enumerable.Repeat((byte)180, 640 * 240).ToArray(), 640 * 240, 640, 240).Length == 0, "uniform frames remain empty after all bounded search profiles");
            var gridWidth = (qrImage.Width + 24) * 8; var gridHeight = (qrImage.Height + 24) * 8;
            var crowded = Enumerable.Repeat((byte)255, gridWidth * gridHeight).ToArray();
            for (var y = 0; y < 8; y++) for (var x = 0; x < 8; x++) Paste(crowded, gridWidth, qrBytes, qrImage.Width, qrImage.Height, x * (qrImage.Width + 24), y * (qrImage.Height + 24));
            var capacityRejected = false;
            try { ordinary.Decode(crowded, crowded.Length, gridWidth, gridHeight); }
            catch (MvsFrameRejection error) when (error.Message.Contains("candidate limit", StringComparison.OrdinalIgnoreCase)) { capacityRejected = true; }
            Check(capacityRejected, "a frame reaching the 64-candidate limit is rejected in full rather than selecting a truncated subset");
            Check(ordinary.Decode(a.Data, a.Data.Length, a.Width, a.Height).SequenceEqual(new[] { "SYNTH-SAME-28" }), "candidate-limit rejection releases native results and leaves the next frame usable");
            using var gcStop = new CancellationTokenSource(); using var gcStarted = new ManualResetEventSlim(); var collections = 0;
            var collector = Task.Run(() => { gcStarted.Set(); while (!gcStop.IsCancellationRequested) { GC.Collect(); GC.WaitForPendingFinalizers(); Interlocked.Increment(ref collections); Thread.Sleep(1); } });
            var completed = 0;
            try
            {
                if (!gcStarted.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("GC stress owner did not start");
                for (var frame = 0; frame < 32; frame++)
                {
                    var pooled = System.Buffers.ArrayPool<byte>.Shared.Rent(a.Data.Length);
                    try
                    {
                        a.Data.CopyTo(pooled, 0); var result = ordinary.DecodeDetailed(pooled, a.Data.Length, a.Width, a.Height);
                        if (result is not [{ Text: "SYNTH-SAME-28" }] || result[0].Corners.Any(p => p.X < 0 || p.X >= a.Width || p.Y < 0 || p.Y >= a.Height) || !pooled.AsSpan(0, a.Data.Length).SequenceEqual(a.Data))
                            throw new InvalidOperationException("Native view ownership failed under concurrent GC");
                        completed++;
                    }
                    finally { System.Buffers.ArrayPool<byte>.Shared.Return(pooled, true); }
                }
            }
            finally { gcStop.Cancel(); collector.GetAwaiter().GetResult(); }
            Check(completed == 32 && collections > 0, "32 consecutive pooled-frame decodes survive concurrent GC/finalizers with exact payload, in-bounds corners and unchanged source pixels");
        }
        finally { ordinary.Release(); }
        var qrOnly = new SoftwareDecoder("utf-8", () => { }, settings: new(["QRCode"], false)); qrOnly.Prepare();
        try { Check(qrOnly.Decode(mixed, mixed.Length, width, height).SequenceEqual(new[] { "SYNTH-SECONDARY-28" }), "directional and threshold searches preserve the explicit QR-only format restriction"); }
        finally { qrOnly.Release(); }
    }
    static async Task SnapshotOrdering(CameraRoundHarness h)
    {
        var vm = new SupplyStationViewModel(h.App); await vm.RefreshAsync(); vm.SetViewport(1366, 768);
        h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-RETIRED-28"), 1);
        h.Api(h.Right).Enqueue(CameraRoundHarness.Barcode("SYNTH-RIGHT-28"), 1);
        await CameraRoundHarness.Until(async () => { await vm.RefreshAsync(); return vm.Left.ShowConfirmedTask && vm.Right.ShowConfirmedTask; });
        var old = vm.Snapshot!;
        h.Api(h.Left).Enqueue(new(Enumerable.Repeat((byte)180, 640 * 240).ToArray(), 640, 240), 2);
        await CameraRoundHarness.Until(async () => { await vm.RefreshAsync(); return vm.Left.DisplayStatus == "未读到条码"; });
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        h.App.Boundary = point => { if (point == "BeforeIngressDurable") { entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("Snapshot/stream regression gate timed out"); } };
        try
        {
            h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-NEW-28"), 3);
            await CameraRoundHarness.Until(() => Task.FromResult(entered.IsSet));
            // No stream subscription: exactly the snapshot-first/new-window path.
            await vm.RefreshAsync();
            Check(!vm.Left.ShowConfirmedTask && !vm.Left.CanViewCurrentPicture && vm.Left.DisplaySlot != "SYNTH-CHUTE" && vm.Left.Barcode != "SYNTH-RETIRED-28" && vm.Right.ShowConfirmedTask,
                "snapshot before its UI stream cue cannot restore retired barcode, slot or picture; right position remains current");
            var snapshot = vm.Snapshot!; var frame = snapshot.CameraReads.Single(f => f.PositionId == "left"); var cue = snapshot.ReadFeedbacks.Single(r => r.PositionId == "left");
            Check(frame.Frame.FrameNumber == 3 && cue.DetectedTicks == frame.Frame.CompletedTicks && cue.ObservationId != vm.Left.Input!.Observation.Id,
                "one snapshot carries matching frame/read watermarks even while the new durable input is not yet written");
            var reopened = new SupplyStationViewModel(h.App); reopened.SetViewport(1366, 768);
            await reopened.AcceptPublishedSnapshotAsync(snapshot, default);
            Check(!reopened.Left.ShowConfirmedTask && reopened.Left.Barcode == "SYNTH-NEW-28", "a new window renders the pending new barcode without borrowing the retired result");
            var missingCue = new SupplyStationViewModel(h.App); missingCue.SetViewport(1366, 768);
            await missingCue.AcceptPublishedSnapshotAsync(snapshot with { ReadFeedbacks = [] }, default);
            Check(!missingCue.Left.ShowConfirmedTask && missingCue.Left.Barcode == "" && !missingCue.Left.CanViewCurrentPicture,
                "a snapshot lacking the companion cue fails closed using the exact latest frame identity");
            await vm.AcceptPublishedSnapshotAsync(old, default);
            Check(!vm.Left.ShowConfirmedTask, "late prior snapshot cannot revive an already superseded camera result");
        }
        finally { release.Set(); h.App.Boundary = null; }
        await CameraRoundHarness.Until(async () => { await vm.RefreshAsync(); return vm.Left.ShowConfirmedTask && vm.Left.Barcode == "SYNTH-NEW-28"; });
        Check(h.Peer.Requests.Count == 3 && vm.Left.Input!.Observation.SourceEventId == "mvs.frame.3", "only the matched new observation and its original RCS response restore a confirmed task");
    }
    static void Paste(byte[] canvas, int width, byte[] data, int w, int h, int x, int y)
    { for (var row = 0; row < h; row++) data.AsSpan(row * w, w).CopyTo(canvas.AsSpan((y + row) * width + x, w)); }
    static CameraRoundHarness.RoundPixels Rotate(CameraRoundHarness.RoundPixels input, double degrees)
    {
        var radians = degrees * Math.PI / 180; var c = Math.Cos(radians); var s = Math.Sin(radians);
        var w = (int)Math.Ceiling(Math.Abs(c) * input.Width + Math.Abs(s) * input.Height) + 80;
        var h = (int)Math.Ceiling(Math.Abs(s) * input.Width + Math.Abs(c) * input.Height) + 80;
        var pixels = Enumerable.Repeat((byte)255, w * h).ToArray();
        for (var y = 0; y < h; y++) for (var x = 0; x < w; x++)
            {
                var sourceX = (int)Math.Round((x - w / 2d) * c + (y - h / 2d) * s + input.Width / 2d);
                var sourceY = (int)Math.Round(-(x - w / 2d) * s + (y - h / 2d) * c + input.Height / 2d);
                if (sourceX >= 0 && sourceX < input.Width && sourceY >= 0 && sourceY < input.Height) pixels[y * w + x] = input.Data[sourceY * input.Width + sourceX];
            }
        return new(pixels, w, h);
    }
    static CameraRoundHarness.RoundPixels Blur(CameraRoundHarness.RoundPixels input, int radius)
    {
        // Independent fixture convolution, not the production enhancement path.
        var kernel = radius == 1 ? new[] { 1, 2, 1 } : new[] { 1, 4, 6, 4, 1 };
        var total = kernel.Sum(); var w = input.Width; var h = input.Height; var result = new byte[input.Data.Length];
        for (var y = 0; y < h; y++) for (var x = 0; x < w; x++)
            {
                var sum = 0;
                for (var ky = -radius; ky <= radius; ky++) for (var kx = -radius; kx <= radius; kx++)
                        sum += input.Data[Math.Clamp(y + ky, 0, h - 1) * w + Math.Clamp(x + kx, 0, w - 1)] * kernel[ky + radius] * kernel[kx + radius];
                result[y * w + x] = (byte)(sum / (total * total));
            }
        return new(result, w, h);
    }
}

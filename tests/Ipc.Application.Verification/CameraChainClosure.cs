using System.Runtime.InteropServices;
using System.Text;
using Ipc.Drivers.Hikrobot.Mvs;
using Ipc.Platform;
using ZXingCpp;

internal static class CameraChainClosure
{
    static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); Console.WriteLine("PASS CLOSURE21 " + message); }
    internal static async Task Run(string workspace, string root)
    {
        Console.WriteLine("Actual ZXing/SQLite/RCS adapter, synthetic native camera API and loopback peer. Physical devices/customer RCS NOT_RUN.");
        await CrossPositionFence(workspace, Path.Combine(root, "global-fence"));
        await OriginalOwnership();
        await LatestAfterCapacityWait(workspace, Path.Combine(root, "latest-after-capacity"));
        await DecoderOptions(workspace, Path.Combine(root, "decoder"));
    }
    static async Task CrossPositionFence(string workspace, string folder)
    {
        await using var h = new CameraRoundHarness(workspace, folder, 30000, 1);
        h.Config.Wcs!.Parameters!["request-timeout-ms"] = "30000";
        ExtensionStore.AtomicWrite(Path.Combine(h.Launch.ConfigurationDirectory, "business.json"), h.Config);
        await h.StartAsync(); h.Peer.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var right = h.Factory.Sources[h.Right];
            for (uint n = 1; n <= 3; n++)
            {
                h.Api(h.Right).Enqueue(CameraRoundHarness.Barcode("SYNTH-FULL-RIGHT"), n);
                await CameraRoundHarness.Until(() => Task.FromResult(right.ReadDiagnostics().DecodeCompleted >= n));
                if (n < 3) await CameraRoundHarness.Until(() => Task.FromResult(right.Undelivered is null));
            }
            await CameraRoundHarness.Until(() => Task.FromResult(right.Undelivered?.SourceEventId == "mvs.frame.3" && right.ReadDiagnostics().Delivered == 2 && h.Peer.Requests.Count == 1));
            Check(right.Failure.Length == 0 && h.Peer.Requests.Count == 1, "right decoded observation owns a full-lane wait while its first real HTTP request is held");
            h.App.Boundary = point => { if (point == "BeforeIngressDurable") throw new InvalidOperationException("Synthetic terminal ingress owner failure before custody"); };
            h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-LEFT-FENCE"), 1);
            await CameraRoundHarness.Until(() => Task.FromResult(right.ReadDiagnostics().Rejected > 0 && h.Factory.Sources[h.Left].ReadDiagnostics().Rejected > 0));
            Check(right.Undelivered is not null && h.Factory.Sources[h.Left].Undelivered is not null && h.Peer.Hold.Task.IsCompleted == false && h.Peer.Requests.Count == 1,
                "terminal left ingress failure wakes the full right lane before its HTTP completes; both undelivered observations remain owned and no new POST occurs");
            Check(!right.Failure.Contains("AggregateException", StringComparison.Ordinal), "capacity/terminal race does not wrap source shutdown as Task.Wait aggregate cancellation");
            h.App.Boundary = null;
            await h.App.StopAsync(default).WaitAsync(TimeSpan.FromSeconds(6));
            Check(h.Api(h.Left).Stops == 1 && h.Api(h.Right).Stops == 1 && !h.Api(h.Left).HasFrame && !h.Api(h.Right).HasFrame,
                "global fence drains both real acquisition owners without borrowed SDK buffers or a blocked handoff lock");
        }
        finally { h.App.Boundary = null; h.Peer.Hold?.TrySetResult(); h.Peer.Hold = null; }
    }
    static async Task OriginalOwnership()
    {
        using (var pool = new MvsOriginalFrames())
        {
            pool.Enable(); var held = Enumerable.Range(0, 32).Select(_ => pool.Reserve(4096, 64, 64, default)!).ToArray();
            var cancellations = 0;
            for (var run = 0; run < 64; run++)
            {
                using var cancelled = new CancellationTokenSource();
                var wait = Task.Run(() => pool.Reserve(4096, 64, 64, cancelled.Token));
                if (run % 2 == 0) cancelled.Cancel();
                else ThreadPool.QueueUserWorkItem(_ => cancelled.Cancel());
                try { await wait.WaitAsync(TimeSpan.FromSeconds(2)); throw new Exception("Full pool reservation escaped its cancelled owner"); }
                catch (OperationCanceledException) { cancellations++; }
            }
            Check(cancellations == 64 && pool.Count == 32, "64 cancellation/register/wait races complete without lost wake-up, excess reservation or resource growth");
            var next = Task.Run(() => pool.Reserve(4096, 64, 64, default)!);
            held[0].Dispose(); var acquired = await next.WaitAsync(TimeSpan.FromSeconds(2));
            Check(pool.Count == 32, "releasing capacity wakes exactly the next bounded reservation");
            acquired.Dispose(); foreach (var frame in held) frame.Dispose();
            Check(pool.Count == 0, "cancelled and resumed reservations release all original pixel ownership");
            foreach (var (length, width, height) in new[] { (0, 1, 1), (int.MaxValue, 1, 1), (5, 2, 2) })
            {
                try { pool.Reserve(length, width, height, default); throw new Exception("Impossible frame reservation accepted"); }
                catch (ArgumentOutOfRangeException) { Check(true, $"impossible reservation is rejected before allocation/wait: {length}/{width}/{height}"); }
            }
            var pending = pool.Reserve(4096, 64, 64, default)!;
            try { pool.Dispose(); throw new Exception("Active acquisition owner reported released"); }
            catch (InvalidOperationException) { Check(pool.Count == 1, "unpublished original owner prevents premature pool disposal"); }
            pending.Dispose(); pool.Dispose();
            try { pool.Reserve(4096, 64, 64, default); throw new Exception("Disposed pool accepted new pixels"); }
            catch (ObjectDisposedException) { Check(true, "disposed pool cannot acquire or publish a new frame"); }
        }
        using (var pool = new MvsOriginalFrames())
        {
            pool.Enable(); var held = new List<MvsOriginalFrames.Frame>();
            try
            {
                // 12 MiB raw frames use actual 16 MiB ArrayPool buckets: sixteen
                // leases consume 256 MiB, below the separate 32-frame limit.
                for (var n = 0; n < 16; n++) held.Add(pool.Reserve(12 * 1024 * 1024, 4096, 3072, default)!);
                using var cancelled = new CancellationTokenSource(50);
                try { await Task.Run(() => pool.Reserve(12 * 1024 * 1024, 4096, 3072, cancelled.Token)).WaitAsync(TimeSpan.FromSeconds(2)); throw new Exception("Actual pool-byte quota ignored"); }
                catch (OperationCanceledException) { Check(pool.Count == 16, "actual 12 MiB frames obey the 256 MiB rented-byte budget before reaching 32-frame count"); }
            }
            finally { foreach (var frame in held) frame.Dispose(); }
            Check(pool.Count == 0, "byte-budget saturation leaves no retained original frame owners");
        }
    }
    static async Task LatestAfterCapacityWait(string workspace, string folder)
    {
        await using var h = new CameraRoundHarness(workspace, folder, 30000);
        h.Config.Wcs!.Parameters!["request-timeout-ms"] = "30000";
        ExtensionStore.AtomicWrite(Path.Combine(h.Launch.ConfigurationDirectory, "business.json"), h.Config);
        await h.StartAsync(); var source = h.Factory.Sources[h.Left]; h.Peer.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            for (uint n = 1; n <= 32; n++)
            {
                h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-CUSTODIED"), n);
                await CameraRoundHarness.Until(() => Task.FromResult(source.ReadDiagnostics().Delivered >= n));
            }
            h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-STALE-UNDECODED"), 33);
            await CameraRoundHarness.Until(() => Task.FromResult(source.ReadDiagnostics().WaitingForOriginalCapacity));
            for (uint n = 34; n <= 38; n++) h.Api(h.Left).Enqueue(CameraRoundHarness.Barcode("SYNTH-LATEST-" + n), n);
            await CameraRoundHarness.Until(() => Task.FromResult(h.Api(h.Left).Releases == 38));
            Check(source.ReadDiagnostics().DecodeCompleted == 32 && source.Failure.Length == 0,
                "capture continues to the latest video while a pre-decode frame waits for bounded original capacity");
            h.Peer.Hold.TrySetResult(); h.Peer.Hold = null;
            await CameraRoundHarness.Until(async () => (await h.App.SnapshotAsync()).Operations.Count(o => o.State == Ipc.Application.Contracts.ParcelState.SlotResolved) == 2);
            var operations = (await h.App.SnapshotAsync()).Operations;
            Check(operations.Count(o => o.ParcelCode == "SYNTH-CUSTODIED") == 1 && operations.Single(o => o.ParcelCode != "SYNTH-CUSTODIED").Acquisition?.SourceEventId == "mvs.frame.38" &&
                h.Peer.Requests.Count == 2 && source.ReadDiagnostics().DecodeCompleted == 33 && h.App.WorkerProgress().Single(p => p.Position == "left").Processed == 33,
                "all 32 recognized frames of one resting parcel are processed but request once; recovery decodes only latest frame 38, never stale pre-decode frame 33");
            Check(source.ReadDiagnostics().SupersededDecodeFrames >= 5 && h.Api(h.Left).Acquires == h.Api(h.Left).Releases && !h.Api(h.Left).HasFrame,
                "superseded undecoded video remains counted and every SDK loan is returned without a camera fence");
        }
        finally { h.Peer.Hold?.TrySetResult(); h.Peer.Hold = null; }
    }
    static async Task DecoderOptions(string workspace, string folder)
    {
        await using var native = new CameraRoundHarness(workspace, folder); // Initializes the verified synthetic ZXing boundary only.
        static CameraRoundHarness.RoundPixels Qr(byte[] value)
        {
            using var barcode = new Barcode(value, new CreatorOptions(BarcodeFormat.QRCode));
            using var image = barcode.ToImage(new WriterOptions { Scale = 5, AddQuietZones = true });
            var bytes = new byte[image.Width * image.Height]; Marshal.Copy(image.Data, bytes, 0, bytes.Length); return new(bytes, image.Width, image.Height);
        }
        var gb = new SoftwareDecoder("gb18030", () => { }, settings: new(["QRCode"], true)); gb.Prepare();
        try
        {
            var chinese = Qr(Encoding.GetEncoding("gb18030").GetBytes("合成中文-001")); var original = chinese.Data.ToArray();
            Check(gb.Decode(chinese.Data, chinese.Data.Length, chinese.Width, chinese.Height).Single() == "合成中文-001" && chinese.Data.SequenceEqual(original),
                "explicit gb18030 decodes actual synthetic Chinese QR bytes while preserving raw pixels");
        }
        finally { gb.Release(); }
        var qr = Qr(Encoding.ASCII.GetBytes("SYNTH-INVERTED")); var inverted = qr.Data.Select(p => (byte)(255 - p)).ToArray();
        foreach (var invert in new[] { false, true })
        {
            bool? applied = null;
            var decoder = new SoftwareDecoder("utf-8", () => { }, searchOptionsForVerification: r => applied = r.TryInvert, settings: new(["QRCode"], invert)); decoder.Prepare();
            try
            {
                var codes = decoder.Decode(inverted, inverted.Length, qr.Width, qr.Height);
                Check(applied == invert && (invert ? codes.SequenceEqual(new[] { "SYNTH-INVERTED" }) : codes.Length == 0),
                    "configured native inversion=" + invert + " controls the actual reversed-polarity QR search");
            }
            finally { decoder.Release(); }
        }
    }
}

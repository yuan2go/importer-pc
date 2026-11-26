using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Ipc.Drivers.Hikrobot.Mvs;
using Ipc.Platform;

// Recognise only settled frames: the parcel is placed on a stationary cart, and frames taken
// while it is moving must not reach the decoder. Synthetic streaming camera (640 x 480 at about
// 50 fps) with a bright block that moves, then rests; the actual MvsAcquisition decides.
internal static class MvsStillFrames
{
    static void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); Console.WriteLine("PASS STILL " + text); }
    const int W = 640, H = 480;
    const byte MovingMarker = 1, StillMarker = 2;

    sealed class Lease : IDisposable { public void Dispose() { } }
    sealed class Decoder : IMonoBarcodeDecoder
    {
        public readonly ConcurrentQueue<(byte Marker, long At)> Seen = new();
        public void Prepare() { }
        // Pixel 0 carries the scene phase; the stillness grid never samples it.
        public string[] Decode(byte[] pixels, int length, int width, int height) { Seen.Enqueue((pixels[0], Environment.TickCount64)); return ["SYNTH-STILL"]; }
        public void Release() { }
    }
    sealed class Camera(bool native = false) : IMvsApi
    {
        public volatile bool Moving = true;
        public bool MoveOnNextDecode { get; set; }
        public readonly ConcurrentQueue<(bool Decode, long At)> NativeCalls = new();
        int offset; uint number; nint buffer;
        public bool HasFrame { get; private set; }
        public bool ReturnsDecodedFrames => native;
        public void Initialize() { }
        public MvsDevice[] Enumerate() => [new("192.0.2.30", "SYNTH-STILL", "SYNTHETIC", "SYNTHETIC", [])];
        public void Create(MvsDevice device) { }
        public void Open() { }
        public uint EnumValue(string key) => key switch { "PixelFormat" => MvsNative.Mono8, "AcquisitionMode" => 2u, _ => 0u };
        public long PayloadSize() => W * H;
        public void ConfigureBuffers(uint nodes, uint strategy = 0) { }
        public void Start() { }
        public MvsFrame? Acquire() => AcquireFrame(native);
        public MvsFrame? AcquirePreview() => AcquireFrame(false);
        MvsFrame AcquireFrame(bool decode)
        {
            if (native) NativeCalls.Enqueue((decode, Stopwatch.GetTimestamp()));
            Thread.Sleep(20);
            var lateMotion = decode && MoveOnNextDecode;
            if (lateMotion) MoveOnNextDecode = false;
            if (Moving || lateMotion) offset = (offset + 24) % (W - 120);
            var pixels = new byte[W * H]; Array.Fill(pixels, (byte)90);
            for (var y = 150; y < 270; y++) for (var x = offset; x < offset + 120; x++) pixels[y * W + x] = 240;
            pixels[0] = Moving ? MovingMarker : StillMarker;
            buffer = Marshal.AllocHGlobal(pixels.Length); Marshal.Copy(pixels, 0, buffer, pixels.Length); HasFrame = true;
            return new(buffer, W, H, MvsNative.Mono8, (ulong)pixels.Length, ++number, 0, 0)
            { NativeSymbols = decode && !Moving ? [new(lateMotion ? "SYNTH-UNSETTLED" : "SYNTH-STILL", "Code128", [], 0)] : [] };
        }
        public void Release() { if (HasFrame) { Marshal.FreeHGlobal(buffer); HasFrame = false; } }
        public bool Connected() => true;
        public void Stop() { }
        public void Close() { }
        public void Destroy() { }
        public void Shutdown() { }
    }

    static MvsAcquisition Source(Camera camera, Decoder? decoder, string? stillMs = null)
    {
        var parameters = new Dictionary<string, string> { ["device.ip"] = "192.0.2.30", ["device.serial"] = "SYNTH-STILL" };
        if (stillMs is not null) parameters["motion.still-ms"] = stillMs;
        return new(new("still-camera", "still-camera", parameters), camera, _ => new Lease(), decoder: decoder);
    }

    internal static async Task Run()
    {
        // The comparison itself.
        var flat = new byte[24576]; Array.Fill(flat, (byte)100);
        var brighter = flat.Select(b => (byte)(b + 30)).ToArray();
        var noisy = flat.Select((b, i) => (byte)(b + (i % 7) - 3)).ToArray();
        var edge = (byte[])flat.Clone(); for (var i = 0; i < 200; i++) edge[i * 50] = 230;
        var vibration = (byte[])flat.Clone(); for (var i = 0; i < 98; i++) vibration[i * 50] = 230;
        Check(!MvsStillness.Moved(flat, flat) && !MvsStillness.Moved(flat, brighter) && !MvsStillness.Moved(flat, noisy),
            "an unchanged scene, a global exposure shift and sensor noise do not count as movement");
        Check(MvsStillness.Moved(flat, edge), "about 0.8% of the scene changing (a hand or parcel edge) counts as movement");
        Check(!MvsStillness.Moved(flat, vibration), "0.4% local vibration does not reset the stillness reference");
        var stillness = new MvsStillness(150, 800);
        var memory = Marshal.AllocHGlobal(flat.Length);
        try
        {
            var origin = Stopwatch.GetTimestamp();
            StillnessDecision Observe(byte[] frame, int ms)
            {
                Marshal.Copy(frame, 0, memory, frame.Length);
                return stillness.Observe(memory, 192, 128, origin + ms * Stopwatch.Frequency / 1000);
            }
            Check(Observe(flat, 0) == StillnessDecision.Moving && Observe(vibration, 75) == StillnessDecision.Moving &&
                Observe(vibration, 150) == StillnessDecision.Still, "0.4% disturbance allows the original 150ms stillness window to complete");
        }
        finally { Marshal.FreeHGlobal(memory); }

        // Placement settles before the independent fallback can become due.
        var camera = new Camera(); var decoder = new Decoder();
        await using (var source = Source(camera, decoder, "200"))
        {
            await source.StartControlledAsync(Wire.Id(), _ => true, _ => { }, default);
            await Task.Delay(450);
            Check(decoder.Seen.IsEmpty && source.ConfirmationSource.Contains("movingFrames="), "no frame is decoded while the parcel is being placed");
            camera.Moving = false; var settledAt = Environment.TickCount64;
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (decoder.Seen.Count < 3 && DateTime.UtcNow < deadline) await Task.Delay(20);
            var seen = decoder.Seen.ToArray();
            Check(seen.Length >= 3 && seen.All(s => s.Marker == StillMarker), "once the parcel rests, only resting frames are decoded, and decoding continues");
            Check(seen[0].At - settledAt >= 150 && seen[0].At - settledAt < 1200, "the first decode waits for the still time (about 200 ms), not a whole decode cycle");
            Check(source.ReadDiagnostics().DecodeMedianMs is >= 0 and < 1000, "diagnostics report the recent median decode time that sets the scan pace");
            await source.StopAsync(default);
        }

        // Never settles: the native path must still decode once the fallback is due.
        await NativePreviewChecks();
        var shaking = new Camera(); var fallback = new Decoder();
        await using (var source = Source(shaking, fallback, "200"))
        {
            await source.StartControlledAsync(Wire.Id(), _ => true, _ => { }, default);
            await CameraRoundHarness.Until(() => Task.FromResult(fallback.Seen.Count >= 2));
            var seen = fallback.Seen.ToArray();
            Check(seen[1].At - seen[0].At is >= 780 and < 1100 && !source.ConfirmationSource.Contains("fallbackDecodes=0"),
                "software recognition continues during large movement with an approximately 800ms fallback");
            await source.StopAsync(default);
        }

        // 0 turns the gate off: every frame may be decoded, as before.
        var off = new Camera(); var every = new Decoder();
        await using (var source = Source(off, every, "0"))
        {
            await source.StartControlledAsync(Wire.Id(), _ => true, _ => { }, default);
            await Task.Delay(500);
            Check(every.Seen.Any(s => s.Marker == MovingMarker), "with the still time set to 0, moving frames are decoded as before");
            await source.StopAsync(default);
        }
        // The pace line maintenance reads on 设备与诊断.
        var fast = Ipc.Application.Sportmaster.Desktop.CameraAcquisitionPresentation.Pace(150);
        var slow = Ipc.Application.Sportmaster.Desktop.CameraAcquisitionPresentation.Pace(1700);
        Check(fast == "单帧识别耗时约 150 ms（最近中位数）" && slow.Contains("识别较慢") && slow.Contains("需要识别的条码种类") &&
            Ipc.Application.Sportmaster.Desktop.CameraAcquisitionPresentation.Pace(null).Contains("尚无样本"), "a slow decode names the setting that speeds it up; a fast one stays a plain number");
        try { _ = Source(new(), new(), "2500"); throw new Exception("out-of-range still time accepted"); }
        catch (InvalidDataException) { Check(true, "a still time outside 0-2000 ms is refused"); }
    }

    static async Task NativePreviewChecks()
    {
        var moving = new Camera(native: true);
        var observations = new ConcurrentQueue<Observation>();
        var feedback = new ConcurrentQueue<ObservationReadFeedback>();
        await using (var source = Source(moving, null, "300"))
        {
            source.ReadFeedback += feedback.Enqueue;
            await source.StartControlledAsync(Wire.Id(), o => { observations.Enqueue(o); return true; }, _ => { }, default);
            await CameraRoundHarness.Until(() => Task.FromResult(
                moving.NativeCalls.TryPeek(out var first) && Stopwatch.GetElapsedTime(first.At).TotalMilliseconds >= 1000));
            var calls = moving.NativeCalls.ToArray();
            var measured = calls.Where(c => Stopwatch.GetElapsedTime(calls[0].At, c.At).TotalMilliseconds < 1000).ToArray();
            Check(measured.Length > 10 && measured.All(c => !c.Decode) && observations.IsEmpty && feedback.IsEmpty &&
                source.ConfirmationSource.Contains("emptyFrames=0;") &&
                source.Timings.Snapshot().Stages["motion.moving-preview"].Count > 0,
                $"first 1000ms continuous motion: native Acquire={measured.Count(c => c.Decode)}, preview={measured.Count(c => !c.Decode)}, no empty observation");
            await source.StopAsync(default);
        }

        var settling = new Camera(native: true);
        await using (var source = Source(settling, null))
        {
            var inspected = new TaskCompletionSource<InspectedFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
            await source.SetInspectionAsync(new("synthetic-debug", true, frame => { inspected.TrySetResult(frame); return true; })
            { CutoverKey = "synthetic-debug-read", MinimumDecodeIntervalMs = _ => 1000 }, default);
            await source.StartControlledAsync(Wire.Id(), _ => throw new InvalidOperationException("Isolated debug frame reached production"), _ => { }, default);
            await CameraRoundHarness.Until(() => Task.FromResult(settling.NativeCalls.Count >= 6));
            settling.Moving = false;
            var stoppedAt = Stopwatch.GetTimestamp();
            var result = await inspected.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var firstDecode = settling.NativeCalls.First(c => c.Decode);
            var delay = Stopwatch.GetElapsedTime(stoppedAt, firstDecode.At).TotalMilliseconds;
            Check(result.Symbols.Single().Text == "SYNTH-STILL" && delay is >= 105 and < 500,
                $"isolated cutover uses native decode once motion settles (default 150ms, delay {delay:F0}ms), without charging preview to the 1000ms decode interval");
        }

        var shaking = new Camera(native: true);
        await using (var source = Source(shaking, null))
        {
            await source.StartControlledAsync(Wire.Id(), _ => true, _ => { }, default);
            await CameraRoundHarness.Until(() => Task.FromResult(shaking.NativeCalls.Any(c => c.Decode) &&
                !source.ConfirmationSource.Contains("fallbackDecodes=0") &&
                shaking.NativeCalls.TryPeek(out var first) && Stopwatch.GetElapsedTime(first.At).TotalMilliseconds >= 1000));
            var calls = shaking.NativeCalls.ToArray();
            var elapsed = Stopwatch.GetElapsedTime(calls[0].At, calls.First(c => c.Decode).At).TotalMilliseconds;
            var firstSecond = calls.Where(c => Stopwatch.GetElapsedTime(calls[0].At, c.At).TotalMilliseconds < 1000).ToArray();
            Check(elapsed is >= 790 and < 1000 && firstSecond.Count(c => c.Decode) == 1,
                $"default settings, first 1000ms of continuous motion: native Acquire=1 at {elapsed:F0}ms (800ms fallback), preview={firstSecond.Count(c => !c.Decode)}");
        }

        var late = new Camera(native: true) { Moving = false, MoveOnNextDecode = true };
        await using (var source = Source(late, null))
        {
            var result = new TaskCompletionSource<Observation>(TaskCreationOptions.RunContinuationsAsynchronously);
            await source.StartControlledAsync(Wire.Id(), o => { result.TrySetResult(o); return true; }, _ => { }, default);
            var decoded = await result.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var calls = late.NativeCalls.ToArray();
            var first = Array.FindIndex(calls, c => c.Decode);
            var second = Array.FindIndex(calls, first + 1, c => c.Decode);
            Check(decoded.Codes.Single() == "SYNTH-STILL" && second > first + 1 &&
                Stopwatch.GetElapsedTime(calls[first].At, calls[second].At).TotalMilliseconds >= 150,
                "motion inside a native decode discards the result and returns to preview until the scene settles again");
        }
    }
}

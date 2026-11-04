using System.Collections.Concurrent;
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
    sealed class Camera : IMvsApi
    {
        public volatile bool Moving = true;
        int offset; uint number; nint buffer;
        public bool HasFrame { get; private set; }
        public void Initialize() { }
        public MvsDevice[] Enumerate() => [new("192.0.2.30", "SYNTH-STILL", "SYNTHETIC", "SYNTHETIC", [])];
        public void Create(MvsDevice device) { }
        public void Open() { }
        public uint EnumValue(string key) => key switch { "PixelFormat" => MvsNative.Mono8, "AcquisitionMode" => 2u, _ => 0u };
        public long PayloadSize() => W * H;
        public void ConfigureBuffers(uint nodes, uint strategy = 0) { }
        public void Start() { }
        public MvsFrame? Acquire()
        {
            Thread.Sleep(20);
            if (Moving) offset = (offset + 24) % (W - 120);
            var pixels = new byte[W * H]; Array.Fill(pixels, (byte)90);
            for (var y = 150; y < 270; y++) for (var x = offset; x < offset + 120; x++) pixels[y * W + x] = 240;
            pixels[0] = Moving ? MovingMarker : StillMarker;
            buffer = Marshal.AllocHGlobal(pixels.Length); Marshal.Copy(pixels, 0, buffer, pixels.Length); HasFrame = true;
            return new(buffer, W, H, MvsNative.Mono8, (ulong)pixels.Length, ++number, 0, 0);
        }
        public void Release() { if (HasFrame) { Marshal.FreeHGlobal(buffer); HasFrame = false; } }
        public bool Connected() => true;
        public void Stop() { }
        public void Close() { }
        public void Destroy() { }
        public void Shutdown() { }
    }

    static MvsAcquisition Source(Camera camera, Decoder decoder, string stillMs) =>
        new(new("still-camera", "still-camera", new Dictionary<string, string> { ["device.ip"] = "192.0.2.30", ["device.serial"] = "SYNTH-STILL", ["motion.still-ms"] = stillMs }),
            camera, _ => new Lease(), decoder: decoder);

    internal static async Task Run()
    {
        // The comparison itself.
        var flat = new byte[24576]; Array.Fill(flat, (byte)100);
        var brighter = flat.Select(b => (byte)(b + 30)).ToArray();
        var noisy = flat.Select((b, i) => (byte)(b + (i % 7) - 3)).ToArray();
        var edge = (byte[])flat.Clone(); for (var i = 0; i < 200; i++) edge[i * 50] = 230;
        Check(!MvsStillness.Moved(flat, flat) && !MvsStillness.Moved(flat, brighter) && !MvsStillness.Moved(flat, noisy),
            "an unchanged scene, a global exposure shift and sensor noise do not count as movement");
        Check(MvsStillness.Moved(flat, edge), "about 0.8% of the scene changing (a hand or parcel edge) counts as movement");

        // Placement: moving for 700 ms, then resting.
        var camera = new Camera(); var decoder = new Decoder();
        await using (var source = Source(camera, decoder, "200"))
        {
            await source.StartControlledAsync(Wire.Id(), _ => true, _ => { }, default);
            await Task.Delay(700);
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

        // Never settles (vibration, a hand resting on the parcel): still read about every 1.5 s.
        var shaking = new Camera(); var fallback = new Decoder();
        await using (var source = Source(shaking, fallback, "200"))
        {
            await source.StartControlledAsync(Wire.Id(), _ => true, _ => { }, default);
            await Task.Delay(3400);
            var seen = fallback.Seen.ToArray();
            Check(seen.Length is >= 1 and <= 3 && source.ConfirmationSource.Contains("fallbackDecodes=") && !source.ConfirmationSource.Contains("fallbackDecodes=0"),
                "a scene that never settles still gets a fallback decode about every 1.5 s, so the station keeps reading");
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
}

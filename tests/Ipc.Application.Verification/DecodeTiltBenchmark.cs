using System.Diagnostics;
using System.Runtime.InteropServices;
using Ipc.Drivers.Hikrobot.Mvs;
using Ipc.Platform;
using ZXingCpp;

// Tilted-label recognition at site resolution: success and cost per angle and per stage.
// Synthetic only: a Code128 label on white paper, slightly blurred, on a textured 3072x2048
// Mono8 background, rotated by each angle. Indicative for this machine, not the site camera or IPC.
// Usage: --decode-tilt-benchmark [--rounds N] [--scale MODULE_PX]
//        --decode-tilt-benchmark --images DIR --expect CODE [--rounds N]
// The second form decodes every 8-bit binary PGM (P5) frame in DIR - for example rotated copies of a site photo kept
// locally on D:. Site photos are customer originals: keep them out of the repository (docs/baseline/PUBLICATION.md).
static class DecodeTiltBenchmark
{
    const int Width = 3072, Height = 2048;
    internal static void Run(string[] args)
    {
        var cache = Environment.GetEnvironmentVariable("NUGET_PACKAGES") ?? throw new InvalidOperationException("D-drive restored cache required");
        var library = NativeLibrary.Load(Path.Combine(cache, OperatingSystem.IsWindows() ? "zxingcpp/0.5.3/runtimes/win-x64/native/ZXing.dll" : "zxingcpp/0.5.3/runtimes/linux-x64/native/libZXing.so"));
        NativeLibrary.SetDllImportResolver(typeof(BarcodeReader).Assembly, (name, _, _) => name is "ZXing" or "ZXing.dll" ? library : 0);
        int Option(string name, int fallback) => args.Contains(name) ? int.Parse(args[Array.IndexOf(args, name) + 1]) : fallback;
        var rounds = Option("--rounds", 3); var scale = Option("--scale", 3);
        var settings = new ReaderSettings(["Code128", "Code39", "EAN13"], true);
        var decoder = new SoftwareDecoder("utf-8", () => { }, settings: settings); decoder.Prepare();
        using var native = new BarcodeReader { Formats = settings.NativeFormats(), TryHarder = true, TryRotate = true, TryInvert = true, TryDownscale = true, MinLineCount = 2 };
        if (args.Contains("--images"))
        {
            var folder = args[Array.IndexOf(args, "--images") + 1]; var expect = args[Array.IndexOf(args, "--expect") + 1];
            Console.WriteLine($"DECODE-TILT-BENCHMARK images={folder} rounds={rounds} cores={Environment.ProcessorCount}");
            try
            {
                foreach (var file in Directory.GetFiles(folder, "*.pgm").Order())
                {
                    var (pixels, w, h) = ReadPgm(file);
                    var samples = new List<double>(); RecognitionSymbol[] found = []; string? rejected = null;
                    for (var r = 0; r <= rounds; r++)
                    {
                        decoder.Prepare(); var watch = Stopwatch.StartNew();
                        try { found = decoder.DecodeDetailed(pixels, pixels.Length, w, h); rejected = null; }
                        catch (MvsFrameRejection error) { found = []; rejected = error.Reason; }
                        if (r > 0 || rounds == 0) samples.Add(watch.Elapsed.TotalMilliseconds);
                    }
                    samples.Sort(); var stages = decoder.TimingSnapshot.Stages;
                    long Count(string key) => stages.TryGetValue(key, out var s) ? s.Count : 0;
                    var ok = found.Length == 1 && found[0].Text == expect;
                    Console.WriteLine($"  {Path.GetFileName(file),-18} {(ok ? "OK " : "MISS")} {(rejected is null ? "codes=" + string.Join("|", found.Select(f => f.Text)) : "rejected=" + rejected),-28} total {samples[samples.Count / 2],7:F0} ms" +
                        $"  [skip {Count("regions.explained-before-search")} early {Count("regions.explained-early")} sharpened {Count("regions.explained-sharpened")} full {Count("regions.full-search")}]");
                }
            }
            finally { decoder.Release(); }
            return;
        }
        Console.WriteLine($"DECODE-TILT-BENCHMARK {Width}x{Height} module={scale}px rounds={rounds} cores={Environment.ProcessorCount} formats=Code128+Code39+EAN13");
        try
        {
            foreach (var angle in new[] { 0, 5, 10, 15, 20, 30, 40, 45, 50, 60, 75, 90, 135 })
            {
                var frame = Frame("JYMW02263", scale, angle);
                var samples = new List<double>(); RecognitionSymbol[] found = [];
                Dictionary<string, MvsStageTiming>? stages = null;
                for (var r = 0; r <= rounds; r++)
                {
                    decoder.Prepare(); // resets the stage timings: one frame per snapshot
                    var watch = Stopwatch.StartNew();
                    try { found = decoder.DecodeDetailed(frame, frame.Length, Width, Height); }
                    catch (MvsFrameRejection error) { found = []; Console.WriteLine($"  angle {angle,3}: rejected {error.Reason}"); }
                    if (r > 0) samples.Add(watch.Elapsed.TotalMilliseconds);
                    stages = decoder.TimingSnapshot.Stages;
                }
                samples.Sort();
                double Stage(string key) => stages!.TryGetValue(key, out var s) ? s.TotalMs : 0;
                long Count(string key) => stages!.TryGetValue(key, out var s) ? s.Count : 0;
                var ok = found is [{ Text: "JYMW02263" }];
                var pin = GCHandle.Alloc(frame, GCHandleType.Pinned); bool nativeOk;
                try { var codes = native.From(new ImageView(pin.AddrOfPinnedObject(), Width, Height, ImageFormat.Lum)); nativeOk = codes.Any(c => c.Text == "JYMW02263"); foreach (var c in codes) c.Dispose(); }
                finally { pin.Free(); }
                Console.WriteLine($"  angle {angle,3}°  {(ok ? "OK " : "MISS")} candidates={found.Length,-2} full-frame-alone={(nativeOk ? "yes" : "no ")} total {samples[samples.Count / 2],7:F0} ms" +
                    $"  native {Stage("zxing.native"),6:F0}  threshold {Stage("zxing.threshold"),6:F0}  regions {Stage("zxing.regions"),6:F0}" +
                    $"  [skip {Count("regions.explained-before-search")} early {Count("regions.explained-early")} sharpened {Count("regions.explained-sharpened")} full {Count("regions.full-search")}]");
            }
        }
        finally { decoder.Release(); }
    }

    static (byte[] Pixels, int Width, int Height) ReadPgm(string path)
    {
        var bytes = File.ReadAllBytes(path); var at = 0;
        string Token()
        {
            while (char.IsWhiteSpace((char)bytes[at]) || bytes[at] == '#') { if (bytes[at] == '#') while (bytes[at] != '\n') at++; at++; }
            var start = at; while (!char.IsWhiteSpace((char)bytes[at])) at++;
            return System.Text.Encoding.ASCII.GetString(bytes, start, at - start);
        }
        if (Token() != "P5") throw new InvalidDataException("Only binary PGM (P5) frames");
        int w = int.Parse(Token()), h = int.Parse(Token()); if (Token() != "255") throw new InvalidDataException("Only 8-bit PGM");
        at++;
        return (bytes.AsSpan(at, w * h).ToArray(), w, h);
    }
    // Label patch: white paper with quiet zone, bar height 40% of the symbol width, light blur, then rotated onto texture.
    internal static byte[] Frame(string text, int scale, double degrees, int seed = 29)
    {
        using var barcode = new Barcode(text, new CreatorOptions(BarcodeFormat.Code128));
        using var image = barcode.ToImage(new WriterOptions { Scale = scale, AddQuietZones = true });
        var row = new byte[image.Width]; Marshal.Copy(image.Data, row, 0, row.Length);
        int lw = image.Width + 60, lh = (int)(image.Width * .4) + 80;
        var label = new byte[lw * lh];
        for (var y = 0; y < lh; y++) for (var x = 0; x < lw; x++)
            {
                var inBars = x >= 30 && x < 30 + image.Width && y >= 40 && y < lh - 40;
                label[y * lw + x] = inBars ? (row[x - 30] < 128 ? (byte)35 : (byte)230) : (byte)230;
            }
        var random = new Random(seed);
        var frame = new byte[Width * Height];
        for (var i = 0; i < frame.Length; i++) frame[i] = (byte)random.Next(90, 171);
        double r = degrees * Math.PI / 180, c = Math.Cos(r), s = Math.Sin(r);
        double cx = Width / 2d, cy = Height / 2d;
        for (var y = 0; y < Height; y++) for (var x = 0; x < Width; x++)
            {
                var u = (x - cx) * c + (y - cy) * s + lw / 2d; var v = -(x - cx) * s + (y - cy) * c + lh / 2d;
                if (u < 0 || v < 0 || u >= lw - 1 || v >= lh - 1) continue;
                int u0 = (int)u, v0 = (int)v; double fu = u - u0, fv = v - v0;
                var value = label[v0 * lw + u0] * (1 - fu) * (1 - fv) + label[v0 * lw + u0 + 1] * fu * (1 - fv) +
                    label[(v0 + 1) * lw + u0] * (1 - fu) * fv + label[(v0 + 1) * lw + u0 + 1] * fu * fv;
                frame[y * Width + x] = (byte)Math.Round(value);
            }
        var blurred = (byte[])frame.Clone();
        for (var y = 1; y < Height - 1; y++) for (var x = 1; x < Width - 1; x++)
            {
                var i = y * Width + x;
                blurred[i] = (byte)((frame[i] * 4 + (frame[i - 1] + frame[i + 1] + frame[i - Width] + frame[i + Width]) * 2 +
                    frame[i - Width - 1] + frame[i - Width + 1] + frame[i + Width - 1] + frame[i + Width + 1]) / 16);
            }
        return blurred;
    }
}

using System.Diagnostics;
using System.Runtime.InteropServices;
using Ipc.Drivers.Hikrobot.Mvs;
using ZXingCpp;

// Synthetic decode cost per configured symbology set (SM-SAME-PLACEMENT-29 follow-up).
// Textured 3072x2048 Mono8 frames: one with a Code128 label, one empty tray.
// Indicative only: synthetic texture on this machine, not the site camera or IPC.
static partial class SportmasterRcs
{
    public static void DecodeFormatBenchmark(string[] args)
    {
        // Same restored native as the round harness (D-drive NuGet cache on Windows).
        var cache = Environment.GetEnvironmentVariable("NUGET_PACKAGES") ?? throw new InvalidOperationException("D-drive restored cache required");
        var library = NativeLibrary.Load(Path.Combine(cache, "zxingcpp/0.5.3/runtimes/win-x64/native/ZXing.dll"));
        NativeLibrary.SetDllImportResolver(typeof(BarcodeReader).Assembly, (name, _, _) => name is "ZXing" or "ZXing.dll" ? library : 0);
        var rounds = args.Contains("--rounds") ? int.Parse(args[Array.IndexOf(args, "--rounds") + 1]) : 5;
        const int width = 3072, height = 2048;
        var random = new Random(29);
        var empty = new byte[width * height];
        for (var i = 0; i < empty.Length; i++) empty[i] = (byte)random.Next(90, 171);
        var code = (byte[])empty.Clone();
        var label = MakeSoftwareBarcode("JYMW02263");
        for (var y = 0; y < label.Height + 40; y++)
            for (var x = 0; x < label.Width + 40; x++)
                code[(y + 1200) * width + x + 1400] = y < 20 || x < 20 || y >= label.Height + 20 || x >= label.Width + 20 ? (byte)235 :
                    label.Data[(y - 20) * label.Width + x - 20] == 0 ? (byte)30 : (byte)235;
        (string Name, ReaderSettings? Settings)[] cases =
        [
            ("All + invert (current default)", null),
            ("All, invert off", new(["All"], false)),
            ("Code128 + invert", new(["Code128"], true)),
            ("Code128, invert off", new(["Code128"], false)),
            ("Code128+Code39+EAN13 + invert", new(["Code128", "Code39", "EAN13"], true)),
            ("Code128+Code39+EAN13+QRCode + invert", new(["Code128", "Code39", "EAN13", "QRCode"], true)),
            ("Code128+QRCode+DataMatrix + invert", new(["Code128", "QRCode", "DataMatrix"], true)),
        ];
        Console.WriteLine($"DECODE-FORMAT-BENCHMARK {width}x{height} rounds={rounds} cores={Environment.ProcessorCount}");
        foreach (var (name, settings) in cases)
        {
            var decoder = new SoftwareDecoder("utf-8", () => { }, settings: settings); decoder.Prepare();
            try
            {
                double Median(byte[] frame, out string[] found)
                {
                    found = decoder.Decode(frame, frame.Length, width, height); // warm-up
                    var samples = new List<double>();
                    for (var r = 0; r < rounds; r++)
                    {
                        var watch = Stopwatch.StartNew(); found = decoder.Decode(frame, frame.Length, width, height);
                        samples.Add(watch.Elapsed.TotalMilliseconds);
                    }
                    samples.Sort(); return samples[samples.Count / 2];
                }
                var withCode = Median(code, out var codes); var withoutCode = Median(empty, out var none);
                if (!codes.SequenceEqual(new[] { "JYMW02263" }) || none.Length != 0)
                    throw new InvalidOperationException(name + " decoded " + string.Join("|", codes) + " / empty " + string.Join("|", none));
                Console.WriteLine($"  {name,-32} label {withCode,8:F1} ms ({1000 / withCode,5:F2}/s)   empty tray {withoutCode,8:F1} ms ({1000 / withoutCode,5:F2}/s)");
            }
            finally { decoder.Release(); }
        }
    }
}

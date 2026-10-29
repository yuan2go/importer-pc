using System.Diagnostics;
using System.Reflection;
using Ipc.Drivers.Hikrobot.Mvs;
using Ipc.Platform;

static partial class SportmasterRcs
{
    // Offline actual-engine measurement. Never opens a camera or calls RCS.
    public static void MvsLatency(string[] args)
    {
        string? Value(string key) => Array.IndexOf(args, key) is var i && i >= 0 ? args[i + 1] : null;
        var output = Value("--timing-output") ?? throw new ArgumentException("New absolute --timing-output file required");
        if (!Path.IsPathFullyQualified(output) || File.Exists(output)) throw new ArgumentException("Timing output must be a new absolute file");
        var repeats = int.Parse(Value("--repeats") ?? "3");
        if (repeats is < 1 or > 20) throw new ArgumentException("Repeats must be 1..20");
        PrepareZxingFixture();
        var directory = Value("--private-pgm-directory");
        var images = directory is null ? SyntheticLatencyImages() : LoadLatencyPgms(directory);
        if (images.Length is < 1 or > 256) throw new ArgumentException("1..256 bounded Mono8 inputs required");
        var reference = args.Contains("--reference-contrast");
        var decoder = new SoftwareDecoder("utf-8", PrepareZxingFixture, reference ? OriginalScalarContrast : null); decoder.Prepare();
        var samples = new List<object>();
        var counts = new Dictionary<int, string[]>();
        using var process = Process.GetCurrentProcess();
        var cpu = process.TotalProcessorTime;
        var allocated = GC.GetTotalAllocatedBytes();
        var clock = Stopwatch.StartNew();
        try
        {
            for (var pass = 0; pass < repeats; pass++)
            for (var index = 0; index < images.Length; index++)
            {
                var image = images[index]; var work = image.Data.ToArray(); // original file/bytes never mutated
                var sampleCpu = process.TotalProcessorTime;
                var gen2 = GC.CollectionCount(2);
                var started = Stopwatch.GetTimestamp();
                var codes = decoder.Decode(work, work.Length, image.Width, image.Height);
                var decodeMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                var cpuMs = (process.TotalProcessorTime - sampleCpu).TotalMilliseconds;
                var collections = GC.CollectionCount(2) - gen2;
                if (counts.TryGetValue(index, out var expected))
                    Check(codes.Order().SequenceEqual(expected.Order()), "offline repeated image preserves every candidate including multiplicity");
                else counts.Add(index, codes);
                samples.Add(new { Pass = pass, Image = index, image.Width, image.Height, CandidateCount = codes.Length,
                    DecodeMs = decodeMs, CpuMs = cpuMs, Gen2Collections = collections });
            }
            var result = new { VerifierSource = typeof(SportmasterRcs).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion,
                Tier = directory is null ? "Synthetic Mono8; actual ZXing-C++; no camera/RCS" : "Offline private recorded Mono8; actual ZXing-C++; no live capture/RCS",
                Contrast = reference ? "OriginalScalarFormulaVerificationOnly" : "ProductionSimdLookup", images.Length, Repeats = repeats, ElapsedMs = clock.Elapsed.TotalMilliseconds,
                CpuMs = (process.TotalProcessorTime - cpu).TotalMilliseconds,
                AllocatedBytes = GC.GetTotalAllocatedBytes() - allocated, Timings = decoder.TimingSnapshot, Samples = samples,
                Boundary = "Includes first-call cold sample; input file IO and copying excluded from DecodeMs; no camera exposure/transport/SDK or Application timings" };
            ExtensionStore.AtomicWrite(output, result);
            Console.WriteLine("MVS_LATENCY_MEASURED images=" + images.Length + "; repeats=" + repeats + "; no live camera/production acceptance");
            Console.WriteLine("TimingOutput=" + output);
        }
        finally { decoder.Release(); }
    }
    static void OriginalScalarContrast(byte[] pixels, int length)
    {
        byte lo = 255, hi = 0;
        for (var i = 0; i < length; i++) { lo = Math.Min(lo, pixels[i]); hi = Math.Max(hi, pixels[i]); }
        if (hi > lo && (lo != 0 || hi != 255))
            for (var i = 0; i < length; i++) pixels[i] = (byte)((pixels[i] - lo) * 255 / (hi - lo));
    }
    static SoftwarePixels[] LoadLatencyPgms(string directory)
    {
        if (!Path.IsPathFullyQualified(directory)) throw new ArgumentException("Private PGM directory must be absolute");
        var files = Directory.EnumerateFiles(directory, "*.pgm").Take(257).Order().ToArray();
        if (files.Length is < 1 or > 256 || files.Sum(p => new FileInfo(p).Length) > 536870912)
            throw new ArgumentException("Offline batch exceeds 256 files / 512 MiB budget");
        return files.Select(ReadLatencyPgm).ToArray();
    }
    static SoftwarePixels[] SyntheticLatencyImages(string value = "SYNTH-LATENCY-0001")
    {
        var barcode = MakeSoftwareBarcode(value);
        const int width = 3072, height = 2048;
        var pixels = Enumerable.Repeat((byte)210, width * height).ToArray();
        for (var y = 0; y < barcode.Height; y++)
            for (var x = 0; x < barcode.Width; x++)
                pixels[(y + 500) * width + x + 500] = barcode.Data[y * barcode.Width + x] == 0 ? (byte)30 : (byte)210;
        return [new(pixels, width, height), new(Enumerable.Repeat((byte)127, width * height).ToArray(), width, height)];
    }
    public static void MvsContrastChecks()
    {
        var cases = 0;
        for (var lo = 0; lo <= 255; lo++)
        for (var hi = lo; hi <= 255; hi++)
        {
            // Every level, vector boundary/tail, constant and full-range cases;
            // sentinel outside the image must not participate or be overwritten.
            var original = Enumerable.Range(0, 303).Select(i => (byte)(lo + i % (hi - lo + 1))).ToArray();
            original[0] = (byte)lo; original[1] = (byte)hi;
            var expected = original.ToArray();
            if (hi > lo && (lo != 0 || hi != 255))
                for (var i = 0; i < expected.Length; i++) expected[i] = (byte)((expected[i] - lo) * 255 / (hi - lo));
            var actual = original.Concat(new byte[] { 0, 255, 43 }).ToArray();
            SoftwareDecoder.ExpandContrast(actual.AsSpan(0, original.Length));
            if (!actual.AsSpan(0, original.Length).SequenceEqual(expected) || !actual.AsSpan(original.Length).SequenceEqual(new byte[] { 0, 255, 43 }))
                throw new Exception("Contrast differed from original scalar mapping");
            cases++;
        }
        foreach (var length in new[] { 0, 1, 2, 15, 31, 32, 33, 63, 64, 65, 127, 128, 129 })
        {
            var original = Enumerable.Range(0, length).Select(i => (byte)(20 + i % 171)).ToArray(); var expected = original.ToArray();
            if (length > 1)
                for (var i = 0; i < length; i++) expected[i] = (byte)((expected[i] - 20) * 255 / (original.Max() - 20));
            SoftwareDecoder.ExpandContrast(original);
            Check(original.SequenceEqual(expected), "short/vector-boundary contrast preserves exact scalar pixels");
        }
        Check(cases == 32896, "all 32896 low/high intensity pairs preserve exact original pixels and pooled-buffer tail");
    }
    static SoftwarePixels ReadLatencyPgm(string path)
    {
        // Existing private diagnostic writes the canonical three-line P5 header.
        var data = File.ReadAllBytes(path); var end = 0;
        for (var i = 0; i < 3; i++) { end = Array.IndexOf(data, (byte)10, end); if (end < 0) throw new InvalidDataException("PGM header incomplete"); end++; }
        var header = System.Text.Encoding.ASCII.GetString(data, 0, end).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (header.Length != 4 || header[0] != "P5" || header[3] != "255") throw new InvalidDataException("Canonical Mono8 PGM required");
        var width = int.Parse(header[1]); var height = int.Parse(header[2]);
        if (width <= 0 || height <= 0 || (long)width * height > 33554432 || data.Length - end != (long)width * height) throw new InvalidDataException("Incomplete/oversized Mono8 PGM");
        return new(data[end..], width, height);
    }
}

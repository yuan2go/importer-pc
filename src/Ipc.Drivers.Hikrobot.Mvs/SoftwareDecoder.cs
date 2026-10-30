using System.Runtime.InteropServices;
using System.Text;
using System.Numerics;
using Ipc.Platform;
using ZXingCpp;

namespace Ipc.Drivers.Hikrobot.Mvs;

// The same 3.1.1 engine used in the real-camera diagnostic, directly in .NET.
// No image files, subprocess, Python runtime, barcode deduplication or RCS work.
internal sealed class SoftwareDecoder(string charset, Action? prepareNative = null, Action<byte[], int>? contrastForVerification = null) : IMonoBarcodeDecoder
{
    const int CandidateLimit = 64, CodeByteLimit = 4096;
    BarcodeReader? reader;
    readonly MvsTimings timings = new();
    public MvsTimingSnapshot TimingSnapshot => timings.Snapshot();
    readonly Encoding encoding = charset switch {
        "utf-8" => new UTF8Encoding(false, true),
        "ascii" => Encoding.GetEncoding("us-ascii", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback),
        _ => throw new InvalidDataException("Unsupported explicit barcode encoding") };
    public void Prepare()
    {
        timings.Reset();
        (prepareNative ?? DecoderRuntime.Prepare)();
        reader ??= new BarcodeReader { Formats = BarcodeFormat.All, TryHarder = true, TryRotate = true,
            TryInvert = true, TryDownscale = true, ReturnErrors = false, MaxNumberOfSymbols = CandidateLimit };
    }
    public string[] Decode(byte[] pixels, int length, int width, int height)
        => DecodeDetailed(pixels, length, width, height).Select(symbol => symbol.Text).ToArray();
    public RecognitionSymbol[] DecodeDetailed(byte[] pixels, int length, int width, int height)
    {
        if (reader is null || width <= 0 || height <= 0 || length != checked(width * height) || length > pixels.Length)
            throw new InvalidDataException("Decoder not prepared or incomplete Mono8 image");
        using var total = timings.Measure("decode.total");
        using (timings.Measure("contrast"))
            if (contrastForVerification is null) ExpandContrast(pixels.AsSpan(0, length));
            else contrastForVerification(pixels, length);
        var pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        Barcode[]? results = null;
        try
        {
            using (timings.Measure("zxing.native"))
                results = reader.From(new ImageView(pin.AddrOfPinnedObject(), width, height, ImageFormat.Lum));
            using var extraction = timings.Measure("candidates.extract");
            if (results.Length >= CandidateLimit) throw new InvalidDataException("Recognition candidate limit reached; complete result cannot be guaranteed");
            return results.Select(result => {
                if (!result.IsValid || result.Bytes.Length > CodeByteLimit) throw new InvalidDataException("Invalid/oversized recognition result");
                var p = result.Position;
                return new RecognitionSymbol(encoding.GetString(result.Bytes), result.Format.ToString(),
                    [new(p.TopLeft.X, p.TopLeft.Y), new(p.TopRight.X, p.TopRight.Y),
                     new(p.BottomRight.X, p.BottomRight.Y), new(p.BottomLeft.X, p.BottomLeft.Y)], result.Orientation);
            }).ToArray();
        }
        finally { if (results is not null) foreach (var result in results) result.Dispose(); pin.Free(); }
    }
    internal static void ExpandContrast(Span<byte> pixels)
    {
        // Same integer mapping as before, including floor rounding and constant
        // images. Scan with SIMD and divide only for the <=256 possible levels.
        byte lo = 255, hi = 0;
        var index = 0;
        if (Vector.IsHardwareAccelerated && pixels.Length >= Vector<byte>.Count)
        {
            var minimum = new Vector<byte>(255); var maximum = Vector<byte>.Zero;
            for (; index <= pixels.Length - Vector<byte>.Count; index += Vector<byte>.Count)
            {
                var values = new Vector<byte>(pixels.Slice(index, Vector<byte>.Count));
                minimum = Vector.Min(minimum, values); maximum = Vector.Max(maximum, values);
            }
            for (var lane = 0; lane < Vector<byte>.Count; lane++) { lo = Math.Min(lo, minimum[lane]); hi = Math.Max(hi, maximum[lane]); }
        }
        for (; index < pixels.Length; index++) { lo = Math.Min(lo, pixels[index]); hi = Math.Max(hi, pixels[index]); }
        if (hi <= lo || lo == 0 && hi == 255) return;
        Span<byte> levels = stackalloc byte[256];
        for (var value = (int)lo; value <= hi; value++) levels[value] = (byte)((value - lo) * 255 / (hi - lo));
        for (index = 0; index < pixels.Length; index++) pixels[index] = levels[pixels[index]];
    }
    public void Release() { reader?.Dispose(); reader = null; }
}

internal static class DecoderRuntime
{
    static readonly object gate = new();
    static bool configured;
    sealed record Inventory(string Package, string Version, string Engine, ExtensionFile File);
    internal static void Prepare()
    {
        lock (gate)
        {
            if (configured) return;
            using var stream = typeof(DecoderRuntime).Assembly.GetManifestResourceStream("Mvs.ZxingRuntime.json")!;
            using var text = new StreamReader(stream); var inventory = Wire.Decode<Inventory>(text.ReadToEnd());
            if (inventory.Package != "ZXingCpp" || inventory.Version != "0.5.3" || inventory.Engine != "3.1.1")
                throw new InvalidDataException("Unreviewed software recognition runtime");
            var handle = NativeSdkBoundary.LoadBundledFiles(typeof(DecoderRuntime).Assembly, "vendor/zxing-cpp", [inventory.File], "ZXing.dll", []);
            NativeLibrary.SetDllImportResolver(typeof(BarcodeReader).Assembly, (name, _, _) =>
                name is "ZXing" or "ZXing.dll" ? handle : throw new DllNotFoundException("Undeclared recognition runtime import"));
            configured = true;
        }
    }
}

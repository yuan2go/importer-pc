using System.Runtime.InteropServices;
using System.Text;
using Ipc.Platform;
using ZXingCpp;

namespace Ipc.Drivers.Hikrobot.Mvs;

// The same 3.1.1 engine used in the real-camera diagnostic, directly in .NET.
// No image files, subprocess, Python runtime, barcode deduplication or RCS work.
internal sealed class SoftwareDecoder(string charset, Action? prepareNative = null) : IMonoBarcodeDecoder
{
    const int CandidateLimit = 64, CodeByteLimit = 4096;
    BarcodeReader? reader;
    readonly Encoding encoding = charset switch {
        "utf-8" => new UTF8Encoding(false, true),
        "ascii" => Encoding.GetEncoding("us-ascii", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback),
        _ => throw new InvalidDataException("Unsupported explicit barcode encoding") };
    public void Prepare()
    {
        (prepareNative ?? DecoderRuntime.Prepare)();
        reader ??= new BarcodeReader { Formats = BarcodeFormat.All, TryHarder = true, TryRotate = true,
            TryInvert = true, TryDownscale = true, ReturnErrors = false, MaxNumberOfSymbols = CandidateLimit };
    }
    public string[] Decode(byte[] pixels, int length, int width, int height)
    {
        if (reader is null || width <= 0 || height <= 0 || length != checked(width * height) || length > pixels.Length)
            throw new InvalidDataException("Decoder not prepared or incomplete Mono8 image");
        byte lo = 255, hi = 0;
        for (var i = 0; i < length; i++) { lo = Math.Min(lo, pixels[i]); hi = Math.Max(hi, pixels[i]); }
        // Deterministic Mono8 contrast expansion, matching diagnostic autocontrast.
        if (hi > lo && (lo != 0 || hi != 255))
            for (var i = 0; i < length; i++) pixels[i] = (byte)((pixels[i] - lo) * 255 / (hi - lo));
        var pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        Barcode[]? results = null;
        try
        {
            results = reader.From(new ImageView(pin.AddrOfPinnedObject(), width, height, ImageFormat.Lum));
            if (results.Length >= CandidateLimit) throw new InvalidDataException("Recognition candidate limit reached; complete result cannot be guaranteed");
            return results.Select(result => {
                if (!result.IsValid || result.Bytes.Length > CodeByteLimit) throw new InvalidDataException("Invalid/oversized recognition result");
                return encoding.GetString(result.Bytes);
            }).ToArray();
        }
        finally { if (results is not null) foreach (var result in results) result.Dispose(); pin.Free(); }
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

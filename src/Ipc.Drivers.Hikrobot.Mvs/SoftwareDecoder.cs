using System.Runtime.InteropServices;
using System.Text;
using System.Numerics;
using System.Buffers;
using Ipc.Platform;
using ZXingCpp;

namespace Ipc.Drivers.Hikrobot.Mvs;

// The same 3.1.1 engine used in the real-camera diagnostic, directly in .NET.
// No image files, subprocess, Python runtime, barcode deduplication or RCS work.
internal sealed class SoftwareDecoder(string charset, Action? prepareNative = null, Action<byte[], int>? contrastForVerification = null,
    Action<BarcodeReader>? searchOptionsForVerification = null, ReaderSettings? settings = null) : IMonoBarcodeDecoder
{
    const int CandidateLimit = 64, CodeByteLimit = 4096;
    BarcodeReader? reader, thresholdReader;
    readonly MvsTimings timings = new();
    public MvsTimingSnapshot TimingSnapshot => timings.Snapshot();
    public bool PreservesInput => true;
    readonly Encoding encoding = charset switch {
        "utf-8" => new UTF8Encoding(false, true),
        "ascii" => Encoding.GetEncoding("us-ascii", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback),
        "gb18030" => StrictChineseEncoding(),
        _ => throw new InvalidDataException("Unsupported explicit barcode encoding") };
    static Encoding StrictChineseEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding("gb18030", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    }
    public void Prepare()
    {
        timings.Reset();
        (prepareNative ?? DecoderRuntime.Prepare)();
        reader ??= new BarcodeReader { Formats = BarcodeFormat.All, TryHarder = true, TryRotate = true,
            TryInvert = true, TryDownscale = true, ReturnErrors = false, MaxNumberOfSymbols = CandidateLimit };
        if (settings is not null) { reader.Formats = settings.NativeFormats(); reader.TryInvert = settings.TryInvert; }
        searchOptionsForVerification?.Invoke(reader);
        thresholdReader ??= new BarcodeReader();
        // A second binarizer recovers narrow/uneven reflective labels which the
        // local average can erase. Keep the same search, checksum and line guards.
        thresholdReader.Formats = reader.Formats;
        thresholdReader.TryHarder = reader.TryHarder; thresholdReader.TryRotate = reader.TryRotate;
        thresholdReader.TryInvert = reader.TryInvert; thresholdReader.TryDownscale = reader.TryDownscale;
        thresholdReader.ReturnErrors = reader.ReturnErrors; thresholdReader.MaxNumberOfSymbols = CandidateLimit;
        thresholdReader.MinLineCount = Math.Max(2, reader.MinLineCount);
        thresholdReader.ValidateOptionalChecksum = reader.ValidateOptionalChecksum;
        thresholdReader.Binarizer = Binarizer.FixedThreshold;
    }
    public string[] Decode(byte[] pixels, int length, int width, int height)
        => DecodeDetailed(pixels, length, width, height).Select(symbol => symbol.Text).ToArray();
    public RecognitionSymbol[] DecodeDetailed(byte[] pixels, int length, int width, int height)
    {
        if (reader is null || width <= 0 || height <= 0 || length != checked(width * height) || length > pixels.Length)
            throw new InvalidDataException("Decoder not prepared or incomplete Mono8 image");
        using var total = timings.Measure("decode.total");
        byte[]? contrast = null;
        var pin = default(GCHandle);
        Barcode[]? results = null;
        try
        {
            using (timings.Measure("contrast"))
            {
                if (contrastForVerification is not null)
                {
                    contrast = ArrayPool<byte>.Shared.Rent(length); pixels.AsSpan(0, length).CopyTo(contrast);
                    contrastForVerification(contrast, length);
                }
                else
                {
                    var (lo, hi) = Range(pixels.AsSpan(0, length));
                    if (hi <= lo) return [];
                    if (lo != 0 || hi != 255)
                    {
                        contrast = ArrayPool<byte>.Shared.Rent(length); pixels.AsSpan(0, length).CopyTo(contrast);
                        ApplyContrast(contrast.AsSpan(0, length), lo, hi);
                    }
                }
            }
            pin = GCHandle.Alloc(contrast ?? pixels, GCHandleType.Pinned);
            using (timings.Measure("zxing.native"))
                results = reader.From(new ImageView(pin.AddrOfPinnedObject(), width, height, ImageFormat.Lum));
            if (results.Length == 0)
            {
                using (timings.Measure("zxing.threshold-fallback"))
                    results = thresholdReader!.From(new ImageView(pin.AddrOfPinnedObject(), width, height, ImageFormat.Lum));
            }
            using var extraction = timings.Measure("candidates.extract");
            if (results.Length >= CandidateLimit) throw new MvsFrameRejection("CandidateLimit", "Recognition candidate limit reached; frame rejected without partial results");
            return results.Select(result => {
                if (!result.IsValid || result.Bytes.Length > CodeByteLimit) throw new MvsFrameRejection("InvalidCandidate", "Invalid/oversized recognition result; complete frame rejected");
                var p = result.Position;
                return new RecognitionSymbol(encoding.GetString(result.Bytes), result.Format.ToString(),
                    [new(p.TopLeft.X, p.TopLeft.Y), new(p.TopRight.X, p.TopRight.Y),
                     new(p.BottomRight.X, p.BottomRight.Y), new(p.BottomLeft.X, p.BottomLeft.Y)], result.Orientation);
            }).ToArray();
        }
        catch (DecoderFallbackException error) { throw new MvsFrameRejection("InvalidEncoding", "Frame contains bytes invalid for the configured barcode encoding", error); }
        finally
        {
            try { if (results is not null) foreach (var result in results) result.Dispose(); }
            finally { if (pin.IsAllocated) pin.Free(); if (contrast is not null) ArrayPool<byte>.Shared.Return(contrast, true); }
        }
    }
    internal static bool ExpandContrast(Span<byte> pixels)
    {
        var (lo, hi) = Range(pixels);
        if (hi <= lo) return false;
        if (lo != 0 || hi != 255) ApplyContrast(pixels, lo, hi);
        return true;
    }
    static (byte Lo, byte Hi) Range(ReadOnlySpan<byte> pixels)
    {
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
        return (lo, hi);
    }
    static void ApplyContrast(Span<byte> pixels, byte lo, byte hi)
    {
        Span<byte> levels = stackalloc byte[256];
        for (var value = (int)lo; value <= hi; value++) levels[value] = (byte)((value - lo) * 255 / (hi - lo));
        for (var index = 0; index < pixels.Length; index++) pixels[index] = levels[pixels[index]];
    }
    public void Release()
    {
        try { reader?.Dispose(); }
        finally { reader = null; thresholdReader?.Dispose(); thresholdReader = null; }
    }
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

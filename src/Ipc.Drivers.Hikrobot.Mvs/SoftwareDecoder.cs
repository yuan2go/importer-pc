using System.Runtime.InteropServices;
using System.Text;
using System.Numerics;
using System.Buffers;
using Ipc.Platform;
using ZXingCpp;

namespace Ipc.Drivers.Hikrobot.Mvs;

// The same 3.1.1 engine used in the real-camera diagnostic, directly in .NET.
// No image files, subprocess, Python runtime, text/entity deduplication or RCS work.
internal sealed partial class SoftwareDecoder(string charset, Action? prepareNative = null, Action<byte[], int>? contrastForVerification = null,
    Action<BarcodeReader>? searchOptionsForVerification = null, ReaderSettings? settings = null) : IMonoBarcodeDecoder
{
    const int CandidateLimit = 64, CodeByteLimit = 4096;
    int patternSamples;
    BarcodeReader? reader, thresholdReader;
    readonly MvsTimings timings = new();
    public MvsTimingSnapshot TimingSnapshot => timings.Snapshot();
    internal object DiagnosticConfiguration => new { Encoding = charset, Formats = settings?.Formats ?? ["All"], TryInvert = settings?.TryInvert ?? true };
    public bool PreservesInput => true;
    readonly Encoding encoding = charset switch
    {
        "utf-8" => new UTF8Encoding(false, true),
        "ascii" => Encoding.GetEncoding("us-ascii", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback),
        "gb18030" => StrictChineseEncoding(),
        _ => throw new InvalidDataException("Unsupported explicit barcode encoding")
    };
    static Encoding StrictChineseEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding("gb18030", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    }
    public void Prepare()
    {
        timings.Reset();
        (prepareNative ?? DecoderRuntime.Prepare)();
        reader ??= new BarcodeReader
        {
            Formats = BarcodeFormat.All,
            TryHarder = true,
            TryRotate = true,
            TryInvert = true,
            TryDownscale = true,
            ReturnErrors = false,
            MinLineCount = 2,
            MaxNumberOfSymbols = CandidateLimit
        };
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
        patternSamples = 0;
        byte[]? contrast = null;
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
            var input = contrast ?? pixels;
            List<RecognitionSymbol> symbols = [];
            using (timings.Measure("zxing.native"))
                ReadPass(reader, input, width, height, symbols, input, width, height);
            // A readable secondary code must not suppress a harder target code.
            using (timings.Measure("zxing.threshold"))
                ReadPass(thresholdReader!, input, width, height, symbols, input, width, height);
            using (timings.Measure("zxing.regions"))
                SearchRegions(input, width, height, symbols);
            return symbols.ToArray();
        }
        catch (DecoderFallbackException error) { throw new MvsFrameRejection("InvalidEncoding", "Frame contains bytes invalid for the configured barcode encoding", error); }
        finally
        {
            if (contrast is not null) ArrayPool<byte>.Shared.Return(contrast, true);
        }
    }
    void ReadPass(BarcodeReader engine, byte[] pixels, int width, int height, List<RecognitionSymbol> collected,
        byte[] original, int originalWidth, int originalHeight, Func<int, int, ImagePoint>? map = null)
    {
        var pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        Barcode[]? found = null;
        ImageView? view = null;
        try
        {
            // This wrapper passes raw native handles. A temporary ImageView can
            // be finalized during the unmanaged read under GC pressure.
            view = new ImageView(pin.AddrOfPinnedObject(), width, height, ImageFormat.Lum);
            found = engine.From(view);
            if (found.Length >= CandidateLimit) throw new MvsFrameRejection("CandidateLimit", "Recognition candidate limit reached; complete frame rejected");
            foreach (var code in found)
            {
                if (!code.IsValid || code.Bytes.Length > CodeByteLimit) throw new MvsFrameRejection("InvalidCandidate", "Invalid/oversized recognition candidate; complete frame rejected");
                if (RequiresSourceSupport(code.Format.ToString()) && code.LineCount < 4) continue;
                var p = code.Position;
                ImagePoint Point(int x, int y) => map?.Invoke(x, y) ?? new(x, y);
                ImagePoint[] corners = [Point(p.TopLeft.X, p.TopLeft.Y), Point(p.TopRight.X, p.TopRight.Y), Point(p.BottomRight.X, p.BottomRight.Y), Point(p.BottomLeft.X, p.BottomLeft.Y)];
                if (map is not null)
                {
                    // Deskewed scan-band boxes can extend past an image edge even
                    // when the complete code baseline is present. Clip annotation
                    // corners only; both baseline ends must still be original pixels.
                    bool Inside(double x, double y) => x >= -.5 && x <= originalWidth - .5 && y >= -.5 && y <= originalHeight - .5;
                    if (!Inside((corners[0].X + corners[3].X) / 2d, (corners[0].Y + corners[3].Y) / 2d) ||
                        !Inside((corners[1].X + corners[2].X) / 2d, (corners[1].Y + corners[2].Y) / 2d))
                        throw new MvsFrameRejection("InvalidGeometry", "Candidate baseline outside original pixels; complete frame rejected: " + code.Format + "; " + string.Join("/", corners.Select(c => c.X + "," + c.Y)) + "; source=" + originalWidth + "x" + originalHeight);
                    corners = corners.Select(c => new ImagePoint(Math.Clamp(c.X, 0, originalWidth - 1), Math.Clamp(c.Y, 0, originalHeight - 1))).ToArray();
                }
                if (corners.Any(c => c.X < 0 || c.X >= originalWidth || c.Y < 0 || c.Y >= originalHeight))
                    throw new MvsFrameRejection("InvalidGeometry", "Candidate is outside original frame; complete frame rejected");
                var orientation = map is null ? code.Orientation : (int)Math.Round(Math.Atan2(corners[1].Y - corners[0].Y, corners[1].X - corners[0].X) * 180 / Math.PI);
                if (!HasOriginalLineSupport(code.Format.ToString(), corners, original, originalWidth, originalHeight)) continue;
                foreach (var symbol in PrintedBands(new(encoding.GetString(code.Bytes), code.Format.ToString(), corners, orientation), original, originalWidth, originalHeight))
                    MergeCandidate(collected, symbol, original, originalWidth, originalHeight);
            }
        }
        finally { GC.KeepAlive(view); GC.KeepAlive(engine); try { if (found is not null) foreach (var code in found) code.Dispose(); } finally { pin.Free(); } }
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

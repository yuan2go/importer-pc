using System.Runtime.InteropServices;
using System.Text;
using Ipc.Platform;

namespace Ipc.Drivers.Hikrobot.Mvs;

// MVIDCodeReader 1.1.4.0 x64 ABI, verified against the deployed managed wrapper.
// This is not the MV_CODEREADER ABI used by MvCodeReaderCtrl.
internal static class MvidNative
{
    internal const string Library = "MVIDCodeReader.dll";
    internal const string Version = "1.1.4.0_3.6.0_2.2.2";
    internal const int NoData = unchecked((int)0x80000006);
    internal static bool IsRecognitionFailure(int status) => status is unchecked((int)0x80002200) or unchecked((int)0x80002300);
    internal const int DeviceBytes = 748, ImageBytes = 160, CodeBytes = 4268, OutputBytes = 1093048;
    [DllImport(Library, ExactSpelling = true)] internal static extern int MVID_CR_GetVersion(nint version);
    [DllImport(Library, ExactSpelling = true)] internal static extern int MVID_CR_CreateHandle(ref nint handle, uint ability);
    [DllImport(Library, ExactSpelling = true)] internal static extern int MVID_CR_DestroyHandle(nint handle);
    [DllImport(Library, ExactSpelling = true)] internal static extern int MVID_CR_CAM_EnumDevices(nint devices);
    [DllImport(Library, ExactSpelling = true)] internal static extern int MVID_CR_CAM_BindDevice(nint handle, nint device);
    [DllImport(Library, ExactSpelling = true)] internal static extern int MVID_CR_CAM_StartGrabbing(nint handle);
    [DllImport(Library, ExactSpelling = true)] internal static extern int MVID_CR_CAM_StopGrabbing(nint handle);
    [DllImport(Library, ExactSpelling = true)] internal static extern int MVID_CR_CAM_GetOneFrameTimeout(nint handle, nint output, uint milliseconds);
    [DllImport(Library, ExactSpelling = true, CharSet = CharSet.Ansi)] internal static extern int MVID_CR_CAM_GetIntValue(nint handle, string key, nint value);
    [DllImport(Library, ExactSpelling = true, CharSet = CharSet.Ansi)] internal static extern int MVID_CR_CAM_GetEnumValue(nint handle, string key, nint value);
    [DllImport(Library, ExactSpelling = true, CharSet = CharSet.Ansi)] internal static extern int MVID_CR_CAM_SetEnumValue(nint handle, string key, uint value);
    [DllImport(Library, ExactSpelling = true)] internal static extern int MVID_CR_CAM_SetImageNodeNum(nint handle, uint count);
    [DllImport(Library, ExactSpelling = true)] internal static extern int MVID_CR_CAM_SetImageOutPutMode(nint handle, uint mode);
    internal static void Check(int result, string operation)
    { if (result != 0) throw new IOException($"MVID {operation}: 0x{unchecked((uint)result):X8}"); }

    internal static readonly IReadOnlyDictionary<int, string> Formats = new Dictionary<int, string>
    {
        [1] = "DataMatrix",
        [2] = "QRCode",
        [8] = "EAN8",
        [9] = "UPCE",
        [12] = "UPCA",
        [13] = "EAN13",
        [14] = "ISBN",
        [20] = "Codabar",
        [25] = "ITF",
        [39] = "Code39",
        [93] = "Code93",
        [128] = "Code128"
    };
    internal static Encoding EncodingFor(string name) => name switch
    {
        "utf-8" => new UTF8Encoding(false, true),
        "ascii" => Encoding.GetEncoding("us-ascii", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback),
        "gb18030" => ChineseEncoding(),
        _ => throw new InvalidDataException("条码字符编码不受支持。")
    };
    static Encoding ChineseEncoding()
    { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); return Encoding.GetEncoding("gb18030", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback); }

    // Validate and preserve the complete list; never deduplicate equal payloads.
    internal static RecognitionSymbol[] CopyCodes(nint output, int width, int height, Encoding encoding)
    {
        var count = Marshal.ReadInt32(output, ImageBytes);
        if (count is < 0 or >= 256) throw new MvsFrameRejection("CandidateLimit", "MVID result list is invalid or saturated");
        List<RecognitionSymbol> result = [];
        for (var i = 0; i < count; i++)
        {
            var code = output + ImageBytes + 4 + i * CodeBytes;
            var length = Marshal.ReadInt32(code, 4096);
            if (length is <= 0 or > 4096) throw new MvsFrameRejection("InvalidCandidate", "MVID barcode length is invalid");
            var raw = new byte[length]; Marshal.Copy(code, raw, 0, length);
            if (raw[^1] == 0) length--;
            string text;
            try { text = encoding.GetString(raw, 0, length); }
            catch (DecoderFallbackException error) { throw new MvsFrameRejection("InvalidEncoding", "MVID barcode encoding is invalid", error); }
            if (text.Length == 0 || text.Contains('\0')) throw new MvsFrameRejection("InvalidCandidate", "MVID barcode is empty or contains a NUL");
            if (!Formats.TryGetValue(Marshal.ReadInt32(code, 4100), out var format))
                throw new MvsFrameRejection("UnknownFormat", "MVID returned an undeclared symbology");
            var corners = Enumerable.Range(0, 4).Select(n => new ImagePoint(Marshal.ReadInt32(code, 4104 + n * 8), Marshal.ReadInt32(code, 4108 + n * 8))).ToArray();
            if (corners.Any(p => p.X < 0 || p.X >= width || p.Y < 0 || p.Y >= height))
                throw new MvsFrameRejection("InvalidGeometry", "MVID barcode corners are outside the original image");
            var flag = Marshal.ReadInt32(code, 4140);
            if (flag is not (0 or 1)) throw new MvsFrameRejection("InvalidCandidate", "MVID filter flag is invalid");
            // Include SDK-filtered candidates too: Application owns filtering and placement suppression.
            result.Add(new(text, format, corners, (int)Math.Round(Marshal.ReadInt32(code, 4136) / 10d)));
        }
        return result.ToArray();
    }
}

using Ipc.Platform;
using ZXingCpp;

namespace Ipc.Drivers.Hikrobot.Mvs;

internal sealed record ReaderSettings(string[] Formats, bool TryInvert, string Engine = "zxing-cpp")
{
    internal static ReaderSettings Read(IReadOnlyDictionary<string, string> values)
    {
        var engine = values.GetValueOrDefault("reader.engine", "zxing-cpp");
        if (engine is not ("zxing-cpp" or "mvid-native")) throw new InvalidDataException("识码方式无效，请重新选择。");
        if (engine == "mvid-native") return new(["All"], false, engine);
        var formats = Wire.Decode<string[]>(values.GetValueOrDefault("code.formats", "[\"All\"]"));
        var allowed = BundledMvs.ReaderDescriptor.Parameters.Single(p => p.Key == "code.formats").Choices!;
        if (formats is not { Length: > 0 and <= 64 } || formats.Any(f => !allowed.Contains(f, StringComparer.Ordinal)) ||
            formats.Distinct(StringComparer.Ordinal).Count() != formats.Length || formats.Contains("All") && formats.Length != 1)
            throw new InvalidDataException("code.formats: 请至少选择一种已声明码制；全部码制不能与具体码制同时勾选。");
        var invert = values.GetValueOrDefault("code.try-invert", "true");
        if (invert is not ("true" or "false")) throw new InvalidDataException("code.try-invert: 请选择启用或关闭反色识别。");
        return new(formats, invert == "true", engine);
    }
    // Parsing native format names runs only after DecoderRuntime.Prepare, never during config validation.
    internal BarcodeFormats NativeFormats() => new(Formats.Select(name =>
        typeof(BarcodeFormat).GetField(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)?.GetValue(null) is BarcodeFormat format
            ? format : throw new InvalidDataException("code.formats: 当前识别组件不支持所选码制：" + name)));
}

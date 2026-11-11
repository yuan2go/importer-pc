using System.Reflection;
using System.Runtime.InteropServices;
using Ipc.Platform;

namespace Ipc.Drivers.Hikrobot.Mvs;

// One real implementation for independently signed use and the current built-in SM composition.
public static class BundledMvs
{
    public const string Implementation = "hikrobot-mvs";
    public const string Contract = "HIKROBOT-MVS-ACQUISITION-v1";
    static T Resource<T>(string name)
    {
        using var stream = typeof(BundledMvs).Assembly.GetManifestResourceStream(name) ?? throw new InvalidDataException("MVS resource missing: " + name);
        using var reader = new StreamReader(stream);
        return Wire.Decode<T>(reader.ReadToEnd());
    }
    public static CapabilityDescriptor Descriptor { get; } = new("observation", Implementation, "1.0.0", Contract, Resource<ParameterField[]>("Mvs.Parameters.json"), new());
    public const string ReaderImplementation = "hikrobot-mvs-reader";
    public const string ReaderContract = "HIKROBOT-MVS-READRESULT-v1";
    public static CapabilityDescriptor ReaderDescriptor { get; } = new("observation", ReaderImplementation, "1.0.0", ReaderContract, Resource<ParameterField[]>("Mvs.ReaderParameters.json"), new());
    sealed record RuntimeInventory(int SchemaVersion, string Abi, string Entry, string[] Preload, ExtensionFile[] Files);
    static readonly Lazy<nint> runtime = new(() =>
    {
        var manifest = Resource<RuntimeInventory>("Mvs.Runtime.json");
        if (manifest.SchemaVersion != 1 || manifest.Abi != "0x04080201" || manifest.Entry != MvsNative.Library) throw new InvalidDataException("Unsupported bundled MVS ABI");
        return NativeSdkBoundary.LoadBundledFiles(typeof(BundledMvs).Assembly, "vendor/hikrobot-mvs", manifest.Files, manifest.Entry, manifest.Preload);
    }, LazyThreadSafetyMode.ExecutionAndPublication);
    static readonly object resolverGate = new();
    static bool resolverSet;
    public static IControlledObservationSource Create(DeviceBinding binding) => CreateCore(binding, false);
    public static IControlledObservationSource CreateReader(DeviceBinding binding) => CreateCore(binding, true);
#if !MVS_ACQUISITION_EXTENSION
    public static void ValidateReaderParameters(IReadOnlyDictionary<string, string> parameters) => _ = ReaderSettings.Read(parameters);
#endif
    static IControlledObservationSource CreateCore(DeviceBinding binding, bool recognize)
    {
        EnsureNativeResolver();
#if MVS_ACQUISITION_EXTENSION
        // The existing independent module exports acquisition only. Its frozen
        // extension closure has no software-reader contract or decoder dependency.
        if (recognize) throw new NotSupportedException("Software reader belongs to the built-in composition, not the acquisition-only extension");
        IMonoBarcodeDecoder? decoder = null;
#else
        var settings = recognize ? ReaderSettings.Read(binding.Parameters ?? new Dictionary<string, string>()) : null;
        var charset = binding.Parameters?.GetValueOrDefault("code.encoding", "utf-8") ?? "utf-8";
        if (settings?.Engine == "mvid-native")
            return new MvsAcquisition(new(binding.DeviceId, binding.DeviceId, binding.Parameters!), new MvidApi(charset));
        IMonoBarcodeDecoder? decoder = recognize ? new SoftwareDecoder(charset, settings: settings) : null;
#endif
        // Construction never loads native code. Actual loading remains on the source lifecycle worker.
        return new MvsAcquisition(new(binding.DeviceId, binding.DeviceId, binding.Parameters ?? throw new InvalidDataException("MVS parameters required")), new MvsApi(), decoder: decoder);
    }
    static void EnsureNativeResolver()
    {
        lock (resolverGate)
        {
            if (!resolverSet)
            {
                NativeLibrary.SetDllImportResolver(typeof(BundledMvs).Assembly, (name, _, _) =>
                    name == MvsNative.Library ? runtime.Value :
#if !MVS_ACQUISITION_EXTENSION
                    name == MvidNative.Library ? MvidRuntime.Load() :
#endif
                    0);
                resolverSet = true;
            }
        }
    }
#if !MVS_ACQUISITION_EXTENSION
    public static string ReadCameraSerial(string ip, string readerEngine)
    {
        if (!System.Net.IPAddress.TryParse(ip, out var address) || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork ||
            address.ToString() != ip || ip is "0.0.0.0" or "255.255.255.255") throw new InvalidDataException("请填写具体相机的完整 IPv4 地址。");
        if (readerEngine is not ("mvid-native" or "zxing-cpp")) throw new InvalidDataException("识码方式无效，请重新选择。");
        EnsureNativeResolver();
        return ReadCameraSerial(ip, readerEngine == "mvid-native" ? new MvidApi("utf-8") : new MvsApi());
    }
    internal static string ReadCameraSerial(string ip, IMvsApi api)
    {
        try
        {
            api.Initialize();
            var matches = api.Enumerate().Where(d => d.Ip == ip).ToArray();
            if (matches.Length == 0) throw new InvalidDataException("未找到此 IP 的相机，请检查地址、网络和相机供电后重新保存。");
            if (matches.Length != 1) throw new InvalidDataException("此 IP 对应多台相机，请先处理地址冲突后重新保存。");
            var serial = matches[0].Serial;
            if (string.IsNullOrWhiteSpace(serial) || serial.Length > 32 || serial.Any(char.IsControl))
                throw new InvalidDataException("相机未返回有效序列号，请检查设备后重新保存。");
            return serial;
        }
        finally { api.Shutdown(); }
    }
#endif
}

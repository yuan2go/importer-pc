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
    sealed record RuntimeInventory(int SchemaVersion, string Abi, string Entry, string[] Preload, ExtensionFile[] Files);
    static readonly Lazy<nint> runtime = new(() => {
        var manifest = Resource<RuntimeInventory>("Mvs.Runtime.json");
        if (manifest.SchemaVersion != 1 || manifest.Abi != "0x04080201" || manifest.Entry != MvsNative.Library) throw new InvalidDataException("Unsupported bundled MVS ABI");
        return NativeSdkBoundary.LoadBundledFiles(typeof(BundledMvs).Assembly, "vendor/hikrobot-mvs", manifest.Files, manifest.Entry, manifest.Preload);
    }, LazyThreadSafetyMode.ExecutionAndPublication);
    static readonly object resolverGate = new();
    static bool resolverSet;
    public static IControlledObservationSource Create(DeviceBinding binding)
        => CreateWithDecoder(new(binding.DeviceId, binding.DeviceId, binding.Parameters ?? throw new InvalidDataException("MVS parameters required")), null);
    internal static IControlledObservationSource CreateWithDecoder(ExtensionContext context, Func<IFrameDecoder>? decoderFactory)
    {
        lock (resolverGate)
        {
            if (!resolverSet)
            {
                NativeLibrary.SetDllImportResolver(typeof(BundledMvs).Assembly, (name, _, _) =>
                    name == MvsNative.Library ? runtime.Value : 0);
                resolverSet = true;
            }
        }
        // Construction never loads native code. Actual loading remains on the source lifecycle worker.
        return new MvsAcquisition(context, new MvsApi(), decoderFactory: decoderFactory);
    }
}

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
    static readonly Lazy<nint> runtime = new(() => {
        var manifest = Resource<RuntimeInventory>("Mvs.Runtime.json");
        if (manifest.SchemaVersion != 1 || manifest.Abi != "0x04080201" || manifest.Entry != MvsNative.Library) throw new InvalidDataException("Unsupported bundled MVS ABI");
        return NativeSdkBoundary.LoadBundledFiles(typeof(BundledMvs).Assembly, "vendor/hikrobot-mvs", manifest.Files, manifest.Entry, manifest.Preload);
    }, LazyThreadSafetyMode.ExecutionAndPublication);
    static readonly object resolverGate = new();
    static bool resolverSet;
    public static IControlledObservationSource Create(DeviceBinding binding) => CreateCore(binding, false);
    public static IControlledObservationSource CreateReader(DeviceBinding binding) => CreateCore(binding, true);
    static IControlledObservationSource CreateCore(DeviceBinding binding, bool recognize)
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
#if MVS_ACQUISITION_EXTENSION
        // The existing independent module exports acquisition only. Its frozen
        // extension closure has no software-reader contract or decoder dependency.
        if (recognize) throw new NotSupportedException("Software reader belongs to the built-in composition, not the acquisition-only extension");
        IMonoBarcodeDecoder? decoder = null;
#else
        IMonoBarcodeDecoder? decoder = recognize ? new SoftwareDecoder(binding.Parameters?.GetValueOrDefault("code.encoding", "utf-8") ?? "utf-8") : null;
#endif
        // Construction never loads native code. Actual loading remains on the source lifecycle worker.
        return new MvsAcquisition(new(binding.DeviceId, binding.DeviceId, binding.Parameters ?? throw new InvalidDataException("MVS parameters required")), new MvsApi(), decoder: decoder);
    }
}

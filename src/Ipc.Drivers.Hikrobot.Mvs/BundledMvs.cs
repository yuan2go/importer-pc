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
    static void EnsureResolver()
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
    }
    public static IControlledObservationSource Create(DeviceBinding binding)
    {
        EnsureResolver();
        // Construction never loads native code. Actual loading remains on the source lifecycle worker.
        return new MvsAcquisition(new(binding.DeviceId, binding.DeviceId, binding.Parameters ?? throw new InvalidDataException("MVS parameters required")), new MvsApi());
    }
    public static MvsCaptureSession CreateCaptureSession(DeviceBinding binding, IMvsFrameSink sink,
        Func<string, IDisposable> acquireOwnership)
    {
        ArgumentNullException.ThrowIfNull(sink); ArgumentNullException.ThrowIfNull(acquireOwnership);
        var values = binding.Parameters ?? throw new InvalidDataException("MVS parameters required");
        var serial = values.GetValueOrDefault("device.serial", "");
        if (serial.Length is < 1 or > 32 || serial.Any(c => c is < '!' or > '~') ||
            values.GetValueOrDefault("acquisition.trigger", "") != "on" ||
            !values.ContainsKey("acquisition.trigger-source") || !values.ContainsKey("acquisition.trigger-selector"))
            throw new InvalidDataException("Service capture requires explicit physical identity and trigger checks");
        EnsureResolver();
        // The service composition supplies the agreed ownership policy. This
        // factory never silently disables or invents a second physical lease.
        return new(new(binding.DeviceId, binding.DeviceId, values), new MvsApi(), acquireOwnership, frameSink: sink);
    }
}

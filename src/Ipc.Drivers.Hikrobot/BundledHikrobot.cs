using System.Runtime.InteropServices;
using Ipc.Platform;

namespace Ipc.Drivers.Hikrobot;

// Built-in and independently signed delivery use the SAME reader implementation.
public static class BundledHikrobot
{
    public const string Implementation = "hikrobot-idmvs";
    public const string Contract = "HIKROBOT-IDMVS-OBSERVATION-v1";
    static T Resource<T>(string name)
    {
        using var stream = typeof(BundledHikrobot).Assembly.GetManifestResourceStream(name) ?? throw new InvalidDataException("MvCodeReader resource missing: " + name);
        using var reader = new StreamReader(stream);
        return Wire.Decode<T>(reader.ReadToEnd());
    }
    public static CapabilityDescriptor Descriptor { get; } = new("observation", Implementation, "1.0.0", Contract,
        Resource<ParameterField[]>("Hikrobot.Parameters.json").Select(p => p.Key switch {
            "entity.mode" => p with { Default = "trigger" },
            "acquisition.mode" => p with { Default = "external-trigger" },
            _ => p }).ToArray(), new());
    sealed record RuntimeInventory(int SchemaVersion, string Abi, string Entry, string[] Preload, ExtensionFile[] Files);
    static readonly Lazy<nint> runtime = new(() => {
        var manifest = Resource<RuntimeInventory>("Hikrobot.Runtime.json");
        if (manifest.SchemaVersion != 1 || manifest.Abi != "0x02000004" || manifest.Entry != NativeMethods.Library) throw new InvalidDataException("Unsupported bundled MvCodeReader ABI");
        return NativeSdkBoundary.LoadBundledFiles(typeof(BundledHikrobot).Assembly, "vendor/hikrobot-idmvs", manifest.Files, manifest.Entry, manifest.Preload);
    }, LazyThreadSafetyMode.ExecutionAndPublication);
    static readonly object resolverGate = new();
    static bool resolverSet;
    public static void ValidateBinding(DeviceBinding binding)
    {
        var values = binding.Parameters ?? [];
        var errors = ExtensionSchema.Validate(Descriptor.Parameters, values);
        if (errors.Length != 0) throw new InvalidDataException(string.Join("; ", errors));
        if (values.GetValueOrDefault("entity.mode", "observe-only") == "trigger" && values.GetValueOrDefault("acquisition.mode", "existing") == "continuous")
            throw new InvalidDataException("acquisition.mode: 连续采集仅用于观察诊断；一包一次作业请选择外部触发");
    }
    public static IControlledObservationSource Create(DeviceBinding binding)
    {
        ValidateBinding(binding);
        lock (resolverGate)
        {
            if (!resolverSet)
            {
                NativeLibrary.SetDllImportResolver(typeof(BundledHikrobot).Assembly, (name, _, _) => name == NativeMethods.Library ? runtime.Value : 0);
                resolverSet = true;
            }
        }
        return new HikrobotReader(new(binding.DeviceId, binding.DeviceId, binding.Parameters ?? throw new InvalidDataException("MvCodeReader parameters required")), new HikrobotApi());
    }
}

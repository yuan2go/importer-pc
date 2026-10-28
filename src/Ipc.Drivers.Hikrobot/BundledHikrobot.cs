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
            "entity.mode" => p with { Default = "read-result" },
            "acquisition.mode" => p with { Default = "continuous" },
            _ => p }).ToArray(), new());
    sealed record RuntimeInventory(int SchemaVersion, string Abi, string Entry, string[] Preload, ExtensionFile[] Files, ExtensionFile? LoggingSource = null, string? LoggingPolicy = null);
    static string? logRoot;
    static readonly Lazy<nint> runtime = new(() => {
        var manifest = Resource<RuntimeInventory>("Hikrobot.Runtime.json");
        if (manifest.SchemaVersion != 1 || manifest.Abi != "0x02000004" || manifest.Entry != NativeMethods.Library) throw new InvalidDataException("Unsupported bundled MvCodeReader ABI");
        if (logRoot is null || Environment.GetEnvironmentVariable("IPC_MVCODEREADER_LOG_ROOT") != logRoot) throw new InvalidDataException("MvCodeReader process log root changed; controlled restart required");
        NativeSdkBoundary.PinBundledWorkingConfiguration(typeof(BundledHikrobot).Assembly, manifest.Files.Where(f => !f.Path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)).ToArray());
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
            throw new InvalidDataException("acquisition.mode: 连续采集请选择逐帧读码上报或仅观察，不可关联硬件触发");
    }
    internal static ExtensionFile LoggingProfile => Resource<RuntimeInventory>("Hikrobot.Runtime.json").Files.Single(f => f.Path == "log4cxx.properties");
    internal static string ConfigureProcessLogs(string dataRoot)
    {
        lock (resolverGate)
        {
            var selected = Path.GetFullPath(Path.Combine(dataRoot, "diagnostics", "native-hikrobot-idmvs")).Replace('\\', '/');
            if (logRoot is not null && logRoot != selected) throw new InvalidDataException("MvCodeReader already owns another log root; controlled restart required");
            for (var current = new DirectoryInfo(selected); current is not null; current = current.Parent)
                if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Linked SDK log directory refused");
            Directory.CreateDirectory(selected); logRoot = selected;
            Environment.SetEnvironmentVariable("IPC_MVCODEREADER_LOG_ROOT", logRoot, EnvironmentVariableTarget.Process);
            return selected;
        }
    }
    public static IControlledObservationSource Create(DeviceBinding binding, string dataRoot)
    {
        ValidateBinding(binding);
        lock (resolverGate)
        {
            ConfigureProcessLogs(dataRoot);
            if (!resolverSet)
            {
                NativeLibrary.SetDllImportResolver(typeof(BundledHikrobot).Assembly, (name, _, _) => name == NativeMethods.Library ? runtime.Value : 0);
                resolverSet = true;
            }
        }
        return new HikrobotReader(new(binding.DeviceId, binding.DeviceId, binding.Parameters ?? throw new InvalidDataException("MvCodeReader parameters required")), new HikrobotApi());
    }
}

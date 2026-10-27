using Ipc.Adapters;
using Ipc.Platform;
using Ipc.Supply;

namespace Ipc.Product;

public static class ReferenceProduct
{
    // This is the one installed reference business; tests may register other controlled
    // compositions without introducing a second customer implementation.
    public static ProductDefinition Definition { get; } = new("ipc-reference", "net10.0", "supply", [RunMode.Simulation, RunMode.Production],
        [new("platform", "platform", "1.0.0", 1, [], "Ipc.Platform.dll"),
         new("supply-contracts", "contracts", "1.0.0", 1, ["platform"], "Ipc.Supply.Contracts.dll"),
         new("supply", "application", "1.0.0", 1, ["supply-contracts", "platform-storage"], "Ipc.Supply.dll"),
         new("platform-storage", "platform", "1.0.0", 1, ["platform"], "Ipc.Platform.Storage.dll"),
         new("application-contracts", "contracts", "2.0.0", 2, ["platform"], "Ipc.Application.Contracts.dll"),
         new("reference-protocol", "fixture", "1.0.0", 1, ["application-contracts"], "Ipc.Reference.Protocol.dll"),
         new("drivers", "drivers", "1.0.0", 1, ["reference-protocol"], "Ipc.Drivers.dll"),
         new("integrations", "integrations", "1.0.0", 1, ["reference-protocol", "supply-contracts"], "Ipc.Integrations.dll"),
         new("product", "product", "1.0.0", 1, ["supply", "drivers", "integrations"], "Ipc.Product.dll")],
        SupplyViews.Installed.ToArray(), ["standard", "compact"],
        [new("manual", RunMode.Simulation), new("replay", RunMode.Simulation)]);
    static readonly IReadOnlyDictionary<string, Func<DeviceBinding, string, IControlledObservationSource>> sourceFactories =
        new Dictionary<string, Func<DeviceBinding, string, IControlledObservationSource>>(StringComparer.Ordinal)
        {
            ["manual"] = (binding, _) => new ManualObservationSource(binding.DeviceId),
            ["replay"] = (binding, directory) => new FileReplaySource(binding.DeviceId, ResolveReplay(binding.ReplayFile, directory))
        };
    public static IReadOnlyList<string> Validate(ProductManifest manifest, StationConfig station, PresentationSettings presentation, string installRoot, string[]? verifiedExtensionDrivers = null) =>
        ProductValidation.Validate(Definition with { Drivers = Definition.Drivers.Concat((verifiedExtensionDrivers ?? []).Select(x => new DriverRegistration(x, station.Mode))).ToArray() }, manifest, station, presentation, installRoot).Concat(SupplyConfiguration.Validate(station)).Distinct().ToArray();
    public static IControlledObservationSource CreateSource(DeviceBinding binding, string configurationDirectory)
    {
        if (!sourceFactories.TryGetValue(binding.Driver, out var create)) throw new InvalidDataException("Unregistered installed driver");
        return create(binding, configurationDirectory);
    }
    static string ResolveReplay(string? relative, string directory)
    {
        if (relative is null || !ProductValidation.IsSafeRelativeFile(directory, relative, out var path) || !File.Exists(path)) throw new InvalidDataException("Replay path must be an installed relative file without links");
        return path;
    }
}

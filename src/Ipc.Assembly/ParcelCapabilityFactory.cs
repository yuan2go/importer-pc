using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Adapters;
using Ipc.Drivers;
#if !DIRECTPLC
using Ipc.Integrations;
#endif
using Ipc.Platform;
namespace Ipc.Assembly;

public sealed record ParcelExtensionStoreConfiguration(string Root, string TrustFile);
public sealed class ParcelCapabilityFactory(BusinessLaunch launch) : IParcelCapabilityFactory
{
    ExtensionStore? store;
    ExtensionLoading? loading;
    PackageUseLease? use;
    ResourceLease? devices;
    readonly List<IAsyncDisposable> constructed = [];
    bool transferred;
    public static void RegisterContracts() => ExtensionPublicContracts.Register(typeof(IPlcExtension).Assembly);
    ExtensionStore Store()
    {
        if (store is not null) return store;
        var settings = Wire.Decode<ParcelExtensionStoreConfiguration>(File.ReadAllText(Path.Combine(launch.ConfigurationDirectory, "extension-store.json")));
        if (!Path.IsPathFullyQualified(settings.Root) || !Path.IsPathFullyQualified(settings.TrustFile)) throw new InvalidDataException("Explicit independent reference store/trust absolute paths are required");
        var trust = Wire.Decode<ExtensionTrust>(File.ReadAllText(settings.TrustFile));
        return store = new(settings.Root, trust, RunMode.Simulation, Path.Combine(launch.DataRoot, "Simulation", "resource-leases"));
    }
    public ParcelConfiguration Normalize(ParcelConfiguration configuration)
    {
        CapabilityBinding? Resolve(CapabilityBinding? b)
        {
            if (b is null || b.Kind == "weight" || b.Package is not null) return b;
            var parameters = new Dictionary<string, string>(b.Parameters);
            parameters.TryAdd("peer-root", Path.GetFullPath(Path.Combine(launch.DataRoot, "Simulation", "reference-peers")));
            return b with { Parameters = parameters };
        }
        return configuration with { Wcs = Resolve(configuration.Wcs), Plc = Resolve(configuration.Plc) };
    }
    public void Validate(ParcelConfiguration c)
    {
        RegisterContracts();
        if (c.Mode != RunMode.Simulation) throw new InvalidDataException("Production capabilities require approved SDK/protocol/native prerequisite evidence; reference ports cannot satisfy Production");
        var bindings = new[] { c.Scale, c.Wcs, c.Plc }.OfType<CapabilityBinding>().ToArray();
        if (c.Reader.Driver != "manual" && !c.Reader.Driver.StartsWith("extension:", StringComparison.Ordinal)) throw new InvalidDataException("No approved reader implementation: " + c.Reader.Driver);
        if (c.Extensions is not null)
        {
            if (c.Extensions.Packages is null || c.Extensions.Instances is null || c.Extensions.Packages.Any(x => x is null) || c.Extensions.Instances.Any(x => x is null) ||
                c.Extensions.Packages.Length > 64 || c.Extensions.Instances.Length > 64 || c.Extensions.Packages.Select(x => x.Id).Distinct().Count() != c.Extensions.Packages.Length)
                throw new InvalidDataException("Malformed or oversized application extension deployment");
            if (c.Extensions.Services is { Length: > 0 } || c.Extensions.Ports is { Length: > 0 }) throw new InvalidDataException("Parcel capabilities are owned by their typed bindings; duplicate service binding authority is forbidden");
            var readerBindings = c.Reader.Driver == "manual" ? Array.Empty<DeviceBinding>() : [c.Reader];
            // Existing validator checks signatures/closure/reader role. New device kinds have
            // their own typed bindings below, never counterfeit observation DeviceIds.
            var observationInstances = c.Extensions.Instances.Where(x => c.Reader.Driver == "extension:" + x.Id).ToArray();
            var observationDeployment = c.Extensions with { Instances = observationInstances };
            var errors = ExtensionDeployment.Validate(observationDeployment, readerBindings, Store());
            if (errors.Length != 0) throw new InvalidDataException(string.Join("; ", errors));
            if (c.Extensions.Instances.Length != observationInstances.Length) throw new InvalidDataException("Unbound observation extension instance");
            foreach (var package in c.Extensions.Packages)
                if (!bindings.Any(x => x.Package == package) && !observationInstances.Any(x => x.PackageId == package.Id) &&
                    !c.Extensions.Packages.Any(x => Store().VerifyInstalled(x).Manifest.Dependencies.Contains(package)))
                    throw new InvalidDataException("Unbound package in application deployment");
        }
        else if (c.Reader.Driver != "manual" || bindings.Any(x => x.Package is not null)) throw new InvalidDataException("Extension binding requires an exact deployment");
        foreach (var b in bindings)
        {
            if (!Uri.TryCreate(b.Target, UriKind.Absolute, out var target) || target.Scheme != "reference") throw new InvalidDataException("Reference capability target must use explicit reference:// identity");
            if (b.Package is not null)
            {
                if (c.Extensions?.Packages.Contains(b.Package) != true) throw new InvalidDataException("Capability package absent from exact deployment");
                if (b.Version != b.Package.Version || b.Implementation != "extension:" + b.Package.Id + "/" + b.Module) throw new InvalidDataException("Capability implementation/version must name its exact installed package/module");
                var manifest = Store().VerifyInstalled(b.Package).Manifest;
                var module = manifest.Modules.SingleOrDefault(x => x.Id == b.Module && x.Kind == b.Kind) ?? throw new InvalidDataException("Typed capability/module missing");
                var errors = ExtensionSchema.Validate(module.Parameters, b.Parameters);
                if (errors.Length != 0 || manifest.Prerequisites.Length != 0) throw new InvalidDataException("Capability schema/native SDK prerequisite evidence is unavailable: " + string.Join("; ", errors));
                if (bindings.Count(x => x.Package == b.Package && x.Module == b.Module) > module.MaximumInstances) throw new InvalidDataException("Capability instance quota exceeded");
            }
            else
            {
                if (b.Implementation != "reference-" + b.Kind || b.Version != "1.0.0" || b.Contract != "PARCEL-REFERENCE-v1") throw new InvalidDataException("No approved exact capability implementation/contract");
                var allowed = b.Kind == "weight" ? Array.Empty<string>() : b.Kind == "plc" ? ["decision", "reply-mode", "complete-on-query", "peer-root"] : ["decision", "reply-mode", "slot", "peer-root"];
                if (b.Kind != "weight" && b.Parameters.TryGetValue("peer-root", out var peerRoot) && !Path.IsPathFullyQualified(peerRoot)) throw new InvalidDataException("Reference peer location must be an explicit absolute frozen parameter");
                if (b.Parameters.Keys.Any(x => !allowed.Contains(x)) || b.Parameters.GetValueOrDefault("decision", "accept") is not ("accept" or "reject") ||
                    b.Parameters.GetValueOrDefault("reply-mode", "normal") is not ("normal" or "lose-after-commit") ||
                    b.Parameters.GetValueOrDefault("complete-on-query", "false") is not ("true" or "false") ||
                    b.Parameters.TryGetValue("slot", out var slot) && (string.IsNullOrWhiteSpace(slot) || slot.Length > 64 || slot.Any(char.IsControl)))
                    throw new InvalidDataException("Invalid reference protocol parameters");
            }
        }
    }
    public ParcelPorts Create(ParcelConfiguration c)
    {
        Validate(c);
        var resourceRoot = Path.Combine(launch.DataRoot, "Simulation", "resource-leases");
        devices = new(resourceRoot, new[] { "reader:" + c.Reader.DeviceId }.Concat(new[] { c.Scale, c.Plc }.OfType<CapabilityBinding>().Select(x => x.Kind + ":" + x.Target)));
        if (c.Extensions is { Packages.Length: > 0 } deployment)
        {
            Store().RegisterConsumer(Path.Combine(launch.DataRoot, c.Mode.ToString(), c.StationId, c.ApplicationId, c.ApplicationId + ".db"));
            use = Store().AcquireUse(deployment.Packages); loading = new(Store());
        }
        IControlledObservationSource reader;
        if (c.Reader.Driver == "manual") reader = new ManualObservationSource(c.Reader.DeviceId);
        else
        {
            var instance = c.Extensions!.Instances.Single(x => "extension:" + x.Id == c.Reader.Driver);
            var package = c.Extensions.Packages.Single(x => x.Id == instance.PackageId);
            reader = loading!.Create(package, Store().VerifyInstalled(package).Manifest.Modules.Single(x => x.Id == instance.ModuleId), instance);
        }
        constructed.Add(reader);
        if (reader.DeviceId != c.Reader.DeviceId) throw new InvalidDataException("Reader returned the wrong stable identity");
        Dictionary<string, string> hashes = [];
        T Own<T>(CapabilityBinding b, Func<T> builtIn, Func<ExtensionLoading, ExtensionModule, T> external) where T : class, ICapabilityPort
        {
            T value;
            if (b.Package is null) { value = builtIn(); hashes.Add(b.InstanceId, Wire.FileHash(value.GetType().Assembly.Location)); }
            else
            {
                var module = Store().VerifyInstalled(b.Package).Manifest.Modules.Single(x => x.Id == b.Module);
                value = external(loading!, module);
                hashes.Add(b.InstanceId, Wire.FileHash(Path.Combine(Store().PackageDirectory(b.Package), module.Assembly)));
            }
            constructed.Add(value); if (value.InstanceId != b.InstanceId) throw new InvalidDataException("Capability returned a mismatched identity"); return value;
        }
        var weight = c.Scale is { } s ? Own<IWeightSource>(s, () => new ReferenceWeightSource(s), (l, m) => l.CreateFactory<IWeightExtension>(s.Package!, m, "weight").Create(s)) : null;
        #if DIRECTPLC
        IWcsPort? wcs = null;
        #else
        var wcs = c.Wcs is { } w ? Own<IWcsPort>(w, () => new ReferenceWcs(w, w.Parameters["peer-root"]), (l, m) => l.CreateFactory<IWcsExtension>(w.Package!, m, "integration").Create(w)) : null;
        #endif
        var plc = c.Plc is { } p ? Own<IPlcPort>(p, () => new ReferencePlc(p, p.Parameters["peer-root"]), (l, m) => l.CreateFactory<IPlcExtension>(p.Package!, m, "plc").Create(p)) : null;
        transferred = true; return new(reader, weight, wcs, plc, hashes);
    }
    public async ValueTask DisposeAsync()
    {
        if (!transferred)
            foreach (var resource in constructed.AsEnumerable().Reverse()) await resource.DisposeAsync().ConfigureAwait(false);
        loading?.Dispose(); use?.Dispose(); devices?.Dispose();
    }
}

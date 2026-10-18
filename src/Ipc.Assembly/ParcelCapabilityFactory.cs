using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Adapters;
using Ipc.Drivers;
using Ipc.Components;
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
        var settingsFile = Path.Combine(launch.ConfigurationDirectory, "extension-store.json");
        (string Root, string TrustFile) paths;
        if (OperatingSystem.IsWindows())
        {
            if (File.Exists(settingsFile)) throw new InvalidDataException("Windows reference extension store/trust overrides are forbidden; use provisioned machine policy");
            paths = MachineRuntimePolicy.ExtensionPaths(RunMode.Simulation);
        }
        else
        {
            var settings = Wire.Decode<ParcelExtensionStoreConfiguration>(File.ReadAllText(settingsFile));
            if (!Path.IsPathFullyQualified(settings.Root) || !Path.IsPathFullyQualified(settings.TrustFile)) throw new InvalidDataException("Explicit independent reference store/trust absolute paths are required");
            paths = MachineRuntimePolicy.ExtensionPaths(RunMode.Simulation, settings.Root, settings.TrustFile);
        }
        if (new FileInfo(paths.TrustFile).Length > ExtensionPackages.MaximumManifestBytes) throw new InvalidDataException("Trust policy quota exceeded");
        var trust = Wire.Decode<ExtensionTrust>(File.ReadAllText(paths.TrustFile));
        var leases = MachineRuntimePolicy.ResolveLeaseRoot(OperatingSystem.IsWindows() ? null : Path.Combine(launch.DataRoot, "Simulation", "resource-leases"));
        return store = new(paths.Root, trust, RunMode.Simulation, leases);
    }
    public ParcelConfiguration Normalize(ParcelConfiguration configuration)
    {
        CapabilityBinding? Resolve(CapabilityBinding? b)
        {
            if (b is null || b.Kind is "weight" or "login" or "camera" || b.Package is not null || b.Parameters.ContainsKey("endpoint")) return b;
            var parameters = new Dictionary<string, string>(b.Parameters);
            parameters.TryAdd("peer-root", Path.GetFullPath(Path.Combine(launch.DataRoot, "Simulation", "reference-peers")));
            return b with { Parameters = parameters };
        }
        return configuration with { CapabilityInstances = configuration.CapabilityInstances?.Select(x => Resolve(x)!).ToArray(), Wcs = Resolve(configuration.Wcs), Plc = Resolve(configuration.Plc), Images = configuration.Images is { } i ? i with { Uploader = Resolve(i.Uploader)! } : null };
    }
    public void Validate(ParcelConfiguration c)
    {
        RegisterContracts();
        if (c.Mode != RunMode.Simulation) throw new InvalidDataException("Production capabilities require approved SDK/protocol/native prerequisite evidence; reference ports cannot satisfy Production");
        var bound = new[] { c.Scale, c.Wcs, c.Plc, c.Login, c.MaintenanceLogin, c.Images?.Camera, c.Images?.Uploader }.OfType<CapabilityBinding>().ToArray();
        var bindings = bound.Concat(c.CapabilityInstances ?? []).DistinctBy(x => x.InstanceId).ToArray();
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
        if (bound.Any(x => !x.Enabled)) throw new InvalidDataException("A bound provider must be enabled");
        if (c.CapabilityInstances is { } instances)
        {
            if (instances.Length > 64 || instances.Select(x => x.InstanceId).Distinct().Count() != instances.Length) throw new InvalidDataException("Invalid bounded capability instance inventory");
            if (bound.Any(b => !instances.Any(i => Wire.Encode(i) == Wire.Encode(b)))) throw new InvalidDataException("Bound provider differs from the same authoritative instance inventory");
        }
        foreach (var b in bindings.Concat(c.CapabilityInstances ?? []).DistinctBy(x => x.InstanceId))
        {
            if (b.Package is null && (!Uri.TryCreate(b.Target, UriKind.Absolute, out var target) || target.Scheme != "reference")) throw new InvalidDataException("Reference capability target must use explicit reference:// identity");
            if (b.Parameters.TryGetValue("endpoint", out var endpoint) &&
                (b.Kind is not ("integration" or "plc" or "upload") || !Uri.TryCreate(endpoint, UriKind.Absolute, out var peerUri) || peerUri.Scheme != "http" || peerUri.Host != "127.0.0.1" || peerUri.AbsolutePath != "/parcel-reference/" || peerUri.UserInfo.Length != 0 || peerUri.Query.Length != 0 || peerUri.Fragment.Length != 0))
                throw new InvalidDataException("Controlled reference HTTP peer must be literal loopback with an exact path");
            if (b.Package is null && b.Kind is ("login" or "camera" or "upload"))
            {
                if (b.Implementation != "reference-" + b.Kind || b.Version != "1.0.0" || b.Contract != "PARCEL-REFERENCE-v1")
                    throw new InvalidDataException("Optional capability only has an explicit reference implementation; signed real provider binding requires approved policy/contract");
                var optionalAllowed = b.Kind == "login" ? new[] { "subjects", "reply-mode" } : b.Kind == "camera" ? new[] { "decision", "reply-mode" } : new[] { "decision", "reply-mode", "peer-root", "endpoint" };
                if (b.Parameters.Keys.Any(x => !optionalAllowed.Contains(x)) ||
                    b.Kind == "login" && (b.Parameters.GetValueOrDefault("subjects") is not { Length: > 0 and <= 1024 } subjects || subjects.Split(',').Any(x => !ExtensionSchema.IsId(x)) || b.Parameters.GetValueOrDefault("reply-mode", "normal") is not ("normal" or "unavailable")) ||
                    b.Kind != "login" && (b.Parameters.GetValueOrDefault("decision", "accept") is not ("accept" or "reject") || b.Parameters.GetValueOrDefault("reply-mode", "normal") is not ("normal" or "lose-after-commit")) ||
                    b.Kind == "upload" && !b.Parameters.ContainsKey("endpoint") && (!b.Parameters.TryGetValue("peer-root", out var peer) || !Path.IsPathFullyQualified(peer)))
                    throw new InvalidDataException("Invalid bounded reference optional capability parameters");
                continue;
            }
            if (b.Package is not null)
            {
                if (c.Extensions?.Packages.Contains(b.Package) != true) throw new InvalidDataException("Capability package absent from exact deployment");
                if (b.Version != b.Package.Version || b.Implementation != "extension:" + b.Package.Id + "/" + b.Module) throw new InvalidDataException("Capability implementation/version must name its exact installed package/module");
                var manifest = Store().VerifyInstalled(b.Package).Manifest;
                var module = manifest.Modules.SingleOrDefault(x => x.Id == b.Module && x.Kind == b.Kind) ?? throw new InvalidDataException("Typed capability/module missing");
                var errors = ExtensionSchema.Validate(module.Parameters, b.Parameters);
                if (module.ContractIdentity is not null && module.ContractIdentity != b.Contract) throw new InvalidDataException("Selected binding contract differs from signed provider contract");
                if (bound.Any(x => x.InstanceId == b.InstanceId)) ValidateRequirements(c, module.Requirements ?? new());
                if (errors.Length != 0 || manifest.Prerequisites.Length != 0) throw new InvalidDataException("Capability schema/native SDK prerequisite evidence is unavailable: " + string.Join("; ", errors));
                if (bindings.Count(x => x.Package == b.Package && x.Module == b.Module) > module.MaximumInstances) throw new InvalidDataException("Capability instance quota exceeded");
            }
            else
            {
                if (b.Implementation != "reference-" + b.Kind || b.Version != "1.0.0" || b.Contract != "PARCEL-REFERENCE-v1") throw new InvalidDataException("No approved exact capability implementation/contract");
                var allowed = b.Kind == "weight" ? Array.Empty<string>() : b.Kind == "plc" ? ["decision", "reply-mode", "complete-on-query", "peer-root", "endpoint"] : ["decision", "reply-mode", "slot", "peer-root", "endpoint"];
                if (b.Kind != "weight" && b.Parameters.TryGetValue("peer-root", out var peerRoot) && !Path.IsPathFullyQualified(peerRoot)) throw new InvalidDataException("Reference peer location must be an explicit absolute frozen parameter");
                if (b.Parameters.Keys.Any(x => !allowed.Contains(x)) || b.Parameters.GetValueOrDefault("decision", "accept") is not ("accept" or "reject") ||
                    b.Parameters.GetValueOrDefault("reply-mode", "normal") is not ("normal" or "lose-after-commit") ||
                    b.Parameters.GetValueOrDefault("complete-on-query", "false") is not ("true" or "false") ||
                    b.Parameters.TryGetValue("slot", out var slot) && (string.IsNullOrWhiteSpace(slot) || slot.Length > 64 || slot.Any(char.IsControl)))
                    throw new InvalidDataException("Invalid reference protocol parameters");
            }
            if (b.Package is null)
            {
                var descriptor = ReferenceCapabilityCatalog.Providers.Single(x => x.Kind == b.Kind);
                var errors = ExtensionSchema.Validate(descriptor.Parameters, b.Parameters);
                if (errors.Length != 0) throw new InvalidDataException(string.Join("; ", errors));
                if (bound.Any(x => x.InstanceId == b.InstanceId)) ValidateRequirements(c, descriptor.Requirements);
            }
        }
    }
    static void ValidateRequirements(ParcelConfiguration c, CapabilityRequirements requirements)
    {
        if (requirements.MeasuredWeight && c.Weight.Source != WeightSource.Measured)
            throw new InvalidDataException("Selected protocol requires an actual same-parcel measurement");
        if (requirements.ImageAddress && (c.Images is null || c.Images.Strategy != UploadStrategy.BeforeMain))
            throw new InvalidDataException("Selected protocol requires a confirmed image address before main flow");
    }
    public CapabilityRequirements Requirements(CapabilityBinding binding) => binding.Package is null
        ? ReferenceCapabilityCatalog.Providers.Single(x => x.Kind == binding.Kind).Requirements
        : Store().VerifyInstalled(binding.Package).Manifest.Modules.Single(x => x.Id == binding.Module).Requirements ?? new(Query: true);
    public CapabilityCatalog Catalog()
    {
        RegisterContracts();
        var settingsFile = Path.Combine(launch.ConfigurationDirectory, "extension-store.json");
        var installed = store is not null || OperatingSystem.IsWindows() || File.Exists(settingsFile) ? Store().Inventory().Where(x => x.Status == "Installed").ToArray() : [];
        var descriptors = installed.SelectMany(x => Store().VerifyInstalled(x.Package).Manifest.Modules.Select(m =>
            new CapabilityDescriptor(m.Kind, "extension:" + x.Package.Id + "/" + m.Id, x.Package.Version, m.ContractIdentity ?? "PARCEL-REFERENCE-v1", m.Parameters, m.Requirements ?? new(), x.Package, m.Id)));
        return new(ReferenceCapabilityCatalog.Providers.Append(new("observation", "manual", "1.0.0", "CONTROLLED-OBSERVATION-v1", [], new())).Concat(descriptors).ToArray(), installed.Select(x => x.Package.Id + "@" + x.Package.Version).ToArray());
    }
    public async Task<BusinessReceipt> InstallAsync(string file, string sha256)
    {
        if (OperatingSystem.IsWindows())
        {
            var appRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "app"));
            var release = Wire.Decode<ApplicationRelease>(File.ReadAllText(Path.Combine(appRoot, "application-release.json")));
            ApplicationReleaseValidation.ValidateIfPublished(appRoot, release.ApplicationId, new(release.Views, release.Layouts));
            MachineRuntimePolicy.RequireProtectedApplicationInstaller(appRoot, release.Files.Keys.Append("application-release.json").ToArray());
            var start = new System.Diagnostics.ProcessStartInfo(Path.Combine(appRoot, "Ipc.Assembly.exe")) { UseShellExecute = true, Verb = "runas", WorkingDirectory = appRoot };
            foreach (var value in new[] { "--action", "extension-install", "--mode", "Simulation", "--file", Path.GetFullPath(file), "--sha256", sha256 }) start.ArgumentList.Add(value);
            using var process = System.Diagnostics.Process.Start(start) ?? throw new IOException("Protected installation helper did not start");
            await process.WaitForExitAsync().ConfigureAwait(false);
            if (process.ExitCode != 0) throw new IOException("Installation helper failed; preserve original installation journal");
            var entry = Store().Inventory().SingleOrDefault(x => x.PackageSha256 == sha256 && x.Status == "Installed") ?? throw new IOException("Installation was not confirmed by the original store registration");
            return new(entry.Status, entry.Reason, 0);
        }
        RegisterContracts(); var installed = Store().Install(file, sha256);
        return new(installed.Status, installed.Reason, 0);
    }
    public ParcelPorts Create(ParcelConfiguration c)
    {
        Validate(c);
        var resourceRoot = MachineRuntimePolicy.ResolveLeaseRoot(OperatingSystem.IsWindows() ? null : Path.Combine(launch.DataRoot, "Simulation", "resource-leases"));
        devices = new(resourceRoot, new[] { "reader:" + c.Reader.DeviceId }.Concat(new[] { c.Scale, c.Plc, c.Images?.Camera }.OfType<CapabilityBinding>().Select(x => x.Kind + ":" + x.Target)));
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
            if (b.Package is null) { value = builtIn(); hashes.Add(b.InstanceId, BuiltInFingerprint(value.GetType())); }
            else
            {
                var module = Store().VerifyInstalled(b.Package).Manifest.Modules.Single(x => x.Id == b.Module);
                value = external(loading!, module);
                hashes.Add(b.InstanceId, Wire.Hash(b.Package.ManifestSha256 + ":" + Wire.FileHash(typeof(IParcelApplication).Assembly.Location) + ":" + Wire.FileHash(typeof(ExtensionStore).Assembly.Location)));
            }
            constructed.Add(value); if (value.InstanceId != b.InstanceId) throw new InvalidDataException("Capability returned a mismatched identity"); return value;
        }
        var weight = c.Scale is { } s ? Own<IWeightSource>(s, () => new ReferenceWeightSource(s), (l, m) => l.CreateFactory<IWeightExtension>(s.Package!, m, "weight").Create(s)) : null;
        #if DIRECTPLC
        IWcsPort? wcs = null;
        #else
        var wcs = c.Wcs is { } w ? Own<IWcsPort>(w, () => new ReferenceWcs(w, w.Parameters.GetValueOrDefault("peer-root", "")), (l, m) => l.CreateFactory<IWcsExtension>(w.Package!, m, "integration").Create(w)) : null;
        #endif
        var plc = c.Plc is { } p ? Own<IPlcPort>(p, () => new ReferencePlc(p, p.Parameters.GetValueOrDefault("peer-root", "")), (l, m) => l.CreateFactory<IPlcExtension>(p.Package!, m, "plc").Create(p)) : null;
        var login = c.Login is { } a ? Own<IOperatorAuthenticator>(a, () => new ReferenceOperatorAuthenticator(a), (l, m) => l.CreateFactory<ILoginExtension>(a.Package!, m, "login").Create(a)) : null;
        var camera = c.Images is { } i ? Own<IImageCamera>(i.Camera, () => new ReferenceImageCamera(i.Camera, Path.Combine(launch.DataRoot, c.Mode.ToString(), c.StationId, c.ApplicationId)), (l, m) => l.CreateFactory<ICameraExtension>(i.Camera.Package!, m, "camera").Create(i.Camera, Path.Combine(launch.DataRoot, c.Mode.ToString(), c.StationId, c.ApplicationId))) : null;
        var uploader = c.Images is { } j ? Own<IImageUploader>(j.Uploader, () => new ReferenceImageUploader(j.Uploader, j.Uploader.Parameters.GetValueOrDefault("peer-root", "")), (l, m) => l.CreateFactory<IUploadExtension>(j.Uploader.Package!, m, "upload").Create(j.Uploader)) : null;
        // Legacy simulation configurations retain their explicit reference maintenance
        // boundary. New deployments select this authenticator in the same revision.
        var maintenanceBinding = c.MaintenanceLogin ?? new("login", "reference-login", "1.0.0", "maintenance", "reference://maintenance", "PARCEL-REFERENCE-v1", new() { ["subjects"] = "maintenance" });
        var maintenance = Own<IOperatorAuthenticator>(maintenanceBinding, () => new ReferenceOperatorAuthenticator(maintenanceBinding),
            (l, m) => l.CreateFactory<ILoginExtension>(maintenanceBinding.Package!, m, "login").Create(maintenanceBinding));
        transferred = true; return new(reader, weight, wcs, plc, hashes, login, camera, uploader, maintenance);
    }
    static string BuiltInFingerprint(Type implementation)
    {
        Dictionary<string, string> closure = [];
        void Visit(System.Reflection.Assembly assembly)
        {
            var name = assembly.GetName().Name!;
            if (closure.ContainsKey(name)) return;
            closure.Add(name, Wire.FileHash(assembly.Location));
            foreach (var dependency in assembly.GetReferencedAssemblies().Where(x => x.Name!.StartsWith("Ipc.", StringComparison.Ordinal)))
                Visit(System.Reflection.Assembly.Load(dependency));
        }
        Visit(implementation.Assembly);
        return Wire.Hash(string.Join("\n", closure.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => x.Key + ":" + x.Value)));
    }
    public async ValueTask DisposeAsync()
    {
        if (!transferred)
            foreach (var resource in constructed.AsEnumerable().Reverse()) await resource.DisposeAsync().ConfigureAwait(false);
        loading?.Dispose(); use?.Dispose(); devices?.Dispose();
    }
}

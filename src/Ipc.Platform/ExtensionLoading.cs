using System.Reflection;
using System.Runtime.Loader;

namespace Ipc.Platform;

// A load context is dependency isolation, not a sandbox. Only independently trusted bytes
// reach this class, after FINAL deployment validation and resource lease acquisition.
public sealed class ExtensionLoading(ExtensionStore store) : IDisposable
{
    readonly Dictionary<string, PackageLoadContext> contexts = [];
    readonly List<FileStream> pinned = [];
    public IControlledObservationSource Create(PackageReference package, ExtensionModule module, ExtensionInstance instance)
    {
        if (!instance.Enabled || instance.PackageId != package.Id || instance.ModuleId != module.Id ||
            ExtensionSchema.Validate(module.Parameters, instance.Parameters).Length != 0) throw new InvalidDataException("Invalid extension instance");
        var factory = CreateFactory<IObservationExtension>(package, module, "observation");
        var source = factory.Create(new(instance.Id, instance.DeviceId, ExtensionSchema.EffectiveValues(module, instance)));
        return source ?? throw new InvalidDataException("Extension returned no source");
        // The Product takes ownership immediately, then validates identity. A mismatched
        // returned source must still be stopped/disposed before releasing any lease.
    }
    public T CreateFactory<T>(PackageReference package, ExtensionModule module, string kind) where T : class
    {
        var verified = store.VerifyInstalled(package);
        if (module.Kind != kind || !verified.Manifest.Modules.Any(x => Wire.Encode(x) == Wire.Encode(module)))
            throw new InvalidDataException("Unverified module descriptor/kind");
        var assembly = Context(package).LoadEntry(module.Assembly);
        var type = assembly.GetType(module.EntryType, throwOnError: true)!;
        if (!typeof(T).IsAssignableFrom(type) || type.IsAbstract || !type.IsPublic)
            throw new InvalidDataException("Entry does not implement the required public extension contract");
        return (T)(Activator.CreateInstance(type) ?? throw new InvalidDataException("Extension factory construction failed"));
    }
    PackageLoadContext Context(PackageReference package)
    {
        if (!contexts.TryGetValue(package.ManifestSha256, out var context))
        {
            var verified = store.VerifyInstalled(package);
            var directory = store.PackageDirectory(package);
            // File sharing denies in-place replacement on Windows until all work terminates.
            foreach (var file in verified.Manifest.Files)
                pinned.Add(new FileStream(Path.Combine(directory, file.Path), FileMode.Open, FileAccess.Read, FileShare.Read));
            store.VerifyInstalled(package);
            var dependencies = verified.Manifest.Dependencies.Select(Context).ToArray();
            context = new PackageLoadContext(directory, verified.Manifest.Files, dependencies); contexts.Add(package.ManifestSha256, context);
        }
        return context;
    }
    public void Dispose()
    {
        // Called only AFTER the owning observation module has stopped AND disposed all sources.
        foreach (var context in contexts.Values) context.Unload();
        contexts.Clear(); foreach (var file in pinned) file.Dispose(); pinned.Clear();
        // Unload is cooperative. No GC loop or 'unloaded successfully' claim is made here.
    }
    sealed class PackageLoadContext(string directory, ExtensionFile[] files, PackageLoadContext[] dependencies) : AssemblyLoadContext(isCollectible: true)
    {
        public Assembly LoadEntry(string file) => LoadFromAssemblyPath(Path.Combine(directory, file));
        protected override Assembly? Load(AssemblyName name)
        {
            var shared = ManagedAssemblyPolicy.Shared(name);
            if (shared is not null) return shared;
            var candidates = files.Where(x => x.Path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && string.Equals(Path.GetFileNameWithoutExtension(x.Path), name.Name, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (candidates.Length == 0)
            {
                var owners = dependencies.SelectMany(x => x.Owners(name.Name!)).Distinct().ToArray();
                if (owners.Length != 1) throw new FileLoadException("Missing/ambiguous declared dependency: " + name.Name);
                return owners[0].LoadFromAssemblyName(name);
            }
            if (candidates.Length != 1) throw new FileLoadException("Private dependency not in signed exact package: " + name.Name);
            var path = Path.Combine(directory, candidates[0].Path);
            if (Wire.FileHash(path) != candidates[0].Sha256.ToUpperInvariant()) throw new FileLoadException("Private dependency changed");
            if (!ManagedAssemblyPolicy.Exact(AssemblyName.GetAssemblyName(path), name)) throw new FileLoadException("Exact private dependency version mismatch");
            return LoadFromAssemblyPath(path);
        }
        IEnumerable<PackageLoadContext> Owners(string name)
        {
            if (files.Any(x => x.Path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && string.Equals(Path.GetFileNameWithoutExtension(x.Path), name, StringComparison.OrdinalIgnoreCase))) yield return this;
            foreach (var owner in dependencies.SelectMany(x => x.Owners(name))) yield return owner;
        }
        protected override nint LoadUnmanagedDll(string name) => throw new DllNotFoundException("No approved native SDK boundary for " + name);
    }
}

public static class ExtensionDeployment
{
    public static string[] Validate(DeploymentExtensions? deployment, DeviceBinding[] bindings, ExtensionStore? store)
    {
        List<string> errors = [];
        var used = bindings.Where(x => x.Driver.StartsWith("extension:", StringComparison.Ordinal)).ToArray();
        if (deployment is null) return used.Length == 0 ? [] : ["Extension binding has no deployment"];
        if (deployment.Packages is null || deployment.Instances is null || deployment.Packages.Length > 64 || deployment.Instances.Length > 64 ||
            deployment.Packages.Any(x => x is null) || deployment.Instances.Any(x => x is null) ||
            deployment.Packages.Select(x => x.Id).Distinct().Count() != deployment.Packages.Length ||
            deployment.Instances.Select(x => x.Id).Distinct().Count() != deployment.Instances.Length)
            return ["Malformed deployment package/instance identities or quota"];
        if (store is null && deployment.Packages.Length > 0) return ["Trusted extension store is unavailable"];
        Dictionary<string, VerifiedExtension> packages = [];
        foreach (var reference in deployment.Packages)
            try { packages.Add(reference.Id, store!.VerifyInstalled(reference)); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or System.Security.Cryptography.CryptographicException or System.Text.Json.JsonException)
            { errors.Add(reference.Id + ": " + ex.Message); }
        var visiting = new HashSet<string>(); var seen = new HashSet<string>();
        bool Visit(string id)
        {
            if (seen.Contains(id)) return true;
            if (!packages.TryGetValue(id, out var p) || !visiting.Add(id)) return false;
            foreach (var d in p.Manifest.Dependencies)
                if (!deployment.Packages.Contains(d) || !Visit(d.Id)) return false;
            visiting.Remove(id); seen.Add(id); return true;
        }
        foreach (var p in packages.Values)
        {
            if (!Visit(p.Manifest.Id)) errors.Add("Missing/conflicting/cyclic dependency: " + p.Manifest.Id);
            if (p.Manifest.Prerequisites.Length > 0) errors.Add("SDK/system-driver prerequisite requires approved evidence resolver: " + p.Manifest.Id);
        }
        foreach (var i in deployment.Instances)
        {
            if (!ExtensionSchema.IsId(i.Id) || string.IsNullOrWhiteSpace(i.DeviceId) || i.DeviceId.Length > 128 || i.DeviceId.Any(char.IsControl) ||
                !packages.TryGetValue(i.PackageId, out var p)) { errors.Add("Invalid instance identity/package: " + i.Id); continue; }
            var module = p.Manifest.Modules.SingleOrDefault(x => x.Id == i.ModuleId);
            if (module is null || module.Kind != "observation") { errors.Add("Unknown observation instance module: " + i.Id); continue; }
            errors.AddRange(ExtensionSchema.Validate(module.Parameters, i.Parameters).Select(x => i.Id + ": " + x));
            if (deployment.Instances.Count(x => x.PackageId == i.PackageId && x.ModuleId == i.ModuleId) > module.MaximumInstances) errors.Add("Module instance quota: " + i.ModuleId);
        }
        if (deployment.Instances.Where(x => x.Enabled).Select(x => x.DeviceId).Distinct().Count() != deployment.Instances.Count(x => x.Enabled)) errors.Add("Duplicate enabled device resources");
        foreach (var b in used)
        {
            var instance = deployment.Instances.SingleOrDefault(x => "extension:" + x.Id == b.Driver);
            if (instance is null || !instance.Enabled || instance.DeviceId != b.DeviceId) errors.Add("Binding requires an enabled matching instance/device: " + b.Role);
            if (used.Count(x => x.Driver == b.Driver) != 1) errors.Add("Instance cannot own two device bindings: " + b.Driver);
        }
        foreach (var i in deployment.Instances.Where(x => x.Enabled))
            if (!used.Any(x => x.Driver == "extension:" + i.Id)) errors.Add("Enabled observation instance requires an explicit business role binding: " + i.Id);
        errors.AddRange(ValidateServices(deployment, packages));
        return errors.Distinct().ToArray();
    }
    static IEnumerable<string> ValidateServices(DeploymentExtensions deployment, Dictionary<string, VerifiedExtension> packages)
    {
        var services = deployment.Services ?? []; var ports = deployment.Ports ?? [];
        if (services.Length > 64 || ports.Length > 64 || services.Any(x => x is null || x.Parameters is null) || ports.Any(x => x is null || x.Target is null))
            return ["Malformed service deployment"];
        List<string> errors = [];
        if (services.Select(x => x.Id).Concat(deployment.Instances.Select(x => x.Id)).Distinct().Count() != services.Length + deployment.Instances.Length)
            errors.Add("Service/device instance IDs must be distinct");
        if (ports.Select(x => x.Port).Distinct().Count() != ports.Length) errors.Add("Duplicate service port binding");
        foreach (var service in services)
        {
            if (!ExtensionSchema.IsId(service.Id) || !packages.TryGetValue(service.PackageId, out var package)) { errors.Add("Invalid service instance/package: " + service.Id); continue; }
            var module = package.Manifest.Modules.SingleOrDefault(x => x.Id == service.ModuleId && x.Kind == "integration");
            if (module is null) { errors.Add("Missing typed integration capability: " + service.Id); continue; }
            errors.AddRange(ExtensionSchema.Validate(module.Parameters, service.Parameters).Select(x => service.Id + ": " + x));
            if (services.Count(x => x.PackageId == service.PackageId && x.ModuleId == service.ModuleId) > module.MaximumInstances) errors.Add("Service module instance quota");
            if (service.Enabled && !ports.Any(x => x.InstanceId == service.Id)) errors.Add("Enabled service requires an explicit port: " + service.Id);
        }
        foreach (var port in ports)
            if (!ExtensionSchema.IsId(port.Port) || !services.Any(x => x.Enabled && x.Id == port.InstanceId) ||
                string.IsNullOrWhiteSpace(port.Target.Id) || string.IsNullOrWhiteSpace(port.Target.Revision) || string.IsNullOrWhiteSpace(port.Target.Contract) ||
                !Uri.TryCreate(port.Target.Endpoint, UriKind.Absolute, out _)) errors.Add("Invalid service port/target: " + port.Port);
        if (ports.GroupBy(x => x.InstanceId).Any(x => x.Count() != 1)) errors.Add("Service instance must own exactly one target/port");
        return errors;
    }

}

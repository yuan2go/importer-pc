using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.Loader;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Assembly;

// This is an explicitly selected, trusted original release, not an executable sandbox.
// The caller owns the offline application lease and complete backup for the entire call.
public static class OriginalParcelQuery
{
    static readonly List<object> retainedFailures = [];
    public static async Task<StageReply?> QueryAsync(string releaseDirectory, string operationJson, string stage,
        string configurationDirectory, string dataRoot, CancellationToken token)
    {
        if (stage is not ("Upload" or "image-upload")) throw new InvalidDataException("Only the original Attached upload may be queried offline");
        var original = Wire.Decode<ParcelOperation>(operationJson);
        var request = original.Upload?.Request ?? throw new InvalidDataException("Original upload intent missing");
        if (original.FrozenConfiguration.Images?.Strategy != UploadStrategy.Attached || request.Stage != "image-upload" ||
            request.ApplicationId != original.FrozenConfiguration.ApplicationId || request.OperationId != original.Id || request.AdmissionId != original.AdmissionId ||
            request.ConfigurationHash != original.ConfigurationHash ||
            request.PayloadHash != Wire.Hash(request.Payload) || Wire.Encode(request.Binding) != Wire.Encode(original.FrozenConfiguration.Images.Uploader))
            throw new InvalidDataException("Original Attached upload identity/configuration is inconsistent");
        var release = new OriginalRelease(releaseDirectory, original.FrozenConfiguration.ApplicationId);
        OriginalContext context;
        try { context = new OriginalContext(release); }
        catch { release.Dispose(); throw; }
        object? factory = null, ports = null, uploader = null;
        Exception? failure = null;
        StageReply? result = null;
        try
        {
            var assembly = context.LoadOriginal("Ipc.Assembly.dll");
            var platform = context.LoadOriginal("Ipc.Platform.dll");
            var contracts = context.LoadOriginal("Ipc.Application.Contracts.dll");
            _ = context.LoadOriginal("Ipc.Application.ParcelFlow.dll");
            var wire = platform.GetType("Ipc.Platform.Wire", true)!;
            object Decode(Type type, string json) => Call(wire.GetMethod("Decode")!.MakeGenericMethod(type), null, json)!;
            string Encode(object value) => (string)Call(wire.GetMethod("Encode")!.MakeGenericMethod(value.GetType()), null, value)!;
            var oldOperation = Decode(contracts.GetType("Ipc.Application.Contracts.ParcelOperation", true)!, operationJson);
            var config = Get(oldOperation, "FrozenConfiguration")!;
            if (original.ConfigurationHash != Wire.Hash(Encode(config))) throw new InvalidDataException("Original release frozen configuration hash mismatch");
            var oldRequest = Get(Get(oldOperation, "Upload")!, "Request")!;
            // Old Wire must accept and round-trip the exact frozen intent; no conversion to a new ABI.
            if (Wire.Hash(Encode(oldRequest)) != Wire.Hash(Wire.Encode(request))) throw new InvalidDataException("Original release cannot preserve the exact frozen request");
            var launch = Activator.CreateInstance(platform.GetType("Ipc.Platform.BusinessLaunch", true)!, Path.GetFullPath(configurationDirectory), Path.GetFullPath(dataRoot), null, null)!;
            var factoryType = assembly.GetType("Ipc.Assembly.ParcelCapabilityFactory", true)!;
            factory = Activator.CreateInstance(factoryType, launch)!;
            var normalized = Call(factoryType.GetMethod("Normalize")!, factory, config)!;
            if (Encode(normalized) != Encode(config)) throw new InvalidDataException("Original frozen configuration requires normalization changes; preserve original target");
            var definition = Call(assembly.GetType("Ipc.Assembly.ParcelAssembly", true)!.GetMethod("Definition")!, null)!;
            if ((string)Get(definition, "Id")! != original.FrozenConfiguration.ApplicationId) throw new InvalidDataException("Original release Application identity mismatch");
            var errors = (string[])Call(definition.GetType().GetMethod("Validate")!, definition, config)!;
            if (errors.Length != 0) throw new InvalidDataException(string.Join("; ", errors));
            Call(factoryType.GetMethod("Validate")!, factory, config);
            var requirements = Call(factoryType.GetMethod("Requirements")!, factory, Get(oldRequest, "Binding"))!;
            if (!(bool)Get(requirements, "Query")!) throw new InvalidDataException("Original provider declares no authoritative query capability");
            ports = Call(factoryType.GetMethod("Create")!, factory, config)!;
            var hashes = (IReadOnlyDictionary<string, string>)Get(ports, "ImplementationHashes")!;
            if (hashes.GetValueOrDefault(request.Binding.InstanceId) != request.ImplementationHash)
                throw new InvalidDataException("Original release implementation differs from the frozen request");
            uploader = Get(ports, "Uploader") ?? throw new InvalidDataException("Original uploader unavailable");
            var capability = uploader.GetType().GetInterfaces().Single(x => x.FullName == "Ipc.Application.Contracts.ICapabilityPort");
            if ((string)capability.GetProperty("InstanceId")!.GetValue(uploader)! != request.Binding.InstanceId) throw new InvalidDataException("Original uploader identity mismatch");
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
            budget.CancelAfter(original.FrozenConfiguration.TimeoutMs);
            // Await the actual provider: cancellation never releases a still-running original call.
            await Await(Call(capability.GetMethod("StartAsync")!, uploader, budget.Token)!).ConfigureAwait(false);
            var health = await Await(Call(capability.GetMethod("HealthAsync")!, uploader, budget.Token)!).ConfigureAwait(false) ?? throw new InvalidDataException("Original uploader health missing");
            var now = DateTimeOffset.UtcNow;
            if ((string)Get(health, "InstanceId")! != request.Binding.InstanceId || !(bool)Get(health, "Ready")! ||
                (DateTimeOffset)Get(health, "ConfirmedAt")! > now || (DateTimeOffset)Get(health, "ValidUntil")! <= now ||
                (DateTimeOffset)Get(health, "ValidUntil")! <= (DateTimeOffset)Get(health, "ConfirmedAt")!)
                throw new InvalidDataException("Original uploader is not authoritatively healthy");
            var query = uploader.GetType().GetInterfaces().SelectMany(x => x.GetMethods()).Single(x => x.Name == "QueryAsync");
            var reply = await Await(Call(query, uploader, oldRequest, budget.Token)!).ConfigureAwait(false);
            result = reply is null ? null : Wire.Decode<StageReply>(Encode(reply));
            if (result is not null && (result.RequestId != request.Id || result.PayloadHash != request.PayloadHash))
                throw new InvalidDataException("Original query returned a different request identity");
        }
        catch (Exception ex) { failure = ex; }
        // Never release package/device leases after a failed stop/dispose. Retain the context
        // and ownership graph until the process exits; the caller must report recovery blocked.
        List<Exception> cleanup = [];
        if (uploader is not null)
        {
            try
            {
                var capability = uploader.GetType().GetInterfaces().Single(x => x.FullName == "Ipc.Application.Contracts.ICapabilityPort");
                await Await(Call(capability.GetMethod("StopAsync")!, uploader, CancellationToken.None)!).ConfigureAwait(false);
            }
            catch (Exception ex) { cleanup.Add(ex); }
        }
        if (ports is not null && cleanup.Count == 0)
        {
            foreach (var resource in PortResources(ports).Reverse())
                try { await ((IAsyncDisposable)resource).DisposeAsync().ConfigureAwait(false); }
                catch (Exception ex) { cleanup.Add(ex); }
        }
        if (cleanup.Count == 0 && factory is not null)
            try { await ((IAsyncDisposable)factory).DisposeAsync().ConfigureAwait(false); }
            catch (Exception ex) { cleanup.Add(ex); }
        if (cleanup.Count != 0)
        {
            lock (retainedFailures) retainedFailures.Add(new object?[] { context, release, factory, ports, uploader });
            if (failure is not null) cleanup.Insert(0, failure);
            throw new AggregateException("Original provider cleanup failed; original resource leases retained until process exit", cleanup);
        }
        context.Unload(); release.Dispose();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        return result;
    }
    static object? Get(object value, string name) => value.GetType().GetProperty(name)!.GetValue(value);
    static object? Call(MethodInfo method, object? instance, params object?[] arguments)
    {
        try { return method.Invoke(instance, arguments); }
        catch (TargetInvocationException ex) when (ex.InnerException is not null) { ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }
    static async Task<object?> Await(object value)
    {
        var task = (Task)value;
        await task.ConfigureAwait(false);
        return task.GetType().GetProperty("Result")?.GetValue(task);
    }
    static IEnumerable<object> PortResources(object ports)
    {
        var resources = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var property in ports.GetType().GetProperties())
        {
            var value = property.GetValue(ports);
            if (value is IAsyncDisposable) resources.Add(value);
            else if (value is System.Collections.IDictionary dictionary)
                foreach (var item in dictionary.Values) if (item is IAsyncDisposable) resources.Add(item);
        }
        return resources;
    }
    sealed class OriginalRelease : IDisposable
    {
        readonly Dictionary<string, string> files;
        readonly List<FileStream> holds = [];
        internal string Root { get; }
        internal OriginalRelease(string directory, string application)
        {
            Root = Path.GetFullPath(directory);
            var manifest = Ordinary("application-release.json");
            if (new FileInfo(manifest).Length > 4 * 1024 * 1024) throw new InvalidDataException("Original release manifest quota exceeded");
            using var document = JsonDocument.Parse(File.ReadAllText(manifest));
            var root = document.RootElement;
            if (root.GetProperty("SchemaVersion").GetInt32() != 1 || root.GetProperty("ApplicationId").GetString() != application)
                throw new InvalidDataException("Original release manifest Application/schema mismatch");
            files = new(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var item in root.GetProperty("Files").EnumerateObject())
                {
                    if (files.Count >= 10000) throw new InvalidDataException("Original release inventory quota exceeded");
                    var hash = item.Value.GetString() ?? "";
                    if (hash.Length != 64 || hash.Any(x => !Uri.IsHexDigit(x)) || !files.TryAdd(item.Name, hash)) throw new InvalidDataException("Invalid original release file inventory");
                    var path = Ordinary(item.Name);
                    var hold = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    holds.Add(hold);
                    if (!string.Equals(Convert.ToHexString(SHA256.HashData(hold)), hash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Original release file hash mismatch: " + item.Name);
                }
                var directories = new Stack<string>(); directories.Push(Root);
                var entries = 0;
                while (directories.TryPop(out var currentDirectory))
                    foreach (var entry in Directory.EnumerateFileSystemEntries(currentDirectory))
                    {
                        if (++entries > 20000) throw new InvalidDataException("Original release tree quota exceeded");
                        var attributes = File.GetAttributes(entry);
                        if ((attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Linked original release entry");
                        if ((attributes & FileAttributes.Directory) != 0) directories.Push(entry);
                        else if (Path.GetExtension(entry).Equals(".dll", StringComparison.OrdinalIgnoreCase) && !files.ContainsKey(Path.GetRelativePath(Root, entry).Replace(Path.DirectorySeparatorChar, '/')))
                            throw new InvalidDataException("Unregistered DLL in original release");
                    }
            }
            catch { Dispose(); throw; }
        }
        string Ordinary(string relative)
        {
            if (string.IsNullOrWhiteSpace(relative) || relative.Contains('\\') || Path.IsPathRooted(relative) || relative.Split('/').Any(x => x is "" or "." or "..")) throw new InvalidDataException("Invalid original release path");
            var path = Path.GetFullPath(Path.Combine(Root, relative));
            for (FileSystemInfo? part = new FileInfo(path); part is not null; part = part is FileInfo f ? f.Directory : ((DirectoryInfo)part).Parent)
                if ((part.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Linked original release file/ancestor");
            if (!File.Exists(path)) throw new InvalidDataException("Original release file absent");
            return path;
        }
        internal string Verified(string path)
        {
            var relative = Path.GetRelativePath(Root, Path.GetFullPath(path)).Replace(Path.DirectorySeparatorChar, '/');
            if (!files.TryGetValue(relative, out var hash)) throw new InvalidDataException("Dependency outside original release inventory: " + relative);
            var verified = Ordinary(relative);
            if (!string.Equals(Wire.FileHash(verified), hash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Original release dependency changed: " + relative);
            return verified;
        }
        public void Dispose() { foreach (var hold in holds) hold.Dispose(); holds.Clear(); }
    }
    sealed class OriginalContext(OriginalRelease release) : AssemblyLoadContext("original-parcel-query-" + Guid.NewGuid().ToString("N"), isCollectible: true)
    {
        readonly AssemblyDependencyResolver resolver = new(release.Verified(Path.Combine(release.Root, "Ipc.Assembly.dll")));
        readonly HashSet<string> framework = Directory.EnumerateFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll").Select(x => Path.GetFileNameWithoutExtension(x)!).ToHashSet(StringComparer.Ordinal);
        internal System.Reflection.Assembly LoadOriginal(string file) => LoadFromAssemblyPath(release.Verified(Path.Combine(release.Root, file)));
        protected override System.Reflection.Assembly? Load(AssemblyName name)
        {
            var path = resolver.ResolveAssemblyToPath(name);
            if (path is not null) return LoadFromAssemblyPath(release.Verified(path));
            if (name.Name is not null && framework.Contains(name.Name) && !name.Name.StartsWith("Ipc.", StringComparison.Ordinal)) return Default.LoadFromAssemblyName(name);
            throw new FileNotFoundException("Dependency absent from exact original release: " + name.FullName);
        }
        protected override IntPtr LoadUnmanagedDll(string name)
        {
            var path = resolver.ResolveUnmanagedDllToPath(name) ?? throw new DllNotFoundException("Native dependency absent from exact original release: " + name);
            return LoadUnmanagedDllFromPath(release.Verified(path));
        }
    }
}

using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Ipc.Platform;

// Static metadata only. Framework identities come from the pinned runtime's shared
// Microsoft.NETCore.App directory, never a System.* prefix or the application's TPA list.
public static class ManagedAssemblyPolicy
{
    public sealed record Image(string Path, AssemblyName Identity, AssemblyName[] References);
    static readonly Lazy<Dictionary<string, AssemblyName>> framework = new(ReadFramework);
    static Dictionary<string, AssemblyName> ReadFramework()
    {
        var directory = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        IEnumerable<string> paths;
        if (new DirectoryInfo(directory).Parent?.Name == "Microsoft.NETCore.App")
            paths = Directory.GetFiles(directory, "*.dll");
        else
        {
            // Self-contained output mixes framework and application DLLs. Only the
            // exact official runtime-pack inventory in this frozen host's deps file
            // defines framework ownership; the directory/TPA/System.* prefix do not.
            var entry = Assembly.GetEntryAssembly()?.Location ?? throw new InvalidDataException("Frozen entry assembly required");
            var deps = Path.ChangeExtension(entry, ".deps.json");
            if (new FileInfo(deps).Length > 16 * 1024 * 1024) throw new InvalidDataException("Runtime dependency inventory quota exceeded");
            using var document = JsonDocument.Parse(File.ReadAllBytes(deps));
            var root = document.RootElement;
            var target = root.GetProperty("targets").GetProperty(root.GetProperty("runtimeTarget").GetProperty("name").GetString()!);
            var prefix = "runtimepack.Microsoft.NETCore.App.Runtime." + RuntimeInformation.RuntimeIdentifier + "/";
            var packs = target.EnumerateObject().Where(p => p.Name.StartsWith(prefix, StringComparison.Ordinal) &&
                root.GetProperty("libraries").GetProperty(p.Name).GetProperty("type").GetString() == "runtimepack").ToArray();
            if (packs.Length != 1) throw new InvalidDataException("Exact self-contained core runtime pack required");
            List<string> selected = [];
            foreach (var file in packs[0].Value.GetProperty("runtime").EnumerateObject())
            {
                if (Path.GetFileName(file.Name) != file.Name || !file.Name.EndsWith(".dll", StringComparison.Ordinal))
                    throw new InvalidDataException("Invalid core runtime pack file");
                var path = Path.Combine(directory, file.Name);
                var identity = AssemblyName.GetAssemblyName(path);
                if (identity.Version?.ToString() != file.Value.GetProperty("assemblyVersion").GetString())
                    throw new InvalidDataException("Runtime pack assembly identity mismatch");
                selected.Add(path);
            }
            if (!selected.Contains(typeof(object).Assembly.Location, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException("Runtime inventory does not own the executing core library");
            paths = selected;
        }
        return paths.Select(TryIdentity).Where(x => x is not null).ToDictionary(x => x!.Name!, x => x!, StringComparer.OrdinalIgnoreCase);
    }
    static AssemblyName? TryIdentity(string file)
    { try { return AssemblyName.GetAssemblyName(file); } catch (BadImageFormatException) { return null; } }
    public static bool Exact(AssemblyName a, AssemblyName b) =>
        string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase) && a.Version == b.Version &&
        (a.CultureName ?? "") == (b.CultureName ?? "") &&
        (a.GetPublicKeyToken() ?? []).SequenceEqual(b.GetPublicKeyToken() ?? []);
    public static Assembly? Shared(AssemblyName reference)
    {
        var contract = ExtensionPublicContracts.Snapshot().GetValueOrDefault(reference.Name!);
        if (contract is not null)
        {
            if (!Exact(reference, contract.GetName())) throw new FileLoadException("Public contract identity mismatch: " + reference.FullName);
            return contract;
        }
        if (framework.Value.TryGetValue(reference.Name!, out var identity))
        {
            if (!Exact(reference, identity)) throw new FileLoadException("Target framework identity mismatch: " + reference.FullName);
            return Assembly.Load(identity);
        }
        return null;
    }
    public static bool IsShared(AssemblyName reference)
    {
        if (ExtensionPublicContracts.Snapshot().TryGetValue(reference.Name!, out var contract)) return Exact(reference, contract.GetName());
        return framework.Value.TryGetValue(reference.Name!, out var known) && Exact(reference, known);
    }
    public static Image Read(string path, byte[] bytes)
    {
        using var pe = new PEReader(new MemoryStream(bytes));
        if (!pe.HasMetadata || pe.PEHeaders.CorHeader is null || (pe.PEHeaders.CorHeader.Flags & CorFlags.ILOnly) == 0)
            throw new InvalidDataException("Only managed IL extensions are supported");
        var reader = pe.GetMetadataReader();
        if (!reader.IsAssembly) throw new InvalidDataException("Assembly metadata required");
        var definition = reader.GetAssemblyDefinition();
        AssemblyName Name(string name, Version version, string culture, byte[] key, bool fullKey)
        {
            var identity = new AssemblyName { Name = name, Version = version, CultureName = culture };
            if (fullKey) identity.SetPublicKey(key); else identity.SetPublicKeyToken(key);
            return identity;
        }
        var own = Name(reader.GetString(definition.Name), definition.Version, reader.GetString(definition.Culture), reader.GetBlobBytes(definition.PublicKey), true);
        if (own.Name != Path.GetFileNameWithoutExtension(path) || ExtensionPublicContracts.Snapshot().ContainsKey(own.Name!) || framework.Value.ContainsKey(own.Name!))
            throw new InvalidDataException("Signed filename/assembly identity mismatch or private copy of shared contract/framework");
        bool target = false;
        foreach (var handle in reader.CustomAttributes)
        {
            var attribute = reader.GetCustomAttribute(handle);
            if (attribute.Parent.Kind != HandleKind.AssemblyDefinition || attribute.Constructor.Kind != HandleKind.MemberReference) continue;
            var constructor = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
            if (constructor.Parent.Kind != HandleKind.TypeReference) continue;
            var type = reader.GetTypeReference((TypeReferenceHandle)constructor.Parent);
            if (reader.GetString(type.Namespace) != "System.Runtime.Versioning" || reader.GetString(type.Name) != "TargetFrameworkAttribute") continue;
            var blob = reader.GetBlobReader(attribute.Value);
            target = blob.ReadUInt16() == 1 && blob.ReadSerializedString() == ".NETCoreApp,Version=v10.0";
        }
        if (!target) throw new InvalidDataException("Extension assembly must explicitly target net10.0 public contracts");
        var references = reader.AssemblyReferences.Select(h =>
        {
            var r = reader.GetAssemblyReference(h);
            return Name(reader.GetString(r.Name), r.Version, reader.GetString(r.Culture), reader.GetBlobBytes(r.PublicKeyOrToken), (r.Flags & AssemblyFlags.PublicKey) != 0);
        }).ToArray();
        return new(path, own, references);
    }
    public static void Validate(Image[] local, IEnumerable<Image> dependencies)
    {
        var all = local.Concat(dependencies).ToArray();
        var duplicate = all.GroupBy(x => x.Identity.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault(x => x.Count() != 1);
        if (duplicate is not null) throw new InvalidDataException("Ambiguous managed assembly in exact closure: " + duplicate.Key);
        foreach (var image in all)
            foreach (var reference in image.References)
            {
                if (IsShared(reference)) continue;
                var match = all.SingleOrDefault(x => string.Equals(x.Identity.Name, reference.Name, StringComparison.OrdinalIgnoreCase));
                if (match is null || !Exact(match.Identity, reference))
                    throw new InvalidDataException("Missing/version-conflicting managed reference: " + reference.FullName + " from " + image.Path);
            }
    }
}

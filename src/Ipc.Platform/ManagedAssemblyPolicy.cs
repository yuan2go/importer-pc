using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Ipc.Platform;

// Static metadata only. Framework identities come from the pinned runtime's shared
// Microsoft.NETCore.App directory, never a System.* prefix or the application's TPA list.
public static class ManagedAssemblyPolicy
{
    public sealed record Image(string Path, AssemblyName Identity, AssemblyName[] References);
    static readonly Lazy<Dictionary<string, AssemblyName>> framework = new(() => Directory.GetFiles(
        Path.GetDirectoryName(typeof(object).Assembly.Location)!, "*.dll").Select(TryIdentity).Where(x => x is not null)
        .ToDictionary(x => x!.Name!, x => x!, StringComparer.OrdinalIgnoreCase));
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

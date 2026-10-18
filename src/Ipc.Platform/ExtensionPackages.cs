using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Ipc.Platform;

public sealed record VerifiedExtension(ExtensionManifest Manifest, string ManifestSha256, string PackageSha256,
    string TrustSha256, DateTimeOffset VerifiedAt, bool TestOnly)
{
    public PackageReference Reference => new(Manifest.Id, Manifest.Version, ManifestSha256);
}

public static class ExtensionPackages
{
    public const int MaximumFiles = 256, MaximumManifestBytes = 512 * 1024;
    public const long MaximumFileBytes = 32 * 1024 * 1024, MaximumExpandedBytes = 128 * 1024 * 1024;
    public static bool SafePath(string? path)
    {
        if (path is null || path.Length is < 1 or > 200 || path.Any(c => c < 32 || c > 126 || "\\:<>\"|?*".Contains(c)) || path.StartsWith('/')) return false;
        return path.Split('/').All(p => p.Length is > 0 and <= 100 && p is not ("." or "..") &&
            !p.EndsWith('.') && !p.EndsWith(' ') && !new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(p.Split('.')[0], StringComparer.OrdinalIgnoreCase));
    }
    static byte[] ReadBounded(Stream stream, long maximum)
    {
        using var output = new MemoryStream(); byte[] buffer = new byte[8192]; int count;
        while ((count = stream.Read(buffer)) != 0)
        {
            if (output.Length + count > maximum) throw new InvalidDataException("Extension size quota exceeded");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }
    static VerifiedExtension VerifyMetadata(byte[] manifestBytes, byte[] signature, ExtensionTrust trust, RunMode mode, string packageHash)
    {
        using (var document = System.Text.Json.JsonDocument.Parse(manifestBytes, new() { MaxDepth = 24 })) RejectDuplicateProperties(document.RootElement);
        var m = Wire.Decode<ExtensionManifest>(Encoding.UTF8.GetString(manifestBytes));
        if (trust.SchemaVersion != 1 || trust.Publishers is null || trust.Publishers.Any(x => x is null || !ExtensionSchema.IsId(x.Id) || x.PublicKeyPem is null || x.PublicKeyPem.Contains("PRIVATE KEY", StringComparison.Ordinal)) ||
            trust.Publishers.Select(x => x.Id).Distinct().Count() != trust.Publishers.Length)
            throw new InvalidDataException("Invalid independent offline trust policy");
        var publisher = trust.Publishers.SingleOrDefault(x => x.Id == m.Publisher)
            ?? throw new InvalidDataException("Publisher is not in the independent trust policy");
        if (mode == RunMode.Production && publisher.TestOnly) throw new InvalidDataException("Test publisher is forbidden in Production");
        using var rsa = RSA.Create(); rsa.ImportFromPem(publisher.PublicKeyPem);
        if (rsa.KeySize < 3072 || !rsa.VerifyData(manifestBytes, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss))
            throw new CryptographicException("Extension signature invalid");
        if (m.SchemaVersion != 1 || !ExtensionSchema.IsId(m.Id) || !ExtensionSchema.IsVersion(m.Version) ||
            m.ContractMajor != 1 || m.Runtime != "net10.0" || m.Mode != mode ||
            !ExtensionSchema.IsHash(m.HostContractSha256) || !ExtensionContractCompatibility.Accepts(m) ||
            m.SourceCommit is null || m.SourceCommit.Length != 40 || !m.SourceCommit.All(char.IsAsciiHexDigit) ||
            m.Rid != "any" && m.Rid != RuntimeInformation.RuntimeIdentifier)
            throw new InvalidDataException("Unsupported extension identity/runtime/contract/RID/mode/source");
        if (m.Files is null || m.Modules is null || m.Dependencies is null || m.Prerequisites is null ||
            m.Files.Length is < 1 or > MaximumFiles || m.Modules.Length > 32 || m.Dependencies.Length > 32 ||
            m.Files.Any(x => x is null || !SafePath(x.Path) || x.Path is "manifest.json" or "manifest.sig" || x.Length is < 0 or > MaximumFileBytes || !ExtensionSchema.IsHash(x.Sha256)) ||
            m.Files.Sum(x => x.Length) > MaximumExpandedBytes || m.Files.Select(x => x.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != m.Files.Length ||
            m.Dependencies.Any(x => x is null || !ExtensionSchema.IsId(x.Id) || !ExtensionSchema.IsVersion(x.Version) || !ExtensionSchema.IsHash(x.ManifestSha256) || x.Id == m.Id) ||
            m.Dependencies.Select(x => x.Id).Distinct().Count() != m.Dependencies.Length ||
            m.Modules.Any(x => x is null || !ExtensionSchema.IsId(x.Id) || x.Kind is not ("observation" or "integration" or "weight" or "plc" or "login" or "camera" or "upload") ||
                x.ContractIdentity is not null && (string.IsNullOrWhiteSpace(x.ContractIdentity) || x.ContractIdentity.Length > 128 || x.ContractIdentity.Any(char.IsControl)) ||
                x.Assurance is { } a && (a.SchemaVersion != 1 || string.IsNullOrWhiteSpace(a.ProtocolEvidence) || a.ProtocolEvidence.Length > 256 || a.ProtocolEvidence.Any(char.IsControl)) ||
                x.Requirements is { CompletesOnSubmit: true, PhysicalConfirmation: false } ||
                x.MaximumInstances is < 1 or > 32 || string.IsNullOrWhiteSpace(x.EntryType) || x.EntryType.Length > 256 ||
                !m.Files.Any(f => f.Path == x.Assembly && f.Path.EndsWith(".dll", StringComparison.Ordinal)) || ExtensionSchema.ValidateDefinition(x.Parameters).Length != 0) ||
            m.Modules.Select(x => x.Id).Distinct().Count() != m.Modules.Length)
            throw new InvalidDataException("Invalid extension inventory/module/schema/dependency/quota");
        var publicContracts = ExtensionPublicContracts.Snapshot();
        if (m.Modules.Any(x => x.Kind is "integration" or "weight" or "plc" or "login" or "camera" or "upload") && m.PublicContracts is not { Count: > 0 })
            throw new InvalidDataException("Integration requires pinned public business contracts");
        if (m.PublicContracts is not null)
            foreach (var contract in m.PublicContracts)
                if (!publicContracts.TryGetValue(contract.Key, out var assembly) || contract.Value != Wire.FileHash(assembly.Location))
                    throw new InvalidDataException("Missing/changed installed public contract: " + contract.Key);
        // SDK packages and system drivers are prerequisite evidence, never installer actions.
        if (m.Prerequisites.Any(x => x is null || x.Kind is not ("vendor-sdk" or "system-driver") || string.IsNullOrWhiteSpace(x.Identity) || string.IsNullOrWhiteSpace(x.Version) || string.IsNullOrWhiteSpace(x.Evidence)))
            throw new InvalidDataException("Invalid prerequisite declaration");
        return new(m, Convert.ToHexString(SHA256.HashData(manifestBytes)), packageHash, Wire.Hash(Wire.Encode(trust)), DateTimeOffset.UtcNow, publisher.TestOnly);
    }
    static void RejectDuplicateProperties(System.Text.Json.JsonElement element)
    {
        if (element.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!keys.Add(property.Name)) throw new InvalidDataException("Duplicate manifest JSON property");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == System.Text.Json.JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) RejectDuplicateProperties(child);
    }
    public static VerifiedExtension Inspect(string archive, ExtensionTrust trust, RunMode mode) => Inspect(archive, trust, mode, null);
    public static VerifiedExtension Inspect(string archive, ExtensionTrust trust, RunMode mode, Func<PackageReference, (VerifiedExtension Package, string Directory)>? resolve)
    {
        using var input = new FileStream(archive, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length > MaximumExpandedBytes) throw new InvalidDataException("Extension archive quota exceeded");
        var packageHash = Convert.ToHexString(SHA256.HashData(input)); input.Position = 0;
        using var zip = new ZipArchive(input, ZipArchiveMode.Read);
        if (zip.Entries.Count is < 3 or > MaximumFiles + 2 || zip.Entries.Any(x => !SafePath(x.FullName) ||
            ((x.ExternalAttributes >> 16) & 0xf000) is 0xa000 or 0x6000 or 0x2000 ||
            (x.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0) ||
            zip.Entries.Select(x => x.FullName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != zip.Entries.Count ||
            zip.Entries.Any(x => x.Length > MaximumFileBytes) || zip.Entries.Sum(x => x.Length) > MaximumExpandedBytes)
            throw new InvalidDataException("Unsafe ZIP paths/links/duplicates/quota");
        byte[] ReadEntry(string name, long limit)
        {
            var entry = zip.GetEntry(name) ?? throw new InvalidDataException("Missing " + name);
            using var stream = entry.Open(); return ReadBounded(stream, limit);
        }
        var verified = VerifyMetadata(ReadEntry("manifest.json", MaximumManifestBytes), ReadEntry("manifest.sig", 2048), trust, mode, packageHash);
        if (!zip.Entries.Select(x => x.FullName).Order(StringComparer.Ordinal).SequenceEqual(
            verified.Manifest.Files.Select(x => x.Path).Concat(["manifest.json", "manifest.sig"]).Order(StringComparer.Ordinal)))
            throw new InvalidDataException("ZIP must have exactly the signed file set");
        List<ManagedAssemblyPolicy.Image> images = [];
        foreach (var file in verified.Manifest.Files)
        {
            var bytes = ReadEntry(file.Path, file.Length);
            if (bytes.LongLength != file.Length || Convert.ToHexString(SHA256.HashData(bytes)) != file.Sha256.ToUpperInvariant())
                throw new InvalidDataException("Extension file hash/length mismatch: " + file.Path);
            ValidateManaged(verified.Manifest, file.Path, bytes);
            if (file.Path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) images.Add(ManagedAssemblyPolicy.Read(file.Path, bytes));
        }
        ValidateClosure(verified, images.ToArray(), resolve);
        return verified;
    }
    static void ValidateClosure(VerifiedExtension root, ManagedAssemblyPolicy.Image[] local,
        Func<PackageReference, (VerifiedExtension Package, string Directory)>? resolve)
    {
        var publicContracts = ExtensionPublicContracts.Snapshot();
        foreach (var reference in local.SelectMany(x => x.References).Where(x => x.Name != typeof(IObservationExtension).Assembly.GetName().Name && publicContracts.ContainsKey(x.Name!)))
            if (root.Manifest.PublicContracts?.ContainsKey(reference.Name!) != true)
                throw new InvalidDataException("Managed public contract reference is not pinned in the signed manifest: " + reference.Name);
        Dictionary<string, PackageReference> selected = new(StringComparer.Ordinal);
        HashSet<string> visiting = [], seen = [];
        List<ManagedAssemblyPolicy.Image> images = [];
        void Visit(PackageReference reference)
        {
            if (selected.TryGetValue(reference.Id, out var prior) && prior != reference)
                throw new InvalidDataException("Conflicting exact dependency versions: " + reference.Id);
            selected[reference.Id] = reference;
            if (visiting.Contains(reference.ManifestSha256)) throw new InvalidDataException("Cyclic package dependency");
            if (!seen.Add(reference.ManifestSha256)) return;
            visiting.Add(reference.ManifestSha256);
            var dependency = resolve?.Invoke(reference) ?? throw new InvalidDataException("Exact dependency resolver required before acceptance");
            if (dependency.Package.Reference != reference) throw new InvalidDataException("Dependency identity mismatch");
            foreach (var child in dependency.Package.Manifest.Dependencies) Visit(child);
            foreach (var file in dependency.Package.Manifest.Files.Where(x => x.Path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
                images.Add(ManagedAssemblyPolicy.Read(reference.Id + "/" + file.Path, File.ReadAllBytes(Path.Combine(dependency.Directory, file.Path))));
            visiting.Remove(reference.ManifestSha256);
        }
        selected[root.Manifest.Id] = root.Reference;
        foreach (var dependency in root.Manifest.Dependencies) Visit(dependency);
        ManagedAssemblyPolicy.Validate(local, images);
    }
    static void ValidateManaged(ExtensionManifest manifest, string path, byte[] bytes)
    {
        if (!path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) return;
        using var input = new MemoryStream(bytes); using var pe = new PEReader(input);
        if (!pe.HasMetadata || pe.PEHeaders.CorHeader is null || (pe.PEHeaders.CorHeader.Flags & CorFlags.ILOnly) == 0)
            throw new InvalidDataException("Native/mixed DLL is not an observation extension; SDK/driver installation is separate");
        var metadata = pe.GetMetadataReader();
        if (!metadata.IsAssembly || metadata.GetString(metadata.GetAssemblyDefinition().Name) == "Ipc.Platform")
            throw new InvalidDataException("Invalid/private shared contract assembly");
        if (metadata.GetString(metadata.GetAssemblyDefinition().Name) != Path.GetFileNameWithoutExtension(path))
            throw new InvalidDataException("Managed assembly identity must match its signed filename");
        foreach (var method in metadata.MethodDefinitions)
            if ((metadata.GetMethodDefinition(method).Attributes & System.Reflection.MethodAttributes.PinvokeImpl) != 0)
                throw new InvalidDataException("Native imports require a separately approved SDK boundary");
        foreach (var module in manifest.Modules.Where(x => x.Assembly == path))
            if (!metadata.TypeDefinitions.Any(h => { var t = metadata.GetTypeDefinition(h); return metadata.GetString(t.Namespace) + "." + metadata.GetString(t.Name) == module.EntryType; }))
                throw new InvalidDataException("Declared entry type missing (static metadata)");
    }
    public static void ExtractVerified(string archive, string destination, ExtensionTrust trust, RunMode mode, string expectedHash) => ExtractVerified(archive, destination, trust, mode, expectedHash, null);
    public static void ExtractVerified(string archive, string destination, ExtensionTrust trust, RunMode mode, string expectedHash, Func<PackageReference, (VerifiedExtension Package, string Directory)>? resolve)
    {
        // One read handle prevents replacing the input while it is inspected/extracted.
        using var hold = new FileStream(archive, FileMode.Open, FileAccess.Read, FileShare.Read);
        var verified = Inspect(archive, trust, mode, resolve);
        if (verified.PackageSha256 != expectedHash) throw new InvalidDataException("Package changed after installation plan");
        using var zip = new ZipArchive(hold, ZipArchiveMode.Read);
        foreach (var entry in zip.Entries)
        {
            if (!ProductValidation.IsSafeRelativeFile(destination, entry.FullName, out var target)) throw new InvalidDataException("Unsafe extraction destination");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var source = entry.Open(); using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, FileOptions.WriteThrough);
            var bytes = ReadBounded(source, entry.FullName == "manifest.json" ? MaximumManifestBytes : MaximumFileBytes);
            output.Write(bytes); output.Flush(true);
        }
        VerifyDirectory(destination, trust, mode, expectedHash, resolve);
    }
    public static VerifiedExtension VerifyDirectory(string directory, ExtensionTrust trust, RunMode mode, string packageHash = "installed") => VerifyDirectory(directory, trust, mode, packageHash, null);
    public static VerifiedExtension VerifyDirectory(string directory, ExtensionTrust trust, RunMode mode, string packageHash, Func<PackageReference, (VerifiedExtension Package, string Directory)>? resolve)
    {
        string Safe(string name) => ProductValidation.IsSafeRelativeFile(directory, name, out var p) ? p : throw new InvalidDataException("Linked or unsafe installed extension");
        var manifest = Safe("manifest.json"); var signature = Safe("manifest.sig");
        if (new FileInfo(manifest).Length > MaximumManifestBytes || new FileInfo(signature).Length > 2048) throw new InvalidDataException("Installed metadata quota");
        var verified = VerifyMetadata(File.ReadAllBytes(manifest), File.ReadAllBytes(signature), trust, mode, packageHash);
        var paths = EnumerateOrdinaryFiles(directory).ToArray();
        if (!paths.Order().SequenceEqual(verified.Manifest.Files.Select(x => x.Path).Concat(["manifest.json", "manifest.sig"]).Order())) throw new InvalidDataException("Installed file set mismatch");
        foreach (var f in verified.Manifest.Files)
        {
            var path = Safe(f.Path);
            if (new FileInfo(path).Length != f.Length || Wire.FileHash(path) != f.Sha256.ToUpperInvariant()) throw new InvalidDataException("Installed extension changed: " + f.Path);
            ValidateManaged(verified.Manifest, f.Path, File.ReadAllBytes(path));
        }
        ValidateClosure(verified, verified.Manifest.Files.Where(x => x.Path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(x => ManagedAssemblyPolicy.Read(x.Path, File.ReadAllBytes(Safe(x.Path)))).ToArray(), resolve);
        return verified;
    }
    static IEnumerable<string> EnumerateOrdinaryFiles(string root)
    {
        Stack<string> pending = new(); pending.Push(root); var count = 0;
        while (pending.TryPop(out var directory))
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                if (++count > MaximumFiles * 4 + 2) throw new InvalidDataException("Installed tree quota exceeded");
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Installed linked/reparse entry refused before traversal");
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(path);
                else yield return Path.GetRelativePath(root, path).Replace('\\', '/');
            }
    }
}

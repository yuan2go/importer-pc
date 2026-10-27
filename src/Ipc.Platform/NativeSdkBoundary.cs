using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;

namespace Ipc.Platform;

// External vendor DLLs retain their independently qualified prerequisite policy.
// The current built-in SDK is pinned by its embedded product inventory and installed
// with the protected application. Both share exact native ownership/loading checks.
public static class NativeSdkBoundary
{
    public static ExtensionPrerequisite RequireDeclaration(ExtensionManifest manifest, string library)
    {
        if (manifest.Rid != "win-x64" || !ExtensionPackages.SafePath(library) ||
            Path.GetFileName(library) != library || !library.EndsWith(".dll", StringComparison.Ordinal))
            throw new InvalidDataException("Native import needs a declared win-x64 vendor DLL basename");
        var declarations = manifest.Prerequisites.Where(p => p.Kind == "vendor-sdk" && p.Identity == library && ExtensionSchema.IsHash(p.Evidence)).ToArray();
        if (declarations.Length != 1) throw new InvalidDataException("Native import lacks a unique signed SDK declaration: " + library);
        var declaration = declarations[0];
        if (declaration.Preload is { } preload && (preload.Length > 16 || preload.Distinct(StringComparer.OrdinalIgnoreCase).Count() != preload.Length ||
            preload.Any(p => !ExtensionPackages.SafePath(p) || Path.GetFileName(p) != p || !p.EndsWith(".dll", StringComparison.Ordinal) || string.Equals(p, library, StringComparison.OrdinalIgnoreCase))))
            throw new InvalidDataException("Invalid signed native transport preload inventory");
        return declaration;
    }

    internal static void ValidateImports(ExtensionManifest manifest, MetadataReader metadata)
    {
        foreach (var handle in metadata.MethodDefinitions)
        {
            var method = metadata.GetMethodDefinition(handle);
            if ((method.Attributes & MethodAttributes.PinvokeImpl) == 0) continue;
            var import = method.GetImport();
            var declaration = RequireDeclaration(manifest, metadata.GetString(metadata.GetModuleReference(import.Module).Name));
            if (string.IsNullOrWhiteSpace(metadata.GetString(import.Name))) throw new InvalidDataException("Native entry point required: " + declaration.Identity);
        }
    }

    // Windows keeps loaded SDK modules for the process lifetime. Changing native bytes
    // requires a controlled process restart, even after an extension ALC is retired.
    static readonly object gate = new();
    static readonly Dictionary<string, (string Hash, nint Handle)> loaded = new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, FileStream> holds = new(StringComparer.OrdinalIgnoreCase);

    internal static nint Load(ExtensionManifest manifest, PackageReference package, DeploymentQualification? qualification, string library)
    {
        var declaration = RequireDeclaration(manifest, library);
        if (qualification is null || qualification.ExpiresAt <= DateTimeOffset.UtcNow)
            throw new InvalidDataException("Native SDK needs current independent deployment qualification");
        var proof = qualification.Prerequisites.SingleOrDefault(p => p.Package == package && Wire.Encode(p.Declaration) == Wire.Encode(declaration))
            ?? throw new InvalidDataException("Native SDK qualification belongs to another package/declaration");
        return LoadFiles(proof.Files, library, declaration.Preload ?? []);
    }

    // Built-in current product dependency: pinned by its assembly's embedded inventory,
    // not an independently installed extension or a fabricated deployment approval.
    public static nint LoadBundledFiles(Assembly owner, string relativeDirectory, ExtensionFile[] files, string library, string[] preload)
    {
        if (!ExtensionPackages.SafePath(relativeDirectory) || files.Length is < 1 or > 64 ||
            files.Any(f => !ExtensionPackages.SafePath(f.Path) || Path.GetFileName(f.Path) != f.Path || !ExtensionSchema.IsHash(f.Sha256) || f.Length <= 0))
            throw new InvalidDataException("Invalid bundled SDK inventory");
        var root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(owner.Location)!, relativeDirectory));
        var qualified = files.Select(f => {
            var path = Path.Combine(root, f.Path);
            if (!File.Exists(path) || new FileInfo(path).Length != f.Length) throw new InvalidDataException("Bundled SDK missing/changed: " + f.Path);
            return new QualificationFile(path, f.Sha256);
        }).ToArray();
        return LoadFiles(qualified, library, preload);
    }

    internal static nint LoadFiles(QualificationFile[] files, string library, string[] preload)
    {
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException("Vendor SDK boundary requires Windows x64");
        var dlls = files.Where(f => f.Path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (dlls.Length is < 1 or > 64 || dlls.Select(f => Path.GetFileName(f.Path)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != dlls.Length)
            throw new InvalidDataException("Native prerequisite file inventory is empty/ambiguous");
        var target = dlls.SingleOrDefault(f => Path.GetFileName(f.Path) == library) ?? throw new InvalidDataException("Qualified vendor entry DLL missing");
        var loadOrder = preload.Append(library).Select(name => dlls.SingleOrDefault(f => Path.GetFileName(f.Path) == name)
            ?? throw new InvalidDataException("Declared SDK transport lacks qualified installed bytes")).ToArray();
        var directory = Path.GetDirectoryName(target.Path)!;
        if (!Path.IsPathFullyQualified(directory) || dlls.Any(f => !string.Equals(Path.GetDirectoryName(f.Path), directory, StringComparison.OrdinalIgnoreCase)) ||
            Directory.GetFiles(directory, "*.dll").Any(f => !dlls.Any(d => string.Equals(d.Path, f, StringComparison.OrdinalIgnoreCase))))
            throw new InvalidDataException("Entire native DLL directory must be qualified together");
        lock (gate)
        {
            Dictionary<string, FileStream> acquired = new(StringComparer.OrdinalIgnoreCase);
            bool loadAttempted = false;
            try
            {
                foreach (var file in dlls)
                {
                    DeploymentQualifications.VerifyInstalledFile(file);
                    using (var process = System.Diagnostics.Process.GetCurrentProcess())
                        foreach (System.Diagnostics.ProcessModule module in process.Modules)
                            if (string.Equals(module.ModuleName, Path.GetFileName(file.Path), StringComparison.OrdinalIgnoreCase) &&
                                !string.Equals(DeploymentQualifications.FileHash(module.FileName), file.Sha256, StringComparison.OrdinalIgnoreCase))
                                throw new InvalidDataException("Another native DLL with the same basename is already loaded; restart with the qualified SDK");
                    if (loaded.TryGetValue(Path.GetFileName(file.Path), out var prior) && !string.Equals(prior.Hash, file.Sha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Native DLL version conflict; controlled process restart required");
                    if (!holds.ContainsKey(file.Path)) acquired.Add(file.Path, new(file.Path, FileMode.Open, FileAccess.Read, FileShare.Read));
                    if (!string.Equals(DeploymentQualifications.FileHash(file.Path), file.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Native bytes changed during acquisition");
                    // Separate read cursor so reconnect cannot inherit PEReader's prior position.
                    using var inspection = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    using var pe = new System.Reflection.PortableExecutable.PEReader(inspection);
                    if (pe.PEHeaders.CoffHeader.Machine != System.Reflection.PortableExecutable.Machine.Amd64 || pe.HasMetadata)
                        throw new InvalidDataException("Only native AMD64 prerequisite DLLs are supported");
                }
                // Initialize only explicitly declared transports and the entry DLL;
                // unrelated vendor tools/codecs must not be initialized speculatively.
                foreach (var file in loadOrder)
                {
                    var name = Path.GetFileName(file.Path);
                    if (!loaded.ContainsKey(name))
                    {
                        loadAttempted = true;
                        var handle = NativeLibrary.Load(file.Path, typeof(NativeSdkBoundary).Assembly,
                            DllImportSearchPath.UseDllDirectoryForDependencies | DllImportSearchPath.System32);
                        loaded.Add(name, (file.Sha256, handle));
                    }
                }
                return loaded[library].Handle;
            }
            finally
            {
                // Even a partially loaded native SDK cannot be safely hot-unloaded.
                foreach (var entry in acquired)
                    if (loadAttempted) holds.Add(entry.Key, entry.Value); else entry.Value.Dispose();
            }
        }
    }
}

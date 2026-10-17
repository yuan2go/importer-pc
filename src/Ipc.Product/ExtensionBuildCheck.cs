using Ipc.Platform;

namespace Ipc.Product;

// Build-time static acceptance reuses the exact runtime verifier. It creates no
// station/store/registration, never loads code, installs trust, or starts a source.
public static class ExtensionBuildCheck
{
    public static VerifiedExtension Inspect(string archive, string trustFile, RunMode mode, string[] dependencyArchives)
    {
        if (!Enum.IsDefined(mode) || dependencyArchives.Length > 32) throw new InvalidDataException("Invalid mode or dependency quota");
        var trust = Wire.Decode<ExtensionTrust>(File.ReadAllText(trustFile));
        var root = Path.Combine(Path.GetTempPath(), "ipc-static-extension-check-" + Wire.Id());
        Directory.CreateDirectory(root);
        Dictionary<PackageReference, (VerifiedExtension Package, string Directory)> verified = [];
        List<string> pending = dependencyArchives.Select(Path.GetFullPath).Distinct(StringComparer.Ordinal).ToList();
        (VerifiedExtension Package, string Directory) Resolve(PackageReference reference) => verified.TryGetValue(reference, out var value)
            ? value : throw new MissingDependencyException(reference.Id + "/" + reference.Version);
        Exception? verificationFailure = null;
        try
        {
            while (pending.Count > 0)
            {
                var progressed = false;
                foreach (var file in pending.ToArray())
                {
                    VerifiedExtension package;
                    try { package = ExtensionPackages.Inspect(file, trust, mode, Resolve); }
                    catch (MissingDependencyException) { continue; }
                    if (verified.Keys.Any(x => x.Id == package.Manifest.Id)) throw new InvalidDataException("Duplicate/conflicting dependency package ID");
                    var destination = Path.Combine(root, package.ManifestSha256);
                    ExtensionPackages.ExtractVerified(file, destination, trust, mode, package.PackageSha256, Resolve);
                    verified.Add(package.Reference, (package, destination));
                    pending.Remove(file); progressed = true;
                }
                if (!progressed) throw new InvalidDataException("Missing, cyclic or mismatched exact dependency archive closure");
            }
            var result = ExtensionPackages.Inspect(archive, trust, mode, Resolve);
            HashSet<PackageReference> used = [];
            void Visit(PackageReference reference)
            {
                if (!used.Add(reference)) return;
                foreach (var dependency in Resolve(reference).Package.Manifest.Dependencies) Visit(dependency);
            }
            foreach (var dependency in result.Manifest.Dependencies) Visit(dependency);
            if (!used.SetEquals(verified.Keys)) throw new InvalidDataException("Unreferenced dependency archives supplied; declare the exact closure only");
            return result;
        }
        catch (Exception ex) { verificationFailure = ex; throw; }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (Exception cleanup)
            {
                if (verificationFailure is not null) throw new AggregateException("Static extension check and cleanup failed", verificationFailure, cleanup);
                throw;
            }
        }
    }
    sealed class MissingDependencyException(string identity) : Exception("Missing exact dependency " + identity);
}

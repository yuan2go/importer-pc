using System.Security.Cryptography;
namespace Ipc.Platform;

public sealed record ExtensionInstallation(PackageReference Package, string PackageSha256, string TrustSha256,
    string OperationId, DateTimeOffset At, string Status, string Reason);
public sealed record ExtensionInstallOperation(string Id, string PackageSha256, PackageReference Package,
    string State, DateTimeOffset At, string? Error = null, string Kind = "Install");

// Filesystem transaction log. It never executes an entry point, script, MSI or SDK installer.
public sealed partial class ExtensionStore(string root, ExtensionTrust trust, RunMode mode, string leaseRoot)
{
    internal Action<string>? PhaseBoundary { get; set; }
    public string Root { get; } = Path.GetFullPath(root);
    public ExtensionTrust Trust => trust;
    public RunMode Mode => mode;
    public string DeviceLeaseRoot => Path.GetFullPath(leaseRoot);
    string FilePath(string relative)
    {
        if (!ProductValidation.IsSafeRelativeFile(Root, relative, out var path)) throw new InvalidDataException("Unsafe extension store path");
        return path;
    }
    public string PackageDirectory(PackageReference package)
    {
        ValidateReference(package); return FilePath($"packages/{package.Id}/{package.Version}/{package.ManifestSha256}");
    }
    static void ValidateReference(PackageReference p)
    {
        if (!ExtensionSchema.IsId(p.Id) || !ExtensionSchema.IsVersion(p.Version) || !ExtensionSchema.IsHash(p.ManifestSha256) || p.ManifestSha256 != p.ManifestSha256.ToUpperInvariant())
            throw new InvalidDataException("Invalid exact package reference");
    }
    public static void AtomicWrite<T>(string file, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var temporary = file + "." + Wire.Id() + ".new";
        using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, FileOptions.WriteThrough))
        { output.Write(System.Text.Encoding.UTF8.GetBytes(Wire.Encode(value))); output.Flush(true); }
        File.Move(temporary, file, true);
    }
    public ExtensionInstallation[] Inventory()
    {
        if (!Directory.Exists(Root)) return [];
        var directory = FilePath("registrations");
        if (!Directory.Exists(directory)) return [];
        return Directory.EnumerateFiles(directory, "*.json").Order().Select(path =>
        {
            var safe = FilePath("registrations/" + Path.GetFileName(path));
            if (new FileInfo(safe).Length > 16384) throw new InvalidDataException("Registration quota");
            var result = Wire.Decode<ExtensionInstallation>(File.ReadAllText(safe)); ValidateReference(result.Package); return result;
        }).ToArray();
    }
    public ExtensionInstallOperation[] Operations()
    {
        if (!Directory.Exists(Root)) return [];
        var directory = FilePath("operations");
        if (!Directory.Exists(directory)) return [];
        return Directory.EnumerateFiles(directory, "*.json").Order().Select(path =>
        {
            var safe = FilePath("operations/" + Path.GetFileName(path));
            if (new FileInfo(safe).Length > 16384) throw new InvalidDataException("Operation log quota");
            return Wire.Decode<ExtensionInstallOperation>(File.ReadAllText(safe));
        }).ToArray();
    }
    [ThreadStatic] static HashSet<string>? verifying;
    public VerifiedExtension VerifyInstalled(PackageReference package)
    {
        verifying ??= [];
        if (!verifying.Add(package.ManifestSha256)) throw new InvalidDataException("Cyclic installed package dependency");
        try { return VerifyInstalledCore(package); }
        finally { verifying.Remove(package.ManifestSha256); }
    }
    VerifiedExtension VerifyInstalledCore(PackageReference package)
    {
        var registration = Inventory().SingleOrDefault(x => x.Package == package && x.Status == "Installed")
            ?? throw new InvalidDataException("Exact extension is not installed: " + package.Id);
        var verified = ExtensionPackages.VerifyDirectory(PackageDirectory(package), trust, mode, registration.PackageSha256, ResolveDependency);
        if (verified.Reference != package) throw new InvalidDataException("Installed manifest identity mismatch");
        return verified;
    }
    public VerifiedExtension Plan(string file)
    {
        var verified = ExtensionPackages.Inspect(file, trust, mode, ResolveDependency);
        foreach (var dependency in verified.Manifest.Dependencies) VerifyInstalled(dependency);
        ValidateIdentity(verified.Reference);
        if (Inventory().Any(x => x.Package.Id == verified.Manifest.Id && x.Package.Version == verified.Manifest.Version && x.Package != verified.Reference))
            throw new InvalidDataException("Package ID/version is already bound to different signed bytes");
        return verified;
    }
    (VerifiedExtension Package, string Directory) ResolveDependency(PackageReference reference) =>
        (VerifyInstalled(reference), PackageDirectory(reference));
    void ValidateIdentity(PackageReference reference)
    {
        ValidateReference(reference);
        var retained = Inventory().Select(x => x.Package).Concat(Operations().Select(x => x.Package));
        var identityFile = Directory.Exists(Root) ? FilePath($"identities/{reference.Id}/{reference.Version}.json") : "";
        if (File.Exists(identityFile)) retained = retained.Append(Wire.Decode<PackageReference>(File.ReadAllText(identityFile)));
        if (retained.Any(x => x.Id == reference.Id && x.Version == reference.Version && x != reference))
            throw new InvalidDataException("Package ID/version is permanently bound to different signed content, including retired/interrupted history");
    }
    void ReserveIdentity(PackageReference reference)
    {
        ValidateIdentity(reference); // Called under the management lease, including recovery and repair.
        var file = FilePath($"identities/{reference.Id}/{reference.Version}.json");
        if (!File.Exists(file)) AtomicWrite(file, reference);
    }
    FileStream Mutation()
    {
        MachineRuntimePolicy.RequireExtensionInstallerAuthority();
        Directory.CreateDirectory(Root);
        return new(FilePath("management.lease"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    void Budget()
    {
        if (Inventory().Count(x => x.Status == "Installed") >= 128 || Operations().Length >= 2048)
            throw new InvalidOperationException("Extension store inventory/log quota requires maintenance review");
        long size = 0;
        foreach (var path in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
        {
            _ = FilePath(Path.GetRelativePath(Root, path).Replace('\\', '/'));
            size += new FileInfo(path).Length;
            if (size > 2L * 1024 * 1024 * 1024) throw new IOException("Extension store budget exceeded; preserve evidence and review retention");
        }
    }
    public ExtensionInstallation Install(string archive, string expectedPackageSha256)
    {
        using var mutation = Mutation(); EnsureConsumerFence(); Budget();
        var plan = Plan(archive);
        if (plan.PackageSha256 != expectedPackageSha256) throw new InvalidDataException("Installation plan/input changed");
        ReserveIdentity(plan.Reference);
        var prior = Inventory().SingleOrDefault(x => x.Package == plan.Reference && x.Status == "Installed");
        var repair = false;
        if (prior is not null)
        {
            try { VerifyInstalled(prior.Package); EnsureUsage(prior.Package); return prior; }
            catch (Exception ex) when (ex is IOException or InvalidDataException or CryptographicException or System.Text.Json.JsonException) { repair = true; }
        }
        var operation = new ExtensionInstallOperation(Wire.Id(), plan.PackageSha256, plan.Reference, "Prepared", DateTimeOffset.UtcNow, Kind: repair ? "Repair" : "Install");
        var archiveCopy = FilePath("incoming/" + operation.Id + ".ipcx"); Directory.CreateDirectory(Path.GetDirectoryName(archiveCopy)!);
        using (var input = new FileStream(archive, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var output = new FileStream(archiveCopy, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, FileOptions.WriteThrough))
        {
            if (input.Length > ExtensionPackages.MaximumExpandedBytes) throw new InvalidDataException("Archive quota");
            input.CopyTo(output); output.Flush(true);
        }
        if (Wire.FileHash(archiveCopy) != operation.PackageSha256) throw new InvalidDataException("Archive changed while taking installation custody");
        Save(operation); PhaseBoundary?.Invoke("Prepared"); return Complete(operation);
    }
    void Save(ExtensionInstallOperation operation) => AtomicWrite(FilePath("operations/" + operation.Id + ".json"), operation);
    public ExtensionInstallation Recover(string operationId, IEnumerable<PackageReference>? deploymentReferences = null, Func<PackageReference[]>? refreshReferences = null)
    {
        if (!Guid.TryParseExact(operationId, "N", out _)) throw new InvalidDataException("Original operation ID required");
        using var mutation = Mutation();
        var operation = Wire.Decode<ExtensionInstallOperation>(File.ReadAllText(FilePath("operations/" + operationId + ".json")));
        if (operation.Id != operationId || operation.State == "Uninstalled") throw new InvalidDataException("Invalid installation recovery identity");
        if (operation.Kind == "Uninstall")
        {
            if (deploymentReferences is null && refreshReferences is null) throw new InvalidOperationException("Uninstall recovery requires current deployment inspection");
            CompleteUninstall(operation, refreshReferences ?? (() => deploymentReferences!.ToArray()));
            return Inventory().Single(x => x.Package == operation.Package);
        }
        return Complete(operation);
    }
    ExtensionInstallation Complete(ExtensionInstallOperation operation)
    {
        ValidateReference(operation.Package);
        ReserveIdentity(operation.Package);
        EnsureUsage(operation.Package);
        using var repairLease = operation.Kind == "Repair" ? AcquireMutation([operation.Package]) : null;
        var archive = FilePath("incoming/" + operation.Id + ".ipcx");
        var plan = Plan(archive);
        if (plan.PackageSha256 != operation.PackageSha256 || plan.Reference != operation.Package) throw new InvalidDataException("Recovery custody mismatch");
        var stage = FilePath("staging/" + operation.Id); var destination = PackageDirectory(operation.Package);
        try
        {
            if (operation.Kind == "Repair" && Directory.Exists(destination))
            {
                try { ExtensionPackages.VerifyDirectory(destination, trust, mode, operation.PackageSha256, ResolveDependency); }
                catch (Exception ex) when (ex is IOException or InvalidDataException or CryptographicException or System.Text.Json.JsonException)
                {
                    var damaged = FilePath("damaged/" + operation.Id);
                    Directory.CreateDirectory(Path.GetDirectoryName(damaged)!); Directory.Move(destination, damaged);
                    Save(operation with { State = "DamagedFilesRetained", Error = ex.GetType().Name });
                }
            }
            if (!Directory.Exists(destination))
            {
                if (Directory.Exists(stage))
                {
                    try { ExtensionPackages.VerifyDirectory(stage, trust, mode, operation.PackageSha256, ResolveDependency); }
                    catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException or System.Security.Cryptography.CryptographicException)
                    { Directory.Move(stage, stage + ".interrupted-" + Wire.Id()); }
                }
                if (!Directory.Exists(stage))
                {
                    Directory.CreateDirectory(stage); ExtensionPackages.ExtractVerified(archive, stage, trust, mode, operation.PackageSha256, ResolveDependency);
                }
                Save(operation with { State = "FilesVerified", Error = null });
                PhaseBoundary?.Invoke("FilesVerified");
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                Directory.Move(stage, destination); // Atomic same-volume publish; no replacement.
            }
            var installed = ExtensionPackages.VerifyDirectory(destination, trust, mode, operation.PackageSha256, ResolveDependency);
            if (installed.Reference != operation.Package) throw new InvalidDataException("Published identity mismatch");
            Save(operation with { State = "Published", Error = null });
            PhaseBoundary?.Invoke("Published");
            var receipt = new ExtensionInstallation(operation.Package, operation.PackageSha256, plan.TrustSha256,
                operation.Id, DateTimeOffset.UtcNow, "Installed", "Installed only; not activated. Offline signature/trust checked; no online revocation claim.");
            AtomicWrite(FilePath("registrations/" + operation.Package.ManifestSha256 + ".json"), receipt);
            PhaseBoundary?.Invoke("Registered");
            Save(operation with { State = "Registered", Error = null }); return receipt;
        }
        catch (Exception ex)
        {
            Save(operation with { State = "RecoveryRequired", Error = ex.GetType().Name + ": " + ex.Message }); throw;
        }
    }
    public void Uninstall(PackageReference package, IEnumerable<PackageReference> deploymentReferences)
        => Uninstall(package, () => deploymentReferences.ToArray());
    public void Uninstall(PackageReference package, Func<PackageReference[]> refreshReferences)
    {
        using var mutation = Mutation();
        var installed = VerifyInstalled(package);
        var operation = new ExtensionInstallOperation(Wire.Id(), installed.PackageSha256, package, "UninstallPrepared", DateTimeOffset.UtcNow, Kind: "Uninstall");
        Save(operation);
        try { CompleteUninstall(operation, refreshReferences); }
        catch (Exception ex) { Save(operation with { State = "UninstallRecoveryRequired", Error = ex.GetType().Name + ": " + ex.Message }); throw; }
    }
    void CompleteUninstall(ExtensionInstallOperation operation, Func<PackageReference[]> refreshReferences)
    {
        var package = operation.Package;
        if (File.GetAttributes(ConsumerFence).HasFlag(FileAttributes.ReparsePoint)) throw new InvalidDataException("Linked consumer fence refused");
        using var consumers = new FileStream(ConsumerFence, FileMode.Open, FileAccess.Read, FileShare.None);
        using var consumerReader = new StreamReader(consumers, leaveOpen: true);
        var coverage = consumerReader.ReadToEnd();
        using var use = AcquireMutation([package]); // Must precede the fresh DB reference snapshot.
        if (coverage.StartsWith("LegacyConsumersUnverified", StringComparison.Ordinal))
            throw new InvalidOperationException("Offline consumer coverage is incomplete for this legacy store; retain exact packages and inventory every original data root before approved migration");
        PhaseBoundary?.Invoke("UninstallLocked");
        var deploymentReferences = refreshReferences();
        if (deploymentReferences.Contains(package)) throw new InvalidOperationException("Effective/pending deployment or original request still references this package");
        foreach (var other in Inventory().Where(x => x.Status == "Installed" && x.Package != package))
            if (VerifyInstalled(other.Package).Manifest.Dependencies.Contains(package)) throw new InvalidOperationException("Reverse package dependency prevents uninstall: " + other.Package.Id);
        var directory = PackageDirectory(package); var retained = FilePath("retired/" + operation.Id);
        try
        {
            if (Directory.Exists(directory))
            {
                var verified = ExtensionPackages.VerifyDirectory(directory, trust, mode, operation.PackageSha256, ResolveDependency);
                if (verified.Reference != package) throw new InvalidDataException("Uninstall package identity changed");
                Directory.CreateDirectory(Path.GetDirectoryName(retained)!); Directory.Move(directory, retained);
            }
            else if (!Directory.Exists(retained)) throw new DirectoryNotFoundException("Original and retired package both missing; inspect before recovery");
            var retired = ExtensionPackages.VerifyDirectory(retained, trust, mode, operation.PackageSha256, ResolveDependency);
            if (retired.Reference != package) throw new InvalidDataException("Retired package identity changed");
            Save(operation with { State = "UninstallRetired", Error = null }); PhaseBoundary?.Invoke("UninstallRetired");
            var previous = Inventory().Single(x => x.Package == package);
            AtomicWrite(FilePath("registrations/" + package.ManifestSha256 + ".json"), previous with { Status = "Uninstalled", Reason = "Disabled/unbound; files retired for recovery, audit retained", At = DateTimeOffset.UtcNow });
            Save(operation with { State = "Uninstalled", Error = null });
        }
        catch (Exception ex) { Save(operation with { State = "UninstallRecoveryRequired", Error = ex.GetType().Name + ": " + ex.Message }); throw; }
    }
    string UsagePath(PackageReference package)
    { ValidateReference(package); return FilePath("usage/" + package.ManifestSha256 + ".lease"); }
    void EnsureUsage(PackageReference package)
    {
        var path = UsagePath(package); if (File.Exists(path)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
        file.Write(System.Text.Encoding.UTF8.GetBytes(Wire.Encode(package))); file.Flush(true);
    }
    public PackageUseLease AcquireUse(IEnumerable<PackageReference> packages) => new(packages.Select(UsagePath), exclusive: false);
    PackageUseLease AcquireMutation(IEnumerable<PackageReference> packages) => new(packages.Select(UsagePath), exclusive: true);
}

using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;

namespace Ipc.Platform;

public sealed record WindowsDeploymentRequest(string MediaRoot, string ApplicationId, string SourceCommit,
    string RuntimeAccountSid, string DataRoot, string ConfigurationSource, RunMode Mode,
    string? TrustFile, string? TrustSha256, Dictionary<string, string> Packages, bool RepairSeed = false,
    Dictionary<string, string>? ConfigurationHashes = null);

public static class WindowsDeploymentSetup
{
    public static string MachineRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ImporterPc");
    public static string Registration(string application) => Path.Combine(MachineRoot, "deployments", application + ".json");
    public static DeploymentLocation? Current(string application)
    {
        var file = Registration(application);
        if (!File.Exists(file)) return null;
        MachineRuntimePolicy.RequireProtectedEvidence(file);
        var current = Wire.Decode<DeploymentLocation>(File.ReadAllText(file));
        if (current.ApplicationId != application) throw new InvalidDataException("Existing deployment identity differs");
        return current;
    }
    public static ExtensionTrust ReadTrust(string path, RunMode mode)
    {
        DeploymentPaths.RequireOrdinaryAncestors(path);
        if (new FileInfo(path).Length > ExtensionPackages.MaximumManifestBytes) throw new InvalidDataException("Trust policy quota exceeded");
        var text = File.ReadAllText(path);
        if (text.Contains("PRIVATE KEY", StringComparison.Ordinal)) throw new InvalidDataException("Private keys must not be deployed");
        var trust = Wire.Decode<ExtensionTrust>(text);
        if (trust.SchemaVersion != 1 || trust.Publishers is not { Length: > 0 } || trust.Publishers.Length > 128 ||
            trust.Publishers.Select(p => p.Id).Distinct().Count() != trust.Publishers.Length) throw new InvalidDataException("Invalid independent trust policy");
        foreach (var publisher in trust.Publishers)
        {
            if (!ExtensionSchema.IsId(publisher.Id) || mode == RunMode.Production && publisher.TestOnly || !publisher.PublicKeyPem.StartsWith("-----BEGIN PUBLIC KEY-----", StringComparison.Ordinal))
                throw new InvalidDataException("Invalid publisher or Production test policy");
            using var rsa = RSA.Create(); rsa.ImportFromPem(publisher.PublicKeyPem);
            if (rsa.KeySize < 3072) throw new InvalidDataException("Publisher RSA key must be at least 3072 bits");
        }
        return trust;
    }
    [SupportedOSPlatform("windows")]
    public static DeploymentLocation Install(WindowsDeploymentRequest request, Action<WindowsDeploymentRequest> validateConfiguration,
        Action<WindowsDeploymentRequest> installPackages, Action<DeploymentLocation> createShortcuts)
    {
        MachineRuntimePolicy.RequireExtensionInstallerAuthority();
        var media = DeploymentMedia.Inspect(request.MediaRoot, request.ApplicationId);
        if (media.SourceCommit != request.SourceCommit) throw new InvalidDataException("Selected media changed");
        validateConfiguration(request);
        var sourceFiles = ConfigurationFiles(request.ConfigurationSource);
        if (request.ConfigurationHashes is null || !sourceFiles.Select(Path.GetFileName).Order().SequenceEqual(request.ConfigurationHashes.Keys.Order()) ||
            sourceFiles.Any(path => !request.ConfigurationHashes.TryGetValue(Path.GetFileName(path), out var hash) || Wire.FileHash(path) != hash))
            throw new InvalidDataException("Selected seed configuration changed after review; read and review it again");
        var sid = new SecurityIdentifier(request.RuntimeAccountSid);
        if (!sid.IsAccountSid()) throw new InvalidDataException("Choose an existing Windows account");
        _ = sid.Translate(typeof(NTAccount));
        var data = Path.GetFullPath(request.DataRoot); DeploymentPaths.RequireOrdinaryAncestors(data);
        var vendor = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ImporterPc");
        if (data.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Data must be outside Program Files");
        if (request.TrustFile is { } trust)
        {
            if (Wire.FileHash(trust) != request.TrustSha256) throw new InvalidDataException("Selected independent trust policy changed");
            _ = ReadTrust(trust, request.Mode);
        }
        MachineRuntimeSetup.InstallForAccount(request.RuntimeAccountSid);
        ProtectedDirectory(vendor, sid); ProtectedDirectory(Path.Combine(vendor, "releases"), sid);
        ProtectedDirectory(Path.Combine(MachineRoot, "deployments"), sid);
        using var setup = new FileStream(Path.Combine(MachineRoot, "deployments", request.ApplicationId + ".lease"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var before = Current(request.ApplicationId);
        var operation = Path.Combine(MachineRoot, "deployments", request.ApplicationId + ".setup-" + Wire.Id() + ".json");
        var releaseRoot = Path.Combine(vendor, "releases", request.ApplicationId + "-" + media.SourceCommit[..12] + "-" + Wire.Id());
        try
        {
            WriteProtected(operation, new { Status = "Prepared", request.SourceCommit, Previous = before, ReleaseRoot = releaseRoot, DataRoot = data }, sid);
            media.CopyTo(releaseRoot, root => ProtectedDirectory(root, sid), file => new FileInfo(file).SetAccessControl(MachineRuntimeSetup.FileAcl(sid, FileSystemRights.ReadAndExecute)));
            ProtectedDirectory(Path.Combine(MachineRoot, "configuration"), sid);
            var config = request.RepairSeed ? Path.Combine(MachineRoot, "configuration", request.ApplicationId + "-" + Wire.Id()) :
                before?.ConfigurationDirectory ?? Path.Combine(MachineRoot, "configuration", request.ApplicationId);
            if (!Directory.Exists(config))
            {
                // Publish a complete seed directory once; upgrades retain it byte for byte.
                var temporary = config + "." + Wire.Id() + ".new"; ProtectedDirectory(temporary, sid);
                foreach (var source in ConfigurationFiles(request.ConfigurationSource))
                {
                    var target = Path.Combine(temporary, Path.GetFileName(source)); File.Copy(source, target, false);
                    new FileInfo(target).SetAccessControl(MachineRuntimeSetup.FileAcl(sid));
                    if (Wire.FileHash(target) != request.ConfigurationHashes[Path.GetFileName(source)]) throw new InvalidDataException("Configuration source changed while copying");
                }
                Directory.Move(temporary, config);
            }
            else { MachineRuntimePolicy.RequireProtectedEvidence(Path.Combine(config, "business.json")); validateConfiguration(request with { ConfigurationSource = config }); }
            if (data.StartsWith(MachineRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) ProtectedDirectory(Path.Combine(MachineRoot, "data"), sid);
            if (!Directory.Exists(data)) new DirectoryInfo(data).Create(MachineRuntimeSetup.DirectoryAcl(sid, FileSystemRights.Modify));
            if (request.TrustFile is not null) ProvisionTrust(request, sid);
            installPackages(request);
            var location = new DeploymentLocation(request.ApplicationId, media.SourceCommit, releaseRoot, config, data, request.RuntimeAccountSid);
            WriteProtected(Path.Combine(releaseRoot, "deployment-location.json"), location, sid);
            MachineRuntimePolicy.RequireProtectedApplicationInstaller(Path.Combine(releaseRoot, "app"), ["Ipc.Assembly.exe", "Ipc.Assembly.dll", "Ipc.Platform.dll"]);
            // The new shortcut is version-specific; existing shortcut remains usable on any failure.
            createShortcuts(location);
            WriteProtected(operation, new { Status = "ReadyToCommit", Location = location, Previous = before, BusinessStarted = false }, sid);
            WriteProtected(Registration(request.ApplicationId), location, sid); // Last authoritative deployment commit; no fallible mutation follows.
            return location;
        }
        catch (Exception error)
        {
            WriteProtected(operation, new { Status = "Failed", error.Message, Previous = before, ReleaseRoot = releaseRoot, DataRoot = data,
                Retained = "Previous deployment, seed, database and leases retained; retry creates another side-by-side release" }, sid);
            throw;
        }
    }
    [SupportedOSPlatform("windows")]
    static void WriteProtected<T>(string path, T value, SecurityIdentifier sid)
    {
        DeploymentPaths.RequireOrdinaryAncestors(path);
        var temporary = path + "." + Wire.Id() + ".new";
        using (var output = new FileInfo(temporary).Create(FileMode.CreateNew, FileSystemRights.FullControl, FileShare.None, 4096, FileOptions.WriteThrough, MachineRuntimeSetup.FileAcl(sid)))
        { output.Write(System.Text.Encoding.UTF8.GetBytes(Wire.Encode(value))); output.Flush(true); }
        File.Move(temporary, path, true);
    }
    public static string[] ConfigurationFiles(string root)
    {
        root = Path.GetFullPath(root); DeploymentPaths.RequireOrdinaryAncestors(root);
        var files = Directory.GetFiles(root);
        if (!files.Any(f => Path.GetFileName(f) == "business.json") || files.Length > 128 || Directory.GetDirectories(root).Length != 0)
            throw new InvalidDataException("Choose a flat Application configuration directory containing business.json");
        foreach (var path in files)
        {
            DeploymentPaths.RequireOrdinaryAncestors(path);
            if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || new FileInfo(path).Length > 4 * 1024 * 1024 || Path.GetFileName(path) == "extension-store.json")
                throw new InvalidDataException("Configuration contains unapproved file or portable extension overrides: " + path);
        }
        return files;
    }
    [SupportedOSPlatform("windows")]
    static void ProtectedDirectory(string path, SecurityIdentifier sid)
    {
        DeploymentPaths.RequireOrdinaryAncestors(path);
        if (Directory.Exists(path)) { MachineRuntimePolicy.RequireProtectedAcl(new DirectoryInfo(path), null); return; }
        new DirectoryInfo(path).Create(MachineRuntimeSetup.DirectoryAcl(sid, FileSystemRights.ReadAndExecute));
        MachineRuntimePolicy.RequireProtectedAcl(new DirectoryInfo(path), null);
    }
    [SupportedOSPlatform("windows")]
    static void ProvisionTrust(WindowsDeploymentRequest request, SecurityIdentifier sid)
    {
        var target = Path.Combine(MachineRoot, "extension-trust." + request.Mode + ".json");
        if (File.Exists(target))
        {
            MachineRuntimePolicy.RequireProtectedEvidence(target);
            if (Wire.FileHash(target) != request.TrustSha256) throw new InvalidDataException("Existing trust retained; trust rotation needs a separately reviewed policy");
            return;
        }
        ProtectedDirectory(Path.Combine(MachineRoot, "extensions"), sid);
        var store = Path.Combine(MachineRoot, "extensions", request.Mode.ToString()); ProtectedDirectory(store, sid);
        ProtectedDirectory(Path.Combine(store, "usage"), sid);
        var fence = Path.Combine(store, "usage", "consumers.lease");
        if (!File.Exists(fence))
        {
            var legacy = Directory.Exists(Path.Combine(store, "registrations")) && Directory.EnumerateFiles(Path.Combine(store, "registrations")).Any();
            File.WriteAllText(fence, legacy ? "LegacyConsumersUnverified: offline consumer scope predates registration; uninstall refused" : "Extension consumer registration and inspection fence v1");
        }
        var temporary = target + "." + Wire.Id() + ".new";
        File.Copy(request.TrustFile!, temporary, false);
        new FileInfo(temporary).SetAccessControl(MachineRuntimeSetup.FileAcl(sid));
        if (Wire.FileHash(temporary) != request.TrustSha256) throw new InvalidDataException("Trust bytes changed");
        File.Move(temporary, target, false);
    }
}

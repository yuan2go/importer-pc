using System.Security.AccessControl;
using System.Security.Principal;

namespace Ipc.Platform;

// Installed by a Windows maintainer, outside both presentation preferences and per-user
// data. The first reference deployment supports exactly one designated runtime account.
public sealed record MachineRuntimePolicy(int SchemaVersion, string RuntimeAccountSid, string LeaseRoot)
{
    public static (string Root, string TrustFile) ExtensionPaths(RunMode mode, string? requestedRoot = null, string? requestedTrust = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            var root = requestedRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ImporterPc", "extensions", mode.ToString());
            return (Path.GetFullPath(root), requestedTrust ?? Path.Combine(root, "trust.json"));
        }
        if (requestedRoot is not null || requestedTrust is not null) throw new InvalidDataException("Windows extensions require protected machine paths; user overrides forbidden");
        var machine = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ImporterPc");
        var directory = Path.Combine(machine, "extensions", mode.ToString());
        var trust = Path.Combine(machine, "extension-trust." + mode + ".json");
        foreach (var path in new[] { machine, Path.Combine(machine, "extensions"), directory })
        {
            if (!Directory.Exists(path)) throw new InvalidDataException("Extension directory must be provisioned by maintenance");
            RejectLinks(path); RequireProtectedAcl(new DirectoryInfo(path), null);
        }
        if (!File.Exists(trust)) throw new InvalidDataException("Independent offline trust policy not provisioned");
        RejectLinks(trust); RequireProtectedAcl(new FileInfo(trust), null);
        return (directory, trust);
    }
    public static void RequireExtensionInstallerAuthority()
    {
        if (!OperatingSystem.IsWindows()) return; // Explicit development/test paths only on portable hosts.
        using var identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            throw new UnauthorizedAccessException("Extension writes require a separate elevated installer; ordinary runtime is read-only");
    }
    public static string InstallerLeaseRoot(string portableRoot)
    {
        if (!OperatingSystem.IsWindows()) return portableRoot;
        RequireExtensionInstallerAuthority();
        var machine = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ImporterPc");
        var file = Path.Combine(machine, "runtime-policy.json");
        RejectLinks(machine); RejectLinks(file); RequireProtectedAcl(new DirectoryInfo(machine), null); RequireProtectedAcl(new FileInfo(file), null);
        var policy = Wire.Decode<MachineRuntimePolicy>(File.ReadAllText(file));
        var leases = Path.Combine(machine, "leases");
        if (policy.SchemaVersion != 1 || Path.GetFullPath(policy.LeaseRoot) != Path.GetFullPath(leases)) throw new InvalidDataException("Installer machine lease policy mismatch");
        RejectLinks(leases); RequireProtectedAcl(new DirectoryInfo(leases), policy.RuntimeAccountSid);
        return leases;
    }
    public static void RequireProtectedInstaller(string installRoot) => RequireProtectedApplicationInstaller(installRoot,
        ["Ipc.Product.exe", "Ipc.Product.dll", "Ipc.Platform.dll", "Ipc.Supply.Contracts.dll", "Ipc.Supply.dll", "Ipc.Adapters.dll"]);
    public static void RequireProtectedApplicationInstaller(string installRoot, IReadOnlyList<string> requiredFiles)
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.GetFullPath(installRoot).TrimEnd(Path.DirectorySeparatorChar);
        var programFiles = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)).TrimEnd(Path.DirectorySeparatorChar);
        var vendor = Path.Combine(programFiles, "ImporterPc");
        if (!root.StartsWith(vendor + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Elevated extension helper must come from a maintenance-protected Program Files/ImporterPc release");
        for (var current = new DirectoryInfo(root); current is not null && !string.Equals(current.FullName, programFiles, StringComparison.OrdinalIgnoreCase); current = current.Parent)
        { RejectLinks(current.FullName); RequireProtectedAcl(current, null); }
        foreach (var name in requiredFiles)
        {
            if (!ProductValidation.IsSafeRelativeFile(root, name, out var path)) throw new InvalidDataException("Unsafe protected installer dependency");
            RejectLinks(path); RequireProtectedAcl(new FileInfo(path), null);
        }
    }
    public static string ResolveLeaseRoot(string? requestedRoot)
    {
        if (!OperatingSystem.IsWindows())
            return requestedRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ImporterPc", "leases");
        var machine = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ImporterPc");
        var policyFile = Path.Combine(machine, "runtime-policy.json");
        if (!Directory.Exists(machine) || !File.Exists(policyFile))
            throw new InvalidDataException("Windows deployment requires a maintainer-installed machine runtime policy and designated runtime account");
        RejectLinks(machine); RejectLinks(policyFile);
        RequireProtectedAcl(new DirectoryInfo(machine), null);
        RequireProtectedAcl(new FileInfo(policyFile), null);
        var policy = Wire.Decode<MachineRuntimePolicy>(File.ReadAllText(policyFile));
        var fixedRoot = Path.GetFullPath(Path.Combine(machine, "leases"));
        if (policy.SchemaVersion != 1 || string.IsNullOrWhiteSpace(policy.RuntimeAccountSid) ||
            !string.Equals(Path.GetFullPath(policy.LeaseRoot), fixedRoot, StringComparison.OrdinalIgnoreCase) ||
            requestedRoot is not null && !string.Equals(Path.GetFullPath(requestedRoot), fixedRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Machine ownership policy/lease root invalid; per-user overrides are not permitted on Windows");
        using var identity = WindowsIdentity.GetCurrent();
        if (identity.User?.Value != policy.RuntimeAccountSid)
            throw new UnauthorizedAccessException("This Windows account is not the designated station runtime account");
        if (!Directory.Exists(fixedRoot)) throw new InvalidDataException("Machine lease directory must be provisioned by a maintainer");
        RejectLinks(fixedRoot); RequireProtectedAcl(new DirectoryInfo(fixedRoot), policy.RuntimeAccountSid);
        return fixedRoot;
    }
    public static void RequireProtectedEvidence(string path)
    {
        if (!OperatingSystem.IsWindows()) return;
        RejectLinks(path); RequireProtectedAcl(new FileInfo(path), null);
        var roots = new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData) };
        var boundary = roots.FirstOrDefault(root => Path.GetFullPath(path).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            ?? throw new UnauthorizedAccessException("Qualification evidence requires a maintenance protected Program Files or ProgramData location");
        for (var current = new FileInfo(path).Directory; current is not null && !string.Equals(current.FullName, boundary, StringComparison.OrdinalIgnoreCase); current = current.Parent)
        { RejectLinks(current.FullName); RequireProtectedAcl(current, null); }
    }
    static void RejectLinks(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Machine ownership policy and lease paths must not be reparse points");
    }
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    static void RequireProtectedAcl(FileSystemInfo item, string? writerSid)
    {
        FileSystemSecurity acl = item is DirectoryInfo directory
            ? directory.GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner)
            : ((FileInfo)item).GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);
        const string system = "S-1-5-18", administrators = "S-1-5-32-544";
        var owner = ((SecurityIdentifier)acl.GetOwner(typeof(SecurityIdentifier))!).Value;
        if (owner is not (system or administrators)) throw new UnauthorizedAccessException("Machine policy/lease directory requires a maintenance owner");
        const FileSystemRights mutation = FileSystemRights.Write | FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles |
            FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;
        foreach (FileSystemAccessRule rule in acl.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            var sid = ((SecurityIdentifier)rule.IdentityReference).Value;
            if (rule.AccessControlType == AccessControlType.Allow && (rule.FileSystemRights & mutation) != 0 &&
                sid is not (system or administrators) && sid != writerSid)
                throw new UnauthorizedAccessException("Machine policy/lease ACL grants write access outside the designated runtime account/maintenance principals");
        }
    }
}

using System.Security.AccessControl;
using System.Security.Principal;

namespace Ipc.Platform;

// Installed by a Windows maintainer, outside both presentation preferences and per-user
// data. The first reference deployment supports exactly one designated runtime account.
public sealed record MachineRuntimePolicy(int SchemaVersion, string RuntimeAccountSid, string LeaseRoot)
{
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

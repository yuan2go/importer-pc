using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace Ipc.Platform;

// Installation is an executable-entry responsibility. Runtime/API guards remain read-only.
public static class MachineRuntimeSetup
{
    public static void EnsureRuntimeIdentity(Action<string> install)
    {
        if (!OperatingSystem.IsWindows()) return;
        Prepare(CurrentRuntimeSid, PolicyExists, install, MachineRuntimePolicy.RequireRuntimeIdentity);
    }

    internal static void Prepare(Func<string> caller, Func<bool> installed, Action<string> install, Action verify)
    {
        var originalSid = caller(); // Validate the original unelevated identity before requesting UAC.
        if (!installed()) install(originalSid);
        verify(); // A child exit code alone never authorizes runtime or data access.
    }

    [SupportedOSPlatform("windows")]
    static string CurrentRuntimeSid()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User?.Value;
        MachineRuntimePolicy.ValidateRuntimeAccount(sid, sid, true, identity.UserClaims);
        return sid!;
    }

    [SupportedOSPlatform("windows")]
    static string MachineRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ImporterPc");

    [SupportedOSPlatform("windows")]
    static bool PolicyExists()
    {
        try { _ = File.GetAttributes(Path.Combine(MachineRoot, "runtime-policy.json")); return true; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
        // Unreadable existing material is an error, never a reason to replace it.
    }

    public static void InstallForAccount(string runtimeSid)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Machine setup requires Windows");
        MachineRuntimePolicy.RequireExtensionInstallerAuthority();
        InstallWindows(MachineRoot, runtimeSid);
    }

    // The internal root is only for isolated Windows verification; no executable accepts a root override.
    [SupportedOSPlatform("windows")]
    internal static void InstallWindows(string machine, string runtimeSid)
    {
        var account = new SecurityIdentifier(runtimeSid);
        if (!account.IsAccountSid()) throw new UnauthorizedAccessException("Runtime setup requires a Windows user account SID");
        _ = account.Translate(typeof(NTAccount));
        var parent = new DirectoryInfo(machine).Parent ?? throw new InvalidDataException("Machine setup parent missing");
        for (var current = parent; current is not null; current = current.Parent)
            MachineRuntimePolicy.RejectLinks(current.FullName);
        var root = new DirectoryInfo(machine);
        try { MachineRuntimePolicy.RejectLinks(machine); }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
        root.Create(DirectoryAcl(account, FileSystemRights.ReadAndExecute));
        MachineRuntimePolicy.RejectLinks(machine);
        MachineRuntimePolicy.RequireProtectedAcl(root, null);

        var lockPath = Path.Combine(machine, ".runtime-setup.lock");
        if (File.Exists(lockPath))
        {
            MachineRuntimePolicy.RejectLinks(lockPath);
            MachineRuntimePolicy.RequireProtectedAcl(new FileInfo(lockPath), null);
        }
        // Kept on disk: deleting a lock pathname could permit a second lock identity.
        using var setupLock = AcquireSetupLock(lockPath, account);
        var file = Path.Combine(machine, "runtime-policy.json");
        var leases = Path.Combine(machine, "leases");
        if (File.Exists(file))
        {
            ValidateInstalled(machine, runtimeSid);
            return; // Never rewrite an installed policy, even when invoked explicitly.
        }
        foreach (var entry in root.EnumerateFileSystemInfos())
        {
            MachineRuntimePolicy.RejectLinks(entry.FullName);
            if (entry.Name is "leases" or ".runtime-setup.lock") continue;
            if (entry is FileInfo && entry.Name.StartsWith(".runtime-policy-", StringComparison.Ordinal) && entry.Extension == ".tmp")
            {
                MachineRuntimePolicy.RequireProtectedAcl(entry, null);
                continue; // Retain interrupted, unpublished installation output.
            }
            throw new InvalidDataException("Existing machine material retained; automatic setup cannot replace a deployment");
        }
        var leaseDirectory = new DirectoryInfo(leases);
        if (leaseDirectory.Exists)
        {
            MachineRuntimePolicy.RequireProtectedAcl(leaseDirectory, runtimeSid);
            if (leaseDirectory.EnumerateFileSystemInfos().Any())
                throw new InvalidDataException("Existing leases retained; restore the original policy through maintenance");
        }
        leaseDirectory.Create(DirectoryAcl(account, FileSystemRights.Modify));
        MachineRuntimePolicy.RejectLinks(leases);
        MachineRuntimePolicy.RequireProtectedAcl(leaseDirectory, runtimeSid);

        var temporary = Path.Combine(machine, ".runtime-policy-" + Guid.NewGuid().ToString("N") + ".tmp");
        using (var stream = new FileInfo(temporary).Create(FileMode.CreateNew, FileSystemRights.FullControl,
            FileShare.None, 4096, FileOptions.None, FileAcl(account)))
        {
            var bytes = Encoding.UTF8.GetBytes(Wire.Encode(new MachineRuntimePolicy(1, runtimeSid, leases)));
            stream.Write(bytes); stream.Flush(true);
        }
        MachineRuntimePolicy.RequireProtectedAcl(new FileInfo(temporary), null);
        File.Move(temporary, file, overwrite: false);
        ValidateInstalled(machine, runtimeSid);
    }

    [SupportedOSPlatform("windows")]
    static FileStream AcquireSetupLock(string path, SecurityIdentifier account)
    {
        // Another installer may be completing the same first launch. Wait for its handle,
        // then re-read the final policy; sharing violations alone are retryable.
        for (var attempt = 0; ; attempt++)
        {
            try { return new FileInfo(path).Create(FileMode.OpenOrCreate, FileSystemRights.FullControl,
                FileShare.None, 4096, FileOptions.None, FileAcl(account)); }
            catch (IOException ex) when ((ex.HResult & 0xffff) == 32 && attempt < 100) { Thread.Sleep(100); }
        }
    }

    [SupportedOSPlatform("windows")]
    static void ValidateInstalled(string machine, string runtimeSid)
    {
        var file = Path.Combine(machine, "runtime-policy.json");
        MachineRuntimePolicy.RejectLinks(file);
        MachineRuntimePolicy.RequireProtectedAcl(new FileInfo(file), null);
        var policy = Wire.Decode<MachineRuntimePolicy>(File.ReadAllText(file));
        var leases = Path.Combine(machine, "leases");
        if (policy.SchemaVersion != 1 || policy.RuntimeAccountSid != runtimeSid ||
            !string.Equals(Path.GetFullPath(policy.LeaseRoot), Path.GetFullPath(leases), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Existing machine policy belongs to another account or has invalid paths; retained unchanged");
        MachineRuntimePolicy.RejectLinks(leases);
        MachineRuntimePolicy.RequireProtectedAcl(new DirectoryInfo(leases), runtimeSid);
    }

    [SupportedOSPlatform("windows")]
    static DirectorySecurity DirectoryAcl(SecurityIdentifier account, FileSystemRights rights)
    {
        var acl = new DirectorySecurity();
        acl.SetAccessRuleProtection(true, false);
        var administrators = new SecurityIdentifier("S-1-5-32-544");
        acl.SetOwner(administrators);
        var inheritance = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        foreach (var sid in new[] { administrators, new SecurityIdentifier("S-1-5-18") })
            acl.AddAccessRule(new(sid, FileSystemRights.FullControl, inheritance, PropagationFlags.None, AccessControlType.Allow));
        acl.AddAccessRule(new(account, rights, inheritance, PropagationFlags.None, AccessControlType.Allow));
        return acl;
    }

    [SupportedOSPlatform("windows")]
    static FileSecurity FileAcl(SecurityIdentifier account)
    {
        var acl = new FileSecurity();
        acl.SetAccessRuleProtection(true, false);
        var administrators = new SecurityIdentifier("S-1-5-32-544");
        acl.SetOwner(administrators);
        foreach (var sid in new[] { administrators, new SecurityIdentifier("S-1-5-18") })
            acl.AddAccessRule(new(sid, FileSystemRights.FullControl, AccessControlType.Allow));
        acl.AddAccessRule(new(account, FileSystemRights.Read, AccessControlType.Allow));
        return acl;
    }
}

using Microsoft.Win32;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Ipc.Platform;

// Machine installation choice, never a per-user/environment override of authority.
public static class WindowsStoragePaths
{
    // Only the isolated verifier supplies an OS-user-directory substitute.
    internal static Func<string>? UserRootForVerification = null;
    public static string DefaultProtectedAnchor => InstallationIdentity.ProtectedAnchor;
    public static string CanonicalDirectory(string path)
    {
        var full = Path.GetFullPath(path);
        if (!OperatingSystem.IsWindows() || !Directory.Exists(full)) return full;
        using var directory = CreateFile(full, 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
        if (directory.IsInvalid) { var error = new Win32Exception(Marshal.GetLastWin32Error()); error.Data["WindowsApi"] = "CreateFileW(directory)"; throw error; }
        var name = new System.Text.StringBuilder(32768); var size = GetFinalPathNameByHandle(directory, name, name.Capacity, 0);
        if (size == 0 || size >= name.Capacity) { var error = new Win32Exception(Marshal.GetLastWin32Error()); error.Data["WindowsApi"] = "GetFinalPathNameByHandleW"; throw error; }
        var value = name.ToString(); return value.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase) ? @"\\" + value[8..] : value.StartsWith(@"\\?\", StringComparison.Ordinal) ? value[4..] : value;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern uint GetFinalPathNameByHandle(SafeFileHandle file, System.Text.StringBuilder path, int size, uint flags);
    public static string? ProtectedAnchor
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return null;
            // A clean workstation has no HKLM registration. Reading the built-in
            // location stays read-only; the elevated installer creates its ACLs.
            // Invalid existing registrations still fail in ReadAnchor, never fall back.
            return ProtectedAnchorFor(InstallationIdentity.IsTest);
        }
    }
    [SupportedOSPlatform("windows")]
    internal static string ProtectedAnchorFor(bool test) => ReadAnchor(test) ?? (test ? InstallationIdentity.TestProtectedAnchor : @"D:\ImporterPc");
    [SupportedOSPlatform("windows")]
    static string? ReadAnchor(bool test)
    {
        using var registry = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = registry.OpenSubKey(test ? @"SOFTWARE\ImporterPc.Test\Storage" : @"SOFTWARE\ImporterPc\Storage");
        if (key is null) return null;
        var security = key.GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);
        if (((SecurityIdentifier)security.GetOwner(typeof(SecurityIdentifier))!).Value is not ("S-1-5-18" or "S-1-5-32-544")) throw new UnauthorizedAccessException("存储路径登记须由安装管理员持有。");
        const RegistryRights mutation = RegistryRights.SetValue | RegistryRights.CreateSubKey | RegistryRights.Delete | RegistryRights.ChangePermissions | RegistryRights.TakeOwnership;
        foreach (RegistryAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            if (rule.AccessControlType == AccessControlType.Allow && (rule.RegistryRights & mutation) != 0 && ((SecurityIdentifier)rule.IdentityReference).Value is not ("S-1-5-18" or "S-1-5-32-544")) throw new UnauthorizedAccessException("存储路径登记可被非安装管理员修改。");
        if (key.GetValue("Root", null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not string value || !Path.IsPathFullyQualified(value) || value.StartsWith(@"\\", StringComparison.Ordinal)) throw new InvalidDataException("受保护存储路径登记无效。");
        var root = Path.GetFullPath(value).TrimEnd(Path.DirectorySeparatorChar);
        if (test && !root.Equals(InstallationIdentity.TestProtectedAnchor, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("测试版受保护存储须使用独立的测试目录。");
        if (!test) InstallationIdentity.RequireOutsideTestStorage(root);
        if (root == Path.GetPathRoot(root)?.TrimEnd(Path.DirectorySeparatorChar) || new DriveInfo(Path.GetPathRoot(root)!).DriveType != DriveType.Fixed) throw new InvalidDataException("存储目录须位于本机固定卷内。");
        RequireExistingProtectedAncestors(root);
        return root;
    }
    // Reading a saved location must not create a deployment or require its future
    // directories to exist. All existing ancestors still have to be trustworthy.
    [SupportedOSPlatform("windows")]
    internal static void RequireExistingProtectedAncestors(string path)
    {
        DeploymentPaths.RequireOrdinaryAncestors(path);
        var existing = Path.GetFullPath(path);
        while (!Directory.Exists(existing))
        {
            if (File.Exists(existing)) throw new InvalidDataException("安装目录被同名文件占用：" + existing);
            existing = Path.GetDirectoryName(existing) ?? throw new InvalidDataException("安装位置没有可核验的本地父目录。");
        }
        TrustedProgramRoots.RequireAncestors(existing);
    }
    [SupportedOSPlatform("windows")]
    internal static void PrepareForInstallation(string runtimeSid)
    {
        MachineRuntimePolicy.RequireExtensionInstallerAuthority();
        if (ProtectedAnchor is not { } root) return;
        PrepareProtectedDirectory(root, new SecurityIdentifier(runtimeSid));
        PrepareProtectedDirectory(Path.Combine(root, "Programs"), new SecurityIdentifier(runtimeSid));
        PrepareUserDataDirectory(Path.Combine(root, "UserData"));
    }
    [SupportedOSPlatform("windows")]
    internal static void PrepareUserDataDirectory(string path)
    {
        MachineRuntimePolicy.RequireExtensionInstallerAuthority();
        DeploymentPaths.RequireOrdinaryAncestors(path);
        TrustedProgramRoots.RequireAncestors(Path.GetDirectoryName(path)!);
        var admin = new SecurityIdentifier("S-1-5-32-544"); var system = new SecurityIdentifier("S-1-5-18");
        var users = new SecurityIdentifier("S-1-5-32-545"); var creator = new SecurityIdentifier("S-1-3-0");
        var inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        var directory = new DirectoryInfo(path);
        if (!directory.Exists)
        {
            var acl = new DirectorySecurity(); acl.SetOwner(admin); acl.SetAccessRuleProtection(true, false);
            acl.AddAccessRule(new(system, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
            acl.AddAccessRule(new(admin, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
            acl.AddAccessRule(new(users, FileSystemRights.ReadAndExecute, InheritanceFlags.None, PropagationFlags.None, AccessControlType.Allow));
            directory.Create(acl);
        }
        var current = directory.GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);
        if (((SecurityIdentifier)current.GetOwner(typeof(SecurityIdentifier))!).Value is not ("S-1-5-18" or "S-1-5-32-544"))
            throw new UnauthorizedAccessException("个人偏好父目录须由安装管理员持有；保留原目录。");
        bool legacyCreate = false;
        foreach (FileSystemAccessRule rule in current.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            var sid = ((SecurityIdentifier)rule.IdentityReference).Value;
            if (sid == users.Value && (rule.FileSystemRights & FileSystemRights.CreateDirectories) != 0) legacyCreate = true;
            if (rule.AccessControlType != AccessControlType.Allow || sid is not ("S-1-5-18" or "S-1-5-32-544") &&
                !(sid == users.Value && (rule.FileSystemRights == (FileSystemRights.ReadAndExecute | FileSystemRights.Synchronize) || rule.FileSystemRights == (FileSystemRights.ReadAndExecute | FileSystemRights.CreateDirectories | FileSystemRights.Synchronize)) && rule.InheritanceFlags == InheritanceFlags.None) &&
                !(sid == creator.Value && rule.FileSystemRights == FileSystemRights.FullControl && rule.InheritanceFlags == inherit && rule.PropagationFlags == PropagationFlags.InheritOnly))
                throw new UnauthorizedAccessException("个人偏好父目录权限不符合按用户隔离要求；保留原ACL，由安装修复核对。");
        }
        if (legacyCreate)
        {
            // Remove only the verified former provisioning permissions. Existing
            // SID folders and their contents remain unchanged and are checked below.
            current.PurgeAccessRules(users); current.PurgeAccessRules(creator);
            current.AddAccessRule(new(users, FileSystemRights.ReadAndExecute, InheritanceFlags.None, PropagationFlags.None, AccessControlType.Allow));
            directory.SetAccessControl(current);
        }
    }
    [SupportedOSPlatform("windows")]
    internal static string ProvisionUserRoot(string windowsSid)
    {
        MachineRuntimePolicy.RequireExtensionInstallerAuthority();
        var account = new SecurityIdentifier(windowsSid);
        if (!account.IsAccountSid()) throw new UnauthorizedAccessException("Windows用户身份无效。");
        var parent = Path.Combine(ProtectedAnchor ?? throw new InvalidDataException("缺少受保护存储位置。"), "UserData");
        PrepareUserDataDirectory(parent);
        var path = Path.Combine(parent, account.Value);
        DeploymentPaths.RequireOrdinaryAncestors(path);
        var directory = new DirectoryInfo(path);
        if (!directory.Exists) directory.Create(MachineRuntimeSetup.DirectoryAcl(account, FileSystemRights.FullControl));
        RequireUserDirectory(path, account.Value);
        return path;
    }
    [SupportedOSPlatform("windows")]
    internal static void RequireUserDirectory(string path, string windowsSid)
    {
        DeploymentPaths.RequireOrdinaryAncestors(path);
        var security = new DirectoryInfo(path).GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);
        var owner = ((SecurityIdentifier)security.GetOwner(typeof(SecurityIdentifier))!).Value;
        if (owner != windowsSid && owner is not ("S-1-5-18" or "S-1-5-32-544")) throw new UnauthorizedAccessException("个人目录所有者与Windows用户不符，保留原目录，请使用安装修复核对。");
        const FileSystemRights mutation = FileSystemRights.Write | FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;
        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            var sid = ((SecurityIdentifier)rule.IdentityReference).Value;
            if (rule.AccessControlType == AccessControlType.Allow && (rule.FileSystemRights & mutation) != 0 &&
                sid != windowsSid && sid is not ("S-1-5-18" or "S-1-5-32-544"))
                throw new UnauthorizedAccessException("个人目录可被其他Windows用户修改，保留原目录，请使用安装修复核对。");
        }
    }
    [SupportedOSPlatform("windows")]
    internal static void PrepareProtectedDirectory(string path, SecurityIdentifier account)
    {
        MachineRuntimePolicy.RequireExtensionInstallerAuthority();
        RequireExistingProtectedAncestors(path);
        // Create each missing level with an explicit ACL, never with inherited
        // writable permissions. Existing material is validated, never repaired.
        var missing = new Stack<string>();
        for (var current = Path.GetFullPath(path); !Directory.Exists(current); current = Path.GetDirectoryName(current)!) missing.Push(current);
        while (missing.TryPop(out var directory))
        {
            RequireExistingProtectedAncestors(directory);
            new DirectoryInfo(directory).Create(MachineRuntimeSetup.DirectoryAcl(account, FileSystemRights.ReadAndExecute));
            TrustedProgramRoots.RequireAncestors(directory);
        }
        TrustedProgramRoots.RequireAncestors(path);
    }
    public static string MachineRoot => ProtectedAnchor is { } root ? Path.Combine(root, "Machine") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), InstallationIdentity.ProductName);
    public static string AdministratorRoot => ProtectedAnchor is { } root ? Path.Combine(root, "Administrators") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), InstallationIdentity.ProductName + ".Administrators");
    public static string ProgramRoot(string product) => ProtectedAnchor is { } root ? Path.Combine(root, "Programs", product) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), product);
    // Address discovery for an installer before any deployment exists. This
    // grants no write authority; consumers of personal files still use UserRoot.
    public static string UserRootPath
    {
        get
        {
            if (UserRootForVerification is { } substitute) return Path.GetFullPath(substitute());
            if (OperatingSystem.IsWindows() && ProtectedAnchor is { } root)
            {
                using var identity = WindowsIdentity.GetCurrent();
                var sid = identity.User?.Value ?? throw new UnauthorizedAccessException("Windows用户身份不可用。");
                return Path.Combine(root, "UserData", sid);
            }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ImporterPc");
        }
    }
    public static string UserRoot
    {
        get
        {
            if (UserRootForVerification is { } substitute) return Path.GetFullPath(substitute());
            if (OperatingSystem.IsWindows() && ProtectedAnchor is { } root)
            {
                using var identity = WindowsIdentity.GetCurrent();
                var sid = identity.User?.Value ?? throw new UnauthorizedAccessException("Windows用户身份不可用。");
                var path = Path.Combine(root, "UserData", sid);
                if (!Directory.Exists(path))
                {
                    if (new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)) return ProvisionUserRoot(sid);
                    var deployment = WindowsDeploymentSetup.Current("sportmaster") ?? throw new UnauthorizedAccessException("个人目录尚未准备，请先完成供包台安装。");
                    _ = ManagementModeBroker.Call<string>(deployment.DataRoot, new("ProvisionUserRoot"));
                }
                MachineRuntimePolicy.RequireProtectedAcl(new DirectoryInfo(Path.Combine(root, "UserData")), null);
                RequireUserDirectory(path, sid);
                return path;
            }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), InstallationIdentity.ProductName);
        }
    }
}

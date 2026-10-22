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
    public static string CanonicalDirectory(string path)
    {
        var full = Path.GetFullPath(path);
        if (!OperatingSystem.IsWindows() || !Directory.Exists(full)) return full;
        using var directory = CreateFile(full, 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
        if (directory.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        var name = new System.Text.StringBuilder(32768); var size = GetFinalPathNameByHandle(directory, name, name.Capacity, 0);
        if (size == 0 || size >= name.Capacity) throw new Win32Exception(Marshal.GetLastWin32Error());
        var value = name.ToString(); return value.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase) ? @"\\" + value[8..] : value.StartsWith(@"\\?\", StringComparison.Ordinal) ? value[4..] : value;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern uint GetFinalPathNameByHandle(SafeFileHandle file, System.Text.StringBuilder path, int size, uint flags);
    public static string? ProtectedAnchor
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return null;
            return ReadAnchor();
        }
    }
    [SupportedOSPlatform("windows")]
    static string? ReadAnchor()
    {
        using var registry = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = registry.OpenSubKey(@"SOFTWARE\ImporterPc\Storage");
        if (key is null) return null;
        var security = key.GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);
        if (((SecurityIdentifier)security.GetOwner(typeof(SecurityIdentifier))!).Value is not ("S-1-5-18" or "S-1-5-32-544")) throw new UnauthorizedAccessException("存储路径登记须由安装管理员持有。");
        const RegistryRights mutation = RegistryRights.SetValue | RegistryRights.CreateSubKey | RegistryRights.Delete | RegistryRights.ChangePermissions | RegistryRights.TakeOwnership;
        foreach (RegistryAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            if (rule.AccessControlType == AccessControlType.Allow && (rule.RegistryRights & mutation) != 0 && ((SecurityIdentifier)rule.IdentityReference).Value is not ("S-1-5-18" or "S-1-5-32-544")) throw new UnauthorizedAccessException("存储路径登记可被非安装管理员修改。");
        if (key.GetValue("Root", null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not string value || !Path.IsPathFullyQualified(value) || value.StartsWith(@"\\", StringComparison.Ordinal)) throw new InvalidDataException("受保护存储路径登记无效。");
        var root = Path.GetFullPath(value).TrimEnd(Path.DirectorySeparatorChar);
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
    internal static bool IsManagedProgramPath(string path) => ProtectedAnchor is { } root &&
        TrustedProgramRoots.Contains(Path.Combine(root, "Programs"), path);
    [SupportedOSPlatform("windows")]
    internal static void PrepareForInstallation(string runtimeSid)
    {
        MachineRuntimePolicy.RequireExtensionInstallerAuthority();
        if (ProtectedAnchor is not { } root) return;
        PrepareProtectedDirectory(root, new SecurityIdentifier(runtimeSid));
        PrepareProtectedDirectory(Path.Combine(root, "Programs"), new SecurityIdentifier(runtimeSid));
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
    public static string MachineRoot => ProtectedAnchor is { } root ? Path.Combine(root, "Machine") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ImporterPc");
    public static string AdministratorRoot => ProtectedAnchor is { } root ? Path.Combine(root, "Administrators") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ImporterPc.Administrators");
    public static string ProgramRoot(string product) => ProtectedAnchor is { } root ? Path.Combine(root, "Programs", product) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), product);
    public static string UserRoot
    {
        get
        {
            if (OperatingSystem.IsWindows() && ProtectedAnchor is { } root)
            {
                using var identity = WindowsIdentity.GetCurrent();
                return Path.Combine(root, "UserData", identity.User?.Value ?? throw new UnauthorizedAccessException("Windows用户身份不可用。"));
            }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ImporterPc");
        }
    }
}

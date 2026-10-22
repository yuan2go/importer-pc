using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Ipc.Platform;

public static class TrustedProgramRoots
{
    public static string Default => WindowsStoragePaths.ProgramRoot("ImporterPc");
    static string RegistryFile => Path.Combine(WindowsDeploymentSetup.MachineRoot, "trusted-program-roots.json");
    public static bool Contains(string root, string path) => string.Equals(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) ||
        Path.GetFullPath(path).StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    public static string Normalize(string path, string dataRoot)
    {
        if (!Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal) || path.Contains('"')) throw new InvalidDataException("程序目录须为本地绝对路径");
        var root = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar); DeploymentPaths.RequireOrdinaryAncestors(root);
        if (root == Path.GetPathRoot(root)?.TrimEnd(Path.DirectorySeparatorChar) || Contains(root, dataRoot) || Contains(dataRoot, root) ||
            Contains(root, WindowsDeploymentSetup.MachineRoot) || Contains(WindowsDeploymentSetup.MachineRoot, root)) throw new InvalidDataException("程序、数据和机器政策目录不能重叠或使用卷根");
        if (OperatingSystem.IsWindows() && new DriveInfo(Path.GetPathRoot(root)!).DriveType != DriveType.Fixed) throw new InvalidDataException("程序目录须位于本地固定卷");
        return root;
    }
    [SupportedOSPlatform("windows")]
    public static void Preview(string root, string dataRoot)
    {
        root = Normalize(root, dataRoot);
        var parent = Directory.GetParent(root) ?? throw new InvalidDataException("程序根缺少父目录");
        if (!parent.Exists && WindowsStoragePaths.IsManagedProgramPath(root))
        {
            WindowsStoragePaths.RequireExistingProtectedAncestors(root);
            return; // The elevated installation creates the managed parent hierarchy.
        }
        if (!parent.Exists) throw new InvalidDataException("先由维护管理员创建受保护父目录，再选择其中的程序根");
        RequireAncestors(Directory.Exists(root) ? root : parent.FullName);
    }
    [SupportedOSPlatform("windows")]
    public static void Approve(string root, string dataRoot, SecurityIdentifier sid)
    {
        MachineRuntimePolicy.RequireExtensionInstallerAuthority(); root = Normalize(root, dataRoot);
        using var rootRegistryLease = new FileInfo(Path.Combine(WindowsDeploymentSetup.MachineRoot, "trusted-program-roots.lease")).Create(FileMode.OpenOrCreate, FileSystemRights.FullControl, FileShare.None, 4096, FileOptions.None, MachineRuntimeSetup.FileAcl(sid));
        var parent = Directory.GetParent(root) ?? throw new InvalidDataException("程序目录缺少可信父目录");
        if (!parent.Exists) throw new InvalidDataException("先由维护管理员创建受保护父目录，再选择其中的程序根");
        RequireAncestors(parent.FullName);
        if (!Directory.Exists(root)) new DirectoryInfo(root).Create(MachineRuntimeSetup.DirectoryAcl(sid, FileSystemRights.ReadAndExecute));
        RequireAncestors(root);
        var roots = Read().Append(root).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (roots.Length > 64) throw new InvalidDataException("受保护根配额已满，须审查已有部署");
        var temporary = RegistryFile + "." + Wire.Id() + ".new";
        using (var file = new FileInfo(temporary).Create(FileMode.CreateNew, FileSystemRights.FullControl, FileShare.None, 4096, FileOptions.WriteThrough, MachineRuntimeSetup.FileAcl(sid)))
        { file.Write(System.Text.Encoding.UTF8.GetBytes(Wire.Encode(roots))); file.Flush(true); }
        File.Move(temporary, RegistryFile, true);
    }
    [SupportedOSPlatform("windows")]
    static string[] Read()
    {
        if (!File.Exists(RegistryFile)) return [];
        // This file is anchored to ProgramData, never recursively trusted by itself.
        DeploymentPaths.RequireOrdinaryAncestors(RegistryFile);
        MachineRuntimePolicy.RequireProtectedAcl(new FileInfo(RegistryFile), null);
        MachineRuntimePolicy.RequireProtectedAcl(new DirectoryInfo(WindowsDeploymentSetup.MachineRoot), null);
        if (new FileInfo(RegistryFile).Length > 65536) throw new InvalidDataException("Trusted roots quota exceeded");
        var roots = Wire.Decode<string[]>(File.ReadAllText(RegistryFile));
        if (roots.Length > 64 || roots.Any(r => !Path.IsPathFullyQualified(r) || r.StartsWith(@"\\") || r.TrimEnd(Path.DirectorySeparatorChar) == Path.GetPathRoot(r)?.TrimEnd(Path.DirectorySeparatorChar))) throw new InvalidDataException("Malformed trusted roots");
        return roots;
    }
    [SupportedOSPlatform("windows")]
    public static string RequireBoundary(string path)
    {
        var root = new[] { Default }.Concat(Read()).Where(r => Contains(r, path)).OrderByDescending(r => r.Length).FirstOrDefault()
            ?? throw new UnauthorizedAccessException("Helper 不在经维护批准的受保护程序根内");
        var existing = Directory.Exists(path) ? path : File.Exists(path) ? Path.GetDirectoryName(path)! : path;
        while (!Directory.Exists(existing)) existing = Path.GetDirectoryName(existing) ?? throw new UnauthorizedAccessException("可信程序路径已无可核验祖先");
        RequireAncestors(existing); return root;
    }
    [SupportedOSPlatform("windows")]
    internal static void RequireAncestors(string path)
    {
        DeploymentPaths.RequireOrdinaryAncestors(path);
        var windowsAnchors = new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData) };
        for (var current = new DirectoryInfo(path); current.Parent is not null && !windowsAnchors.Contains(current.FullName, StringComparer.OrdinalIgnoreCase); current = current.Parent)
            MachineRuntimePolicy.RequireProtectedAcl(current, null);
        if (windowsAnchors.Any(anchor => Contains(anchor, path))) return;
        // A volume may allow creating unrelated folders, but must not allow deleting
        // protected children. Directory ACLs alone cannot protect against DELETE_CHILD.
        var volume = new DirectoryInfo(Path.GetPathRoot(path)!);
        foreach (FileSystemAccessRule rule in volume.GetAccessControl().GetAccessRules(true, true, typeof(SecurityIdentifier)))
            if (rule.AccessControlType == AccessControlType.Allow && (rule.FileSystemRights & (FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership)) != 0 &&
                ((SecurityIdentifier)rule.IdentityReference).Value is not ("S-1-5-18" or "S-1-5-32-544" or "S-1-3-0")) throw new UnauthorizedAccessException("卷根允许非维护主体替换受保护目录");
    }
}

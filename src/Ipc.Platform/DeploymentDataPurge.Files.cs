using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Ipc.Platform;

public static partial class WindowsDeploymentSetup
{
    static void RequirePurgeScope(DeploymentLocation location, string machineRoot)
    {
        var roots = new[] { location.DataRoot, location.ConfigurationDirectory }.Select(Path.GetFullPath).ToArray();
        machineRoot = Path.GetFullPath(machineRoot);
        foreach (var root in roots)
            if (Path.GetPathRoot(root) == root || TrustedProgramRoots.Contains(root, machineRoot) ||
                TrustedProgramRoots.Contains(root, Path.Combine(machineRoot, "deployments")) || TrustedProgramRoots.Contains(Path.Combine(machineRoot, "deployments"), root) ||
                TrustedProgramRoots.Contains(root, location.ReleaseRoot) || TrustedProgramRoots.Contains(location.ReleaseRoot, root))
                throw new InvalidDataException("数据目录与磁盘根、部署记录或程序目录重叠，拒绝永久删除。");
        if (TrustedProgramRoots.Contains(roots[0], roots[1]) || TrustedProgramRoots.Contains(roots[1], roots[0]))
            throw new InvalidDataException("数据目录与配置目录互相包含，拒绝永久删除。");
        RequireExclusivePurgeRoots(location.ApplicationId, roots, machineRoot);
    }

    // Installation also holds these primary deployment leases. Include absent
    // supported registrations so a new installation cannot appear mid-purge.
    internal static IDisposable AcquireOtherPurgeDeployments(string application, string machineRoot)
    {
        var directory = Path.Combine(machineRoot, "deployments");
        DeploymentPaths.RequireOrdinaryAncestors(directory);
        var applications = new[] { "sportmaster", "direct-plc", "wcs-to-plc" }.Concat(
            Directory.EnumerateFiles(directory, "*.json").Select(Path.GetFileNameWithoutExtension).OfType<string>().Where(name => !name.Contains('.')))
            .Where(name => name != application).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).Take(129).ToArray();
        if (applications.Length > 128) throw new InvalidDataException("安装登记超过核对范围，数据保留。");
        var leases = new PurgeDeploymentLeases();
        try
        {
            foreach (var name in applications)
            {
                var path = Path.Combine(directory, name + ".lease"); DeploymentPaths.RequireOrdinaryAncestors(path);
                leases.Files.Add(new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
            }
            return leases;
        }
        catch { leases.Dispose(); throw; }
    }
    sealed class PurgeDeploymentLeases : IDisposable
    {
        internal readonly List<FileStream> Files = [];
        public void Dispose() { foreach (var file in Files) file.Dispose(); }
    }

    // Primary registrations have no journal suffix. Other applications may use
    // the same storage base, but that does not authorize deleting their data.
    static void RequireExclusivePurgeRoots(string application, string[] roots, string machineRoot)
    {
        var directory = Path.Combine(machineRoot, "deployments");
        if (!Directory.Exists(directory)) return;
        DeploymentPaths.RequireOrdinaryAncestors(directory);
        var files = Directory.EnumerateFiles(directory, "*.json").Where(path => !Path.GetFileNameWithoutExtension(path).Contains('.')).Take(129).ToArray();
        if (files.Length > 128) throw new InvalidDataException("安装登记超过核对范围，数据保留。");
        foreach (var file in files)
        {
            if (Path.GetFileNameWithoutExtension(file) == application) continue;
            DeploymentPaths.RequireOrdinaryAncestors(file);
            if (new FileInfo(file).Length > 1024 * 1024) throw new InvalidDataException("其他安装登记无法核对，数据保留。");
            var other = Wire.Decode<DeploymentLocation>(File.ReadAllText(file));
            if (other.ApplicationId != Path.GetFileNameWithoutExtension(file)) throw new InvalidDataException("其他安装登记身份无法核对，数据保留。");
            if (roots.Any(root => new[] { other.DataRoot, other.ConfigurationDirectory, other.ReleaseRoot }.Any(owned =>
                TrustedProgramRoots.Contains(root, owned) || TrustedProgramRoots.Contains(owned, root))))
                throw new InvalidDataException("本站目录与其他安装共用，无法单独删除本站数据。程序可卸载，数据保留，请交实施人员处理。");
        }
    }

    // Hash and delete the same Windows handle. FileShare.Read excludes writers
    // and renames until disposition is set; a pathname is never reopened to delete.
    internal static void DeleteReviewedPath(string path, string? expectedHash)
    {
        path = Path.GetFullPath(path);
        DeploymentPaths.RequireOrdinaryAncestors(path);
        if (!OperatingSystem.IsWindows())
        {
            if (expectedHash is null) Directory.Delete(path, false);
            else
            {
                using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (expectedHash != LeaseHash && Convert.ToHexString(SHA256.HashData(input)) != expectedHash)
                    throw new InvalidDataException("文件在核对后变化，保留原件，请交实施人员处理。");
                File.Delete(path);
            }
            return;
        }
        var extended = @"\\?\" + path;
        // GENERIC_READ | DELETE, FILE_SHARE_READ, OPEN_EXISTING,
        // FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT.
        using var handle = OpenPurgeHandle(extended, 0x80010000, 1, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
        if (handle.IsInvalid) throw new IOException("无法独占核对待删除文件。", new Win32Exception(Marshal.GetLastWin32Error()));
        if (!PurgeHandleAttributes(handle, 9, out var attributes, 8)) throw new IOException("无法核对文件类型。");
        if ((attributes.Attributes & 0x400) != 0 || ((attributes.Attributes & 0x10) != 0) != (expectedHash is null))
            throw new InvalidDataException("删除目标已变成链接或其他文件类型，原件保留。");
        var actual = new StringBuilder(32768);
        var length = PurgeHandlePath(handle, actual, (uint)actual.Capacity, 0);
        if (length == 0 || length >= actual.Capacity || !string.Equals(actual.ToString(), extended, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("删除目标位置已变化，原件保留。");
        if (expectedHash is not null)
        {
            using var input = new FileStream(handle, FileAccess.Read);
            if (expectedHash != LeaseHash && Convert.ToHexString(SHA256.HashData(input)) != expectedHash)
                throw new InvalidDataException("文件在核对后变化，保留原件，请交实施人员处理。");
            MarkPurgeHandle(handle);
        }
        else MarkPurgeHandle(handle);
    }

    static void MarkPurgeHandle(SafeFileHandle handle)
    {
        byte delete = 1;
        if (!PurgeHandleDisposition(handle, 4, ref delete, 1)) throw new IOException("文件暂不能删除。", new Win32Exception(Marshal.GetLastWin32Error()));
    }
    [StructLayout(LayoutKind.Sequential)] struct PurgeAttributes { public uint Attributes; public uint ReparseTag; }
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern SafeFileHandle OpenPurgeHandle(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] static extern bool PurgeHandleAttributes(SafeFileHandle handle, int kind, out PurgeAttributes attributes, uint size);
    [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern uint PurgeHandlePath(SafeFileHandle handle, StringBuilder path, uint size, uint flags);
    [DllImport("kernel32.dll", EntryPoint = "SetFileInformationByHandle", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] static extern bool PurgeHandleDisposition(SafeFileHandle handle, int kind, ref byte disposition, uint size);
}

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Ipc.Platform;

public static partial class WindowsDeploymentSetup
{
    [SupportedOSPlatform("windows")]
    internal static void RequireDataStorage(string dataRoot)
    {
        if (!Path.IsPathFullyQualified(dataRoot) || dataRoot.StartsWith(@"\\", StringComparison.Ordinal))
            throw new InvalidDataException("业务数据须位于本地固定卷：" + dataRoot);
        var data = Path.GetFullPath(dataRoot); var volume = Path.GetPathRoot(data)!;
        DeploymentPaths.RequireOrdinaryAncestors(data);
        if (new DriveInfo(volume).DriveType != DriveType.Fixed) throw new InvalidDataException("业务数据须位于本地固定卷，不能使用网络或移动卷：" + volume);
        var target = new StringBuilder(32768);
        if (QueryDosDeviceW(volume[..2], target, target.Capacity) == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "数据卷映射检查失败：" + volume);
        if (target.ToString().StartsWith(@"\??\", StringComparison.Ordinal))
            throw new InvalidDataException("数据卷是路径映射，不能保证 SYSTEM 服务和日常账户访问同一物理目录；请选择本地实际卷：" + volume);
        if (!GetVolumeInformationW(volume, IntPtr.Zero, 0, out _, out _, out var flags, IntPtr.Zero, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "数据卷文件系统检查失败：" + volume);
        if ((flags & 8) == 0) throw new InvalidDataException("数据卷不支持持久 Windows ACL；请选择支持 ACL 的本地卷（如 NTFS）：" + volume);
        for (var current = new DirectoryInfo(data); current is not null; current = current.Parent)
        {
            if (File.Exists(current.FullName)) throw new InvalidDataException("数据目录被文件占用：" + current.FullName);
            if (current.Exists && (current.Attributes & FileAttributes.Encrypted) != 0)
                throw new InvalidDataException("数据路径包含 EFS 加密目录，不能保证 SYSTEM 授权服务访问；保留原目录，选择未加密的新数据位置：" + current.FullName);
        }
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    static extern uint QueryDosDeviceW(string deviceName, StringBuilder target, int capacity);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    static extern bool GetVolumeInformationW(string root, IntPtr name, uint nameSize, out uint serial, out uint maximumName,
        out uint flags, IntPtr fileSystemName, uint fileSystemNameSize);
}

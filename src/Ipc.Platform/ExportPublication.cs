using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Ipc.Platform;

// Windows exports keep every destination ancestor fixed through publication.
// Rename the very handle whose bytes were checked, never a later pathname lookup.
public sealed class ExportPublication : IDisposable
{
    readonly List<SafeFileHandle> directories = [];
    readonly string destination;
    internal string Destination => destination;
    public ExportPublication(string destination)
    {
        this.destination = Path.GetFullPath(destination);
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            var parent = Path.GetDirectoryName(this.destination)!;
            var components = new Stack<string>();
            for (var current = parent; current is not null; current = Path.GetDirectoryName(current)) components.Push(current);
            foreach (var component in components)
            {
                var handle = Open(component, 0x80000000, 3, 0x02200000);
                directories.Add(handle);
                if (!GetFileInformationByHandleEx(handle, 9, out var attributes, 8)) throw new IOException("无法核对导出目录。", new Win32Exception(Marshal.GetLastWin32Error()));
                if ((attributes.Attributes & 0x410) != 0x10) throw new IOException("导出目录包含链接或已改变，未发布文件。");
            }
        }
        catch { Dispose(); throw; }
    }
    public void Publish(string temporary, long length, string sha256, Action? beforeRename = null)
    {
        if (Path.GetDirectoryName(Path.GetFullPath(temporary)) != Path.GetDirectoryName(destination))
            throw new InvalidDataException("导出临时文件与目标目录不一致。");
        if (!OperatingSystem.IsWindows())
        {
            // Portable synthetic verification has no Windows path/ACL authority.
            if (new FileInfo(temporary).Length != length || Wire.FileHash(temporary) != sha256) throw new InvalidDataException("导出文件已变化。");
            beforeRename?.Invoke();
            File.Move(temporary, destination, false); return;
        }
        using var handle = Open(temporary, 0x80010000, 1, 0x00200000);
        if (!GetFileInformationByHandleEx(handle, 9, out var attributes, 8)) throw new IOException("无法核对导出文件。", new Win32Exception(Marshal.GetLastWin32Error()));
        if ((attributes.Attributes & 0x410) != 0) throw new InvalidDataException("导出文件不是普通文件。");
        using var input = new FileStream(handle, FileAccess.Read);
        if (input.Length != length || Convert.ToHexString(SHA256.HashData(input)) != sha256) throw new InvalidDataException("导出文件已变化，未发布文件。");
        foreach (var directory in directories)
            if (!GetFileInformationByHandleEx(directory, 9, out var current, 8) || (current.Attributes & 0x410) != 0x10)
                throw new IOException("导出目录身份已变化，未发布文件。");
        var name = Encoding.Unicode.GetBytes(Path.GetFileName(destination));
        var rootOffset = IntPtr.Size == 8 ? 8 : 4; var lengthOffset = rootOffset + IntPtr.Size; var nameOffset = lengthOffset + 4;
        var size = checked(nameOffset + name.Length + 8); var info = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.Copy(new byte[size], 0, info, size);
            Marshal.WriteIntPtr(info, rootOffset, directories[^1].DangerousGetHandle());
            Marshal.WriteInt32(info, lengthOffset, name.Length); Marshal.Copy(name, 0, info + nameOffset, name.Length);
            beforeRename?.Invoke();
            var status = NtSetInformationFile(handle, out _, info, (uint)size, 10);
            if (status < 0) throw new IOException("导出发布未完成，原目标保留。", new Win32Exception(unchecked((int)RtlNtStatusToDosError(status))));
        }
        finally { Marshal.FreeHGlobal(info); }
    }
    static SafeFileHandle Open(string path, uint access, uint share, uint flags)
    {
        var result = CreateFileW(path, access, share, IntPtr.Zero, 3, flags, IntPtr.Zero);
        if (!result.IsInvalid) return result;
        var error = Marshal.GetLastWin32Error(); result.Dispose(); throw new IOException("无法固定导出文件或目录。", new Win32Exception(error));
    }
    public void Dispose() { foreach (var handle in directories.AsEnumerable().Reverse()) handle.Dispose(); directories.Clear(); }
    [StructLayout(LayoutKind.Sequential)] struct AttributeTag { public uint Attributes, ReparseTag; }
    [StructLayout(LayoutKind.Sequential)] struct IoStatus { public nint Status; public nuint Information; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int type, out AttributeTag info, uint size);
    [DllImport("ntdll.dll")] static extern int NtSetInformationFile(SafeFileHandle handle, out IoStatus status, IntPtr info, uint size, int type);
    [DllImport("ntdll.dll")] static extern uint RtlNtStatusToDosError(int status);
}

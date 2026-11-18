using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Ipc.Platform;

namespace Ipc.Operations;

internal static class OperationsProcessRole
{
    // ProcessCommandLineInformation reads the pipe peer through its process handle.
    // Parse with Windows argument rules, including quoted executable paths.
    internal static bool Matches(Process peer, bool server)
    {
        const int commandLineInformation = 60;
        const int maximum = 65536 + 32;
        var buffer = Marshal.AllocHGlobal(maximum);
        try
        {
            if (NtQueryInformationProcess(peer.SafeHandle, commandLineInformation, buffer, maximum, out var size) != 0 ||
                size < Marshal.SizeOf<UnicodeString>() || size > maximum)
                throw new UnauthorizedAccessException("无法核验连接进程的启动角色。");
            var command = Marshal.PtrToStructure<UnicodeString>(buffer);
            var offset = command.Buffer.ToInt64() - buffer.ToInt64();
            if (offset < Marshal.SizeOf<UnicodeString>() || offset > size - command.Length || command.Length == 0 || command.Length % 2 != 0)
                throw new UnauthorizedAccessException("连接进程的启动参数无效。");
            var args = CommandLineToArgvW(Marshal.PtrToStringUni(command.Buffer, command.Length / 2)!, out var count);
            if (args == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                for (int i = 1; i < count; i++)
                    if (Marshal.PtrToStringUni(Marshal.ReadIntPtr(args, i * IntPtr.Size)) == InstallationIdentity.OperationsArgument)
                        return !server && count == 2;
                return server && count > 0;
            }
            finally { LocalFree(args); }
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct UnicodeString { public ushort Length, MaximumLength; public IntPtr Buffer; }
    [DllImport("ntdll.dll")]
    static extern int NtQueryInformationProcess(Microsoft.Win32.SafeHandles.SafeProcessHandle process, int infoClass, IntPtr data, int size, out int length);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    static extern IntPtr CommandLineToArgvW(string command, out int count);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr pointer);
}

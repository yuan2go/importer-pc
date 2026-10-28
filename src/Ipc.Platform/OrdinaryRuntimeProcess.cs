using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Ipc.Platform;

// Executable-entry bootstrap only. The child keeps the same user, arguments and
// environment; every existing runtime/SDK/configuration guard still runs there.
public static class OrdinaryRuntimeProcess
{
    public static bool RelaunchCurrentIfElevated(string[] arguments)
    {
        if (!OperatingSystem.IsWindows()) return false;
        using var identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)) return false;
        var executable = Environment.ProcessPath ?? throw new IOException("Current executable unavailable");
        var args = Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            ? new[] { Assembly.GetEntryAssembly()?.Location ?? throw new IOException("Entry assembly unavailable") }.Concat(arguments).ToArray()
            : arguments;
        using var child = Start(executable, args, Environment.CurrentDirectory);
        return true; // Process creation only; never a business-start/installation receipt.
    }

    [SupportedOSPlatform("windows")]
    internal static Child Start(string executable, string[] arguments, string workingDirectory)
    {
        if (!Path.IsPathFullyQualified(executable) || !Path.IsPathFullyQualified(workingDirectory))
            throw new ArgumentException("Explicit executable and working directory required");
        using var original = WindowsIdentity.GetCurrent();
        var sid = original.User?.Value;
        if (sid is null or "S-1-5-18" or "S-1-5-19" or "S-1-5-20")
            throw new UnauthorizedAccessException("Service identities cannot become the daily desktop user");
        if (!SaferCreateLevel(2, 0x20000, 1, out var level, 0)) throw Error("Open normal-user token level");
        try
        {
            // Flags=0: apply the normal restrictions; do not disable AppLocker/SRP.
            if (!SaferComputeTokenFromLevel(level, 0, out var token, 0, 0)) { token.Dispose(); throw Error("Restrict desktop token"); }
            using (token)
            using (var reduced = new WindowsIdentity(token.DangerousGetHandle()))
            {
                if (reduced.User?.Value != sid) throw new UnauthorizedAccessException("Runtime bootstrap cannot switch users");
                MachineRuntimePolicy.ValidateRuntimeAccount(sid, sid, true, reduced.Claims);
                var startup = new Startup { Size = Marshal.SizeOf<Startup>(), Desktop = "winsta0\\default" };
                if (!CreateProcessAsUserW(token, executable, new StringBuilder(CommandLine(executable, arguments)), 0, 0,
                    false, 0x400, 0, workingDirectory, ref startup, out var info)) throw Error("Start normal-user desktop");
                CloseHandle(info.Thread);
                return new(new SafeProcessHandle(info.Process, true), info.ProcessId);
            }
        }
        finally { SaferCloseLevel(level); }
    }

    internal static string CommandLine(string executable, string[] arguments)
    {
        static string Quote(string value)
        {
            if (value.Contains('\0')) throw new ArgumentException("NUL is not a process argument");
            var result = new StringBuilder("\""); var slashes = 0;
            foreach (var ch in value)
            {
                if (ch == '\\') { slashes++; continue; }
                result.Append('\\', ch == '"' ? slashes * 2 + 1 : slashes).Append(ch); slashes = 0;
            }
            return result.Append('\\', slashes * 2).Append('"').ToString();
        }
        var text = string.Join(" ", new[] { executable }.Concat(arguments).Select(Quote));
        return text.Length < 32767 ? text : throw new ArgumentException("Process command line exceeds Windows limit");
    }
    static Win32Exception Error(string stage) => new(Marshal.GetLastWin32Error(), stage);

    [SupportedOSPlatform("windows")]
    internal sealed class Child(SafeProcessHandle handle, uint id) : IDisposable
    {
        public uint Id => id;
        public uint Wait(int milliseconds)
        {
            var result = WaitForSingleObject(handle, checked((uint)milliseconds));
            if (result == 0x102) throw new TimeoutException("Original child is still running; no repeat launch");
            if (result != 0 || !GetExitCodeProcess(handle, out var code)) throw Error("Read original child outcome");
            return code;
        }
        public void Dispose() => handle.Dispose();
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct Startup
    {
        public int Size; public string? Reserved, Desktop, Title;
        public uint X, Y, Width, Height, Columns, Rows, Fill, Flags;
        public ushort Show, ReservedSize; public nint ReservedData, Input, Output, Error;
    }
    [StructLayout(LayoutKind.Sequential)] struct Information { public nint Process, Thread; public uint ProcessId, ThreadId; }
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool SaferCreateLevel(uint scope, uint level, uint flags, out nint handle, nint reserved);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool SaferComputeTokenFromLevel(nint level, nint original, out SafeAccessTokenHandle token, uint flags, nint reserved);
    [DllImport("advapi32.dll")] static extern bool SaferCloseLevel(nint level);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool CreateProcessAsUserW(SafeAccessTokenHandle token, string application, StringBuilder command, nint processAttributes, nint threadAttributes,
        bool inheritHandles, uint flags, nint environment, string directory, ref Startup startup, out Information information);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(nint handle);
    [DllImport("kernel32.dll", SetLastError = true)] static extern uint WaitForSingleObject(SafeProcessHandle handle, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetExitCodeProcess(SafeProcessHandle handle, out uint code);
}

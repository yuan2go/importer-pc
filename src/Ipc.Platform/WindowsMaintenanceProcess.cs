using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace Ipc.Platform;

// With UAC disabled, runas inherits the SAFER runtime's restricted token.
// The existing same-user desktop shell can launch the separate maintenance
// helper. Authenticate its image, token, session and start time before use.
[SupportedOSPlatform("windows")]
public static class WindowsMaintenanceProcess
{
    public static async Task<Child> StartAsync(ProcessStartInfo start, NamedPipeServerStream receipt,
        Task connected, CancellationToken token)
    {
        if (!start.UseShellExecute || start.Verb != "runas" || start.Arguments.Length != 0 || !Path.IsPathFullyQualified(start.FileName))
            throw new ArgumentException("Explicit Windows maintenance executable and runas required");
        using var caller = WindowsIdentity.GetCurrent();
        // The registry's EnableLUA may have changed without a system restart.
        // SAFER can disable the administrator group without adding restricting
        // SIDs, so IsTokenRestricted/elevation type alone are insufficient too.
        var administratorEnabled = new WindowsPrincipal(caller).IsInRole(WindowsBuiltInRole.Administrator);
        var disabledAdministrator = !administratorEnabled && caller.Claims.Any(c => c.Value == "S-1-5-32-544");
        if (!disabledAdministrator)
            return new Child(Process.Start(start) ?? throw new IOException("Windows did not start maintenance"));

        var started = DateTime.UtcNow;
        await DispatchFromDesktop(start, caller.User!.Value).ConfigureAwait(false);
        try { await connected.WaitAsync(TimeSpan.FromSeconds(30), token).ConfigureAwait(false); }
        catch (TimeoutException error)
        { throw new InstallationOutcomeUnknownException("Windows 桌面未交回维护进程；原操作可能仍在启动，请保留窗口并核对结果，不要重复提交。", error); }
        if (!GetNamedPipeClientProcessId(receipt.SafePipeHandle, out var pid)) throw new Win32Exception(Marshal.GetLastWin32Error());
        var process = Process.GetProcessById(checked((int)pid));
        SafeProcessHandle? pinned = null;
        try
        {
            pinned = OpenProcess(0x101000, false, pid); // Query limited information + synchronize; never PROCESS_ALL_ACCESS.
            if (pinned.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
            RequireIdentity(process, pinned, caller.User.Value);
            if (process.StartTime.ToUniversalTime() < started.AddSeconds(-1) ||
                !string.Equals(ImagePath(pinned), Path.GetFullPath(start.FileName), StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("维护回执程序或启动时间不匹配；请核对原操作。");
            return new Child(process, pinned);
        }
        catch (Exception error)
        {
            pinned?.Dispose(); process.Dispose();
            throw new InstallationOutcomeUnknownException("维护进程身份未完成核对；请保留原操作记录，不要重复提交。", error);
        }
    }

    static Task DispatchFromDesktop(ProcessStartInfo start, string callerSid)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            object? windows = null, desktop = null, document = null, shell = null;
            try
            {
                windows = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39"), true)!);
                object location = 0, root = Type.Missing; int hwnd;
                desktop = ((dynamic)windows!).FindWindowSW(ref location, ref root, 8, out hwnd, 1);
                if (desktop is null || hwnd == 0) throw new IOException("Windows 桌面不可用，维护尚未启动；请从同版安装器的维护入口继续。");
                GetWindowThreadProcessId((nint)hwnd, out var pid);
                using (var explorer = Process.GetProcessById(checked((int)pid)))
                using (var handle = OpenProcess(0x1000, false, pid))
                {
                    if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
                    RequireIdentity(explorer, handle, callerSid, requireAdministrator: false);
                    if (!string.Equals(ImagePath(handle), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), StringComparison.OrdinalIgnoreCase))
                        throw new UnauthorizedAccessException("Windows 桌面程序身份不匹配，维护尚未启动。");
                }
                document = ((dynamic)desktop).Document; shell = ((dynamic)document).Application;
                // Fixed request path/hash only; passwords remain in the DPAPI payload.
                var command = OrdinaryRuntimeProcess.CommandLine(start.FileName, start.ArgumentList.ToArray());
                var arguments = command[OrdinaryRuntimeProcess.CommandLine(start.FileName, []).Length..].TrimStart();
                ((dynamic)shell).ShellExecute(start.FileName, arguments, start.WorkingDirectory, "runas", 0);
                completion.SetResult();
            }
            catch (Exception error) { completion.SetException(error); }
            finally
            {
                foreach (var value in new[] { shell, document, desktop, windows })
                    if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
            }
        }) { IsBackground = true, Name = "Windows maintenance authorization" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
    }

    static void RequireIdentity(Process process, SafeProcessHandle handle, string sid, bool requireAdministrator = true)
    {
        using var current = Process.GetCurrentProcess();
        if (process.SessionId != current.SessionId) throw new UnauthorizedAccessException("维护进程不属于当前Windows桌面会话。");
        if (!OpenProcessToken(handle, 8, out var token)) throw new Win32Exception(Marshal.GetLastWin32Error());
        using (token)
        using (var identity = new WindowsIdentity(token.DangerousGetHandle()))
                if (identity.User?.Value != sid || requireAdministrator && !identity.Claims.Any(c => c.Value == "S-1-5-32-544" && c.Type is System.Security.Claims.ClaimTypes.GroupSid or System.Security.Claims.ClaimTypes.PrimaryGroupSid))
                    throw new UnauthorizedAccessException("Windows维护未取得当前用户的管理员权限；已有数据与凭据保留。");
    }
    static string ImagePath(SafeProcessHandle handle)
    {
        var text = new System.Text.StringBuilder(32768); var size = text.Capacity;
        if (!QueryFullProcessImageName(handle, 0, text, ref size)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return Path.GetFullPath(text.ToString());
    }
    public sealed class Child : IDisposable
    {
        readonly Process process; readonly SafeProcessHandle? pinned;
        internal Child(Process process, SafeProcessHandle? pinned = null) { this.process = process; this.pinned = pinned; }
        public int Id => process.Id;
        public int ExitCode => pinned is null ? process.ExitCode : GetExitCodeProcess(pinned, out var code)
            ? unchecked((int)code) : throw new Win32Exception(Marshal.GetLastWin32Error());
        public async Task WaitForExitAsync(CancellationToken token)
        {
            if (pinned is null) { await process.WaitForExitAsync(token).ConfigureAwait(false); return; }
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var wait = new EventWaitHandle(false, EventResetMode.ManualReset);
            wait.SafeWaitHandle = new SafeWaitHandle(pinned.DangerousGetHandle(), ownsHandle: false);
            var registration = ThreadPool.RegisterWaitForSingleObject(wait, (_, _) => completion.TrySetResult(), null, Timeout.Infinite, executeOnlyOnce: true);
            using var cancellation = token.Register(() => completion.TrySetCanceled(token));
            try { await completion.Task.ConfigureAwait(false); }
            finally { registration.Unregister(null); }
        }
        public void Dispose() { process.Dispose(); pinned?.Dispose(); }
    }
    [DllImport("kernel32.dll", SetLastError = true)] static extern SafeProcessHandle OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetExitCodeProcess(SafeProcessHandle process, out uint code);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool QueryFullProcessImageName(SafeProcessHandle process, int flags, System.Text.StringBuilder text, ref int size);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool OpenProcessToken(SafeProcessHandle process, uint access, out SafeAccessTokenHandle token);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint pid);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(nint window, out uint pid);
}

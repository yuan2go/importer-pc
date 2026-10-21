using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace Ipc.Platform;

// A local credential boundary only. It never creates business ports or performs equipment work.
public static partial class AdministratorBroker
{
    static string Key(string root) => Wire.Hash(CanonicalRoot(root).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant())[..24];
    static string CanonicalRoot(string root)
    {
        var full = Path.GetFullPath(root); if (!OperatingSystem.IsWindows() || !Directory.Exists(full)) return full;
        using var directory = CreateFile(full, 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
        if (directory.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        var name = new System.Text.StringBuilder(32768); var size = GetFinalPathNameByHandle(directory, name, name.Capacity, 0);
        if (size == 0 || size >= name.Capacity) throw new Win32Exception(Marshal.GetLastWin32Error());
        var value = name.ToString(); return value.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase) ? @"\\" + value[8..] : value.StartsWith(@"\\?\", StringComparison.Ordinal) ? value[4..] : value;
    }
    public static string ProtectedRoot(string root) => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ImporterPc.Administrators", Key(root));
    static string ServiceName(string root) => "ImporterPc.Administrator." + Key(root);
    static string PipeName(string root) => "ImporterPc.Administrator.v1." + Key(root);
    public static bool IsManaged(string root)
    {
        if (!OperatingSystem.IsWindows()) return false;
        using var manager = OpenSCManager(null, null, 1);
        if (manager.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        using var service = OpenService(manager, ServiceName(root), 4);
        if (!service.IsInvalid) return true;
        var error = Marshal.GetLastWin32Error(); if (error != 1060) throw new Win32Exception(error);
        var anchor = Path.Combine(ProtectedRoot(root), "AUTHORITY.json");
        if (File.Exists(anchor)) { MachineRuntimePolicy.RequireProtectedEvidence(anchor); return true; }
        // A missing service after an installed deployment must never fall back to a mutable store.
        return new[] { "sportmaster", "direct-plc", "wcs-to-plc" }.Select(WindowsDeploymentSetup.Current)
            .Any(d => d is not null && CanonicalRoot(d.DataRoot).Equals(CanonicalRoot(root), StringComparison.OrdinalIgnoreCase));
    }
    public static void RequireProduction(string root)
    {
        if (!IsManaged(root)) throw new InvalidOperationException("生产模式缺少受保护的软件管理员认证服务；请使用同版安装器安装或修复。");
        if (!Call<AdministratorStatus>(root, new("Status")).Provisioned) throw new InvalidOperationException("请先由安装管理员开通首个软件管理员；未开放生产业务。");
    }
    [SupportedOSPlatform("windows")]
    static void RequireServer(string root, SafePipeHandle pipe)
    {
        using var manager = OpenSCManager(null, null, 1); using var service = OpenService(manager, ServiceName(root), 4);
        if (manager.IsInvalid || service.IsInvalid || !QueryServiceStatusEx(service, 0, out var status, Marshal.SizeOf<ServiceProcessStatus>(), out _) ||
            status.CurrentState != 4 || !GetNamedPipeServerProcessId(pipe, out var pid) || status.ProcessId != pid)
            throw new UnauthorizedAccessException("认证通道不是Windows登记的本机认证服务。");
    }
    public static bool HandleService(string[] args)
    {
        if (args.FirstOrDefault() != "--administrator-service") return false;
        if (!OperatingSystem.IsWindows() || args.Length != 2) throw new PlatformNotSupportedException("认证服务启动参数无效。");
        RunService(Path.GetFullPath(args[1])); return true;
    }
    [SupportedOSPlatform("windows")]
    static void RunService(string root)
    {
        using var identity = WindowsIdentity.GetCurrent(); if (!identity.IsSystem) throw new UnauthorizedAccessException("认证服务必须使用System服务身份。");
        using var stopped = new CancellationTokenSource(); IntPtr handle = IntPtr.Zero;
        ServiceHandler handler = (control, _, _, _) => { if (control is 1 or 5) { stopped.Cancel(); return 0; } return 0; };
        ServiceMain main = (_, _) =>
        {
            handle = RegisterServiceCtrlHandlerEx(ServiceName(root), handler, IntPtr.Zero);
            if (handle == IntPtr.Zero) return;
            var status = new ServiceStatus { ServiceType = 0x10, CurrentState = 2, WaitHint = 10000 };
            SetServiceStatus(handle, ref status);
            try
            {
                MachineRuntimePolicy.RequireProtectedAcl(new DirectoryInfo(ProtectedRoot(root)), null);
                new LocalAdministrator(root, true).RevokeBrokerSessions();
                var pipes = Enumerable.Range(0, 8).Select(i => CreateServer(root, i == 0)).ToArray();
                try
                {
                    status.CurrentState = 4; status.ControlsAccepted = 1 | 4; status.WaitHint = 0; SetServiceStatus(handle, ref status);
                    var workers = pipes.Select(p => Serve(root, p, stopped.Token)).ToArray();
                    foreach (var worker in workers) _ = worker.ContinueWith(_ => stopped.Cancel(), CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                    Task.WhenAll(workers).GetAwaiter().GetResult();
                }
                finally { foreach (var pipe in pipes) pipe.Dispose(); }
            }
            catch (OperationCanceledException) when (stopped.IsCancellationRequested) { }
            catch (Exception error)
            {
                status.Win32ExitCode = 1066; status.ServiceSpecificExitCode = 1;
                try { RuntimeJournal.AppendMetadata(Path.Combine(root, "security", "diagnostics"), new { At = DateTimeOffset.UtcNow, Source = "认证", Level = "错误", Event = "AdministratorServiceStopped", Detail = error.ToString() }); } catch (IOException) { }
            }
            finally { status.CurrentState = 1; status.ControlsAccepted = 0; SetServiceStatus(handle, ref status); }
        };
        var table = new[] { new ServiceEntry { Name = ServiceName(root), Main = main }, new ServiceEntry() };
        if (!StartServiceCtrlDispatcher(table)) throw new Win32Exception(Marshal.GetLastWin32Error());
        GC.KeepAlive(main); GC.KeepAlive(handler);
    }
    static readonly SemaphoreSlim passwordWork = new(1, 1);
    [SupportedOSPlatform("windows")]
    static async Task Serve(string root, NamedPipeServerStream pipe, CancellationToken stopping)
    {
        while (!stopping.IsCancellationRequested)
        {
            await pipe.WaitForConnectionAsync(stopping).ConfigureAwait(false);
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(stopping); budget.CancelAfter(TimeSpan.FromSeconds(10));
            try
            {
                var bytes = await ReadFrame(pipe, budget.Token).ConfigureAwait(false);
                var request = JsonSerializer.Deserialize<AdministratorRpcRequest>(bytes) ?? throw new InvalidDataException("认证请求无效。");
                bool installer = false;
                pipe.RunAsClient(() => { using var client = WindowsIdentity.GetCurrent(); installer = new WindowsPrincipal(client).IsInRole(WindowsBuiltInRole.Administrator); });
                object? value; bool expensive = request.Action is "Login" or "Provision" or "Maintain";
                if (expensive) await passwordWork.WaitAsync(budget.Token).ConfigureAwait(false);
                try { budget.Token.ThrowIfCancellationRequested(); value = await Task.Run(() => new LocalAdministrator(root, true).HandleBrokerRequest(request, installer), budget.Token).ConfigureAwait(false); }
                finally { if (expensive) passwordWork.Release(); }
                await WriteFrame(pipe, JsonSerializer.SerializeToUtf8Bytes(new AdministratorRpcReply(JsonSerializer.SerializeToElement(value))), budget.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or JsonException)
            {
                var code = error is AdministratorBootstrapRequiredException ? "BootstrapRequiresInstaller" : error is UnauthorizedAccessException ? "Unauthorized" : "Rejected";
                try { await WriteFrame(pipe, JsonSerializer.SerializeToUtf8Bytes(new AdministratorRpcReply(JsonSerializer.SerializeToElement(false), DiagnosticText.Protect(error.Message), code)), budget.Token).ConfigureAwait(false); }
                catch (Exception send) when (send is IOException or OperationCanceledException) { }
            }
            finally { if (pipe.IsConnected) pipe.Disconnect(); }
        }
    }
    [SupportedOSPlatform("windows")]
    static NamedPipeServerStream CreateServer(string root, bool first)
    {
        // Eight permanent instances occupy the entire name; the client additionally pins SCM's PID.
        if (!ConvertStringSecurityDescriptorToSecurityDescriptor("D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;GRGW;;;AU)", 1, out var descriptor, out _)) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var attributes = new SecurityAttributes { Length = Marshal.SizeOf<SecurityAttributes>(), Descriptor = descriptor };
            var pipe = CreateNamedPipe(@"\\.\pipe\" + PipeName(root), 3u | 0x40000000u | (first ? 0x00080000u : 0), 8, 8, MaximumFrame, MaximumFrame, 0, ref attributes);
            if (pipe.IsInvalid) { pipe.Dispose(); throw new Win32Exception(Marshal.GetLastWin32Error()); }
            return new(PipeDirection.InOut, true, false, pipe);
        }
        finally { LocalFree(descriptor); }
    }
    [StructLayout(LayoutKind.Sequential)] struct SecurityAttributes { public int Length; public IntPtr Descriptor; public int Inherit; }
    [StructLayout(LayoutKind.Sequential)] struct ServiceStatus { public uint ServiceType, CurrentState, ControlsAccepted, Win32ExitCode, ServiceSpecificExitCode, CheckPoint, WaitHint; }
    [StructLayout(LayoutKind.Sequential)] struct ServiceProcessStatus { public uint ServiceType, CurrentState, ControlsAccepted, Win32ExitCode, ServiceSpecificExitCode, CheckPoint, WaitHint, ProcessId, ServiceFlags; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct ServiceEntry { [MarshalAs(UnmanagedType.LPWStr)] public string? Name; public ServiceMain? Main; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void ServiceMain(uint count, IntPtr args);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate uint ServiceHandler(uint control, uint eventType, IntPtr eventData, IntPtr context);
    sealed class ServiceHandle : SafeHandleZeroOrMinusOneIsInvalid { public ServiceHandle() : base(true) { } protected override bool ReleaseHandle() => CloseServiceHandle(handle); }
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern ServiceHandle OpenSCManager(string? machine, string? database, uint access);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern ServiceHandle OpenService(ServiceHandle manager, string name, uint access);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool CloseServiceHandle(IntPtr handle);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool QueryServiceStatusEx(ServiceHandle service, int level, out ServiceProcessStatus status, int size, out int needed);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint pid);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool StartServiceCtrlDispatcher([In] ServiceEntry[] table);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr RegisterServiceCtrlHandlerEx(string name, ServiceHandler handler, IntPtr context);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool SetServiceStatus(IntPtr handle, ref ServiceStatus status);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string text, uint revision, out IntPtr descriptor, out uint size);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr pointer);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern SafePipeHandle CreateNamedPipe(string name, uint mode, uint pipeMode, uint instances, int outputSize, int inputSize, uint timeout, ref SecurityAttributes security);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern uint GetFinalPathNameByHandle(SafeFileHandle file, System.Text.StringBuilder path, int size, uint flags);
}

public sealed partial class LocalAdministrator
{
    internal void RevokeBrokerSessions()
    {
        lock (sync) { using var held = LockStore(); var s = Read(); if (s.Accounts.Length > 0) Save(s with { SessionHash = null, SessionAccount = null, SessionExpires = null, Delegations = [] }, "AdministratorServiceStartedSessionsRevoked", null); }
    }
}

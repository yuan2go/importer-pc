using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Ipc.Platform;

public static partial class ManagementModeBroker
{
    [SupportedOSPlatform("windows")]
    public static void CopyCredentialsForMigration(string originalRoot, string targetRoot)
    {
        MachineRuntimePolicy.RequireExtensionInstallerAuthority();
        var original = ProtectedRoot(originalRoot); var target = ProtectedRoot(targetRoot);
        if (original.Equals(target, StringComparison.OrdinalIgnoreCase)) return;
        var names = new[] { "MODE-CONFIGURED", "PROVISIONED", "administrators.json", "management-mode.json" };
        var present = names.Where(n => File.Exists(Path.Combine(original,n))).ToArray(); if(present.Length == 0) return;
        if(present.Contains("MODE-CONFIGURED") && !present.Contains("management-mode.json") || present.Contains("PROVISIONED") && !present.Contains("administrators.json")) throw new InvalidDataException("原管理模式或历史凭据缺失，保留原根修复，不能创建新密码覆盖。");
        ProtectedDirectory(Path.GetDirectoryName(target)!, true); ProtectedDirectory(target, true);
        if (names.Any(n=>File.Exists(Path.Combine(target,n)))) throw new InvalidOperationException("恢复目标已有管理密码或历史凭据，不能覆盖；请核对原责任。");
        var inputs = new List<(string Name, FileStream Stream)>();
        try
        {
            // Pin all credential files without delete sharing: password/session
            // replacement cannot race this offline copy. Source bytes are unchanged.
            foreach(var name in present)
            {
                var file=Path.Combine(original,name); MachineRuntimePolicy.RequireProtectedEvidence(file);
                var input=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read); inputs.Add((name,input));
                if(input.Length>1024*1024) throw new InvalidDataException("原管理模式凭据超出迁移预算。");
            }
            foreach(var item in inputs)
            {
                using var output=new FileInfo(Path.Combine(target,item.Name)).Create(FileMode.CreateNew,FileSystemRights.FullControl,FileShare.None,16384,FileOptions.WriteThrough,MachineRuntimeSetup.FileAcl(new("S-1-5-18")));
                item.Stream.CopyTo(output); output.Flush(true);
            }
        }
        finally { foreach(var item in inputs)item.Stream.Dispose(); }
        // The target service revokes copied sessions before publishing its RUNNING state.
    }
    [SupportedOSPlatform("windows")]
    static void ProtectedDirectory(string path, bool readableAnchor = false)
    {
        DeploymentPaths.RequireOrdinaryAncestors(path);
        if (!Directory.Exists(path)) new DirectoryInfo(path).Create(MachineRuntimeSetup.DirectoryAcl(new(readableAnchor ? "S-1-5-32-545" : "S-1-5-18"), FileSystemRights.ReadAndExecute));
        MachineRuntimePolicy.RequireProtectedAcl(new DirectoryInfo(path), null);
    }
    [SupportedOSPlatform("windows")]
    public static void Install(string dataRoot, string sourceApp)
    {
        MachineRuntimePolicy.RequireExtensionInstallerAuthority(); dataRoot = CanonicalRoot(dataRoot); sourceApp = Path.GetFullPath(sourceApp);
        if (dataRoot.Contains('"')) throw new InvalidDataException("认证服务数据目录无效。");
        var declarationPath = Path.Combine(sourceApp, "application-release.json"); MachineRuntimePolicy.RequireProtectedEvidence(declarationPath);
        var release = Wire.Decode<ApplicationRelease>(File.ReadAllText(declarationPath));
        var hostParent = WindowsStoragePaths.ProgramRoot("ImporterPc.Administrator");
        ProtectedDirectory(hostParent); ProtectedDirectory(Path.Combine(hostParent, "hosts"));
        var host = Path.Combine(hostParent, "hosts", release.SourceCommit + "-" + Wire.Id()); ProtectedDirectory(host);
        foreach (var file in release.Files.Append(new KeyValuePair<string, string>("application-release.json", Wire.FileHash(declarationPath))))
        {
            if (file.Key.Contains(':') || Path.IsPathRooted(file.Key) || file.Key.Split('/', '\\').Any(p => p is "" or "." or "..")) throw new InvalidDataException("认证宿主文件范围无效。");
            var source = Path.Combine(sourceApp, file.Key); var target = Path.Combine(host, file.Key); MachineRuntimePolicy.RequireProtectedEvidence(source);
            if (Wire.FileHash(source) != file.Value) throw new InvalidDataException("认证宿主源文件变化。");
            ProtectedDirectory(Path.GetDirectoryName(target)!);
            using (var output = new FileInfo(target).Create(FileMode.CreateNew, FileSystemRights.FullControl, FileShare.None, 16384, FileOptions.WriteThrough, MachineRuntimeSetup.FileAcl(new("S-1-5-18"))))
            { using var input = File.OpenRead(source); input.CopyTo(output); output.Flush(true); }
            if (Wire.FileHash(target) != file.Value) throw new IOException("认证宿主复制不完整，原版本保留。");
        }
        ProtectedDirectory(Path.GetDirectoryName(ProtectedRoot(dataRoot))!, true); ProtectedDirectory(ProtectedRoot(dataRoot), true);
        var anchor = Path.Combine(ProtectedRoot(dataRoot), "AUTHORITY.json");
        if (!File.Exists(anchor))
        {
            using var output = new FileInfo(anchor).Create(FileMode.CreateNew, FileSystemRights.FullControl, FileShare.Read, 4096, FileOptions.WriteThrough, MachineRuntimeSetup.FileAcl(new("S-1-5-32-545")));
            output.Write(System.Text.Encoding.UTF8.GetBytes(Wire.Encode(new { Protocol = 1, DataRoot = CanonicalRoot(dataRoot), Service = ServiceName(dataRoot) }))); output.Flush(true);
        }
        MachineRuntimePolicy.RequireProtectedEvidence(anchor);
        var executable = Path.Combine(host, "Ipc.Assembly.exe"); if (!File.Exists(executable)) throw new InvalidDataException("认证服务宿主缺失。");
        var command = "\"" + executable + "\" --administrator-service \"" + dataRoot + "\"";
        using var manager = OpenSCManager(null, null, 1 | 2); if (manager.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        using var existing = OpenService(manager, ServiceName(dataRoot), 0xF01FF);
        var previous = existing.IsInvalid ? null : ReadCommand(existing);
        var displayName = "供包台管理模式授权 " + Key(dataRoot)[..8];
        using var service = existing.IsInvalid ? CreateService(manager, ServiceName(dataRoot), displayName, 0xF01FF, 0x10, 2, 1, command, null, IntPtr.Zero, null, null, null) : OpenService(manager, ServiceName(dataRoot), 0xF01FF);
        if (service.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            Stop(service);
            if (!ChangeServiceConfig(service, uint.MaxValue, 2, 1, command, null, IntPtr.Zero, null, null, null, displayName)) throw new Win32Exception(Marshal.GetLastWin32Error());
            ConfigureRecovery(service);
            Start(service); _ = Call<ManagementModeStatus>(dataRoot, new("Status"));
        }
        catch
        {
            // Preserve the original credential authority and executable on upgrade failure.
            if (previous is not null)
            { Stop(service); if (!ChangeServiceConfig(service, uint.MaxValue, 2, 1, previous, null, IntPtr.Zero, null, null, null, null)) throw new Win32Exception(Marshal.GetLastWin32Error()); Start(service); }
            else { Stop(service); if (!DeleteService(service)) throw new Win32Exception(Marshal.GetLastWin32Error()); }
            throw;
        }
    }
    [SupportedOSPlatform("windows")]
    public static void RemoveIfUnused(string dataRoot)
    {
        MachineRuntimePolicy.RequireExtensionInstallerAuthority();
        if (new[] { "sportmaster", "direct-plc", "wcs-to-plc" }.Select(WindowsDeploymentSetup.Current).Any(d => d is { LifecycleState: "Installed" } && Path.GetFullPath(d.DataRoot).Equals(Path.GetFullPath(dataRoot), StringComparison.OrdinalIgnoreCase))) return;
        using var manager = OpenSCManager(null, null, 1); using var service = OpenService(manager, ServiceName(dataRoot), 0xF01FF);
        if (service.IsInvalid) { if (Marshal.GetLastWin32Error() == 1060) return; throw new Win32Exception(Marshal.GetLastWin32Error()); }
        Stop(service); if (!DeleteService(service)) throw new Win32Exception(Marshal.GetLastWin32Error());
        // Protected credentials, provisioned marker and immutable hosts remain for authorized reinstall/repair.
    }
    [SupportedOSPlatform("windows")]
    static void Stop(ServiceHandle service)
    {
        if (!QueryServiceStatusEx(service, 0, out var status, Marshal.SizeOf<ServiceProcessStatus>(), out _)) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (status.CurrentState == 1) return;
        if (status.CurrentState != 3 && !ControlService(service, 1, out _) && Marshal.GetLastWin32Error() != 1062) throw new Win32Exception(Marshal.GetLastWin32Error());
        Wait(service, 1);
    }
    [SupportedOSPlatform("windows")]
    static void Start(ServiceHandle service)
    {
        if (!StartService(service, 0, IntPtr.Zero) && Marshal.GetLastWin32Error() != 1056) throw new Win32Exception(Marshal.GetLastWin32Error()); Wait(service, 4);
    }
    [SupportedOSPlatform("windows")]
    static void Wait(ServiceHandle service, uint state)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        while (System.Diagnostics.Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(15))
        {
            if (!QueryServiceStatusEx(service, 0, out var status, Marshal.SizeOf<ServiceProcessStatus>(), out _)) throw new Win32Exception(Marshal.GetLastWin32Error());
            if (status.CurrentState == state) return;
            if (state == 4 && status.CurrentState == 1) throw new IOException("管理模式授权服务未能启动；原凭据和部署保留。");
            Thread.Sleep(25);
        }
        throw new TimeoutException("管理模式授权服务未在期限内完成状态切换；原凭据保留。");
    }
    [SupportedOSPlatform("windows")]
    static string ReadCommand(ServiceHandle service)
    {
        QueryServiceConfig(service, IntPtr.Zero, 0, out var needed); if (needed is < 1 or > 65536) throw new InvalidDataException("认证服务登记超出限制。");
        var buffer = Marshal.AllocHGlobal((int)needed);
        try { if (!QueryServiceConfig(service, buffer, needed, out _)) throw new Win32Exception(Marshal.GetLastWin32Error()); return Marshal.PtrToStringUni(Marshal.PtrToStructure<ServiceConfig>(buffer).BinaryPath) ?? throw new InvalidDataException("认证服务登记缺失。"); }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    [StructLayout(LayoutKind.Sequential)] struct ServiceConfig { public uint Type, Start, Error; public IntPtr BinaryPath, Group; public uint Tag; public IntPtr Dependencies, Account, DisplayName; }
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern ServiceHandle CreateService(ServiceHandle manager, string name, string displayName, uint access, uint type, uint start, uint error, string path, string? group, IntPtr tag, string? dependencies, string? account, string? password);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool ChangeServiceConfig(ServiceHandle service, uint type, uint start, uint error, string? path, string? group, IntPtr tag, string? dependencies, string? account, string? password, string? displayName);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool QueryServiceConfig(ServiceHandle service, IntPtr config, uint size, out uint needed);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool StartService(ServiceHandle service, uint count, IntPtr args);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool ControlService(ServiceHandle service, uint control, out ServiceStatus status);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool DeleteService(ServiceHandle service);
}

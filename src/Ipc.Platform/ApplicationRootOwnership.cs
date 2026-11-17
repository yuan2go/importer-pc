using System.Runtime.Versioning;

namespace Ipc.Platform;

// Complements per-station/database and device leases. The shared key fences new
// runtime startup before any seed/DB reads, including an unreadable station seed.
public static class ApplicationRootOwnership
{
    [SupportedOSPlatform("windows")]
    public static void RequireWindowsStorage(string dataRoot, string? programRoot = null)
    {
        InstallationIdentity.RequireDataRoot(dataRoot);
        if (programRoot is not null) InstallationIdentity.RequireProgramRoot(programRoot);
        RequireDPaths([dataRoot, WindowsStoragePaths.MachineRoot, WindowsStoragePaths.AdministratorRoot,
            programRoot ?? WindowsStoragePaths.ProgramRoot("ImporterPc")]);
    }

    internal static void RequireDPaths(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (!Path.IsPathFullyQualified(path) || !Path.GetPathRoot(path)!.Equals(@"D:\", StringComparison.OrdinalIgnoreCase) ||
                Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) == Path.GetPathRoot(path)!.TrimEnd(Path.DirectorySeparatorChar))
                throw new InvalidDataException(@"SM安装与维护须使用D盘的程序、数据及受保护存储根；请由Windows管理员核对HKLM\SOFTWARE\ImporterPc\Storage，不能静默使用C盘。");
            DeploymentPaths.RequireOrdinaryAncestors(path);
        }
    }

    public static string Resource(string application, string dataRoot)
    {
        InstallationIdentity.RequireDataRoot(dataRoot);
        if (!ExtensionSchema.IsId(application) || !Path.IsPathFullyQualified(dataRoot) || dataRoot.StartsWith(@"\\", StringComparison.Ordinal) ||
            Path.GetFullPath(dataRoot).TrimEnd(Path.DirectorySeparatorChar) == Path.GetPathRoot(dataRoot)?.TrimEnd(Path.DirectorySeparatorChar) ||
            OperatingSystem.IsWindows() && !Path.GetPathRoot(dataRoot)!.Equals(@"D:\", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("应用根所有权身份无效。");
        DeploymentPaths.RequireOrdinaryAncestors(dataRoot);
        var path = Path.GetFullPath(dataRoot).TrimEnd(Path.DirectorySeparatorChar);
        if (OperatingSystem.IsWindows()) path = path.ToUpperInvariant();
        return "application-root:" + application + ":" + Wire.Hash(path);
    }

    public static IDisposable AcquireRuntime(string application, string dataRoot, Action? verifyDeployment = null)
        => AcquireAt(MachineRuntimePolicy.ResolveLeaseRoot(OperatingSystem.IsWindows() ? null : Path.Combine(dataRoot, ".runtime-ownership")), application, dataRoot, verifyDeployment);

    [SupportedOSPlatform("windows")]
    public static IDisposable AcquireMaintenance(string application, string dataRoot)
    {
        MachineRuntimePolicy.RequireExtensionInstallerAuthority();
        return AcquireAt(MachineRuntimePolicy.InstallerLeaseRoot(""), application, dataRoot);
    }

    internal static IDisposable AcquireAt(string leaseRoot, string application, string dataRoot, Action? verifyDeployment = null)
    {
        if (OperatingSystem.IsWindows() && !Path.GetPathRoot(Path.GetFullPath(leaseRoot))!.Equals(@"D:\", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("应用停机所有权须使用管理员批准的D盘机器租约根。");
        // DeleteOnClose lets the OS remove ended-session files atomically. Never
        // delete a lock pathname by hand while another process can still own it.
        var scope = new ResourceLease(leaseRoot, ["application-lifetime:" + application, Resource(application, dataRoot)], createDirectory: true, removeOnClose: true);
        try { verifyDeployment?.Invoke(); return scope; }
        catch (Exception original)
        {
            try { scope.Dispose(); }
            catch (Exception cleanup) { throw new AggregateException("部署复核失败且所有权释放未完成。", original, cleanup); }
            throw;
        }
    }
}

using System.Runtime.Versioning;

namespace Ipc.Platform;

public static partial class WindowsDeploymentSetup
{
    // The offline UI is not a business owner, but its loaded DLLs still prevent
    // deletion. Launch outside the release before the old UI host exits.
    [SupportedOSPlatform("windows")]
    public static uint LaunchUninstaller(string application)
    {
        var location = Current(application) ?? throw new InvalidDataException("未找到安装信息。");
        var hash = Wire.FileHash(Registration(application));
        var cache = RequireMaintenanceCache(location);
        RequireDetachedUninstaller(location, cache);
        var media = DeploymentMedia.Inspect(cache, application);
        if (media.SourceCommit != location.SourceCommit || Wire.FileHash(Registration(application)) != hash)
            throw new InvalidOperationException("安装信息已变化，请重新打开卸载入口。");
        RequireProtectedMaintenanceHelper(application, Path.Combine(cache, "desktop"),
            media.Files.Keys.Where(name => name.StartsWith("desktop/", StringComparison.Ordinal)).Select(name => name[8..]).ToArray());
        return OrdinaryRuntimeProcess.LaunchForCurrentUser(Path.Combine(cache, "desktop", "Ipc.Desktop.exe"),
            ["--product-maintenance"], cache);
    }

    internal static void RequireDetachedUninstaller(DeploymentLocation location, string hostRoot)
    {
        if (TrustedProgramRoots.Contains(location.ReleaseRoot, hostRoot) || TrustedProgramRoots.Contains(hostRoot, location.ReleaseRoot))
            throw new InvalidDataException("卸载入口不能占用待删除的程序目录，请使用独立维护程序。");
    }
}

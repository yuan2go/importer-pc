using System.Runtime.Versioning;
using System.Diagnostics;
using System.Globalization;

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
        using var parent = Process.GetCurrentProcess();
        return OrdinaryRuntimeProcess.LaunchForCurrentUser(Path.Combine(cache, "desktop", "Ipc.Desktop.exe"),
            ["--product-maintenance", "--wait-for-host", parent.Id.ToString(CultureInfo.InvariantCulture),
                parent.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture)], cache);
    }

    public static async Task WaitForUninstallHostAsync(string[] arguments)
    {
        if (arguments.Length == 1) return; // Windows installed-apps entry has no old UI host.
        if (arguments.Length != 4 || arguments[1] != "--wait-for-host" ||
            !int.TryParse(arguments[2], NumberStyles.None, CultureInfo.InvariantCulture, out var pid) || pid <= 0 ||
            !long.TryParse(arguments[3], NumberStyles.None, CultureInfo.InvariantCulture, out var started) || started <= 0)
            throw new InvalidDataException("卸载交接身份无效，请重新打开卸载入口。");
        await WaitForUninstallHostAsync(pid, started, TimeSpan.FromSeconds(15));
    }

    internal static async Task WaitForUninstallHostAsync(int pid, long started, TimeSpan timeout)
    {
        Process parent;
        try { parent = Process.GetProcessById(pid); }
        catch (ArgumentException) { return; } // The exact original host already exited.
        using (parent)
        {
            if (parent.HasExited || parent.StartTime.ToUniversalTime().Ticks != started) return;
            try { await parent.WaitForExitAsync().WaitAsync(timeout); }
            catch (TimeoutException error)
            { throw new IOException("原供包台窗口尚未退出，请先关闭后重新打开卸载入口。程序和数据未删除。", error); }
        }
    }

    internal static void RequireDetachedUninstaller(DeploymentLocation location, string hostRoot)
    {
        if (TrustedProgramRoots.Contains(location.ReleaseRoot, hostRoot) || TrustedProgramRoots.Contains(hostRoot, location.ReleaseRoot))
            throw new InvalidDataException("卸载入口不能占用待删除的程序目录，请使用独立维护程序。");
    }
}

using System.Text.Json;

namespace Ipc.Platform;

// A product-family floor, separate from station data and its backup/retirement.
public static class RecoverySecurityPolicy
{
    public const int CurrentEpoch = 1;
    public static void RequireRelease(string applicationDirectory)
    {
        var path = Path.Combine(applicationDirectory, "application-release.json");
        DeploymentPaths.RequireOrdinaryAncestors(path);
        if (new FileInfo(path).Length > 8 * 1024 * 1024) throw new InvalidDataException("发布声明超出预算。");
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        if (!document.RootElement.TryGetProperty("RecoverySecurityEpoch", out var epoch) ||
            !epoch.TryGetInt32(out var value) || value != CurrentEpoch)
            throw new InvalidDataException("该介质未满足管理密码恢复安全下限，禁止安装、修复或回退。请使用已修复版本。");
    }
}

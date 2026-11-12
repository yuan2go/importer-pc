using System.Reflection;
using System.Text.Json;

namespace Ipc.Platform;

public sealed record DeploymentBuildIdentity(string BusinessVersion, string SourceCommit, string Detail);

public static partial class WindowsDeploymentSetup
{
    public static DeploymentBuildIdentity RunningBuild(Assembly assembly)
    {
        var information = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+', 2);
        var source = information is { Length: 2 } && information[1].Length == 40 && information[1].All(char.IsAsciiHexDigit) ? information[1] : "未记录";
        return new(information?[0] ?? assembly.GetName().Version?.ToString(3) ?? "未记录", source, "实际运行程序集的构建元数据");
    }
    public static DeploymentBuildIdentity RegisteredBuild(DeploymentLocation location)
    {
        try
        {
            var inventory = location.InventoryFile ?? throw new InvalidDataException("登记未记录库存位置");
            MachineRuntimePolicy.RequireProtectedEvidence(inventory);
            if (new FileInfo(inventory).Length > 4 * 1024 * 1024) throw new InvalidDataException("原库存超出读取预算");
            if (Wire.FileHash(inventory) != location.InventorySha256) throw new InvalidDataException("原库存与登记不一致");
            var json = File.ReadAllText(inventory); _ = ValidateInstalledInventory(location, json);
            using var document = JsonDocument.Parse(json);
            var expected = document.RootElement.GetProperty("Files").GetProperty("desktop/application-release.json").GetString();
            var file = Path.Combine(location.ReleaseRoot, "desktop", "application-release.json");
            MachineRuntimePolicy.RequireProtectedEvidence(file);
            if (new FileInfo(file).Length > 4 * 1024 * 1024 || Wire.FileHash(file) != expected) throw new InvalidDataException("程序版本元数据与原库存不一致");
            var release = Wire.Decode<ApplicationRelease>(File.ReadAllText(file));
            if (release.ApplicationId != location.ApplicationId || release.SourceCommit != location.SourceCommit) throw new InvalidDataException("程序来源与登记冲突");
            return new(release.BusinessVersion ?? "未记录", location.SourceCommit, "受保护登记及原库存核验一致；" + (location.LifecycleState == "Installed" ? "当前程序已登记安装" : "原部署已退役，数据选择及余项分别核对"));
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException)
        { return new("未核验", location.SourceCommit, "登记来源已保留；程序版本未核验：" + error.Message); }
    }
}

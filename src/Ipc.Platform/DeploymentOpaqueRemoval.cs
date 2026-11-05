using System.Runtime.Versioning;
using System.Text.Json;

namespace Ipc.Platform;

public sealed record DeploymentOpaqueRemovalPlan(string Identity, string ApplicationId, string ExpectedRegistrationHash,
    string ReleaseRoot, string DataRoot, string ConfigurationDirectory, bool Allowed, string Reason, string[] RuntimeDirectories, int? RuntimeDirectoryCount = null)
{
    public string? OperationId { get; init; }
}

public static partial class WindowsDeploymentSetup
{
    // One user action: recover an uncommitted intent or continue the retired
    // inventory automatically. No configuration, database or backup is opened.
    [SupportedOSPlatform("windows")]
    public static WindowsRemovalReceipt UninstallProgram(string application, string expectedHash,
        Action<DeploymentLocation> shortcuts, MaintenanceExecution? execution, string? operationId = null)
    {
        MachineRuntimePolicy.RequireExtensionInstallerAuthority();
        if (application != "sportmaster") throw new NotSupportedException("此卸载方式仅用于当前SM。");
        if (Wire.FileHash(Registration(application)) != expectedHash) throw new InvalidOperationException("安装信息已变化，请重新打开卸载程序。");
        var current = Current(application) ?? throw new InvalidDataException("未找到安装信息。");
        if (current.LifecycleState == "Installed")
        {
            var original = InspectRemoval(application);
            if (original.Status == "NotCommittedNeedsClosure")
                CloseUncommittedRemoval(application, original.OperationId!, expectedHash);
            else if (original.Status != "Installed")
                throw new InstallationOutcomeUnknownException("上次卸载结果尚未确认，请先查看结果。");
            var plan = ReviewOpaqueRemoval(application, expectedHash, execution, operationId);
            if (!plan.Allowed) throw new InvalidOperationException(plan.Reason);
            return RemoveWithoutReadableConfiguration(plan, shortcuts, execution);
        }
        if (current.LifecycleState != "RemovedDataRetained") throw new InvalidOperationException("本站正在删除数据，请查看上次卸载结果。");
        using var ownership = new StoppedApplicationOwnership(MachineRuntimePolicy.InstallerLeaseRoot(""), application, current.DataRoot, execution?.Token ?? default);
        return Remove(application, expectedHash, _ => throw new InvalidOperationException("已卸载的程序不能重复提交。"), shortcuts, null, execution, withoutBusinessInspection: true);
    }

    static void RequireOpaqueRemovalCapability(DeploymentLocation location)
    {
        if (location.ApplicationId != "sportmaster") throw new NotSupportedException("此程序移除方式仅用于当前SM。");
        var cache = RequireMaintenanceCache(location);
        using var original = JsonDocument.Parse(File.ReadAllBytes(location.InventoryFile!));
        var files = original.RootElement.GetProperty("Files");
        foreach (var area in new[] { "app", "desktop" })
        {
            var releaseFile = Path.Combine(cache, area, "application-release.json");
            if (!files.TryGetProperty(area + "/application-release.json", out var expected) || expected.GetString() != Wire.FileHash(releaseFile))
                throw new InvalidDataException("维护缓存的停机能力声明不能由原库存证明。");
            var compatibility = RuntimeCompatibility(Path.Combine(cache, area));
            if (compatibility.Release.SourceCommit != location.SourceCommit || compatibility.Compatibility.LifecycleProtocol < 4)
                throw new InvalidDataException("原安装未提供可核验的全应用停机能力，请先核对同源程序。");
        }
        _ = InstalledInventory(location);
    }

    static DeploymentOpaqueRemovalPlan OpaqueRemovalPlan(DeploymentLocation location, string registrationHash, StoppedApplicationOwnership scope, string operationId)
    {
        if (scope.Application != location.ApplicationId || !scope.DataRoot.Equals(Path.GetFullPath(location.DataRoot), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("停机所有权不属于当前数据根。");
        var identity = Wire.Hash(Wire.Encode(new
        {
            operationId,
            registrationHash,
            location.ApplicationId,
            location.SourceCommit,
            location.ReleaseRoot,
            location.DataRoot,
            location.ConfigurationDirectory,
            location.InventorySha256,
            Directories = scope.Directories,
            Backup = "ExplicitlySkipped"
        }));
        return new(identity, location.ApplicationId, registrationHash, location.ReleaseRoot, location.DataRoot, location.ConfigurationDirectory, true,
            "已确认本应用未被运行实例占用；维护期间阻止启动新实例。此操作不读取作业记录、不创建完整备份，只移除程序；配置、数据、原记录与未确认责任全部保留。", scope.Directories)
        { OperationId = operationId };
    }

    [SupportedOSPlatform("windows")]
    static DeploymentOpaqueRemovalPlan ReviewOpaqueRemoval(string application, string expectedHash, MaintenanceExecution? execution, string? operationId = null)
    {
        MachineRuntimePolicy.RequireExtensionInstallerAuthority();
        using var deployment = new FileStream(Path.Combine(MachineRoot, "deployments", application + ".lease"), FileMode.Open, FileAccess.Read, FileShare.None);
        var current = Current(application) ?? throw new InvalidDataException("缺少唯一部署登记，原文件保留。");
        if (Wire.FileHash(Registration(application)) != expectedHash || current.LifecycleState != "Installed") throw new InvalidOperationException("部署已变化，请重新读取。");
        DeploymentOpaqueRemovalPlan Denied(string reason) => new("", application, expectedHash, current.ReleaseRoot, current.DataRoot, current.ConfigurationDirectory, false, reason, []);
        execution?.Checkpoint(); RequireDataStorage(current.DataRoot); RequireNoUnresolvedRemoval(application);
        try
        {
            RequireOpaqueRemovalCapability(current);
            using var scope = new StoppedApplicationOwnership(MachineRuntimePolicy.InstallerLeaseRoot(""), application, current.DataRoot, execution?.Token ?? default);
            execution?.Checkpoint(); return OpaqueRemovalPlan(current, expectedHash, scope, operationId ?? Wire.Id());
        }
        catch (IOException error) when ((error.HResult & 0xFFFF) is 32 or 33)
        { return Denied("请先关闭供包台，再卸载。程序和数据未删除。"); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException or NotSupportedException)
        { return Denied("当前安装的停机能力、原库存或存储权限无法核验；请由实施人员核对同源维护缓存。程序和数据保留。"); }
    }

    [SupportedOSPlatform("windows")]
    static WindowsRemovalReceipt RemoveWithoutReadableConfiguration(DeploymentOpaqueRemovalPlan reviewed,
        Action<DeploymentLocation> shortcuts, MaintenanceExecution? execution)
    {
        MachineRuntimePolicy.RequireExtensionInstallerAuthority();
        var current = Current(reviewed.ApplicationId) ?? throw new InvalidDataException("缺少原部署登记。");
        RequireDataStorage(current.DataRoot);
        if (!reviewed.Allowed || Wire.FileHash(Registration(reviewed.ApplicationId)) != reviewed.ExpectedRegistrationHash)
            throw new InvalidOperationException("仅移除程序的原计划已失效，请重新检查。");
        RequireOpaqueRemovalCapability(current);
        var approved = false;
        try
        {
            using var scope = new StoppedApplicationOwnership(MachineRuntimePolicy.InstallerLeaseRoot(""), current.ApplicationId, current.DataRoot, execution?.Token ?? default);
            return Remove(current.ApplicationId, reviewed.ExpectedRegistrationHash, location =>
            {
                RequireOpaqueRemovalCapability(location);
                if (OpaqueRemovalPlan(location, reviewed.ExpectedRegistrationHash, scope, reviewed.OperationId ?? throw new InvalidDataException("缺少原卸载身份。")).Identity != reviewed.Identity)
                    throw new InvalidOperationException("停机目录或安装身份在审阅后变化，请重新检查。");
                approved = true;
            }, shortcuts, null, execution, withoutBusinessInspection: true, operationId: reviewed.OperationId);
        }
        catch (Exception error) when (approved && error is not InstallationOutcomeUnknownException &&
            !(error is OperationCanceledException && execution?.Committing != true))
        { throw new InstallationOutcomeUnknownException("仅移除程序的原次结果未确认，请查看原卸载结果；未检查或清偿业务责任。", error); }
    }
}

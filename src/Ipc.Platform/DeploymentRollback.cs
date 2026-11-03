using System.Runtime.Versioning;
using System.Security.Principal;

namespace Ipc.Platform;

public sealed record DeploymentRollbackPlan(string Identity, string OperationId, string ExpectedRegistrationHash,
    DeploymentLocation Current, DeploymentLocation? Target, bool Allowed, string Reason,
    string? CurrentVersion = null, string? TargetVersion = null, string? TargetInventoryIdentity = null);
public sealed record DeploymentRollbackReceipt(string OperationId, string Status, string Detail, string? OperationFile = null);
internal sealed record DeploymentRollbackRecord(int SchemaVersion, string Status, DateTimeOffset At,
    DeploymentRollbackPlan Plan, DeploymentLocation Target, string? ErrorCode = null);

public static partial class WindowsDeploymentSetup
{
    static string RollbackFile(string application, string operation)
    {
        if (!ExtensionSchema.IsId(application) || !Guid.TryParseExact(operation, "N", out var parsed) || parsed == Guid.Empty || parsed.ToString("N") != operation)
            throw new InvalidDataException("回退操作身份无效。");
        return Path.Combine(MachineRoot, "deployments", application + ".rollback-" + operation + ".json");
    }

    static (ApplicationRelease Release, DeploymentRuntimeCompatibility Compatibility) RuntimeCompatibility(string directory)
    {
        var release = Wire.Decode<ApplicationRelease>(File.ReadAllText(Path.Combine(directory, "application-release.json")));
        RecoverySecurityPolicy.RequireRelease(directory);
        if (!release.Files.TryGetValue(DeploymentRuntimeCompatibility.FileName, out var hash))
            throw new InvalidDataException("此版本没有可核验的回退兼容声明。");
        var path = Path.Combine(directory, DeploymentRuntimeCompatibility.FileName);
        DeploymentPaths.RequireOrdinaryAncestors(path);
        if (new FileInfo(path).Length > 65536 || Wire.FileHash(path) != hash) throw new InvalidDataException("回退兼容声明已变化。");
        var compatibility = Wire.Decode<DeploymentRuntimeCompatibility>(File.ReadAllText(path));
        compatibility.Validate(release);
        return (release, compatibility);
    }

    [SupportedOSPlatform("windows")]
    public static DeploymentRollbackPlan PlanRollback(string application, string operationId)
    {
        using var lease = new FileStream(Path.Combine(MachineRoot, "deployments", application + ".lease"), FileMode.Open, FileAccess.Read, FileShare.None);
        return PlanRollbackCore(application, operationId);
    }

    [SupportedOSPlatform("windows")]
    static DeploymentRollbackPlan PlanRollbackCore(string application, string operationId)
    {
        _ = RollbackFile(application, operationId);
        var current = Current(application) ?? throw new InvalidDataException("没有可核验的当前安装。");
        var beforeHash = Wire.FileHash(Registration(application));
        var target = current.PreviousDeployment;
        DeploymentRollbackPlan Denied(string reason) => new("", operationId, beforeHash, current, target, false, reason);
        if (current.LifecycleState != "Installed") return Denied("当前程序已移除，不能切换版本。");
        if (target is null) return Denied("没有保留的上一版本。");
        try
        {
            RequireInstalledReady(current);
            RequireNoUnresolvedLifecycle(application);
            if (target.ApplicationId != application || target.LifecycleState != "Installed" || target.RuntimeAccountSid != current.RuntimeAccountSid ||
                !Path.GetFullPath(target.DataRoot).Equals(Path.GetFullPath(current.DataRoot), StringComparison.OrdinalIgnoreCase) ||
                Path.GetFullPath(target.ReleaseRoot).Equals(Path.GetFullPath(current.ReleaseRoot), StringComparison.OrdinalIgnoreCase))
                return Denied("上一版本不属于本站安装或数据根。");
            _ = TrustedProgramRoots.RequireBoundary(current.ReleaseRoot);
            _ = TrustedProgramRoots.RequireBoundary(target.ReleaseRoot);
            var currentMedia = DeploymentMedia.Inspect(current.ReleaseRoot, application);
            var targetMedia = DeploymentMedia.Inspect(target.ReleaseRoot, application);
            if (currentMedia.SourceCommit != current.SourceCommit || targetMedia.SourceCommit != target.SourceCommit)
                return Denied("版本库存与部署身份不一致。");
            var left = RuntimeCompatibility(Path.Combine(current.ReleaseRoot, "app"));
            var right = RuntimeCompatibility(Path.Combine(target.ReleaseRoot, "app"));
            var currentDesktop = RuntimeCompatibility(Path.Combine(current.ReleaseRoot, "desktop"));
            var targetDesktop = RuntimeCompatibility(Path.Combine(target.ReleaseRoot, "desktop"));
            // app and desktop have separate dependency inventories, but must
            // declare the same readers and service/application protocols.
            if ((left.Compatibility with { DependencyInventorySha256 = "" }) != (currentDesktop.Compatibility with { DependencyInventorySha256 = "" }) ||
                (right.Compatibility with { DependencyInventorySha256 = "" }) != (targetDesktop.Compatibility with { DependencyInventorySha256 = "" }))
                return Denied("命令行与桌面程序的读取或维护协议不一致。");
            var reason = DeploymentRuntimeCompatibility.Incompatibility(left.Compatibility, right.Compatibility) ??
                DeploymentRuntimeCompatibility.Incompatibility(currentDesktop.Compatibility, targetDesktop.Compatibility);
            if (reason is not null) return Denied(reason);
            if (ReleaseVersionPolicy.Compare(right.Release.BusinessVersion!, left.Release.BusinessVersion!) >= 0)
                return Denied("保留版本不是较早版本；较新版本请使用更新入口。");
            _ = InstalledInventory(target);
            var targetIdentity = Wire.Hash(Wire.Encode(targetMedia.Files.OrderBy(p => p.Key, StringComparer.Ordinal).ToArray()));
            var identity = Wire.Hash(Wire.Encode(new { operationId, beforeHash, target.ReleaseRoot, target.SourceCommit, targetIdentity }));
            return new(identity, operationId, beforeHash, current, target, true,
                "兼容声明与程序库存一致；还须离线核对原责任并完成完整备份。只切换程序，不恢复旧数据库。",
                left.Release.BusinessVersion, right.Release.BusinessVersion, targetIdentity);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException)
        { return Denied("暂不能回退：" + WindowsInstallationError.Safe(error.Message)); }
    }

    // Caller owns the application's offline lease and verified complete backup.
    [SupportedOSPlatform("windows")]
    public static DeploymentRollbackReceipt Rollback(DeploymentRollbackPlan reviewed,
        Action<DeploymentLocation> projectShortcuts, MaintenanceExecution? execution = null)
    {
        MachineRuntimePolicy.RequireExtensionInstallerAuthority();
        var application = reviewed.Current.ApplicationId;
        using var lease = new FileStream(Path.Combine(MachineRoot, "deployments", application + ".lease"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var file = RollbackFile(application, reviewed.OperationId);
        if (File.Exists(file)) throw new InstallationOutcomeUnknownException("原回退记录已存在，请核对该次结果，不重复提交。");
        var actual = PlanRollbackCore(application, reviewed.OperationId);
        if (!actual.Allowed || actual.Identity != reviewed.Identity) throw new InvalidOperationException("回退计划已变化或不再兼容，请重新核对。");
        var after = actual.Target! with
        {
            ConfigurationDirectory = actual.Current.ConfigurationDirectory,
            Shortcuts = actual.Current.Shortcuts,
            Recovery = actual.Current.Recovery,
            PreviousDeployment = actual.Current with { PreviousDeployment = null },
            LifecycleOperation = file,
            DeploymentPlanIdentity = actual.Identity
        };
        var record = new DeploymentRollbackRecord(1, "Prepared", DateTimeOffset.UtcNow, actual, after);
        var sid = new SecurityIdentifier(after.RuntimeAccountSid);
        execution?.Checkpoint();
        ApplyRollback(record, () => (Current(application)!, Wire.FileHash(Registration(application))),
            location => { execution?.BeginCommit(MaintenanceStage.RepairingSystem); WriteProtected(Registration(application), location, sid); },
            state => WriteProtected(file, state, sid), location => RepairRollbackProjection(location, projectShortcuts));
        return new(actual.OperationId, "Completed", "已切换到上一版本；原数据和请求事实保留，未启动作业或设备。", file);
    }

    internal static void ApplyRollback(DeploymentRollbackRecord prepared, Func<(DeploymentLocation Location, string Hash)> observe,
        Action<DeploymentLocation> commitRegistration, Action<DeploymentRollbackRecord> save,
        Action<DeploymentLocation> repairProjection)
    {
        if (!prepared.Plan.Allowed || observe().Hash != prepared.Plan.ExpectedRegistrationHash)
            throw new InvalidOperationException("登记在回退前已变化。");
        save(prepared);
        try
        {
            commitRegistration(prepared.Target); // Only authoritative switch; no data restore.
            repairProjection(prepared.Target);
            save(prepared with { Status = "Completed" });
        }
        catch (Exception failure)
        {
            (DeploymentLocation Location, string Hash) current;
            try { current = observe(); }
            catch (Exception inspectionFailure)
            {
                throw new InstallationOutcomeUnknownException("回退后无法读取权威登记，请保留原操作核对。", new AggregateException(failure, inspectionFailure));
            }
            var committed = current.Location.ReleaseRoot == prepared.Target.ReleaseRoot && current.Location.DeploymentPlanIdentity == prepared.Plan.Identity;
            var state = committed ? "CommittedNeedsSystemRepair" : current.Hash == prepared.Plan.ExpectedRegistrationHash ? "NotCommitted" : "Unknown";
            try { save(prepared with { Status = state, ErrorCode = failure.GetType().Name }); }
            catch (Exception receiptFailure) { throw new InstallationOutcomeUnknownException("回退与回执未完成，请保留原操作核对。", new AggregateException(failure, receiptFailure)); }
            if (committed || state == "Unknown") throw new InstallationOutcomeUnknownException("回退结果须按原登记核对，不能再次提交。", failure);
            throw;
        }
    }

    [SupportedOSPlatform("windows")]
    static void RepairRollbackProjection(DeploymentLocation target, Action<DeploymentLocation> shortcuts)
    {
        _ = DeploymentMedia.Inspect(target.ReleaseRoot, target.ApplicationId);
        RecoverySecurityPolicy.RequireRelease(Path.Combine(target.ReleaseRoot, "app"));
        if (target.ApplicationId is "sportmaster" or "direct-plc" or "wcs-to-plc") ManagementModeBroker.Install(target.DataRoot, Path.Combine(target.ReleaseRoot, "app"));
        var sid = new SecurityIdentifier(target.RuntimeAccountSid);
        void Project()
        {
            WriteProtected(Path.Combine(target.ReleaseRoot, "deployment-location.json"), target, sid);
            shortcuts(target); ProjectInstalledApplication(target);
        }
        if (target.Recovery?.Enabled == true) DesktopRecoverySupervisor.RepairAndReset(target, Project);
        else Project();
    }

    static DeploymentRollbackRecord ReadRollback(string application, string operation)
    {
        var file = RollbackFile(application, operation);
        MachineRuntimePolicy.RequireProtectedEvidence(file);
        if (new FileInfo(file).Length > 4 * 1024 * 1024) throw new InvalidDataException("原回退记录超出预算。");
        var record = Wire.Decode<DeploymentRollbackRecord>(File.ReadAllText(file));
        if (record.SchemaVersion != 1 || record.Plan.OperationId != operation || record.Plan.Current.ApplicationId != application ||
            record.Target.ApplicationId != application || record.Target.LifecycleOperation != file || record.Target.DeploymentPlanIdentity != record.Plan.Identity)
            throw new InvalidDataException("原回退记录身份不一致。");
        return record;
    }

    public static void RequireInstalledReady(DeploymentLocation location)
    {
        if (location.LifecycleOperation is null) return;
        var name = Path.GetFileNameWithoutExtension(location.LifecycleOperation);
        var prefix = location.ApplicationId + ".rollback-";
        if (!name.StartsWith(prefix, StringComparison.Ordinal) || location.LifecycleOperation != RollbackFile(location.ApplicationId, name[prefix.Length..]))
            throw new InvalidDataException("版本切换记录范围无效。");
        RequireRollbackReady(location, ReadRollback(location.ApplicationId, name[prefix.Length..]));
    }

    internal static void RequireRollbackReady(DeploymentLocation location, DeploymentRollbackRecord record)
    {
        if (record.Status != "Completed" || record.Target.ReleaseRoot != location.ReleaseRoot ||
            record.Plan.Identity != location.DeploymentPlanIdentity || record.Target.SourceCommit != location.SourceCommit)
            throw new InvalidOperationException("版本切换已登记，但系统入口尚未核对完成。请从维护入口核对上次回退；原数据保留。");
    }

    static void RequireNoUnresolvedLifecycle(string application)
    {
        var records = Directory.EnumerateFiles(Path.Combine(MachineRoot, "deployments"), application + ".rollback-*.json").Take(1025).ToArray();
        if (records.Length > 1024) throw new InvalidDataException("版本切换记录超过核对预算。");
        foreach (var file in records)
        {
            var operation = Path.GetFileNameWithoutExtension(file)[(application.Length + ".rollback-".Length)..];
            if (ReadRollback(application, operation).Status is not ("Completed" or "NotCommitted"))
                throw new InstallationOutcomeUnknownException("存在尚未核对的原回退，请先核对原操作。");
        }
    }

    public static string? LastRollbackOperation(string application)
    {
        var directory = Path.Combine(MachineRoot, "deployments");
        if (!Directory.Exists(directory)) return null;
        var records = Directory.EnumerateFiles(directory, application + ".rollback-*.json").Take(1025).ToArray();
        if (records.Length > 1024) throw new InvalidDataException("版本切换记录超过核对预算。");
        return records.Select(file => ReadRollback(application, Path.GetFileNameWithoutExtension(file)[(application.Length + ".rollback-".Length)..]))
            .OrderByDescending(r => r.At).FirstOrDefault()?.Plan.OperationId;
    }

    [SupportedOSPlatform("windows")]
    public static DeploymentRollbackReceipt InspectRollback(string application, string operation, bool repair,
        Action<DeploymentLocation>? shortcuts = null)
    {
        if (repair) MachineRuntimePolicy.RequireExtensionInstallerAuthority();
        using var lease = new FileStream(Path.Combine(MachineRoot, "deployments", application + ".lease"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var record = ReadRollback(application, operation);
        var current = Current(application) ?? throw new InvalidDataException("缺少唯一部署登记。");
        var file = RollbackFile(application, operation);
        var committed = current.LifecycleState == "Installed" && current.ReleaseRoot == record.Target.ReleaseRoot && current.DeploymentPlanIdentity == record.Plan.Identity;
        var status = committed ? record.Status == "Completed" ? "Completed" : "CommittedNeedsSystemRepair" :
            Wire.FileHash(Registration(application)) == record.Plan.ExpectedRegistrationHash ? "NotCommitted" : "Unknown";
        if (repair && status is "NotCommitted" or "CommittedNeedsSystemRepair")
        {
            if (committed) RepairRollbackProjection(current, shortcuts ?? throw new ArgumentNullException(nameof(shortcuts)));
            WriteProtected(file, record with { Status = committed ? "Completed" : "NotCommitted" }, new SecurityIdentifier(current.RuntimeAccountSid));
            status = committed ? "Completed" : "NotCommitted";
        }
        return new(operation, status, status switch
        {
            "Completed" => "原回退已完成；未启动设备，数据未回滚。",
            "NotCommitted" => "取得独占核对权后确认原登记未切换；原版本仍有效。",
            "CommittedNeedsSystemRepair" => "程序登记已切换，须修复原系统入口；不要再次回退。",
            _ => "原记录与当前登记不一致，请保留原件联系实施人员。"
        }, file);
    }
}

using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text.RegularExpressions;

namespace Ipc.Platform;

// Permanent deletion of one station's data after the program was removed (IE-D04, lifecycle §6).
// It is a separate task from uninstall: the business check must prove every request settled,
// the reviewed inventory fences the deletion (any changed, added or replaced entry refuses the
// whole plan before the first delete), only listed files and their emptied parents are removed,
// and a partial result stays PartiallyPurged so the station can never be reinstalled as an
// empty working station over unremoved records. Windows registration and deployment receipts
// are retained for the implementer's final review; organisation backups are out of scope.
public sealed record DeploymentPurgePlan(string Identity, string OperationId, string ApplicationId, string ExpectedRegistrationHash,
    bool Allowed, string Reason, DeploymentVersionFiles[] Delete, string[] Retained, string? BusinessPlanIdentity);
// The business check (every request settled, no restore/overload marker, no other station root).
public sealed record DeploymentPurgeBusinessCheck(string Identity, bool Allowed, string? Summary);
public sealed record DeploymentPurgeReceipt(string OperationId, string Status, string Detail, string[] Remaining, string OperationFile);
internal sealed record DeploymentPurgeRecord(int SchemaVersion, DateTimeOffset At, DeploymentPurgePlan Plan, string Status, string[] Remaining, string WindowsAdministratorSid);

public static partial class WindowsDeploymentSetup
{
    // Owner lease files are rewritten by every check, so their bytes are not part of the fence.
    internal const string LeaseHash = "lease";
    const int PurgeFileBudget = 65536;
    internal static readonly string[] PurgeRetained =
    [
        "Windows 部署登记和安装回执（实施人员核对最终回执后另行移除）",
        "组织备份、导出的诊断材料和本机以外的副本",
        "其他安装或其他业务的数据、产品安全下限与共享政策"
    ];

    static string PurgeFile(string application, string operation)
    {
        _ = RollbackFile(application, operation); // Same bounded identity grammar.
        return Path.Combine(MachineRoot, "deployments", application + ".purge-" + operation + ".json");
    }

    // Inventory of one root: every regular file with its SHA-256; reparse points or an
    // over-budget tree refuse the plan instead of being skipped.
    internal static DeploymentVersionFiles PurgeInventory(string root)
    {
        root = Path.GetFullPath(root);
        DeploymentPaths.RequireOrdinaryAncestors(root);
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(root)) return new(root, files, 0);
        var pending = new Stack<string>(); pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("数据目录内有链接或挂载点，拒绝永久删除。");
            foreach (var child in Directory.EnumerateDirectories(directory)) pending.Push(child);
            foreach (var file in Directory.EnumerateFiles(directory))
            {
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("数据目录内有链接文件，拒绝永久删除。");
                if (files.Count >= PurgeFileBudget) throw new InvalidDataException("数据文件超过永久删除的核对预算。");
                var name = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
                files.Add(name, LeaseFile().IsMatch(Path.GetFileName(file)) ? LeaseHash : Wire.FileHash(file));
            }
        }
        return new(root, files, files.Count);
    }

    // Plan from a registration and a business check result; no I/O beyond reading the roots.
    internal static DeploymentPurgePlan PlanDataPurgeCore(DeploymentLocation location, string registrationHash, string operation,
        DeploymentPurgeBusinessCheck? business, string machineRoot)
    {
        DeploymentPurgePlan Denied(string why) => new("", operation, location.ApplicationId, registrationHash, false, why, [], PurgeRetained, business?.Identity);
        if (location.LifecycleState is "PartiallyPurged" or "Purging")
            return Denied("上一次永久删除尚未完成，请核对并继续原次，不能换清单重新删除。");
        if (location.LifecycleState == "Purged") return Denied("本站数据已永久删除。");
        if (location.LifecycleState != "RemovedDataRetained")
            return Denied("须先卸载程序并保留数据，再单独执行永久删除。");
        if (location.RestoreHandoffHash is not null) return Denied("数据来自备份恢复，换机交接尚未核对完成，不能永久删除。");
        if (business is null) return Denied("业务记录尚未检查，不能永久删除。");
        if (!business.Allowed) return Denied(business.Summary ?? "仍有未完成或结果未确认的请求，不能永久删除；可只卸载程序并保留数据。");
        var roots = new[] { location.DataRoot, location.ConfigurationDirectory }.Select(Path.GetFullPath).ToArray();
        machineRoot = Path.GetFullPath(machineRoot);
        foreach (var root in roots)
            if (Path.GetPathRoot(root) == root || TrustedProgramRoots.Contains(root, machineRoot) ||
                TrustedProgramRoots.Contains(root, Path.Combine(machineRoot, "deployments")) || TrustedProgramRoots.Contains(root, location.ReleaseRoot))
                return Denied("数据目录与磁盘根、部署记录或程序目录重叠，拒绝永久删除。");
        if (TrustedProgramRoots.Contains(roots[0], roots[1]) || TrustedProgramRoots.Contains(roots[1], roots[0]))
            return Denied("数据目录与配置目录互相包含，拒绝永久删除。");
        var delete = roots.Select(PurgeInventory).Where(area => area.Files.Count > 0).ToArray();
        if (delete.Length == 0) return Denied("没有可删除的本站数据。");
        // The deleter only accepts plain relative names; find any other name now, not halfway through.
        if (delete.Any(area => area.Files.Keys.Any(name => !ExtensionPackages.SafePath(name))))
            return Denied("数据目录内有名称无法核对的文件，拒绝永久删除；请交实施人员处理。");
        var identity = Wire.Hash(Wire.Encode(new { location.ApplicationId, operation, registrationHash, delete, business.Identity }));
        return new(identity, operation, location.ApplicationId, registrationHash, true,
            "将删除本站的配置、数据库、图片、回包和日志（共 " + delete.Sum(d => d.Files.Count) + " 个文件），不可撤销。", delete, PurgeRetained, business.Identity);
    }

    // markRegistration(state): writes Purging before the first delete, then Purged or PartiallyPurged.
    internal static DeploymentPurgeReceipt ApplyDataPurge(DeploymentPurgeRecord record, string path, Func<string> registrationHash,
        Action<DeploymentPurgeRecord> save, Action<string> markRegistration, MaintenanceExecution? execution = null)
    {
        var plan = record.Plan;
        var resume = record.Status is "Purging" or "PartiallyPurged";
        if (!plan.Allowed || !resume && registrationHash() != plan.ExpectedRegistrationHash)
            throw new InvalidOperationException("登记已变化或计划不允许，请重新检查永久删除。");
        if (plan.Delete.Length is 0 or > 2 || plan.Delete.Select(d => Path.GetFullPath(d.Root)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != plan.Delete.Length)
            throw new InvalidDataException("永久删除范围无效。");
        foreach (var area in plan.Delete)
        {
            DeploymentPaths.RequireOrdinaryAncestors(area.Root);
            if (!resume)
            {
                // The fence: the tree must be exactly the reviewed inventory, byte for byte.
                var actual = PurgeInventory(area.Root);
                if (actual.Files.Count != area.Files.Count || actual.Files.Any(f => !area.Files.TryGetValue(f.Key, out var hash) || hash != f.Value))
                    throw new InvalidDataException("数据在审阅后有变化；整份删除清单保留，请重新检查。");
            }
            else
            {
                foreach (var file in area.Files.Where(f => f.Value != LeaseHash))
                {
                    if (!ProductValidation.IsSafeRelativeFile(area.Root, file.Key, out var full)) throw new InvalidDataException("永久删除清单路径无效。");
                    if (File.Exists(full) && Wire.FileHash(full) != file.Value) throw new InvalidDataException("剩余文件在中断后有变化；保留原状，交实施人员核对。");
                }
            }
        }
        execution?.Checkpoint();
        if (!resume) save(record with { Status = "Prepared" });
        try { execution?.BeginCommit(MaintenanceStage.RemovingProgram); }
        catch (OperationCanceledException cancelled) when (!resume)
        {
            try { save(record with { Status = "NotStarted", Remaining = [] }); }
            catch (Exception receiptFailure) { throw new InstallationOutcomeUnknownException("删除前取消的回执未确认；数据保留，请核对原次。", new AggregateException(cancelled, receiptFailure)); }
            throw;
        }
        try
        {
            markRegistration("Purging"); save(record with { Status = "Purging" });
            var remaining = plan.Delete.SelectMany(area => CleanRemovalInventory(area.Root, area.Files.Keys.ToArray(), _ => false)
                .Remaining.Select(name => Path.IsPathFullyQualified(name) ? name : Path.Combine(area.Root, name))).ToArray();
            var leftovers = plan.Delete.Any(area => Directory.Exists(area.Root));
            var status = remaining.Length == 0 && !leftovers ? "Purged" : "PartiallyPurged";
            save(record with { Status = status, Remaining = remaining });
            markRegistration(status);
            return new(plan.OperationId, status, status == "Purged"
                ? "本站数据已永久删除。Windows 登记和安装回执保留，供实施人员核对后移除。"
                : "部分文件未能删除（被占用或目录内有清单外文件），本站保持“部分删除”，不能重新安装为空站。关闭占用程序后继续原次。", remaining, path);
        }
        catch (Exception failure) when (failure is not InstallationOutcomeUnknownException)
        { throw new InstallationOutcomeUnknownException("永久删除结果未确认；只能核对并继续原次，不能创建新清单。", failure); }
    }

    [SupportedOSPlatform("windows")]
    public static DeploymentPurgePlan PlanDataPurge(string application, string operation, DeploymentPurgeBusinessCheck? business)
    {
        using var lease = new FileStream(Path.Combine(MachineRoot, "deployments", application + ".lease"), FileMode.Open, FileAccess.Read, FileShare.None);
        _ = PurgeFile(application, operation);
        var current = Current(application) ?? throw new InvalidDataException("缺少唯一部署登记。");
        return PlanDataPurgeCore(current, Wire.FileHash(Registration(application)), operation, business, MachineRoot);
    }

    [SupportedOSPlatform("windows")]
    public static DeploymentPurgeReceipt PurgeData(DeploymentPurgePlan reviewed, DeploymentPurgeBusinessCheck business, MaintenanceExecution? execution = null)
    {
        MachineRuntimePolicy.RequireExtensionInstallerAuthority();
        using var lease = new FileStream(Path.Combine(MachineRoot, "deployments", reviewed.ApplicationId + ".lease"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var path = PurgeFile(reviewed.ApplicationId, reviewed.OperationId);
        if (File.Exists(path)) throw new InstallationOutcomeUnknownException("原永久删除记录已存在，请核对或继续原次，不能重复提交。");
        var current = Current(reviewed.ApplicationId) ?? throw new InvalidDataException("缺少唯一部署登记。");
        using var rootOwnership = reviewed.ApplicationId == "sportmaster" ? ApplicationRootOwnership.AcquireMaintenance(reviewed.ApplicationId, current.DataRoot) : null;
        var actual = PlanDataPurgeCore(current, Wire.FileHash(Registration(reviewed.ApplicationId)), reviewed.OperationId, business, MachineRoot);
        if (!actual.Allowed || actual.Identity != reviewed.Identity) throw new InvalidOperationException("永久删除计划已变化，请重新检查。");
        using var identity = WindowsIdentity.GetCurrent();
        var sid = new SecurityIdentifier(current.RuntimeAccountSid);
        var record = new DeploymentPurgeRecord(1, DateTimeOffset.UtcNow, actual, "Prepared", [], identity.User!.Value);
        return ApplyDataPurge(record, path, () => Wire.FileHash(Registration(actual.ApplicationId)),
            state => WriteProtected(path, state, sid), state => WriteProtected(Registration(actual.ApplicationId), current with { LifecycleState = state, MaintenanceOperation = path }, sid), execution);
    }

    // Continue an interrupted or partial purge with its original list only.
    [SupportedOSPlatform("windows")]
    public static DeploymentPurgeReceipt ResumeDataPurge(string application, string operation, MaintenanceExecution? execution = null)
    {
        MachineRuntimePolicy.RequireExtensionInstallerAuthority();
        using var lease = new FileStream(Path.Combine(MachineRoot, "deployments", application + ".lease"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var path = PurgeFile(application, operation);
        MachineRuntimePolicy.RequireProtectedEvidence(path);
        if (new FileInfo(path).Length > 8 * 1024 * 1024) throw new InvalidDataException("永久删除记录超过读取预算。");
        var record = Wire.Decode<DeploymentPurgeRecord>(File.ReadAllText(path));
        var current = Current(application) ?? throw new InvalidDataException("缺少唯一部署登记。");
        if (record.Plan.ApplicationId != application || record.Plan.OperationId != operation || current.MaintenanceOperation != path ||
            record.Status is not ("Purging" or "PartiallyPurged") || current.LifecycleState is not ("Purging" or "PartiallyPurged"))
            throw new InvalidOperationException("没有可继续的原永久删除。");
        var sid = new SecurityIdentifier(current.RuntimeAccountSid);
        return ApplyDataPurge(record, path, () => Wire.FileHash(Registration(application)),
            state => WriteProtected(path, state, sid), state => WriteProtected(Registration(application), current with { LifecycleState = state }, sid), execution);
    }

    [GeneratedRegex(@"^[0-9A-F]{64}\.lease$")] private static partial Regex LeaseFile();
}

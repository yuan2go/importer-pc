using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text.RegularExpressions;

namespace Ipc.Platform;

// Optional station-data deletion within uninstall. Program removal has already
// verified exclusive ownership. Request state and configuration readability do
// not gate uninstall; only the fixed directory and file boundaries gate deletion.
public sealed record DeploymentPurgePlan(string Identity, string OperationId, string ApplicationId, string ExpectedRegistrationHash,
    bool Allowed, string Reason, DeploymentVersionFiles[] Delete, string[] Retained);
public sealed record DeploymentPurgeReceipt(string OperationId, string Status, string Detail, string[] Remaining, string OperationFile);
internal sealed record DeploymentPurgeRecord(int SchemaVersion, DateTimeOffset At, DeploymentPurgePlan Plan, string Status, string[] Remaining, string WindowsAdministratorSid);

public static partial class WindowsDeploymentSetup
{
    public static DeploymentPurgeReceipt InspectDataPurge(string application)
    {
        using var lease = new FileStream(Path.Combine(MachineRoot, "deployments", application + ".lease"), FileMode.Open, FileAccess.Read, FileShare.None);
        var current = Current(application) ?? throw new InvalidDataException("未找到安装信息。");
        var path = current.MaintenanceOperation ?? throw new InvalidDataException("未找到数据删除记录。");
        MachineRuntimePolicy.RequireProtectedEvidence(path);
        if (new FileInfo(path).Length > 8 * 1024 * 1024) throw new InvalidDataException("数据删除记录超过读取预算。");
        var record = Wire.Decode<DeploymentPurgeRecord>(File.ReadAllText(path));
        if (path != PurgeFile(application, record.Plan.OperationId))
            throw new InstallationOutcomeUnknownException("数据删除结果尚未确认，请稍后再次查看。");
        return ProjectDataPurge(record, path, current);
    }

    internal static DeploymentPurgeReceipt ProjectDataPurge(DeploymentPurgeRecord record, string path, DeploymentLocation current)
    {
        var roots = new[] { current.DataRoot, current.ConfigurationDirectory }.Select(Path.GetFullPath).Order(StringComparer.OrdinalIgnoreCase);
        if (record.SchemaVersion != 1 || !record.Plan.Allowed || record.Plan.ApplicationId != current.ApplicationId ||
            current.MaintenanceOperation != path || Path.GetFileName(path) != current.ApplicationId + ".purge-" + record.Plan.OperationId + ".json" ||
            !record.Plan.Delete.Select(area => Path.GetFullPath(area.Root)).Order(StringComparer.OrdinalIgnoreCase).SequenceEqual(roots, StringComparer.OrdinalIgnoreCase) ||
            current.LifecycleState is not ("Purging" or "PartiallyPurged" or "Purged") ||
            record.Status is not ("Prepared" or "Purging" or "PartiallyPurged" or "Purged") ||
            current.LifecycleState == "Purged" && record.Status != "Purged")
            throw new InstallationOutcomeUnknownException("数据删除结果尚未确认，请稍后再次查看。");
        // Registration and receipt are separate durable writes. A crash between
        // them still belongs to this exact plan and can finish through retry.
        var status = current.LifecycleState == "Purged" ? "Purged" : "PartiallyPurged";
        return new(record.Plan.OperationId, status, status == "Purged" ? "本站数据已删除。" : "数据清理尚未完成，请关闭占用文件的程序后重试卸载。", record.Remaining, path);
    }
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
        if (File.Exists(root)) throw new InvalidDataException("数据目录已被同名文件占用，不能删除。");
        if (!Directory.Exists(root)) return new(root, files, 0);
        var pending = new Stack<string>(); pending.Push(root); var directories = 0;
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            if (++directories > PurgeFileBudget) throw new InvalidDataException("数据目录超过删除核对预算。");
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

    // No business database or configuration parsing, and no request-state check.
    internal static DeploymentPurgePlan PlanDataPurgeCore(DeploymentLocation location, string registrationHash, string operation,
        string machineRoot)
    {
        DeploymentPurgePlan Denied(string why) => new("", operation, location.ApplicationId, registrationHash, false, why, [], PurgeRetained);
        if (location.LifecycleState is "PartiallyPurged" or "Purging")
            return Denied("上一次永久删除尚未完成，请核对并继续原次，不能换清单重新删除。");
        if (location.LifecycleState == "Purged") return Denied("本站数据已永久删除。");
        if (location.LifecycleState != "RemovedDataRetained")
            return Denied("程序尚未卸载，请先关闭供包台后卸载。");
        var roots = new[] { location.DataRoot, location.ConfigurationDirectory }.Select(Path.GetFullPath).ToArray();
        machineRoot = Path.GetFullPath(machineRoot);
        foreach (var root in roots)
            if (Path.GetPathRoot(root) == root || TrustedProgramRoots.Contains(root, machineRoot) ||
                TrustedProgramRoots.Contains(root, Path.Combine(machineRoot, "deployments")) || TrustedProgramRoots.Contains(root, location.ReleaseRoot))
                return Denied("数据目录与磁盘根、部署记录或程序目录重叠，拒绝永久删除。");
        if (TrustedProgramRoots.Contains(roots[0], roots[1]) || TrustedProgramRoots.Contains(roots[1], roots[0]))
            return Denied("数据目录与配置目录互相包含，拒绝永久删除。");
        var delete = roots.Select(PurgeInventory).ToArray();
        // The deleter only accepts plain relative names; find any other name now, not halfway through.
        if (delete.Any(area => area.Files.Keys.Any(name => !ExtensionPackages.SafePath(name))))
            return Denied("数据目录内有名称无法核对的文件，拒绝永久删除；请交实施人员处理。");
        var identity = Wire.Hash(Wire.Encode(new { location.ApplicationId, operation, registrationHash, delete }));
        return new(identity, operation, location.ApplicationId, registrationHash, true,
            "将删除本站的配置、数据库、图片、回包和日志（共 " + delete.Sum(d => d.Files.Count) + " 个文件），不可撤销。", delete, PurgeRetained);
    }

    // markRegistration(state): writes Purging before the first delete, then Purged or PartiallyPurged.
    internal static DeploymentPurgeReceipt ApplyDataPurge(DeploymentPurgeRecord record, string path, Func<string> registrationHash,
        Action<DeploymentPurgeRecord> save, Action<string> markRegistration, MaintenanceExecution? execution = null)
    {
        var plan = record.Plan;
        var resume = record.Status is "Purging" or "PartiallyPurged" or "Purged";
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
                    if (!ExtensionPackages.SafePath(file.Key)) throw new InvalidDataException("永久删除清单路径无效。");
                    var full = Path.GetFullPath(Path.Combine(area.Root, file.Key));
                    DeploymentPaths.RequireOrdinaryAncestors(full);
                    if (File.Exists(full) && Wire.FileHash(full) != file.Value) throw new InvalidDataException("剩余文件在中断后有变化；保留原状，交实施人员核对。");
                }
            }
        }
        execution?.Checkpoint();
        if (!resume) save(record with { Status = "Prepared" });
        try
        {
            if (execution is { Committing: false }) execution.BeginCommit(MaintenanceStage.RemovingProgram);
            else execution?.Report(MaintenanceStage.RemovingProgram);
        }
        catch (OperationCanceledException cancelled) when (!resume)
        {
            try { save(record with { Status = "NotStarted", Remaining = [] }); }
            catch (Exception receiptFailure) { throw new InstallationOutcomeUnknownException("删除前取消的回执未确认；数据保留，请核对原次。", new AggregateException(cancelled, receiptFailure)); }
            throw;
        }
        try
        {
            markRegistration("Purging"); save(record with { Status = "Purging" });
            var remaining = plan.Delete.SelectMany(area =>
            {
                var cleanup = CleanRemovalInventory(area.Root, area.Files.Keys.ToArray(), _ => false, PurgeFileBudget);
                // Empty directories have no file entry in the original inventory.
                // Remove them without following links or deleting unlisted files.
                var pending = new Stack<string>(); var directories = new List<string>();
                if (Directory.Exists(area.Root)) pending.Push(area.Root);
                while (pending.TryPop(out var directory))
                {
                    DeploymentPaths.RequireOrdinaryAncestors(directory);
                    if (directories.Count >= PurgeFileBudget) throw new InvalidDataException("数据目录超过删除核对预算。");
                    directories.Add(directory);
                    foreach (var child in Directory.EnumerateDirectories(directory)) pending.Push(child);
                }
                var remainingDirectories = new List<string>();
                foreach (var directory in directories.AsEnumerable().Reverse())
                {
                    DeploymentPaths.RequireOrdinaryAncestors(directory);
                    try { Directory.Delete(directory, false); }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException) { remainingDirectories.Add(directory); }
                }
                return cleanup.Remaining.Select(name => Path.IsPathFullyQualified(name) ? name : Path.Combine(area.Root, name))
                    .Where(path => File.Exists(path) || Directory.Exists(path)).Concat(remainingDirectories);
            }).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
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
    public static DeploymentPurgePlan PlanDataPurge(string application, string operation)
    {
        using var lease = new FileStream(Path.Combine(MachineRoot, "deployments", application + ".lease"), FileMode.Open, FileAccess.Read, FileShare.None);
        _ = PurgeFile(application, operation);
        var current = Current(application) ?? throw new InvalidDataException("缺少唯一部署登记。");
        return PlanDataPurgeCore(current, Wire.FileHash(Registration(application)), operation, MachineRoot);
    }

    [SupportedOSPlatform("windows")]
    public static DeploymentPurgeReceipt PurgeData(DeploymentPurgePlan reviewed, MaintenanceExecution? execution = null)
    {
        MachineRuntimePolicy.RequireExtensionInstallerAuthority();
        using var lease = new FileStream(Path.Combine(MachineRoot, "deployments", reviewed.ApplicationId + ".lease"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var path = PurgeFile(reviewed.ApplicationId, reviewed.OperationId);
        if (File.Exists(path)) throw new InstallationOutcomeUnknownException("原永久删除记录已存在，请核对或继续原次，不能重复提交。");
        var current = Current(reviewed.ApplicationId) ?? throw new InvalidDataException("缺少唯一部署登记。");
        using var rootOwnership = reviewed.ApplicationId == "sportmaster" ? ApplicationRootOwnership.AcquireMaintenance(reviewed.ApplicationId, current.DataRoot) : null;
        var actual = PlanDataPurgeCore(current, Wire.FileHash(Registration(reviewed.ApplicationId)), reviewed.OperationId, MachineRoot);
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
        using var rootOwnership = application == "sportmaster" ? ApplicationRootOwnership.AcquireMaintenance(application, current.DataRoot) : null;
        if (record.Plan.OperationId != operation)
            throw new InvalidOperationException("没有可继续的原永久删除。");
        var observed = ProjectDataPurge(record, path, current);
        if (observed.Status == "Purged") return observed;
        var sid = new SecurityIdentifier(current.RuntimeAccountSid);
        return ApplyDataPurge(record with { Status = "Purging" }, path, () => Wire.FileHash(Registration(application)),
            state => WriteProtected(path, state, sid), state => WriteProtected(Registration(application), current with { LifecycleState = state }, sid), execution);
    }

    [GeneratedRegex(@"^[0-9A-F]{64}\.lease$")] private static partial Regex LeaseFile();
}

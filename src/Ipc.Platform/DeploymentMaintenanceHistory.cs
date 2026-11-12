using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text.Json;

namespace Ipc.Platform;

public sealed record DeploymentHistoryItem(string Kind, string OperationId, string RecordedStatus, string Status, string Detail,
    string OperationFile, DeploymentLocation? Deployment, string[] Remaining, DeploymentVersionRetention[] Retained, bool CanResume = false,
    int TotalRemaining = 0, int TotalRetained = 0, DateTimeOffset? At = null, string? OriginalScope = null)
{
    public string? RestoreSummary { get; init; }
    public string? RestoreState { get; init; }
    public bool? EverEnabled { get; init; }
    public bool? BusinessPaused { get; init; }
}
public sealed record DeploymentHistoryPage(DeploymentHistoryItem[] Items, bool HasMore);

public static partial class WindowsDeploymentSetup
{
    static string HistoryFile(string application, string kind, string operation)
    {
        if (!ExtensionSchema.IsId(application) || !IsInstallationOperationId(operation) ||
            kind is not ("setup" or "rollback" or "migration" or "restore" or "remove" or "prune" or "assets" or "purge"))
            throw new InvalidDataException("历史维护操作身份无效。");
        return Path.Combine(MachineRoot, "deployments", application + "." + kind + "-" + operation + ".json");
    }

    public static DeploymentHistoryPage ReadMaintenanceHistory(string application, int offset = 0)
    {
        if (offset is < 0 or > 8192 || !ExtensionSchema.IsId(application)) throw new InvalidDataException("历史分页无效。");
        var directory = Path.Combine(MachineRoot, "deployments");
        if (!Directory.Exists(directory)) return new([], false);
        using var owner = new FileStream(Path.Combine(directory, application + ".lease"), FileMode.Open, FileAccess.Read, FileShare.None);
        var selections = new List<(string Kind, string Operation)>();
        foreach (var kind in new[] { "setup", "rollback", "migration", "restore", "remove", "prune", "assets", "purge" })
        {
            var prefix = application + "." + kind + "-";
            var files = Directory.EnumerateFiles(directory, prefix + "*.json").Take(1025).ToArray();
            if (files.Length > 1024) throw new InvalidDataException("历史维护记录超过读取预算，原件保留。");
            selections.AddRange(files.Select(f => Path.GetFileName(f)[prefix.Length..^5]).Where(IsInstallationOperationId).Select(op => (kind, op)));
        }
        var page = selections.OrderBy(p => p.Kind, StringComparer.Ordinal).ThenBy(p => p.Operation, StringComparer.Ordinal).Skip(offset).Take(13).ToArray();
        // List only original fields. Full result projection is a separate read of
        // the selected operation, under the same deployment exclusion boundary.
        return new(page.Take(12).Select(p =>
        {
            var file = HistoryFile(application, p.Kind, p.Operation); MachineRuntimePolicy.RequireProtectedEvidence(file);
            var maximum = p.Kind == "assets" ? 32 * 1024 * 1024 : p.Kind == "purge" ? 8 * 1024 * 1024 : 4 * 1024 * 1024;
            if (new FileInfo(file).Length > maximum) return new DeploymentHistoryItem(p.Kind, p.Operation, "Unknown", "Unknown", "记录超过读取预算，保留原件。", file, null, [], []);
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(file)); var root = document.RootElement;
                var status = p.Kind == "restore" ? "CopyIntentPrepared" : root.GetProperty("Status").GetString() ?? "Unknown";
                DateTimeOffset? at = root.TryGetProperty("At", out var time) && time.TryGetDateTimeOffset(out var stamp) ? stamp : null;
                DeploymentLocation? deployment = null;
                var locationKey = p.Kind == "migration" ? "Next" : p.Kind == "rollback" ? "Target" : p.Kind == "restore" ? "Source" : "Location";
                if (root.TryGetProperty(locationKey, out var location) && location.ValueKind == JsonValueKind.Object) deployment = Wire.Decode<DeploymentLocation>(location.GetRawText());
                string? scope = root.TryGetProperty("Plan", out var plan) && plan.TryGetProperty(p.Kind == "assets" ? "Scope" : "ExpectedRegistrationHash", out var originalScope) ? originalScope.GetString() : null;
                return new DeploymentHistoryItem(p.Kind, p.Operation, status, "Uninspected", "选择此操作核对原部署、原结果和当前余项。", file, deployment, [], [], At: at, OriginalScope: scope);
            }
            catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException)
            { return new DeploymentHistoryItem(p.Kind, p.Operation, "Unknown", "Unknown", "原记录无法完整读取，待核实：" + error.Message, file, null, [], []); }
        }).ToArray(), page.Length > 12);
    }

    public static DeploymentHistoryItem InspectMaintenanceHistory(string application, string kind, string operation)
    {
        using var owner = new FileStream(Path.Combine(MachineRoot, "deployments", application + ".lease"), FileMode.Open, FileAccess.Read, FileShare.None);
        var file = HistoryFile(application, kind, operation); MachineRuntimePolicy.RequireProtectedEvidence(file);
        var maximum = kind == "assets" ? 32 * 1024 * 1024 : kind == "purge" ? 8 * 1024 * 1024 : 4 * 1024 * 1024;
        if (new FileInfo(file).Length > maximum) throw new InvalidDataException("原维护记录超过读取预算。");
        var json = File.ReadAllText(file); using var document = JsonDocument.Parse(json);
        var recorded = kind == "restore" ? "CopyIntentPrepared" : document.RootElement.GetProperty("Status").GetString() ?? "Unknown";
        var current = Current(application); var history = ReadDeploymentHistory(application);
        DateTimeOffset? at = document.RootElement.TryGetProperty("At", out var time) && time.TryGetDateTimeOffset(out var stamp) ? stamp : null;
        string? scope = document.RootElement.TryGetProperty("Plan", out var plan) && plan.TryGetProperty(kind == "assets" ? "Scope" : "ExpectedRegistrationHash", out var originalScope) ? originalScope.GetString() : null;
        DeploymentLocation? OriginalDeployment(string hash)
        {
            var candidates = history.SelectMany(edge => new[] { edge.Previous, edge.Next }).Concat(current is null ? [] : [current])
                .Where(location => Wire.Hash(Wire.Encode(location)) == hash).DistinctBy(location => Wire.Encode(location)).ToArray();
            return candidates.Length == 1 ? candidates[0] : null;
        }
        DeploymentHistoryItem Result(string status, string detail, DeploymentLocation? location = null, string[]? remaining = null,
            DeploymentVersionRetention[]? retained = null, bool resume = false)
        {
            remaining ??= []; retained ??= [];
            return new(kind, operation, recorded, status, detail, file, location,
                remaining.Take(16).Select(s => s[..Math.Min(s.Length, 600)]).ToArray(), retained.Take(16).ToArray(), resume, remaining.Length, retained.Length, at, scope);
        }
        if (kind == "setup")
        {
            var request = ReadInstallationRequest(application, operation, json);
            var inspection = ProjectInstallation(request, json, current, history);
            return Result(inspection.Status, inspection.Detail, inspection.Location);
        }
        if (kind == "rollback")
        {
            var record = ReadRollback(application, operation);
            var superseded = DeploymentPrecedes(record.Target, current, history);
            var committed = current is not null && DeploymentNode(current) == DeploymentNode(record.Target);
            var state = superseded ? "Superseded" : committed ? recorded == "Completed" ? "Completed" : "CommittedNeedsSystemRepair" : record.Plan.ExpectedRegistrationHash == (File.Exists(Registration(application)) ? Wire.FileHash(Registration(application)) : null) ? "NotCommitted" : "Unknown";
            return Result(state, superseded ? "原回滚已被后续操作取代；原提交结果保留，不修复退役入口。" : "原回滚提交与当前登记已分别核对；程序切换不代表业务启用。", record.Target);
        }
        if (kind == "restore")
        {
            var intent = ReadRestoreIntents(application).Single(i => i.OperationId == operation);
            return Result("CopyIntentPrepared", "原恢复复制意图已登记；复制、接管提交、责任确认及当前启停分别按原证据核对。", intent.Source);
        }
        if (kind == "migration")
        {
            var record = ReadRestoreCommit(application, operation);
            var state = RestoreDataReady(current ?? record.Previous, record) ? "AdoptedIsolated" : RestoreCommitState(record, current);
            return Result(state, state == "Superseded" ? "原恢复已被后续部署取代，只读保管原提交。" : "原恢复提交记录已读取；恢复责任确认和当前业务启停另读原站审计，不能从部署提交推断。", record.Next);
        }
        if (kind == "remove")
        {
            var record = RemovalRecords(application).Single(p => p.File == file).State;
            var receipt = ObserveRemoval(new(recorded, file, [record.Location.DataRoot, record.Location.ConfigurationDirectory], record.RestartRequired,
                record.Remaining ?? [], record.Location.ReleaseRoot), record.Files);
            var retired = record.Location with { LifecycleState = "RemovedDataRetained", MaintenanceOperation = file };
            var superseded = DeploymentPrecedes(retired, current, history);
            return Result(superseded ? "Superseded" : receipt.Status,
                "原卸载回执保留。" + (superseded ? "已被后续部署取代，不能重做旧卸载；" : "") +
                (receipt.RestartRequired ? "原清单仍有重启待删文件。" : "已只读核对原程序余项。") + "数据删除及历史资产分别查看对应原操作。", record.Location, receipt.RemainingFiles);
        }
        if (kind == "prune")
        {
            var record = ReadPrune(application, operation);
            var remaining = ObserveVersionPruneRemaining(record);
            return Result(record.Status, record.Status == "RetainedUnverified" ? "原余项已选择保留，未逐项确认。" : "原清理提交结果保留；当前已知清单余项见详情，续办仍核验当前引用。",
                OriginalDeployment(record.Plan.ExpectedRegistrationHash), remaining, record.Plan.Retain, record.Status is "Prepared" or "Partial");
        }
        if (kind == "assets")
        {
            var record = ReadHistoricalAssets(application, operation);
            var original = HistoricalAssetLocation(application, record);
            var projected = ProjectUninstallAssets(record, file);
            return Result(projected.Status, projected.Status == "RestartRequired" ? "原资产清理已登记重启删除，尚未实际清完；新部署不在原清单内。" : "只读核对原卸载资产范围；新部署不改变原余项，也不能扩展旧清单。",
                original, projected.Remaining, projected.Retained, projected.Status != "Completed")
                with
            { TotalRemaining = projected.TotalRemaining, TotalRetained = projected.TotalRetained };
        }
        var purge = Wire.Decode<DeploymentPurgeRecord>(json);
        if (purge.SchemaVersion != 2 || purge.Plan.ApplicationId != application || purge.Plan.OperationId != operation)
            throw new InvalidDataException("原数据删除记录身份无效。");
        return Result(purge.Status, "原数据删除回执已读取；这是原冻结范围的结果，不描述后续新部署的数据。", OriginalDeployment(purge.Plan.ExpectedRegistrationHash), purge.Remaining);
    }

    static UninstallAssetsRecord ReadHistoricalAssets(string application, string operation)
    {
        var file = AssetRecordFile(application, operation); MachineRuntimePolicy.RequireProtectedEvidence(file);
        if (new FileInfo(file).Length > 32 * 1024 * 1024) throw new InvalidDataException("原资产记录超过预算。");
        var record = Wire.Decode<UninstallAssetsRecord>(File.ReadAllText(file));
        if (record.SchemaVersion != 1 || record.Plan.OperationId != operation || record.Plan.ApplicationId != application || record.Identity != Wire.Hash(Wire.Encode(record.Plan)))
            throw new InvalidDataException("原资产记录身份冲突。");
        return record;
    }
    static DeploymentLocation HistoricalAssetLocation(string application, UninstallAssetsRecord record)
    {
        var originals = RemovalRecords(application).Select(r => r.State.Location).Where(l => UninstallScope(l) == record.Plan.Scope).ToArray();
        if (originals.Length != 1) throw new InvalidDataException("无法唯一核对原资产所属退役部署，原范围保留。");
        return originals[0] with { LifecycleState = "RemovedDataRetained" };
    }

    [SupportedOSPlatform("windows")]
    public static UninstallAssetsReceipt ResumeHistoricalAssets(string application, string operation, MaintenanceExecution? execution = null)
    {
        MachineRuntimePolicy.RequireExtensionInstallerAuthority();
        using var owner = new FileStream(Path.Combine(MachineRoot, "deployments", application + ".lease"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using var othersOwner = AcquireOtherPurgeDeployments(application, MachineRoot);
        var current = Current(application) ?? throw new InvalidDataException("当前登记缺失，无法复核引用。");
        using var stopped = new StoppedApplicationOwnership(MachineRuntimePolicy.InstallerLeaseRoot(""), application, current.DataRoot, execution?.Token ?? default);
        var record = ReadHistoricalAssets(application, operation); var original = HistoricalAssetLocation(application, record);
        var references = ReadVersionReferences(Path.Combine(MachineRoot, "deployments")).Where(l => l.LifecycleState == "Installed" || UninstallScope(l) != record.Plan.Scope).ToArray();
        foreach (var area in record.Plan.Delete)
        {
            RequireUninstallAssetRoot(area.Root, original, MachineRoot, references);
            if (!record.Plan.Caches.Contains(area.Root, StringComparer.OrdinalIgnoreCase)) _ = TrustedProgramRoots.RequireBoundary(area.Root);
        }
        RequirePruneNotExecuting(record.Plan.Delete.Where(a => !record.Plan.Caches.Contains(a.Root, StringComparer.OrdinalIgnoreCase)).Select(a => a.Root));
        RequireProtectedPruneFiles(record.Plan.Delete);
        return ApplyUninstallAssets(record, AssetRecordFile(application, operation), original,
            state => WriteProtected(AssetRecordFile(application, operation), state, new SecurityIdentifier(original.RuntimeAccountSid)),
            root => TrustedProgramRoots.Contains(root, AppContext.BaseDirectory), QueueUninstallCacheDeletion, execution);
    }
}

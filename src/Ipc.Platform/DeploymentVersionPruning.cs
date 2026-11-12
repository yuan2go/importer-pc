using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text.Json;

namespace Ipc.Platform;

public sealed record DeploymentVersionFiles(string Root, Dictionary<string, string> Files, int? ReviewedFileCount = null);
public sealed record DeploymentVersionRetention(string Root, string Reason);
public sealed record DeploymentPrunePlan(string Identity, string OperationId, string ApplicationId, string ExpectedRegistrationHash,
    bool Allowed, string Reason, DeploymentVersionFiles[] Delete, DeploymentVersionRetention[] Retain, bool OrdinaryStartupConfirmed);
public sealed record DeploymentPruneReceipt(string OperationId, string Status, string Detail, string[] Remaining, string OperationFile,
    int? TotalRemaining = null, bool RemainingTruncated = false);
internal sealed record DeploymentPruneRecord(int SchemaVersion, DateTimeOffset At, DeploymentPrunePlan Plan,
    string Status, string[] Remaining, string WindowsAdministratorSid,
    string? DispositionAdministratorSid = null, DateTimeOffset? DispositionAt = null);

public static partial class WindowsDeploymentSetup
{
    static string PruneFile(string application, string operation)
    {
        _ = RollbackFile(application, operation); // Same bounded identity grammar.
        return Path.Combine(MachineRoot, "deployments", application + ".prune-" + operation + ".json");
    }

    [SupportedOSPlatform("windows")]
    public static DeploymentPrunePlan PlanVersionPruning(string application, string operation, bool ordinaryStartupConfirmed)
    {
        using var lease = new FileStream(Path.Combine(MachineRoot, "deployments", application + ".lease"), FileMode.Open, FileAccess.Read, FileShare.None);
        return PlanVersionPruningCore(application, operation, ordinaryStartupConfirmed);
    }

    [SupportedOSPlatform("windows")]
    static DeploymentPrunePlan PlanVersionPruningCore(string application, string operation, bool ordinaryStartupConfirmed)
    {
        _ = PruneFile(application, operation);
        var current = Current(application) ?? throw new InvalidDataException("缺少唯一部署登记。");
        var hash = Wire.FileHash(Registration(application));
        DeploymentPrunePlan Denied(string why) => new("", operation, application, hash, false, why, [], [], ordinaryStartupConfirmed);
        if (!ordinaryStartupConfirmed) return Denied("须先由管理员确认当前版本已普通启动并完成必要观察；此确认不代表设备或现场验收。");
        if (current.LifecycleState != "Installed")
            return Denied("当前程序已退役，不能发起版本清理。");
        RequireInstalledReady(current); RequireNoUnresolvedInstallation(application, null); RequireNoUnresolvedLifecycle(application);
        var left = RuntimeCompatibility(Path.Combine(current.ReleaseRoot, "app"));
        var leftDesktop = RuntimeCompatibility(Path.Combine(current.ReleaseRoot, "desktop"));
        _ = DeploymentMedia.Inspect(current.ReleaseRoot, application);
        var directory = Path.Combine(MachineRoot, "deployments");
        var journals = Directory.EnumerateFiles(directory, application + ".setup-*.json").Order(StringComparer.Ordinal).Take(129).ToArray();
        if (journals.Length > 128) return Denied("原安装记录超出清理预算，保留版本并由实施人员核对。");
        var history = ReadDeploymentHistory(application);
        var candidates = new List<(DeploymentLocation Location, int Distance, DeploymentVersionFiles[] Files)>();
        var retained = new List<DeploymentVersionRetention> { new(current.ReleaseRoot, "当前版本") };
        if (current.PreviousDeployment is { } previous) retained.Add(new(previous.ReleaseRoot, "保留的上一版本"));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.GetFullPath(current.ReleaseRoot) };
        var verifiedPredecessor = false;
        var references = ReadVersionReferences(directory);
        var pinned = Directory.EnumerateFiles(directory, application + ".prune-*.json").Take(129)
            .Select(file => ReadPrune(application, Path.GetFileNameWithoutExtension(file)[(application.Length + ".prune-".Length)..]))
            .Where(r => r.Status == "RetainedUnverified").SelectMany(r => r.Plan.Delete.Select(d => d.Root)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var file in journals)
        {
            MachineRuntimePolicy.RequireProtectedEvidence(file);
            if (new FileInfo(file).Length > 4 * 1024 * 1024) return Denied("原安装库存超出核对预算。");
            using var document = JsonDocument.Parse(File.ReadAllBytes(file));
            if (document.RootElement.GetProperty("Status").GetString() != "Completed") continue;
            var location = Wire.Decode<DeploymentLocation>(document.RootElement.GetProperty("Location").GetRawText());
            if (!seen.Add(Path.GetFullPath(location.ReleaseRoot)) || !Directory.Exists(location.ReleaseRoot)) continue;
            try
            {
                if (location.ApplicationId != application || location.ConfigurationDirectory != current.ConfigurationDirectory ||
                    location.RuntimeAccountSid != current.RuntimeAccountSid || location.LifecycleState != "Installed")
                    throw new InvalidDataException("其他根或身份的历史版本");
                var boundary = TrustedProgramRoots.RequireBoundary(location.ReleaseRoot);
                RequireVersionDirectory(boundary, location);
                var release = RuntimeCompatibility(Path.Combine(location.ReleaseRoot, "app"));
                var desktop = RuntimeCompatibility(Path.Combine(location.ReleaseRoot, "desktop"));
                var distance = DeploymentDistance(location, current, history);
                if (distance is not > 0) throw new InvalidDataException("不能证明是当前部署之前的历史构建，保留原目录");
                if (DeploymentRuntimeCompatibility.Incompatibility(left.Compatibility, release.Compatibility) is not null ||
                    DeploymentRuntimeCompatibility.Incompatibility(leftDesktop.Compatibility, desktop.Compatibility) is not null)
                    throw new InvalidDataException("兼容/原实现引用不能证明，保留版本");
                var cache = RequireMaintenanceCache(location);
                if (pinned.Contains(location.ReleaseRoot) || pinned.Contains(cache))
                    throw new InvalidDataException("原清理已明确保留余项，不纳入新的删除范围");
                RequirePruneNotExecuting([location.ReleaseRoot, cache]);
                var media = DeploymentMedia.Inspect(location.ReleaseRoot, application);
                if (media.SourceCommit != location.SourceCommit) throw new InvalidDataException("程序源身份与原库存不同");
                _ = InstalledInventory(location);
                using var inventory = JsonDocument.Parse(File.ReadAllBytes(location.InventoryFile!));
                var original = inventory.RootElement.GetProperty("Files").EnumerateObject()
                    .ToDictionary(p => p.Name, p => p.Value.GetString()!, StringComparer.OrdinalIgnoreCase);
                if (original.Count != media.Files.Count || original.Any(p => media.Files.GetValueOrDefault(p.Key) != p.Value))
                    throw new InvalidDataException("程序与受保护原库存不同");
                var programFiles = new Dictionary<string, string>(original, StringComparer.OrdinalIgnoreCase);
                var descriptor = Path.Combine(location.ReleaseRoot, "deployment-location.json");
                MachineRuntimePolicy.RequireProtectedEvidence(descriptor);
                var described = Wire.Decode<DeploymentLocation>(File.ReadAllText(descriptor));
                if (described.ApplicationId != application || described.SourceCommit != location.SourceCommit || described.ReleaseRoot != location.ReleaseRoot)
                    throw new InvalidDataException("原版本描述不属于库存");
                if (location.ReleaseRoot.Equals(current.PreviousDeployment?.ReleaseRoot, StringComparison.OrdinalIgnoreCase))
                { verifiedPredecessor = true; retained.Add(new(location.ReleaseRoot, "已核验完整的上一部署回滚候选")); }
                if (references.Any(r => r.ReleaseRoot.Equals(location.ReleaseRoot, StringComparison.OrdinalIgnoreCase) ||
                    r.MaintenanceRoot?.Equals(cache, StringComparison.OrdinalIgnoreCase) == true ||
                    r.PreviousDeployment?.ReleaseRoot.Equals(location.ReleaseRoot, StringComparison.OrdinalIgnoreCase) == true))
                    throw new InvalidDataException("当前登记或上一版本仍引用");
                programFiles.Add("deployment-location.json", Wire.FileHash(descriptor));
                var cacheFiles = new Dictionary<string, string>(original, StringComparer.OrdinalIgnoreCase)
                { ["installed-inventory.json"] = location.InventorySha256! };
                candidates.Add((location, distance.Value, [new(location.ReleaseRoot, programFiles), new(cache, cacheFiles)]));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException)
            { retained.Add(new(location.ReleaseRoot, "保留：" + WindowsInstallationError.Safe(error.Message))); }
        }
        return BuildVersionPruningPlan(application, operation, ordinaryStartupConfirmed, current, hash, history,
            candidates.Select(c => (c.Location, c.Files)).ToArray(), retained.ToArray(), verifiedPredecessor);
    }

    // Both the normal entry and regression tests generate the executable plan
    // here. File/ACL/media checks above produce candidates, never allowed plans.
    internal static DeploymentPrunePlan BuildVersionPruningPlan(string application, string operation, bool observed,
        DeploymentLocation current, string hash, DeploymentTransition[] history,
        (DeploymentLocation Location, DeploymentVersionFiles[] Files)[] candidates, DeploymentVersionRetention[] retained, bool verifiedPredecessor = false)
    {
        if (!observed || current.LifecycleState != "Installed")
            return new("", operation, application, hash, false, "当前部署尚未确认普通启动，或已退役；原构建保留。", [], retained, observed);
        var keep = retained.ToList();
        var eligible = new List<(DeploymentLocation Location, DeploymentVersionFiles[] Files, int Distance)>();
        foreach (var candidate in candidates)
        {
            var distance = DeploymentDistance(candidate.Location, current, history);
            if (candidate.Location.ApplicationId != application || candidate.Location.RuntimeAccountSid != current.RuntimeAccountSid || distance is not > 0)
                keep.Add(new(candidate.Location.ReleaseRoot, "无法证明是本站当前部署之前的构建，保留原件"));
            else eligible.Add((candidate.Location, candidate.Files, distance.Value));
        }
        // The ordinary reader has already verified and retained a directly
        // registered predecessor. Otherwise keep the closest verified ancestor.
        var fallback = verifiedPredecessor ? default : eligible.OrderBy(c => c.Distance).FirstOrDefault();
        if (fallback.Location is not null) keep.Add(new(fallback.Location.ReleaseRoot, "最近一个可核验兼容回滚候选"));
        var remove = eligible.Where(c => c.Location != fallback.Location && !keep.Any(r => r.Root.Equals(c.Location.ReleaseRoot, StringComparison.OrdinalIgnoreCase)))
            .SelectMany(c => c.Files).ToArray();
        var identity = VersionPruneIdentity(application, operation, hash, remove, observed);
        var result = new DeploymentPrunePlan(identity, operation, application, hash, remove.Length > 0,
            remove.Length > 0 ? "仅删除列出的历史构建和维护缓存；完整备份后执行，业务数据及原回执保留。" : "没有可安全删除的旧构建；当前、回滚候选及证据不足项保留。",
            remove, keep.ToArray(), observed);
        if (System.Text.Encoding.UTF8.GetByteCount(Wire.Encode(result)) > 3 * 1024 * 1024)
            return new("", operation, application, hash, false, "清理清单超过固定预算，保留所有版本。", [], keep.ToArray(), observed);
        return result;
    }

    internal static void RequireVersionDirectory(string boundary, DeploymentLocation location)
    {
        if (!ExtensionSchema.IsId(location.ApplicationId) || location.SourceCommit.Length != 40 || !location.SourceCommit.All(char.IsAsciiHexDigit))
            throw new InvalidDataException("旧版本发布身份无效。");
        var parent = Path.Combine(Path.GetFullPath(boundary), "releases");
        var name = Path.GetFileName(location.ReleaseRoot);
        var prefix = location.ApplicationId + "-" + location.SourceCommit[..12] + "-";
        if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(location.ReleaseRoot)), parent, StringComparison.OrdinalIgnoreCase) || !name.StartsWith(prefix, StringComparison.Ordinal) ||
            !Guid.TryParseExact(name[prefix.Length..], "N", out var id) || id == Guid.Empty ||
            TrustedProgramRoots.Contains(location.ReleaseRoot, location.DataRoot) || TrustedProgramRoots.Contains(location.ReleaseRoot, location.ConfigurationDirectory))
            throw new InvalidDataException("旧版本目录不属于固定安装范围。");
    }

    static DeploymentLocation[] ReadVersionReferences(string directory)
    {
        var files = Directory.EnumerateFiles(directory, "*.json").Order(StringComparer.Ordinal).Take(513).ToArray();
        if (files.Length > 512) throw new InvalidDataException("部署引用超过核对预算，所有版本保留。");
        long bytes = 0; var references = new List<DeploymentLocation>();
        foreach (var file in files)
        {
            MachineRuntimePolicy.RequireProtectedEvidence(file);
            bytes = checked(bytes + new FileInfo(file).Length);
            if (bytes > 16 * 1024 * 1024) throw new InvalidDataException("部署引用超过读取预算，所有版本保留。");
            using var document = JsonDocument.Parse(File.ReadAllBytes(file));
            var value = document.RootElement;
            if (value.TryGetProperty("ApplicationId", out _) && value.TryGetProperty("ReleaseRoot", out _) && value.TryGetProperty("RuntimeAccountSid", out _))
                references.Add(Wire.Decode<DeploymentLocation>(value.GetRawText()));
            else if (Path.GetFileName(file).StartsWith("sportmaster.restore-", StringComparison.Ordinal))
            {
                var intent = Wire.Decode<DeploymentRestoreIntent>(value.GetRawText()); ValidateRestoreIntent(intent);
                if (file != RestoreIntentFile(intent.ApplicationId, intent.OperationId)) throw new InvalidDataException("恢复引用不属于原操作。");
                var committed = File.Exists(RestoreCommitFile(intent.ApplicationId, intent.OperationId)) ? ReadRestoreCommit(intent.ApplicationId, intent.OperationId) : null;
                if (committed?.Status == "CompletedIsolated") continue;
                var active = Current(intent.ApplicationId); var history = ReadDeploymentHistory(intent.ApplicationId);
                var rootChanged = active is not null && active.DataRoot != intent.Source.DataRoot && DeploymentPrecedes(intent.Source, active, history);
                var retired = history.Any(e => e.Next.LifecycleState != "Installed" &&
                    (DeploymentNode(intent.Source) == DeploymentNode(e.Previous) || DeploymentPrecedes(intent.Source, e.Previous, history)) &&
                    (active is not null && DeploymentNode(e.Next) == DeploymentNode(active) || DeploymentPrecedes(e.Next, active, history)));
                if (!rootChanged && !retired) references.Add(intent.Source);
            }
        }
        return references.ToArray();
    }

    [SupportedOSPlatform("windows")]
    public static DeploymentPruneReceipt PruneVersions(DeploymentPrunePlan reviewed, MaintenanceExecution? execution = null)
    {
        MachineRuntimePolicy.RequireExtensionInstallerAuthority();
        using var lease = new FileStream(Path.Combine(MachineRoot, "deployments", reviewed.ApplicationId + ".lease"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var path = PruneFile(reviewed.ApplicationId, reviewed.OperationId);
        if (File.Exists(path)) throw new InstallationOutcomeUnknownException("原清理记录已存在，请核对或续办原次，不能换清单重复提交。");
        using var otherDeployments = AcquireOtherPurgeDeployments(reviewed.ApplicationId, MachineRoot);
        var actual = PlanVersionPruningCore(reviewed.ApplicationId, reviewed.OperationId, reviewed.OrdinaryStartupConfirmed);
        if (!actual.Allowed || actual.Identity != reviewed.Identity) throw new InvalidOperationException("版本清理计划已变化，请重新检查。");
        using var identity = WindowsIdentity.GetCurrent();
        var record = new DeploymentPruneRecord(1, DateTimeOffset.UtcNow, actual, "Prepared", [], identity.User!.Value);
        var current = Current(actual.ApplicationId)!;
        using var rootOwnership = actual.ApplicationId == "sportmaster" ? ApplicationRootOwnership.AcquireMaintenance(actual.ApplicationId, current.DataRoot) : null;
        RequirePruneExcludesCurrent(actual, current);
        RequirePruneNotExecuting(actual.Delete.Select(a => a.Root));
        RequireProtectedPruneFiles(actual.Delete);
        return ApplyVersionPruning(record, path, () => Wire.FileHash(Registration(actual.ApplicationId)),
            state => SavePrune(path, state, current.RuntimeAccountSid), execution);
    }

    static void RequirePruneNotExecuting(IEnumerable<string> roots)
    {
        var areas = roots.ToArray();
        if (areas.Any(root => TrustedProgramRoots.Contains(root, AppContext.BaseDirectory)))
            throw new InvalidOperationException("此历史构建或缓存仍是当前维护宿主，保留全部原文件。");
        // Installed hosts have fixed executable names. Do not terminate them or
        // remove their not-yet-loaded files while a maintenance process is alive.
        foreach (var name in new[] { "Ipc.Assembly", "Ipc.Desktop", "Ipc.Installer" })
        {
            var processes = System.Diagnostics.Process.GetProcessesByName(name);
            try
            {
                if (processes.Length > 128) throw new InvalidOperationException("维护进程超过核对预算，旧构建保留。");
                foreach (var process in processes)
                {
                    try
                    {
                        if (process.HasExited) continue;
                        var executable = process.MainModule?.FileName ?? throw new InvalidOperationException("运行中维护宿主路径待核实，旧构建保留。");
                        if (areas.Any(root => TrustedProgramRoots.Contains(root, executable))) throw new InvalidOperationException("历史构建或缓存仍有运行进程，关闭原宿主后再清理。");
                    }
                    catch (System.ComponentModel.Win32Exception error) { throw new InvalidOperationException("无法核对运行中的维护宿主，旧构建保留。", error); }
                }
            }
            finally { foreach (var process in processes) process.Dispose(); }
        }
    }

    static string VersionPruneIdentity(string application, string operation, string hash, DeploymentVersionFiles[] remove, bool ordinaryStartupConfirmed)
        => Wire.Hash(Wire.Encode(new { application, operation, hash, remove, ordinaryStartupConfirmed }));

    static void RequirePruneExcludesCurrent(DeploymentPrunePlan plan, DeploymentLocation current)
    {
        var pinned = new[] { current.ReleaseRoot, current.MaintenanceRoot, current.DataRoot, current.ConfigurationDirectory, MachineRoot,
            current.PreviousDeployment?.ReleaseRoot, current.PreviousDeployment?.MaintenanceRoot }.Where(p => p is not null).Cast<string>();
        if (plan.Delete.Any(d => pinned.Any(p => TrustedProgramRoots.Contains(d.Root, p) || TrustedProgramRoots.Contains(p, d.Root))))
            throw new InvalidDataException("原清单与当前程序、数据、政策或回退候选重叠，拒绝删除。");
    }

    [SupportedOSPlatform("windows")]
    static void SavePrune(string path, DeploymentPruneRecord record, string runtimeSid)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(Wire.Encode(record)) > 4 * 1024 * 1024)
            throw new InvalidDataException("版本清理回执超过读取预算，原意图保留，请核对原次。");
        WriteProtected(path, record, new SecurityIdentifier(runtimeSid));
    }

    [SupportedOSPlatform("windows")]
    static void RequireProtectedPruneFiles(DeploymentVersionFiles[] areas)
    {
        foreach (var area in areas)
        {
            DeploymentPaths.RequireOrdinaryAncestors(area.Root);
            if (Directory.Exists(area.Root)) TrustedProgramRoots.RequireAncestors(area.Root);
            foreach (var name in area.Files.Keys)
            {
                var file = ResolvePruneFile(area.Root, name);
                if (File.Exists(file)) MachineRuntimePolicy.RequireProtectedEvidence(file);
            }
        }
    }

    internal static DeploymentPruneReceipt ApplyVersionPruning(DeploymentPruneRecord record, string path, Func<string> registrationHash,
        Action<DeploymentPruneRecord> save, MaintenanceExecution? execution = null, bool referencesRevalidated = false)
    {
        if (!record.Plan.Allowed || !record.Plan.OrdinaryStartupConfirmed || !referencesRevalidated && registrationHash() != record.Plan.ExpectedRegistrationHash)
            throw new InvalidOperationException("登记或普通启动确认不符合原清理计划。");
        if (record.Plan.Delete.Any(d => record.Plan.Retain.Any(r => TrustedProgramRoots.Contains(d.Root, r.Root) || TrustedProgramRoots.Contains(r.Root, d.Root))))
            throw new InvalidDataException("删除范围与保留版本重叠。");
        ValidatePruneFiles(record.Plan.Delete);
        execution?.Checkpoint(); save(record with { Status = "Prepared" });
        try { execution?.BeginCommit(MaintenanceStage.RemovingProgram); }
        catch (OperationCanceledException cancelled)
        {
            try { save(record with { Status = "NotStarted", Remaining = [] }); }
            catch (Exception receiptFailure) { throw new InstallationOutcomeUnknownException("删除前取消的回执未确认；原范围保留，请核对原次。", new AggregateException(cancelled, receiptFailure)); }
            throw;
        }
        try
        {
            var remaining = record.Plan.Delete.SelectMany(area => CleanRemovalInventory(area.Root, area.Files.Keys.ToArray(), _ => false, reviewedHashes: area.Files)
                .Remaining.Select(name => Path.IsPathFullyQualified(name) ? name : Path.Combine(area.Root, name))).ToArray();
            var pending = record.Plan.Delete.Any(area => area.Files.Keys.Any(name => File.Exists(Path.Combine(area.Root, name))));
            var status = pending ? "Partial" : remaining.Length == 0 ? "Completed" : "CompletedWithRetainedFiles";
            save(record with { Status = status, Remaining = remaining });
            return new(record.Plan.OperationId, status, status switch
            {
                "Completed" => "原清单旧版本已清理；当前、回退候选、数据与原回执保留。",
                "CompletedWithRetainedFiles" => "原清单已清理；目录内的清单外文件保留，请由实施人员另行核对。",
                _ => "部分旧文件仍被占用；保留原次，可关闭占用程序后继续清理。未安排重启删除。"
            }, remaining, path);
        }
        catch (Exception failure)
        { throw new InstallationOutcomeUnknownException("清理结果未确认；只核对原清单，不能创建新范围。", failure); }
    }

    internal static void ValidatePruneFiles(DeploymentVersionFiles[] areas, CancellationToken cancellationToken = default)
    {
        if (areas.Length is 0 or > 256 || areas.Select(a => Path.GetFullPath(a.Root)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != areas.Length)
            throw new InvalidDataException("清理目录数量或范围重复。");
        foreach (var area in areas)
        {
            DeploymentPaths.RequireOrdinaryAncestors(area.Root);
            if (!Path.IsPathFullyQualified(area.Root) || Path.GetPathRoot(area.Root) == Path.GetFullPath(area.Root) || area.Files.Count is 0 or > 16384 ||
                area.Files.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != area.Files.Count ||
                areas.Any(other => !ReferenceEquals(area, other) && TrustedProgramRoots.Contains(area.Root, other.Root)))
                throw new InvalidDataException("清理库存范围无效或重叠。");
            foreach (var file in area.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var full = ResolvePruneFile(area.Root, file.Key);
                if (!ExtensionSchema.IsHash(file.Value))
                    throw new InvalidDataException("清理文件路径或字节身份无效。");
                DeploymentPaths.RequireOrdinaryAncestors(full);
                if (Directory.Exists(full) || File.Exists(full) && Wire.FileHash(full, cancellationToken) != file.Value)
                    throw new InvalidDataException("旧文件已变化；删除前保留整个原清单。");
            }
        }
    }

    static string ResolvePruneFile(string root, string name)
    {
        if (name.Length > 1024 || !ExtensionPackages.SafePath(name)) throw new InvalidDataException("原清理文件路径无效。");
        var path = Path.GetFullPath(Path.Combine(root, name));
        if (!TrustedProgramRoots.Contains(root, path) || Path.GetFullPath(root).Equals(path, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("原清理文件超出范围。");
        DeploymentPaths.RequireOrdinaryAncestors(path);
        return path;
    }

    static DeploymentPruneRecord ReadPrune(string application, string operation)
    {
        var path = PruneFile(application, operation); MachineRuntimePolicy.RequireProtectedEvidence(path);
        if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException("原版本清理记录超出预算。");
        var record = Wire.Decode<DeploymentPruneRecord>(File.ReadAllText(path));
        if (record.SchemaVersion != 1 || record.Plan.ApplicationId != application || record.Plan.OperationId != operation ||
            record.Plan.Identity != VersionPruneIdentity(application, operation, record.Plan.ExpectedRegistrationHash, record.Plan.Delete, record.Plan.OrdinaryStartupConfirmed) ||
            record.Status is not ("Prepared" or "Partial" or "NotStarted" or "Completed" or "CompletedWithRetainedFiles" or "RetainedUnverified")) throw new InvalidDataException("原版本清理记录身份或状态无效。");
        return record;
    }

    internal static string[] ObserveVersionPruneRemaining(DeploymentPruneRecord record)
    {
        var remaining = new List<string>();
        foreach (var area in record.Plan.Delete)
        {
            var observed = ObserveRemoval(new("Unknown", "", [], false, [], area.Root), area.Files.Keys.ToArray());
            foreach (var name in observed.RemainingFiles ?? [])
            {
                if (remaining.Count >= 32768) throw new InvalidDataException("原清理余项超过核对预算，原范围保留。");
                remaining.Add(Path.Combine(area.Root, name));
            }
        }
        return remaining.ToArray();
    }

    [SupportedOSPlatform("windows")]
    public static DeploymentPruneReceipt InspectVersionPruning(string application, string operation, bool resume, MaintenanceExecution? execution = null)
    {
        if (resume) MachineRuntimePolicy.RequireExtensionInstallerAuthority();
        using var lease = new FileStream(Path.Combine(MachineRoot, "deployments", application + ".lease"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var record = ReadPrune(application, operation); var path = PruneFile(application, operation);
        if (resume && record.Status is "Prepared" or "Partial")
        {
            var current = Current(application) ?? throw new InvalidDataException("原登记缺失，不能扩大清理范围。");
            using var rootOwnership = application == "sportmaster" ? ApplicationRootOwnership.AcquireMaintenance(application, current.DataRoot) : null;
            using var otherLeases = AcquireOtherPurgeDeployments(application, MachineRoot);
            RequirePruneExcludesCurrent(record.Plan, current);
            foreach (var reference in ReadVersionReferences(Path.Combine(MachineRoot, "deployments"))) RequirePruneExcludesCurrent(record.Plan, reference);
            RequirePruneNotExecuting(record.Plan.Delete.Select(a => a.Root));
            RequireProtectedPruneFiles(record.Plan.Delete);
            return ApplyVersionPruning(record, path, () => Wire.FileHash(Registration(application)),
                state => SavePrune(path, state, current.RuntimeAccountSid), execution, referencesRevalidated: true);
        }
        var observedRemaining = ObserveVersionPruneRemaining(record);
        return new(operation, record.Status, record.Status switch
        {
            "Completed" or "CompletedWithRetainedFiles" => "原清单清理已完成；清单外保留项见详情。",
            "RetainedUnverified" => "管理员已选择保留原范围余项；未逐项核对，不代表文件全部清理完成。",
            "NotStarted" => "原清理已在删除前取消，原版本全部保留；重新检查后可发起新计划。",
            _ => "原清理未完成；只允许按原固定清单继续，不能重选目录。"
        }, observedRemaining, path);
    }

    [SupportedOSPlatform("windows")]
    public static DeploymentPruneReceipt KeepVersionPruningRemainder(string application, string operation)
    {
        MachineRuntimePolicy.RequireExtensionInstallerAuthority();
        using var lease = new FileStream(Path.Combine(MachineRoot, "deployments", application + ".lease"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var record = ReadPrune(application, operation); var path = PruneFile(application, operation);
        var current = Current(application) ?? throw new InvalidDataException("缺少原部署登记。");
        if (record.Status is "Prepared" or "Partial")
        {
            using var identity = WindowsIdentity.GetCurrent();
            // Disposition only. No file, data, registration or service changes;
            // keep the complete original inventory and disclose unverified roots.
            record = RetainPruneRemainder(record, identity.User!.Value);
            try { SavePrune(path, record, current.RuntimeAccountSid); }
            catch (Exception error) { throw new InstallationOutcomeUnknownException("保留余项的回执未确认，请核对原次；未执行新的文件删除。", error); }
        }
        return new(operation, record.Status, "原清理处理结束；未确认余项保留，不代表全部删除。原库存及回执保留，可继续其他安装维护。", record.Remaining, path);
    }

    internal static DeploymentPruneRecord RetainPruneRemainder(DeploymentPruneRecord record, string windowsSid)
        => record.Status is not ("Prepared" or "Partial") ? record : record with
        {
            Status = "RetainedUnverified",
            DispositionAdministratorSid = windowsSid,
            DispositionAt = DateTimeOffset.UtcNow,
            Remaining = record.Plan.Delete.Select(d => d.Root + "（原范围余项保留，未逐项核对）").ToArray()
        };

    public static string? LastVersionPruning(string application)
    {
        var directory = Path.Combine(MachineRoot, "deployments"); if (!Directory.Exists(directory)) return null;
        var files = Directory.EnumerateFiles(directory, application + ".prune-*.json").Take(129).ToArray();
        if (files.Length > 128) throw new InvalidDataException("版本清理回执超出核对预算。");
        return files.Select(file => ReadPrune(application, Path.GetFileNameWithoutExtension(file)[(application.Length + ".prune-".Length)..]))
            .OrderByDescending(r => r.At).FirstOrDefault()?.Plan.OperationId;
    }

    static void RequireNoUnresolvedPruning(string application)
    {
        var directory = Path.Combine(MachineRoot, "deployments");
        var files = Directory.EnumerateFiles(directory, application + ".prune-*.json").Take(129).ToArray();
        if (files.Length > 128) throw new InvalidDataException("版本清理回执超出核对预算。");
        foreach (var file in files)
            if (ReadPrune(application, Path.GetFileNameWithoutExtension(file)[(application.Length + ".prune-".Length)..]).Status is "Prepared" or "Partial")
                throw new InstallationOutcomeUnknownException("原版本清理尚未结束，请先核对或续办原清单。");
    }
}

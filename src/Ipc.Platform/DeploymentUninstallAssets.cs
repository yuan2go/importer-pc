using System.Text.Json;

namespace Ipc.Platform;

public sealed record UninstallAssetsReceipt(string Status, string OperationFile, string[] Remaining,
    DeploymentVersionRetention[] Retained, int TotalRemaining = 0, int TotalRetained = 0, bool DetailsTruncated = false);
internal sealed record UninstallAssetInventory(string Root, string Json);
internal sealed record UninstallAssetsPlan(string ApplicationId, string Scope, string OperationId,
    DeploymentVersionFiles[] Delete, string[] Caches, DeploymentVersionRetention[] Retained);
internal sealed record UninstallAssetsRecord(int SchemaVersion, DateTimeOffset At, UninstallAssetsPlan Plan,
    string Identity, string Status, string[] Remaining, string? Failure = null);

public static partial class WindowsDeploymentSetup
{
    internal static string UninstallScope(DeploymentLocation location)
        => Wire.Hash(Wire.Encode(location with { LifecycleState = "", MaintenanceOperation = null }));

    // Inventories attest ownership of names. Freeze the actual bytes at admission
    // so damaged owned files can be removed, but replacements after admission cannot.
    internal static UninstallAssetsRecord PlanUninstallAssets(DeploymentLocation location, string operation,
        string machineRoot, string cacheParent, UninstallAssetInventory[] inventories, DeploymentLocation[] others,
        Action<string, string> archiveInventory, CancellationToken cancellationToken = default)
    {
        if (location.ApplicationId != "sportmaster" || location.LifecycleState == "Installed" || inventories.Length > 128)
            throw new InvalidDataException("历史程序清理需要已退役的本站登记及有界库存。");
        var areas = new Dictionary<string, DeploymentVersionFiles>(StringComparer.OrdinalIgnoreCase);
        var caches = new List<string>(); var retained = new List<DeploymentVersionRetention>();
        foreach (var input in inventories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cache = Path.GetFullPath(input.Root);
            var retainedRoot = cache;
            try
            {
                if (Path.GetDirectoryName(cache) != Path.GetFullPath(cacheParent) ||
                    !Guid.TryParseExact(Path.GetFileName(cache), "N", out var id) || id == Guid.Empty)
                    throw new InvalidDataException("维护库存目录身份无效。");
                using var document = JsonDocument.Parse(input.Json);
                var value = document.RootElement;
                var source = value.GetProperty("SourceCommit").GetString()!;
                var program = Path.GetFullPath(value.GetProperty("ReleaseRoot").GetString()!);
                if (value.GetProperty("ApplicationId").GetString() != location.ApplicationId || source.Length != 40 || !source.All(char.IsAsciiHexDigit))
                    throw new InvalidDataException("库存不属于本站应用。");
                var evidence = new DeploymentLocation(location.ApplicationId, source, program, location.ConfigurationDirectory, location.DataRoot, location.RuntimeAccountSid);
                var names = ValidateInstalledInventory(evidence, input.Json);
                RequireUninstallAssetRoot(cache, location, machineRoot, others);
                var originalHash = Wire.Hash(input.Json);
                if (string.Equals(cache, location.MaintenanceRoot, StringComparison.OrdinalIgnoreCase) && originalHash != location.InventorySha256)
                    throw new InvalidDataException("本站原库存身份已变化。");
                archiveInventory(originalHash, input.Json); // Durable safety evidence lives outside deletion roots.
                Add(cache, names.Append("installed-inventory.json")); caches.Add(cache);

                if (!Directory.Exists(program)) continue;
                retainedRoot = program;
                RequireUninstallAssetRoot(program, location, machineRoot, others);
                var prefix = location.ApplicationId + "-" + source[..12] + "-";
                var leaf = Path.GetFileName(program);
                if (Path.GetFileName(Path.GetDirectoryName(program)) != "releases" || !leaf.StartsWith(prefix, StringComparison.Ordinal) ||
                    !Guid.TryParseExact(leaf[prefix.Length..], "N", out var releaseId) || releaseId == Guid.Empty)
                    throw new InvalidDataException("历史程序目录不符合原安装范围。");
                var marker = Path.Combine(program, "deployment-location.json");
                var descriptor = Wire.Decode<DeploymentLocation>(File.ReadAllText(marker));
                if (descriptor.ApplicationId != location.ApplicationId || descriptor.SourceCommit != source ||
                    descriptor.ReleaseRoot != program || descriptor.MaintenanceRoot != cache || descriptor.InventorySha256 != originalHash)
                    throw new InvalidDataException("历史程序描述与受保护库存不一致。");
                var markers = new List<string> { "deployment-location.json" };
                if (File.Exists(Path.Combine(program, "deployment-retired.json")))
                {
                    var retired = Wire.Decode<DeploymentLocation>(File.ReadAllText(Path.Combine(program, "deployment-retired.json")));
                    if (retired.ApplicationId != location.ApplicationId || retired.SourceCommit != source || retired.ReleaseRoot != program)
                        throw new InvalidDataException("历史退役描述不属于原程序。");
                    markers.Add("deployment-retired.json");
                }
                Add(program, names.Concat(markers));
            }
            catch (Exception error) when (error is InvalidDataException or JsonException or InvalidOperationException or ArgumentException or KeyNotFoundException)
            { retained.Add(new(retainedRoot, "库存或历史程序无法核验：" + DiagnosticText.Protect(error.Message))); }
        }
        var plan = new UninstallAssetsPlan(location.ApplicationId, UninstallScope(location), operation,
            areas.Values.OrderBy(x => x.Root, StringComparer.Ordinal).ToArray(), caches.ToArray(), retained.ToArray());
        if (plan.Delete.Length > 256 || plan.Delete.Sum(x => x.Files.Count) > 131072)
            throw new InvalidDataException("历史程序清理超过库存预算，未执行删除。");
        return new(1, DateTimeOffset.UtcNow, plan, Wire.Hash(Wire.Encode(plan)), "Prepared", []);

        void Add(string root, IEnumerable<string> names)
        {
            var files = names.Distinct(StringComparer.OrdinalIgnoreCase).ToDictionary(name => name, name =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var full = ResolvePruneFile(root, name); DeploymentPaths.RequireOrdinaryAncestors(full);
                if (Directory.Exists(full)) throw new InvalidDataException("库存文件已变成目录。");
                return File.Exists(full) ? Wire.FileHash(full, cancellationToken) : new string('0', 64);
            }, StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(root))
            {
                var pending = new Stack<string>(); pending.Push(root); var entries = 0;
                while (pending.TryPop(out var directory))
                    foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (++entries > 32768 || retained.Count >= 4096) throw new InvalidDataException("历史目录或保留项超过枚举预算。");
                        var attributes = File.GetAttributes(entry);
                        if ((attributes & FileAttributes.ReparsePoint) != 0)
                            retained.Add(new(entry, "链接或挂载点保留，不跟随清理。"));
                        else if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
                        else if (!files.ContainsKey(Path.GetRelativePath(root, entry).Replace(Path.DirectorySeparatorChar, '/')))
                            retained.Add(new(entry, "清单外文件保留。"));
                    }
            }
            areas.TryAdd(root, new(root, files));
        }
    }

    internal static void RequireUninstallAssetRoot(string root, DeploymentLocation current, string machineRoot, DeploymentLocation[] others)
    {
        root = Path.GetFullPath(root); DeploymentPaths.RequireOrdinaryAncestors(root);
        var protectedRoots = new[] { current.DataRoot, current.ConfigurationDirectory, machineRoot }
            .Concat(others.SelectMany(x => new[] { x.ReleaseRoot, x.MaintenanceRoot, x.DataRoot, x.ConfigurationDirectory }).OfType<string>());
        if (Path.GetPathRoot(root) == root || protectedRoots.Any(p => TrustedProgramRoots.Contains(root, p) || TrustedProgramRoots.Contains(p, root)))
            throw new InvalidDataException("历史程序范围与数据、安全回执或其他安装重叠。");
    }

    internal static void ValidateUninstallAssets(UninstallAssetsRecord record, DeploymentLocation current, CancellationToken cancellationToken = default)
    {
        if (record.SchemaVersion != 1 || current.LifecycleState == "Installed" || record.Plan.ApplicationId != current.ApplicationId ||
            record.Plan.Scope != UninstallScope(current) || record.Identity != Wire.Hash(Wire.Encode(record.Plan)) ||
            record.Status is not ("Prepared" or "Cleaning" or "Partial" or "RestartRequired" or "Completed" or "NotStarted"))
            throw new InvalidDataException("历史程序清理记录不属于当前退役安装。");
        if (record.Plan.Delete.Length != 0) ValidatePruneFiles(record.Plan.Delete, cancellationToken);
        if (record.Plan.Caches.Any(root => !record.Plan.Delete.Any(a => a.Root == root)))
            throw new InvalidDataException("待重启缓存不属于冻结清单。");
    }

    internal static UninstallAssetsReceipt ProjectUninstallAssets(UninstallAssetsRecord record, string path)
    {
        var remaining = record.Plan.Delete.Where(a => Directory.Exists(a.Root) || File.Exists(a.Root)).Select(a => a.Root).ToArray();
        var status = record.Plan.Retained.Length != 0 ? "Partial" : remaining.Length == 0 ? "Completed" : record.Status == "RestartRequired" ? "RestartRequired" : "Partial";
        string Preview(string value, int limit) => value.Length <= limit ? value : value[..limit] + "…";
        var clipped = remaining.Length > 16 || record.Plan.Retained.Length > 16 || remaining.Any(p => p.Length > 384) ||
            record.Plan.Retained.Any(p => p.Root.Length > 384 || p.Reason.Length > 192);
        return new(status, path, remaining.Take(16).Select(p => Preview(p, 384)).ToArray(),
            record.Plan.Retained.Take(16).Select(p => new DeploymentVersionRetention(Preview(p.Root, 384), Preview(p.Reason, 192))).ToArray(),
            remaining.Length, record.Plan.Retained.Length, clipped);
    }

    internal static UninstallAssetsReceipt ApplyUninstallAssets(UninstallAssetsRecord record, string path, DeploymentLocation current,
        Action<UninstallAssetsRecord> save, Func<string, bool> executingCache, Action<string> queueCacheDelete,
        MaintenanceExecution? execution = null)
    {
        ValidateUninstallAssets(record, current, execution?.Token ?? default);
        execution?.Checkpoint();
        if (record.Status == "RestartRequired" && record.Plan.Caches.Any(executingCache))
            return ProjectUninstallAssets(record, path); // Repeated clicks must not grow the OS queue.
        try { save(record with { Status = "Prepared" }); }
        catch (Exception error) { throw new InstallationOutcomeUnknownException("历史清理意图保存结果未确认；未执行新的文件删除，请核对原次。", error); }
        try
        {
            if (execution is { Committing: false }) execution.BeginCommit(MaintenanceStage.RemovingProgram);
            else execution?.Report(MaintenanceStage.RemovingProgram);
        }
        catch (OperationCanceledException cancelled)
        {
            try { save(record with { Status = "NotStarted" }); }
            catch (Exception error) { throw new InstallationOutcomeUnknownException("清理前取消回执未确认，请核对原次。", new AggregateException(cancelled, error)); }
            throw;
        }
        var restart = false;
        try
        {
            save(record with { Status = "Cleaning" });
            foreach (var area in record.Plan.Delete)
            {
                if (record.Plan.Caches.Contains(area.Root, StringComparer.OrdinalIgnoreCase) && executingCache(area.Root))
                {
                    // Keep the live host intact. Only its fixed, reviewed cache is
                    // queued; Windows removes directories only when they are empty.
                    foreach (var name in area.Files.Keys.Where(n => File.Exists(Path.Combine(area.Root, n)))) queueCacheDelete(Path.Combine(area.Root, name));
                    foreach (var dir in area.Files.Keys.Select(n => Path.GetDirectoryName(Path.Combine(area.Root, n))!)
                        .SelectMany(p => Parents(p, area.Root)).Append(area.Root).Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(p => p.Length))
                        if (Directory.Exists(dir)) queueCacheDelete(dir);
                    restart = true;
                }
                else _ = CleanRemovalInventory(area.Root, area.Files.Keys.ToArray(), _ => false, reviewedHashes: area.Files);
            }
            var result = ProjectUninstallAssets(record with { Status = restart ? "RestartRequired" : "Partial" }, path);
            save(record with { Status = result.Status, Remaining = result.Remaining });
            return result;
        }
        catch (Exception failure) when (failure is not InstallationOutcomeUnknownException)
        { throw new InstallationOutcomeUnknownException("历史程序或维护缓存清理结果未确认；请查看原清单，不创建另一份删除范围。", failure); }
    }
}

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;

namespace Ipc.Platform;

public static partial class WindowsDeploymentSetup
{
    static string AssetInventoryArchive(string sha) => Path.Combine(MachineRoot, "deployments", "sportmaster.inventory-" + sha + ".json");
    static string AssetRecordFile(string application, string operation)
    {
        _ = RemovalOperationFile(application, operation);
        return Path.Combine(MachineRoot, "deployments", application + ".assets-" + operation + ".json");
    }

    static (string Path, UninstallAssetsRecord Record)? FindUninstallAssets(DeploymentLocation current)
    {
        var files = Directory.EnumerateFiles(Path.Combine(MachineRoot, "deployments"), current.ApplicationId + ".assets-*.json").Take(129).ToArray();
        if (files.Length > 128) throw new InvalidDataException("历史卸载清理记录超过核对预算。");
        var matching = new List<(string Path, UninstallAssetsRecord Record)>(); long bytes = 0;
        foreach (var file in files)
        {
            MachineRuntimePolicy.RequireProtectedEvidence(file);
            bytes = checked(bytes + new FileInfo(file).Length);
            if (bytes > 64 * 1024 * 1024 || new FileInfo(file).Length > 32 * 1024 * 1024) throw new InvalidDataException("历史清理记录超过读取预算。");
            var record = Wire.Decode<UninstallAssetsRecord>(File.ReadAllText(file));
            if (AssetRecordFile(record.Plan.ApplicationId, record.Plan.OperationId) != file || record.Identity != Wire.Hash(Wire.Encode(record.Plan)))
                throw new InvalidDataException("历史清理记录身份无效。");
            if (record.Plan.Scope == UninstallScope(current)) matching.Add((file, record));
        }
        if (matching.Count > 1) throw new InstallationOutcomeUnknownException("同一次退役安装存在多个清理记录，请先核对原结果。");
        return matching.Count == 0 ? null : matching[0];
    }

    public static UninstallAssetsReceipt? InspectUninstallAssets(string application)
    {
        using var lease = new FileStream(Path.Combine(MachineRoot, "deployments", application + ".lease"), FileMode.Open, FileAccess.Read, FileShare.None);
        return InspectUninstallAssetsCore(Current(application) ?? throw new InvalidDataException("缺少本站登记。"));
    }
    static UninstallAssetsReceipt? InspectUninstallAssetsCore(DeploymentLocation current)
    {
        var original = FindUninstallAssets(current);
        if (original is null) return null;
        ValidateUninstallAssets(original.Value.Record, current);
        return ProjectUninstallAssets(original.Value.Record, original.Value.Path);
    }

    [SupportedOSPlatform("windows")]
    public static UninstallAssetsReceipt CleanUninstallAssets(string application, string expectedHash, MaintenanceExecution? execution = null)
    {
        MachineRuntimePolicy.RequireExtensionInstallerAuthority();
        using var lease = new FileStream(Path.Combine(MachineRoot, "deployments", application + ".lease"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using var otherLeases = AcquireOtherPurgeDeployments(application, MachineRoot);
        if (Wire.FileHash(Registration(application)) != expectedHash) throw new InvalidOperationException("安装信息已变化，未清理历史文件。");
        var current = Current(application) ?? throw new InvalidDataException("缺少本站登记。");
        if (application != "sportmaster" || current.LifecycleState is not ("RemovedDataRetained" or "Purged"))
            throw new InvalidOperationException("请先完成原程序和数据删除事务，再清理历史文件。");
        using var ownership = new StoppedApplicationOwnership(MachineRuntimePolicy.InstallerLeaseRoot(""), application, current.DataRoot, execution?.Token ?? default);
        var others = ReadVersionReferences(Path.Combine(MachineRoot, "deployments")).Where(x => x.ApplicationId != application).ToArray();
        var original = FindUninstallAssets(current);
        var sid = new SecurityIdentifier(current.RuntimeAccountSid);
        UninstallAssetsRecord record; string path;
        if (original is { } saved) { record = saved.Record; path = saved.Path; }
        else
        {
            var parent = Path.Combine(MaintenanceCacheRoot, application);
            DeploymentPaths.RequireOrdinaryAncestors(parent);
            var directories = Directory.Exists(parent) ? Directory.EnumerateDirectories(parent).Take(129).ToArray() : [];
            if (directories.Length > 128) throw new InvalidDataException("维护库存目录超过清理预算。");
            long inventoryBytes = 0;
            var inputs = directories.Select(root =>
            {
                execution?.Checkpoint();
                var inventory = Path.Combine(root, "installed-inventory.json");
                try
                {
                    MachineRuntimePolicy.RequireProtectedEvidence(inventory);
                    if (new FileInfo(inventory).Length > 4 * 1024 * 1024) throw new InvalidDataException("库存超过读取预算。");
                    inventoryBytes = checked(inventoryBytes + new FileInfo(inventory).Length);
                    if (inventoryBytes > 32 * 1024 * 1024) throw new InvalidDataException("库存总量超过读取预算。");
                    return new UninstallAssetInventory(root, File.ReadAllText(inventory));
                }
                catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException or InvalidDataException)
                { return new UninstallAssetInventory(root, "{}"); } // Unverifiable roots become explicit retained items.
            }).ToArray();
            record = PlanUninstallAssets(current, Wire.Id(), MachineRoot, parent, inputs, others, (hash, json) =>
            {
                var archive = AssetInventoryArchive(hash);
                if (!File.Exists(archive)) WriteProtectedBytes(archive, System.Text.Encoding.UTF8.GetBytes(json), sid, overwrite: false);
                MachineRuntimePolicy.RequireProtectedEvidence(archive);
                if (Wire.FileHash(archive) != hash) throw new InvalidDataException("保管的安全库存回执不一致。");
            }, execution?.Token ?? default);
            // Discovery never authorizes deletion: orphan releases without a
            // protected inventory remain visible instead of being prefix-deleted.
            var releaseParent = Path.GetDirectoryName(current.ReleaseRoot)!;
            DeploymentPaths.RequireOrdinaryAncestors(releaseParent);
            var orphans = Directory.Exists(releaseParent) ? Directory.EnumerateDirectories(releaseParent).Take(257).ToArray() : [];
            if (orphans.Length > 256) throw new InvalidDataException("历史程序目录超过核对预算。");
            var retained = orphans.Where(root => Path.GetFileName(root).StartsWith(application + "-", StringComparison.Ordinal) && !record.Plan.Delete.Any(a => a.Root == root))
                .Select(root => new DeploymentVersionRetention(root, "未找到可核验的受保护安装清单，原目录保留。")).ToArray();
            var plan = record.Plan with { Retained = [.. record.Plan.Retained, .. retained] };
            record = record with { Plan = plan, Identity = Wire.Hash(Wire.Encode(plan)) };
            path = AssetRecordFile(application, plan.OperationId);
        }
        foreach (var area in record.Plan.Delete)
        {
            RequireUninstallAssetRoot(area.Root, current, MachineRoot, others);
            if (!record.Plan.Caches.Contains(area.Root, StringComparer.OrdinalIgnoreCase)) _ = TrustedProgramRoots.RequireBoundary(area.Root);
        }
        RequireProtectedPruneFiles(record.Plan.Delete);
        void Save(UninstallAssetsRecord state)
        {
            if (System.Text.Encoding.UTF8.GetByteCount(Wire.Encode(state)) > 32 * 1024 * 1024) throw new InvalidDataException("清理回执超过保存预算。");
            WriteProtected(path, state, sid);
        }
        return ApplyUninstallAssets(record, path, current, Save, root => TrustedProgramRoots.Contains(root, AppContext.BaseDirectory), QueueUninstallCacheDeletion, execution);
    }

    [SupportedOSPlatform("windows")]
    static void QueueUninstallCacheDeletion(string path)
    {
        // Exact frozen paths only; no replacement, recursion, shell or reboot action.
        if (!MoveUninstallCacheFile(@"\\?\" + Path.GetFullPath(path), null, 4)) throw new IOException("维护缓存的重启清理登记失败。", new Win32Exception(Marshal.GetLastWin32Error()));
    }
    [DllImport("kernel32.dll", EntryPoint = "MoveFileExW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool MoveUninstallCacheFile(string path, string? target, uint flags);
}

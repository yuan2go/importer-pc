using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelStartupMaintenance
{
    sealed record Snapshot(string Directory, string Database, string SourceIdentity) : IDisposable
    { public void Dispose() => System.IO.Directory.Delete(Directory, true); }
    Snapshot CopyForMaintenance(string directory)
    {
        var database = Path.Combine(directory, definition.Id + ".db");
        if (!File.Exists(database)) throw new FileNotFoundException("原数据库不存在；不能用空库代替维护检查", database);
        var names = new[] { "configuration.json", definition.Id + ".db", definition.Id + ".db-wal", "overload.json", "RESTORED-ISOLATED.json", "RESTORE-INCOMPLETE.json" }
            .Where(n => File.Exists(Path.Combine(directory, n))).Concat(RecoveryFiles.Enumerate(directory, "receipts"))
            .Concat(RecoveryFiles.Enumerate(directory, "images", skipImageLeases: true)).Order(StringComparer.Ordinal).ToArray();
        RecoveryFiles.ValidateNames(directory, names.Where(n => n is "configuration.json" or "overload.json" || n.StartsWith("receipts/") || n.StartsWith("images/")));
        var hashes = names.ToDictionary(n => n, n => Wire.FileHash(RecoveryFiles.Resolve(directory, n)));
        var scratch = MaintenanceWorkspace();
        try
        {
            var bytes = names.Sum(n => new FileInfo(RecoveryFiles.Resolve(directory, n)).Length);
            if (!ParcelStorageVolume.Plan(scratch, bytes, 67108864).Fits) throw new IOException("完整维护快照卷空间不足；原文件保留");
            foreach (var name in names)
            {
                var target = RecoveryFiles.Resolve(scratch, name); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                RecoveryFiles.CopyVerified(RecoveryFiles.Resolve(directory, name), target, hashes[name]);
            }
            if (hashes.Any(h => Wire.FileHash(RecoveryFiles.Resolve(directory, h.Key)) != h.Value)) throw new IOException("原文件在取得所有权后变化；拒绝使用不一致副本");
            return new(scratch, Path.Combine(scratch, definition.Id + ".db"), Wire.Hash(Wire.Encode(hashes)));
        }
        catch { Directory.Delete(scratch, true); throw; }
    }
    StartupChangePreview Preview(string purpose, string? destination, (ParcelConfiguration Seed, ConfigurationRevision<ParcelConfiguration>? Envelope, string Directory, string File, string Hash) state, Snapshot? copy)
    {
        if (purpose is not ("Upgrade" or "Uninstall" or "Backup")) throw new InvalidDataException("不支持的产品维护目的");
        if (copy is null)
        {
            if (Directory.Exists(state.Directory) && new[] { "receipts", "images" }.Any(n => Directory.Exists(Path.Combine(state.Directory, n)) && Directory.EnumerateFileSystemEntries(Path.Combine(state.Directory, n)).Any()))
                throw new InvalidDataException("无数据库却存在原件；保留原根并核对原发布");
            return new(Wire.Hash(state.Hash + purpose + destination), purpose, true, "尚未创建业务库；保留原配置和数据根，未启动设备。", state.Hash, destination);
        }
        var inspection = ParcelRepository.InspectMaintenance(copy.Database, definition);
        var markers = new[] { "RESTORE-INCOMPLETE.json", "RESTORED-ISOLATED.json", "overload.json" }.Where(n => File.Exists(Path.Combine(state.Directory, n))).ToArray();
        var allowed = purpose == "Backup" || inspection.OrdinaryUpgradeAllowed && markers.Length == 0;
        return new(Wire.Hash(copy.SourceIdentity + state.Hash + purpose + destination), purpose, allowed,
            Wire.Encode(inspection) + "\n隔离标记：" + string.Join(",", markers) + "\n程序切换不回滚数据库；未决请求须原发布核对。", state.Hash, destination);
    }
    public Task<StartupChangePreview> PlanChangeAsync(string purpose, string? backupDestination)
    {
        Authorize(); var state = ReadState();
        using var owner = Directory.Exists(state.Directory) ? new ResourceLease(state.Directory, ["application-owner"]) : null;
        if (ReadState().Hash != state.Hash) throw new InvalidOperationException("配置在取得所有权时变化");
        using var copy = File.Exists(Path.Combine(state.Directory, definition.Id + ".db")) ? CopyForMaintenance(state.Directory) : null;
        return Task.FromResult(Preview(purpose, backupDestination, state, copy));
    }
    public async Task<StartupChangePreview> PrepareChangeAsync(string expectedPlan, string purpose, string backupDestination)
        => await ExecuteChangeAsync(expectedPlan, purpose, backupDestination, null);
    public async Task<StartupChangePreview> ExecuteChangeAsync(string expectedPlan, string purpose, string backupDestination, Func<Task>? change)
    {
        Authorize(); var state = ReadState();
        using var owner = new ResourceLease(state.Directory, ["application-owner"]);
        if (ReadState().Hash != state.Hash) throw new InvalidOperationException("配置在取得所有权时变化");
        using var copy = File.Exists(Path.Combine(state.Directory, definition.Id + ".db")) ? CopyForMaintenance(state.Directory) : null;
        var preview = Preview(purpose, backupDestination, state, copy);
        if (preview.Identity != expectedPlan || !preview.Allowed) throw new InvalidOperationException("维护计划已变化或原责任未清；重新检查，不能切换发布");
        if (copy is not null) await BackupCopy(state.Directory, backupDestination, state, copy);
        if (change is not null) await change(); // Same application owner spans backup and deployment commit.
        return preview;
    }
    async Task BackupCopy(string directory, string destination, (ParcelConfiguration Seed, ConfigurationRevision<ParcelConfiguration>? Envelope, string Directory, string File, string Hash) state, Snapshot copy)
    {
        var envelope = state.Envelope ?? throw new InvalidDataException("数据库存在而原配置账缺失");
        await using var ports = factory();
        using var repository = new ParcelRepository(copy.Directory, definition.Id, ParcelRepository.ReadStorageVersion(copy.Database, definition.Id));
        // Snapshot repository is disposable; the live DB/WAL is never opened by SQLite.
        ParcelRecovery.CreateBackup(directory, destination, envelope.Effective, envelope.Pending, new FileInfo(copy.Database).Length,
            repository.Backup, ports.RegisterBackup);
    }
    public async Task BackupAsync(string destination)
    {
        Authorize(); var state = ReadState(); using var owner = new ResourceLease(state.Directory, ["application-owner"]);
        if (ReadState().Hash != state.Hash) throw new InvalidOperationException("配置已变化");
        using var copy = CopyForMaintenance(state.Directory); await BackupCopy(state.Directory, destination, state, copy);
    }
    public Task<StartupBackupItem[]> ListBackupsAsync(string directory, CancellationToken token)
    {
        directory = Path.GetFullPath(directory); RecoveryFiles.RequireOrdinaryAncestors(directory);
        var result = new List<StartupBackupItem>();
        foreach (var recovery in Directory.EnumerateDirectories(directory, "*.recovery").Take(257))
        {
            token.ThrowIfCancellationRequested(); if (result.Count == 256) throw new IOException("单目录超过256份备份；按交付目录分组后重试");
            var backup = recovery[..^".recovery".Length];
            try
            {
                var config = BackupConfiguration(backup);
                var target = Path.Combine(Path.GetTempPath(), "ipc-backup-preview-" + Wire.Id(), config.Mode.ToString(), config.StationId, definition.Id);
                var plan = ParcelRecovery.PlanRestore(backup, target);
                var manifest = Wire.Decode<ParcelRecoveryManifest>(File.ReadAllText(Path.Combine(recovery, "COMPLETED.json")));
                result.Add(new(backup, "Verified", manifest.At, new FileInfo(backup).Length + manifest.Files.Sum(f => new FileInfo(RecoveryFiles.Resolve(recovery, f.Key)).Length), BackupIdentity(backup), Wire.Encode(plan)));
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException)
            { result.Add(new(backup, "InvalidOrIncomplete", null, 0, null, error.GetType().Name + "；保留原备份，重新取得完整集合")); }
        }
        return Task.FromResult(result.OrderByDescending(x => x.At).ToArray());
    }
}

using Ipc.Application.Contracts;
using Ipc.Platform;
using Ipc.Platform.Storage;
using Microsoft.Data.Sqlite;

namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelStartupMaintenance
{
    sealed record Snapshot(string Directory, string Database, string SourceIdentity) : IDisposable
    { public void Dispose() => System.IO.Directory.Delete(Directory, true); }
    Snapshot CopyForMaintenance(string directory, MaintenanceExecution? execution = null)
    {
        execution?.Checkpoint(); execution?.Report(MaintenanceStage.Checking);
        var token = execution?.Token ?? CancellationToken.None;
        var database = Path.Combine(directory, definition.Id + ".db");
        if (!File.Exists(database)) throw new FileNotFoundException("原数据库不存在；不能用空库代替维护检查", database);
        var names = new[] { "configuration.json", ParcelMachineOrigin.FileName, definition.Id + ".db", definition.Id + ".db-wal", "overload.json", "RESTORED-ISOLATED.json", "RESTORE-INCOMPLETE.json" }
            .Where(n => File.Exists(Path.Combine(directory, n))).Concat(RecoveryFiles.Enumerate(directory,"receipts",false,token))
            .Concat(RecoveryFiles.Enumerate(directory,"images",true,token)).Concat(RecoveryFiles.MaintenanceEvidence(directory,token)).Order(StringComparer.Ordinal).ToArray();
        RecoveryFiles.ValidateNames(directory, names.Where(n => n is "configuration.json" or "overload.json" or ParcelMachineOrigin.FileName || n.StartsWith("receipts/") || n.StartsWith("images/") || n.StartsWith("startup-maintenance/")),token);
        var hashes = names.ToDictionary(n => n, n => Wire.FileHash(RecoveryFiles.Resolve(directory, n), token));
        var scratch = MaintenanceWorkspace();
        try
        {
            var bytes = names.Sum(n => {token.ThrowIfCancellationRequested();return new FileInfo(RecoveryFiles.Resolve(directory,n)).Length;});
            if (!ParcelStorageVolume.Plan(scratch, bytes, 67108864).Fits) throw new IOException("完整维护快照卷空间不足；原文件保留");
            long copiedBytes = 0; execution?.Report(MaintenanceStage.CopyingSnapshot,0,bytes);
            foreach (var name in names)
            {
                Authorize(); execution?.Checkpoint();
                var target = RecoveryFiles.Resolve(scratch, name); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                RecoveryFiles.CopyVerified(RecoveryFiles.Resolve(directory, name), target, hashes[name], token,
                    count => { copiedBytes += count; execution?.Checkpoint(); execution?.Report(MaintenanceStage.CopyingSnapshot,copiedBytes,bytes); });
            }
            if (hashes.Any(h => Wire.FileHash(RecoveryFiles.Resolve(directory, h.Key), token) != h.Value)) throw new IOException("原文件在取得所有权后变化；拒绝使用不一致副本");
            return new(scratch, Path.Combine(scratch, definition.Id + ".db"), Wire.Hash(Wire.Encode(hashes)));
        }
        catch { Directory.Delete(scratch, true); throw; }
    }
    StartupChangePreview Preview(string purpose, string? destination, (ParcelConfiguration Seed, ConfigurationRevision<ParcelConfiguration>? Envelope, string Directory, string File, string Hash) state, Snapshot? copy, MaintenanceExecution? execution = null)
    {
        if (purpose is not ("Upgrade" or "Repair" or "Uninstall" or "Backup")) throw new InvalidDataException("不支持的产品维护目的");
        if (copy is null)
        {
            if (new[] { definition.Id + ".db-wal", definition.Id + ".db-shm", "RESTORE-INCOMPLETE.json", "RESTORED-ISOLATED.json", "overload.json" }
                .Any(n => File.Exists(Path.Combine(state.Directory, n))))
                throw new InvalidDataException("无数据库却存在日志或恢复/过载责任标记；保留原根并核对原发布，不能按未使用部署升级");
            if (Directory.Exists(state.Directory) && new[] { "receipts", "images" }.Any(n => Directory.Exists(Path.Combine(state.Directory, n)) && Directory.EnumerateFileSystemEntries(Path.Combine(state.Directory, n)).Any()))
                throw new InvalidDataException("无数据库却存在原件；保留原根并核对原发布");
            return new(Wire.Hash(state.Hash + purpose + destination), purpose, true, "尚未创建业务库；保留原配置和数据根，未启动设备。", state.Hash, destination);
        }
        execution?.Checkpoint(); execution?.Report(MaintenanceStage.Verifying);
        var inspection = ParcelRepository.InspectMaintenance(copy.Database, definition, execution?.Token ?? CancellationToken.None);
        var markers = new[] { "RESTORE-INCOMPLETE.json", "RESTORED-ISOLATED.json", "overload.json" }.Where(n => File.Exists(Path.Combine(state.Directory, n))).ToArray();
        var allowed = purpose is "Backup" or "Repair" || inspection.ResponsibilitiesSettled && markers.Length == 0;
        return new(Wire.Hash(copy.SourceIdentity + state.Hash + purpose + destination), purpose, allowed,
            Wire.Encode(inspection) + "\n隔离标记：" + string.Join(",", markers) + (purpose == "Repair" ? "\n仅相同冻结发布的程序修复：原责任/标记保留，完整备份后不改账、不激活、不启动；装配层必须核对相同源码。" : "\n程序切换不回滚数据库；未决请求须原发布核对。"), state.Hash, destination);
    }
    static async Task<T> ObserveMaintenance<T>(IProgress<MaintenanceProgress>? progress,CancellationToken token,Func<MaintenanceExecution,Task<T>> operation)
    {
        using var execution=new MaintenanceExecution(progress,token);
        try{var result=await operation(execution);execution.Complete();return result;}
        catch(OperationCanceledException)when(execution.CancellationRequested&&!execution.Committing)
        {throw new MaintenanceCancelledException("维护已在提交前取消；原数据保留，准备生成的文件须核对后保管。");}
        finally{execution.Finish();}
    }
    public Task<StartupChangePreview> PlanChangeAsync(string purpose,string? backupDestination)
        => PlanChangeAsync(purpose,backupDestination,null,CancellationToken.None);
    public Task<StartupChangePreview> PlanChangeAsync(string purpose,string? backupDestination,IProgress<MaintenanceProgress>? progress,CancellationToken token)
        => ObserveMaintenance(progress,token,e=>PlanChangeAsync(purpose,backupDestination,e));
    public Task<StartupChangePreview> PlanChangeAsync(string purpose,string? backupDestination,MaintenanceExecution execution)
    {
        execution.Checkpoint();execution.Report(MaintenanceStage.Checking);
        Authorize(); var state = ReadState();
        using var owner = Directory.Exists(state.Directory) ? new ResourceLease(state.Directory, ["application-owner"]) : null;
        if (ReadState().Hash != state.Hash) throw new InvalidOperationException("配置在取得所有权时变化");
        using var copy = File.Exists(Path.Combine(state.Directory, definition.Id + ".db")) ? CopyForMaintenance(state.Directory,execution) : null;
        Authorize();execution.Checkpoint();
        return Task.FromResult(Preview(purpose, backupDestination, state, copy,execution));
    }
    public Task<StartupChangePreview> PrepareChangeAsync(string expectedPlan,string purpose,string backupDestination)
        => PrepareChangeAsync(expectedPlan,purpose,backupDestination,null,CancellationToken.None);
    public Task<StartupChangePreview> PrepareChangeAsync(string expectedPlan,string purpose,string backupDestination,IProgress<MaintenanceProgress>? progress,CancellationToken token)
        => ObserveMaintenance(progress,token,e=>ExecuteChangeAsync(expectedPlan,purpose,backupDestination,null,e));
    public Task<StartupChangePreview> ExecuteChangeAsync(string expectedPlan, string purpose, string backupDestination, Func<Task>? change)
        => ExecuteChangeAsync(expectedPlan,purpose,backupDestination,change,null);
    public async Task<StartupChangePreview> ExecuteChangeAsync(string expectedPlan, string purpose, string backupDestination, Func<Task>? change, MaintenanceExecution? execution)
    {
        execution?.Checkpoint(); execution?.Report(MaintenanceStage.Checking);
        Authorize(); var state = ReadState();
        using var owner = new ResourceLease(state.Directory, ["application-owner"]);
        if (ReadState().Hash != state.Hash) throw new InvalidOperationException("配置在取得所有权时变化");
        using var copy = File.Exists(Path.Combine(state.Directory, definition.Id + ".db")) ? CopyForMaintenance(state.Directory, execution) : null;
        var preview = Preview(purpose, backupDestination, state, copy, execution);
        if (preview.Identity != expectedPlan || !preview.Allowed) throw new InvalidOperationException("维护计划已变化或原责任未清；重新检查，不能切换发布");
        if (copy is not null) await BackupCopy(state.Directory, backupDestination, state, copy, execution, finalOperation: change is null);
        Authorize(); execution?.Checkpoint();
        if (change is not null) await change(); // Same application owner spans backup and deployment commit.
        return preview;
    }
    async Task BackupCopy(string directory, string destination, (ParcelConfiguration Seed, ConfigurationRevision<ParcelConfiguration>? Envelope, string Directory, string File, string Hash) state, Snapshot copy, MaintenanceExecution? execution = null, bool finalOperation = true)
    {
        var envelope = state.Envelope ?? throw new InvalidDataException("数据库存在而原配置账缺失");
        _ = ParcelRepository.ReadStorageVersion(copy.Database, definition.Id);
        await using var ports = factory();
        using var database = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = copy.Database, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()); database.Open();
        // Back up the exact current-format private DB/WAL snapshot without runtime
        // recovery writes; the original DB is never opened by SQLite.
        var snapshotBytes = checked(new FileInfo(copy.Database).Length + (File.Exists(copy.Database + "-wal") ? new FileInfo(copy.Database + "-wal").Length : 0));
        ParcelRecovery.CreateBackup(directory, destination, envelope.Effective, envelope.Pending, snapshotBytes,
            file => Database.Backup(database, file, execution?.Token ?? CancellationToken.None,
                (done,total) => { Authorize(); execution?.Checkpoint(); execution?.Report(MaintenanceStage.CopyingDatabase,done,total); }),
            ports.RegisterBackup, Authorize, execution?.Token ?? CancellationToken.None, execution, finalOperation);
    }
    public Task BackupAsync(string destination) => BackupAsync(destination,null,CancellationToken.None);
    public async Task BackupAsync(string destination, IProgress<MaintenanceProgress>? progress, CancellationToken token)
    {
        using var execution = new MaintenanceExecution(progress,token);
        try { await BackupAsync(destination,execution); }
        catch (OperationCanceledException) when (execution.CancellationRequested && !execution.Committing)
        { throw new MaintenanceCancelledException("备份已在完成标记提交前取消；保留未完成恢复集，重新执行须选择新目标。"); }
        finally { execution.Finish(); }
    }
    public async Task BackupAsync(string destination, MaintenanceExecution execution)
    {
        execution.Checkpoint(); execution.Report(MaintenanceStage.Checking);
        Authorize(); var state = ReadState(); using var owner = new ResourceLease(state.Directory, ["application-owner"]);
        if (ReadState().Hash != state.Hash) throw new InvalidOperationException("配置已变化");
        using var copy = CopyForMaintenance(state.Directory,execution); await BackupCopy(state.Directory,destination,state,copy,execution);
    }
    public Task<StartupBackupItem[]> ListBackupsAsync(string directory,CancellationToken token)=>ListBackupsAsync(directory,null,token);
    public Task<StartupBackupItem[]> ListBackupsAsync(string directory,IProgress<MaintenanceProgress>? progress,CancellationToken token)
        => ObserveMaintenance(progress,token,e=>ListBackupsCore(directory,e));
    Task<StartupBackupItem[]> ListBackupsCore(string directory,MaintenanceExecution execution)
    {
        execution.Checkpoint();execution.Report(MaintenanceStage.Verifying);var token=execution.Token;
        Authorize();
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
                var plan = ParcelRecovery.PlanRestore(backup,target,Authorize,execution);
                var manifest = Wire.Decode<ParcelRecoveryManifest>(File.ReadAllText(Path.Combine(recovery, "COMPLETED.json")));
                result.Add(new(backup, "Verified", manifest.At, new FileInfo(backup).Length + manifest.Files.Sum(f => new FileInfo(RecoveryFiles.Resolve(recovery, f.Key)).Length), BackupIdentity(backup,token), Wire.Encode(plan)));
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException)
            { result.Add(new(backup, "InvalidOrIncomplete", null, 0, null, error.GetType().Name + "；保留原备份，重新取得完整集合")); }
        }
        return Task.FromResult(result.OrderByDescending(x => x.At).ToArray());
    }
}

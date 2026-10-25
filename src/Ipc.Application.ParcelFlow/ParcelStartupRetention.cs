using Ipc.Application.Contracts;
using Ipc.Platform;
using Microsoft.Data.Sqlite;

namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelStartupMaintenance : IStartupRetentionMaintenance
{
    internal Action<string>? RetentionPhase { get; set; }
    static string RetentionOperation(string directory, string identity)
    {
        if (!ExtensionSchema.IsHash(identity)) throw new InvalidDataException("到期维护计划身份无效");
        return RecoveryFiles.Resolve(directory, "startup-maintenance/" + identity[..32].ToLowerInvariant() + "/retention-result.json");
    }
    public Task<StartupRetentionPlan> PlanRetentionAsync(string destination, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested(); Authorize(); var state = ReadState(); using var owner = new ResourceLease(state.Directory, ["application-owner"]);
        var effective = state.Envelope?.Effective ?? throw new InvalidOperationException("先完成配置启动读回");
        if (effective.Retention?.HistoryDays is not (>= 7 and <= 30) || state.Envelope!.Pending is not null || state.Envelope.Confirmation?.Required == true)
            throw new InvalidOperationException("先使7～30天保留策略生效并确认；存在待生效候选时不执行清理");
        destination = Path.GetFullPath(destination); RecoveryFiles.RequireOrdinaryAncestors(destination);
        if (File.Exists(destination) || Directory.Exists(destination + ".recovery")) throw new IOException("请选择新的完整备份目标");
        var database = Path.Combine(state.Directory, definition.Id + ".db");
        ParcelMaintenanceInspection inspection;
        try { inspection = ParcelRepository.InspectMaintenanceCore(database, definition, token); }
        catch (SqliteException) when (token.IsCancellationRequested) { throw new OperationCanceledException(token); }
        if (!inspection.Paused || inspection.DatabaseVersion != 9 || File.Exists(database + "-wal") && new FileInfo(database + "-wal").Length != 0) throw new InvalidOperationException("先暂停并完全停止同版软件，确认WAL已收拢；不在线清理或升级数据库");
        var databaseHash = Wire.FileHash(database, token); Authorize();
        var identity = Wire.Hash(Wire.Encode(new { state.Hash, databaseHash, destination, effective.Retention.HistoryDays }));
        return Task.FromResult(new StartupRetentionPlan(identity, state.Hash, databaseHash, destination, effective.Retention.HistoryDays.Value,
            "软件必须暂停并完全停止；先创建完整备份，按原清理内核逐项保护未决责任、防重和永久身份。已结明细删除不会自动压缩磁盘；本操作不执行VACUUM。\n" + Wire.Encode(inspection)));
    }
    public async Task<StartupRetentionOutcome> ExecuteRetentionAsync(StartupRetentionPlan plan)
    {
        Authorize(); var fresh = await PlanRetentionAsync(plan.BackupDestination);
        if (fresh.Identity != plan.Identity || fresh.DatabaseHash != plan.DatabaseHash || fresh.ConfigurationHash != plan.ConfigurationHash || fresh.Days != plan.Days) throw new InvalidOperationException("原维护计划已变化；重新预检，不能提交旧计划");
        var state = ReadState(); var file = RetentionOperation(state.Directory, plan.Identity); Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        // This is an intent/result in the existing maintenance evidence tree, never a deletion authority.
        using (var reserved = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { var bytes = System.Text.Encoding.UTF8.GetBytes(Wire.Encode(new StartupRetentionOutcome("Started", plan.Identity, "已提交原维护计划；结果需按原回执核对，不重复提交。"))); reserved.Write(bytes); reserved.Flush(true); }
        try
        {
            await using var ports = factory();
            var result = ParcelRepository.ExpireHistoryAuthorized(Path.Combine(state.Directory, definition.Id + ".db"), plan.DatabaseHash, plan.BackupDestination, definition,
                (path, config) => { ports.Normalize(config); ports.RegisterBackup(path, config); }, false, Authorize, phase: RetentionPhase);
            var outcome = new StartupRetentionOutcome("Completed", plan.Identity, "完整备份、到期清理和完整性核对已完成；未决/永久身份保留。\n" + Wire.Encode(result), result.Run, result.BackupHash);
            ExtensionStore.AtomicWrite(file, outcome); return outcome;
        }
        catch (Exception error)
        {
            try { ExtensionStore.AtomicWrite(file, new StartupRetentionOutcome("Interrupted", plan.Identity,
                "维护未完整结束。已提交的到期事实不回滚；原数据和完整备份保留。请先核对原回执，再重新预检剩余范围。\n" + DiagnosticText.Protect(error.Message))); }
            catch (Exception receiptError) { throw new AggregateException("原维护及终态回执均未完整结束；保留原计划核对", error, receiptError); }
            throw;
        }
    }
    public Task<StartupRetentionOutcome> InspectRetentionAsync(StartupRetentionPlan plan)
    {
        Authorize(); var state = ReadState(); using var owner = new ResourceLease(state.Directory, ["application-owner"]);
        var file = RetentionOperation(state.Directory, plan.Identity); RecoveryFiles.RequireOrdinaryAncestors(file);
        if (!File.Exists(file)) return Task.FromResult(new StartupRetentionOutcome("Unknown", plan.Identity, "原操作没有可读终态；可能尚在执行。保留计划，不重复提交。"));
        if (new FileInfo(file).Length > 4 * 1024 * 1024) throw new InvalidDataException("原维护回执超出预算");
        var result = Wire.Decode<StartupRetentionOutcome>(File.ReadAllText(file));
        if (result.Identity != plan.Identity) throw new InvalidDataException("原维护回执身份不同");
        if (File.Exists(plan.BackupDestination) && File.Exists(plan.BackupDestination + ".recovery/COMPLETED.json"))
        {
            var backupHash = Wire.FileHash(plan.BackupDestination);
            var manifestPath = plan.BackupDestination + ".recovery/COMPLETED.json"; RecoveryFiles.RequireOrdinaryAncestors(manifestPath);
            if (new FileInfo(manifestPath).Length > RecoveryFiles.MaximumManifestBytes) throw new InvalidDataException("原备份清单超出预算");
            var manifest = Wire.Decode<ParcelRecoveryManifest>(File.ReadAllText(manifestPath));
            var evidenceName = Path.GetRelativePath(state.Directory, file).Replace('\\', '/');
            if (manifest.Application != definition.Id || manifest.DatabaseHash != backupHash || !manifest.Files.TryGetValue(evidenceName, out var evidenceHash)) throw new InvalidDataException("备份与原维护身份不匹配");
            var evidence = RecoveryFiles.Resolve(plan.BackupDestination + ".recovery", evidenceName);
            if (Wire.FileHash(evidence) != evidenceHash || Wire.Decode<StartupRetentionOutcome>(File.ReadAllText(evidence)).Identity != plan.Identity) throw new InvalidDataException("原维护备份证据不匹配");
            using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(state.Directory, definition.Id + ".db"), Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()); db.Open();
            using var command = db.CreateCommand(); command.CommandText = "SELECT id,summary FROM parcel_retention_runs WHERE backup_hash=$backup"; command.Parameters.AddWithValue("$backup", backupHash);
            string? run = null, summary = null;
            using (var rows = command.ExecuteReader())
            {
                if (rows.Read()) { run = rows.GetString(0); summary = rows.GetString(1); }
                if (rows.Read()) throw new InvalidDataException("原备份对应多个维护运行，保持待核对");
            }
            if (result.Status == "Completed" && (run != result.Run || result.BackupHash != backupHash)) throw new InvalidDataException("原完成回执与原账不同");
            if (summary?.Contains("\"Completed\"", StringComparison.Ordinal) == true)
            {
                command.CommandText = "PRAGMA integrity_check"; if (command.ExecuteScalar() as string != "ok") throw new InvalidDataException("当前原账完整性核对未通过");
                db.Close(); // Release this read session before the inventory reader hashes DB/WAL.
                var inspection = ParcelRepository.InspectMaintenanceCore(Path.Combine(state.Directory, definition.Id + ".db"), definition);
                if (inspection.Reasons.Any(r => r.StartsWith("Original receipt bytes missing/changed", StringComparison.Ordinal))) throw new InvalidDataException("原回执完整性未核对一致");
                Authorize();
                return Task.FromResult(new StartupRetentionOutcome("Completed", plan.Identity,
                    "已从原账核对本次清理提交，并重新核验完整备份与当前完整性。未发起新的清理；未决责任和永久身份继续保留。\n原运行：" + run, run, backupHash));
            }
            if (result.Status == "Completed") throw new InvalidDataException("原账缺少本次完成事实");
        }
        else if (result.Status == "Completed") throw new InvalidDataException("原完成回执缺少可核验完整备份");
        return Task.FromResult(result);
    }
}

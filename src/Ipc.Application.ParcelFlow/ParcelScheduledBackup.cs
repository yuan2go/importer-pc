using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime
{
    sealed record BackupScheduleState(string PolicyHash, DateTimeOffset NextDue, string Outcome, string? Backup, DateTimeOffset At,
        string? LastSuccessfulBackup = null, DateTimeOffset? LastSuccessfulAt = null, string? Detail = null);
    BackupScheduleState? backupScheduleState;
    DateTimeOffset nextBackupCheck;
    string BackupScheduleFile => Path.Combine(directory!, "backup-schedule.json");
    internal async Task CheckScheduledBackupAsync(DateTimeOffset now)
    { await gate.WaitAsync().ConfigureAwait(false); try { TryScheduledBackup(now); } finally { gate.Release(); } }
    void TryScheduledBackup(DateTimeOffset now)
    {
        if (backupScheduleState is null && File.Exists(BackupScheduleFile)) backupScheduleState = Wire.Decode<BackupScheduleState>(File.ReadAllText(BackupScheduleFile));
        if (Effective.BackupSchedule is not { } policy || now < nextBackupCheck) return;
        nextBackupCheck = now.AddMinutes(1);
        var hash = Wire.Hash(Wire.Encode(policy));
        if (backupScheduleState?.PolicyHash != hash)
        {
            backupScheduleState = new(hash, now.AddMinutes(policy.IntervalMinutes), "Scheduled", null, now, backupScheduleState?.LastSuccessfulBackup ?? (backupScheduleState?.Outcome == "BackupCreated" ? backupScheduleState.Backup : null), backupScheduleState?.LastSuccessfulAt ?? (backupScheduleState?.Outcome == "BackupCreated" ? backupScheduleState.At : null));
            ExtensionStore.AtomicWrite(BackupScheduleFile, backupScheduleState);
        }
        if (now < backupScheduleState.NextDue) return;
        var outcome = "Pending: pause/authenticated maintenance/no external call required";
        string? detail = "等待暂停、有效管理授权且没有在途外呼。保存计划不会自动暂停作业。";
        string? backup = null;
        if (repository!.Paused && MaintenanceAuthorized && externalWaits == 0 && !stopping.IsCancellationRequested)
        {
            try
            {
                RecoveryFiles.RequireOrdinaryAncestors(policy.Directory);
                if (!Directory.Exists(policy.Directory)) throw new IOException("备份目标目录不存在；由维护人员选择/创建，计划不会改道");
                if (Directory.EnumerateDirectories(policy.Directory, "*.recovery").Take(policy.MaximumSets).Count() >= policy.MaximumSets)
                    throw new IOException("备份份数容量上限已到；保留所有原备份，转移到新卷或审查消费者后维护");
                backup = Path.Combine(policy.Directory, Id + "-" + now.ToString("yyyyMMddTHHmmssZ") + "-" + Wire.Id() + ".db");
                foreach (var op in operations.Values.Where(x => x.Image is not null && !x.ImageReleased)) _ = ReadImage(op, op.Image!);
                ParcelRecovery.CreateBackup(directory!, backup, Effective, configuration!.Current.Pending, repository.StorageBytes, repository.Backup, factory.RegisterBackup, () => { Management.Require(); });
                outcome = "BackupCreated";
                detail = "完整恢复集已创建；数据库与同名恢复目录必须一起保留。";
            }
            catch (Exception error) when (error is IOException or InvalidOperationException or UnauthorizedAccessException or InvalidDataException)
            { outcome = "Failed:" + error.GetType().Name; detail = DiagnosticText.Protect(error.Message); backup = null; }
        }
        backupScheduleState = new(hash, outcome == "BackupCreated" ? now.AddMinutes(policy.IntervalMinutes) : backupScheduleState.NextDue, outcome, backup, now,
            outcome == "BackupCreated" ? backup : backupScheduleState.LastSuccessfulBackup ?? (backupScheduleState.Outcome == "BackupCreated" ? backupScheduleState.Backup : null),
            outcome == "BackupCreated" ? now : backupScheduleState.LastSuccessfulAt ?? (backupScheduleState.Outcome == "BackupCreated" ? backupScheduleState.At : null), detail);
        ExtensionStore.AtomicWrite(BackupScheduleFile, backupScheduleState);
    }
    ParcelBackupScheduleStatus BackupStatus()
    {
        var state = backupScheduleState; var policy = Effective.BackupSchedule;
        return new(policy is not null, policy is null ? "Disabled" : state?.Outcome ?? "Scheduled", state?.Detail, state?.At, policy is null ? null : state?.NextDue,
            state?.LastSuccessfulAt ?? (state?.Outcome == "BackupCreated" ? state.At : null), state?.LastSuccessfulBackup ?? (state?.Outcome == "BackupCreated" ? state.Backup : null), policy?.MaximumSets, policy?.Directory);
    }
}

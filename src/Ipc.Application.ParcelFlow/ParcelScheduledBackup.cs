using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime
{
    sealed record BackupScheduleState(string PolicyHash, DateTimeOffset NextDue, string Outcome, string? Backup, DateTimeOffset At,
        string? LastSuccessfulBackup = null, DateTimeOffset? LastSuccessfulAt = null, string? Detail = null);
    BackupScheduleState? backupScheduleState;
    DateTimeOffset nextBackupCheck;
    Task? scheduledBackupWork;
    string BackupScheduleFile => Path.Combine(directory!, "backup-schedule.json");
    internal async Task CheckScheduledBackupAsync(DateTimeOffset now)
    {
        Task? work;
        await gate.WaitAsync().ConfigureAwait(false);
        try { TryScheduledBackup(now); work = scheduledBackupWork; } finally { gate.Release(); }
        if (work is not null) await work.ConfigureAwait(false);
    }
    void TryScheduledBackup(DateTimeOffset now)
    {
        if (backupScheduleState is null && File.Exists(BackupScheduleFile)) backupScheduleState = Wire.Decode<BackupScheduleState>(File.ReadAllText(BackupScheduleFile));
        if (Effective.BackupSchedule is not { } policy || now < nextBackupCheck || scheduledBackupWork is { IsCompleted: false }) return;
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
            backup = Path.Combine(policy.Directory, Id + "-" + now.ToString("yyyyMMddTHHmmssZ") + "-" + Wire.Id() + ".db");
            var destination = backup;
            try
            {
                scheduledBackupWork = OwnedRead(async token =>
                {
                    string result; string? resultDetail = null;
                    try
                    {
                        RecoveryFiles.RequireOrdinaryAncestors(policy.Directory);
                        if (!Directory.Exists(policy.Directory)) throw new IOException("备份目标目录不存在；由维护人员选择/创建，计划不会改道");
                        if (Directory.EnumerateDirectories(policy.Directory, "*.recovery").Take(policy.MaximumSets).Count() >= policy.MaximumSets)
                            throw new IOException("备份份数容量上限已到；保留所有原备份，转移到新卷或审查消费者后维护");
                        result = (await BackupCoreAsync(destination, token).ConfigureAwait(false)).Status;
                        resultDetail = "完整恢复集已创建；数据库与同名恢复目录必须一起保留。";
                    }
                    catch (OperationCanceledException) { result = "Cancelled: incomplete set retained; no COMPLETED publication"; resultDetail = "备份已取消；残件不能作为完整恢复集。"; }
                    catch (Exception error) when (error is IOException or InvalidOperationException or UnauthorizedAccessException or InvalidDataException)
                    { result = "Failed:" + error.GetType().Name + "; inspect backup/capacity/permission; originals retained"; resultDetail = DiagnosticText.Protect(error.Message); }
                    BackupScheduleState state;
                    await gate.WaitAsync().ConfigureAwait(false);
                    try { state = backupScheduleState = new(hash, result == "BackupCreated" ? now.AddMinutes(policy.IntervalMinutes) : backupScheduleState!.NextDue, result, result == "BackupCreated" ? destination : null, DateTimeOffset.UtcNow,
                        result == "BackupCreated" ? destination : backupScheduleState.LastSuccessfulBackup,
                        result == "BackupCreated" ? now : backupScheduleState.LastSuccessfulAt, resultDetail); }
                    finally { gate.Release(); }
                    ExtensionStore.AtomicWrite(BackupScheduleFile, state);
                    return true;
                });
                outcome = "Running: owned cancellable backup; original responsibilities fenced";
                detail = "正在创建完整恢复集；原责任受备份围栏保护。";
            }
            catch (InvalidOperationException) { outcome = "Pending: background read capacity occupied; originals retained"; }
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

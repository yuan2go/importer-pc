using Ipc.Application.Contracts;
using Ipc.Platform;
using System.Text.Json;

namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime
{
    sealed record BackupScheduleState(string PolicyHash, DateTimeOffset NextDue, string Outcome, string? Backup, DateTimeOffset At,
        string? LastSuccessfulBackup = null, DateTimeOffset? LastSuccessfulAt = null, string? Detail = null, BusinessReceipt? Receipt = null);
    BackupScheduleState? backupScheduleState;
    string? backupScheduleFault;
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
    void BackupScheduleFailed(Exception error)
    {
        backupScheduleFault = "备份计划状态无法确认，计划已停止，原文件保留。供包不受此辅助状态影响。请维护人员关闭软件后保全并核对备份计划状态文件及完整恢复集，移走故障辅助文件，再打开重新建立计划。";
        Diagnostic("维护", "备份计划状态故障，计划已停止", "警告", error.ToString());
    }
    bool PersistBackupSchedule(BackupScheduleState state)
    {
        // Called under gate, including completion: an older Running write must never
        // overwrite the final receipt. Failure retains the last readable success in memory.
        backupScheduleState = state;
        try
        {
            RecoveryFiles.RequireOrdinaryAncestors(BackupScheduleFile);
            ExtensionStore.AtomicWrite(BackupScheduleFile, state);
            return true;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        { BackupScheduleFailed(error); return false; }
    }
    void TryScheduledBackup(DateTimeOffset now)
    {
        // A disabled plan has no dependency on a leftover auxiliary file.
        if (Effective.BackupSchedule is not { } policy || backupScheduleFault is not null || now < nextBackupCheck || scheduledBackupWork is { IsCompleted: false }) return;
        if (scheduledBackupWork is { IsFaulted: true } ended)
        {
            backupScheduleState = backupScheduleState! with { Outcome = "ResultUnconfirmed", Detail = "上次后台备份没有可确认的终态，保留原目标与回执，先核对原结果。" };
            BackupScheduleFailed(ended.Exception!);
            return;
        }
        nextBackupCheck = now.AddMinutes(1);
        if (backupScheduleState is null)
        {
            try
            {
                RecoveryFiles.RequireOrdinaryAncestors(BackupScheduleFile);
                if (Directory.Exists(BackupScheduleFile)) throw new InvalidDataException("备份计划状态不是普通文件");
                if (File.Exists(BackupScheduleFile))
                {
                    if (new FileInfo(BackupScheduleFile).Length > 65536) throw new InvalidDataException("备份计划状态超出读取预算");
                    var json = File.ReadAllText(BackupScheduleFile);
                    using var document = JsonDocument.Parse(json);
                    var root = document.RootElement;
                    if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != root.EnumerateObject().Count())
                        throw new InvalidDataException("备份计划状态含重复或无效字段");
                    var state = Wire.Decode<BackupScheduleState>(json);
                    if (state is null || !ExtensionSchema.IsHash(state.PolicyHash) || state.NextDue == default || state.At == default ||
                        (state.Outcome is not ("Scheduled" or "BackupCreated" or "Rejected" or "Cancelled" or "ResultUnconfirmed") &&
                            state.Outcome?.StartsWith("Pending:", StringComparison.Ordinal) != true && state.Outcome?.StartsWith("Running:", StringComparison.Ordinal) != true &&
                            state.Outcome?.StartsWith("Failed:", StringComparison.Ordinal) != true) || (state.LastSuccessfulAt is null) != (state.LastSuccessfulBackup is null) ||
                        state.Outcome == "BackupCreated" && (state.LastSuccessfulAt is null || state.LastSuccessfulBackup != state.Backup))
                        throw new InvalidDataException("备份计划状态缺少必要事实");
                    backupScheduleState = state;
                    if (state.Outcome.StartsWith("Running:", StringComparison.Ordinal) || state.Outcome == "ResultUnconfirmed")
                    {
                        backupScheduleState = state with { Outcome = "ResultUnconfirmed", Detail = "上次备份结果未确认，保留原位置和回执；先核对完整恢复集，再重新建立计划。" };
                        throw new InvalidDataException("上次备份没有终态回执，禁止自动重复执行");
                    }
                }
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or NotSupportedException)
            { BackupScheduleFailed(error); return; }
        }
        var hash = Wire.Hash(Wire.Encode(policy));
        if (backupScheduleState?.PolicyHash != hash && !PersistBackupSchedule(new(hash, now.AddMinutes(policy.IntervalMinutes), "Scheduled", null, now,
            backupScheduleState?.LastSuccessfulBackup, backupScheduleState?.LastSuccessfulAt))) return;
        if (now < backupScheduleState!.NextDue) return;
        if (!repository!.Paused || !MaintenanceAuthorized || externalWaits != 0 || stopping.IsCancellationRequested)
        {
            _ = PersistBackupSchedule(backupScheduleState with
            {
                Outcome = "Pending: maintenance conditions",
                At = now,
                Backup = null,
                Detail = "等待暂停、有效管理授权且没有在途外呼。保存计划不会自动暂停作业。",
                Receipt = null
            });
            return;
        }
        var destination = Path.Combine(policy.Directory, Id + "-" + now.ToString("yyyyMMddTHHmmssZ") + "-" + Wire.Id() + ".db");
        if (!PersistBackupSchedule(backupScheduleState with
        {
            Outcome = "Running: owned backup",
            At = now,
            Backup = destination,
            Detail = "正在创建完整恢复集；原责任受备份围栏保护。",
            Receipt = null
        })) return;
        try
        {
            scheduledBackupWork = OwnedRead(async token =>
            {
                BusinessReceipt receipt;
                try
                {
                    RecoveryFiles.RequireOrdinaryAncestors(policy.Directory);
                    if (!Directory.Exists(policy.Directory)) throw new IOException("备份目标目录不存在；由维护人员选择/创建，计划不会改道");
                    if (Directory.EnumerateDirectories(policy.Directory, "*.recovery").Take(policy.MaximumSets).Count() >= policy.MaximumSets)
                        throw new IOException("备份份数容量上限已到；保留所有原备份，转移到新卷或审查消费者后维护");
                    receipt = await BackupCoreAsync(destination, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                { receipt = new("Cancelled", "备份已取消；未核验的残件不能作为完整恢复集。", repository!.Revision); }
                catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
                {
                    // BackupCore owns source guards; destination failure only fails this backup.
                    Diagnostic("维护", "计划备份创建失败", "警告", error.ToString());
                    receipt = new("Failed:" + error.GetType().Name, DiagnosticText.Protect(error.Message), repository!.Revision);
                }
                await gate.WaitAsync().ConfigureAwait(false);
                try
                {
                    var at = DateTimeOffset.UtcNow;
                    var success = receipt.Status == "BackupCreated";
                    var state = backupScheduleState! with
                    {
                        Outcome = receipt.Status,
                        At = at,
                        Backup = destination,
                        Receipt = receipt,
                        NextDue = success ? now.AddMinutes(policy.IntervalMinutes) : backupScheduleState!.NextDue,
                        LastSuccessfulBackup = success ? destination : backupScheduleState!.LastSuccessfulBackup,
                        LastSuccessfulAt = success ? at : backupScheduleState!.LastSuccessfulAt,
                        Detail = success ? "完整恢复集已创建；数据库与同名恢复目录必须一起保留。" : receipt.Reason
                    };
                    _ = PersistBackupSchedule(state);
                    Diagnostic("维护", "备份计划执行结果", success ? "信息" : "警告", Wire.Encode(new { Destination = destination, Receipt = receipt }));
                }
                finally { gate.Release(); }
                return true;
            });
        }
        catch (ReadOperationCapacityException)
        {
            _ = PersistBackupSchedule(backupScheduleState with
            {
                Outcome = "Pending: background capacity",
                At = now,
                Detail = "后台读取容量已满，等待当前操作完成。"
            });
        }
        catch (InvalidOperationException) when (stopping.IsCancellationRequested) { }
    }
    ParcelBackupScheduleStatus BackupStatus()
    {
        var state = backupScheduleState; var policy = Effective.BackupSchedule;
        return new(policy is not null, policy is null ? "Disabled" : state?.Outcome == "ResultUnconfirmed" ? "ResultUnconfirmed" : backupScheduleFault is not null ? "ScheduleStateFault" : state?.Outcome ?? "Scheduled",
            backupScheduleFault ?? state?.Detail, state?.At, policy is null || backupScheduleFault is not null ? null : state?.NextDue,
            state?.LastSuccessfulAt, state?.LastSuccessfulBackup, policy?.MaximumSets, policy?.Directory);
    }
}

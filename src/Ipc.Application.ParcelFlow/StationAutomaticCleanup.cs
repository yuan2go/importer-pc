using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime
{
    sealed record AutomaticCleanupState(string State, DateTimeOffset? LastCompleted, DateTimeOffset NextCheck, string? Detail, string? Backup = null);
    AutomaticCleanupState? automaticCleanup;
    Task? automaticCleanupWork;
    DateTimeOffset cleanupWarmup = DateTimeOffset.UtcNow.AddSeconds(30);
    TimeProvider cleanupClock = TimeProvider.System;
    DateTimeOffset? cleanupUtc;
    long cleanupTicks;
    bool cleanupClockFault;
    bool cleanupStateFault;
    internal TimeProvider CleanupClockForVerification { set { cleanupClock = value; cleanupUtc = null; cleanupClockFault = false; } }
    string AutomaticCleanupFile => Path.Combine(directory!, "automatic-cleanup.json");
    bool PersistAutomaticCleanup()
    {
        try { ExtensionStore.AtomicWrite(AutomaticCleanupFile, automaticCleanup!); return true; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            cleanupStateFault = true;
            automaticCleanup = new("维护记录待核对", automaticCleanup?.LastCompleted, cleanupClock.GetUtcNow().AddMinutes(15),
                "自动清理已停止，保留原状态文件和已提交业务事实；请核对维护记录。" + DiagnosticText.Protect(error.Message));
            Diagnostic("维护", "自动清理状态记录失败", "警告", DiagnosticText.Protect(error.Message));
            return false;
        }
    }
    internal async Task CheckAutomaticCleanupAsync(DateTimeOffset now)
    {
        Task? work;
        await gate.WaitAsync().ConfigureAwait(false);
        try { TryAutomaticCleanup(now); work = automaticCleanupWork; }
        finally { gate.Release(); }
        if (work is not null) await work.ConfigureAwait(false);
    }
    public async Task<StationCleanupStatus> ReadCleanupStatusAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            Management.Require();
            return new(Effective.OperationsPolicy?.AutomaticHistoryCleanup == true, automaticCleanup?.State ?? "尚未执行",
                automaticCleanup?.LastCompleted, automaticCleanup?.NextCheck, automaticCleanup?.Detail);
        }
        finally { gate.Release(); }
    }
    void TryAutomaticCleanup(DateTimeOffset now)
    {
        if (Id != "sportmaster" || Effective.OperationsPolicy is not { AutomaticHistoryCleanup: true } policy || cleanupStateFault) return;
        var ticks = cleanupClock.GetTimestamp();
        if (cleanupUtc is { } prior && Math.Abs((now - prior - cleanupClock.GetElapsedTime(cleanupTicks, ticks)).TotalSeconds) > 5)
        {
            cleanupClockFault = true;
            automaticCleanup = new("时钟待核对", automaticCleanup?.LastCompleted, now.AddMinutes(15), "检测到系统时间跳变，自动清理已停止。请核对系统时间后重启软件；原件保留。");
            if (!PersistAutomaticCleanup()) return;
        }
        cleanupUtc = now; cleanupTicks = ticks;
        if (cleanupClockFault || now < cleanupWarmup ||
            automaticCleanupWork is { IsCompleted: false } || stopping.IsCancellationRequested) return;
        if (automaticCleanup is null && File.Exists(AutomaticCleanupFile))
        {
            try
            {
                RecoveryFiles.RequireOrdinaryAncestors(AutomaticCleanupFile);
                if (new FileInfo(AutomaticCleanupFile).Length > 65536) throw new InvalidDataException("自动清理状态超出预算，保留原件");
                automaticCleanup = Wire.Decode<AutomaticCleanupState>(File.ReadAllText(AutomaticCleanupFile));
                if (automaticCleanup is null || string.IsNullOrWhiteSpace(automaticCleanup.State) || automaticCleanup.NextCheck == default)
                    throw new InvalidDataException("自动清理状态缺少必要字段，保留原件");
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                cleanupStateFault = true;
                automaticCleanup = new("维护状态待核对", null, now.AddMinutes(15), "自动清理已停止，原状态文件保留。" + DiagnosticText.Protect(error.Message));
                Diagnostic("维护", "自动清理状态无法读取", "警告", DiagnosticText.Protect(error.Message));
                return;
            }
        }
        if (automaticCleanup is not null && now < automaticCleanup.NextCheck) return;
        var local = TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById(policy.TimeZoneId));
        if (local.Hour < policy.CleanupHour) { cleanupWarmup = now.AddMinutes(1); return; }
        if (configuration!.Current.Pending is not null || configuration.Current.Confirmation?.Required != false ||
            backupInProgress || configurationSaving || externalWaits != 0 || activeStages.Count != 0 || attachedRunning.Count != 0 ||
            Volatile.Read(ref pendingObservations) != 0 || latestInputs.Values.Any(i => now - i.RecordedAt < TimeSpan.FromSeconds(30)) ||
            operations.Values.Any(o => (!MainTerminated(o) && !NeedsReconciliation(o)) || o.OriginalFrame?.State == "Pending"))
        { cleanupWarmup = now.AddMinutes(1); return; }
        var captured = Wire.Decode<ParcelConfiguration>(Wire.Encode(Effective));
        if (!repository!.RetentionDue(captured.Retention!.HistoryDays!.Value, now)) { cleanupWarmup = now.AddHours(1); return; }
        automaticCleanup = new("运行中", automaticCleanup?.LastCompleted, now.AddMinutes(15), "空闲窗口内先完整备份，再执行有界清理；原请求与未决责任保留。");
        if (!PersistAutomaticCleanup()) return;
        try
        {
            automaticCleanupWork = OwnedRead(t => RunAutomaticCleanup(captured, now, t));
            auxiliary.Add(automaticCleanupWork);
        }
        catch (ReadOperationCapacityException)
        {
            automaticCleanup = new("等待后台读取完成", automaticCleanup.LastCompleted, now.AddMinutes(1), "后台读取容量已满，本轮清理延后，不阻断供包。");
            _ = PersistAutomaticCleanup();
        }
        catch (InvalidOperationException) when (stopping.IsCancellationRequested) { }
    }
    async Task<bool> RunAutomaticCleanup(ParcelConfiguration captured, DateTimeOffset now, CancellationToken token)
    {
        var ownsFence = false; string? backup = null; var previous = automaticCleanup?.LastCompleted;
        try
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(token); budget.CancelAfter(TimeSpan.FromSeconds(45));
            var cancellation = budget.Token;
            await gate.WaitAsync(cancellation).ConfigureAwait(false);
            try
            {
                if (Wire.Encode(Effective) != Wire.Encode(captured) || configuration!.Current.Pending is not null || configurationSaving || backupInProgress ||
                    externalWaits != 0 || activeStages.Count != 0 || attachedRunning.Count != 0 || Volatile.Read(ref pendingObservations) != 0)
                    throw new InvalidOperationException("运行状态已变化，本次自动清理延后");
                configurationSaving = backupInProgress = ownsFence = true;
                var archived = repository!.PrepareAutomaticRetention(definition, now.AddDays(-captured.Retention!.HistoryDays!.Value));
                foreach (var id in archived) operations.Remove(id);
            }
            finally { gate.Release(); }
            var policy = captured.OperationsPolicy!;
            var backupRoot = Path.Combine(launch.DataRoot, "maintenance-backups", captured.Mode.ToString(), captured.StationId, Id); RecoveryFiles.RequireOrdinaryAncestors(backupRoot);
            Directory.CreateDirectory(backupRoot);
            if (Directory.EnumerateDirectories(backupRoot, "*.recovery").Take(policy.MaximumBackupSets).Count() >= policy.MaximumBackupSets)
                throw new IOException("自动维护备份已达份数上限，请在维护中心核对并转存；原备份不自动删除");
            backup = Path.Combine(backupRoot, now.ToString("yyyyMMddTHHmmss") + "-" + Wire.Id() + ".db");
            ParcelRecovery.CreateBackup(directory!, backup, captured, null, repository!.StorageBytes,
                p =>
                {
                    using var source = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
                    {
                        DataSource = Path.Combine(directory!, Id + ".db"),
                        Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly,
                        Pooling = false
                    }.ToString());
                    source.Open(); Ipc.Platform.Storage.Database.Backup(source, p, cancellation);
                }, factory.RegisterBackup, cancellation.ThrowIfCancellationRequested, cancellation);
            var manifest = ParcelRecovery.VerifyBackup(backup, cancellation.ThrowIfCancellationRequested, cancellation);
            await gate.WaitAsync(cancellation).ConfigureAwait(false);
            try
            {
                var result = repository.ExpireHistoryCore(definition, captured.Retention!.HistoryDays!.Value, now, manifest.DatabaseHash,
                    cancellation.ThrowIfCancellationRequested, onlineOwned: true, maximumOperations: 64, maximumInputs: 256);
                automaticCleanup = new("本轮完成", now, result.Operations >= 64 || result.Inputs >= 256 ? now.AddMinutes(15) : now.AddDays(1), $"本轮清理{result.Operations}条已结作业、{result.Inputs}条输入；统计汇总及永久防重身份已保留。", backup);
                repository.Commit(null, "AutomaticRetentionCompleted", Wire.Encode(new { result.Run, result.Operations, result.Inputs, result.BackupHash }));
                Diagnostic("维护", "自动历史清理完成", detail: "Run=" + result.Run);
            }
            finally { gate.Release(); }
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or UnauthorizedAccessException or OperationCanceledException or Microsoft.Data.Sqlite.SqliteException)
        {
            GuardBackupSourceFailure(error, "AutomaticRetention");
            automaticCleanup = new("本轮未完成", previous, now.AddMinutes(15), "原件和已提交清理事实保留；请在维护中心查看详情。" + DiagnosticText.Protect(error.Message), backup);
            Diagnostic("维护", "自动历史清理未完成", "警告", DiagnosticText.Protect(error.Message));
        }
        finally
        {
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (ownsFence) { configurationSaving = false; backupInProgress = false; }
                if (automaticCleanup is not null) _ = PersistAutomaticCleanup();
            }
            finally { gate.Release(); }
        }
        return true;
    }
}
public sealed partial class ParcelRepository
{
    internal string[] PrepareAutomaticRetention(FlowDefinition flow, DateTimeOffset cutoff)
    {
        PrepareRetentionCommandOwners();
        var ids = Load().Where(o => o.ObservedAt < cutoff && RetentionSettled(o, flow) && RetentionClear(o.Id, cutoff)).Take(64).Select(o => o.Id).ToArray();
        if (ids.Length != 0) Archive(ids, "Automatic retention pre-backup archival; original custody retained");
        return ids;
    }
}

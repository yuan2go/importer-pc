using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime : ICameraInspection
{
    sealed record IsolationFence(string Id, string CommandId, string CommandHash, string ConfigurationHash, string Reason,
        ManagementModeEvidence Actor, DateTimeOffset StartedAt, bool PriorPause);
    sealed record CollectingInspection(string Id, CameraInspectionMode Mode, DateTimeOffset Expires, CancellationToken Authority, long StartedTicks, TimeSpan Lifetime);
    readonly SemaphoreSlim inspectionControl = new(1, 1);
    readonly SemaphoreSlim inspectionExportControl = new(1, 1);
    IsolationFence? inspectionFence;
    CollectingInspection? collectingInspection;
    string? lastInspectionSession, inspectionStoreFailure;
    CameraInspectionStore? inspectionStore;
    DateTimeOffset nextInspectionRetention;
    Task? inspectionRetention;
    bool singleReadInspection;
    bool CameraBusinessFenced => Volatile.Read(ref inspectionFence) is not null;
    void InitializeCameraInspection()
    {
        if (Id != "sportmaster") return;
        if (repository!.LatestCameraIsolation() is { } original)
        {
            var fence = Wire.Decode<IsolationFence>(original);
            Volatile.Write(ref inspectionFence, fence); lastInspectionSession = fence.Id;
            // This remains effective even if diagnostic files were not in a
            // business backup. The business audit, not the optional image index,
            // owns the durable production admission fence.
            repository.Commit(null, "CameraIsolationRecoveryFenced", "原实机调试未显式清场；不恢复生产、不回放隔离输入。", paused: true);
        }
        try { inspectionStore = new(directory!, () => Effective.Retention?.MinimumFreeBytes ?? SystemParameterDefaults.MinimumFreeBytes); }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        { inspectionStoreFailure = "原相机诊断集合不可用，保留原件：" + error.GetType().Name; }
    }
    public Task<CameraInspectionStatus> CameraInspectionStatusAsync(CancellationToken token) => InspectionRead(t => Task.Run(() =>
    {
        var fence = Volatile.Read(ref inspectionFence); var sessionId = Volatile.Read(ref collectingInspection)?.Id ?? fence?.Id ?? lastInspectionSession;
        if (inspectionStore is { } store)
        {
            var value = store.Status(sessionId, fence is not null) with { SingleReadRounds = singleReadInspection };
            if (inspectionStoreFailure is { } failed) return value with { State = "Faulted", Detail = failed };
            if (Volatile.Read(ref collectingInspection) is null && value.State is "Active" or "Preparing")
                return value with { State = inspectionControl.CurrentCount == 0 ? "Transitioning" : fence is null ? "Stopped" : "RecoveryFenced", Detail = "当前无活动采集；切换、停止或恢复尚待核对。隔离栅不自动解除。" };
            return value;
        }
        return new CameraInspectionStatus(sessionId, fence is null ? null : CameraInspectionMode.Isolated, fence is null ? "Unavailable" : "RecoveryFenced", fence is not null,
            null, 0, 0, 0, 0, inspectionStoreFailure ?? "诊断集合尚未打开。", fence?.ConfigurationHash ?? "");
    }, t), token);
    static void ValidateInspectionCommand(string id, string reason)
    {
        if (!Guid.TryParseExact(id, "N", out _) || reason.Trim().Length < 5 || reason.Length > 1024 || reason.Any(c => c == '\0'))
            throw new InvalidOperationException("调试命令需要唯一身份和5～1024字的操作原因。");
    }
    IObservationInspectionSource[] InspectionSources() => readers.Values
        .Where(r => !ParcelConnectionSetup.IsDeferred(Effective, r.Source.DeviceId)).Select(r => r.Source)
        .Select(source => source as IObservationInspectionSource ?? throw new InvalidOperationException("当前相机来源不支持原帧关联与隔离切换边界：" + source.DeviceId)).ToArray();
    public Task<CameraInspectionStatus> BeginCameraInspectionAsync(BeginCameraInspection command, CancellationToken token) => OwnedAdministration(async owned =>
    {
        using var requested = CancellationTokenSource.CreateLinkedTokenSource(owned, token); var t = requested.Token;
        ValidateInspectionCommand(command.CommandId, command.Reason);
        if (command.Mode == CameraInspectionMode.Isolated && !command.PhysicalSceneConfirmed) throw new InvalidOperationException("进入实机隔离前须确认已停止真实投放，并核对两位当前实体与原责任。");
        if (command.Limits.DurationMinutes is < 1 or > 60 || command.Limits.ImageMiB is < 64 or > 2048 || command.Limits.Records is < 1000 or > 500000 || !Enum.IsDefined(command.Mode))
            throw new InvalidOperationException("调试预算：1～60分钟、64～2048MiB原图、1000～500000条元数据。");
        var authority = Management.Require(); var actor = ManagementEvidence ?? throw new UnauthorizedAccessException();
        await inspectionControl.WaitAsync(t).ConfigureAwait(false);
        string? id = null; IObservationInspectionSource[] sources = [];
        try
        {
            await gate.WaitAsync(t).ConfigureAwait(false);
            try
            {
                authority.ThrowIfCancellationRequested(); Management.Require();
                if (Id != "sportmaster" || command.RuntimeId != runtimeId || stopped || stopping.IsCancellationRequested) throw new InvalidOperationException("运行实例已变化，请重新读取调试状态。");
                if (command.SingleReadRounds && command.Mode != CameraInspectionMode.Isolated) throw new InvalidOperationException("按轮扫码必须先建立实机隔离。");
                var hash = Wire.Hash(Wire.Encode(command));
                if (inspectionFence is { } prior)
                {
                    if (prior.CommandId == command.CommandId && prior.CommandHash == hash)
                        return InspectionStore.Status(prior.Id, true);
                    throw new InvalidOperationException("原隔离尚未清场；核对原会话，不重复进入。");
                }
                if (InspectionStore.FindCommand(command.CommandId, hash) is { } existing) return InspectionStore.Status(existing, false);
                if (collectingInspection is not null) throw new InvalidOperationException("已有活动关联采集，请先结束原会话。");
                if (configuration!.Current.Pending is not null) throw new InvalidOperationException("存在待重启配置；请先完成受控应用并核对生效参数，再开始关联会话。");
                if (command.ExpectedConfigurationRevision != configuration!.Current.Revision || backupInProgress || configurationSaving || maintenanceOperations.Count != 0)
                    throw new InvalidOperationException("生效配置账修订或维护状态已变化，请重新核对后操作。");
                sources = InspectionSources(); if (sources.Length == 0) throw new InvalidOperationException("请先配置至少一台内置MVS相机。");
                id = Wire.Id(); var configHash = Wire.Hash(Wire.Encode(Effective));
                if (command.Mode == CameraInspectionMode.Isolated)
                {
                    var fence = new IsolationFence(id, command.CommandId, hash, configHash, command.Reason, actor, DateTimeOffset.UtcNow, repository.Paused);
                    // Commit first, then publish the in-memory fence while holding
                    // the same application gate used by all business dispatch.
                    repository.Commit(null, "CameraIsolationStarted", Wire.Encode(fence), paused: true);
                    Volatile.Write(ref inspectionFence, fence); resumePriorPause = false;
                }
                var expires = new[] { DateTimeOffset.UtcNow.AddMinutes(command.Limits.DurationMinutes), actor.ExpiresAt }.Min();
                InspectionStore.Begin(id, command, actor, expires, configHash);
                lastInspectionSession = id; Volatile.Write(ref collectingInspection, new(id, command.Mode, expires, authority,
                    System.Diagnostics.Stopwatch.GetTimestamp(), expires - DateTimeOffset.UtcNow));
            }
            finally { gate.Release(); }
            if (command.Mode == CameraInspectionMode.Isolated) await WaitInspectionDrainAsync(t).ConfigureAwait(false);
            singleReadInspection = command.SingleReadRounds;
            var registration = new InspectionRegistration(id!, command.Mode == CameraInspectionMode.Isolated, frame =>
            {
                var current = Volatile.Read(ref collectingInspection);
                if (current?.Id != id || current.Authority.IsCancellationRequested || current.Expires <= DateTimeOffset.UtcNow ||
                    System.Diagnostics.Stopwatch.GetElapsedTime(current.StartedTicks) >= current.Lifetime || stopping.IsCancellationRequested) return false;
                return command.SingleReadRounds ? ReceiveRoundFrame(id!, frame) : InspectionStore.Offer(id!, frame);
            }) { DecodeAllowed = device =>
                Volatile.Read(ref collectingInspection)?.Id == id && (!command.SingleReadRounds || RoundDecodeAllowed(id!, device)) };
            foreach (var source in sources) await source.SetInspectionAsync(registration, t).ConfigureAwait(false);
            authority.ThrowIfCancellationRequested(); Management.Require();
            InspectionStore.State(id!, "Active", command.Mode == CameraInspectionMode.Isolated
                ? "实机隔离已生效：新输入只记录诊断/阻断证据，不产生业务请求；结束后不回放。"
                : "生产观察采集：正常业务仍运行，放码仍可能产生RCS请求。");
            Diagnostic("相机调试", command.Mode == CameraInspectionMode.Isolated ? "实机调试隔离已进入" : "生产观察采集已开始", detail: Wire.Encode(new { Session = id, command.Reason, Actor = actor }));
            return InspectionStore.Status(id, CameraBusinessFenced);
        }
        catch (Exception error)
        {
            Volatile.Write(ref collectingInspection, null);
            if (id is not null && inspectionStore is { } store)
            {
                try { store.State(id, "Faulted", "进入未完成：" + error.GetType().Name + "；原隔离栅不自动解除。", close: true); }
                catch (Exception report) { inspectionStoreFailure = "调试进入失败且回执未保存：" + report.GetType().Name; }
            }
            throw;
        }
        finally { inspectionControl.Release(); }
    });
    async Task WaitInspectionDrainAsync(CancellationToken token)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (true)
        {
            await gate.WaitAsync(token).ConfigureAwait(false);
            try { if (externalWaits == 0 && pendingObservations == 0 && activeStages.Count == 0) return; }
            finally { gate.Release(); }
            if (DateTimeOffset.UtcNow >= deadline) throw new TimeoutException("原在途输入/请求未在10秒内排空。保持隔离，核对原责任，不能当作成功进入或恢复。");
            await Task.Delay(50, token).ConfigureAwait(false);
        }
    }
    public Task<CameraInspectionStatus> EndCameraInspectionAsync(EndCameraInspection command, CancellationToken token) => OwnedAdministration(async owned =>
    {
        using var requested = CancellationTokenSource.CreateLinkedTokenSource(owned, token); var t = requested.Token;
        ValidateInspectionCommand(command.CommandId, command.Reason); var authority = Management.Require();
        await inspectionControl.WaitAsync(t).ConfigureAwait(false);
        try
        {
            var fence = Volatile.Read(ref inspectionFence); var current = Volatile.Read(ref collectingInspection);
            if (fence?.Id != command.SessionId && current?.Id != command.SessionId)
            {
                if (lastInspectionSession == command.SessionId && inspectionStore is { } endedStore) return endedStore.Status(command.SessionId, CameraBusinessFenced);
                throw new InvalidOperationException("不是当前待处理会话，不能解除其他隔离。");
            }
            if (fence is not null && (!command.TestPiecesRemoved || fence.Id != command.SessionId)) throw new InvalidOperationException("必须确认两位测试件已移除并核对原责任，才能结束实机隔离。");
            await EndReadRoundAsync(t, command.SessionId).ConfigureAwait(false);
            Volatile.Write(ref collectingInspection, null);
            // Await the actual acquisition-cycle boundary, not a cancelled wrapper.
            foreach (var source in InspectionSources())
            {
                // Reassert the old scope before the final clear. A cancelled prior
                // end may have completed its SDK cutover but not its audit release.
                if (fence is not null) await source.SetInspectionAsync(new(fence.Id, true, _ => false, false), t).ConfigureAwait(false);
                await source.SetInspectionAsync(null, t).ConfigureAwait(false);
            }
            if (inspectionStore is { } store) await store.FlushAsync(t).ConfigureAwait(false);
            if (fence is not null) await WaitInspectionDrainAsync(t).ConfigureAwait(false);
            await gate.WaitAsync(t).ConfigureAwait(false);
            try
            {
                authority.ThrowIfCancellationRequested(); Management.Require();
                if (fence is not null)
                {
                    if (inspectionFence?.Id != fence.Id) throw new InvalidOperationException("隔离身份已变化。");
                    repository!.Commit(null, "CameraIsolationReleased", Wire.Encode(new { Session = fence.Id, Command = command, Actor = ManagementEvidence,
                        Policy = "隔离输入永不回放；只解除此调试栅，原请求与其他守卫继续有效" }), paused: true);
                    Volatile.Write(ref inspectionFence, null);
                    if (command.ResumeProduction)
                    {
                        resumePriorPause = definition.AutomaticConfigurationQuiescence;
                        if (CanResume()) { repository.Commit(null, "CameraInspectionSupplyRestored", "已确认测试件移除，现行准入守卫通过；不回放隔离输入。", paused: false); resumePriorPause = false; }
                    }
                }
            }
            finally { gate.Release(); }
            if (inspectionStore is { } originalStore)
            {
                try { originalStore.State(command.SessionId, "Closed", "关联采集结束；原业务账和隔离记录保留。", close: true); }
                catch (InvalidOperationException) when (fence is not null) { inspectionStoreFailure = "原诊断集合缺失；隔离清场事实已在业务审计保存。"; }
            }
            Diagnostic("相机调试", "调试会话已结束", detail: Wire.Encode(new { Session = command.SessionId, Command = command, Actor = ManagementEvidence }));
            return inspectionStore?.Status(command.SessionId, CameraBusinessFenced) ?? new(command.SessionId, null, "Closed", CameraBusinessFenced, null, 0, 0, 0, 0, inspectionStoreFailure ?? "已结束。", "");
        }
        finally { inspectionControl.Release(); }
    });
    public Task<CameraInspectionStatus> StopCameraInspectionCaptureAsync(string sessionId, string reason, CancellationToken token) => OwnedAdministration(async owned =>
    {
        if (reason.Trim().Length < 5 || reason.Length > 1024) throw new InvalidOperationException("请填写至少5字的停止原因。");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(owned, token); var t = linked.Token; var authority = Management.Require();
        await inspectionControl.WaitAsync(t).ConfigureAwait(false);
        try
        {
            var current = Volatile.Read(ref collectingInspection);
            if (current?.Id != sessionId) throw new InvalidOperationException("原采集已结束或身份变化，请刷新状态。");
            await EndReadRoundAsync(t, sessionId).ConfigureAwait(false);
            Volatile.Write(ref collectingInspection, null);
            var fence = Volatile.Read(ref inspectionFence);
            foreach (var source in InspectionSources()) await source.SetInspectionAsync(fence is null ? null : new InspectionRegistration(fence.Id, true, _ => false, false) { DecodeAllowed = _ => false }, t).ConfigureAwait(false);
            await InspectionStore.FlushAsync(t).ConfigureAwait(false); authority.ThrowIfCancellationRequested(); Management.Require();
            InspectionStore.State(sessionId, fence is null ? "Closed" : "Suspended", reason + (fence is null ? "；生产观察已结束。" : "；仅停止采集，实机隔离继续阻断，仍需确认清场。"), close: true);
            Diagnostic("相机调试", "关联采集已显式停止", detail: Wire.Encode(new { Session = sessionId, Reason = reason, Actor = ManagementEvidence }));
            return InspectionStore.Status(sessionId, fence is not null);
        }
        finally { inspectionControl.Release(); }
    });
    void CheckCameraInspectionLease()
    {
        if (inspectionStore is { } originalStore && Volatile.Read(ref collectingInspection) is null &&
            DateTimeOffset.UtcNow >= nextInspectionRetention && inspectionRetention is not { IsCompleted: false })
        {
            nextInspectionRetention = DateTimeOffset.UtcNow.AddMinutes(1); var days = Effective.Retention?.DiagnosticLogDays ?? 7;
            inspectionRetention = Task.Run(async () =>
            {
                try
                {
                    await inspectionControl.WaitAsync(stopping.Token).ConfigureAwait(false);
                    try
                    {
                        if (Volatile.Read(ref collectingInspection) is not null) return;
                        await originalStore.FlushAsync(stopping.Token).ConfigureAwait(false);
                        originalStore.PruneClosed(days, Volatile.Read(ref inspectionFence)?.Id);
                    }
                    finally { inspectionControl.Release(); }
                }
                catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
                catch (Exception error) { Volatile.Write(ref inspectionStoreFailure, "诊断保留期清理未完成：" + error.GetType().Name + "；保留原件，业务隔离状态不变。"); }
            });
            auxiliary.Add(inspectionRetention);
        }
        var current = Volatile.Read(ref collectingInspection); if (current is null) return;
        var expired = current.Authority.IsCancellationRequested || current.Expires <= DateTimeOffset.UtcNow || System.Diagnostics.Stopwatch.GetElapsedTime(current.StartedTicks) >= current.Lifetime;
        var fault = inspectionStore?.Fault;
        if (!expired && fault is null) return;
        if (readRound?.Session == current.Id) readRound.Cancel.Cancel();
        Volatile.Write(ref collectingInspection, null);
        var detail = expired ? "管理授权或会话期限已结束；停止采集，原隔离继续阻断，需重新进入管理模式并确认清场。" : fault!;
        if (inspectionStore is { } store) auxiliary.Add(Task.Run(() =>
        {
            try { store.State(current.Id, expired ? "Expired" : "Faulted", detail, close: true); }
            catch (Exception error) { Volatile.Write(ref inspectionStoreFailure, "调试停止回执未保存：" + error.GetType().Name + "；原业务隔离审计继续有效。"); }
        }));
        Diagnostic("相机调试", "关联采集已停止", "警告", detail);
    }
}

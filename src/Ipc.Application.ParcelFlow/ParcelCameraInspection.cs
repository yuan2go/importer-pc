using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime : ICameraInspection
{
    sealed record CollectingInspection(string Id, CameraInspectionMode Mode, DateTimeOffset Expires, CancellationToken Authority, long StartedTicks, TimeSpan Lifetime);
    readonly SemaphoreSlim inspectionControl = new(1, 1);
    readonly SemaphoreSlim inspectionExportControl = new(1, 1);
    CollectingInspection? collectingInspection;
    string? lastInspectionSession, inspectionStoreFailure;
    CameraInspectionStore? inspectionStore;
    DateTimeOffset nextInspectionRetention;
    Task? inspectionRetention;
    bool singleReadInspection;
    void InitializeCameraInspection()
    {
        if (Id != "sportmaster") return;
        if (repository!.LatestLegacyCameraIsolation() is { } original)
            repository.Commit(null, "CameraIsolationRetired", Wire.Encode(new
            {
                OriginalRecordHash = Wire.Hash(original),
                Policy = "SM-DEBUG-NO-ISOLATION-31: retire only the obsolete debug admission fence; preserve original audits, requests and pause state; no replay"
            }));
        try { inspectionStore = new(directory!, () => Effective.Retention?.MinimumFreeBytes ?? SystemParameterDefaults.MinimumFreeBytes); }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        { inspectionStoreFailure = "原相机诊断集合不可用，原件保留；请导出诊断记录。"; }
    }
    public Task<CameraInspectionStatus> CameraInspectionStatusAsync(CancellationToken token) => InspectionRead(t => Task.Run(() =>
    {
        var current = Volatile.Read(ref collectingInspection);
        var session = current?.Id ?? lastInspectionSession;
        if (inspectionStore is { } store)
        {
            var value = store.Status(session, false) with { SingleReadRounds = singleReadInspection };
            if (inspectionStoreFailure is { } failed) return value with { State = "Faulted", Detail = failed };
            if (current is null && value.State is "Active" or "Preparing")
                return value with { State = inspectionControl.CurrentCount == 0 ? "Transitioning" : "Stopped", Detail = "当前没有活动调试采集，正常供包不受影响。" };
            return value;
        }
        return new CameraInspectionStatus(session, null, "Unavailable", false, null, 0, 0, 0, 0,
            inspectionStoreFailure ?? "诊断集合尚未打开。", "")
        { SingleReadRounds = singleReadInspection };
    }, t), token);
    static void ValidateInspectionCommand(string id, string reason)
    {
        if (!Guid.TryParseExact(id, "N", out _) || string.IsNullOrWhiteSpace(reason) || reason.Length is < 5 or > 1024)
            throw new InvalidOperationException("调试命令身份或说明无效，请重新操作。");
    }
    IObservationInspectionSource[] InspectionSources() => readers.Values.Where(r => !ParcelConnectionSetup.IsDeferred(Effective, r.Source.DeviceId)).Select(r => r.Source)
        .Select(source => source as IObservationInspectionSource ?? throw new InvalidOperationException("当前相机来源不支持原帧关联：" + source.DeviceId)).ToArray();
    public Task<CameraInspectionStatus> BeginCameraInspectionAsync(BeginCameraInspection command, CancellationToken token) => OwnedAdministration(async owned =>
    {
        using var requested = CancellationTokenSource.CreateLinkedTokenSource(owned, token); var t = requested.Token;
        ValidateInspectionCommand(command.CommandId, command.Reason);
        if (command.Mode != CameraInspectionMode.ObserveProduction)
            throw new InvalidOperationException("调试隔离已删除；请使用当前扫码调试页面，正常供包继续运行。");
        if (command.Limits.DurationMinutes is < 1 or > 60 || command.Limits.ImageMiB is < 64 or > 2048 || command.Limits.Records is < 1000 or > 500000)
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
                if (Id != "sportmaster" || command.RuntimeId != runtimeId || stopped || stopping.IsCancellationRequested || repository is null || configuration is null)
                    throw new InvalidOperationException("运行实例已变化，请重新读取调试状态。");
                if (InspectionStore.FindCommand(command.CommandId, Wire.Hash(Wire.Encode(command))) is { } existing) return InspectionStore.Status(existing, false);
                if (collectingInspection is not null) throw new InvalidOperationException("已有调试采集正在运行，请先结束该采集。");
                if (configuration.Current.Pending is not null) throw new InvalidOperationException("存在待重启配置；请先应用并核对生效参数，再开始扫码调试。");
                if (command.ExpectedConfigurationRevision != configuration.Current.Revision || backupInProgress || configurationSaving || maintenanceOperations.Count != 0)
                    throw new InvalidOperationException("生效配置修订或维护状态已变化，请重新核对后操作。");
                sources = InspectionSources(); if (sources.Length == 0) throw new InvalidOperationException("请先配置至少一台海康相机。");
                if (InspectionStore.Fault is { } previousFault)
                {
                    var previousError = InspectionStore.LastFailure?.ToString();
                    await InspectionStore.RetryAfterFailureAsync(t).ConfigureAwait(false);
                    Diagnostic("相机调试", "重新尝试诊断采集", detail: Wire.Encode(new { PreviousFault = previousFault, PreviousError = previousError, Dropped = InspectionStore.Dropped }));
                }
                id = Wire.Id(); var expires = new[] { DateTimeOffset.UtcNow.AddMinutes(command.Limits.DurationMinutes), actor.ExpiresAt }.Min();
                InspectionStore.Begin(id, command, actor, expires, Wire.Hash(Wire.Encode(Effective)));
                // A single-read session can expire before it ever reaches Arm.
                // Its cleanup still belongs to this management window.
                if (ManagementWorkspaceOpen) workspaceInspectionSessions.TryAdd(id, 0);
                lastInspectionSession = id;
                Volatile.Write(ref collectingInspection, new(id, command.Mode, expires, authority,
                    System.Diagnostics.Stopwatch.GetTimestamp(), expires - DateTimeOffset.UtcNow));
                singleReadInspection = command.SingleReadRounds;
            }
            finally { gate.Release(); }
            // Read rounds attach their passive observer to the selected camera when armed.
            // A continuous sample observes normal frames; neither path changes business admission.
            if (!command.SingleReadRounds)
            {
                var registration = new InspectionRegistration(id!, false, frame =>
                {
                    var current = Volatile.Read(ref collectingInspection);
                    return current?.Id == id && !current.Authority.IsCancellationRequested && current.Expires > DateTimeOffset.UtcNow &&
                        System.Diagnostics.Stopwatch.GetElapsedTime(current.StartedTicks) < current.Lifetime && InspectionStore.Offer(id!, frame);
                });
                foreach (var source in sources) await ApplyInspectionAsync(source, registration, t).ConfigureAwait(false);
            }
            authority.ThrowIfCancellationRequested(); Management.Require();
            InspectionStore.State(id!, "Active", "调试采集已开始；正常供包继续，调试请求仅在明确发送时调用实际RCS服务。");
            Diagnostic("相机调试", "调试采集已开始", detail: Wire.Encode(new { Session = id, command.Reason, Actor = actor }));
            return InspectionStore.Status(id, false) with { SingleReadRounds = singleReadInspection };
        }
        catch (Exception error)
        {
            // Validation failures must not retire a collection owned by another command.
            if (id is not null)
            {
                if (collectingInspection?.Id == id) Volatile.Write(ref collectingInspection, null);
                foreach (var source in sources)
                    try { await ApplyInspectionAsync(source, null, CancellationToken.None).ConfigureAwait(false); }
                    catch (Exception cleanup) { Diagnostic("相机调试", "采集清理未完成", "警告", cleanup.ToString()); }
            }
            if (id is not null && inspectionStore is { } store)
                try { store.State(id, "Faulted", "调试采集未能开始；正常供包状态未改变。", close: true); }
                catch (Exception report) { inspectionStoreFailure = "调试启动回执未保存；请导出诊断记录。"; Diagnostic("相机调试", "启动回执未保存", "警告", report.ToString()); }
            Diagnostic("相机调试", "调试采集启动失败", "警告", error.ToString());
            throw;
        }
        finally { inspectionControl.Release(); }
    });
    public Task<CameraInspectionStatus> EndCameraInspectionAsync(EndCameraInspection command, CancellationToken token) => OwnedAdministration(async owned =>
    {
        using var requested = CancellationTokenSource.CreateLinkedTokenSource(owned, token); var t = requested.Token;
        ValidateInspectionCommand(command.CommandId, command.Reason); var authority = Management.Require();
        await inspectionControl.WaitAsync(t).ConfigureAwait(false);
        try
        {
            var current = Volatile.Read(ref collectingInspection);
            if (current?.Id != command.SessionId)
            {
                if (lastInspectionSession == command.SessionId && inspectionStore is { } endedStore) return endedStore.Status(command.SessionId, false);
                throw new InvalidOperationException("不是当前调试采集，不能结束其他会话。");
            }
            if (command.CloseReadRounds && (!singleReadInspection || readRound?.Id != command.ExpectedRoundId))
                throw new CameraReadRoundConflictException("扫码轮次已变化；本窗口不结束其他窗口的当前轮次。");
            await EndReadRoundAsync(t, command.SessionId).ConfigureAwait(false);
            foreach (var source in InspectionSources()) await ApplyInspectionAsync(source, null, t).ConfigureAwait(false);
            if (inspectionStore is { } store) await store.FlushAsync(t).ConfigureAwait(false);
            authority.ThrowIfCancellationRequested(); Management.Require();
            Volatile.Write(ref collectingInspection, null);
            InspectionStore.State(command.SessionId, "Closed", "调试采集已结束；正常供包与原请求责任保持。", close: true);
            Diagnostic("相机调试", "调试采集已结束", detail: Wire.Encode(new { Session = command.SessionId, Command = command, Actor = ManagementEvidence }));
            return InspectionStore.Status(command.SessionId, false);
        }
        finally { inspectionControl.Release(); }
    });
    public async Task<CameraInspectionStatus> StopCameraInspectionCaptureAsync(string sessionId, string reason, CancellationToken token)
        => await EndCameraInspectionAsync(new(Wire.Id(), sessionId, false, false, reason), token).ConfigureAwait(false);
    void CheckCameraInspectionLease()
    {
        if (inspectionStore is { } originalStore && Volatile.Read(ref collectingInspection) is null && DateTimeOffset.UtcNow >= nextInspectionRetention && inspectionRetention is not { IsCompleted: false })
        {
            nextInspectionRetention = DateTimeOffset.UtcNow.AddMinutes(1); var days = Effective.Retention?.DiagnosticLogDays ?? 7;
            inspectionRetention = Task.Run(async () =>
            {
                try
                {
                    await inspectionControl.WaitAsync(stopping.Token).ConfigureAwait(false);
                    try { if (collectingInspection is null) { await originalStore.FlushAsync(stopping.Token).ConfigureAwait(false); originalStore.PruneClosed(days, null); } }
                    finally { inspectionControl.Release(); }
                }
                catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
                catch (Exception error) { inspectionStoreFailure = "诊断保留期清理未完成，原件保留。"; Diagnostic("相机调试", "诊断清理未完成", "警告", error.ToString()); }
            });
            auxiliary.Add(inspectionRetention);
        }
        var current = Volatile.Read(ref collectingInspection); if (current is null) return;
        var expired = current.Authority.IsCancellationRequested || current.Expires <= DateTimeOffset.UtcNow || System.Diagnostics.Stopwatch.GetElapsedTime(current.StartedTicks) >= current.Lifetime;
        if (!expired && inspectionStore?.Fault is null) return;
        if (readRound?.Session == current.Id) readRound.Cancel.Cancel();
        Volatile.Write(ref collectingInspection, null);
        auxiliary.Add(Task.Run(async () =>
        {
            await inspectionControl.WaitAsync().ConfigureAwait(false);
            try
            {
                // Closing the original management window already restores its
                // sources under inspectionControl. A queued expiry cleanup for
                // that closed session must not clear the live SDK buffers again.
                if (collectingInspection is null && (!workspaceInspectionSessions.ContainsKey(current.Id) || ManagementWorkspaceOpen))
                    foreach (var source in InspectionSources()) await ApplyInspectionAsync(source, null, CancellationToken.None).ConfigureAwait(false);
                inspectionStore?.State(current.Id, expired ? "Expired" : "Faulted", "调试采集已停止；原请求保留，正常供包状态未改变。", close: true);
            }
            catch (Exception error) { inspectionStoreFailure = "调试停止回执未保存，请导出诊断记录。"; Diagnostic("相机调试", "调试停止未完成", "警告", error.ToString()); }
            finally { inspectionControl.Release(); }
        }));
    }
}

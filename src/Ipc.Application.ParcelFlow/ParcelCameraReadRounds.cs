using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime : ICameraReadRounds
{
    sealed class ReadRound(string id, string session, string device, string epoch, ParcelConfiguration config, CancellationToken authority)
    {
        public readonly string Id = id, Session = session, Device = device, Epoch = epoch;
        public readonly ParcelConfiguration Configuration = config;
        public readonly CancellationToken Authority = authority;
        public readonly CancellationTokenSource Cancel = new();
        public readonly object Sync = new();
        public TaskCompletionSource<InspectedFrame>? Capture = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task? Worker;
        public string State = "Preparing", Detail = "正在清理旧轮次与SDK缓存；尚未开始读取。";
        public string? Barcode, Operation;
        public CameraFrameMetadata? Frame;
        public bool Manual;
        public int ConsecutiveNoCodeFrames;
    }
    ReadRound? readRound;
    readonly HashSet<Task> cameraRoundWorkers = [];
    readonly HashSet<string> explicitDebugSends = [];
    bool RoundDecodeAllowed(string session, string device)
    {
        var round = Volatile.Read(ref readRound); var collection = Volatile.Read(ref collectingInspection);
        return round?.Session == session && round.Device == device && Volatile.Read(ref round.State) == "Reading" &&
            !round.Cancel.IsCancellationRequested && !round.Authority.IsCancellationRequested && collection?.Id == session &&
            !collection.Authority.IsCancellationRequested && collection.Expires > DateTimeOffset.UtcNow && System.Diagnostics.Stopwatch.GetElapsedTime(collection.StartedTicks) < collection.Lifetime;
    }
    bool ReceiveRoundFrame(string session, InspectedFrame frame)
    {
        var round = Volatile.Read(ref readRound);
        if (round?.Session != session || round.Device != frame.DeviceId || !RoundDecodeAllowed(session, frame.DeviceId)) return true;
        if (round.Epoch != frame.Epoch)
        { round.Capture?.TrySetException(new InvalidOperationException("相机连接周期已改变，请明确重新触发扫码。")); return true; }
        var observation = new Observation(frame.ObservationId ?? Wire.Id(), frame.DeviceId, frame.Epoch, frame.SourceEventId ?? "no-read", 1,
            "debug." + round.Id, "Read", frame.Symbols.Select(s => s.Text).ToArray(), null, frame.CapturedAt) { Symbols = frame.Symbols };
        var selection = definition.SelectBarcode(round.Configuration, observation);
        var emptyFrames = frame.Symbols.Length == 0 ? Interlocked.Increment(ref round.ConsecutiveNoCodeFrames) : 0;
        if (frame.Symbols.Length != 0) Interlocked.Exchange(ref round.ConsecutiveNoCodeFrames, 0);
        if (selection.SelectedCode is null || selection.Matches != 1)
        {
            Volatile.Write(ref round.Detail, frame.Symbols.Length == 0
                ? emptyFrames >= 3 ? "无条码。请调整包裹或条码朝向；本轮继续等待，也可点击“停止扫码”。" : "正在等待条码…"
                : "未取得唯一有效条码，继续等待；未生成请求。");
            if (emptyFrames == 3) Diagnostic("相机调试", "本轮未识别到条码", detail: Wire.Encode(new
            { Round = round.Id, round.Session, round.Device, frame.Epoch, frame.FrameNumber, frame.CapturedAt, ConsecutiveNoCodeFrames = emptyFrames, RequestSent = false }));
            return true;
        }
        lock (round.Sync)
        {
            if (!RoundDecodeAllowed(session, frame.DeviceId)) return true;
            round.Barcode = selection.SelectedCode;
            Volatile.Write(ref round.State, "Sealing"); // Closes decoding before publishing the winner.
            // Waiting frames remain live preview/read diagnostics. Only the winning
            // frame needs durable custody; archiving every empty frame can flood it.
            if (InspectionStore.Offer(session, frame)) round.Capture?.TrySetResult(frame);
            else round.Capture?.TrySetException(new IOException("本轮照片未能保存。"));
        }
        return true;
    }
    public Task<CameraReadRound> ArmCameraReadAsync(ArmCameraRead command, CancellationToken token) => OwnedAdministration(async owned =>
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(owned, token); var t = linked.Token;
        if (!Guid.TryParseExact(command.CommandId, "N", out _) || !Guid.TryParseExact(command.SessionId, "N", out _) || string.IsNullOrWhiteSpace(command.DeviceId) || command.DeviceId.Length > 256 ||
            command.ExpectedRoundId is not null && !Guid.TryParseExact(command.ExpectedRoundId, "N", out _)) throw new ArgumentException("扫码轮次命令或相机身份无效。");
        var receiptCommand = new BusinessCommand(command.CommandId, command.ConfigurationRevision, "ArmCameraRead", command.SessionId, Wire.Encode(command));
        var authority = Management.Require(); await inspectionControl.WaitAsync(t).ConfigureAwait(false);
        try
        {
            ReadRound round; IObservationInspectionSource[] sources;
            await gate.WaitAsync(t).ConfigureAwait(false);
            try
            {
                authority.ThrowIfCancellationRequested(); Management.Require();
                if (repository is null || stopped) throw new InvalidOperationException("Application未运行或已停止。");
                if (repository.PriorCommand(receiptCommand) is { } prior)
                {
                    if (prior.Status == "Rejected") throw new InvalidOperationException(prior.Reason);
                    return RoundProjection(); // Current facts, never replay a persisted arming command.
                }
                if (command.RuntimeId != runtimeId || command.ConfigurationRevision != configuration!.Current.Revision ||
                    collectingInspection?.Id != command.SessionId ||
                    !singleReadInspection || !SessionValid || configuration.Current.Pending is not null || backupInProgress || configurationSaving || maintenanceOperations.Count != 0)
                    throw new InvalidOperationException("需要当前生效配置、有效操作员和当前扫码调试会话；请先核对原会话。");
                if (readRound?.Id != command.ExpectedRoundId || readRound?.State is "Preparing" or "Reading" or "Sealing" or "Sending" || readRound?.Worker is { IsCompleted: false })
                    throw new CameraReadRoundConflictException("轮次已变化或正在处理，本次触发未接纳；请刷新并明确重新触发。");
                if (!readers.TryGetValue(command.DeviceId, out var reader) || reader.Health?.IsValid(DateTimeOffset.UtcNow) != true)
                    throw new InvalidOperationException("所选相机没有有效连接确认，不能触发扫码。");
                authority.ThrowIfCancellationRequested(); Management.Require();
                var scope = collectingInspection!;
                if (scope.Authority.IsCancellationRequested || scope.Expires <= DateTimeOffset.UtcNow || System.Diagnostics.Stopwatch.GetElapsedTime(scope.StartedTicks) >= scope.Lifetime)
                    throw new InvalidOperationException("本轮调试采集期限已结束，请重新触发扫码。");
                CloseUnsentRound("明确开始下一轮，旧预备请求未发送并已取消；原结果保留。");
                var retired = readRound;
                round = new(command.CommandId, command.SessionId, command.DeviceId, reader.Epoch,
                    Wire.Decode<ParcelConfiguration>(Wire.Encode(Effective)), authority);
                repository.Commit(null, "CameraReadRoundPreparing", Wire.Encode(new { Command = command, Actor = ManagementEvidence }), command: receiptCommand,
                    commandResult: new("Pending", "已登记轮次，SDK边界尚待确认；不自动重开。", repository.Revision + 1));
                Volatile.Write(ref readRound, round); retired?.Cancel.Dispose(); sources = [reader.Source as IObservationInspectionSource ?? throw new InvalidOperationException("当前相机不支持原帧关联。")];
            }
            finally { gate.Release(); }
            try
            {
                var registration = new InspectionRegistration(round.Session, false, f => ReceiveRoundFrame(round.Session, f))
                { CaptureAllowed = device => RoundDecodeAllowed(round.Session, device), CutoverKey = round.Id };
                foreach (var source in sources) await ApplyInspectionAsync(source, registration, t).ConfigureAwait(false);
                await gate.WaitAsync(t).ConfigureAwait(false);
                try
                {
                    authority.ThrowIfCancellationRequested(); Management.Require();
                    if (collectingInspection?.Id != round.Session) throw new InvalidOperationException("调试采集已结束，未开始读取。");
                    round.Worker = OwnedOperation(ct => CompleteReadRoundAsync(round, ct), cameraRoundWorkers, 1, default);
                    round.Detail = "正在读取本轮条码…"; Volatile.Write(ref round.State, "Reading");
                    repository.CompleteCommand(receiptCommand, new("RoundArmed", "SDK缓存与采集循环边界完成，本轮开始读取；不发送请求。", repository.Revision));
                    authority.ThrowIfCancellationRequested(); Management.Require();
                    return RoundProjection();
                }
                finally { gate.Release(); }
            }
            catch
            {
                round.Cancel.Cancel(); Volatile.Write(ref round.State, "Interrupted"); round.Detail = "触发未完成，请核对原轮次后重新触发。"; throw;
            }
        }
        finally { inspectionControl.Release(); }
    });
    async Task<bool> CompleteReadRoundAsync(ReadRound round, CancellationToken token)
    {
        var businessStorage = false;
        using var waiting = CancellationTokenSource.CreateLinkedTokenSource(token, round.Authority, round.Cancel.Token);
        waiting.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            var frame = await round.Capture!.Task.WaitAsync(waiting.Token).ConfigureAwait(false);
            await InspectionStore.FlushAsync(waiting.Token).ConfigureAwait(false);
            var metadata = InspectionStore.Find(frame.ObservationId!) ?? throw new InvalidDataException("本轮原帧未持久留存，不能填充可发送请求。");
            await gate.WaitAsync(waiting.Token).ConfigureAwait(false);
            try
            {
                waiting.Token.ThrowIfCancellationRequested(); Management.Require();
                if (readRound != round || collectingInspection?.Id != round.Session ||
                    Wire.Hash(Wire.Encode(Effective)) != Wire.Hash(Wire.Encode(round.Configuration))) throw new InvalidOperationException("本轮配置或调试会话已变化，不能准备请求。");
                round.Frame = metadata; round.State = "Locked"; round.Detail = "本轮读码完成；照片和条码保持，等待手动发送或再次扫码。";
                businessStorage = true;
                repository!.Commit(null, "CameraReadRoundLocked", Wire.Encode(new { Round = round.Id, Frame = metadata, round.Barcode, Actor = ManagementEvidence }));
                if (frame.Acquisition is not { } acquisition || acquisition.DeviceId != round.Device || acquisition.Epoch != round.Epoch ||
                    acquisition.SourceEventId != frame.SourceEventId || acquisition.ObservedAt != frame.CapturedAt || string.IsNullOrWhiteSpace(acquisition.AdmissionId) ||
                    metadata.DeviceId != frame.DeviceId || metadata.Epoch != frame.Epoch || metadata.FrameNumber != frame.FrameNumber || metadata.CapturedAt != frame.CapturedAt ||
                    metadata.SourceEventId != frame.SourceEventId || metadata.SessionId != round.Session)
                { round.Detail += " 原帧采集身份缺失或不一致，拒绝准备请求；不生成替代采集身份。"; return true; }
                if (Effective.Wcs is null || ports?.Wcs is not IWcsRequestPreparer || ParcelConnectionSetup.IsDeferred(Effective, Effective.Wcs.InstanceId) || Effective.Images is not null || Effective.Weight.Source == WeightSource.Measured)
                { round.Detail += " 当前RCS或必需图像/测量条件不支持独立测试请求，请完成配置后重新触发。"; return true; }
                if (!CapacityAvailable || factory.AdmissionFailure(Effective) is { } || !RestoreConfirmed)
                { round.Detail += " 原业务资格或容量未满足，请保留原责任。"; return true; }
                var op = new ParcelOperation(Wire.Id(), "debug." + round.Id, round.Barcode!, Wire.Hash("manual-debug:" + round.Id), frame.CapturedAt,
                    Wire.Hash(Wire.Encode(Effective)), Wire.Decode<ParcelConfiguration>(Wire.Encode(Effective)), new(ports!.ImplementationHashes), Effective.Weight,
                    WeightRules.Configured(Effective.Weight), ParcelState.Prepared, null, null, null, "独立手动调试请求；未发送",
                    Operator: currentOperator, Acquisition: acquisition, PositionId: positions.Values.Single(p => p.ReaderDeviceId == round.Device).Id, ReaderBinding: ReaderBinding(positions.Values.Single(p => p.ReaderDeviceId == round.Device).Id))
                { DebugRequest = new(round.Session, round.Id, metadata.Id, round.Device, frame.Epoch, frame.SourceEventId!, metadata.OriginalSha256, ManagementEvidence!.AuditActor) };
                Save(op, "CameraDebugIntentCreated", "独立调试请求；仅明确发送命令可执行，正常供包继续。"); round.Operation = op.Id;
                op = ((IParcelExecution)this).PrepareWcs(op);
                Save(op, "CameraDebugRequestPrepared", "冻结本轮原请求，仅供查看，未执行外呼。");
                if (op.Wcs is null) round.Detail += " 请求准备失败，请查看原意图与原因。";
            }
            finally { gate.Release(); }
            return true;
        }
        catch (Exception error)
        {
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (readRound == round)
                {
                    round.State = error is OperationCanceledException ? "Stopped" : "Faulted";
                    round.Detail = error is OperationCanceledException
                        ? "本轮读取已结束，未发送请求。请放好包裹后点击“再次扫码”。"
                        : "本轮扫码未完成，未发送请求。请点击“再次扫码”；仍失败时可停止试扫并导出日志。";
                    Diagnostic("相机调试", "本轮扫码已结束", error is OperationCanceledException ? "信息" : "警告", Wire.Encode(new
                    {
                        Round = round.Id, round.Session, round.Device, round.Epoch, round.State,
                        Phase = businessStorage ? "PrepareBusinessIntent" : "CaptureDiagnosticFrame",
                        Reason = error is OperationCanceledException ? round.Cancel.IsCancellationRequested ? "Stopped" : round.Authority.IsCancellationRequested ? "AuthorizationEnded" : token.IsCancellationRequested ? "RuntimeStopped" : "ReadTimeout" : "ReadFailed",
                        DiagnosticStoreFault = inspectionStore?.Fault, DiagnosticStoreError = inspectionStore?.LastFailure?.ToString(), Dropped = inspectionStore?.Dropped,
                        ConsecutiveNoCodeFrames = Volatile.Read(ref round.ConsecutiveNoCodeFrames),
                        Error = error.ToString(), RequestSent = false
                    }));
                    // Diagnostic custody is separate from business writes. A failed
                    // photo or full sample queue must not fence the entire runtime.
                    if (businessStorage && IsStorageError(error)) StorageFailed(error, "CameraReadRound");
                }
            }
            finally { gate.Release(); }
            return false;
        }
        finally { round.Capture = null; }
    }
    bool CanCloseUnsentDebugIntent(ParcelOperation op) => IsDebugRequest(op) && !MainTerminated(op) &&
        new[] { op.Wcs, op.Plc, op.Capture, op.Upload }.All(s => s is null or { State: StageState.Prepared, Attempts: 0 }) &&
        !activeStages.Any(k => k.StartsWith(op.Id + ":", StringComparison.Ordinal)) &&
        !repository!.PendingReceipts().Any(x => x.Operation == op.Id);
    void CloseUnsentRound(string reason)
    {
        if (readRound is not { } round) return;
        if (round.State == "Sending" || round.Worker is { IsCompleted: false }) throw new InvalidOperationException("当前轮次尚未结束，请等待实际处理完成。");
        round.Cancel.Cancel();
        if (round.Operation is { } id && operations.TryGetValue(id, out var op) && CanCloseUnsentDebugIntent(op))
            Save(op with { State = ParcelState.Abandoned, Wcs = CancelUnsent(op.Wcs), Reason = reason }, "CameraDebugIntentCancelled", reason);
    }
    async Task EndReadRoundAsync(CancellationToken token, string session)
    {
        Task? worker;
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (readRound?.State == "Sending") throw new InvalidOperationException("本轮原请求正在发送，请等待实际返回后再结束本轮。");
            readRound?.Cancel.Cancel(); worker = readRound?.Worker;
        }
        finally { gate.Release(); }
        if (worker is not null) await worker.WaitAsync(token).ConfigureAwait(false);
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            CloseUnsentRound("明确结束调试，取消从未发送的本轮预备请求；已发原责任保留。");
            // Also settle never-attempted test intents recovered from a previous
            // process. Sent/Unknown originals remain under their original guards.
            foreach (var op in operations.Values.Where(o => o.DebugRequest?.SessionId == session && !MainTerminated(o)).ToArray())
                if ((op.Wcs is null || op.Wcs is { State: StageState.Prepared, Attempts: 0 }) && !activeStages.Any(k => k.StartsWith(op.Id + ":", StringComparison.Ordinal)))
                    Save(op with { State = ParcelState.Abandoned, Wcs = CancelUnsent(op.Wcs), Reason = "明确停止本轮，取消未发送测试意图。" }, "CameraDebugIntentCancelled", "明确停止或结束本轮，只取消从未尝试的测试意图，已发送请求责任保持。");
            if (readRound is { } round) round.State = "Closed";
        }
        finally { gate.Release(); }
    }
    CameraReadRound RoundProjection()
    {
        var round = readRound;
        if (round is null) return new(null, collectingInspection?.Id, null, "Idle", "点击触发扫码开始一轮读取；不会自动发送请求。", null, null, null, null, null, null, null, null, false, repository?.Revision ?? 0);
        var op = round.Operation is { } id ? operations.GetValueOrDefault(id) ?? repository!.ReadOperation(id) : null;
        var request = op?.Wcs?.Request;
        var response = op?.Wcs?.Replies.LastOrDefault()?.HttpResponse;
        var value = new CameraReadRound(round.Id, round.Session, round.Device, round.State, round.Detail, round.Frame, round.Barcode, round.Operation,
            request?.IntegrationRequest?.RequestId.ToString() ?? request?.Id, request?.IntegrationRequest?.Body, request is null ? null : RoundRequestHash(request),
            request?.Binding.Target, response, op is not null && round.State == "Locked" && DebugRequestReady(op), repository!.Revision)
        {
            ManualRequest = round.Manual,
            ConsecutiveNoCodeFrames = Volatile.Read(ref round.ConsecutiveNoCodeFrames),
            SendAttempts = op?.Wcs?.Attempts ?? 0,
            SendBlockedReason = op?.Wcs is { State: StageState.Prepared, Attempts: 0 } && !DebugRequestReady(op) ? DebugRequestBlockingReason(op) : null
        };
        return Wire.Decode<CameraReadRound>(Wire.Encode(value));
    }
    static string RoundRequestHash(FrozenRequest request) => Wire.Hash(Wire.Encode(request));
    public Task<CameraReadRound> ReadCameraReadRoundAsync(CancellationToken token) => InspectionRead(async t =>
    {
        await gate.WaitAsync(t).ConfigureAwait(false); try { return RoundProjection(); } finally { gate.Release(); }
    }, token);
    bool DebugRequestReady(ParcelOperation op)
    {
        if (op.OperatorDebugRequest is not null) return OperatorDebugRequestReady(op);
        if (op.DebugRequest is not { } evidence || readRound is not { } round || round.Operation != op.Id || round.Id != evidence.RoundId ||
            collectingInspection?.Id != evidence.SessionId || !singleReadInspection ||
            round.Authority.IsCancellationRequested || round.Cancel.IsCancellationRequested || !MaintenanceAuthorized || !SessionValid ||
            op.RecoveryRequired || op.Wcs is not { State: StageState.Prepared, Attempts: 0, Request.IntegrationRequest: not null } || round.State is not ("Locked" or "Sending")) return false;
        var now = DateTimeOffset.UtcNow;
        var collection = collectingInspection!;
        if (collection.Authority.IsCancellationRequested || collection.Expires <= now || System.Diagnostics.Stopwatch.GetElapsedTime(collection.StartedTicks) >= collection.Lifetime) return false;
        return Volatile.Read(ref accepting) == 1 && failure is null && !stopping.IsCancellationRequested && RestoreConfirmed &&
            configuration?.Current.Confirmation?.Required != true && configuration!.Current.Pending is null && !configurationSaving && !backupInProgress &&
            !ParcelConnectionSetup.BlocksWork(Effective) && factory.AdmissionFailure(Effective) is null && !HasIdentityConflict(op.PositionId!) &&
            ManualPositionMaintenanceClear(op.PositionId!) && !interruptedPositions.ContainsKey(op.PositionId!) && overload == 0 &&
            Wire.Hash(Wire.Encode(Effective)) == op.ConfigurationHash && OriginalRoundFrameMatches(op, evidence, round) &&
            AdmissionCapabilitiesReady(op.PositionId) && WeightReady(op) &&
            (Effective.Login is null || currentOperator is not null && currentOperator.Subject == op.Operator?.Subject) &&
            !repository!.PendingReceipts().Any(x => x.Operation == op.Id) &&
            !operations.Values.Any(x => x.Id != op.Id && x.PositionId == op.PositionId && !MainTerminated(x));
    }
    static bool OriginalRoundFrameMatches(ParcelOperation op, CameraDebugRequestEvidence evidence, ReadRound round) =>
        op.Acquisition is { } acquisition && round.Frame is { } frame && op.OperatorInput is null &&
        round.Device == evidence.DeviceId && round.Epoch == evidence.Epoch &&
        acquisition.DeviceId == evidence.DeviceId && acquisition.Epoch == evidence.Epoch && acquisition.SourceEventId == evidence.SourceEventId &&
        frame.Id == evidence.FrameId && frame.DeviceId == evidence.DeviceId && frame.Epoch == evidence.Epoch &&
        frame.SourceEventId == evidence.SourceEventId && frame.OriginalSha256 == evidence.OriginalSha256;
    string DebugRequestBlockingReason(ParcelOperation op)
    {
        if (op.Wcs is not { State: StageState.Prepared, Attempts: 0 }) return "原请求已经尝试或结束，只能核对原结果，不能重发。";
        if (!MaintenanceAuthorized || !SessionValid) return "管理模式或操作员会话已失效，请重新授权后核对原请求。";
        if (op.OperatorDebugRequest is not null)
        {
            var reasons = ManualInputReasons(op.PositionId!, exceptOperation: op.Id, debugRequest: true, requireAdmissionCapacity: false);
            return reasons.Length != 0 ? string.Join("；", reasons) : "人工请求的轮次或配置已变化，请核对原请求。";
        }
        if (readRound is not { } round || op.DebugRequest is not { } evidence || round.Id != evidence.RoundId || round.Operation != op.Id)
            return "当前显示的请求已不属于本轮，请核对当前轮次。";
        if (round.Authority.IsCancellationRequested || round.Cancel.IsCancellationRequested || collectingInspection is not { } collection ||
            collection.Id != evidence.SessionId || collection.Expires <= DateTimeOffset.UtcNow ||
            System.Diagnostics.Stopwatch.GetElapsedTime(collection.StartedTicks) >= collection.Lifetime)
            return "本轮调试会话已结束或到期，原请求未发送。";
        if (!OriginalRoundFrameMatches(op, evidence, round)) return "冻结请求与原扫码证据不一致，未发送。";
        if (Wire.Hash(Wire.Encode(Effective)) != op.ConfigurationHash || configuration!.Current.Pending is not null)
            return "生效配置已变化或存在待生效配置，请核对原请求。";
        if (!AdmissionCapabilitiesReady(op.PositionId)) return "RCS或必需业务能力当前不可用，请核对服务连接。";
        if (interruptedPositions.ContainsKey(op.PositionId!)) return "本位运行数据无法自动恢复，请联系维护人员；本次调试请求尚未发送。";
        if (backupInProgress || configurationSaving || !ManualPositionMaintenanceClear(op.PositionId!)) return "正在备份、保存设置或维护设备，完成后可发送本轮原请求。";
        if (stopping.IsCancellationRequested || Volatile.Read(ref accepting) != 1 || failure is not null) return "软件尚未就绪或正在停止，本次调试请求尚未发送。";
        if (operations.Values.Any(x => x.Id != op.Id && x.PositionId == op.PositionId && !MainTerminated(x)))
            return "本位还有未结束的原作业，请先核对原责任。";
        return "当前运行、维护或原请求守卫尚未满足，请核对本位诊断。";
    }
    public Task<CameraReadRound> SendCameraRoundAsync(SendCameraRound command, CancellationToken token) => OwnedAdministration(async owned =>
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(owned, token); var t = linked.Token;
        if (!Guid.TryParseExact(command.CommandId, "N", out _) || !Guid.TryParseExact(command.RoundId, "N", out _) || command.RequestHash?.Length != 64) throw new ArgumentException("发送命令身份无效。");
        var authority = Management.Require(); var receipt = new BusinessCommand(command.CommandId, 0, "SendCameraRound", command.RoundId, Wire.Encode(command));
        await gate.WaitAsync(t).ConfigureAwait(false);
        try
        {
            authority.ThrowIfCancellationRequested(); Management.Require();
            if (repository is null || stopped) throw new InvalidOperationException("Application未运行或已停止。");
            if (repository.PriorCommand(receipt) is { } prior)
            { if (prior.Status == "Rejected") throw new InvalidOperationException(prior.Reason); return RoundProjection(); }
            if (command.RuntimeId != runtimeId || readRound is not { } currentRound || currentRound.Id != command.RoundId || currentRound.Operation is not { } id ||
                currentRound.State != "Locked" || !operations.TryGetValue(id, out var op) || op.Wcs is null || RoundRequestHash(op.Wcs.Request) != command.RequestHash)
                throw new InvalidOperationException("本轮请求已变化、已尝试或守卫关闭；不重新准备、不重发，请核对原作业。");
            if (op.Wcs is not { State: StageState.Prepared, Attempts: 0 }) throw new InvalidOperationException(DebugRequestBlockingReason(op));
            if (!DebugRequestReady(op))
            {
                WriteRuntimeLog("RCS请求", "调试请求未发送", new
                {
                    command.CommandId, command.RoundId, OperationId = op.Id, op.PositionId, op.ParcelCode,
                    Request = op.Wcs.Request, SendAttempts = op.Wcs.Attempts, Reason = DebugRequestBlockingReason(op)
                });
                return RoundProjection();
            }
            t.ThrowIfCancellationRequested(); Management.Require();
            repository.Commit(null, "CameraDebugSendAdmitted", Wire.Encode(new { Command = command, Operation = op.Id, Actor = ManagementEvidence }), command: receipt,
                commandResult: new("Pending", "手动测试发送已接纳；只核对原结果，禁止重发。", repository.Revision + 1));
            var round = currentRound; round.State = "Sending"; explicitDebugSends.Add(op.Id);
            try { await ExecuteStageAsync(op, ParcelStage.Wcs, false).ConfigureAwait(false); }
            finally { explicitDebugSends.Remove(op.Id); round.State = "Locked"; }
            if (round.Manual) round.Detail = "人工请求发送已返回；请核对原响应，不自动重发。";
            repository.CompleteCommand(receipt, new("DebugSendReturned", "原发送调用已返回，结果以原阶段及回执为准，不等于物理完成。", repository.Revision));
            authority.ThrowIfCancellationRequested(); Management.Require();
            return RoundProjection();
        }
        catch (Exception error) when (IsStorageError(error)) { StorageFailed(error, "CameraDebugSend"); throw; }
        finally { gate.Release(); }
    });
}

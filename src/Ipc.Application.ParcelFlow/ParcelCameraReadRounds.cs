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
        var accepted = InspectionStore.Offer(session, frame);
        if (!accepted) { round.Capture?.TrySetException(new IOException("原帧留存未接纳，本轮停止；隔离保持。")); return false; }
        var observation = new Observation(frame.ObservationId ?? Wire.Id(), frame.DeviceId, frame.Epoch, frame.SourceEventId ?? "no-read", 1,
            "debug." + round.Id, "Read", frame.Symbols.Select(s => s.Text).ToArray(), null, frame.CapturedAt);
        var selection = definition.SelectBarcode(round.Configuration, observation);
        if (selection.SelectedCode is null || selection.Matches != 1)
        { Volatile.Write(ref round.Detail, frame.Symbols.Length == 0 ? "正在等待条码…" : "未取得唯一有效条码，继续等待；未生成请求。"); return true; }
        lock (round.Sync)
        {
            if (!RoundDecodeAllowed(session, frame.DeviceId)) return true;
            round.Barcode = selection.SelectedCode;
            Volatile.Write(ref round.State, "Sealing"); // Closes decoding before publishing the winner.
            round.Capture?.TrySetResult(frame);
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
                    inspectionFence?.Id != command.SessionId || collectingInspection?.Id != command.SessionId ||
                    !singleReadInspection || !SessionValid || configuration.Current.Pending is not null || backupInProgress || configurationSaving || maintenanceOperations.Count != 0)
                    throw new InvalidOperationException("需要当前生效配置、有效操作员和已生效的按轮实机隔离；请先核对原会话。");
                if (readRound?.Id != command.ExpectedRoundId || readRound?.State is "Preparing" or "Reading" or "Sealing" or "Sending")
                    throw new InvalidOperationException("轮次已变化或正在处理，请刷新并等待当前轮次结束。");
                if (!readers.TryGetValue(command.DeviceId, out var reader) || reader.Health?.IsValid(DateTimeOffset.UtcNow) != true)
                    throw new InvalidOperationException("所选相机没有有效连接确认，不能触发扫码。");
                authority.ThrowIfCancellationRequested(); Management.Require();
                var scope = collectingInspection!;
                if (scope.Authority.IsCancellationRequested || scope.Expires <= DateTimeOffset.UtcNow || System.Diagnostics.Stopwatch.GetElapsedTime(scope.StartedTicks) >= scope.Lifetime)
                    throw new InvalidOperationException("原隔离采集期限已结束，请先清场并结束原会话。");
                CloseUnsentRound("明确开始下一轮，旧预备请求未发送并已取消；原结果保留。");
                var retired = readRound;
                round = new(command.CommandId, command.SessionId, command.DeviceId, reader.Epoch,
                    Wire.Decode<ParcelConfiguration>(Wire.Encode(Effective)), authority);
                repository.Commit(null, "CameraReadRoundPreparing", Wire.Encode(new { Command = command, Actor = ManagementEvidence }), command: receiptCommand,
                    commandResult: new("Pending", "已登记轮次，SDK边界尚待确认；不自动重开。", repository.Revision + 1));
                Volatile.Write(ref readRound, round); retired?.Cancel.Dispose(); sources = InspectionSources();
            }
            finally { gate.Release(); }
            try
            {
                var registration = new InspectionRegistration(round.Session, true, f => ReceiveRoundFrame(round.Session, f))
                    { DecodeAllowed = device => RoundDecodeAllowed(round.Session, device), CutoverKey = round.Id };
                foreach (var source in sources) await source.SetInspectionAsync(registration, t).ConfigureAwait(false);
                await gate.WaitAsync(t).ConfigureAwait(false);
                try
                {
                    authority.ThrowIfCancellationRequested(); Management.Require();
                    if (collectingInspection?.Id != round.Session || inspectionFence?.Id != round.Session) throw new InvalidOperationException("隔离采集已结束，未开始读取。");
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
                round.Cancel.Cancel(); Volatile.Write(ref round.State, "Interrupted"); round.Detail = "触发未完成，隔离保持；请核对原轮次，不自动开始。"; throw;
            }
        }
        finally { inspectionControl.Release(); }
    });
    async Task<bool> CompleteReadRoundAsync(ReadRound round, CancellationToken token)
    {
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
                if (readRound != round || inspectionFence?.Id != round.Session || collectingInspection?.Id != round.Session ||
                    Wire.Hash(Wire.Encode(Effective)) != Wire.Hash(Wire.Encode(round.Configuration))) throw new InvalidOperationException("本轮配置或隔离已变化，不能准备请求。");
                round.Frame = metadata; round.State = "Locked"; round.Detail = "本轮读码完成，已自动暂停；照片和条码保持，等待手动发送或再次扫码。";
                repository!.Commit(null, "CameraReadRoundLocked", Wire.Encode(new { Round = round.Id, Frame = metadata, round.Barcode, Actor = ManagementEvidence }));
                if (Effective.Wcs is null || ports?.Wcs is not IWcsRequestPreparer || ParcelConnectionSetup.IsDeferred(Effective, Effective.Wcs.InstanceId) || Effective.Images is not null || Effective.Weight.Source == WeightSource.Measured)
                { round.Detail += " 当前RCS或必需图像/测量条件不支持独立测试请求，请结束隔离后完成配置。"; return true; }
                if (!CapacityAvailable || factory.AdmissionFailure(Effective) is { } || !RestoreConfirmed)
                { round.Detail += " 原业务资格或容量未满足，请保留原责任。"; return true; }
                var op = new ParcelOperation(Wire.Id(), "debug." + round.Id, round.Barcode!, Wire.Hash("manual-debug:" + round.Id), frame.CapturedAt,
                    Wire.Hash(Wire.Encode(Effective)), Wire.Decode<ParcelConfiguration>(Wire.Encode(Effective)), new(ports!.ImplementationHashes), Effective.Weight,
                    WeightRules.Configured(Effective.Weight), ParcelState.Prepared, null, null, null, "隔离中的独立手动测试意图；未发送",
                    Operator: currentOperator, PositionId: positions.Values.Single(p => p.ReaderDeviceId == round.Device).Id, ReaderBinding: ReaderBinding(positions.Values.Single(p => p.ReaderDeviceId == round.Device).Id))
                    { DebugRequest = new(round.Session, round.Id, metadata.Id, round.Device, frame.Epoch, frame.SourceEventId!, metadata.OriginalSha256, ManagementEvidence!.AuditActor) };
                Save(op, "CameraDebugIntentCreated", "独立测试意图，隔离观察未准入/未回放；仅明确发送命令可执行。"); round.Operation = op.Id;
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
                    round.Detail = error is OperationCanceledException ? "本轮等待已结束或授权失效，未自动重开；可重新触发，隔离保持。" : "本轮处理失败，隔离保持：" + error.Message;
                    if (IsStorageError(error)) StorageFailed(error, "CameraReadRound");
                }
            }
            finally { gate.Release(); }
            return false;
        }
        finally { round.Capture = null; }
    }
    void CloseUnsentRound(string reason)
    {
        if (readRound is not { } round) return;
        if (round.State == "Sending" || round.Worker is { IsCompleted: false }) throw new InvalidOperationException("当前轮次尚未结束，请等待实际处理完成。");
        round.Cancel.Cancel();
        if (round.Operation is { } id && operations.TryGetValue(id, out var op) && op.Wcs?.Attempts is null or 0 &&
            op.Wcs?.State is null or StageState.Prepared && !MainTerminated(op))
            Save(op with { State = ParcelState.Abandoned, Wcs = CancelUnsent(op.Wcs), Reason = reason }, "CameraDebugIntentCancelled", reason);
    }
    async Task EndReadRoundAsync(CancellationToken token, string session)
    {
        Task? worker;
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (readRound?.State == "Sending") throw new InvalidOperationException("本轮原请求正在发送，请等待实际返回后再确认清场。");
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
                    Save(op with { State = ParcelState.Abandoned, Wcs = CancelUnsent(op.Wcs), Reason = "明确停止本轮，取消未发送测试意图。" }, "CameraDebugIntentCancelled", "明确停止或结束本轮，只取消从未尝试的测试意图，原隔离与已发责任保持。");
            if (readRound is { } round) round.State = "Closed";
        }
        finally { gate.Release(); }
    }
    CameraReadRound RoundProjection()
    {
        var round = readRound;
        if (round is null) return new(null, inspectionFence?.Id, null, "Idle", "点击触发扫码开始一轮读取；不会自动发送请求。", null, null, null, null, null, null, null, null, false, repository?.Revision ?? 0);
        var op = round.Operation is { } id ? operations.GetValueOrDefault(id) ?? repository!.ReadOperation(id) : null;
        var request = op?.Wcs?.Request;
        var response = op?.Wcs is { } stage ? "状态：" + stage.State + " · 发送尝试：" + stage.Attempts + "\n" +
            ClipRound(Wire.Encode(stage.Replies.LastOrDefault()), 65536) + (stage.Failure is null ? "" : "\n" + stage.Failure) : null;
        var value = new CameraReadRound(round.Id, round.Session, round.Device, round.State, round.Detail, round.Frame, round.Barcode, round.Operation,
            request?.IntegrationRequest?.RequestId.ToString() ?? request?.Id, request?.IntegrationRequest?.Body, request is null ? null : RoundRequestHash(request),
            request?.Binding.Target, response, op is not null && round.State == "Locked" && DebugRequestReady(op), repository!.Revision);
        return Wire.Decode<CameraReadRound>(Wire.Encode(value));
    }
    static string ClipRound(string text, int maximum) => text.Length <= maximum ? text : text[..maximum] + "\n[显示已截断，原回执仍保留]";
    static string RoundRequestHash(FrozenRequest request) => Wire.Hash(Wire.Encode(request));
    public Task<CameraReadRound> ReadCameraReadRoundAsync(CancellationToken token) => InspectionRead(async t =>
    {
        await gate.WaitAsync(t).ConfigureAwait(false); try { return RoundProjection(); } finally { gate.Release(); }
    }, token);
    bool DebugRequestReady(ParcelOperation op)
    {
        if (op.DebugRequest is not { } evidence || readRound is not { } round || round.Operation != op.Id || round.Id != evidence.RoundId ||
            inspectionFence?.Id != evidence.SessionId || collectingInspection?.Id != evidence.SessionId || !singleReadInspection ||
            round.Authority.IsCancellationRequested || round.Cancel.IsCancellationRequested || !MaintenanceAuthorized || !SessionValid ||
            op.RecoveryRequired || op.Wcs is not { State: StageState.Prepared, Attempts: 0, Request.IntegrationRequest: not null } || round.State is not ("Locked" or "Sending")) return false;
        var now = DateTimeOffset.UtcNow;
        var collection = collectingInspection!;
        if (collection.Authority.IsCancellationRequested || collection.Expires <= now || System.Diagnostics.Stopwatch.GetElapsedTime(collection.StartedTicks) >= collection.Lifetime) return false;
        return Volatile.Read(ref accepting) == 1 && failure is null && !stopping.IsCancellationRequested && RestoreConfirmed &&
            configuration?.Current.Confirmation?.Required != true && configuration!.Current.Pending is null && !configurationSaving && !backupInProgress &&
            !ParcelConnectionSetup.BlocksWork(Effective) && factory.AdmissionFailure(Effective) is null && !HasIdentityConflict(op.PositionId!) &&
            PositionMaintenanceClear(op.PositionId!) && !interruptedPositions.ContainsKey(op.PositionId!) && overload == 0 &&
            Wire.Hash(Wire.Encode(Effective)) == op.ConfigurationHash && readers[evidence.DeviceId].Epoch == evidence.Epoch && readers[evidence.DeviceId].Health?.IsValid(now) == true &&
            AdmissionCapabilitiesReady(op.PositionId) && WeightReady(op) &&
            (Effective.Login is null || currentOperator is not null && currentOperator.Subject == op.Operator?.Subject) &&
            !repository!.PendingReceipts().Any(x => x.Operation == op.Id) &&
            !operations.Values.Any(x => x.Id != op.Id && x.PositionId == op.PositionId && !MainTerminated(x));
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
                currentRound.State != "Locked" || !operations.TryGetValue(id, out var op) || !DebugRequestReady(op) || RoundRequestHash(op.Wcs!.Request) != command.RequestHash)
                throw new InvalidOperationException("本轮请求已变化、已尝试或守卫关闭；不重新准备、不重发，请核对原作业。");
            t.ThrowIfCancellationRequested(); Management.Require();
            repository.Commit(null, "CameraDebugSendAdmitted", Wire.Encode(new { Command = command, Operation = op.Id, Actor = ManagementEvidence }), command: receipt,
                commandResult: new("Pending", "手动测试发送已接纳；只核对原结果，禁止重发。", repository.Revision + 1));
            var round = currentRound; round.State = "Sending"; explicitDebugSends.Add(op.Id);
            try { await ExecuteStageAsync(op, ParcelStage.Wcs, false).ConfigureAwait(false); }
            finally { explicitDebugSends.Remove(op.Id); round.State = "Locked"; }
            repository.CompleteCommand(receipt, new("DebugSendReturned", "原发送调用已返回，结果以原阶段及回执为准，不等于物理完成。", repository.Revision));
            authority.ThrowIfCancellationRequested(); Management.Require();
            return RoundProjection();
        }
        catch (Exception error) when (IsStorageError(error)) { StorageFailed(error, "CameraDebugSend"); throw; }
        finally { gate.Release(); }
    });
}

using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime
{
    // Uses the existing frozen request/one-shot send owner. Preparing operator
    // input never touches the SDK, invents a frame or sends an HTTP request.
    public Task<CameraReadRound> PrepareManualRcsRequestAsync(PrepareManualRcsRequest command, CancellationToken token) => OwnedAdministration(async owned =>
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(owned, token);
        var t = linked.Token;
        if (!Guid.TryParseExact(command.CommandId, "N", out _) || command.PositionId is not ("left" or "right") ||
            string.IsNullOrWhiteSpace(command.Barcode) || command.Barcode.Length > 4096 || command.Barcode.Any(char.IsControl))
            throw new ArgumentException("需要左/右位置及完整单条码；保留前导零，不接受控制字符。");
        var authority = Management.Require();
        var receipt = new BusinessCommand(command.CommandId, command.ConfigurationRevision, "PrepareManualRcsRequest", command.PositionId, Wire.Encode(command));
        await inspectionControl.WaitAsync(t).ConfigureAwait(false);
        try
        {
            await gate.WaitAsync(t).ConfigureAwait(false);
            try
            {
                t.ThrowIfCancellationRequested(); authority.ThrowIfCancellationRequested(); Management.Require();
                if (repository is null || stopped || Id != "sportmaster") throw new InvalidOperationException("Application未运行或已停止。");
                if (repository.PriorCommand(receipt) is { } priorReceipt)
                {
                    if (priorReceipt.Status == "Rejected") throw new InvalidOperationException(priorReceipt.Reason);
                    return RoundProjection();
                }
                if (command.RuntimeId != runtimeId || command.ConfigurationRevision != configuration!.Current.Revision || configuration.Current.Pending is not null)
                    throw new CameraReadRoundConflictException("运行或生效配置已变化，请重新核对请求。");
                if (readRound?.Id != command.ExpectedRoundId || readRound?.State is "Preparing" or "Reading" or "Sealing" or "Sending" || readRound?.Worker is { IsCompleted: false })
                    throw new CameraReadRoundConflictException("当前轮次已变化或仍在读取/发送；核对原轮次后再准备请求。");
                var replaceable = readRound?.Operation is { } priorId && operations.TryGetValue(priorId, out var prior) &&
                    prior.Wcs is { State: StageState.Prepared, Attempts: 0 } ? priorId : null;
                var reasons = ManualInputReasons(command.PositionId, exceptOperation: replaceable, debugRequest: true);
                if (reasons.Length != 0) throw new CameraReadRoundConflictException(string.Join("；", reasons));
                if (ports?.Wcs is not IWcsRequestPreparer) throw new CameraReadRoundConflictException("当前RCS不支持冻结请求。");
                var at = DateTimeOffset.UtcNow;
                var evidence = new OperatorInputEvidence(command.CommandId, "Keyboard", ManagementEvidence!.AuditActor, currentOperator?.SessionId, at);
                var observation = new Observation(command.CommandId, positions[command.PositionId].ReaderDeviceId, "operator." + runtimeId.ToString("N"),
                    "operator." + command.CommandId, 1, "manual." + command.CommandId, "Read", [command.Barcode], null, at) { OperatorInput = evidence };
                var selection = definition.SelectBarcode(Effective, observation);
                if (selection.SelectedCode is not { } code || selection.Matches != 1) throw new CameraReadRoundConflictException("人工条码未通过当前筛选规则；请求未准备、未发送。");
                repository.AdmitCommand(receipt);
                CloseUnsentRound("明确准备新的手工请求，取消从未发送的旧预备请求；已发原责任保持。");
                var retired = readRound;
                var round = new ReadRound(command.CommandId, inspectionFence?.Id ?? "", observation.DeviceId, "",
                    Wire.Decode<ParcelConfiguration>(Wire.Encode(Effective)), authority)
                { Manual = true, Capture = null, Barcode = code, State = "Locked", Detail = "人工请求已准备；点击发送才调用RCS。" };
                var op = new ParcelOperation(Wire.Id(), observation.AdmissionId, code, Wire.Hash("operator-debug:" + round.Id), at,
                    Wire.Hash(Wire.Encode(Effective)), round.Configuration, new(ports.ImplementationHashes), Effective.Weight,
                    WeightRules.Configured(Effective.Weight), ParcelState.Prepared, null, null, null, "独立人工调试意图；未发送",
                    Operator: currentOperator, PositionId: command.PositionId, ReaderBinding: ReaderBinding(command.PositionId), BarcodeSelection: selection)
                { OperatorInput = evidence, OperatorDebugRequest = new(round.Id, ManagementEvidence.AuditActor) };
                Save(op, "OperatorDebugIntentCreated", "明确人工输入；无相机采集身份，不发送请求。");
                round.Operation = op.Id; Volatile.Write(ref readRound, round); retired?.Cancel.Dispose();
                op = ((IParcelExecution)this).PrepareWcs(op);
                Save(op, "OperatorDebugRequestPrepared", "冻结人工请求，未执行外呼。");
                repository.CompleteCommand(receipt, new("RoundPrepared", "手工请求已准备；不自动发送。", repository.Revision + 1));
                return RoundProjection();
            }
            catch (Exception error) when (IsStorageError(error)) { StorageFailed(error, "OperatorDebugPrepare"); throw; }
            finally { gate.Release(); }
        }
        finally { inspectionControl.Release(); }
    });

    bool OperatorDebugRequestReady(ParcelOperation op) => op.OperatorDebugRequest is { } evidence &&
        readRound is { Manual: true } round && round.Id == evidence.RoundId && round.Operation == op.Id &&
        round.State is "Locked" or "Sending" && !round.Authority.IsCancellationRequested && !round.Cancel.IsCancellationRequested &&
        MaintenanceAuthorized && !op.RecoveryRequired && IsManualParcel(op) &&
        op.Wcs is { State: StageState.Prepared, Attempts: 0, Request.IntegrationRequest: not null } &&
        configuration?.Current.Pending is null && Wire.Hash(Wire.Encode(Effective)) == op.ConfigurationHash &&
        ManualInputReasons(op.PositionId!, exceptOperation: op.Id, debugRequest: true).Length == 0 && WeightReady(op) &&
        (Effective.Login is null || currentOperator?.Subject == op.Operator?.Subject) &&
        !repository!.PendingReceipts().Any(x => x.Operation == op.Id);
}

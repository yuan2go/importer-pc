using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime : IParcelWorkstation
{
    readonly HashSet<Task> operatorInputOperations = [];
    readonly HashSet<string> activeOperatorCommands = [];
    public Task<ParcelBarcodeReceipt> SubmitParcelBarcodeAsync(SubmitParcelBarcode request, CancellationToken token) =>
        OwnedOperation(async owned =>
        {
            if (!Guid.TryParseExact(request.CommandId, "N", out _) || request.PositionId is not ("left" or "right") ||
                request.Barcode is null || request.Barcode.Length is < 1 or > 4096 || request.Barcode.Any(char.IsControl) ||
                request.ExpectedObservationId?.Length > 256 || request.ExpectedOperationId?.Length > 256 ||
                !Enum.IsDefined(request.Kind)) throw new ArgumentException("需要有效命令、左/右位置及1～4096字符的完整单条码；保留前导零，不接受控制字符或多条粘贴。");
            var command = new BusinessCommand(request.CommandId, request.ConfigurationRevision, "OperatorBarcode", request.PositionId, Wire.Encode(request));
            await gate.WaitAsync(owned).ConfigureAwait(false);
            try
            {
                if (repository is null || Id != "sportmaster" || !SessionValid)
                    return new("Rejected", "作业输入不可用或操作员会话已失效。", null, null, repository?.Revision ?? 0);
                if (repository.PriorCommand(command) is { } prior)
                {
                    if (prior.Status == "BarcodeResult") return Wire.Decode<ParcelBarcodeReceipt>(prior.Reason);
                    if (prior.Status == "Pending" && !activeOperatorCommands.Contains(request.CommandId))
                    {
                        var originalEpoch = "operator." + request.RuntimeId.ToString("N");
                        var originalHash = Wire.Hash(positions.GetValueOrDefault(request.PositionId)?.ReaderDeviceId + ":" + originalEpoch + ":operator." + request.CommandId);
                        var original = repository.FindIdentities("manual." + request.CommandId, originalHash).SingleOrDefault();
                        if (original?.OperatorInput?.CommandId == request.CommandId && original.AdmissionId == "manual." + request.CommandId)
                        {
                            var recovered = new ParcelBarcodeReceipt("Admitted", "已核对原持久作业；只补齐提交回执，不重新执行原请求。", original.Id, null, repository.Revision + 1);
                            repository.CompleteCommand(command, new("BarcodeResult", Wire.Encode(recovered), recovered.Revision));
                            return recovered;
                        }
                    }
                    return new(prior.Status, prior.Reason, null, null, prior.Revision);
                }
                if (request.RuntimeId != runtimeId || request.ConfigurationRevision != configuration!.Current.Revision)
                    return new("Conflict", "运行或生效配置已改变；核对当前包裹后重新提交。", null, null, repository.Revision);
                if (latestInputs.GetValueOrDefault(request.PositionId)?.Observation.Id != request.ExpectedObservationId ||
                    DisplayedOperation(request.PositionId)?.Id != request.ExpectedOperationId)
                    return new("Conflict", "输入期间本位包裹已变化；核对当前包裹及输入内容后，再明确更新输入基准。", null, null, repository.Revision);
                var blocking = ManualInputReasons(request.PositionId);
                if (blocking.Length != 0)
                    return new("Rejected", string.Join("；", blocking) + " 输入保留，未创建作业或发送请求。", null, null, repository.Revision);
                var position = positions[request.PositionId];
                owned.ThrowIfCancellationRequested();
                var at = DateTimeOffset.UtcNow;
                var evidence = new OperatorInputEvidence(request.CommandId, request.Kind.ToString(),
                    currentOperator?.Subject ?? "local-workstation", currentOperator?.SessionId, at);
                var observation = new Observation(Wire.Id(), position.ReaderDeviceId, "operator." + runtimeId.ToString("N"),
                    "operator." + request.CommandId, 1, "manual." + request.CommandId, "Read", [request.Barcode], null, at)
                    { OperatorInput = evidence };
                // Durable intent comes before any work or external effect. A crash/Pending
                // command is inspectable but is never replayed as a fresh camera event.
                repository.Commit(null, "OperatorBarcodeSubmitted", Wire.Encode(new { Command = request, Evidence = evidence, Observation = observation }),
                    command: command, commandResult: new("Pending", "人工输入已持久接纳；结果尚待核对，禁止以新命令盲重发。", repository.Revision + 1));
                activeOperatorCommands.Add(request.CommandId);
                try { await ProcessTrustedObservationAsync(request.PositionId, observation, currentOperator, at, readers[position.ReaderDeviceId]).ConfigureAwait(false); }
                finally { activeOperatorCommands.Remove(request.CommandId); }
                var admitted = repository.FindIdentities(observation.AdmissionId, Wire.Hash(observation.DeviceId + ":" + observation.Epoch + ":" + observation.SourceEventId)).SingleOrDefault();
                var input = latestInputs.GetValueOrDefault(request.PositionId);
                if (input?.Observation.OperatorInput?.CommandId != request.CommandId) input = null;
                var result = new ParcelBarcodeReceipt(admitted is not null ? "Admitted" : input?.Outcome.ToString() ?? "Pending",
                    admitted is not null ? "人工输入已创建独立作业；RCS结果以该作业为准。" : input?.Reason ?? "未取得本次输入结果，请查看原命令。",
                    admitted?.Id ?? input?.OperationId, observation.Id, repository.Revision + 1);
                repository.CompleteCommand(command, new("BarcodeResult", Wire.Encode(result), result.Revision));
                return result;
            }
            catch (Exception error) when (IsStorageError(error)) { StorageFailed(error, "OperatorBarcode"); throw; }
            finally { gate.Release(); }
        }, operatorInputOperations, 2, token);
}

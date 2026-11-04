using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime
{
    async Task<ParcelOperation> ExecuteStageAsync(ParcelOperation op, ParcelStage kind, bool query, string? missingOccurrence = null, string? resolutionReason = null, BusinessCommand? resolutionCommand = null)
    {
        var key = op.Id + ":" + kind;
        if (!activeStages.Add(key)) return operations[op.Id];
        try
        {
            op = operations[op.Id];
            var stage = GetStage(op, kind)!;
            var attached = kind == ParcelStage.Upload && op.FrozenConfiguration.Images?.Strategy == UploadStrategy.Attached;
            var binding = BindingFor(Effective, kind, op.PositionId);
            if (binding is null || Wire.Encode(binding) != Wire.Encode(stage.Request.Binding) ||
                ports!.ImplementationHashes.GetValueOrDefault(binding.InstanceId) != stage.Request.ImplementationHash)
            { op = StageFailure(op, kind, stage, "OriginalCapabilityUnavailable: preserve original request, package, target and implementation"); Save(op, "OriginalCapabilityBlocked", GetStage(op, kind)!.Failure!); return op; }
            if (query && !factory.Requirements(stage.Request.Binding).Query)
            {
                repository!.Commit(null, "QueryUnsupported", Wire.Encode(new
                {
                    Operation = op.Id,
                    OriginalRequest = stage.Request.Id,
                    Policy = "Provider declares no authoritative query capability; original stage and receipt responsibility remain unchanged"
                }));
                return op;
            }
            var port = PortFor(op, kind);
            if (!PortMaintenanceClear(port)) return op;
            CapabilityHealth health;
            try { health = await ExternalPortAsync(port, () => ProbeHealthAsync(port, stopping.Token)).ConfigureAwait(false); }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            { return operations[op.Id]; } // No delivery occurred, no failed health fact is invented on shutdown.
            capabilityHealth[port.InstanceId] = health;
            op = operations[op.Id];
            if (!PortMaintenanceClear(port)) return op;
            if (!health.Ready || health.ValidUntil <= DateTimeOffset.UtcNow || health.ConfirmedAt > DateTimeOffset.UtcNow)
            { op = StageFailure(op, kind, stage, "Original capability is not healthy; reconnect and reconcile"); Save(op, "CapabilityNotReady", "Original capability is not healthy"); return op; }
            if (!query && ((!attached && !CanDispatch(op) || attached && !CanSendUpload(op)) ||
                !PredecessorsAccepted(op, kind) || (kind is ParcelStage.Wcs or ParcelStage.Plc || kind == ParcelStage.Upload && factory.Requirements(stage.Request.Binding).MeasuredWeight) && !WeightReady(op))) return op;
            if (stopping.IsCancellationRequested) return op;
            var deliveryAuthority = query || IsDebugRequest(op) ? Management.Require() : CancellationToken.None;
            // Adapter dictionaries are mutable; never expose the owned original intent.
            var deliveryRequest = Wire.Decode<FrozenRequest>(Wire.Encode(stage.Request));
            if (!query)
            {
                if (stage.State != StageState.Prepared || stage.Attempts != 0) throw new InvalidOperationException("Only a never-attempted prepared intent can be dispatched");
                stage = stage with { State = StageState.Dispatching, Attempts = 1 };
                op = SetStage(op, kind, stage); Save(op, "Dispatching", "T2: attempt persisted before external call");
            }
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token, deliveryAuthority);
            budget.CancelAfter(definition.RequestTimeoutMs(op, kind));
            var occurrence = repository!.BeginReceipt(op, stage, query ? "Query" : "Submit");
            var receiptStartedAt = ReceiptTimeForVerification.GetUtcNow();
            var receiptStartedTicks = ReceiptTimeForVerification.GetTimestamp();
            if (kind == ParcelStage.Wcs)
                Diagnostic("RCS请求", "开始请求格口", detail: Wire.Encode(new { OperationId = op.Id, op.PositionId, op.ParcelCode, RequestId = stage.Request.Id, ProtocolRequestId = stage.Request.IntegrationRequest?.RequestId, stage.Attempts, query }));
            DebugDiagnostic("外部通信", "原请求发送开始", () => new
            {
                Operation = op.Id,
                op.PositionId,
                Stage = kind,
                Query = query,
                Request = stage.Request.Id,
                stage.Request.PayloadHash,
                stage.Request.ConfigurationHash,
                stage.Request.ImplementationHash,
                Target = stage.Request.Binding.Target,
                Contract = stage.Request.Binding.Contract,
                stage.Attempts,
                TimeoutMs = definition.RequestTimeoutMs(op, kind),
                Payload = DiagnosticFragment(stage.Request.IntegrationRequest?.Body ?? stage.Request.Payload)
            });
            StageReply? reply;
            if (Id == "sportmaster" && kind == ParcelStage.Wcs) RecordRcsPerformance(op, query ? "RcsQueryStarted" : "RcsSubmitStarted");
            try
            {
                reply = await ExternalPortAsync(port, () => PortCallAsync(port, async () => kind switch
                {
                    ParcelStage.Wcs => query ? await ports.Wcs!.QueryAsync(deliveryRequest, budget.Token).ConfigureAwait(false) : await ports.Wcs!.ResolveSlotAsync(deliveryRequest, budget.Token).ConfigureAwait(false),
                    ParcelStage.Plc => query ? await ports.Plc!.QueryAsync(deliveryRequest, budget.Token).ConfigureAwait(false) : await ports.Plc!.SubmitAsync(deliveryRequest, budget.Token).ConfigureAwait(false),
                    ParcelStage.Capture => query ? await ((IImageCamera)port).QueryAsync(deliveryRequest, budget.Token).ConfigureAwait(false) : await ((IImageCamera)port).CaptureAsync(deliveryRequest, budget.Token).ConfigureAwait(false),
                    ParcelStage.Upload => query ? await ports.Uploader!.QueryAsync(deliveryRequest, budget.Token).ConfigureAwait(false) : await ports.Uploader!.UploadAsync(deliveryRequest, ReadImage(op, op.Image!), budget.Token).ConfigureAwait(false),
                    _ => throw new InvalidOperationException("Unregistered stage")
                }, budget.Token)).ConfigureAwait(false);
                op = operations[op.Id];
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException)
            {
                DebugDiagnostic("外部通信", "原请求发送异常详情", () => new
                {
                    Operation = op.Id,
                    op.PositionId,
                    Stage = kind,
                    Query = query,
                    Request = stage.Request.Id,
                    ElapsedMs = ReceiptTimeForVerification.GetElapsedTime(receiptStartedTicks).TotalMilliseconds,
                    DeadlineCancelled = budget.IsCancellationRequested,
                    Stopping = stopping.IsCancellationRequested,
                    Exception = DiagnosticFragment(ex.ToString())
                });
                Diagnostic("通信", query ? "原请求核对异常" : "业务请求异常", "错误", ex.ToString());
                repository.NoReply(occurrence);
                op = operations[op.Id];
                // A failed confirmation query supplies no contrary evidence about an
                // already accepted/rejected command. Keep that fact and its original reply.
                var knownOutcome = stage.State is StageState.Accepted or StageState.Rejected;
                var timedOut = !query && budget.IsCancellationRequested && !stopping.IsCancellationRequested;
                stage = stage with
                {
                    State = knownOutcome ? stage.State : StageState.Unknown,
                    Failure = timedOut ? "TimeoutException: Request deadline expired" : ex.GetType().Name + ": " + ex.Message,
                    LocalFailure = timedOut ? RequestLocalFailure.DeadlineExpired : stage.LocalFailure
                };
                op = SetStage(op, kind, stage) with
                {
                    State = attached || knownOutcome ? op.State : UnknownState(kind),
                    Reason = attached || knownOutcome ? op.Reason : "Delivery/outcome unknown; original request must be reconciled, never resent"
                };
                Save(op, "OutcomeUnknown", stage.Failure!); LogRcsResult(op, kind, receiptStartedTicks, query); return op;
            }
            DebugDiagnostic("外部通信", "原请求响应详情", () => new
            {
                Operation = op.Id,
                op.PositionId,
                Stage = kind,
                Query = query,
                Request = stage.Request.Id,
                ElapsedMs = ReceiptTimeForVerification.GetElapsedTime(receiptStartedTicks).TotalMilliseconds,
                Reply = reply is null ? null : new
                {
                    reply.Id,
                    reply.RequestId,
                    reply.PayloadHash,
                    reply.TransportSucceeded,
                    reply.ProtocolAcknowledged,
                    reply.BusinessAccepted,
                    reply.PhysicalCompleted,
                    reply.Slot,
                    Evidence = DiagnosticFragment(reply.Evidence),
                    Response = DiagnosticResponse(reply.HttpResponse)
                }
            });
            if (reply is null)
            {
                repository.NoReply(occurrence);
                // Query absence does not prove no side effect.
                if (stage.State is not (StageState.Accepted or StageState.Rejected)) stage = stage with { State = StageState.Unknown };
                op = StageFailure(SetStage(op, kind, stage), kind, stage, "Original query has no authoritative result; no resend is permitted");
                Save(op, "QueryInconclusive", GetStage(op, kind)!.Failure!); LogRcsResult(op, kind, receiptStartedTicks, query); return op;
            }
            // If persistence fails here, the Awaiting occurrence remains independently visible.
            // Do not reclassify a returned-but-unrecorded reply as an ordinary transport failure.
            reply = ParcelReceiptChronology.Observe(stage.Request, reply, runtimeId, receiptStartedAt, ReceiptTimeForVerification.GetUtcNow(),
                ReceiptTimeForVerification.GetElapsedTime(receiptStartedTicks), definition.RequestTimeoutMs(op, kind), repository.ReadOriginalReply(stage.Request, reply.Id));
            repository.StoreRaw(occurrence, reply);
            Boundary?.Invoke("AfterRawDurable");
            op = ApplyReply(op, kind, reply);
            // Some adapters retain a partial response on cancellation. Preserve that raw
            // receipt while recording the caller's expired request deadline separately.
            if (!query && reply.BusinessAccepted is null && budget.IsCancellationRequested && !stopping.IsCancellationRequested && GetStage(op, kind) is { State: StageState.Unknown } timedOutStage)
                op = SetStage(op, kind, timedOutStage with { Failure = "TimeoutException: Request deadline expired", LocalFailure = RequestLocalFailure.DeadlineExpired });
            if (missingOccurrence is not null && repository.Paused && MaintenanceAuthorized &&
                GetStage(op, kind) is { State: StageState.Accepted or StageState.Rejected, Failure: null } resolved &&
                resolved.Replies.Any(r => r.Id == reply.Id && r.BusinessAccepted is not null) && repository.MissingAwaitingRaw(missingOccurrence))
            {
                repository.Commit(op, "ReceiptOutcomeKnown", Wire.Encode(new { OriginalOccurrence = missingOccurrence, QueryOccurrence = occurrence, Detail = resolutionReason }),
                    occurrence: occurrence, command: resolutionCommand, reconciledOccurrence: missingOccurrence);
                operations[op.Id] = op;
            }
            else Save(op, "ReceiptApplied", reply.LocalClockEvidence is null ? "T3: original raw reply, stage and downstream intent committed together" :
                Wire.Encode(new { Policy = "T3: original raw reply, stage and downstream intent committed together", ClockAnomaly = "InFlightUtcRollbackEvidence", reply.LocalClockEvidence }), occurrence);
            LogRcsResult(op, kind, receiptStartedTicks, query);
            return op;
        }
        finally { activeStages.Remove(key); }
    }
    void LogRcsResult(ParcelOperation operation, ParcelStage kind, long startedTicks, bool query)
    {
        if (Id != "sportmaster" || kind != ParcelStage.Wcs || operation.Wcs is not { } stage) return;
        RecordRequestLatency(operation, ReceiptTimeForVerification.GetElapsedTime(startedTicks).TotalMilliseconds);
        RecordRcsPerformance(operation, query ? "RcsQueryCompleted" : "RcsSubmitCompleted", ReceiptTimeForVerification.GetElapsedTime(startedTicks).TotalMilliseconds);
        // Ordinary operational summary. Credentials, target URLs and raw bodies stay out.
        Diagnostic("RCS结果", "取格口结果", detail: Wire.Encode(new
        {
            OperationId = operation.Id,
            operation.ParcelCode,
            operation.PositionId,
            RequestId = stage.Request.Id,
            ProtocolRequestId = stage.Request.IntegrationRequest?.RequestId,
            Result = stage.State.ToString(),
            stage.Failure,
            HttpStatus = stage.Replies.LastOrDefault()?.HttpResponse?.StatusCode,
            ResponseCode = stage.Replies.LastOrDefault()?.Destination?.Code,
            ResponseMessage = stage.Replies.LastOrDefault()?.Destination?.Message,
            stage.Attempts,
            operation.Slot,
            ElapsedMs = Math.Round(ReceiptTimeForVerification.GetElapsedTime(startedTicks).TotalMilliseconds, 1),
            NextAction = stage.State is StageState.Unknown or StageState.Conflict
                ? "勿重复上件；核对本件原请求结果" : stage.State == StageState.Rejected
                ? "本件未分配格口；按拒绝原因处理" : "结果已记录；投放以作业页当前有效格口为准"
        }));
    }
    internal TimeProvider ReceiptTimeForVerification { get; set; } = TimeProvider.System;
    ParcelOperation ApplyReply(ParcelOperation op, ParcelStage kind, StageReply reply)
    {
        var wcs = kind == ParcelStage.Wcs;
        var independentUpload = kind == ParcelStage.Upload && op.FrozenConfiguration.Images?.Strategy == UploadStrategy.Attached;
        var stage = GetStage(op, kind)!;
        var prior = stage.Replies.FirstOrDefault(x => x.Id == reply.Id);
        var inconsistent = repository!.OriginalReplyHash(stage.Request.Id, reply.Id) is { } originalHash && originalHash != Wire.Hash(Wire.Encode(reply)) || reply.RequestId != stage.Request.Id || reply.PayloadHash != stage.Request.PayloadHash ||
            !Guid.TryParseExact(reply.Id, "N", out _) || string.IsNullOrWhiteSpace(reply.Evidence) || !ParcelReceiptChronology.Valid(stage.Request, reply, ReceiptTimeForVerification.GetUtcNow()) ||
            reply.BusinessAccepted is not null && (!reply.TransportSucceeded || reply.ProtocolAcknowledged != true) ||
            kind == ParcelStage.Plc && factory.Requirements(stage.Request.Binding) is { Query: false, CompletesOnSubmit: true } && reply.BusinessAccepted == true && reply.PhysicalCompleted != true ||
            reply.PhysicalCompleted == true && (reply.BusinessAccepted != true || !factory.Requirements(stage.Request.Binding).PhysicalConfirmation) ||
            wcs && reply.BusinessAccepted == true && (string.IsNullOrWhiteSpace(reply.Slot) || reply.Slot.Length > 64 || reply.Slot.Any(char.IsControl)) ||
            prior is not null && Wire.Encode(prior) != Wire.Encode(reply) ||
            kind is ParcelStage.Capture or ParcelStage.Upload && reply.BusinessAccepted == true && !ValidImageReply(op, kind, reply.Image);
        inconsistent |= reply.Destination is { } destination && (!wcs || !Enum.IsDefined(destination.Outcome) ||
            (destination.Outcome == WcsDestinationOutcome.Assigned) != (reply.BusinessAccepted == true) ||
            destination.Outcome == WcsDestinationOutcome.UnpackRequired && (reply.BusinessAccepted != false || destination.Unpack is null));
        var terminal = stage.Replies.LastOrDefault(x => x.BusinessAccepted is not null);
        inconsistent |= !Enum.IsDefined(reply.SenderDelivery) || reply.SenderDelivery != SenderDeliveryEvidence.Unknown &&
            (!wcs || stage.Request.Binding.Contract != "SPORTMASTER-RCS-SORTER-v1" || reply.TransportSucceeded ||
                reply.ProtocolAcknowledged is not null || reply.BusinessAccepted is not null || reply.PhysicalCompleted is not null ||
                reply.HttpResponse is not null || reply.Destination is not null || reply.Image is not null ||
                !reply.Evidence.StartsWith("RCS_NOT_DELIVERED:", StringComparison.Ordinal));
        inconsistent |= terminal is not null && reply.BusinessAccepted is not null &&
            (terminal.BusinessAccepted != reply.BusinessAccepted || terminal.Slot != reply.Slot || Wire.Encode(terminal.Image) != Wire.Encode(reply.Image) ||
             Wire.Encode(terminal.Destination) != Wire.Encode(reply.Destination) || stage.Replies.Any(x => x.PhysicalCompleted == true) && reply.PhysicalCompleted == false);
        if (inconsistent)
        {
            stage = stage with { State = StageState.Conflict, Replies = RetainReplyFacts(stage.Replies.Append(reply).ToArray()), Failure = "Contradictory or invalid original-stage receipt" };
            return SetStage(op, kind, stage) with { State = independentUpload ? op.State : ParcelState.Blocked, Reason = independentUpload ? op.Reason : stage.Failure };
        }
        if (prior is null) stage = stage with { Replies = RetainReplyFacts(stage.Replies.Append(reply).ToArray()) };
        if (stage.State == StageState.Conflict) return SetStage(op, kind, stage) with { State = independentUpload ? op.State : ParcelState.Blocked, Reason = independentUpload ? op.Reason : "Receipt conflict remains isolated" };
        if (reply.BusinessAccepted is null)
        {
            if (terminal is not null) return SetStage(op, kind, stage); // Less information cannot erase a terminal fact.
            return SetStage(op, kind, stage with { State = StageState.Unknown }) with
            { State = kind == ParcelStage.Upload && op.FrozenConfiguration.Images?.Strategy == UploadStrategy.Attached ? op.State : UnknownState(kind), Reason = independentUpload ? op.Reason : "Transport/protocol facts do not establish business acceptance" };
        }
        stage = stage with { State = reply.BusinessAccepted == true ? StageState.Accepted : StageState.Rejected, Failure = null };
        op = SetStage(op, kind, stage) with { Reason = independentUpload ? op.Reason : reply.BusinessAccepted == true ? null : reply.Evidence };
        op = kind is ParcelStage.Capture or ParcelStage.Upload ? ApplyImageReply(op, kind, reply) : definition.ApplyBusinessReply(this, op, kind, reply);
        return MainTerminated(op) ? op with { RecoveryRequired = false } : op;
    }
    void RecoverLocalFacts()
    {
        RestoreMainProjections();
        foreach (var op0 in operations.Values.ToArray())
        {
            var op = op0;
            if (!IsSettled(op))
            {
                op = op with { RecoveryRequired = !MainTerminated(op) };
                repository!.Commit(null, "RestartUnsettled", "Original entity/responsibility requires explicit operator recovery; no automatic dispatch", paused: true);
            }
            foreach (var kind in Enum.GetValues<ParcelStage>())
            {
                var stage = GetStage(op, kind);
                if (stage is { State: StageState.Prepared, Attempts: 0 })
                {
                    repository!.Commit(null, "RestartPrepared", "Never attempted original intention retained for explicit continuation or local disposition", paused: true);
                    continue;
                }
                if (stage?.State is StageState.Prepared or StageState.Dispatching)
                    op = SetStage(op, kind, stage with { State = StageState.Unknown, Failure = "Interrupted startup: reconcile the original identity" }) with
                    { State = kind == ParcelStage.Upload && op.FrozenConfiguration.Images?.Strategy == UploadStrategy.Attached ? op.State : UnknownState(kind), Reason = kind == ParcelStage.Upload && op.FrozenConfiguration.Images?.Strategy == UploadStrategy.Attached ? op.Reason : "Restart does not authorize automatic dispatch or replay" };
            }
            if (op != op0) Save(op, "RestartRecovery", "Prepared/Dispatching conservatively retained as unknown");
        }
        foreach (var item in repository!.PendingReceipts())
        {
            if (!operations.TryGetValue(item.Operation, out var op))
            {
                op = repository.ReadOperation(item.Operation) ?? throw new InvalidDataException("Receipt occurrence has no original operation");
                operations.Add(op.Id, op);
            }
            if (item.Reply is null)
            {
                if (repository.HasReceiptOutcomeBasis(item.Id)) continue; // Outcome known; missing original remains an independent pending custody occurrence.
                var missingKind = ParseStage(item.Stage);
                op = StageFailure(op, missingKind, GetStage(op, missingKind)!, "RawUnavailable:" + item.Id);
                if (!(missingKind == ParcelStage.Upload && op.FrozenConfiguration.Images?.Strategy == UploadStrategy.Attached)) op = op with { State = ParcelState.Blocked };
                Save(op, "ReceiptResponsibility", "Original receipt missing; a new query cannot clear this occurrence"); continue;
            }
            var kind = ParseStage(item.Stage);
            if (GetStage(op, kind)?.Request.Id != item.Request) throw new InvalidDataException("Original receipt stage mismatch");
            repository.RecoverRaw(item.Id, item.Reply);
            Save(ApplyReply(op, kind, item.Reply), "ReceiptRecovered", "Original durable raw replayed locally without an external call", item.Id);
        }
    }
    static StageReply[] RetainReplyFacts(StageReply[] replies)
    {
        if (replies.Length <= 64) return replies;
        var essential = new[] { replies.FirstOrDefault(r => r.BusinessAccepted is not null), replies.LastOrDefault(r => r.PhysicalCompleted == true) }.OfType<StageReply>();
        // Complete raw response history and reply-ID hashes remain in durable custody.
        return essential.Concat(replies.TakeLast(62)).DistinctBy(r => r.Id).ToArray();
    }
    ParcelOperation StageFailure(ParcelOperation op, ParcelStage kind, StageFact stage, string reason)
    {
        var attached = kind == ParcelStage.Upload && op.FrozenConfiguration.Images?.Strategy == UploadStrategy.Attached;
        return SetStage(op, kind, stage with { Failure = reason }) with { State = attached || stage.State is StageState.Accepted or StageState.Rejected or StageState.Unknown or StageState.Conflict ? op.State : ParcelState.Blocked, Reason = attached || stage.State is StageState.Accepted or StageState.Rejected ? op.Reason : reason };
    }
    public async Task<BusinessReceipt> ReconcileAsync(string operationId, string? stage = null)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            Management.Require();
            if (stopped || ports is null || !operations.TryGetValue(operationId, out var op)) return new("NotReady", "Original active operation unavailable; archived originals are read-only", repository?.Revision ?? 0);
            var requested = stage is null ? (ParcelStage?)null : ParseStage(stage);
            // Main dependencies first. An independent upload's missing raw never blocks a PLC/WCS query.
            var stages = new[] { ParcelStage.Capture }.Where(k => op.Capture?.State == StageState.Unknown)
                .Concat(definition.ReconciliationStages(op)).Concat(op.Upload?.State == StageState.Unknown ? new[] { ParcelStage.Upload } : []).Distinct().ToArray();
            var maintenanceBlocked = false; var queryUnsupported = false;
            foreach (var kind in stages.Where(k => requested is null || requested == k))
            {
                var original = GetStage(op, kind)!;
                // An absent query contract is not a new failure of the original request.
                // Keep its timeout/receipt facts intact and report the unavailable action.
                if (!factory.Requirements(original.Request.Binding).Query) { queryUnsupported = true; continue; }
                if (!PortMaintenanceClear(PortFor(op, kind))) { maintenanceBlocked = true; continue; }
                if (repository!.PendingReceipts().Any(x => x.Operation == operationId && x.Stage == original.Request.Stage)) continue;
                op = await ExecuteStageAsync(op, kind, true).ConfigureAwait(false);
            }
            if (maintenanceBlocked) return new("MaintenanceInProgress", "Original stage retained; selected capability is under maintenance", repository!.Revision);
            if (queryUnsupported) return new("QueryUnsupported", "原接口不提供结果查询；原请求和回包事实保留，不会重发。", repository!.Revision);
            return new(op.State.ToString(), requested is { } selected ? GetStage(op, selected)?.Failure ?? "Original stage inspected" : op.Reason ?? "Original main facts retained; attached responsibility is independent", repository!.Revision);
        }
        finally { gate.Release(); }
    }
}

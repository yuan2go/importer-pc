using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime
{
    async Task<ParcelOperation> ExecuteStageAsync(ParcelOperation op, ParcelStage kind, bool query)
    {
        var key = op.Id + ":" + kind;
        if (!activeStages.Add(key)) return operations[op.Id];
        try
        {
        op = operations[op.Id];
        var stage = GetStage(op, kind)!;
        var attached = kind == ParcelStage.Upload && op.FrozenConfiguration.Images?.Strategy == UploadStrategy.Attached;
        if (stage.Replies.Length >= 64) return op with { Reason = "Receipt retention budget requires controlled archive before further queries" };
        var binding = BindingFor(Effective, kind, op.PositionId);
        if (binding is null || Wire.Encode(binding) != Wire.Encode(stage.Request.Binding) ||
            ports!.ImplementationHashes.GetValueOrDefault(binding.InstanceId) != stage.Request.ImplementationHash)
        { op = op with { State = ParcelState.Blocked, Reason = "OriginalCapabilityUnavailable: preserve original request, package, target and implementation" }; Save(op, "OriginalCapabilityBlocked", op.Reason); return op; }
        if (query && !factory.Requirements(stage.Request.Binding).Query)
        { op = op with { Reason = "Provider declares no authoritative query capability; preserve unknown and original responsibility" }; Save(op, "QueryUnsupported", op.Reason); return op; }
        var port = PortFor(op, kind);
        CapabilityHealth health;
        try { health = await ExternalAsync(() => ProbeHealthAsync(port, stopping.Token)).ConfigureAwait(false); }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        { return operations[op.Id]; } // No delivery occurred, no failed health fact is invented on shutdown.
        capabilityHealth[port.InstanceId] = health;
        op = operations[op.Id];
        if (!health.Ready || health.ValidUntil <= DateTimeOffset.UtcNow || health.ConfirmedAt > DateTimeOffset.UtcNow)
        { op = SetStage(op, kind, stage with { Failure = "Original capability is not healthy" }) with { State = attached ? op.State : ParcelState.Blocked, Reason = attached ? op.Reason : "Original capability is not healthy; reconnect and reconcile" }; Save(op, "CapabilityNotReady", "Original capability is not healthy"); return op; }
        if (!query && ((!attached && !CanDispatch(op) || attached && !CanSendUpload(op)) ||
            !PredecessorsAccepted(op, kind) || (kind is ParcelStage.Wcs or ParcelStage.Plc || kind == ParcelStage.Upload && factory.Requirements(stage.Request.Binding).MeasuredWeight) && !WeightReady(op))) return op;
        if (stopping.IsCancellationRequested) return op;
        if (!query)
        {
            if (stage.State != StageState.Prepared || stage.Attempts != 0) throw new InvalidOperationException("Only a never-attempted prepared intent can be dispatched");
            stage = stage with { State = StageState.Dispatching, Attempts = 1 };
            op = SetStage(op, kind, stage); Save(op, "Dispatching", "T2: attempt persisted before external call");
        }
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token);
        budget.CancelAfter(Effective.TimeoutMs);
        var occurrence = repository!.BeginReceipt(op, stage, query ? "Query" : "Submit");
        StageReply? reply;
        try
        {
            reply = await ExternalAsync(async () => kind switch
            {
                ParcelStage.Wcs => query ? await ports.Wcs!.QueryAsync(stage.Request, budget.Token).ConfigureAwait(false) : await ports.Wcs!.ResolveSlotAsync(stage.Request, budget.Token).ConfigureAwait(false),
                ParcelStage.Plc => query ? await ports.Plc!.QueryAsync(stage.Request, budget.Token).ConfigureAwait(false) : await ports.Plc!.SubmitAsync(stage.Request, budget.Token).ConfigureAwait(false),
                ParcelStage.Capture => query ? await ((IImageCamera)port).QueryAsync(stage.Request, budget.Token).ConfigureAwait(false) : await ((IImageCamera)port).CaptureAsync(stage.Request, budget.Token).ConfigureAwait(false),
                ParcelStage.Upload => query ? await ports.Uploader!.QueryAsync(stage.Request, budget.Token).ConfigureAwait(false) : await ports.Uploader!.UploadAsync(stage.Request, ReadImage(op, op.Image!), budget.Token).ConfigureAwait(false),
                _ => throw new InvalidOperationException("Unregistered stage")
            }).ConfigureAwait(false);
            op = operations[op.Id];
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException)
        {
            repository.NoReply(occurrence);
            op = operations[op.Id];
            // A failed confirmation query supplies no contrary evidence about an
            // already accepted/rejected command. Keep that fact and its original reply.
            var knownOutcome = stage.State is StageState.Accepted or StageState.Rejected;
            stage = stage with { State = knownOutcome ? stage.State : StageState.Unknown, Failure = ex.GetType().Name + ": " + ex.Message };
            op = SetStage(op, kind, stage) with { State = attached || knownOutcome ? op.State : UnknownState(kind),
                Reason = knownOutcome ? "Original query unavailable; prior outcome retained, physical completion needs evidence" :
                    "Delivery/outcome unknown; original request must be reconciled, never resent" };
            Save(op, "OutcomeUnknown", stage.Failure!); return op;
        }
        if (reply is null)
        {
            repository.NoReply(occurrence);
            // Query absence does not prove no side effect.
            if (stage.State is not (StageState.Accepted or StageState.Rejected)) stage = stage with { State = StageState.Unknown };
            op = SetStage(op, kind, stage) with { Reason = "Original query has no authoritative result; no resend is permitted" };
            Save(op, "QueryInconclusive", op.Reason); return op;
        }
        // If persistence fails here, the Awaiting occurrence remains independently visible.
        // Do not reclassify a returned-but-unrecorded reply as an ordinary transport failure.
        repository.StoreRaw(occurrence, reply);
        Boundary?.Invoke("AfterRawDurable");
        op = ApplyReply(op, kind, reply); Save(op, "ReceiptApplied", "T3: original raw reply, stage and downstream intent committed together", occurrence);
        return op;
        }
        finally { activeStages.Remove(key); }
    }
    ParcelOperation ApplyReply(ParcelOperation op, ParcelStage kind, StageReply reply)
    {
        var wcs = kind == ParcelStage.Wcs;
        var independentUpload = kind == ParcelStage.Upload && op.FrozenConfiguration.Images?.Strategy == UploadStrategy.Attached;
        var stage = GetStage(op, kind)!;
        var prior = stage.Replies.FirstOrDefault(x => x.Id == reply.Id);
        var inconsistent = reply.RequestId != stage.Request.Id || reply.PayloadHash != stage.Request.PayloadHash ||
            !Guid.TryParseExact(reply.Id, "N", out _) || string.IsNullOrWhiteSpace(reply.Evidence) || reply.At < stage.Request.PreparedAt || reply.At > DateTimeOffset.UtcNow.AddSeconds(1) ||
            reply.BusinessAccepted is not null && (!reply.TransportSucceeded || reply.ProtocolAcknowledged != true) ||
            kind == ParcelStage.Plc && factory.Requirements(stage.Request.Binding) is { Query: false, CompletesOnSubmit: true } && reply.BusinessAccepted == true && reply.PhysicalCompleted != true ||
            reply.PhysicalCompleted == true && (reply.BusinessAccepted != true || !factory.Requirements(stage.Request.Binding).PhysicalConfirmation) ||
            wcs && reply.BusinessAccepted == true && (string.IsNullOrWhiteSpace(reply.Slot) || reply.Slot.Length > 64 || reply.Slot.Any(char.IsControl)) ||
            prior is not null && Wire.Encode(prior) != Wire.Encode(reply) ||
            kind is ParcelStage.Capture or ParcelStage.Upload && reply.BusinessAccepted == true && !ValidImageReply(op, kind, reply.Image);
        var terminal = stage.Replies.LastOrDefault(x => x.BusinessAccepted is not null);
        inconsistent |= terminal is not null && reply.BusinessAccepted is not null &&
            (terminal.BusinessAccepted != reply.BusinessAccepted || terminal.Slot != reply.Slot || Wire.Encode(terminal.Image) != Wire.Encode(reply.Image) || terminal.PhysicalCompleted == true && reply.PhysicalCompleted == false);
        if (inconsistent)
        {
            stage = stage with { State = StageState.Conflict, Replies = stage.Replies.Append(reply).ToArray(), Failure = "Contradictory or invalid original-stage receipt" };
            return SetStage(op, kind, stage) with { State = independentUpload ? op.State : ParcelState.Blocked, Reason = independentUpload ? op.Reason : stage.Failure };
        }
        if (prior is null) stage = stage with { Replies = stage.Replies.Append(reply).ToArray() };
        if (stage.State == StageState.Conflict) return SetStage(op, kind, stage) with { State = independentUpload ? op.State : ParcelState.Blocked, Reason = independentUpload ? op.Reason : "Receipt conflict remains isolated" };
        if (reply.BusinessAccepted is null)
        {
            if (terminal is not null) return SetStage(op, kind, stage); // Less information cannot erase a terminal fact.
            return SetStage(op, kind, stage with { State = StageState.Unknown }) with
                { State = kind == ParcelStage.Upload && op.FrozenConfiguration.Images?.Strategy == UploadStrategy.Attached ? op.State : UnknownState(kind), Reason = "Transport/protocol facts do not establish business acceptance" };
        }
        stage = stage with { State = reply.BusinessAccepted == true ? StageState.Accepted : StageState.Rejected, Failure = null };
        op = SetStage(op, kind, stage) with { Reason = reply.BusinessAccepted == true ? null : reply.Evidence };
        if (kind is ParcelStage.Capture or ParcelStage.Upload) return ApplyImageReply(op, kind, reply);
        return definition.ApplyBusinessReply(this, op, kind, reply);
    }
    void RecoverLocalFacts()
    {
        foreach (var op0 in operations.Values.ToArray())
        {
            var op = op0;
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
                        { State = kind == ParcelStage.Upload && op.FrozenConfiguration.Images?.Strategy == UploadStrategy.Attached ? op.State : UnknownState(kind), Reason = "Restart does not authorize automatic dispatch or replay" };
            }
            if (op != op0) Save(op, "RestartRecovery", "Prepared/Dispatching conservatively retained as unknown");
        }
        foreach (var item in repository!.PendingReceipts())
        {
            if (!operations.TryGetValue(item.Operation, out var op)) throw new InvalidDataException("Receipt occurrence has no original operation");
            if (item.Reply is null)
            { Save(op with { State = ParcelState.Blocked, Reason = "RawUnavailable:" + item.Id }, "ReceiptResponsibility", "Original receipt missing; a new query cannot clear this occurrence"); continue; }
            var kind = ParseStage(item.Stage);
            if (GetStage(op, kind)?.Request.Id != item.Request) throw new InvalidDataException("Original receipt stage mismatch");
            repository.RecoverRaw(item.Id, item.Reply);
            Save(ApplyReply(op, kind, item.Reply), "ReceiptRecovered", "Original durable raw replayed locally without an external call", item.Id);
        }
    }
    public async Task<BusinessReceipt> ReconcileAsync(string operationId)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (stopped || ports is null || !operations.TryGetValue(operationId, out var op)) return new("NotReady", "Original operation unavailable", repository?.Revision ?? 0);
            if (repository!.PendingReceipts().Any(x => x.Operation == operationId)) return new("Blocked", "Original raw receipt responsibility must be recovered first", repository.Revision);
            if (op.Capture?.State == StageState.Unknown) op = await ExecuteStageAsync(op, ParcelStage.Capture, true).ConfigureAwait(false);
            if (op.Upload?.State == StageState.Unknown) op = await ExecuteStageAsync(op, ParcelStage.Upload, true).ConfigureAwait(false);
            foreach (var stage in definition.ReconciliationStages(op)) op = await ExecuteStageAsync(op, stage, true).ConfigureAwait(false);
            return new(op.State.ToString(), op.Reason ?? "Original stage reconciled; physical completion is independent", repository.Revision);
        }
        finally { gate.Release(); }
    }
}

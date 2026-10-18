using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime
{
    async Task<ParcelOperation> ExecuteStageAsync(ParcelOperation op, bool wcs, bool query)
    {
        var stage = (wcs ? op.Wcs : op.Plc)!;
        if (stage.Replies.Length >= 64) return op with { Reason = "Receipt retention budget requires controlled archive before further queries" };
        var binding = wcs ? Effective.Wcs : Effective.Plc;
        if (binding is null || Wire.Encode(binding) != Wire.Encode(stage.Request.Binding) ||
            ports!.ImplementationHashes.GetValueOrDefault(binding.InstanceId) != stage.Request.ImplementationHash)
        { op = op with { State = ParcelState.Blocked, Reason = "OriginalCapabilityUnavailable: preserve original request, package, target and implementation" }; Save(op, "OriginalCapabilityBlocked", op.Reason); return op; }
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token);
        budget.CancelAfter(Effective.TimeoutMs);
        var port = wcs ? (ICapabilityPort)ports.Wcs! : ports.Plc!;
        var health = await port.HealthAsync(budget.Token).ConfigureAwait(false);
        if (!health.Ready || health.ValidUntil <= DateTimeOffset.UtcNow || health.ConfirmedAt > DateTimeOffset.UtcNow)
        { op = op with { State = ParcelState.Blocked, Reason = "Original capability is not healthy; reconnect and reconcile" }; Save(op, "CapabilityNotReady", op.Reason); return op; }
        if (!query)
        {
            if (stage.State != StageState.Prepared || stage.Attempts != 0) throw new InvalidOperationException("Only a never-attempted prepared intent can be dispatched");
            stage = stage with { State = StageState.Dispatching, Attempts = 1 };
            op = SetStage(op, wcs, stage); Save(op, "Dispatching", "T2: attempt persisted before external call");
        }
        var occurrence = repository!.BeginReceipt(op, stage, query ? "Query" : "Submit");
        StageReply? reply;
        try
        {
            reply = wcs ? query ? await ports.Wcs!.QueryAsync(stage.Request, budget.Token).ConfigureAwait(false)
                    : await ports.Wcs!.ResolveSlotAsync(stage.Request, budget.Token).ConfigureAwait(false)
                : query ? await ports.Plc!.QueryAsync(stage.Request, budget.Token).ConfigureAwait(false)
                    : await ports.Plc!.SubmitAsync(stage.Request, budget.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException)
        {
            repository.NoReply(occurrence);
            stage = stage with { State = StageState.Unknown, Failure = ex.GetType().Name + ": " + ex.Message };
            op = SetStage(op, wcs, stage) with { State = wcs ? ParcelState.WcsUnknown : ParcelState.PlcUnknown,
                Reason = "Delivery/outcome unknown; original request must be reconciled, never resent" };
            Save(op, "OutcomeUnknown", stage.Failure!); return op;
        }
        if (reply is null)
        {
            repository.NoReply(occurrence);
            // Query absence does not prove no side effect.
            if (stage.State is not (StageState.Accepted or StageState.Rejected)) stage = stage with { State = StageState.Unknown };
            op = SetStage(op, wcs, stage) with { Reason = "Original query has no authoritative result; no resend is permitted" };
            Save(op, "QueryInconclusive", op.Reason); return op;
        }
        // If persistence fails here, the Awaiting occurrence remains independently visible.
        // Do not reclassify a returned-but-unrecorded reply as an ordinary transport failure.
        repository.StoreRaw(occurrence, reply);
        Boundary?.Invoke("AfterRawDurable");
        op = ApplyReply(op, wcs, reply); Save(op, "ReceiptApplied", "T3: original raw reply, stage and downstream intent committed together", occurrence);
        return op;
    }
    static ParcelOperation SetStage(ParcelOperation op, bool wcs, StageFact stage) => wcs ? op with { Wcs = stage } : op with { Plc = stage };
    ParcelOperation ApplyReply(ParcelOperation op, bool wcs, StageReply reply)
    {
        var stage = (wcs ? op.Wcs : op.Plc)!;
        var prior = stage.Replies.FirstOrDefault(x => x.Id == reply.Id);
        var inconsistent = reply.RequestId != stage.Request.Id || reply.PayloadHash != stage.Request.PayloadHash ||
            !Guid.TryParseExact(reply.Id, "N", out _) || string.IsNullOrWhiteSpace(reply.Evidence) || reply.At < stage.Request.PreparedAt || reply.At > DateTimeOffset.UtcNow.AddSeconds(1) ||
            reply.BusinessAccepted is not null && (!reply.TransportSucceeded || reply.ProtocolAcknowledged != true) ||
            reply.PhysicalCompleted == true && reply.BusinessAccepted != true ||
            wcs && reply.BusinessAccepted == true && (string.IsNullOrWhiteSpace(reply.Slot) || reply.Slot.Length > 64 || reply.Slot.Any(char.IsControl)) ||
            prior is not null && Wire.Encode(prior) != Wire.Encode(reply);
        var terminal = stage.Replies.LastOrDefault(x => x.BusinessAccepted is not null);
        inconsistent |= terminal is not null && reply.BusinessAccepted is not null &&
            (terminal.BusinessAccepted != reply.BusinessAccepted || terminal.Slot != reply.Slot || terminal.PhysicalCompleted == true && reply.PhysicalCompleted == false);
        if (inconsistent)
        {
            stage = stage with { State = StageState.Conflict, Replies = stage.Replies.Append(reply).ToArray(), Failure = "Contradictory or invalid original-stage receipt" };
            return SetStage(op, wcs, stage) with { State = ParcelState.Blocked, Reason = stage.Failure };
        }
        if (prior is null) stage = stage with { Replies = stage.Replies.Append(reply).ToArray() };
        if (stage.State == StageState.Conflict) return SetStage(op, wcs, stage) with { State = ParcelState.Blocked, Reason = "Receipt conflict remains isolated" };
        if (reply.BusinessAccepted is null)
        {
            if (terminal is not null) return SetStage(op, wcs, stage); // Less information cannot erase a terminal fact.
            return SetStage(op, wcs, stage with { State = StageState.Unknown }) with
                { State = wcs ? ParcelState.WcsUnknown : ParcelState.PlcUnknown, Reason = "Transport/protocol facts do not establish business acceptance" };
        }
        stage = stage with { State = reply.BusinessAccepted == true ? StageState.Accepted : StageState.Rejected, Failure = null };
        op = SetStage(op, wcs, stage) with { Reason = reply.BusinessAccepted == true ? null : reply.Evidence };
        if (wcs)
        {
            op = op with { Slot = reply.BusinessAccepted == true ? reply.Slot : op.Slot, State = reply.BusinessAccepted == true ? ParcelState.SlotResolved : ParcelState.WcsRejected };
            // Critical atomic bridge: WCS acceptance and the original PLC intention are one T3.
            if (reply.BusinessAccepted == true && definition.RequiresPlc && op.Plc is null) op = PreparePlc(op);
            if (op.Plc is not null) op = op with { State = PlcState(op.Plc), Reason = op.Plc.Failure };
        }
        else op = op with { State = reply.BusinessAccepted == false ? ParcelState.PlcRejected :
            reply.PhysicalCompleted == true || terminal?.PhysicalCompleted == true ? ParcelState.PhysicalCompleted : ParcelState.PlcAccepted };
        return op;
    }
    static ParcelState PlcState(StageFact stage) => stage.State switch
    {
        StageState.Prepared or StageState.Dispatching => ParcelState.SubmittingPlc,
        StageState.Accepted => stage.Replies.Any(x => x.PhysicalCompleted == true) ? ParcelState.PhysicalCompleted : ParcelState.PlcAccepted,
        StageState.Rejected => ParcelState.PlcRejected,
        StageState.Unknown => ParcelState.PlcUnknown,
        _ => ParcelState.Blocked
    };
    void RecoverLocalFacts()
    {
        foreach (var op0 in operations.Values.ToArray())
        {
            var op = op0;
            foreach (var wcs in new[] { true, false })
            {
                var stage = wcs ? op.Wcs : op.Plc;
                if (stage?.State is StageState.Prepared or StageState.Dispatching)
                    op = SetStage(op, wcs, stage with { State = StageState.Unknown, Failure = "Interrupted startup: reconcile the original identity" }) with
                        { State = wcs ? ParcelState.WcsUnknown : ParcelState.PlcUnknown, Reason = "Restart does not authorize automatic dispatch or replay" };
            }
            if (op != op0) Save(op, "RestartRecovery", "Prepared/Dispatching conservatively retained as unknown");
        }
        foreach (var item in repository!.PendingReceipts())
        {
            if (!operations.TryGetValue(item.Operation, out var op)) throw new InvalidDataException("Receipt occurrence has no original operation");
            if (item.Reply is null)
            { Save(op with { State = ParcelState.Blocked, Reason = "RawUnavailable:" + item.Id }, "ReceiptResponsibility", "Original receipt missing; a new query cannot clear this occurrence"); continue; }
            var wcs = item.Stage == "wcs-slot";
            if ((wcs ? op.Wcs : op.Plc)?.Request.Id != item.Request) throw new InvalidDataException("Original receipt stage mismatch");
            repository.RecoverRaw(item.Id, item.Reply);
            Save(ApplyReply(op, wcs, item.Reply), "ReceiptRecovered", "Original durable raw replayed locally without an external call", item.Id);
        }
    }
    public async Task<BusinessReceipt> ReconcileAsync(string operationId)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (stopped || ports is null || !operations.TryGetValue(operationId, out var op)) return new("NotReady", "Original operation unavailable", repository?.Revision ?? 0);
            if (repository!.PendingReceipts().Any(x => x.Operation == operationId)) return new("Blocked", "Original raw receipt responsibility must be recovered first", repository.Revision);
            if (op.Wcs?.State is StageState.Unknown) op = await ExecuteStageAsync(op, true, true).ConfigureAwait(false);
            if (op.Plc?.State == StageState.Unknown || op.Plc?.State == StageState.Accepted && !op.Plc.Replies.Any(x => x.PhysicalCompleted == true)) op = await ExecuteStageAsync(op, false, true).ConfigureAwait(false);
            return new(op.State.ToString(), op.Reason ?? "Original stage reconciled; physical completion is independent", repository.Revision);
        }
        finally { gate.Release(); }
    }
}

using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Platform;
namespace Ipc.Application.WcsToPlc;

public sealed class WcsToPlcFlow : FlowDefinition
{
    public override string Id => "wcs-to-plc";
    public override string[] ValidateBusiness(ParcelConfiguration c) => c.Wcs is null || c.Plc is null ? ["WcsToPlc capability dependencies do not match its flow"] : [];
    public override bool PredecessorsAccepted(ParcelOperation op, ParcelStage stage) => stage == ParcelStage.Wcs || stage == ParcelStage.Plc && op.Wcs?.State == StageState.Accepted && !string.IsNullOrWhiteSpace(op.Slot);
    public override ParcelStage[] ReconciliationStages(ParcelOperation op) =>
        (op.Wcs?.State == StageState.Unknown ? new[] { ParcelStage.Wcs } : []).Concat(
            op.Plc?.State == StageState.Unknown || op.Plc?.State == StageState.Accepted && !op.Plc.Replies.Any(x => x.PhysicalCompleted == true) ? [ParcelStage.Plc] : []).ToArray();
    public override bool MainSettled(ParcelOperation op) => op.State is ParcelState.PhysicalCompleted or ParcelState.PlcRejected or ParcelState.WcsRejected;
    public override async Task AdvanceAsync(IParcelExecution e, ParcelOperation op)
    {
        op = e.Current(op.Id);
        if (op.Wcs is null) { op = e.PrepareWcs(op); e.Persist(op, "Original WCS intent"); }
        if (op.Wcs is { State: StageState.Prepared, Attempts: 0 }) op = await e.ExecuteAsync(op, ParcelStage.Wcs).ConfigureAwait(false);
        if (op.Wcs?.State != StageState.Accepted || string.IsNullOrWhiteSpace(op.Slot)) return;
        if (op.Plc is null) { op = e.PreparePlc(op); e.Persist(op, "Original PLC intent"); }
        if (op.Plc is { State: StageState.Prepared, Attempts: 0 }) await e.ExecuteAsync(op, ParcelStage.Plc).ConfigureAwait(false);
    }
    public override ParcelOperation ApplyBusinessReply(IParcelExecution e, ParcelOperation op, ParcelStage stage, StageReply reply)
    {
        if (stage == ParcelStage.Wcs)
        {
            op = op with { Slot = reply.BusinessAccepted == true ? reply.Slot : op.Slot,
                State = reply.BusinessAccepted == true ? ParcelState.SlotResolved : ParcelState.WcsRejected };
            // Returned aggregate is saved by the shared receipt T3 in ONE transaction.
            if (reply.BusinessAccepted == true && op.Plc is null) op = e.PreparePlc(op);
            if (op.Plc is not null) op = op with { State = FlowResults.PlcState(op.Plc) };
            return op;
        }
        if (stage == ParcelStage.Plc) return op with { State = FlowResults.PlcState(op.Plc!) };
        throw new InvalidDataException("Stage does not belong to this Application");
    }
}
public static class WcsToPlcApplication
{
    public static ParcelRuntime Create(BusinessLaunch launch, IParcelCapabilityFactory factory) => new(launch, new WcsToPlcFlow(), factory);
}

using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Platform;
namespace Ipc.Application.DirectPlc;

public sealed class DirectPlcFlow : FlowDefinition
{
    public override string Id => "direct-plc";
    public override string[] ValidateBusiness(ParcelConfiguration c) => c.Wcs is not null || c.Plc is null ? ["DirectPlc capability dependencies do not match its flow"] : [];
    public override bool PredecessorsAccepted(ParcelOperation op, ParcelStage stage) => stage == ParcelStage.Plc;
    public override ParcelStage[] ReconciliationStages(ParcelOperation op) =>
        op.Plc?.State == StageState.Unknown || op.Plc?.State == StageState.Accepted && !op.Plc.Replies.Any(x => x.PhysicalCompleted == true) ? [ParcelStage.Plc] : [];
    public override bool MainSettled(ParcelOperation op) => op.State is ParcelState.PhysicalCompleted or ParcelState.PlcRejected;
    public override async Task AdvanceAsync(IParcelExecution e, ParcelOperation op)
    {
        op = e.Current(op.Id);
        if (op.Plc is null) { op = e.PreparePlc(op); e.Persist(op, "Original PLC intent"); }
        if (op.Plc is { State: StageState.Prepared, Attempts: 0 }) await e.ExecuteAsync(op, ParcelStage.Plc).ConfigureAwait(false);
    }
    public override ParcelOperation ApplyBusinessReply(IParcelExecution e, ParcelOperation op, ParcelStage stage, StageReply reply)
    {
        if (stage == ParcelStage.Plc) return op with { State = FlowResults.PlcState(op.Plc!) };
        throw new InvalidDataException("Stage does not belong to this Application");
    }
}
public static class DirectPlcApplication
{
    public static ParcelRuntime Create(BusinessLaunch launch, IParcelCapabilityFactory factory) => new(launch, new DirectPlcFlow(), factory);
}

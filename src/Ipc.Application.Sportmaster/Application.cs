using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Platform;
namespace Ipc.Application.Sportmaster;

public sealed class SportmasterFlow : FlowDefinition
{
    public override string Id => "sportmaster";
    public override string[] PositionRoles => ["left", "right"];
    public override string[] ValidateBusiness(ParcelConfiguration c) => c.Wcs is null || c.Plc is not null ? ["Sportmaster capability dependencies do not match its flow"] : [];
    public override bool PredecessorsAccepted(ParcelOperation op, ParcelStage stage) => stage == ParcelStage.Wcs;
    public override ParcelStage[] ReconciliationStages(ParcelOperation op) =>
        op.Wcs?.State == StageState.Unknown ? [ParcelStage.Wcs] : [];
    public override bool MainSettled(ParcelOperation op) => op.State is ParcelState.SlotResolved or ParcelState.WcsRejected;
    public override async Task AdvanceAsync(IParcelExecution e, ParcelOperation op)
    {
        op = e.Current(op.Id);
        if (op.Wcs is null) { op = e.PrepareWcs(op); e.Persist(op, "Original WCS intent"); }
        if (op.Wcs is { State: StageState.Prepared, Attempts: 0 }) op = await e.ExecuteAsync(op, ParcelStage.Wcs).ConfigureAwait(false);
    }
    public override ParcelOperation ApplyBusinessReply(IParcelExecution e, ParcelOperation op, ParcelStage stage, StageReply reply)
    {
        if (stage == ParcelStage.Wcs)
        {
            op = op with { Slot = reply.BusinessAccepted == true ? reply.Slot : op.Slot,
                State = reply.BusinessAccepted == true ? ParcelState.SlotResolved : ParcelState.WcsRejected };
            return op;
        }
        throw new InvalidDataException("Stage does not belong to this Application");
    }
}
public static class SportmasterApplication
{
    public static ParcelRuntime Create(BusinessLaunch launch, IParcelCapabilityFactory factory) => new(launch, new SportmasterFlow(), factory);
}

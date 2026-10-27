using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Platform;
namespace Ipc.Application.Sportmaster;

public sealed class SportmasterFlow : FlowDefinition
{
    public override string Id => "sportmaster";
    public override ParcelConfiguration InitialConfiguration()
    {
        DeviceBinding[] readers = [new("parcel-reader", "tcp-line", "sportmaster.reader.left", "1") { Parameters = new() { ["entity.mode"] = "line" } }, new("parcel-reader", "tcp-line", "sportmaster.reader.right", "1") { Parameters = new() { ["entity.mode"] = "line" } }];
        // A draft in Production with unavailable real dependencies cannot silently run a simulation.
        return new(3, Id, SystemParameterDefaults.InitialStationId, RunMode.Production,
            new(WeightSource.Disabled, WeightUnit.Kg, 3, null, 3000), readers[0], null,
            new("integration", "sportmaster-rcs", "2.0.0", "sportmaster.integration", "", "SPORTMASTER-RCS-SORTER-v1", new() { ["left-station-code"] = SystemParameterDefaults.InitialStationId, ["right-station-code"] = SystemParameterDefaults.InitialStationId }), null,
            WeightValidity: Ipc.Application.Contracts.WeightValidity.UntilDispatch,
            Topology: new(readers, [new("left", readers[0].DeviceId), new("right", readers[1].DeviceId)]),
            StationCode: SystemParameterDefaults.StationCode(SystemParameterDefaults.InitialStationId),
            Retention: new(DiagnosticLogDays: SystemParameterDefaults.DiagnosticLogDays, HistoryDays: SystemParameterDefaults.HistoryDays))
            { DeferredConnections = [readers[0].DeviceId, readers[1].DeviceId, "sportmaster.integration"] };
    }
    public override string[] PositionRoles => ["left", "right"];
    public override BarcodeFilterResult SelectBarcode(ParcelConfiguration c, Observation o) => BarcodeFiltering.Select(c.BarcodeFilter, o.Codes, o.CandidateIssue);
    public override string[] ValidateBusiness(ParcelConfiguration c) => [.. c.Wcs is null || c.Plc is not null ? new[] { "Sportmaster capability dependencies do not match its flow" } : [], .. BarcodeFiltering.Validate(c.BarcodeFilter) is { } error ? new[] { error } : []];
    public override bool PredecessorsAccepted(ParcelOperation op, ParcelStage stage) => stage == ParcelStage.Wcs;
    public override ParcelStage[] ReconciliationStages(ParcelOperation op) =>
        op.Wcs?.State == StageState.Unknown && !AllowsNextAdmissionAfter(op) ? [ParcelStage.Wcs] : [];
    public override bool MainSettled(ParcelOperation op) => op.State is ParcelState.SlotResolved or ParcelState.WcsRejected;
    public override bool AllowsNextAdmissionAfter(ParcelOperation op) => op.State == ParcelState.WcsUnknown &&
        op.Plc is null && (op.Capture is null or { State: StageState.Accepted }) &&
        (op.FrozenConfiguration.Images?.Strategy == UploadStrategy.Attached || op.Upload is null or { State: StageState.Accepted or StageState.Rejected or StageState.Cancelled }) &&
        op.Wcs is { State: StageState.Unknown, Attempts: 1, Request.Binding.Contract: "SPORTMASTER-RCS-SORTER-v1" } stage &&
        stage.Replies.All(r => r.BusinessAccepted is null) &&
        (stage.Failure?.StartsWith("TimeoutException:", StringComparison.Ordinal) == true ||
         stage.Replies.LastOrDefault()?.Evidence.StartsWith("RCS_REQUEST_TIMEOUT:", StringComparison.Ordinal) == true);
    public override int RequestTimeoutMs(ParcelOperation op, ParcelStage stage)
    {
        if (stage != ParcelStage.Wcs || op.Wcs?.Request.Binding is not { Contract: "SPORTMASTER-RCS-SORTER-v1" } binding)
            return base.RequestTimeoutMs(op, stage);
        return RcsRequestPolicy.TimeoutMs(binding);
    }
    public override ParcelOperation RestoreMainProjection(ParcelOperation op)
    {
        if (op.Wcs is not { State: StageState.Accepted or StageState.Rejected } wcs) return op;
        var reply = wcs.Replies.Last(x => x.BusinessAccepted is not null);
        return op with { State = wcs.State == StageState.Accepted ? ParcelState.SlotResolved :
                reply.Destination?.Outcome == WcsDestinationOutcome.UnpackRequired ? ParcelState.UnpackRequired : ParcelState.WcsRejected,
            Slot = wcs.State == StageState.Accepted ? reply.Slot : op.Slot, Reason = wcs.State == StageState.Accepted ? null : reply.Evidence };
    }
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
                State = reply.BusinessAccepted == true ? ParcelState.SlotResolved :
                    reply.Destination?.Outcome == WcsDestinationOutcome.UnpackRequired ? ParcelState.UnpackRequired : ParcelState.WcsRejected };
            return op;
        }
        throw new InvalidDataException("Stage does not belong to this Application");
    }
}
public static class SportmasterApplication
{
    public static ParcelRuntime Create(BusinessLaunch launch, IParcelCapabilityFactory factory) => new(launch, new SportmasterFlow(), factory);
}

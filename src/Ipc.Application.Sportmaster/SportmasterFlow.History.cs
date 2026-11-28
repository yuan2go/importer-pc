using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;

namespace Ipc.Application.Sportmaster;

public sealed partial class SportmasterFlow
{
    public override bool RetainsReleasedUnknownsInHistory => true;
    public override bool CountsCurrentRoundCapacity => true;
    public override bool SupportsStationStatistics => true;
    public override bool SupportsAutomaticHistoryCleanup => true;
    public override bool SupportsStationDiagnostics => true;
    public override bool SupportsStationExceptions => true;
    public override string CapacityPolicyDescription =>
        "SM parcel admission has no unresolved-operation count limit; actual storage reserve and original request guards remain enforced";

    public override bool HasTerminalRecoveryBasis(ParcelOperation operation) => operation is
        { State: ParcelState.SlotResolved, Wcs: { State: StageState.Accepted } wcs } &&
        !string.IsNullOrWhiteSpace(operation.Slot) && wcs.Replies.Any(r => r.BusinessAccepted == true && r.Slot == operation.Slot);

    public override bool IsReleasedUnknownHistoryCandidate(ParcelOperation operation) =>
        operation.FrozenConfiguration.ApplicationId == Id &&
        operation.FrozenConfiguration is { Images: null, Plc: null } &&
        operation is { State: ParcelState.WcsUnknown, Capture: null, Upload: null, Plc: null, Image: null } &&
        operation.OriginalFrame?.State != "Pending" &&
        operation.Wcs is
        {
            State: StageState.Unknown, Attempts: 1,
            Request.Binding: { Package: null, Implementation: "sportmaster-rcs", Contract: RcsRequestPolicy.Contract }
        };

    public override StationOperationFact? ProjectStationOperationFacts(ParcelOperation operation) =>
        operation.FrozenConfiguration.ApplicationId != Id ? null : StationOperationFact.FromDestinationRequest(operation);
}

using Ipc.Application.Contracts;

namespace Ipc.Application.ParcelFlow;

internal static class ParcelUnsubmittedIntent
{
    // The caller owns authorization, entity inspection, pause and attempt/custody guards.
    // Both online and offline recovery retain every frozen request and original identity.
    internal static ParcelOperation Abandon(ParcelOperation operation, string reason) => operation with
    {
        State = ParcelState.Abandoned, RecoveryRequired = false,
        Wcs = Cancel(operation.Wcs), Plc = Cancel(operation.Plc),
        Upload = Cancel(operation.Upload, "Local cancellation before any external upload attempt"),
        Capture = Cancel(operation.Capture, "Local cancellation before acquisition"),
        Reason = "Unsubmitted parcel inspected and removed: " + reason
    };
    internal static StageFact? Cancel(StageFact? stage, string failure = "Inspected local cancellation before any external attempt") =>
        stage is { State: StageState.Prepared, Attempts: 0 } ? stage with { State = StageState.Cancelled, Failure = failure } : stage;
}

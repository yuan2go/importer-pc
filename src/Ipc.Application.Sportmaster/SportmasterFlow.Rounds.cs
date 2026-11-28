using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.Sportmaster;

public sealed partial class SportmasterFlow
{
    public override bool AllowsSingleLaneCalibration => true;
    public override bool SupportsInspectionCapture => true;
    public override bool SupportsManualInput => true;
    public override bool SupportsManagedWorkspace => true;
    public override bool SupportsLiveConfiguration => true;
    public override bool ReconnectsReadersAutomatically => true;
    public override bool UsesEarlyDurableIngress => true;
    public override bool SkipsPreviousCameraInputs => true;
    public override bool UsesPositionScopedRequests => true;
    public override bool RequiresArrivalHeartbeatForAutomaticRead => true;
    public override bool RequiresRecoveryReviewOnStart => false;

    public override bool UsesStationHeartbeat(ParcelConfiguration configuration) =>
        configuration.Wcs?.Contract == RcsRequestPolicy.Contract;

    public override bool IsAutomaticReadTarget(ParcelConfiguration configuration, string positionId) =>
        !UsesStationHeartbeat(configuration) ||
        !string.IsNullOrWhiteSpace(configuration.Wcs!.Parameters.GetValueOrDefault(positionId + "-bcr-code"));

    public override string? FrozenRequestStationCode(ParcelConfiguration configuration) =>
        configuration.ApplicationId == Id ? configuration.StationCode : null;

    public override bool RequiresNewArrival(SmPositionCycle cycle, StationVehicleState vehicle) =>
        cycle.VehicleNo != vehicle.VehicleNo && cycle.RecoveryReason is null;

    public override SmPositionCycle NewArrival(SmPositionCycle cycle, StationVehicleState vehicle, long requestId,
        string generation, string placementGeneration) => cycle with
    {
        Generation = generation, PlacementGeneration = placementGeneration, VehicleNo = vehicle.VehicleNo, ArrivalAt = vehicle.ReceivedAt,
        ArrivalEvidence = "heartbeat:" + requestId,
        AutoSuppressed = false, Presence = TrayPresence.Unknown, Epoch = null, Sequence = 0,
        Evidence = null, ClearedAt = vehicle.ReceivedAt, Withdrawn = false
    };

    public override SmPositionCycle[] ProjectDestinationCustody(SmPositionCycle cycle, ParcelOperation operation, bool firstAttempt)
    {
        if (firstAttempt && operation.Wcs is { Attempts: 1, State: StageState.Dispatching } &&
            (cycle.TargetOperation is null || cycle.TargetOperation == operation.Id))
            return [cycle with { TargetOperation = operation.Id, SubmissionAttempted = true, SubmittedOperation = operation.Id }];
        if (cycle.TargetOperation == operation.Id && operation.Wcs is { } stage && HasConfirmedDestinationOutcome(stage))
            return [cycle with { TargetOperation = null }];
        return [];
    }

    public override bool HasConfirmedDestinationOutcome(StageFact stage) =>
        stage.State is StageState.Accepted or StageState.Rejected or StageState.Cancelled ||
        stage is { Attempts: 1, Replies: [{ SenderDelivery: SenderDeliveryEvidence.ConnectionRefusedBeforeBody }] };

    public override bool EndsSuccessfulRound(TimeSpan displayed) => displayed.TotalMilliseconds >= 3000;

    public override bool IsReadWindowExpired(SmParcelRound round, TimeSpan elapsed) =>
        elapsed.TotalMilliseconds >= round.ReadTimeoutMs;

    public override StationPositionMode ResolvePositionMode(bool enabled, bool drainsOriginal) =>
        enabled ? StationPositionMode.Enabled : drainsOriginal ? StationPositionMode.Disabling : StationPositionMode.Disabled;

    public override bool HasPositionScopedRecovery(ParcelOperation operation, bool isDebug, bool placementReleased) => !isDebug &&
        (placementReleased || (operation.Wcs?.Request.Binding ?? operation.FrozenConfiguration.Wcs)?.Contract == RcsRequestPolicy.Contract);
}

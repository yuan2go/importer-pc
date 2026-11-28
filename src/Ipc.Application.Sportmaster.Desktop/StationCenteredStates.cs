using Ipc.Presentation.Wpf;
using System.Globalization;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Platform;

namespace Ipc.Application.Sportmaster.Desktop;

public sealed partial class SupplyStationPositionViewModel
{
    bool PreserveDetailedResult => HasUncertainOriginal || ShowConfirmedTask || CurrentTaskAvailable || IsProcessing ||
        !CameraNoCode && (Operation?.State == ParcelState.WcsRejected || Input?.Outcome == ParcelInputOutcome.Rejected);
    bool RcsUnavailableForDisplay => owner.Snapshot is { } snapshot && snapshot.Configuration.Effective.Wcs is { } binding &&
        !ParcelConnectionSetup.IsDeferred(snapshot.Configuration.Effective, binding.InstanceId) &&
        (snapshot.Health.SingleOrDefault(h => h.InstanceId == binding.InstanceId) is not { Ready: true } health ||
            health.ConfirmedAt > DateTimeOffset.UtcNow || health.ValidUntil <= DateTimeOffset.UtcNow);
    bool CenteredRcsPause => owner.Fresh && SupplyEnabled && !DisplayPaused && !PreserveDetailedResult && RcsUnavailableForDisplay;
    bool CenteredWaiting => owner.Fresh && SupplyEnabled && !DisplayPaused && !PreserveDetailedResult && !RcsUnavailableForDisplay &&
        !HasCurrentInput && owner.Snapshot?.Positions?.SingleOrDefault(p => p.Id == PositionId) is { } position &&
        (position.Admit || position.BarcodeInput?.Manual == true);
    bool CenteredNoBarcodeBlocked => owner.Fresh && !SupplyDisabled && !DisplayPaused && !PreserveDetailedResult &&
        (CameraNoCode || !HasCurrentInput) &&
        !CameraAwaitingRead && !CenteredWaiting && !CenteredRcsPause;
    bool CenteredSoftwarePause => owner.Fresh && SupplyEnabled && DisplayPaused && !PausedAttention;
    bool CurrentResultVisible => !CameraNoCode && !CameraObservationUnmatched && !unreadFeedback && !invalidReadFeedback;
    WcsDestinationResult? CurrentRcsRejection =>
        Operation is { State: ParcelState.WcsRejected, Wcs: { State: StageState.Rejected, Request.Binding.Contract: RcsRequestPolicy.Contract } stage } &&
        owner.Fresh && !SupplyDisabled && !InputUnadmitted && CurrentResultVisible && !HasUncertainOriginal &&
        stage.Replies.LastOrDefault() is { TransportSucceeded: true, ProtocolAcknowledged: true, BusinessAccepted: false,
            Destination: { Outcome: WcsDestinationOutcome.Rejected } destination } ? destination : null;
    public bool CenteredRcsRejection => CurrentRcsRejection is not null;
    ParcelInputSnapshot? CurrentInputException => CurrentResultVisible && InputUnadmitted &&
        Input is { Outcome: ParcelInputOutcome.Blocked or ParcelInputOutcome.Rejected } input ? input : null;
    bool CenteredOriginalAttention => owner.Fresh && !SupplyDisabled && HasUncertainOriginal;
    public bool ShowRcsGuidance => owner.Fresh && !SupplyDisabled && !CenteredRcsRejection &&
        CurrentInputException is null && !ParcelPresentation.RcsNotDelivered(Operation) &&
        (Arrival?.RecoveryReason is not null || CenteredOriginalAttention && Operation?.Wcs?.State is StageState.Unknown or StageState.Conflict);
    bool RcsResultConflict => Operation?.Wcs?.State == StageState.Conflict;
    public string RcsGuidanceTitle => RcsResultConflict ? UiText.Get("Work.ConflictLocked") : UiText.Get("Work.UnknownLocked");
    public string RcsGuidanceSummary => RcsResultConflict ? UiText.Get("Work.ConflictDoNotUse") :
        ParcelPresentation.RcsTimedOut(Operation) ? UiText.Get("Work.TimeoutNoResend") :
        UiText.Get("Work.UnknownNoResend");
    public string RcsGuidanceBarcode => ShowRcsGuidance && CurrentRound is { } round
        ? SingleDisplayCode(Operation is { } operation && operation.RoundId == round.Id ? operation.ParcelCode : round.SelectedCode)
        : "";
    public string RcsGuidanceBarcodeSuffix
    {
        get
        {
            var barcode = new StringInfo(RcsGuidanceBarcode);
            return barcode.LengthInTextElements switch
            {
                0 => "",
                < 4 => UiText.Get("Work.FullBarcodePrefix") + barcode.String,
                _ => UiText.Get("Work.LastFourPrefix") + barcode.SubstringByTextElements(barcode.LengthInTextElements - 4)
            };
        }
    }
    public string RcsGuidanceSecondStep => RcsGuidanceBarcode.Length == 0 ? UiText.Get("Work.UnknownStepKeep") :
        new StringInfo(RcsGuidanceBarcode).LengthInTextElements < 4 ? UiText.Get("Work.UnknownStepFull") : UiText.Get("Work.UnknownStepFour");

    bool CenteredReadAttention => !RoundReadActive && owner.Fresh && !SupplyDisabled && (!DisplayPaused || PausedAttention) &&
        (invalidReadFeedback || CurrentInputException is not null);
    bool MissingConfirmedSlot => CurrentTaskAvailable && !SlotAvailable;
    bool CenteredOperationAttention => owner.Fresh && !SupplyDisabled && CurrentResultVisible && !InputUnadmitted &&
        (!DisplayPaused || PausedAttention) && (MissingConfirmedSlot || Operation?.State is ParcelState.WcsRejected or ParcelState.UnpackRequired or ParcelState.Blocked);
    bool CenteredStale => !owner.Fresh && !SupplyDisabled;
    public bool CenteredAttention => CenteredStale || CenteredOriginalAttention || CenteredReadAttention || CenteredOperationAttention;
    ParcelPresentationTone CenteredAttentionTone => CenteredStale ? ParcelPresentationTone.Stale :
        CenteredOriginalAttention || invalidReadFeedback || CenteredOperationAttention && (MissingConfirmedSlot || Operation?.State == ParcelState.UnpackRequired)
            ? ParcelPresentationTone.Unknown : ParcelPresentationTone.Rejected;

    string OriginalAttentionTitle => CurrentInputException switch
    {
        { Outcome: ParcelInputOutcome.Rejected } input => ParcelPresentation.InputText(input),
        { Outcome: ParcelInputOutcome.Blocked } blocked => AdmissionMessage(blocked).Title,
        _ => ParcelPresentation.RcsNotDelivered(Operation) ? UiText.Get("Work.ChuteRequestNotDelivered") : Operation?.Wcs?.State switch
        {
            StageState.Conflict => UiText.Get("Work.ChuteResultConflict"),
            StageState.Unknown => UiText.Get("Work.ChuteResultUnconfirmed"),
            _ => UiText.Get("Work.TaskResultUnconfirmed")
        }
    };
    string OriginalAttentionReason => CameraNoCode ? UiText.Get("Work.CheckingTray") : CurrentInputException switch
    {
        { Outcome: ParcelInputOutcome.Rejected } => UiText.Get("Work.ScanUnsent"),
        { Outcome: ParcelInputOutcome.Blocked } => UiText.Get("Work.ScanUnsent"),
        _ => ParcelPresentation.RcsTimedOut(Operation) ? UiText.Get("Work.ChuteResponseTimeout") : UiText.Get("Work.NoChuteAvailable")
    };
    string OriginalAttentionInstruction => CanConfirmPlacementRemoval ? UiText.Get("Work.TakeOffThenConfirm") :
        CurrentInputException is { Outcome: ParcelInputOutcome.Blocked } blocked ? AdmissionMessage(blocked).Instruction :
        DisplayPaused ? UiText.Get("Work.RemoveUntilResume") :
        RcsUnavailableForDisplay ? UiText.Get("Work.RemoveUntilRcs") :
        UiText.Get("Work.RemoveWhileOffline");
    string ReadAttentionTitle => invalidReadFeedback ? UiText.Get("Work.StaleRead") :
        CurrentInputException is { Outcome: ParcelInputOutcome.Blocked } blocked ? AdmissionMessage(blocked).Title : ParcelPresentation.InputText(CurrentInputException!);
    string ReadAttentionInstruction => DisplayPaused ? UiText.Get("Work.RemoveTrayPaused") : SupplyDisabling ? UiText.Get("Work.DisablingNoInput") :
        RcsUnavailableForDisplay ? UiText.Get("Work.RemoveUntilRcsShort") : invalidReadFeedback ? UiText.Get("Work.RescanWithoutDelivery") :
        CurrentInputException is { Outcome: ParcelInputOutcome.Blocked } blocked ? AdmissionMessage(blocked).Instruction :
        CurrentInputException?.Reason.StartsWith("ObservationExpired;", StringComparison.Ordinal) == true ? UiText.Get("Work.RemoveReload") :
        CanViewCurrentPicture ? UiText.Get("Work.ReviewImageReload") : UiText.Get("Work.ReviewBarcodeReload");
    string RcsRejectionInstruction()
    {
        var needsConfirmation = !SupplyDisabling && CanConfirmPlacementRemoval;
        var removal = needsConfirmation ? UiText.Get("Work.RemoveAndClick") : UiText.Get("Work.Remove");
        if (ParcelPresentation.RcsManualHandling(Operation))
            removal += UiText.Get("Work.ManualDestination");
        if (SupplyDisabling) return removal + UiText.Get("Work.DisableNoInputSuffix");
        var blockers = new List<string>();
        if (DisplayPaused) blockers.Add(UiText.Get("Work.StillPaused"));
        if (RcsUnavailableForDisplay) blockers.Add(UiText.Get("Work.RcsNotRecovered"));
        if (Arrival?.RecoveryReason is not null) blockers.Add(UiText.Get("Work.MemberReview"));
        if (blockers.Count > 0) return removal + "\n" + string.Join("；", blockers) + UiText.Get("Work.ProcessThenLoad");
        return removal + (needsConfirmation ? UiText.Get("Work.ConfirmThenNext") : "");
    }
    bool InterruptedInputBlocksResume => owner.Snapshot?.BlockingReasons.Any(r => r.StartsWith("IngressInterrupted:", StringComparison.Ordinal)) == true;
    public string PauseReason => InterruptedInputBlocksResume ? UiText.Get("Work.RecoveryUnavailable") :
        RcsUnavailableForDisplay ? UiText.Get("Work.RcsStillUnavailable") :
        PausedReadBlocked ? UiText.Get("Work.ScannedNotRequested") : UiText.Get("Work.NewParcelHeld");
    public string PauseInstruction => InterruptedInputBlocksResume
        ? UiText.Get("Work.MaintenanceDataInstruction")
        : owner.CanResumeInViewport ? UiText.Get("Work.ResumeInstruction")
        : UiText.Get("Work.CheckEquipmentBeforeReload");
    bool AutoSuppressedWaiting => CurrentRound is null && owner.Snapshot?.Positions?.SingleOrDefault(p => p.Id == PositionId)?.AutomaticSuppressed == true;
    bool RoundReadActive => CurrentRound is { OperationId: null };
    bool RoundReadDisplay => owner.Fresh && RoundReadActive && Arrival is not { Confirmed: false } && Arrival?.RecoveryReason is null && !RcsUnavailableForDisplay;
    bool CenteredArrival => owner.Fresh && SupplyEnabled && !DisplayPaused && !PreserveDetailedResult &&
        !RcsUnavailableForDisplay && Arrival is { } arrival && (!arrival.Confirmed || arrival.RecoveryReason is not null || arrival.Submitted || arrival.PlacementClosed);
    public bool ShowCenteredState => ShowRcsGuidance || RoundReadActive || !ShowRecentChute && (CenteredArrival || CenteredAttention || SupplyDisabled || CenteredSoftwarePause || CenteredRcsPause || CenteredWaiting || CenteredNoBarcodeBlocked);
    public string CenteredStateTitle => RoundReadDisplay ? CurrentRound!.ReadOutcome == "ExecutionCapacityBlocked" ? UiText.Get("Work.InFlightFull") : CurrentRound.AutoExpired ? CurrentRound.SafeContinuation ? UiText.Get("Work.ContinueReading") : UiText.Get("Work.ReadIssueManual") : UiText.Get("Work.Reading") : CenteredStale ? UiText.Get("Work.StatusReadInterrupted") : CenteredOriginalAttention ? OriginalAttentionTitle : CenteredReadAttention ? ReadAttentionTitle :
        CurrentRcsRejection is { } rejection ? string.IsNullOrWhiteSpace(rejection.Message) ? UiText.Get("Work.NoChuteFromRcs") : rejection.Message :
        CenteredOperationAttention ? MissingConfirmedSlot ? UiText.Get("Work.NoValidChute") : Operation?.State switch
        { ParcelState.UnpackRequired => UiText.Get("Work.NeedsUnpacking"), ParcelState.Blocked => UiText.Get("Work.CannotHandleParcel"), _ => UiText.Get("Work.ChuteNotAssigned") } :
        SupplyDisabled ? SupplyConfigured ? UiText.Get("Work.Disabled") : UiText.Get("Work.LaneNotConfigured") : Arrival?.RecoveryReason is not null ? UiText.Get("Work.RequestReview") : CenteredSoftwarePause ? UiText.Get("Work.InductionPaused") : CenteredRcsPause ? UiText.Get("Work.LoadUnavailable") : Arrival is { Confirmed: false } ? UiText.Get("Work.WaitVehicle") : Arrival is { PlacementClosed: true } ? UiText.Get("Work.WaitRemovalConfirmation") : Arrival is { Submitted: true, SubmissionAttempted: false } && CurrentRound is null ? UiText.Get("Work.OriginalClosing") : Arrival is { Submitted: true } && CurrentRound is null ? UiText.Get("Work.WaitRemoval") :
        CenteredNoBarcodeBlocked ? SupplyDisabling ? UiText.Get("Work.DisablePending") : UiText.Get("Work.LoadUnavailable") : CenteredWaiting && CurrentReaderState?.IsValid(DateTimeOffset.UtcNow) != true ? UiText.Get("Work.CameraManualAvailable") : UiText.Get("Work.WaitingToLoad");
    public bool ShowsWaitingToLoad => !CenteredRcsRejection && CenteredStateTitle == UiText.Get("Work.WaitingToLoad");
    public string CenteredStateForegroundKey => CenteredAttention ? CenteredAttentionTone switch
    { ParcelPresentationTone.Stale => "Muted", ParcelPresentationTone.Unknown => "OperatorWarning", _ => "OperatorDanger" }
        : CenteredRcsPause ? "OperatorDanger" : SupplyDisabled || CenteredSoftwarePause || CenteredNoBarcodeBlocked || CenteredArrival ? "Muted" : "OperatorSuccess";
    public string CenteredStateReason => RoundReadDisplay ? CurrentRound!.ReadOutcome == "ExecutionCapacityBlocked" ? UiText.Get("Lane.PriorCall") : CurrentRound.FormalCodes.Length > 1 ? UiText.Get("Lane.Multiple") : UiText.Get("Lane.NoBarcode") : CenteredStale ? UiText.Get("Work.OldChuteInvalid") : CenteredOriginalAttention ? OriginalAttentionReason :
        CenteredReadAttention ? invalidReadFeedback ? UiText.Get("Lane.InvalidChute") : UiText.Get("Work.ScanUnsent") :
        CenteredRcsRejection ? "" : CenteredOperationAttention ? Operation?.State == ParcelState.UnpackRequired ? UiText.Get("Lane.ManualInspection") : UiText.Get("Lane.ChuteUnavailable") :
        SupplyDisabled ? SupplyConfigured ? UiText.Get("Lane.NoScan") : "" : Arrival?.RecoveryReason is { } recovery ? recovery : Arrival is { PlacementClosed: true } ? UiText.Get("Lane.PlacementEnded") : Arrival is { Submitted: true, SubmissionAttempted: false } && CurrentRound is null ? UiText.Get("Lane.AcceptedNotSent") : CenteredSoftwarePause ? PauseReason : CenteredRcsPause ? UiText.Get("Lane.RcsOffline") : Arrival is { Confirmed: false } ? UiText.Get("Lane.VehicleChecking") :
        CenteredNoBarcodeBlocked ? SupplyDisabling ? UiText.Get("Lane.Finalizing") : Operation?.State == ParcelState.ResolvingWcs ? UiText.Get("Lane.WaitChute") : UiText.Get("Lane.NewCodesBlocked") :
        Arrival?.CaptureFailure is { } captureFailure ? captureFailure : Arrival is { CameraBoundaryReady: false } && CurrentReaderState?.IsValid(DateTimeOffset.UtcNow) == true ? UiText.Get("Lane.CameraBoundary") :
        AutoSuppressedWaiting ? UiText.Get("Lane.AutoReadUsed") : CurrentReaderState?.IsValid(DateTimeOffset.UtcNow) == true ? UiText.Get("Work.ReadByPlacing") : UiText.Get("Work.TypeFullCode");
    public string CenteredStateInstruction => CenteredStateFullInstruction;
    public string CenteredStateFullInstruction => RoundReadDisplay ? CurrentRound!.ReadOutcome == "ExecutionCapacityBlocked" ? UiText.Get("Lane.WaitForCall") : CurrentRound.AutoExpired ? CurrentRound.SafeContinuation ? UiText.Get("Lane.SlowerReading") : UiText.Get("Lane.PdaOrRemove") : UiText.Get("Lane.AdjustLabel") : CenteredStale ? UiText.Get("Lane.WaitForLiveStatus") :
        CenteredOriginalAttention ? OriginalAttentionInstruction : CenteredReadAttention ? ReadAttentionInstruction :
        CenteredOperationAttention ? MissingConfirmedSlot ? UiText.Get("Lane.CheckDetailsCall") : Operation?.State switch
        { ParcelState.UnpackRequired => UiText.Get("Lane.Unpack"), ParcelState.Blocked => UiText.Get("Lane.CheckDetails"), _ => RcsRejectionInstruction() } :
        SupplyDisabled ? !SupplyConfigured ? UiText.Get("Lane.SetCameraCode") : Arrival?.RecoveryReason ?? UiText.Get("Lane.WaitForVehicleRemoval") : Arrival?.RecoveryReason is not null ? UiText.Get("Lane.CheckOriginalRcs") : Arrival is { PlacementClosed: true } ? UiText.Get("Lane.WaitEmptyTray") : Arrival is { Submitted: true } && CurrentRound is null ? UiText.Get("Lane.RemoveThenContinue") : CenteredSoftwarePause ? PauseInstruction : CenteredRcsPause ? UiText.Get("Lane.ReconnectThenSubmit") : Arrival is { Confirmed: false } ? UiText.Get("Lane.VehicleThenCurrent") :
        CenteredNoBarcodeBlocked ? SupplyDisabling ? UiText.Get("Lane.AutoDisable") : Operation?.State == ParcelState.ResolvingWcs ? UiText.Get("Lane.WaitWithNextParcel") : UiText.Get("Lane.CheckLane") :
        AutoSuppressedWaiting ? UiText.Get("Lane.WaitForRemoval") : CurrentReaderState?.IsValid(DateTimeOffset.UtcNow) == true ? UiText.Get("Work.ScanGunOrKeyboard") : "";

    static (string Title, string Instruction) AdmissionMessage(ParcelInputSnapshot input) => ParcelInputReasons.BlockReason(input) switch
    {
        ParcelInputBlockReason.VehicleUnconfirmed => (UiText.Get("Work.WaitVehicle"), UiText.Get("Lane.FollowLocalRecovery")),
        ParcelInputBlockReason.ArrivalCapturePending => (UiText.Get("Lane.WaitCameraVehicle"), UiText.Get("Lane.CameraBoundaryRestore")),
        ParcelInputBlockReason.ReaderConfirmationExpired => (UiText.Get("Lane.CameraUnconfirmed"), UiText.Get("Lane.WaitCamera")),
        ParcelInputBlockReason.RcsUnavailable => (UiText.Get("Lane.RcsNotReady"), UiText.Get("Lane.WaitRcs")),
        ParcelInputBlockReason.PreviousResultPending => (UiText.Get("Lane.LookingUpChute"), UiText.Get("Lane.WaitForLane")),
        ParcelInputBlockReason.PreviousResultUnconfirmed => (UiText.Get("Lane.ChuteUnconfirmed"), UiText.Get("Lane.RemoveAndFollow")),
        ParcelInputBlockReason.PreviousWorkBlocked => (UiText.Get("Lane.UnfinishedWork"), UiText.Get("Lane.ReadLaneDetails")),
        ParcelInputBlockReason.SoftwarePaused => (UiText.Get("Work.InductionPaused"), UiText.Get("Lane.ResumeThenLoad")),
        ParcelInputBlockReason.ManagementOpen => (UiText.Get("Lane.Diagnostics"), UiText.Get("Lane.AwaitDiagnostics")),
        ParcelInputBlockReason.PositionDisabled => (UiText.Get("Lane.DisabledOrDisabling"), UiText.Get("Lane.EnableThenLoad")),
        ParcelInputBlockReason.OperatorSessionExpired => (UiText.Get("Lane.VerifyStationSettings"), UiText.Get("Lane.VerifyLogin")),
        ParcelInputBlockReason.PositionMaintenance => (UiText.Get("Lane.LocalService"), UiText.Get("Lane.WaitRecovery")),
        ParcelInputBlockReason.StationMaintenance => (UiText.Get("Lane.WorkstationMaintenance"), UiText.Get("Lane.WaitForMaintenance")),
        ParcelInputBlockReason.StationStartingOrStopping => (UiText.Get("Lane.WorkstationNotReady"), UiText.Get("Lane.WaitForStartup")),
        ParcelInputBlockReason.CameraNotConfigured => (UiText.Get("Lane.CameraNotConfigured"), UiText.Get("Lane.CheckCameraSettings")),
        ParcelInputBlockReason.ConnectionNotConfigured => (UiText.Get("Lane.ConnectionNotConfigured"), UiText.Get("Lane.CheckConnectionSettings")),
        ParcelInputBlockReason.ConfigurationUnconfirmed => (UiText.Get("Lane.ConfigurationUnconfirmed"), UiText.Get("Lane.ConfirmConfiguration")),
        ParcelInputBlockReason.RestoreUnconfirmed => (UiText.Get("Lane.RestoreUnconfirmed"), UiText.Get("Lane.ConfirmRecovery")),
        ParcelInputBlockReason.InstallationNotReady => (UiText.Get("Lane.InstallationNotReady"), UiText.Get("Lane.CheckInstall")),
        ParcelInputBlockReason.RuntimeFault => (UiText.Get("Lane.RuntimeFault"), UiText.Get("Lane.RemoveNotify")),
        ParcelInputBlockReason.CapabilityUnavailable => (UiText.Get("Lane.CapabilityUnavailable"), UiText.Get("Lane.WaitDevice")),
        ParcelInputBlockReason.StorageUnavailable => (UiText.Get("Lane.StorageUnavailable"), UiText.Get("Lane.CheckStorage")),
        ParcelInputBlockReason.CapacityExhausted => (UiText.Get("Lane.CapacityReached"), UiText.Get("Lane.WaitCapacity")),
        ParcelInputBlockReason.ImageCapacityUnavailable => (UiText.Get("Lane.ImageCapacity"), UiText.Get("Lane.ImageRecovery")),
        ParcelInputBlockReason.InputOverload => (UiText.Get("Lane.ScanBacklog"), UiText.Get("Lane.RemoveReload")),
        ParcelInputBlockReason.InterruptedScan => (UiText.Get("Lane.InterruptedScan"), UiText.Get("Lane.CheckInterrupted")),
        ParcelInputBlockReason.IdentityConflict => (UiText.Get("Lane.IdentityConflict"), UiText.Get("Lane.CheckOriginal")),
        ParcelInputBlockReason.ExpiredIdentity => (UiText.Get("Lane.RepeatedScan"), UiText.Get("Work.RemoveReload")),
        ParcelInputBlockReason.CameraInspectionIsolated => (UiText.Get("Lane.DiagnosticScan"), UiText.Get("Lane.EndDiagnosticsThenReload")),
        ParcelInputBlockReason.ManualConditionsChanged => (UiText.Get("Lane.ConditionsChanged"), UiText.Get("Lane.VerifyParcel")),
        _ => (UiText.Get("Lane.NoRejectionReason"), UiText.Get("Lane.CheckLogs"))
    };
}

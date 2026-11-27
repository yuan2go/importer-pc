using Ipc.Presentation.Wpf;
using Ipc.Application.ParcelFlow.Desktop;
using System.Diagnostics;
using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.Sportmaster.Desktop;

public sealed partial class SupplyStationPositionViewModel
{
    ParcelReadFeedback? readFeedback;
    sealed record RecentResult(Guid Runtime, string Reader, string Round, string? ArrivalGeneration, string Operation, string Slot, string Barcode, string Task, long ShownTicks);
    RecentResult? recentResult;
    DateTimeOffset? PlacementClearedAt => owner.Snapshot?.Positions?.SingleOrDefault(p => p.Id == PositionId)?.PlacementClearedAt;
    ManualWorkCardExpiry? ExpiredManualResult => null;
    bool CameraObservationUnmatched => false;
    bool CameraAwaitingRead => false;
    // No-code is a frame fact, not a declaration that a parcel exists or has left.
    // Manual input has its own source and remains usable while the camera sees no code.
    bool CameraNoCode => false;
    DeviceState? fastReaderState;
    Guid fastReaderRuntime;
    DeviceState? CurrentReaderState
    {
        get
        {
            var published = owner.Snapshot?.Devices?.SingleOrDefault(d => d.DeviceId == ReaderId);
            return fastReaderState is { } fast && (published is null || fast.UpdatedAt >= published.UpdatedAt) ? fast : published;
        }
    }
    bool unreadFeedback => false;
    bool invalidReadFeedback => false;
    static string SingleDisplayCode(string? code) => code is { Length: > 0 and <= 4096 } && !code.Any(char.IsControl) ? code : "";
    string ObservedBarcode => CurrentRound?.SelectedCode ?? (CurrentRound is { } round ? string.Join(";", round.FormalCodes.Select(SingleDisplayCode)) : "");
    // Manual input remains available when the camera is offline, subject to the position's admission guards.
    bool ManualInputAvailable => owner.Snapshot?.Positions?.SingleOrDefault(p => p.Id == PositionId)?.BarcodeInput?.Manual == true;
    public string BarcodePlaceholder => !SupplyEnabled ? SupplyDisabling ? UiText.Get("Work.Disabling") : UiText.Get("Work.DisabledNoInput") : ReaderUnavailableForDisplay ? ManualInputAvailable ? UiText.Get("Work.CameraOfflineCanEnter") : UiText.Get("Work.CameraOfflinePlain") : (CurrentRound?.FormalCodes.Length ?? 0) > 1
        ? UiText.Get("Work.MultipleViewImage") : RoundReadActive ? CurrentRound!.AutoExpired ? CurrentRound.SafeContinuation ? UiText.Get("Work.RetryOrPda") : UiText.Get("Work.TimeoutPda") : UiText.Get("Work.ReadingAdjust") : HasCurrentInput ? UiText.Get("Work.RoundCode") : UiText.Get(CompactLocalizedGuidance ? "Work.CompactScanHint" : "Work.ScanOrType");
    public ParcelPictureQuery? CurrentPictureQuery => !owner.Fresh || CameraNoCode || CameraObservationUnmatched || unreadFeedback || IsProcessing ? null :
        Input is { Outcome: ParcelInputOutcome.Rejected or ParcelInputOutcome.Blocked, Observation.OperatorInput: null } unadmitted ? new(ObservationId: unadmitted.Observation.Id) : InputUnadmitted ? null :
        Input is { } resting && Operation is { } placed && ParcelInputReasons.IsSamePlacement(resting) && resting.OperationId == placed.Id ? new(OperationId: placed.Id) :
        Input is { } input && Operation is { } current && input.OperationId == current.Id &&
            (current.Acquisition is null || input.Observation.DeviceId == current.Acquisition.DeviceId &&
                input.Observation.Epoch == current.Acquisition.Epoch && input.Observation.SourceEventId == current.Acquisition.SourceEventId &&
                input.Observation.AdmissionId == current.Acquisition.AdmissionId) ? new(ObservationId: input.Observation.Id) :
        Input is null && Operation is { } operation ? new(OperationId: operation.Id) : null;
    public bool CanViewCurrentPicture => CurrentPictureQuery is not null;
    internal string? WorkDetailsOperationId =>
        CanReconcile && !ShowRecentChute && !(CameraNoCode && CenteredWaiting) ? Operation?.Id :
        ShowRcsGuidance && CurrentRound is null && owner.CanUseWorkspace && !owner.Busy ? Arrival?.TargetOperation : null;
    public bool CanViewWorkDetails => WorkDetailsOperationId is not null;
    public string PictureUnavailableReason => !owner.Fresh ? "SnapshotUnavailable" : CameraNoCode ? "NoCurrentCode" :
        CameraObservationUnmatched ? "CameraObservationAheadOfSnapshot" : unreadFeedback ? "ReadAheadOfSnapshot" :
        IsProcessing ? "Processing" : CurrentPictureQuery is null ? "NoPictureIdentity" : "Available";
    public string? BarcodeObservationKey => unreadFeedback ? ReadFeedbackKey : Input is { } input
        ? $"{owner.Snapshot?.RuntimeId}/{input.Observation.DeviceId}/{input.Observation.Epoch}/{input.Observation.Id}"
        : Operation is { } operation ? $"{owner.Snapshot?.RuntimeId}/{operation.Id}" : null;
    bool FastReaderBlocked => fastReaderState is { } state && (
        owner.Snapshot?.Devices?.SingleOrDefault(d => d.DeviceId == ReaderId) is not { } published ||
        published.Epoch != state.Epoch || published.UpdatedAt < state.UpdatedAt);
    bool ReaderUnavailableForDisplay => CurrentRound is null && owner.Fresh && (Input is not null || owner.Snapshot?.Positions?.SingleOrDefault(p => p.Id == PositionId)?.BarcodeInput?.Manual != true) && Input?.Observation.OperatorInput is null && !(unreadFeedback && readFeedback?.OperatorInput == true) &&
        (FastReaderBlocked || owner.Snapshot?.Devices?.SingleOrDefault(d => d.DeviceId == ReaderId)?.IsValid(DateTimeOffset.UtcNow) != true);
    bool OfflineCameraSurface => ReaderUnavailableForDisplay && !HasUncertainOriginal && Operation?.State != ParcelState.WcsRejected && Input?.Outcome != ParcelInputOutcome.Rejected;
    // Previous results are a bounded display-only summary; Operation and all
    // input/image/command identities continue to follow the current round.
    public bool SlotProvisional => false;
    public bool IsProcessing => CurrentRound?.State == SmRoundState.Submitted && !SupplyDisabled && owner.Fresh && !DisplayPaused && !ReaderUnavailableForDisplay && !HasUncertainOriginal &&
        !CameraNoCode && !invalidReadFeedback && (CurrentRound?.State == SmRoundState.Submitted);
    bool DisplayPaused => owner.Snapshot?.Paused == true;
    bool PausedAttention => DisplayPaused && (HasUncertainOriginal || !CameraNoCode && !CameraObservationUnmatched && !unreadFeedback &&
        (Input is { Outcome: ParcelInputOutcome.Rejected } rejected && !rejected.Reason.StartsWith("ObservationExpired;", StringComparison.Ordinal) || Operation?.State == ParcelState.WcsRejected));
    bool PausedReadBlocked => DisplayPaused && !CameraNoCode && !CameraObservationUnmatched && !unreadFeedback && Input?.Outcome == ParcelInputOutcome.Blocked;
    public bool ShowConfirmedTask => !SupplyDisabled && CurrentTaskAvailable && SlotAvailable && !unreadFeedback && !ReaderUnavailableForDisplay && !DisplayPaused && owner.ViewportReady;
    public bool ShowRecentChute => recentResult is { } recent && owner.Fresh && owner.ViewportReady &&
        !SupplyDisabled && !DisplayPaused && !RcsUnavailableForDisplay && Arrival?.RecoveryReason is null &&
        CurrentRound is null && Operation is null && Stopwatch.GetElapsedTime(recent.ShownTicks) < TimeSpan.FromSeconds(3);
    public string RecentChuteBarcode => ShowRecentChute ? UiText.Format("Work.PreviousBarcodeTask", recentResult!.Barcode, recentResult.Task) : "";
    public string? SuccessIdentity => ShowConfirmedTask ? $"{owner.Snapshot?.RuntimeId}/{ReaderId}/{Operation?.Id}/{Operation?.Wcs?.Request.Id}" : null;
    public bool DisplaySlotVisible => !ShowRcsGuidance && (ShowConfirmedTask || IsProcessing || ShowRecentChute);
    public string DisplaySlot => IsProcessing ? UiText.Get("Work.ProcessingDots") : ShowConfirmedTask ? Slot : ShowRecentChute ? recentResult!.Slot : "";
    public ParcelStatusPresentation DisplayPresentation => !owner.Fresh ? ParcelPresentation.Stale() : ShowRecentChute ? ParcelPresentation.Neutral(UiText.Get("Work.PreviousChute")) : CenteredAttention
        ? CenteredAttentionTone == ParcelPresentationTone.Unknown ? ParcelPresentation.Unknown(CenteredStateTitle) : ParcelPresentation.Rejected(CenteredStateTitle) :
        ShowCenteredState ? ParcelPresentation.Neutral(CenteredStateTitle) : DisplayPaused && !PausedAttention ? ParcelPresentation.Neutral(PausedReadBlocked ? UiText.Get("Work.ScanPaused") : UiText.Get("Work.InductionPaused")) : HasUncertainOriginal ? Presentation : OfflineCameraSurface ? ParcelPresentation.Neutral(ManualInputAvailable ? UiText.Get("Work.WaitInput") : UiText.Get("Work.WaitForAdmission")) :
        CameraAwaitingRead && !HasUncertainOriginal ? ParcelPresentation.Neutral(UiText.Get("Work.WaitRescan")) : invalidReadFeedback ? ParcelPresentation.Unknown(UiText.Get("Work.StaleRead")) : IsProcessing ? ParcelPresentation.Pending(UiText.Get("Work.Processing")) : CurrentTaskAvailable && !SlotAvailable ? ParcelPresentation.Unknown(UiText.Get("Work.NoChute")) : Presentation;
    public string DisplaySlotLabel => ShowRecentChute ? UiText.Get("Work.PreviousChuteReadOnly") : IsProcessing ? UiText.Get("Work.ChuteNotYetAvailable") : UiText.Get("Work.CurrentChuteCopy");
    public string ContinuityNotice => "";
    public string DisplayStatus => SupplyDisabled ? UiText.Get("Work.Disabled") : ShowRecentChute ? UiText.Get("Work.PreviousChuteStatus") : ShowConfirmedTask ? UiText.Get("Work.ChuteReceived") : !owner.Fresh ? UiText.Get("Work.StateExpired") : DisplayPaused && !PausedAttention ? PausedReadBlocked ? UiText.Get("Work.ScanPaused") : UiText.Get("Work.InductionPaused") : OfflineCameraSurface ? ManualInputAvailable ? UiText.Get("Work.WaitInput") : UiText.Get("Work.NoNewCode") : HasUncertainOriginal ? Operation?.State == ParcelState.WcsUnknown ? UiText.Get("Work.ChuteResultUnconfirmed") : Status :
        CameraAwaitingRead ? UiText.Get("Work.WaitRescan") : CameraNoCode ? CenteredStateTitle : invalidReadFeedback ? UiText.Get("Work.StaleRead") : IsProcessing ? UiText.Get("Work.ProcessingCurrent") : Operation?.State == ParcelState.WcsRejected ? UiText.Get("Work.NoChute") : Status;
    public string DisplayAttentionReason => CenteredAttention && !CenteredRcsRejection ? CenteredStateReason :
        SupplyDisabled || DisplayPaused && !PausedAttention ? PausedReadBlocked ? UiText.Get("Work.NotSentViewImage") : "" : OfflineCameraSurface ? ManualInputAvailable ? UiText.Get("Work.EnterBarcodeManually") : AttentionReason :
        CameraAwaitingRead ? UiText.Get("Work.ReconnectedScanning") :
        unreadFeedback ? UiText.Get("Work.ReadNotConfirmed") : AttentionReason;
    public string DisplayNextStep => ShowRecentChute ? CanAdmit ? UiText.Get("Work.NowCanScanLoad") : CanSubmitBarcode ? UiText.Get("Work.NowCanManual") : UiText.Get("Work.NowPrefix") + CenteredStateTitle : CenteredAttention ? CenteredStateInstruction : SupplyDisabled ? "" : DisplayPaused ? PausedAttention
        ? UiText.Get("Work.PausedPrefix") + (HasUncertainOriginal ? NextStep : Input?.Outcome == ParcelInputOutcome.Rejected ? UiText.Get("Work.CheckImageBeforeNext") : NextStep)
        : PauseInstruction : ShowConfirmedTask ? "" : OfflineCameraSurface ? ManualInputAvailable ? UiText.Get("Work.SubmitFullBarcode") : NextStep :
        invalidReadFeedback ? UiText.Get("Work.RescanPendingResult") : !HasUncertainOriginal && IsProcessing ? UiText.Get("Work.NoPreviousDelivery") :
        !HasUncertainOriginal && Input?.Reason.StartsWith("ObservationExpired;", StringComparison.Ordinal) == true ? UiText.Get("Work.SkippedRemoveReload") :
        !HasUncertainOriginal && Input is { Outcome: ParcelInputOutcome.Rejected, BarcodeSelection.SelectedCode: null } ? UiText.Get("Work.NoChuteCheckImage") : NextStep;
    public double DisplaySlotFontSize => IsProcessing ? UiTypography.Placeholder : ShowRecentChute ? UiTypography.ResponsiveSlotSize(recentResult!.Slot, owner.ChuteScale) : SlotFontSize;
    public string CameraReadFeedback => readFeedback is { } read && CurrentRound?.Observation?.Id == read.ObservationId && Stopwatch.GetElapsedTime(read.DetectedTicks) < TimeSpan.FromSeconds(1)
        ? UiText.Get("Work.ReadWaitingResult") : "";
    public string? ReadFeedbackIdentity => CurrentRound?.Observation?.Id == readFeedback?.ObservationId ? readFeedback?.ObservationId : null;
    public string? ReadFeedbackKey => ReadFeedbackIdentity is not null && readFeedback is { } read ? $"{read.RuntimeId}/{read.DeviceId}/{read.Epoch}/{read.ObservationId}" : null;
    public string? RejectionIdentity => !OfflineCameraSurface && (!CameraNoCode || HasUncertainOriginal) && !unreadFeedback && (InputFailed || Operation?.State is ParcelState.WcsRejected or ParcelState.WcsUnknown or ParcelState.UnpackRequired || Operation?.Wcs?.State == StageState.Conflict)
        ? $"{owner.Snapshot?.RuntimeId}/{ReaderId}/{Input?.Observation.Epoch}/{Input?.Observation.Id ?? Operation?.Id}" : null;
    internal void ApplyReadFeedback(ParcelReadFeedback value)
    {
        if (!SupplyEnabled) return;
        if (value.RuntimeId != owner.Snapshot?.RuntimeId || value.DeviceId != ReaderId || value.PositionId != PositionId ||
            value.DetectedTicks <= 0 || value.DetectedTicks > Stopwatch.GetTimestamp() ||
            readFeedback is { } old && value.DetectedTicks < old.DetectedTicks) return;
        // A short-lived cue may expire while its observed identity still leads the
        // business consumer. Keep that watermark for late snapshots/new windows.
        readFeedback = value;
        RetireRecentResultForNewRead();
        NotifySlotDisplay();
    }
    void RetireRecentResultForNewRead()
    {
        if (CurrentRound is null && readFeedback is { HasFormalCandidates: true, ArrivalGeneration: { } generation } &&
            generation == Arrival?.Generation && recentResult?.ArrivalGeneration is { } previousGeneration && generation != previousGeneration)
            recentResult = null;
    }
    internal void ApplyReaderFeedback(Guid runtime, DeviceState value)
    {
        if (runtime != owner.Snapshot?.RuntimeId || value.DeviceId != ReaderId ||
            fastReaderState is { } old && old.UpdatedAt > value.UpdatedAt) return;
        fastReaderState = value;
        fastReaderRuntime = runtime;
        Update(owner.Snapshot, DateTimeOffset.UtcNow, owner.Fresh, owner.ViewportReady);
    }
    internal void ApplyCameraReadFeedback(Guid runtime, ParcelCameraReadFeedback value) { }
    void UpdateSlotMemory(ParcelSnapshot? snapshot, DateTimeOffset now, bool fresh, bool viewportReady)
    {
        // Display invalidation hides the summary through ShowRecentChute, but
        // must preserve the first-shown watermark for this same operation.
        if (snapshot?.RuntimeId != recentResult?.Runtime || ReaderId != recentResult?.Reader ||
            CurrentRound is { } current && current.Id != recentResult?.Round)
            recentResult = null;
        if (ShowConfirmedTask && Operation is { } success && CurrentRound is { } round && recentResult?.Operation != success.Id)
            recentResult = new(snapshot!.RuntimeId, ReaderId, round.Id, round.ArrivalGeneration, success.Id, success.Slot!, success.ParcelCode ?? "", TaskId, Stopwatch.GetTimestamp());
        if (readFeedback?.RuntimeId != snapshot?.RuntimeId || readFeedback?.DeviceId != ReaderId) readFeedback = null;
        if (fastReaderRuntime != snapshot?.RuntimeId || fastReaderState?.DeviceId != ReaderId) fastReaderState = null;
        if (fastReaderState is { } state && snapshot?.Devices?.SingleOrDefault(d => d.DeviceId == ReaderId) is { } acknowledged &&
            acknowledged.Epoch == state.Epoch && acknowledged.UpdatedAt >= state.UpdatedAt) fastReaderState = null;
        if (snapshot?.ReadFeedbacks.SingleOrDefault(r => r.PositionId == PositionId) is { } read) ApplyReadFeedback(read);
        RetireRecentResultForNewRead(); // A newer stream cue may precede this snapshot's round ending.
    }
    void NotifySlotDisplay()
    {
        foreach (var (name, value) in new (string, object?)[] {
            (nameof(Arrival), Arrival), (nameof(SupplyEnabled), SupplyEnabled), (nameof(SupplyDisabling), SupplyDisabling), (nameof(SupplyDisabled), SupplyDisabled),
            (nameof(SupplyConfigured), SupplyConfigured), (nameof(CanChangeSupply), CanChangeSupply), (nameof(CanConfirmPlacementRemoval), CanConfirmPlacementRemoval), (nameof(ShowConfirmedPlacementRemoval), ShowConfirmedPlacementRemoval), (nameof(DisableLabel), DisableLabel), (nameof(EnableLabel), EnableLabel), (nameof(SupplyNotice), SupplyNotice), (nameof(SupplyStateText), SupplyStateText),
            (nameof(EnableBackgroundKey), EnableBackgroundKey), (nameof(EnableForegroundKey), EnableForegroundKey), (nameof(DisableBackgroundKey), DisableBackgroundKey), (nameof(DisableForegroundKey), DisableForegroundKey),
            (nameof(Barcode), Barcode), (nameof(HasCurrentInput), HasCurrentInput), (nameof(BarcodePlaceholder), BarcodePlaceholder),
            (nameof(BarcodeObservationKey), BarcodeObservationKey),
            (nameof(CanViewCurrentPicture), CanViewCurrentPicture), (nameof(CanViewWorkDetails), CanViewWorkDetails), (nameof(PictureUnavailableReason), PictureUnavailableReason), (nameof(CurrentTaskAvailable), CurrentTaskAvailable), (nameof(SlotAvailable), SlotAvailable), (nameof(Slot), Slot),
            (nameof(WaveNo), WaveNo), (nameof(TaskId), TaskId), (nameof(SkuId), SkuId), (nameof(ResponseQuantity), ResponseQuantity),
            (nameof(IsProcessing), IsProcessing), (nameof(SlotProvisional), SlotProvisional), (nameof(DisplaySlotVisible), DisplaySlotVisible), (nameof(DisplaySlot), DisplaySlot),
            (nameof(ShowRecentChute), ShowRecentChute), (nameof(RecentChuteBarcode), RecentChuteBarcode),
            (nameof(DisplayPresentation), DisplayPresentation), (nameof(SuccessIdentity), SuccessIdentity), (nameof(DisplaySlotLabel), DisplaySlotLabel), (nameof(ContinuityNotice), ContinuityNotice),
            (nameof(ShowCenteredState), ShowCenteredState), (nameof(CenteredAttention), CenteredAttention), (nameof(CenteredRcsRejection), CenteredRcsRejection), (nameof(CenteredStateTitle), CenteredStateTitle), (nameof(ShowsWaitingToLoad), ShowsWaitingToLoad), (nameof(CenteredStateForegroundKey), CenteredStateForegroundKey),
            (nameof(ShowRcsGuidance), ShowRcsGuidance), (nameof(RcsGuidanceTitle), RcsGuidanceTitle), (nameof(RcsGuidanceSummary), RcsGuidanceSummary),
            (nameof(RcsGuidanceBarcode), RcsGuidanceBarcode), (nameof(RcsGuidanceBarcodeSuffix), RcsGuidanceBarcodeSuffix), (nameof(RcsGuidanceSecondStep), RcsGuidanceSecondStep),
            (nameof(CenteredStateReason), CenteredStateReason), (nameof(CenteredStateInstruction), CenteredStateInstruction), (nameof(CenteredStateFullInstruction), CenteredStateFullInstruction),
            (nameof(DisplayStatus), DisplayStatus), (nameof(DisplayAttentionReason), DisplayAttentionReason), (nameof(DisplayNextStep), DisplayNextStep), (nameof(ShowConfirmedTask), ShowConfirmedTask),
            (nameof(DisplaySlotFontSize), DisplaySlotFontSize), (nameof(CameraReadFeedback), CameraReadFeedback), (nameof(ReadFeedbackIdentity), ReadFeedbackIdentity),
            (nameof(ReadFeedbackKey), ReadFeedbackKey), (nameof(RejectionIdentity), RejectionIdentity) })
        {
            if (published.TryGetValue(name, out var prior) && Equals(prior, value)) continue;
            published[name] = value; PropertyChanged?.Invoke(this, new(name));
        }
    }


}

using Ipc.Presentation.Wpf;
using System.ComponentModel;
using System.Windows;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Platform;

namespace Ipc.Application.Sportmaster.Desktop;

public sealed partial class SupplyStationViewModel : ParcelViewModel
{
    string selectedPage = "workstation";
    string inputTargetPosition = "";
    public string InputTargetPosition => inputTargetPosition;
    internal void SetInputTarget(string value)
    {
        if (inputTargetPosition == value) return;
        inputTargetPosition = value; Changed(nameof(InputTargetPosition)); Left.NotifyInputTarget(); Right.NotifyInputTarget();
    }
    bool isTight;
    double chuteScale = 1;
    public double ChuteScale { get => chuteScale; internal set { if (Math.Abs(chuteScale - value) < .001) return; chuteScale = value; Left.NotifyLayout(); Right.NotifyLayout(); } }
    public bool IsTight { get => isTight; set { if (isTight == value) return; isTight = value; Changed(); } }
    public SupplyStationPositionViewModel Left { get; }
    public SupplyStationPositionViewModel Right { get; }
    public string Station => Snapshot?.Configuration.Effective.StationId ?? UiText.Get("Work.WaitingConfiguration");
    readonly Dictionary<string, string> preferenceNotices = [];
    public string PreferenceNoticeText => string.Join("\n\n", preferenceNotices.Values);
    public bool HasPreferenceNotices => preferenceNotices.Count != 0;
    public void SetLanguagePreferenceNotice(string? message) => SetPreferenceNotice("language", message);
    internal void SetPreferenceNotice(string key, string? message)
    {
        if (string.IsNullOrEmpty(message))
        {
            if (!preferenceNotices.Remove(key)) return;
        }
        else
        {
            if (preferenceNotices.GetValueOrDefault(key) == message) return;
            preferenceNotices[key] = message;
        }
        Changed(nameof(PreferenceNoticeText)); Changed(nameof(HasPreferenceNotices));
    }
    public enum RcsConnectionState { Unknown, Deferred, Simulation, Offline, AwaitingConfirmation, Online }
    public RcsConnectionState RcsState
    {
        get
        {
            if (!Fresh || Snapshot is not { } s) return RcsConnectionState.Unknown;
            if (s.Configuration.Effective.Wcs is { } deferred && ParcelConnectionSetup.IsDeferred(s.Configuration.Effective, deferred.InstanceId)) return RcsConnectionState.Deferred;
            if (s.Configuration.Effective.Mode == RunMode.Simulation) return RcsConnectionState.Simulation;
            if (s.Configuration.Effective.Wcs is not { Contract: "SPORTMASTER-RCS-SORTER-v1" } binding) return RcsConnectionState.Deferred;
            var health = s.Health.SingleOrDefault(h => h.InstanceId == binding.InstanceId); var now = DateTimeOffset.UtcNow;
            if (health is null || health.ConfirmedAt > now || health.ValidUntil <= now || health.ValidUntil <= health.ConfirmedAt) return RcsConnectionState.Unknown;
            if (!health.Ready) return RcsConnectionState.Offline;
            return health.RemoteConfirmation == RemoteConnectionConfirmation.ResponseReceived ? RcsConnectionState.Online : RcsConnectionState.AwaitingConfirmation;
        }
    }
    public string RcsServiceStatus => RcsState switch
    {
        RcsConnectionState.Online => UiText.Get("Work.RcsOnlineDetail"),
        RcsConnectionState.Offline => UiText.Get("Work.RcsOfflineDetail"),
        RcsConnectionState.Simulation => UiText.Get("Work.RcsSimulationDetail"),
        RcsConnectionState.Deferred => UiText.Get("Work.RcsNotConfiguredDetail"),
        RcsConnectionState.AwaitingConfirmation => UiText.Get("Work.RcsConfirmDetail"),
        _ => UiText.Get("Work.RcsUnknownDetail")
    };
    public ParcelStatusPresentation RcsPresentation => RcsState switch
    {
        RcsConnectionState.Online => ParcelPresentation.Accepted(UiText.Get("Work.RcsOnline")),
        RcsConnectionState.Offline => ParcelPresentation.Rejected(UiText.Get("Work.RcsOffline")),
        RcsConnectionState.Simulation => ParcelPresentation.Neutral(UiText.Get("Work.RcsSimulated")),
        RcsConnectionState.Deferred => ParcelPresentation.Unknown(UiText.Get("Work.RcsNotConfigured")),
        _ => ParcelPresentation.Unknown(UiText.Get("Work.RcsPending"))
    };
    bool checkingRcs;
    public string RcsButtonText => checkingRcs ? UiText.Get("Work.RcsChecking") : RcsPresentation.Text;
    public bool CanCheckRcs => !checkingRcs && Fresh && Application is IStationRcsHeartbeat &&
        RcsState is not (RcsConnectionState.Deferred or RcsConnectionState.Simulation);
    internal async Task CheckRcsAsync()
    {
        if (!CanCheckRcs || Application is not IStationRcsHeartbeat heartbeat) return;
        checkingRcs = true;
        Changed(nameof(CanCheckRcs)); Changed(nameof(RcsButtonText));
        try
        {
            var result = await ServiceAsync(() => heartbeat.CheckRcsHeartbeatAsync(CancellationToken.None));
            Message = result switch
            {
                RcsHeartbeatResult.Received => UiText.Get("Work.RcsReplyReceived"),
                RcsHeartbeatResult.Failed => UiText.Get("Work.RcsReplyMissing"),
                RcsHeartbeatResult.NoPositions => UiText.Get("Work.RcsMissingCameraCode"),
                RcsHeartbeatResult.Busy => UiText.Get("Work.RcsCheckWaiting"),
                _ => UiText.Get("Work.RcsCheckUnavailable")
            };
        }
        catch (Exception) { Message = UiText.Get("Work.RcsCheckIncomplete"); }
        finally { checkingRcs = false; Changed(nameof(CanCheckRcs)); Changed(nameof(RcsButtonText)); }
    }
    // Display copy only: never infer availability or remove the original blocking facts.
    public string WorkNotice => BlockingReasons;
    public bool IsInternalWorkspace => SelectedPage != "workstation";
    public bool IsSettingsNavigation => SelectedPage == "maintenance";
    public bool IsSettingsWorkspace => SelectedPage is "maintenance" or "logs";
    public string SettingsRunMode => (Snapshot?.Configuration.Effective.Mode switch
    { RunMode.Simulation => UiText.Get("Work.Simulation"), RunMode.Production => UiText.Get("Work.Production"), _ => UiText.Get("Work.ModeUnknown") }) + UiText.Get("Work.SafetySuffix");
    public string WorkspaceStatusBackground => OperatorTokens.Neutral;
    internal void ReportBarcodeFeedback(ParcelBarcodeReceipt receipt, string value) => SetPresentationMessage(value, receipt.Status == "Admitted" ? PresentationFeedbackKind.ResultMirrored : PresentationFeedbackKind.General);
    public string WorkFeedback => SelectedPage == "maintenance" ? ChineseInterfaceText.ParameterMessage(TaskFeedback) : TaskFeedback;
    // This release hides page entrances only; the business ledger and retained pages remain.
    public bool ShowExceptionAndStatisticsEntries => false;
    public Visibility ExceptionAndStatisticsEntryVisibility => ShowExceptionAndStatisticsEntries ? Visibility.Visible : Visibility.Collapsed;
    public string SelectedPage
    {
        get => selectedPage;
        set { if (value is not ("workstation" or "history" or "maintenance" or "diagnostics" or "logs" or "debug" or "statistics" or "exceptions")) throw new ArgumentException(UiText.Get("Work.NotInstalledPage")); if (selectedPage == value) return; selectedPage = value; Changed(); Changed(nameof(PageTitle)); Changed(nameof(WorkFeedback)); Changed(nameof(IsInternalWorkspace)); Changed(nameof(IsSettingsNavigation)); Changed(nameof(IsSettingsWorkspace)); Changed(nameof(WorkspaceStatusBackground)); }
    }
    public string PageTitle => SelectedPage switch { "statistics" => UiText.Get("Work.Stats"), "exceptions" => UiText.Get("Work.Exceptions"), "history" => UiText.Get("Work.NavRecords"), "maintenance" => UiText.Get("Common.Settings"), "diagnostics" => UiText.Get("Work.DeviceDiagnostics"), "debug" => UiText.Get("Work.Debug"), "logs" => UiText.Get("Work.Log"), _ => UiText.Get("Work.NavWork") };
    public SupplyStationViewModel(IParcelApplication application) : base(application, UiText.Get("Work.Station"), UiText.Get("Work.Flow"))
    {
        Left = new(this, "left"); Right = new(this, "right");
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(StationDisplaySummary)) Changed(nameof(Station));
            if (e.PropertyName is nameof(IsCompact) or nameof(IsTight) or nameof(Busy)) { Left.NotifyLayout(); Right.NotifyLayout(); }
            if (e.PropertyName == nameof(SelectedPage)) SetFeedbackContext(SelectedPage);
            if (e.PropertyName == nameof(TaskFeedback)) Changed(nameof(WorkFeedback));
            if (e.PropertyName is nameof(Busy) or nameof(IsManagementMode)) NotifyOperatorWorkspace();
        };
    }
    internal void RefreshLanguage()
    {
        Changed(null);
        Left.RefreshLanguage();
        Right.RefreshLanguage();
    }

    protected override void SnapshotValidityChanged(DateTimeOffset now)
    {
        Left.Update(Snapshot, now, Fresh, ViewportReady); Right.Update(Snapshot, now, Fresh, ViewportReady);
        NotifyOperatorWorkspace();
        foreach (var name in new[] { nameof(Station), nameof(WorkNotice), nameof(WorkspaceStatusBackground), nameof(SettingsRunMode), nameof(RcsState), nameof(RcsServiceStatus), nameof(RcsPresentation), nameof(RcsButtonText), nameof(CanCheckRcs) }) Changed(name);
    }
    public Task RefreshDisplayAsync() => RefreshAsync();
    public async Task ReconcilePositionAsync(SupplyStationPositionViewModel position)
    {
        var op = position.Operation;
        if (!CanStartOperation || !CanUseWorkspace || op is null) { Message = UiText.Get("Work.LaneNotReady"); return; }
        Selected = op;
        await RunAsync(async () => { var result = await ServiceAsync(() => Application.ReconcileAsync(op.Id)); Message = result.Status + " · " + result.Reason; }, UiText.Get("Work.QueryLane"));
    }
    public async Task ContinuePositionAsync(SupplyStationPositionViewModel position)
    {
        var op = position.Operation;
        if (!CanUseWorkspace || op is null || !position.CanContinue || Snapshot is not { } displayed) { Message = UiText.Get("Work.CannotContinue"); return; }
        var inspection = position.InspectionReason;
        await RunAsync(async () =>
        {
            var result = await ServiceAsync(() => Application.CommandAsync(new(Wire.Id(), displayed.Revision, "ContinuePrepared", op.Id, inspection)));
            Message = result.Status + " · " + result.Reason;
        }, UiText.Get("Work.ContinueLane"));
    }
    internal async Task CalibrateEmptyTrayAsync(SupplyStationPositionViewModel position, bool stationDebug)
    {
        await RequireManagementModeAsync();
        var state = CameraAcquisitionPresentation.Calibration(this, position, stationDebug);
        if (!state.CanStart || Snapshot is not { } snapshot) throw new InvalidOperationException(state.Detail);
        var command = new BusinessCommand(Wire.Id(), snapshot.Revision, "CalibrateEmptyTray", position.ReaderId,
            stationDebug ? "StationDebug" : "DisabledLane");
        var result = await ServiceAsync(() => Application.CommandAsync(command));
        if (result.Status != "Persisted")
            Message = UiText.Get("Debug.CalibrationRejected");
        await RefreshAsync();
    }
    long historyOpenGeneration;
    public async Task OpenHistoryAsync(string? operationId = null)
    {
        var generation = Interlocked.Increment(ref historyOpenGeneration); var runtime = Snapshot?.RuntimeId;
        if (operationId is not null) Selected = HistoryItems.FirstOrDefault(x => x.Operation.Id == operationId)?.Operation
            ?? Snapshot?.Operations.FirstOrDefault(x => x.Id == operationId);
        var selection = SelectionGeneration;
        await LoadHistoryAsync();
        if (operationId is null || generation != Volatile.Read(ref historyOpenGeneration) || Snapshot?.RuntimeId != runtime || selection != SelectionGeneration) return;
        var row = HistoryItems.FirstOrDefault(x => x.Operation.Id == operationId);
        if (row is not null) SelectedHistory = row;
        else
        {
            var detail = await ServiceAsync(() => Application.OperationAsync(operationId));
            if (generation != Volatile.Read(ref historyOpenGeneration) || Snapshot?.RuntimeId != runtime || selection != SelectionGeneration) return;
            Selected = detail;
            if (Selected is null) Message = UiText.Get("Work.ReadDetailFailed") + operationId;
        }
    }
    public async Task EmitPositionAsync(SupplyStationPositionViewModel position)
    {
        if (!CanUseWorkspace || !position.CanAdmit) { Message = UiText.Get("Work.ReferenceRejected"); return; }
        await RunAsync(() =>
        {
            var queued = Application.EmitReferenceAt(position.PositionId, position.AdmissionId, position.ParcelCode, Wire.Id());
            SetPresentationMessage(queued ? UiText.Get("Work.ReferenceAccepted") : UiText.Get("Work.ReferenceNotAccepted"), queued ? PresentationFeedbackKind.ObservationQueued : PresentationFeedbackKind.General);
            return Task.CompletedTask;
        }, UiText.Get("Work.SimulateRead"));
    }
}

// Stable objects retain each window's drafts and selection when snapshots are refreshed.
public sealed partial class SupplyStationPositionViewModel : INotifyPropertyChanged
{
    readonly string requestedPosition;
    readonly SupplyStationViewModel owner;
    public event PropertyChangedEventHandler? PropertyChanged;
    public string PositionId { get; private set; }
    public string Heading => requestedPosition == "left" ? UiText.Get("Work.LeftLane") : UiText.Get("Work.RightLane");
    StationVehicleState? vehicle;
    public string VehicleNumberText => vehicle?.VehicleNo ?? UiText.Get("Work.Vehicle");
    public string VehicleStatusText => vehicle is null ? UiText.Get("Work.VehiclePending") : VehicleNumberText + " · " + (vehicle.Status switch
    { "IDLE" => CanAdmit || CanSubmitBarcode ? UiText.Get("Work.CanLoad") : UiText.Get("Work.VehicleNotReady"), "TASKING" => UiText.Get("Work.VehicleBusy"), _ => UiText.Get("Common.Pending") });
    public string CompactVehicleStatusText => vehicle?.Status switch { "IDLE" => CanAdmit || CanSubmitBarcode ? UiText.Get("Work.CanLoad") : UiText.Get("Work.VehicleNotReady"), "TASKING" => UiText.Get("Work.VehicleBusy"), _ => UiText.Get("Common.Pending") };
    public string VehicleStatusForegroundKey => vehicle?.Status switch { "IDLE" when CanAdmit || CanSubmitBarcode => "OperatorSuccess", "TASKING" => "OperatorDanger", _ => "Muted" };
    public bool InputTargetActive => owner.InputTargetPosition == PositionId;
    internal void SetInputFocused(bool focused) { if (focused && !SupplyEnabled) return; if (focused || InputTargetActive) owner.SetInputTarget(focused ? PositionId : ""); }
    internal void RefreshLanguage() => PropertyChanged?.Invoke(this, new(null));
    internal void NotifyInputTarget() { PropertyChanged?.Invoke(this, new(nameof(InputTargetActive))); }
    public string ReaderId { get; private set; } = UiText.Get("Common.NotConfigured");
    public string ReaderIdentity { get; private set; } = UiText.Get("Common.NotConfigured");
    public string CameraAddress { get; private set; } = UiText.Get("Work.AddressMissing");
    public ParcelOperation? Operation { get; private set; }
    public ParcelInputSnapshot? Input { get; private set; }
    public bool InputUnadmitted => ParcelPresentation.IsUnadmitted(Input);
    public string LatestInputSummary => ParcelPresentation.InputSummary(Input);
    public bool InputFailed => (!RoundReadActive || CurrentRound?.AutoExpired == true) && ParcelPresentation.IsInputFailure(Input);
    public TrayPresenceModelSnapshot? TrayPresence => owner.Snapshot?.Positions?.SingleOrDefault(p => p.Id == PositionId)?.TrayPresence;
    public bool ShowUncalibratedHint => SupplyConfigured && TrayPresence is { Calibrated: false } &&
        DevicePresentation.Tone == ParcelPresentationTone.Accepted;
    public string CameraFooterText => ShowUncalibratedHint ? UiText.Get("Work.CameraNeedsTray") : DevicePresentation.Text;
    public string CameraFooterForegroundKey => ShowUncalibratedHint ? "OperatorWarning" : DeviceForegroundKey;
    public string DeviceForegroundKey => DevicePresentation.Tone switch
    {
        ParcelPresentationTone.Accepted => "Success",
        ParcelPresentationTone.Rejected => "Danger",
        ParcelPresentationTone.Unknown => "Warning",
        _ => "Muted"
    };
    public string? CameraFooterHint => ShowUncalibratedHint
        ? UiText.Get("Work.CalibrationAfterReconnect") : null;
    public SmArrivalSnapshot? Arrival => owner.Snapshot?.Positions?.SingleOrDefault(p => p.Id == PositionId)?.Arrival;
    public SmParcelRound? CurrentRound => owner.Snapshot?.Rounds.SingleOrDefault(r => r.Id == owner.Snapshot.Positions?.SingleOrDefault(p => p.Id == PositionId)?.CurrentRoundId);
    public bool HasCurrentInput => CurrentRound is not null;
    public bool IsCompact => owner.IsCompact;
    public bool IsTight => owner.IsTight;
    public bool CanStartOperation => owner.CanStartOperation;
    public string Barcode => ObservedBarcode;
    public bool CurrentTaskAvailable => owner.Fresh && CurrentRound is { } round && Operation?.RoundId == round.Id &&
        Operation.Wcs?.State == StageState.Accepted && round.SuccessAt is not null;
    public string Slot => !owner.Fresh ? UiText.Get("Work.OldChuteInvalid") : CurrentTaskAvailable ? string.IsNullOrWhiteSpace(Operation!.Slot) ? OperatorCopy.NotReturned : Operation.Slot : UiText.Get("Work.NotReceived");
    public string AttentionReason => !owner.Fresh ? UiText.Get("Work.StaleChute") :
        ParcelPresentation.RcsTimedOut(Operation) ? OperatorCopy.TimeoutUnconfirmed :
        CenteredRcsRejection ? CenteredStateTitle :
        CenteredAttention ? CenteredStateReason :
        InputUnadmitted ? ParcelPresentation.InputReason(Input!) :
        Operation is null ? IdleReason : "";
    string IdleReason => owner.Snapshot is not { } s ? UiText.Get("Work.WaitSnapshot") :
        s.Positions?.SingleOrDefault(p => p.Id == PositionId) is { Admit: true } ? "" :
        ParcelConnectionSetup.IsDeferred(s.Configuration.Effective, ReaderId) ? UiText.Get("Work.CameraDisabledManual") :
        s.Configuration.Effective.Wcs is { } w && ParcelConnectionSetup.IsDeferred(s.Configuration.Effective, w.InstanceId) ? UiText.Get("Work.RcsContact") :
        s.Paused ? OperatorCopy.Paused : s.Positions?.SingleOrDefault(p => p.Id == PositionId)?.Admit == true ? "" :
        UiText.Get("Work.UnavailableContact");
    public bool SlotAvailable => CurrentTaskAvailable && !string.IsNullOrWhiteSpace(Operation!.Slot);
    WcsDestinationResult? CurrentDestination => CurrentTaskAvailable
        ? Operation!.Wcs!.Replies.LastOrDefault(r => r.BusinessAccepted == true && r.RequestId == Operation.Wcs.Request.Id &&
            r.PayloadHash == Operation.Wcs.Request.PayloadHash)?.Destination : null;
    string TaskValue(string? value) => !owner.Fresh ? UiText.Get("Common.Expired") : !CurrentTaskAvailable ? "—" : string.IsNullOrWhiteSpace(value) ? OperatorCopy.NotReturned : value;
    public string WaveNo => TaskValue(CurrentDestination?.WaveNo);
    public string TaskId => TaskValue(CurrentDestination?.TaskId);
    public string SkuId => TaskValue(CurrentDestination?.SkuId);
    public string ResponseQuantity => TaskValue(CurrentDestination?.Quantity);
    public string RawCandidates => Input is null ? UiText.Get("Work.NoObservation") : Input.Observation.Codes.Length == 0 ? UiText.Get("Work.NoBarcodeDetected") : string.Join("\n", Input.Observation.Codes);
    public string SelectionSummary => Input?.BarcodeSelection is { } selection
        ? selection.Detail + UiText.Get("Work.ValidBarcodeSuffix") + selection.Matches
        : Input is null ? UiText.Get("Work.WaitCameraInput") : UiText.Get("Work.OldBarcodePolicy") + ParcelPresentation.InputText(Input);
    public string SelectedCode => Input?.BarcodeSelection?.SelectedCode ??
        (Input is { Outcome: ParcelInputOutcome.Admitted } && Operation is { } op && Input.OperationId == op.Id ? op.ParcelCode : UiText.Get("Work.NotSelected"));
    public string ScanTime => Input?.Observation.ObservedAt.ToLocalTime().ToString("HH:mm:ss.fff") ?? UiText.Get("Work.NoObservation");
    public string ScanSummary => Input is null ? UiText.Get("Work.ScanOrType") : (Input.Observation.OperatorInput is { } entry ? (entry.Kind == "PdaKeyboard" ? UiText.Get("Work.PdaInput") : UiText.Get("Work.KeyboardInput")) : UiText.Get("Work.CameraReadPrefix")) + ScanTime + UiText.Get("Work.BarcodeCount") + Input.Observation.Codes.Length + UiText.Get("Work.CountItems");
    public string ObservationIdentity => Input is null ? UiText.Get("Work.NoObservation") : UiText.Get("Work.CameraSession") + Input.Observation.Epoch + UiText.Get("Work.Sequence") + Input.Observation.SourceEventId;
    public double SlotFontSize => !SlotAvailable ? UiTypography.Placeholder : UiTypography.ResponsiveSlotSize(Operation!.Slot!, owner.ChuteScale);
    public bool NeedsAttention => !owner.Fresh || CurrentRound?.AutoExpired == true || HasUncertainOriginal || !RoundReadActive && (InputUnadmitted || Presentation.NeedsAttention);
    public string OperatorInstruction => !owner.Fresh ? OperatorCopy.StaleStep :
        InputUnadmitted ? ParcelPresentation.InputReason(Input!) :
        Operation?.State switch
        {
            ParcelState.WcsUnknown when ParcelPresentation.RcsNotDelivered(Operation) => UiText.Get("Work.RcsNotDelivered"),
            ParcelState.WcsUnknown when ParcelPresentation.RcsTimedOut(Operation) => OperatorCopy.TimeoutUnconfirmed,
            ParcelState.WcsUnknown => OperatorCopy.UnconfirmedStep,
            ParcelState.WcsRejected when ParcelPresentation.RcsManualHandling(Operation) => UiText.Get("Work.ManualDecision"),
            ParcelState.WcsRejected => ParcelPresentation.RcsException(Operation) ?? OperatorCopy.RejectedStep,
            ParcelState.UnpackRequired => UiText.Get("Work.UnpackNoReload"),
            ParcelState.ResolvingWcs or ParcelState.SlotResolved => "",
            _ => BlockingReasons.Length > 0 ? BlockingReasons : CanContinue ? UiText.Get("Work.UnpackReview") : ""
        };
    public ParcelStatusPresentation Presentation { get; private set; } = ParcelPresentation.Pending(UiText.Get("Common.Initializing"));
    public string Status => !owner.Fresh ? UiText.Get("Work.StatusInterrupted") : InputUnadmitted ? Input!.Outcome switch
    {
        ParcelInputOutcome.Rejected => ParcelPresentation.InputText(Input),
        ParcelInputOutcome.Blocked => AdmissionMessage(Input).Title,
        ParcelInputOutcome.Duplicate => UiText.Get("Work.DuplicateScan"),
        _ => Presentation.Text
    } : Operation?.Wcs?.State == StageState.Conflict ? UiText.Get("Work.ChuteConflict") : Operation?.State switch
    {
        ParcelState.SlotResolved => UiText.Get("Work.Destination"),
        ParcelState.ResolvingWcs => UiText.Get("Work.GettingChute"),
        ParcelState.WcsUnknown when ParcelPresentation.RcsNotDelivered(Operation) => UiText.Get("Work.NotDelivered"),
        ParcelState.WcsUnknown when ParcelPresentation.RcsTimedOut(Operation) => UiText.Get("Work.ChuteTimeout"),
        ParcelState.WcsUnknown => UiText.Get("Work.ChutePending"),
        ParcelState.WcsRejected => UiText.Get("Work.ChuteNotAssigned"),
        _ => Operation is null ? IdleReason.Length > 0 ? UiText.Get("Work.SupplyUnavailable") : UiText.Get("Work.WaitScanOrType") : Presentation.Text
    };
    public string OriginalResponsibility => InputUnadmitted && Operation is { } op ?
        UiText.Get("Work.RelatedTask") + op.Id + " · " + op.ParcelCode + " · " + ParcelPresentation.Operation(op, owner.Fresh).Text : "";
    public string DeviceStatus { get; private set; } = UiText.Get("Work.NoDeviceStatus");
    public ParcelStatusPresentation DevicePresentation { get; private set; } = ParcelPresentation.Unknown(UiText.Get("Work.CameraPendingStatus"));
    public string RcsServiceStatus => owner.RcsServiceStatus;
    public string DeviceLabel => DeviceStatus.Split(" · ", 2)[0];
    public string NextStep => !owner.Fresh ? OperatorCopy.StaleStep : InputUnadmitted ?
        OperatorCopy.RescanStep :
        SlotAvailable ? UiText.Get("Work.UseReturnedChute") :
        CurrentTaskAvailable ? UiText.Get("Work.RcsNoChuteSetAside") :
        Operation?.State == ParcelState.ResolvingWcs ? UiText.Get("Work.DoNotRescan") :
        ParcelPresentation.RcsNotDelivered(Operation) ? UiText.Get("Work.RcsNotDelivered") :
        ParcelPresentation.RcsTimedOut(Operation) ? OperatorCopy.TimeoutUnconfirmed :
        HasUncertainOriginal ? OperatorCopy.UnconfirmedStep :
        ParcelPresentation.RcsManualHandling(Operation) ? UiText.Get("Work.ManualDecision") :
        Operation?.State == ParcelState.WcsRejected ? OperatorCopy.RejectedStep :
        BlockingReasons.Length > 0 ? UiText.Get("Work.NotifyMaintenance") : UiText.Get("Work.LoadOrEnter");
    public string BlockingReasons { get; private set; } = UiText.Get("Work.WaitStatus");
    public string OperationId => Operation?.Id ?? UiText.Get("Work.NotReceived");
    public string Admission => InputUnadmitted ? Input!.Observation.AdmissionId : Operation?.AdmissionId ?? UiText.Get("Work.NotReceived");
    public string RequestId => Operation?.Wcs?.Request.Id ?? UiText.Get("Work.NotCreated");
    public string ResultReason => InputUnadmitted ? Input!.Reason : Operation is { } op
        ? string.Join("\n", new[] { op.Wcs?.Failure ?? op.Reason, ParcelViewModel.DestinationSummary(op) }.Where(s => !string.IsNullOrEmpty(s))) : "";
    public string ObservedAt => UiText.Get("Work.TaskScanPrefix") + (Operation?.ObservedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff zzz") ?? UiText.Get("Work.NoTask")) + UiText.Get("Work.OriginalTime") + (Operation?.Wcs?.Replies.LastOrDefault()?.At.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff zzz") ?? UiText.Get("Work.NoResponse"));
    public string ImageStatus => Operation?.Capture is { } c ? UiText.Get("Work.AcquisitionPrefix") + c.State + (Operation.Upload is { } u ? UiText.Get("Work.UploadSuffix") + u.State : "") : UiText.Get("Work.NoCapture");
    public bool CanAdmit { get; private set; }
    public bool CanSubmitBarcode => SupplyEnabled && owner.CanUseWorkspace && owner.CanStartOperation && owner.Snapshot?.Positions?.SingleOrDefault(p => p.Id == PositionId)?.BarcodeInput?.Manual == true;
    public bool CanReconcile { get; private set; }
    public bool CanQueryOriginal => owner.IsManagementMode && CanReconcile && HasUncertainOriginal;
    public bool HasUncertainOriginal => Operation is { } op &&
        (op.Wcs?.State is StageState.Unknown or StageState.Conflict || op.Capture?.State is StageState.Unknown or StageState.Conflict ||
            op.Upload?.State is StageState.Unknown or StageState.Conflict);
    public bool HasContinuation => Operation is { } op && owner.Snapshot?.Availability?.ContinuePrepared.Contains(op.Id) == true;
    public bool CanContinue { get; private set; }
    public ParcelOperation[] Recent { get; private set; } = [];
    public string InspectionReason { get; set; } = "";
    public string ParcelCode { get; set; } = "000123456789";
    public string AdmissionId { get; private set; } = "parcel-" + Wire.Id();
    public SupplyStationPositionViewModel(SupplyStationViewModel owner, string position)
    { this.owner = owner; requestedPosition = PositionId = position; }
    public void NewParcel() { AdmissionId = "parcel-" + Wire.Id(); PropertyChanged?.Invoke(this, new(nameof(AdmissionId))); }
    internal void NotifyLayout() { PropertyChanged?.Invoke(this, new(nameof(IsCompact))); PropertyChanged?.Invoke(this, new(nameof(IsTight))); PropertyChanged?.Invoke(this, new(nameof(SlotFontSize))); PropertyChanged?.Invoke(this, new(nameof(CanStartOperation))); PropertyChanged?.Invoke(this, new(nameof(CanSubmitBarcode))); NotifySlotDisplay(); }
    internal void Update(ParcelSnapshot? snapshot, DateTimeOffset now, bool fresh, bool viewportReady)
    {
        PositionId = requestedPosition;
        var position = snapshot?.Positions?.SingleOrDefault(x => x.Id == PositionId);
        var binding = snapshot is null ? null : ParcelBindings.Positions(snapshot.Configuration.Effective).SingleOrDefault(x => x.Id == PositionId);
        ReaderId = binding?.ReaderDeviceId ?? UiText.Get("Common.NotConfigured");
        var reader = snapshot is null ? null : ParcelBindings.Readers(snapshot.Configuration.Effective).SingleOrDefault(r => r.DeviceId == ReaderId);
        var extension = snapshot?.Configuration.Effective.Extensions?.Instances.SingleOrDefault(x => reader?.Driver == "extension:" + x.Id);
        var address = extension?.Parameters.GetValueOrDefault("device.ip");
        if (reader?.Driver == "tcp-line") address = reader.Parameters?.GetValueOrDefault("device.ip");
        CameraAddress = string.IsNullOrWhiteSpace(address) ? UiText.Get("Work.AddressMissing") : UiText.Get("Work.CameraAddress") + address;
        ReaderIdentity = string.IsNullOrWhiteSpace(address) ? ReaderId : address + " · " + ReaderId;
        var recent = snapshot?.Operations.Where(x => (x.PositionId ?? "main") == PositionId)
            .OrderByDescending(x => x.ObservedAt).ThenByDescending(x => x.Id, StringComparer.Ordinal).ToArray() ?? [];
        Input = snapshot?.Inputs?.SingleOrDefault(x => x.PositionId == PositionId);
        var previousOperation = Operation?.Id;
        // A named current operation must match both identities. Never fall back to another parcel.
        Operation = position?.CurrentOperation is { } id ? recent.FirstOrDefault(x => x.Id == id) : null;
        // The small recent grid displays only these facts. The command/detail owner
        // above always comes from the current authoritative snapshot, never this grid.
        if (!recent.Select(x => (x.Id, x.ParcelCode, x.State, x.Slot)).SequenceEqual(Recent.Select(x => (x.Id, x.ParcelCode, x.State, x.Slot)))) Recent = recent;
        if (Operation?.Id != previousOperation) { InspectionReason = ""; PropertyChanged?.Invoke(this, new(nameof(InspectionReason))); }
        var device = snapshot?.Devices?.SingleOrDefault(x => x.DeviceId == ReaderId);
        DevicePresentation = !fresh ? ParcelPresentation.Unknown(UiText.Get("Work.CameraInterrupted")) :
            snapshot is not null && ParcelConnectionSetup.IsDeferred(snapshot.Configuration.Effective, ReaderId) ? ParcelPresentation.Unknown(UiText.Get("Work.CameraNotConfigured")) : position?.CameraConnectionSuspended == true ?
            SupplyDisabled ? ParcelPresentation.Neutral(UiText.Get("Work.CameraDisconnectLane")) : ParcelPresentation.Unknown(UiText.Get("Work.CameraConnecting")) :
            FastReaderBlocked ? ParcelPresentation.Rejected(UiText.Get("Work.CameraRestoring")) :
            device is null ? ParcelPresentation.Unknown(UiText.Get("Work.CameraChecking")) :
            device.IsValid(now) ? snapshot?.Configuration.Effective.Mode == RunMode.Simulation
                ? ParcelPresentation.Neutral(UiText.Get("Work.CameraSimulation")) : ParcelPresentation.Accepted(UiText.Get("Work.CameraOnline")) :
            device.Health is DeviceHealth.Disconnected or DeviceHealth.Faulted or DeviceHealth.Stopped or DeviceHealth.Stopping
                ? ParcelPresentation.Rejected(UiText.Get("Work.CameraOffline")) : ParcelPresentation.Unknown(UiText.Get("Work.CameraChecking"));
        DeviceStatus = DevicePresentation.Text + (device?.ConfirmedAt is { } confirmed ? UiText.Get("Work.CameraLastConfirmed") + confirmed.ToLocalTime().ToString("HH:mm:ss") : "");
        vehicle = fresh && position?.Vehicle is { } status && status.ReceivedAt <= now && status.ValidUntil > now ? status : null;
        var identityMissing = position?.CurrentOperation is not null && Operation is null;
        CanAdmit = !FastReaderBlocked && !owner.Busy && fresh && viewportReady && binding is not null && !identityMissing && position?.Admit == true;
        CanReconcile = !owner.Busy && fresh && viewportReady && Operation is not null;
        CanContinue = !owner.Busy && fresh && viewportReady && Operation is { } op && snapshot?.Availability?.ContinuePrepared.Contains(op.Id) == true;
        BlockingReasons = !fresh ? UiText.Get("Work.StatusWaitingRecovery") : !viewportReady ? UiText.Get("Work.ViewportTooSmall") : binding is null ? UiText.Get("Work.LaneDisabled") :
            position is null ? UiText.Get("Work.LaneStatusReading") : identityMissing ? UiText.Get("Work.TaskConflictPrefix") + position.CurrentOperation + "）。\n" + OperatorCopy.JoinReasons(position.BlockingReasons.Select(ChineseInterfaceText.Notice), "\n") : OperatorCopy.JoinReasons(position.BlockingReasons.Select(ChineseInterfaceText.Notice), "\n");
        Presentation = !fresh ? ParcelPresentation.Stale() : binding is null ? ParcelPresentation.Neutral(UiText.Get("Work.NotEnabled")) :
            InputUnadmitted ? ParcelPresentation.InputPresentation(Input!) :
            position?.CurrentOperation is not null && Operation is null ? ParcelPresentation.Unknown(UiText.Get("Work.TaskConflictRereading")) : ParcelPresentation.Operation(Operation, true);
        UpdateSlotMemory(snapshot, now, fresh, viewportReady);
        NotifySlotDisplay();
        // A wildcard invalidation reassigns every TextBox/ItemsSource on every poll,
        // including local drafts. Publish only changed display facts, never drafts.
        foreach (var (name, value) in new (string, object?)[] {
            (nameof(PositionId), PositionId), (nameof(Heading), Heading), (nameof(ReaderId), ReaderId), (nameof(ReaderIdentity), ReaderIdentity), (nameof(CameraAddress), CameraAddress), (nameof(DevicePresentation), DevicePresentation), (nameof(DeviceForegroundKey), DeviceForegroundKey),
            (nameof(TrayPresence), TrayPresence), (nameof(CameraFooterText), CameraFooterText), (nameof(CameraFooterForegroundKey), CameraFooterForegroundKey), (nameof(CameraFooterHint), CameraFooterHint),
            (nameof(VehicleNumberText), VehicleNumberText), (nameof(VehicleStatusText), VehicleStatusText),
            (nameof(CompactVehicleStatusText), CompactVehicleStatusText), (nameof(VehicleStatusForegroundKey), VehicleStatusForegroundKey),
            (nameof(Barcode), Barcode), (nameof(Slot), Slot), (nameof(SlotAvailable), SlotAvailable), (nameof(NeedsAttention), NeedsAttention), (nameof(SlotFontSize), SlotFontSize),
            (nameof(CurrentTaskAvailable), CurrentTaskAvailable), (nameof(HasCurrentInput), HasCurrentInput), (nameof(AttentionReason), AttentionReason), (nameof(WaveNo), WaveNo), (nameof(TaskId), TaskId),
            (nameof(SkuId), SkuId), (nameof(ResponseQuantity), ResponseQuantity), (nameof(RawCandidates), RawCandidates),
            (nameof(SelectionSummary), SelectionSummary), (nameof(SelectedCode), SelectedCode), (nameof(ScanTime), ScanTime), (nameof(ScanSummary), ScanSummary), (nameof(ObservationIdentity), ObservationIdentity),
            (nameof(Presentation), Presentation), (nameof(Status), Status), (nameof(OperatorInstruction), OperatorInstruction),
            (nameof(OriginalResponsibility), OriginalResponsibility),
            (nameof(RcsServiceStatus), RcsServiceStatus), (nameof(DeviceStatus), DeviceStatus), (nameof(DeviceLabel), DeviceLabel), (nameof(BlockingReasons), BlockingReasons), (nameof(OperationId), OperationId),
            (nameof(Admission), Admission), (nameof(RequestId), RequestId), (nameof(ResultReason), ResultReason),
            (nameof(ObservedAt), ObservedAt), (nameof(LatestInputSummary), LatestInputSummary), (nameof(ImageStatus), ImageStatus),
            (nameof(CanAdmit), CanAdmit), (nameof(CanSubmitBarcode), CanSubmitBarcode), (nameof(CanReconcile), CanReconcile), (nameof(CanQueryOriginal), CanQueryOriginal), (nameof(CanContinue), CanContinue),
            (nameof(HasUncertainOriginal), HasUncertainOriginal), (nameof(HasContinuation), HasContinuation), (nameof(NextStep), NextStep),
            (nameof(Recent), Recent), (nameof(InspectionReason), InspectionReason) })
        {
            if (published.TryGetValue(name, out var prior) && Equals(prior, value)) continue;
            published[name] = value; PropertyChanged?.Invoke(this, new(name));
        }
    }
    readonly Dictionary<string, object?> published = [];
    public static string StageText(ParcelState state) => state switch
    {
        ParcelState.SlotResolved => UiText.Get("Work.ChuteReceived"),
        ParcelState.WcsUnknown => UiText.Get("Work.ChuteNoReload"),
        ParcelState.WcsRejected => UiText.Get("Work.ChuteNotAssigned"),
        ParcelState.UnpackRequired => UiText.Get("Work.UnpackRequired"),
        ParcelState.ResolvingWcs => UiText.Get("Work.GettingChute"),
        ParcelState.Prepared => UiText.Get("Work.Pending"),
        ParcelState.Blocked => UiText.Get("Work.Blocked"),
        ParcelState.WaitingWeight => UiText.Get("Work.WaitWeight"),
        ParcelState.WeightTimedOut => UiText.Get("Work.WeightTimeout"),
        ParcelState.ImageUnknown => UiText.Get("Work.UploadUnconfirmed"),
        ParcelState.ImageRejected => UiText.Get("Work.ImageRejected"),
        ParcelState.CapturingImage => UiText.Get("Work.ImageInProgress"),
        ParcelState.UploadingImage => UiText.Get("Work.UploadingOriginal"),
        ParcelState.Abandoned => UiText.Get("Work.CancelledUnsent"),
        _ => UiText.Get("Work.StagePrefix") + state
    };
}

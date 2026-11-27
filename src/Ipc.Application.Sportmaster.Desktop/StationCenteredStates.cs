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
    public string CenteredStateReason => RoundReadDisplay ? CurrentRound!.ReadOutcome == "ExecutionCapacityBlocked" ? "本轮尚未提交；本位旧调用仍在收尾" : CurrentRound.FormalCodes.Length > 1 ? "本轮有多个有效条码，尚未请求RCS" : "本轮尚未取得可提交条码，尚未请求RCS" : CenteredStale ? UiText.Get("Work.OldChuteInvalid") : CenteredOriginalAttention ? OriginalAttentionReason :
        CenteredReadAttention ? invalidReadFeedback ? "本次没有有效格口" : UiText.Get("Work.ScanUnsent") :
        CenteredRcsRejection ? "" : CenteredOperationAttention ? Operation?.State == ParcelState.UnpackRequired ? "本件需人工检查" : "本件暂不能分配格口" :
        SupplyDisabled ? SupplyConfigured ? "不接收条码" : "" : Arrival?.RecoveryReason is { } recovery ? recovery : Arrival is { PlacementClosed: true } ? "本车放置已结束，未发送请求" : Arrival is { Submitted: true, SubmissionAttempted: false } && CurrentRound is null ? "本件已接纳但未发送" : CenteredSoftwarePause ? PauseReason : CenteredRcsPause ? "RCS 服务离线" : Arrival is { Confirmed: false } ? "正在核对本位小车，输入已保留" :
        CenteredNoBarcodeBlocked ? SupplyDisabling ? "正在等待本轮请求及结果保存结束" : Operation?.State == ParcelState.ResolvingWcs ? "正在等待本件格口" : "本位暂不接收新条码" :
        Arrival?.CaptureFailure is { } captureFailure ? captureFailure : Arrival is { CameraBoundaryReady: false } && CurrentReaderState?.IsValid(DateTimeOffset.UtcNow) == true ? "正在确认相机新画面，可人工补录" :
        AutoSuppressedWaiting ? "本次自动识读机会已使用" : CurrentReaderState?.IsValid(DateTimeOffset.UtcNow) == true ? UiText.Get("Work.ReadByPlacing") : UiText.Get("Work.TypeFullCode");
    public string CenteredStateInstruction => CenteredStateFullInstruction;
    public string CenteredStateFullInstruction => RoundReadDisplay ? CurrentRound!.ReadOutcome == "ExecutionCapacityBlocked" ? "请等待本位在途调用收尾后重新提交；另一位仍可独立供包" : CurrentRound.AutoExpired ? CurrentRound.SafeContinuation ? "原包裹持续在位，正在降频识读；也可PDA补录或取下包裹" : "可原位PDA补录或取下；可靠空盘自动结束，视觉不可靠时确认取下" : "请调整标签，或使用PDA补录；本轮识读期限不会刷新" : CenteredStale ? "请等待状态恢复，暂勿按旧格口投递" :
        CenteredOriginalAttention ? OriginalAttentionInstruction : CenteredReadAttention ? ReadAttentionInstruction :
        CenteredOperationAttention ? MissingConfirmedSlot ? "本件暂勿投递，请查看详情并通知维护人员" : Operation?.State switch
        { ParcelState.UnpackRequired => "请查看拆包明细，处理完再继续", ParcelState.Blocked => "本件暂勿投递，请查看详情", _ => RcsRejectionInstruction() } :
        SupplyDisabled ? !SupplyConfigured ? "请前往“设置”填写本位相机编码后再启用" : Arrival?.RecoveryReason ?? "等待RCS撤车确认；启用后须确认新车" : Arrival?.RecoveryReason is not null ? "请停用本位，联系维护核对客户RCS原请求及调度状态" : Arrival is { PlacementClosed: true } ? "请取下包裹，等待可信空盘确认" : Arrival is { Submitted: true } && CurrentRound is null ? "请取下包裹结束本件，再处理下一件" : CenteredSoftwarePause ? PauseInstruction : CenteredRcsPause ? "请检查连接，恢复后再提交" : Arrival is { Confirmed: false } ? "确认后可处理当前件；同次放置不会重复请求" :
        CenteredNoBarcodeBlocked ? SupplyDisabling ? "本轮处理结束后自动停用，无需确认空托盘" : Operation?.State == ParcelState.ResolvingWcs ? "请等待结果后再上件" : "请核对本位状态，必要时通知维护人员" :
        AutoSuppressedWaiting ? "等待可信物理边界和本位安全准入，勿重复请求" : CurrentReaderState?.IsValid(DateTimeOffset.UtcNow) == true ? UiText.Get("Work.ScanGunOrKeyboard") : "";

    static (string Title, string Instruction) AdmissionMessage(ParcelInputSnapshot input) => ParcelInputReasons.BlockReason(input) switch
    {
        ParcelInputBlockReason.VehicleUnconfirmed => (UiText.Get("Work.WaitVehicle"), "请按本位到位或人工恢复提示处理，勿重复请求"),
        ParcelInputBlockReason.ArrivalCapturePending => ("等待相机换车确认", "相机边界完成后自动恢复；合法人工补录仍可用"),
        ParcelInputBlockReason.ReaderConfirmationExpired => ("相机连接未确认", "请等待相机自动恢复，再重新上件"),
        ParcelInputBlockReason.RcsUnavailable => ("RCS 连接未就绪", "请等待 RCS 连接恢复，再重新上件"),
        ParcelInputBlockReason.PreviousResultPending => ("本位正在获取格口", "请等待本位处理结束，再重新上件"),
        ParcelInputBlockReason.PreviousResultUnconfirmed => ("本位有格口结果尚未确认", "请取下包裹，按本位提示处理"),
        ParcelInputBlockReason.PreviousWorkBlocked => ("本位作业尚未结束", "请查看本位详情，处理后再上件"),
        ParcelInputBlockReason.SoftwarePaused => (UiText.Get("Work.InductionPaused"), "确认现场后恢复供包，再重新上件"),
        ParcelInputBlockReason.ManagementOpen => ("设备调试正在进行", "请联系运维人员结束设备调试后再上件"),
        ParcelInputBlockReason.PositionDisabled => ("本位已停用或正在停用", "请启用本投递位后再上件"),
        ParcelInputBlockReason.OperatorSessionExpired => ("本站配置需要核对", "SM 不使用操作员登录，请在工作站设置核对本站配置"),
        ParcelInputBlockReason.PositionMaintenance => ("本位设备正在维护", "请等待设备恢复后再上件"),
        ParcelInputBlockReason.StationMaintenance => ("供包台正在保存设置或备份", "请等待操作结束后再上件"),
        ParcelInputBlockReason.StationStartingOrStopping => ("供包台尚未就绪", "请等待供包台启动完成"),
        ParcelInputBlockReason.CameraNotConfigured => ("本位相机未配置", "请通知维护人员检查相机设置"),
        ParcelInputBlockReason.ConnectionNotConfigured => ("业务连接尚未配置", "请通知维护人员检查连接设置"),
        ParcelInputBlockReason.ConfigurationUnconfirmed => ("当前设置尚未确认生效", "请通知维护人员核对当前设置"),
        ParcelInputBlockReason.RestoreUnconfirmed => ("恢复的数据尚未核对", "请通知维护人员核对恢复结果"),
        ParcelInputBlockReason.InstallationNotReady => ("当前安装状态未就绪", "请通知维护人员检查安装状态"),
        ParcelInputBlockReason.RuntimeFault => ("供包台运行故障", "请取下包裹并通知维护人员"),
        ParcelInputBlockReason.CapabilityUnavailable => ("本位所需设备尚未就绪", "请等待设备恢复，持续出现时通知维护人员"),
        ParcelInputBlockReason.StorageUnavailable => ("供包台存储不可用", "请通知维护人员检查剩余空间和存储状态"),
        ParcelInputBlockReason.CapacityExhausted => ("当前作业已达容量上限", "请等待请求处理完成，或按本位提示确认包裹已取下"),
        ParcelInputBlockReason.ImageCapacityUnavailable => ("图片处理容量不足", "请等待本位恢复，持续出现时通知维护人员"),
        ParcelInputBlockReason.InputOverload => ("本位扫码积压", "请取下包裹，等待本位恢复后重新上件"),
        ParcelInputBlockReason.InterruptedScan => ("本位有中断的扫码记录", "请通知维护人员检查中断记录"),
        ParcelInputBlockReason.IdentityConflict => ("本次读码与原记录冲突", "请取下包裹并通知维护人员核对记录"),
        ParcelInputBlockReason.ExpiredIdentity => ("重复读到了旧扫码记录", UiText.Get("Work.RemoveReload")),
        ParcelInputBlockReason.CameraInspectionIsolated => ("本次读码来自相机调试", "结束调试后再重新上件"),
        ParcelInputBlockReason.ManualConditionsChanged => ("提交期间本位条件已变化", "请核对本位状态和当前包裹后再提交"),
        _ => ("本次扫码原因未记录", "请取下包裹，持续出现时通知维护人员查看日志")
    };
}

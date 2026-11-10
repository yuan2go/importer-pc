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
        (CameraNoCode || !HasCurrentInput && (ExpiredManualResult is not null || PlacementClearedAt is not null || CanConfirmPlacementRemoval)) &&
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
    bool CenteredReadAttention => owner.Fresh && !SupplyDisabled && (!DisplayPaused || PausedAttention) &&
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
        _ => ParcelPresentation.RcsNotDelivered(Operation) ? "取格口请求未送达" : Operation?.Wcs?.State switch
        {
            StageState.Conflict => "格口结果有冲突",
            StageState.Unknown => "格口结果未确认",
            _ => "作业结果未确认"
        }
    };
    string OriginalAttentionReason => CameraNoCode ? "正在确认托盘是否清空" : CurrentInputException switch
    {
        { Outcome: ParcelInputOutcome.Rejected } => "本次扫码未发送请求",
        { Outcome: ParcelInputOutcome.Blocked } => "本次扫码未发送请求",
        _ => ParcelPresentation.RcsTimedOut(Operation) ? "等待取格口响应超时" : "本件没有可用格口"
    };
    string OriginalAttentionInstruction => CanConfirmPlacementRemoval ? "请先取下包裹，再确认" :
        CurrentInputException is { Outcome: ParcelInputOutcome.Blocked } blocked ? AdmissionMessage(blocked).Instruction :
        DisplayPaused ? "请取下包裹，供包恢复后再上件" :
        RcsUnavailableForDisplay ? "请取下包裹，RCS 连接恢复后再上件" :
        "请取下包裹，等本位恢复后再上件\n多次出现请通知维护人员";
    string ReadAttentionTitle => invalidReadFeedback ? "本次读码已失效" :
        CurrentInputException is { Outcome: ParcelInputOutcome.Blocked } blocked ? AdmissionMessage(blocked).Title : ParcelPresentation.InputText(CurrentInputException!);
    string ReadAttentionInstruction => DisplayPaused ? "供包已暂停，请将包裹移出托盘" : SupplyDisabling ? "本位停用中，暂不接收新条码" :
        RcsUnavailableForDisplay ? "请取下包裹，RCS 恢复后再上件" : invalidReadFeedback ? "本件暂勿投递，请重新扫码" :
        CurrentInputException is { Outcome: ParcelInputOutcome.Blocked } blocked ? AdmissionMessage(blocked).Instruction :
        CurrentInputException?.Reason.StartsWith("ObservationExpired;", StringComparison.Ordinal) == true ? "请取下包裹后重新上件" :
        CanViewCurrentPicture ? "请查看图片核对条码，取下包裹后再上件" : "请核对包裹条码，取下包裹后再上件";
    string RcsRejectionInstruction()
    {
        var manual = ParcelPresentation.RcsManualHandling(Operation);
        var removal = manual ? "请取下包裹后人工判断" : "请取下包裹";
        if (SupplyDisabling) return removal + "\n停用中，暂不接收新条码";
        if (DisplayPaused) return removal + "\n供包恢复后再放下一件";
        if (RcsUnavailableForDisplay) return removal + "\nRCS 连接恢复后再放下一件";
        if (CanConfirmPlacementRemoval) removal += "，再确认";
        else if (CanAdmit || CanSubmitBarcode) removal += manual ? "，再放下一件" : "，可放下一件";
        return removal + "\n多次出现请通知维护人员";
    }
    bool InterruptedInputBlocksResume => owner.Snapshot?.BlockingReasons.Any(r => r.StartsWith("IngressInterrupted:", StringComparison.Ordinal)) == true;
    public string PauseReason => InterruptedInputBlocksResume ? "上次运行的数据无法自动恢复" :
        RcsUnavailableForDisplay ? "RCS 连接尚未恢复" :
        PausedReadBlocked ? "条码已读到，尚未请求格口" : "新包裹暂不受理";
    public string PauseInstruction => InterruptedInputBlocksResume
        ? "请联系维护人员检查运行数据，操作工无需进入管理中心。"
        : owner.CanResumeInViewport ? "确认现场后，点击顶部“恢复供包”。"
        : "请通知维护人员检查设备，恢复后再上件。";
    public bool ShowCenteredState => !ShowRecentChute && (CenteredAttention || SupplyDisabled || CenteredSoftwarePause || CenteredRcsPause || CenteredWaiting || CenteredNoBarcodeBlocked);
    public string CenteredStateTitle => CenteredStale ? "状态读取中断" : CenteredOriginalAttention ? OriginalAttentionTitle : CenteredReadAttention ? ReadAttentionTitle :
        CurrentRcsRejection is { } rejection ? string.IsNullOrWhiteSpace(rejection.Message) ? "RCS 未分配格口" : rejection.Message :
        CenteredOperationAttention ? MissingConfirmedSlot ? "未取得有效格口" : Operation?.State switch
        { ParcelState.UnpackRequired => "需要拆包检查", ParcelState.Blocked => "本件暂不能处理", _ => "未分配格口" } :
        SupplyDisabled ? "已停用" : CenteredSoftwarePause ? "供包已暂停" : CenteredRcsPause ? "暂不能投包" :
        CenteredNoBarcodeBlocked ? SupplyDisabling ? "停用中" : "暂不能投包" : "等待投包";
    public string CenteredStateForeground => CenteredAttention ? CenteredAttentionTone switch
    { ParcelPresentationTone.Stale => OperatorTokens.Muted, ParcelPresentationTone.Unknown => OperatorTokens.Warning, _ => OperatorTokens.Danger }
        : CenteredRcsPause ? OperatorTokens.Danger : SupplyDisabled || CenteredSoftwarePause || CenteredNoBarcodeBlocked ? OperatorTokens.Muted : OperatorTokens.Success;
    public string CenteredStateReason => CenteredStale ? "旧格口已失效" : CenteredOriginalAttention ? OriginalAttentionReason :
        CenteredReadAttention ? invalidReadFeedback ? "本次没有有效格口" : "本次扫码未发送请求" :
        CenteredRcsRejection ? "本件暂不能分配格口" : CenteredOperationAttention ? Operation?.State == ParcelState.UnpackRequired ? "本件需人工检查" : "本件暂不能分配格口" :
        SupplyDisabled ? "不接收条码" : CenteredSoftwarePause ? PauseReason : CenteredRcsPause ? "RCS 服务离线" :
        CenteredNoBarcodeBlocked ? SupplyDisabling ? "正在确认本次放置结束" : Operation?.State == ParcelState.ResolvingWcs ? "正在等待本件格口" : "本位暂不接收新条码" :
        CurrentReaderState?.IsValid(DateTimeOffset.UtcNow) == true ? "放上包裹，由相机扫码" : "可输入完整条码";
    public string CenteredStateInstruction => CenteredStateFullInstruction;
    public string CenteredStateFullInstruction => CenteredStale ? "请等待状态恢复，暂勿按旧格口投递" :
        CenteredOriginalAttention ? OriginalAttentionInstruction : CenteredReadAttention ? ReadAttentionInstruction :
        CenteredOperationAttention ? MissingConfirmedSlot ? "本件暂勿投递，请查看详情并通知维护人员" : Operation?.State switch
        { ParcelState.UnpackRequired => "请查看拆包明细，处理完再继续", ParcelState.Blocked => "本件暂勿投递，请查看详情", _ => RcsRejectionInstruction() } :
        SupplyDisabled ? "" : CenteredSoftwarePause ? PauseInstruction : CenteredRcsPause ? "请检查连接，恢复后再提交" :
        CenteredNoBarcodeBlocked ? SupplyDisabling ? CanConfirmPlacementRemoval ? "请先取下包裹，再确认" : "等待确认托盘清空" : Operation?.State == ParcelState.ResolvingWcs ? "请等待结果后再上件" : "请核对本位状态，必要时通知维护人员" :
        CurrentReaderState?.IsValid(DateTimeOffset.UtcNow) == true ? "也可输入完整条码后按回车" : "";

    static (string Title, string Instruction) AdmissionMessage(ParcelInputSnapshot input) => ParcelInputReasons.BlockReason(input) switch
    {
        ParcelInputBlockReason.ReaderConfirmationExpired => ("相机连接未确认", "请等待相机自动恢复，再重新上件"),
        ParcelInputBlockReason.RcsUnavailable => ("RCS 连接未就绪", "请等待 RCS 连接恢复，再重新上件"),
        ParcelInputBlockReason.PreviousResultPending => ("本位正在获取格口", "请等待本位处理结束，再重新上件"),
        ParcelInputBlockReason.PreviousResultUnconfirmed => ("本位有格口结果尚未确认", "请取下包裹，按本位提示处理"),
        ParcelInputBlockReason.PreviousWorkBlocked => ("本位作业尚未结束", "请查看本位详情，处理后再上件"),
        ParcelInputBlockReason.SoftwarePaused => ("供包已暂停", "确认现场后恢复供包，再重新上件"),
        ParcelInputBlockReason.ManagementOpen => ("管理中心正在使用", "关闭管理中心后再上件"),
        ParcelInputBlockReason.PositionDisabled => ("本位已停用或正在停用", "请启用本投递位后再上件"),
        ParcelInputBlockReason.OperatorSessionExpired => ("操作员登录已失效", "请重新登录后再上件"),
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
        ParcelInputBlockReason.ExpiredIdentity => ("重复读到了旧扫码记录", "请取下包裹后重新上件"),
        ParcelInputBlockReason.CameraInspectionIsolated => ("本次读码来自相机调试", "结束调试后再重新上件"),
        ParcelInputBlockReason.ManualConditionsChanged => ("提交期间本位条件已变化", "请核对本位状态和当前包裹后再提交"),
        _ => ("本次扫码原因未记录", "请取下包裹，持续出现时通知维护人员查看日志")
    };
}

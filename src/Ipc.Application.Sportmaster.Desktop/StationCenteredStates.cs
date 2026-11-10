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
        CameraNoCode && !CameraAwaitingRead && !CenteredWaiting && !CenteredRcsPause;
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
        { Outcome: ParcelInputOutcome.Blocked } => "本次扫码未发送",
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
        { Outcome: ParcelInputOutcome.Blocked } => "条码已读到，托盘尚未确认清空",
        _ => ParcelPresentation.RcsTimedOut(Operation) ? "等待取格口响应超时" : "本件没有可用格口"
    };
    string OriginalAttentionInstruction => CanConfirmPlacementRemoval ? "请先取下包裹，再确认" :
        DisplayPaused ? "请取下包裹，供包恢复后再上件" :
        RcsUnavailableForDisplay ? "请取下包裹，RCS 连接恢复后再上件" :
        "请取下包裹，等本位恢复后再上件\n多次出现请通知维护人员";
    string ReadAttentionTitle => invalidReadFeedback ? "本次读码已失效" : ParcelPresentation.InputText(CurrentInputException!);
    string ReadAttentionInstruction => DisplayPaused ? "供包已暂停，请将包裹移出托盘" : SupplyDisabling ? "本位停用中，暂不接收新条码" :
        RcsUnavailableForDisplay ? "请取下包裹，RCS 恢复后再上件" : invalidReadFeedback ? "本件暂勿投递，请重新扫码" :
        CurrentInputException is { Outcome: ParcelInputOutcome.Blocked } ? "请按本位状态处理，恢复后再上件" :
        CurrentInputException?.Reason.StartsWith("ObservationExpired;", StringComparison.Ordinal) == true ? "请取下包裹后重新上件" :
        CanViewCurrentPicture ? "请查看图片核对条码，取下包裹后再上件" : "请核对包裹条码，取下包裹后再上件";
    string RcsRejectionInstruction()
    {
        var manual = ParcelPresentation.RcsManualHandling(Operation);
        var removal = manual ? "请取下包裹后人工判断" : "请取下包裹";
        if (SupplyDisabling) return removal + "\n停用中，暂不接收新条码";
        if (DisplayPaused) return removal + "\n供包恢复后再放下一件";
        if (RcsUnavailableForDisplay) return removal + "\nRCS 连接恢复后再放下一件";
        if (CanAdmit || CanSubmitBarcode) removal += manual ? "，再放下一件" : "，可放下一件";
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
    public bool ShowCenteredState => CenteredAttention || SupplyDisabled || CenteredSoftwarePause || CenteredRcsPause || CenteredWaiting || CenteredNoBarcodeBlocked;
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
        CenteredNoBarcodeBlocked ? SupplyDisabling ? "等待确认托盘清空" : Operation?.State == ParcelState.ResolvingWcs ? "请等待结果后再上件" : "请核对本位状态，必要时通知维护人员" :
        CurrentReaderState?.IsValid(DateTimeOffset.UtcNow) == true ? "也可输入完整条码后按回车" : "";
}

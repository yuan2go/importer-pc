using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Platform;

namespace Ipc.Application.Sportmaster.Desktop;

public sealed partial class SupplyStationPositionViewModel
{
    bool PreserveDetailedResult => HasUncertainOriginal || ShowConfirmedTask || CurrentTaskAvailable || IsProcessing ||
        Operation?.State == ParcelState.WcsRejected || Input?.Outcome == ParcelInputOutcome.Rejected;
    bool RcsUnavailableForDisplay => owner.Snapshot is { } snapshot && snapshot.Configuration.Effective.Wcs is { } binding &&
        !ParcelConnectionSetup.IsDeferred(snapshot.Configuration.Effective, binding.InstanceId) &&
        (snapshot.Health.SingleOrDefault(h => h.InstanceId == binding.InstanceId) is not { Ready: true } health ||
            health.ConfirmedAt > DateTimeOffset.UtcNow || health.ValidUntil <= DateTimeOffset.UtcNow);
    bool CenteredRcsPause => owner.Fresh && SupplyEnabled && !DisplayPaused && !PreserveDetailedResult && RcsUnavailableForDisplay;
    bool CenteredWaiting => owner.Fresh && SupplyEnabled && !DisplayPaused && !PreserveDetailedResult && !RcsUnavailableForDisplay &&
        !HasCurrentInput && owner.Snapshot?.Positions?.SingleOrDefault(p => p.Id == PositionId) is { } position &&
        (position.Admit || position.BarcodeInput?.Manual == true);
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

    string OriginalAttentionTitle => ParcelPresentation.RcsNotDelivered(Operation) ? "取格口请求未送达" :
        Operation?.Wcs?.State == StageState.Conflict ? "格口结果有冲突" :
        Operation?.Wcs?.State == StageState.Unknown ? CurrentInputException is null ? "格口结果未确认" : "上一件格口未确认" : "作业结果未确认";
    string OriginalAttentionReason
    {
        get
        {
            if (CurrentInputException is not null && Operation?.ParcelCode is { Length: > 0 } code && !code.Any(char.IsControl))
                return "上一件条码 · " + (code.Length > 24 ? "…" + code[^12..] : code);
            if (ParcelPresentation.RcsNotDelivered(Operation)) return "取格口请求未到达 RCS";
            if (ParcelPresentation.RcsTimedOut(Operation)) return "等待取格口响应超时";
            return Operation?.Wcs?.State == StageState.Conflict ? "原请求收到不一致的结果" : "请核对本次原请求";
        }
    }
    string OriginalAttentionInstruction()
    {
        if (ParcelPresentation.RcsNotDelivered(Operation) && CurrentInputException is null)
            return DisplayPaused ? "供包已暂停，暂勿投递\n恢复供包后取下包裹重新上件" : "本件暂勿投递\nRCS 连接恢复后取下包裹重新上件";
        var action = DisplayPaused ? "供包已暂停，请查看详情并通知维护人员" : "暂勿投递，请查看详情并通知维护人员";
        return CurrentInputException switch
        {
            { Outcome: ParcelInputOutcome.Rejected } input => ParcelPresentation.InputText(input) + "，本件未发送\n" + action,
            { Outcome: ParcelInputOutcome.Blocked } => "本件已读码，未发送\n" + action,
            _ => "请勿重复上件\n" + action
        };
    }
    string ReadAttentionTitle => invalidReadFeedback ? "本次读码已失效" : ParcelPresentation.InputText(CurrentInputException!);
    string ReadAttentionInstruction => DisplayPaused ? "供包已暂停，请取下包裹" : SupplyDisabling ? "本位停用中，暂不接收新条码" :
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
        owner.CanResumeInViewport ? PausedReadBlocked ? "条码已读到，尚未请求格口" : "新包裹暂不受理" : BlockingReasons;
    public string PauseInstruction => InterruptedInputBlocksResume
        ? "请联系维护人员检查运行数据，操作工无需进入管理中心。"
        : owner.CanResumeInViewport ? "确认现场后，点击顶部“恢复供包”。"
        : "请到管理中心的设备与诊断处理阻断原因，再恢复供包。";
    public bool ShowCenteredState => CenteredAttention || SupplyDisabled || CenteredSoftwarePause || CenteredRcsPause || CenteredWaiting;
    public string CenteredStateTitle => CenteredStale ? "状态读取中断" : CenteredOriginalAttention ? OriginalAttentionTitle : CenteredReadAttention ? ReadAttentionTitle :
        CurrentRcsRejection is { } rejection ? string.IsNullOrWhiteSpace(rejection.Message) ? "RCS 未分配格口" : rejection.Message :
        CenteredOperationAttention ? MissingConfirmedSlot ? "未取得有效格口" : Operation?.State switch
        { ParcelState.UnpackRequired => "需要拆包检查", ParcelState.Blocked => "本件暂不能处理", _ => "未分配格口" } :
        SupplyDisabled ? "已停用" : CenteredSoftwarePause ? "供包已暂停" : CenteredRcsPause ? "暂不能投包" : "等待投包";
    public string CenteredStateForeground => CenteredAttention ? CenteredAttentionTone switch
    { ParcelPresentationTone.Stale => OperatorTokens.Muted, ParcelPresentationTone.Unknown => OperatorTokens.Warning, _ => OperatorTokens.Danger }
        : CenteredRcsPause ? OperatorTokens.Danger : SupplyDisabled || CenteredSoftwarePause ? OperatorTokens.Muted : OperatorTokens.Success;
    string RcsRejectionReason()
    {
        const string reason = "本件暂不能分配格口";
        if (Operation is not { ParcelCode: { Length: > 0 } code } operation || code.Any(char.IsControl))
            return reason;
        var source = operation.OperatorInput is null ? "已读到" : "已录入";
        return reason + "　" + source + " · …" + code[^Math.Min(4, code.Length)..];
    }
    public string CenteredStateReason => CenteredStale ? "旧格口已失效" : CenteredOriginalAttention ? OriginalAttentionReason :
        CenteredReadAttention ? invalidReadFeedback ? "本次没有有效格口" : "本次扫码未发送请求" :
        CenteredRcsRejection ? RcsRejectionReason() : CenteredOperationAttention ? Operation?.State == ParcelState.UnpackRequired ? "本件需人工检查" : "本件暂不能分配格口" :
        SupplyDisabled ? "不接收条码" : CenteredSoftwarePause ? PauseReason : CenteredRcsPause ? "RCS 服务离线" :
        CurrentReaderState?.IsValid(DateTimeOffset.UtcNow) == true ? "放上包裹，由相机扫码" : "可输入完整条码";
    public string CenteredStateInstruction => CenteredStateFullInstruction;
    public string CenteredStateFullInstruction => CenteredStale ? "请等待状态恢复，暂勿按旧格口投递" :
        CenteredOriginalAttention ? OriginalAttentionInstruction() : CenteredReadAttention ? ReadAttentionInstruction :
        CenteredOperationAttention ? MissingConfirmedSlot ? "本件暂勿投递，请查看详情并通知维护人员" : Operation?.State switch
        { ParcelState.UnpackRequired => "请查看拆包明细，处理完再继续", ParcelState.Blocked => "本件暂勿投递，请查看详情", _ => RcsRejectionInstruction() } :
        SupplyDisabled ? "" : CenteredSoftwarePause ? PauseInstruction : CenteredRcsPause ? "请检查连接，恢复后再提交" :
        CurrentReaderState?.IsValid(DateTimeOffset.UtcNow) == true ? "也可输入完整条码后按回车" : "";
}

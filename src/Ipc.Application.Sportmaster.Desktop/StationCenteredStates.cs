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
    WcsDestinationResult? CurrentRcsRejection =>
        Operation is { State: ParcelState.WcsRejected, Wcs: { State: StageState.Rejected, Request.Binding.Contract: RcsRequestPolicy.Contract } stage } &&
        owner.Fresh && !SupplyDisabled && !InputUnadmitted && !CameraNoCode && !CameraObservationUnmatched &&
        !unreadFeedback && !invalidReadFeedback && !HasUncertainOriginal &&
        stage.Replies.LastOrDefault() is { TransportSucceeded: true, ProtocolAcknowledged: true, BusinessAccepted: false,
            Destination: { Outcome: WcsDestinationOutcome.Rejected } destination } ? destination : null;
    public bool CenteredRcsRejection => CurrentRcsRejection is not null;
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
    public bool ShowCenteredState => CenteredRcsRejection || SupplyDisabled || CenteredSoftwarePause || CenteredRcsPause || CenteredWaiting;
    public string CenteredStateTitle => CurrentRcsRejection is { } rejection
        ? string.IsNullOrWhiteSpace(rejection.Message) ? "RCS 未分配格口" : rejection.Message
        : SupplyDisabled ? "已停用" : CenteredSoftwarePause ? "供包已暂停" : CenteredRcsPause ? "暂不能投包" : "等待投包";
    public string CenteredStateForeground => CenteredRcsRejection || CenteredRcsPause ? OperatorTokens.Danger :
        SupplyDisabled || CenteredSoftwarePause ? OperatorTokens.Muted : OperatorTokens.Success;
    public string CenteredStateReason => CenteredRcsRejection ? "本件暂不能分配格口" : SupplyDisabled ? "不接收条码" : CenteredSoftwarePause ? PauseReason : CenteredRcsPause ? "RCS 服务离线" :
        CurrentReaderState?.IsValid(DateTimeOffset.UtcNow) == true ? "放上包裹，由相机扫码" : "可输入完整条码";
    public string CenteredStateInstruction => CenteredStateFullInstruction;
    public string CenteredStateFullInstruction => CenteredRcsRejection ? RcsRejectionInstruction() : SupplyDisabled ? "" : CenteredSoftwarePause ? PauseInstruction : CenteredRcsPause ? "请检查连接，恢复后再提交" :
        CurrentReaderState?.IsValid(DateTimeOffset.UtcNow) == true ? "也可输入完整条码后按回车" : "";
}

using Ipc.Application.Contracts;
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
    public bool CenteredWaiting => owner.Fresh && SupplyEnabled && !DisplayPaused && !PreserveDetailedResult && !RcsUnavailableForDisplay &&
        !HasCurrentInput && owner.Snapshot?.Positions?.SingleOrDefault(p => p.Id == PositionId) is { } position &&
        (position.Admit || position.BarcodeInput?.Manual == true);
    bool CenteredSoftwarePause => owner.Fresh && SupplyEnabled && DisplayPaused && !PausedAttention;
    bool InterruptedInputBlocksResume => owner.Snapshot?.BlockingReasons.Any(r => r.StartsWith("IngressInterrupted:", StringComparison.Ordinal)) == true;
    public string PauseReason => InterruptedInputBlocksResume ? "上次运行的数据无法自动恢复" :
        RcsUnavailableForDisplay ? "RCS 连接尚未恢复" :
        owner.CanResumeInViewport ? PausedReadBlocked ? "条码已读到，尚未请求格口" : "新包裹暂不受理" : BlockingReasons;
    public string PauseInstruction => InterruptedInputBlocksResume
        ? "请联系维护人员检查运行数据，操作工无需进入管理中心。"
        : owner.CanResumeInViewport ? "确认现场后，点击顶部“恢复供包”。"
        : "请到管理中心的设备与诊断处理阻断原因，再恢复供包。";
    WcsDestinationResult? CurrentRcsRejection => owner.Fresh && !SupplyDisabled && !HasUncertainOriginal && !InputUnadmitted &&
        !CameraNoCode && !CameraObservationUnmatched && !unreadFeedback && !invalidReadFeedback &&
        Operation is { State: ParcelState.WcsRejected, Wcs: { State: StageState.Rejected, Request.Binding.Contract: RcsRequestPolicy.Contract } stage } &&
        stage.Replies.LastOrDefault()?.Destination is { Outcome: WcsDestinationOutcome.Rejected } rejection ? rejection : null;
    public bool CenteredRcsRejection => CurrentRcsRejection is not null;
    public string CenteredBarcodeCue => CenteredRcsRejection && Operation is { ParcelCode: { Length: > 0 } code } op && !code.Any(char.IsControl)
        ? (op.OperatorInput is null ? "已读到" : "已录入") + " · …" + code[^Math.Min(4, code.Length)..] : "";
    public bool ShowCenteredState => SupplyDisabled || CenteredRcsRejection || CenteredSoftwarePause || CenteredRcsPause || CenteredWaiting;
    public string CenteredStateTitle => CurrentRcsRejection is { } rejection ?
        string.IsNullOrWhiteSpace(rejection.Message) ? "RCS 拒绝本件" : rejection.Message :
        SupplyDisabled ? "已停用" : CenteredSoftwarePause ? "供包已暂停" : CenteredRcsPause ? "暂不能投包" : "等待投包";
    public string CenteredStateForeground => SupplyDisabled || CenteredSoftwarePause ? OperatorTokens.Muted : CenteredRcsPause || CenteredRcsRejection ? OperatorTokens.Danger : OperatorTokens.Success;
    public string CenteredStateReason => CenteredRcsRejection ? "本件暂不能分配格口" +
        (IsTight && CenteredBarcodeCue is { Length: > 0 } cue ? "　" + cue : "") :
        SupplyDisabled ? "不接收条码" : CenteredSoftwarePause ? PauseReason : CenteredRcsPause ? "RCS 服务离线" :
        CurrentReaderState?.IsValid(DateTimeOffset.UtcNow) == true ? "放上包裹，由相机扫码" : "可输入完整条码";
    public string CenteredStateInstruction => CenteredRcsRejection && IsTight
        ? CenteredStateFullInstruction.Split('\n', 2)[0] : CenteredStateFullInstruction;
    public string CenteredStateFullInstruction => CurrentRcsRejection is { } rejection ?
        (rejection.ManualHandlingRequired ? "请取下包裹后人工判断" : "请取下包裹") +
        (DisplayPaused ? "，等待恢复供包。" : "，可放下一件。\n多次出现请通知维护人员。") :
        SupplyDisabled ? "" : CenteredSoftwarePause ? PauseInstruction : CenteredRcsPause ? "请检查连接，恢复后再提交" :
        CurrentReaderState?.IsValid(DateTimeOffset.UtcNow) == true ? "也可输入完整条码后按回车" : "";
}

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
    bool CenteredWaiting => owner.Fresh && SupplyEnabled && !DisplayPaused && !PreserveDetailedResult && !RcsUnavailableForDisplay &&
        !HasCurrentInput && owner.Snapshot?.Positions?.SingleOrDefault(p => p.Id == PositionId) is { } position &&
        (position.Admit || position.BarcodeInput?.Manual == true);
    bool CenteredSoftwarePause => owner.Fresh && SupplyEnabled && DisplayPaused && !PausedAttention;
    bool InterruptedInputBlocksResume => owner.Snapshot?.BlockingReasons.Any(r => r.StartsWith("IngressInterrupted:", StringComparison.Ordinal)) == true;
    public string PauseReason => InterruptedInputBlocksResume ? "有上次中断的扫码尚未核对" :
        RcsUnavailableForDisplay ? "RCS 连接尚未恢复" :
        owner.CanResumeInViewport ? PausedReadBlocked ? "条码已读到，尚未请求格口" : "新包裹暂不受理" : BlockingReasons;
    public string PauseInstruction => InterruptedInputBlocksResume
        ? "请到管理中心 → 设备与诊断 → 维护工具，读取并核对未处理的扫码。"
        : owner.CanResumeInViewport ? "确认现场后，点击顶部“恢复供包”。"
        : "请到管理中心的设备与诊断处理阻断原因，再恢复供包。";
    public bool ShowCenteredState => SupplyDisabled || CenteredSoftwarePause || CenteredRcsPause || CenteredWaiting;
    public string CenteredStateTitle => SupplyDisabled ? "已停用" : CenteredSoftwarePause ? "供包已暂停" : CenteredRcsPause ? "暂不能投包" : "等待投包";
    public string CenteredStateForeground => SupplyDisabled || CenteredSoftwarePause ? OperatorTokens.Muted : CenteredRcsPause ? OperatorTokens.Danger : OperatorTokens.Success;
    public string CenteredStateReason => SupplyDisabled ? "不接收条码" : CenteredSoftwarePause ? PauseReason : CenteredRcsPause ? "RCS 服务离线" :
        CurrentReaderState?.IsValid(DateTimeOffset.UtcNow) == true ? "放上包裹，由相机扫码" : "可用扫码枪或键盘输入完整条码";
    public string CenteredStateInstruction => CenteredSoftwarePause && IsTight && InterruptedInputBlocksResume
        ? "到管理中心核对未处理的扫码。" : CenteredStateFullInstruction;
    public string CenteredStateFullInstruction => SupplyDisabled ? "" : CenteredSoftwarePause ? PauseInstruction : CenteredRcsPause ? "请检查连接，恢复后再提交" :
        CurrentReaderState?.IsValid(DateTimeOffset.UtcNow) == true ? "也可输入完整条码后按回车" : "输入完成后按回车提交";
}

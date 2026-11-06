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
    public bool ShowCenteredState => SupplyDisabled || CenteredRcsPause || CenteredWaiting;
    public string CenteredStateTitle => SupplyDisabled ? "已停用" : CenteredRcsPause ? "暂不能投包" : "等待投包";
    public string CenteredStateForeground => SupplyDisabled ? OperatorTokens.Muted : CenteredRcsPause ? OperatorTokens.Danger : OperatorTokens.Success;
    public string CenteredStateReason => SupplyDisabled ? "不接收条码" : CenteredRcsPause ? "RCS 服务离线" :
        CurrentReaderState?.IsValid(DateTimeOffset.UtcNow) == true ? "放上包裹，由相机扫码" : "可用扫码枪或键盘输入完整条码";
    public string CenteredStateInstruction => SupplyDisabled ? "" : CenteredRcsPause ? "请检查连接，恢复后再提交" :
        CurrentReaderState?.IsValid(DateTimeOffset.UtcNow) == true ? "也可输入完整条码后按回车" : "输入完成后按回车提交";
}

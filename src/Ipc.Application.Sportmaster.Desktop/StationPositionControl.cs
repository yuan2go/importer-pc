using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;

namespace Ipc.Application.Sportmaster.Desktop;

public sealed partial class SupplyStationViewModel
{
    public bool ShowSupplyControlNotice => !Left.SupplyEnabled || !Right.SupplyEnabled;
    internal void ReportSupplyReceipt(Ipc.Platform.BusinessReceipt receipt) => SetPresentationMessage(ChineseInterfaceText.Notice(receipt.Reason),
        receipt.Status == "Accepted" ? PresentationFeedbackKind.ResultMirrored : PresentationFeedbackKind.General);
    public sealed record SupplyHistoryRow(string Time, string Position, string Action, string Actor, string Task);
    public SupplyHistoryRow[] SupplyHistory { get; private set; } = [];
    long supplyHistoryCursor = long.MaxValue;
    public async Task LoadSupplyHistoryAsync(bool earlier = false)
    {
        if (Application is not IStationPositionControl control) return;
        var records = await ServiceAsync(() => control.ReadPositionControlHistoryAsync(earlier ? supplyHistoryCursor : long.MaxValue, 100, CancellationToken.None));
        SupplyHistory = records.Select(r => new SupplyHistoryRow(r.State.ChangedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "", r.State.PositionId == "left" ? "左投递位" : "右投递位",
            r.State.Mode switch { StationPositionMode.Enabled => "启用", StationPositionMode.Disabling => "停用中", _ => "已停用" },
            r.State.ChangedBy switch { "application" => "自动完成", "local-workstation" => "操作员", _ => r.State.ChangedBy ?? "" }, r.State.TaskId ?? "—")).ToArray();
        if (records.Length > 0) supplyHistoryCursor = records[^1].Sequence;
        Changed(nameof(SupplyHistory));
    }
}

public sealed partial class SupplyStationPositionViewModel
{
    public StationPositionState? SupplyControl => owner.Snapshot?.Positions?.SingleOrDefault(p => p.Id == PositionId)?.SupplyControl;
    public bool SupplyEnabled => SupplyControl?.Mode is null or StationPositionMode.Enabled;
    public bool SupplyDisabling => SupplyControl?.Mode == StationPositionMode.Disabling;
    public bool SupplyDisabled => SupplyControl?.Mode == StationPositionMode.Disabled;
    public bool SupplyConfigured => Arrival?.Configured != false;
    public bool CanChangeSupply => SupplyConfigured && owner.Fresh && owner.CanStartOperation && owner.Application is IStationPositionControl;
    string? PlacementRemovalOperation => owner.Snapshot?.Positions?.SingleOrDefault(p => p.Id == PositionId)?.PlacementRemovalOperation;
    public bool CanConfirmPlacementRemoval => owner.Fresh && SupplyEnabled && !ShowConfirmedTask && PlacementRemovalOperation is not null;
    public bool ShowConfirmedPlacementRemoval => CanConfirmPlacementRemoval;
    bool removalBusy;
    internal async Task ConfirmPlacementRemovalAsync()
    {
        if (removalBusy || !CanConfirmPlacementRemoval || owner.Snapshot is not { } snapshot || PlacementRemovalOperation is not { } target) return;
        removalBusy = true;
        try
        {
            var receipt = await owner.ServiceAsync(() => owner.Application.CommandAsync(
                new(Ipc.Platform.Wire.Id(), snapshot.Revision, "ConfirmPlacementRemoval", PositionId, target)));
            if (receipt.Status != "Persisted") owner.Message = "本位状态已变化，请查看当前提示。";
        }
        catch (Exception) { owner.Message = "未能确认取下，请按当前轮次核对原结果，不重复提交。"; }
        finally { removalBusy = false; await owner.RefreshAsync(); }
    }
    public string DisableLabel => SupplyDisabling ? "停用中…" : "停用";
    public string SupplyStateText => SupplyEnabled ? "本位已启用" : SupplyDisabling ? "本位停用中" : "本位已停用";
    public string EnableLabel => "启用" + Heading;
    public string SupplyNotice => SupplyDisabling
        ? "停用中：不再接收新条码。本轮请求及结果保存结束后自动停用。点“启用”可取消。"
        : SupplyConfigured ? "本位已停用，不接收条码。" : "相机编码未配置，本位停用；在设置填写后可启用。";
    public string EnableBackground => SupplyEnabled ? OperatorTokens.Success : OperatorTokens.Surface;
    public string EnableForeground => SupplyEnabled ? OperatorTokens.Surface : OperatorTokens.Muted;
    public string DisableBackground => SupplyEnabled ? OperatorTokens.Surface : SupplyDisabling ? OperatorTokens.Warning : OperatorTokens.Inactive;
    public string DisableForeground => SupplyEnabled ? OperatorTokens.Muted : OperatorTokens.Surface;
    internal Task SetSupplyEnabledAsync(bool enabled) => owner.RunAsync(async () =>
    {
        if (!owner.Fresh || owner.Application is not IStationPositionControl control || owner.Snapshot is not { } snapshot) return;
        var request = new SetStationPositionEnabled(Guid.NewGuid().ToString("N"), snapshot.RuntimeId, PositionId, SupplyControl?.Revision ?? 0, enabled);
        var receipt = await owner.ServiceAsync(() => control.SetPositionEnabledAsync(request, CancellationToken.None));
        owner.ReportSupplyReceipt(receipt);
        await owner.RefreshDisplayAsync();
    }, "切换投递位启停");
}

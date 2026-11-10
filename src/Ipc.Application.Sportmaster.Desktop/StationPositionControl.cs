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
    public bool CanChangeSupply => owner.Fresh && owner.CanStartOperation && owner.Application is IStationPositionControl;
    string? PlacementRemovalOperation => owner.Snapshot?.Positions?.SingleOrDefault(p => p.Id == PositionId)?.PlacementRemovalOperation;
    public bool CanConfirmPlacementRemoval => owner.Fresh && !SupplyDisabled && PlacementRemovalOperation is not null;
    public bool ShowConfirmedPlacementRemoval => CanConfirmPlacementRemoval &&
        Operation is { OperatorInput: not null, State: ParcelState.SlotResolved or ParcelState.WcsRejected };
    internal Task ConfirmPlacementRemovalAsync() => owner.RunAsync(async () =>
    {
        if (!CanConfirmPlacementRemoval || owner.Snapshot is not { } snapshot || PlacementRemovalOperation is not { } operation) return;
        try
        {
            var receipt = await owner.ServiceAsync(() => owner.Application.CommandAsync(
                new(Ipc.Platform.Wire.Id(), snapshot.Revision, "ConfirmPlacementRemoval", PositionId, operation)));
            if (receipt.Status != "Persisted")
                owner.Message = "本位状态已变化，请查看当前提示。";
        }
        catch (Exception)
        {
            // The Application records the command and exception for diagnostics.
            owner.Message = "未能确认包裹已取下，请等状态更新后查看本位提示。";
        }
    }, "确认已取下包裹");
    public string DisableLabel => SupplyDisabling ? "停用中…" : "停用";
    public string SupplyStateText => SupplyEnabled ? "本位已启用" : SupplyDisabling ? "本位停用中" : "本位已停用";
    public string EnableLabel => "启用" + Heading;
    public string SupplyNotice => SupplyDisabling ? "停用中：不再接收新条码。请取下包裹，托盘清空后自动停用。点“启用”可取消。" : "本位已停用，不接收条码。";
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

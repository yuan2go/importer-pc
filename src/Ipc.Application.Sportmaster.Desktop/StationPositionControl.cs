using Ipc.Presentation.Wpf;
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
        finally
        {
            removalBusy = false;
            try { await owner.RefreshAsync(); }
            catch (Exception error) { owner.MarkReadFailure("取下确认后的状态读取失败：" + error.Message); }
        }
    }
    public string DisableLabel => SupplyDisabling ? UiText.Get("Work.DisablingShort") : UiText.Get("Work.Disable");
    public string SupplyStateText => SupplyEnabled ? UiText.Get("Work.CurrentLaneEnabled") : SupplyDisabling ? UiText.Get("Work.CurrentLaneDisabling") : UiText.Get("Work.CurrentLaneDisabled");
    public string EnableLabel => UiText.Format("Work.EnablePosition", Heading);
    public string SupplyNotice => SupplyDisabling
        ? UiText.Get("Work.DisablingExplain")
        : SupplyConfigured ? UiText.Get("Work.DisabledExplain") : UiText.Get("Work.NoCameraCode");
    public string EnableBackgroundKey => SupplyEnabled ? "Accent" : "Surface";
    public string EnableForegroundKey => SupplyEnabled ? "OnAccent" : "Muted";
    public string DisableBackgroundKey => SupplyEnabled ? "Surface" : SupplyDisabling ? "OperatorWarning" : "OperatorInactive";
    public string DisableForegroundKey => SupplyEnabled ? "Muted" : SupplyDisabling ? "OperatorOnWarning" : "InkOnInactive";
    internal Task SetSupplyEnabledAsync(bool enabled) => owner.RunAsync(async () =>
    {
        if (!owner.Fresh || owner.Application is not IStationPositionControl control || owner.Snapshot is not { } snapshot) return;
        var request = new SetStationPositionEnabled(Guid.NewGuid().ToString("N"), snapshot.RuntimeId, PositionId, SupplyControl?.Revision ?? 0, enabled);
        var receipt = await owner.ServiceAsync(() => control.SetPositionEnabledAsync(request, CancellationToken.None));
        owner.ReportSupplyReceipt(receipt);
        await owner.RefreshDisplayAsync();
    }, "切换投递位启停");
}

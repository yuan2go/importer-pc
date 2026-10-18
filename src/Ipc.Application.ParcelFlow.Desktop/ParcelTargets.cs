using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow.Desktop;

public sealed record PositionPresentation(string Id, string DeviceId, string Availability, string Code, string Admission, string State, string Slot, string Reason);
public partial class ParcelViewModel
{
    string selectedPositionId = "main";
    public string[] PositionIds => Snapshot?.Positions?.Select(x => x.Id).ToArray() ?? [];
    public string SelectedPositionId { get => selectedPositionId; set { selectedPositionId = value; Changed(); NotifyTargets(); } }
    public PositionPresentation[] PositionRows => Snapshot?.Positions?.Select(p =>
    {
        var op = Snapshot.Operations.FirstOrDefault(x => x.Id == p.CurrentOperation);
        return new PositionPresentation(p.Id, p.ReaderDeviceId, p.Admit ? "允许新入场" : string.Join(" · ", p.BlockingReasons), op?.ParcelCode ?? "—", op?.AdmissionId ?? "—", op?.State.ToString() ?? "等待本位观察", op?.Slot ?? "未取得", op?.Reason ?? "");
    }).ToArray() ?? [];
    public string CurrentCommandTarget => "当前位命令目标：" + SelectedPositionId + " · " + (Current?.ParcelCode ?? "无作业") + " · " + (Current?.Id ?? "—");
    public string HistoryCommandTarget => "记录命令目标：" + (Selected?.PositionId ?? "—") + " · " + (Selected?.ParcelCode ?? "未选择") + " · " + (Selected?.Id ?? "—");
    public bool CanHistoryContinue => Selected is not null && Snapshot?.Availability?.ContinuePrepared.Contains(Selected.Id) == true;
    public bool CanHistoryQuery => Selected is not null && Snapshot?.Operations.Any(x => x.Id == Selected.Id && (x.Wcs?.State == StageState.Unknown || x.Plc?.State is StageState.Unknown or StageState.Accepted || x.Capture?.State == StageState.Unknown || x.Upload?.State == StageState.Unknown)) == true;
    public string CapacityState => Snapshot?.Capacity is { } c ? $"未结 {c.Unresolved}/{c.MaximumUnresolved} · 工作集 {c.Resident} · 归档 {c.Archived}（原件和去重保留）" : "容量未取得";
    public string AuxiliaryState => string.Join("\n", Snapshot?.AuxiliaryReasons ?? []);
    public string InstalledProviderParameters { get; private set; } = "";
    public async Task ValidateConfigurationDraftAsync() { var r = await application.ValidateConfigurationAsync(Wire.Decode<ParcelConfiguration>(ConfigurationDraft)); Message = r.Status + " · " + r.Reason; }
    public string ConfigurationReason { get; set; } = "";
    public string ExtensionAction { get; set; } = "extension-recover";
    public string ExtensionIdentity { get; set; } = "";
    public string IdentityConflictId { get; set; } = "";
    public ParcelHistoryItem[] HistoryItems { get; private set; } = [];
    ParcelHistoryItem? selectedHistory;
    public ParcelHistoryItem? SelectedHistory { get => selectedHistory; set { selectedHistory = value; Selected = value?.Operation; Changed(); } }
    long? nextHistory;
    public bool HasNextHistory => nextHistory is not null;
    public async Task LoadHistoryAsync(bool next = false)
    {
        var page = await application.HistoryAsync(next ? nextHistory ?? long.MaxValue : long.MaxValue);
        HistoryItems = page.Items; nextHistory = page.NextBeforeSequence; SelectedHistory = null;
        Changed(nameof(HistoryItems)); Changed(nameof(HasNextHistory));
    }
    public async Task QuerySelectedAsync()
    { var op = Selected ?? throw new InvalidOperationException("先选择详情中的原作业"); var r = await application.ReconcileAsync(op.Id); Message = r.Status + " · " + r.Reason; Selected = await application.OperationAsync(op.Id); }
    public async Task ContinueSelectedAsync()
    {
        var op = Selected ?? throw new InvalidOperationException("先选择详情中的原作业");
        var r = await application.CommandAsync(new(Wire.Id(), Snapshot!.Revision, "ContinuePrepared", op.Id, InspectionReason)); Message = r.Status + " · " + r.Reason;
        Selected = await application.OperationAsync(op.Id);
    }
    public async Task ManageExtensionAsync() { var r = await application.ManageCapabilityAsync(ExtensionAction, ExtensionIdentity); Message = r.Status + " · " + r.Reason; }
    public async Task ArchiveHistoryAsync() { var r = await application.CommandAsync(new(Wire.Id(), Snapshot!.Revision, "ArchiveHistory", Value: InspectionReason)); Message = r.Status + " · " + r.Reason; }
    public async Task AcknowledgeIdentityAsync() { var r = await application.CommandAsync(new(Wire.Id(), Snapshot!.Revision, "AcknowledgeIdentityConflict", IdentityConflictId, InspectionReason)); Message = r.Status + " · " + r.Reason; }
    void NotifyTargets()
    {
        foreach (var name in new[] { nameof(Current), nameof(CurrentSlot), nameof(CurrentStage), nameof(CurrentWeight), nameof(OperationOperator), nameof(CurrentImage), nameof(CaptureState), nameof(UploadState), nameof(PositionIds), nameof(PositionRows), nameof(CurrentCommandTarget), nameof(HistoryCommandTarget), nameof(CanHistoryContinue), nameof(CanHistoryQuery), nameof(CanContinue), nameof(CanContinueUpload), nameof(CanAbandon), nameof(Details), nameof(CapacityState), nameof(AuxiliaryState) }) Changed(name);
    }
}

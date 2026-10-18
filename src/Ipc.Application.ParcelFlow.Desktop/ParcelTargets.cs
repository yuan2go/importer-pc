using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow.Desktop;

public sealed record PositionPresentation(string Id, string DeviceId, string Availability, string Code, string Admission, string State, string Slot, string Reason, string MainResult, string Background);
public partial class ParcelViewModel
{
    string selectedPositionId = "main";
    public string[] PositionIds => Snapshot?.Positions?.Select(x => x.Id).ToArray() ?? [];
    public string SelectedPositionId { get => selectedPositionId; set { selectedPositionId = value; Changed(); NotifyTargets(); } }
    public PositionPresentation[] PositionRows => Snapshot?.Positions?.Select(p =>
    {
        var op = Snapshot.Operations.FirstOrDefault(x => x.Id == p.CurrentOperation);
        return new PositionPresentation(p.Id, p.ReaderDeviceId, p.Admit ? "允许新入场" : string.Join(" · ", p.BlockingReasons), op?.ParcelCode ?? "—", op?.AdmissionId ?? "—", op?.State.ToString() ?? "等待本位观察", op?.Slot ?? "未取得", op?.Reason ?? "", p.MainResultComplete ? "主结果已确认" : "主结果未完成", p.BackgroundComplete ? "后台责任已清偿" : "后台责任待清偿 · " + op?.Upload?.State + " · " + op?.Upload?.Failure);
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
    public string EffectiveConfiguration => Snapshot is null ? "未加载" : Wire.Encode(Snapshot.Configuration.Effective);
    public string EffectiveConfigurationHeading => Snapshot?.Configuration.Confirmation is { Required: false } ? "最后确认 Effective" : "当前运行输入 · 尚待确认";
    public string PendingConfiguration => Snapshot?.Configuration.Pending is { } pending ? Wire.Encode(pending) : "无候选";
    Guid? confirmationRuntimeId;
    long? confirmationRevision;
    string? confirmationEffectiveHash;
    string configurationConfirmationReason = "";
    public string ConfigurationConfirmationReason
    { get => configurationConfirmationReason; set { configurationConfirmationReason = value; Changed(); Changed(nameof(CanConfirmConfiguration)); } }
    public string ConfigurationConfirmationEvidence { get; private set; } = "读取待确认原账，核对原始记录、SHA256和当前运行输入后填写维护说明。";
    public string ConfigurationConfirmationState => confirmationRevision is { } revision
        ? "本窗确认资料基于运行实例 " + confirmationRuntimeId + " / 配置修订 " + revision +
            (Snapshot?.RuntimeId != confirmationRuntimeId ? " · 运行实例已变，必须重新读取" : Snapshot?.Configuration.Revision != revision ? " · 当前修订已改变，旧资料提交将冲突；请核对后重新读取" : " · 自动刷新保留本次读取身份")
        : "尚未读取待确认原账";
    public bool CanConfirmConfiguration => CanConfigure && Snapshot?.Configuration.Confirmation?.Required == true &&
        confirmationRuntimeId == Snapshot.RuntimeId && confirmationRevision is not null && !string.IsNullOrWhiteSpace(ConfigurationConfirmationReason);
    public async Task LoadConfigurationConfirmationAsync()
    {
        await RefreshAsync();
        var loaded = Snapshot ?? throw new InvalidOperationException("先读取当前应用投影");
        var confirmation = loaded.Configuration.Confirmation ?? throw new InvalidOperationException("当前输入没有可核验确认资料；检查配置恢复状态");
        confirmationRuntimeId = loaded.RuntimeId; confirmationRevision = loaded.Configuration.Revision; confirmationEffectiveHash = confirmation.EffectiveHash;
        var original = confirmation.OriginalEnvelopeBase64 is { } bytes
            ? System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(bytes)).TrimStart('\uFEFF')
            : "新部署种子无旧账；首次资源成功启动后由应用确认。";
        ConfigurationConfirmationEvidence = "运行实例：" + loaded.RuntimeId + "\n读取时配置修订：" + loaded.Configuration.Revision +
            "\n来源 / 确认状态：" + confirmation.Source + " / " + confirmation.State +
            "\n当前运行输入 SHA256：" + confirmation.EffectiveHash + "\n原账字节 SHA256：" + (confirmation.OriginalEnvelopeSha256 ?? "无旧账") +
            "\n资源启动尝试：\n" + Wire.Encode(confirmation.Attempts) + "\n\n原始配置账（只读展示；原始字节在权威账中保留）：\n" + original +
            "\n\n本次读取的实际运行输入：\n" + Wire.Encode(loaded.Configuration.Effective);
        Changed(nameof(ConfigurationConfirmationEvidence)); Changed(nameof(ConfigurationConfirmationState)); Changed(nameof(CanConfirmConfiguration));
    }
    public async Task ConfirmConfigurationAsync()
    {
        if (confirmationRevision is not { } revision || confirmationRuntimeId is not { } runtime || confirmationEffectiveHash is null)
            throw new InvalidOperationException("先读取并核对待确认原账");
        var (_, current) = await ReadSnapshotWithIdAsync();
        if (current.RuntimeId != runtime) throw new InvalidOperationException("运行实例已改变；重新读取原账，不自动推进旧确认资料");
        var receipt = await application.CommandAsync(new(Wire.Id(), current.Revision, "ConfirmConfiguration", runtime.ToString("N") + ":" + revision + ":" + confirmationEffectiveHash, ConfigurationConfirmationReason));
        Message = receipt.Status + " · " + receipt.Reason;
    }
    public string ReceiptOccurrenceId { get; set; } = "";
    string? receiptInventoryOwner;
    string[] receiptInventoryIds = [];
    public string ReceiptInventory { get; private set; } = "先在记录页选择原作业，再读取原接收发生。";
    public async Task LoadReceiptResponsibilitiesAsync()
    {
        var op = Selected ?? throw new InvalidOperationException("先选择记录中的原作业");
        var r = await application.CommandAsync(new(Wire.Id(), Snapshot!.Revision, "ReceiptResponsibilities", op.Id));
        ReceiptInventory = r.Reason;
        receiptInventoryOwner = r.Status == "ReceiptResponsibilities" ? op.Id : null;
        if (receiptInventoryOwner is not null)
        {
            using var json = System.Text.Json.JsonDocument.Parse(r.Reason);
            receiptInventoryIds = json.RootElement.EnumerateArray().Select(x => x.GetProperty("Id").GetString()!).ToArray();
        }
        else receiptInventoryIds = [];
        Changed(nameof(ReceiptInventory));
    }
    public async Task ReconcileReceiptAsync()
    {
        if (Selected?.Id != receiptInventoryOwner || !receiptInventoryIds.Contains(ReceiptOccurrenceId))
            throw new InvalidOperationException("重新读取当前所选原作业的回执责任，并选择其中的确切发生ID；不回退到其他位或隐藏目标");
        var r = await application.CommandAsync(new(Wire.Id(), Snapshot!.Revision, "ReconcileReceipt", ReceiptOccurrenceId, InspectionReason));
        Message = r.Status + " · " + r.Reason;
        await LoadReceiptResponsibilitiesAsync();
    }
    void NotifyTargets()
    {
        foreach (var name in new[] { nameof(Current), nameof(CurrentSlot), nameof(CurrentStage), nameof(CurrentWeight), nameof(OperationOperator), nameof(CurrentImage), nameof(CaptureState), nameof(UploadState), nameof(PositionIds), nameof(PositionRows), nameof(CurrentCommandTarget), nameof(HistoryCommandTarget), nameof(CanHistoryContinue), nameof(CanHistoryQuery), nameof(CanContinue), nameof(CanContinueUpload), nameof(CanAbandon), nameof(Details), nameof(CapacityState), nameof(AuxiliaryState), nameof(EffectiveConfiguration), nameof(EffectiveConfigurationHeading), nameof(PendingConfiguration), nameof(ConfigurationConfirmationState), nameof(CanConfirmConfiguration) }) Changed(name);
    }
}

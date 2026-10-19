using System.ComponentModel;
using System.Windows;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Platform;

namespace Ipc.Application.Sportmaster.Desktop;

// Presentation only. Every fact/permission comes from the existing application contract.
public sealed class SupplyStationViewModel : ParcelViewModel
{
    string selectedPage = "workstation";
    public SupplyStationPositionViewModel Left { get; }
    public SupplyStationPositionViewModel Right { get; }
    public bool Legacy => Snapshot?.Configuration.Effective.Topology is null;
    public string Station => Snapshot?.Configuration.Effective.StationId ?? "等待站位配置";
    public string TopologyStatus => Legacy ? "单读码兼容配置 · 仅 main 位，未启用双位" : "left / right 独立投递位 · 扫码 → WCS 格口";
    public string SelectedPage
    {
        get => selectedPage;
        set { if (value is not ("workstation" or "history" or "maintenance")) throw new ArgumentException("未安装的页面"); selectedPage = value; Changed(); }
    }
    public SupplyStationViewModel(IParcelApplication application) : base(application, "Sportmaster 供包工作台", "扫码 → WCS 格口")
    {
        Left = new(this, "left"); Right = new(this, "right");
        PropertyChanged += (_, e) => { if (e.PropertyName is nameof(IsCompact) or nameof(Busy)) { Left.NotifyLayout(); Right.NotifyLayout(); } };
    }
    protected override void SnapshotValidityChanged(DateTimeOffset now)
    {
        Left.Update(Snapshot, now, Fresh, ViewportReady); Right.Update(Snapshot, now, Fresh, ViewportReady);
        foreach(var name in new[]{nameof(Legacy),nameof(Station),nameof(TopologyStatus)})Changed(name);
    }
    public Task RefreshDisplayAsync() => RefreshAsync();
    public async Task ReconcilePositionAsync(SupplyStationPositionViewModel position)
    {
        var op = position.Operation;
        if (!CanStartOperation || !CanUseWorkspace || op is null) { Message = "需取得当前位的有效作业快照及可用视图"; return; }
        Selected = op;
        await RunAsync(async () => { var result = await ServiceAsync(() => Application.ReconcileAsync(op.Id)); Message = result.Status + " · " + result.Reason; }, "核对本位原请求");
    }
    public async Task ContinuePositionAsync(SupplyStationPositionViewModel position)
    {
        var op = position.Operation;
        if (!CanUseWorkspace || op is null || !position.CanContinue || Snapshot is not { } displayed) { Message = "当前原阶段不可继续"; return; }
        var inspection = position.InspectionReason;
        await RunAsync(async () =>
        {
            var result = await ServiceAsync(() => Application.CommandAsync(new(Wire.Id(), displayed.Revision, "ContinuePrepared", op.Id, inspection)));
            Message = result.Status + " · " + result.Reason;
        }, "继续本位未尝试阶段");
    }
    long historyOpenGeneration;
    public async Task OpenHistoryAsync(string? operationId = null)
    {
        var generation = Interlocked.Increment(ref historyOpenGeneration); var runtime = Snapshot?.RuntimeId;
        if (operationId is not null) Selected = HistoryItems.FirstOrDefault(x => x.Operation.Id == operationId)?.Operation
            ?? Snapshot?.Operations.FirstOrDefault(x => x.Id == operationId);
        var selection = SelectionGeneration;
        await LoadHistoryAsync();
        if (operationId is null || generation != Volatile.Read(ref historyOpenGeneration) || Snapshot?.RuntimeId != runtime || selection != SelectionGeneration) return;
        var row = HistoryItems.FirstOrDefault(x => x.Operation.Id == operationId);
        if (row is not null) SelectedHistory = row;
        else
        {
            var detail = await ServiceAsync(() => Application.OperationAsync(operationId));
            if (generation != Volatile.Read(ref historyOpenGeneration) || Snapshot?.RuntimeId != runtime || selection != SelectionGeneration) return;
            Selected = detail;
            if (Selected is null) Message = "原作业详情未取得：" + operationId;
        }
    }
    public async Task EmitPositionAsync(SupplyStationPositionViewModel position)
    {
        if (!CanUseWorkspace || !position.CanAdmit) { Message = "当前位不可接纳参考输入"; return; }
        await RunAsync(() =>
        {
            Message = Application.EmitReferenceAt(position.PositionId, position.AdmissionId, position.ParcelCode, Wire.Id())
                ? "模拟观察已排队；实体身份保持，结果由应用服务确认。" : "观察未接纳；请检查当前位阻断。";
            return Task.CompletedTask;
        }, "模拟本位读码");
    }
}

// Stable objects retain each window's drafts and selection when snapshots are refreshed.
public sealed class SupplyStationPositionViewModel : INotifyPropertyChanged
{
    readonly string requestedPosition;
    readonly SupplyStationViewModel owner;
    public event PropertyChangedEventHandler? PropertyChanged;
    public string PositionId { get; private set; }
    public string Heading => PositionId == "main" ? "MAIN · 单读码兼容位" : requestedPosition == "left" ? "LEFT · 左投递位" : "RIGHT · 右投递位";
    public string Accent => requestedPosition == "left" ? "#2563EB" : "#7C3AED";
    public string ReaderId { get; private set; } = "未配置";
    public ParcelOperation? Operation { get; private set; }
    public ParcelInputSnapshot? Input { get; private set; }
    public bool InputUnadmitted => ParcelPresentation.IsUnadmitted(Input);
    public string LatestInputSummary => ParcelPresentation.InputSummary(Input);
    public bool InputFailed => ParcelPresentation.IsInputFailure(Input);
    public bool IsCompact => owner.IsCompact;
    public bool CanStartOperation => owner.CanStartOperation;
    public string Barcode => InputUnadmitted ? ParcelPresentation.InputCode(Input!) : Operation?.ParcelCode ?? "等待包裹";
    public string Slot => InputUnadmitted ? "未取得" : !owner.Fresh ? "旧格口已失效" : Operation?.Wcs?.State == StageState.Accepted ? Operation.Slot ?? "未取得" : "未取得";
    public ParcelStatusPresentation Presentation { get; private set; } = ParcelPresentation.Pending("正在初始化");
    public string Status => Presentation.Text;
    public string OriginalResultSummary => InputUnadmitted && Operation is { } op ? "原件仍保留 · " + ParcelPresentation.Operation(op, owner.Fresh).Text  : "";
    public string OriginalResponsibility => InputUnadmitted && Operation is { } op ? "当前保留原作业：" + op.ParcelCode + " · " + op.Id + " · " + ParcelPresentation.Operation(op, owner.Fresh).Text + "；下方原请求只属于该原作业，格口不属于本次观察。" : "";
    public string DeviceStatus { get; private set; } = "设备状态未取得";
    public string BlockingReasons { get; private set; } = "等待有效快照";
    public string OperationId => Operation?.Id ?? "未取得";
    public string Admission => InputUnadmitted ? Input!.Observation.AdmissionId : Operation?.AdmissionId ?? "未取得";
    public string RequestId => Operation?.Wcs?.Request.Id ?? "未创建";
    public string ResultReason => InputUnadmitted ? Input!.Reason : Operation is { } op
        ? string.Join("\n", new[] { op.Wcs?.Failure ?? op.Reason, ParcelViewModel.DestinationSummary(op) }.Where(s => !string.IsNullOrEmpty(s))) : "";
    public string ObservedAt => "原作业观察：" + (Operation?.ObservedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff zzz") ?? "尚无作业") + "\n原结果时间：" + (Operation?.Wcs?.Replies.LastOrDefault()?.At.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff zzz") ?? "尚无回执");
    public string ImageStatus => Operation?.Capture is { } c ? "采集 " + c.State + (Operation.Upload is { } u ? " · 上传 " + u.State : "") : "未参与采集";
    public bool CanAdmit { get; private set; }
    public bool CanReconcile { get; private set; }
    public bool CanContinue { get; private set; }
    public ParcelOperation[] Recent { get; private set; } = [];
    public string InspectionReason { get; set; } = "";
    public string ParcelCode { get; set; } = "000123456789";
    public string AdmissionId { get; private set; } = "parcel-" + Wire.Id();
    public SupplyStationPositionViewModel(SupplyStationViewModel owner, string position)
    { this.owner = owner; requestedPosition = PositionId = position; }
    public void NewParcel() { AdmissionId = "parcel-" + Wire.Id(); PropertyChanged?.Invoke(this, new(nameof(AdmissionId))); }
    internal void NotifyLayout() { PropertyChanged?.Invoke(this, new(nameof(IsCompact))); PropertyChanged?.Invoke(this, new(nameof(CanStartOperation))); }
    internal void Update(ParcelSnapshot? snapshot, DateTimeOffset now, bool fresh, bool viewportReady)
    {
        var legacy = snapshot?.Configuration.Effective.Topology is null;
        PositionId = legacy && requestedPosition == "left" ? "main" : requestedPosition;
        var position = snapshot?.Positions?.SingleOrDefault(x => x.Id == PositionId);
        var binding = snapshot is null ? null : ParcelBindings.Positions(snapshot.Configuration.Effective).SingleOrDefault(x => x.Id == PositionId);
        ReaderId = binding?.ReaderDeviceId ?? "未配置";
        Recent = snapshot?.Operations.Where(x => (x.PositionId ?? "main") == PositionId)
            .OrderByDescending(x => x.ObservedAt).ThenByDescending(x => x.Id, StringComparer.Ordinal).ToArray() ?? [];
        Input = snapshot?.Inputs?.SingleOrDefault(x => x.PositionId == PositionId);
        var previousOperation = Operation?.Id;
        // A named current operation must match both identities. Never fall back to another parcel.
        Operation = position?.CurrentOperation is { } id ? Recent.FirstOrDefault(x => x.Id == id) : null;
        if (Operation?.Id != previousOperation) InspectionReason = "";
        var device = snapshot?.Devices?.SingleOrDefault(x => x.DeviceId == ReaderId);
        DeviceStatus = !fresh ? "设备旧状态已失效" : device is null ? "设备状态未取得" : device.IsValid(now) ? "读码就绪 · " + device.ConfirmedAt?.ToLocalTime().ToString("HH:mm:ss") : "未就绪 · " + device.ValidityReason(now);
        var identityMissing = position?.CurrentOperation is not null && Operation is null;
        CanAdmit = !owner.Busy && fresh && viewportReady && binding is not null && !identityMissing && position?.Admit == true;
        CanReconcile = !owner.Busy && fresh && viewportReady && Operation is not null;
        CanContinue = !owner.Busy && fresh && viewportReady && Operation is { } op && snapshot?.Availability?.ContinuePrepared.Contains(op.Id) == true;
        BlockingReasons = !fresh ? "快照失效；等待状态读取恢复" : !viewportReady ? "视图空间保护；恢复客户区并人工确认" : binding is null ? "当前配置未启用该投递位" :
            position is null ? "尚未取得投递位状态" : identityMissing ? "当前作业身份未匹配：" + position.CurrentOperation + "；停止本位新操作并重新读取。\n" + string.Join("\n", position.BlockingReasons) : string.Join("\n", position.BlockingReasons);
        Presentation = !fresh ? ParcelPresentation.Stale() : binding is null ? ParcelPresentation.Neutral("未启用") :
            InputUnadmitted ? ParcelPresentation.InputPresentation(Input!) :
            position?.CurrentOperation is not null && Operation is null ? ParcelPresentation.Unknown("当前作业身份未匹配 · 等待新快照") : ParcelPresentation.Operation(Operation, true);
        PropertyChanged?.Invoke(this, new(""));
    }
    public static string StageText(ParcelState state) => state switch
    {
        ParcelState.SlotResolved => "WCS 格口已取得",
        ParcelState.WcsUnknown => "WCS 结果待核对 · 请勿重复放货",
        ParcelState.WcsRejected => "WCS 明确拒绝",
        ParcelState.UnpackRequired => "需拆包 · 本位等待检查处置",
        ParcelState.ResolvingWcs => "WCS 格口处理中",
        ParcelState.Prepared => "原阶段待执行",
        ParcelState.Blocked => "作业阻断",
        ParcelState.WaitingWeight => "等待本件有效重量",
        ParcelState.WeightTimedOut => "本件称重超时",
        ParcelState.ImageUnknown => "图像阶段待核对 · 保留原采集与上传事实",
        ParcelState.ImageRejected => "图像阶段拒绝",
        ParcelState.CapturingImage => "正在采集本件图像",
        ParcelState.UploadingImage => "正在上传原图",
        ParcelState.Abandoned => "未发送意图已处置",
        _ => "阶段：" + state
    };
}

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
    bool isTight;
    public bool IsTight { get => isTight; set { if (isTight == value) return; isTight = value; Changed(); } }
    public SupplyStationPositionViewModel Left { get; }
    public SupplyStationPositionViewModel Right { get; }
    public bool Legacy => Snapshot?.Configuration.Effective.Topology is null;
    public string Station => Snapshot?.Configuration.Effective.StationId ?? "等待站位配置";
    public string TopologyStatus => Legacy ? "单读码兼容配置 · 仅 main 位，未启用双位" : "left / right 独立投递位 · 扫码 → WCS 格口";
    public string WorkStatus => !Fresh ? "状态读取中断" : Snapshot?.Paused == true ? "已暂停新入场" :
        Snapshot?.Operations.Any(op => new[] { op.Wcs, op.Capture, op.Upload }.OfType<StageFact>().Any(stage => stage.State is StageState.Unknown or StageState.Conflict)) == true ? "有结果待核对" :
        Snapshot?.BlockingReasons.Length > 0 ? "有作业阻断" : Status;
    // Display copy only: never infer availability or remove the original blocking facts.
    public string WorkNotice => BlockingReasons;
    const string PauseNotice = "Software admission paused; this is not an emergency stop";
    public bool IsSettingsWorkspace => SelectedPage == "maintenance";
    public bool SettingsHasAttention => !Fresh || Snapshot?.BlockingReasons.Any(x => x != PauseNotice) == true ||
        Snapshot?.Operations.Any(op => new[] { op.Wcs, op.Capture, op.Upload }.OfType<StageFact>().Any(stage => stage.State is StageState.Unknown or StageState.Conflict)) == true;
    public string DisplayWorkNotice => !IsSettingsWorkspace || !Fresh ? WorkNotice :
        string.Join(" · ", Snapshot?.BlockingReasons.Where(x => x != PauseNotice) ?? []) is { Length: > 0 } reasons ? reasons : SettingsHasAttention ? "有原请求结果待核对；请查看诊断。" : "软件暂停不是安全急停";
    public string WorkspaceStatusBackground => IsSettingsWorkspace && !SettingsHasAttention ? "#F6F7F9" : StatusPresentation.Background;
    public string WorkspaceStatusForeground => IsSettingsWorkspace && !SettingsHasAttention ? "#25384B" : StatusPresentation.Foreground;
    public string WorkFeedback => Feedback is "等待相机读码；模拟工程不操作真实设备。" or "模拟观察已排队；实体身份保持，结果由应用服务确认。" ||
        SelectedPage is "workstation" or "maintenance" && Feedback == "已读取完整拓扑与能力库存；表单为当前编辑来源。保存为Pending，受控重启生效。" ? "" : OperatorFeedback;
    public string SelectedPage
    {
        get => selectedPage;
        set { if (value is not ("workstation" or "history" or "maintenance" or "diagnostics")) throw new ArgumentException("未安装的页面"); if (selectedPage == value) return; selectedPage = value; Changed(); Changed(nameof(PageTitle)); Changed(nameof(WorkFeedback)); Changed(nameof(IsSettingsWorkspace)); Changed(nameof(DisplayWorkNotice)); Changed(nameof(WorkspaceStatusBackground)); Changed(nameof(WorkspaceStatusForeground)); }
    }
    public string PageTitle => SelectedPage switch { "history" => "作业记录", "maintenance" => "参数设置", "diagnostics" => "诊断与恢复", _ => "作业" };
    public SupplyStationViewModel(IParcelApplication application) : base(application, "Sportmaster 供包工作台", "扫码 → WCS 格口")
    {
        Left = new(this, "left"); Right = new(this, "right");
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(IsCompact) or nameof(IsTight) or nameof(Busy)) { Left.NotifyLayout(); Right.NotifyLayout(); }
            if (e.PropertyName == nameof(Feedback)) Changed(nameof(WorkFeedback));
        };
    }
    protected override void SnapshotValidityChanged(DateTimeOffset now)
    {
        Left.Update(Snapshot, now, Fresh, ViewportReady); Right.Update(Snapshot, now, Fresh, ViewportReady);
        foreach(var name in new[]{nameof(Legacy),nameof(Station),nameof(TopologyStatus),nameof(WorkStatus),nameof(WorkNotice),nameof(DisplayWorkNotice),nameof(SettingsHasAttention),nameof(WorkspaceStatusBackground),nameof(WorkspaceStatusForeground)})Changed(name);
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
    public string Heading => PositionId == "main" ? "单读码兼容位" : requestedPosition == "left" ? "左投递位" : "右投递位";
    public string Accent => "#192736";
    public string ReaderId { get; private set; } = "未配置";
    public ParcelOperation? Operation { get; private set; }
    public ParcelInputSnapshot? Input { get; private set; }
    public bool InputUnadmitted => ParcelPresentation.IsUnadmitted(Input);
    public string LatestInputSummary => ParcelPresentation.InputSummary(Input);
    public bool InputFailed => ParcelPresentation.IsInputFailure(Input);
    public bool IsCompact => owner.IsCompact;
    public bool IsTight => owner.IsTight;
    public bool CanStartOperation => owner.CanStartOperation;
    public string Barcode => InputUnadmitted ? ParcelPresentation.InputCode(Input!) : Operation?.ParcelCode ?? "等待包裹";
    public bool CurrentTaskAvailable => !InputUnadmitted && owner.Fresh && Operation?.Wcs?.State == StageState.Accepted &&
        (Input is null || Input.OperationId == Operation.Id && (Operation.Acquisition is null ||
            Input.Observation.DeviceId == Operation.Acquisition.DeviceId && Input.Observation.Epoch == Operation.Acquisition.Epoch &&
            Input.Observation.SourceEventId == Operation.Acquisition.SourceEventId && Input.Observation.AdmissionId == Operation.Acquisition.AdmissionId));
    public string Slot => !owner.Fresh ? "旧格口已失效" : CurrentTaskAvailable ? Operation!.Slot ?? "未取得" : "未取得";
    public bool SlotAvailable => CurrentTaskAvailable && Operation!.Slot is not null;
    WcsDestinationResult? CurrentDestination => CurrentTaskAvailable
        ? Operation!.Wcs!.Replies.LastOrDefault(r => r.BusinessAccepted == true && r.RequestId == Operation.Wcs.Request.Id &&
            r.PayloadHash == Operation.Wcs.Request.PayloadHash)?.Destination : null;
    string TaskValue(string? value) => !owner.Fresh ? "已失效" : !CurrentTaskAvailable ? "—" : value ?? "回执未包含";
    public string WaveNo => TaskValue(CurrentDestination?.WaveNo);
    public string TaskId => TaskValue(CurrentDestination?.TaskId);
    public string SkuId => TaskValue(CurrentDestination?.SkuId);
    public string ResponseQuantity => TaskValue(CurrentDestination?.Quantity);
    public string RawCandidates => Input is null ? "尚无观察" : Input.Observation.Codes.Length == 0 ? "未识别到条码" : string.Join("\n", Input.Observation.Codes);
    public string SelectionSummary => Input?.BarcodeSelection is { } selection
        ? selection.Detail + " · 有效候选 " + selection.Matches
        : Input is null ? "等待相机输入" : "旧单码规则 · " + ParcelPresentation.InputText(Input);
    public string SelectedCode => Input?.BarcodeSelection?.SelectedCode ??
        (Input is { Outcome: ParcelInputOutcome.Admitted } && Operation is { } op && Input.OperationId == op.Id ? op.ParcelCode : "未选中");
    public string ScanTime => Input?.Observation.ObservedAt.ToLocalTime().ToString("HH:mm:ss.fff") ?? "尚无观察";
    public string ObservationIdentity => Input is null ? "尚无观察" : "连接周期 " + Input.Observation.Epoch + " · 本周期序号 " + Input.Observation.SourceEventId;
    public double SlotFontSize => !SlotAvailable ? 24 : (Operation!.Slot!.Length, IsCompact) switch
    { (<= 6, false) => 64, (<= 6, true) => 44, (<= 16, false) => 40, (<= 16, true) => 28, (_, false) => 28, _ => 24 };
    public string OperatorInstruction => !owner.Fresh ? "等待有效状态；保留原件，不重复放货。" :
        InputUnadmitted ? (InputFailed ? Input!.Reason + "；检查本次输入及准入条件，原请求仍归原作业。" : "本次未创建新作业；核对下方原件责任。") :
        Operation?.State switch
        {
            ParcelState.WcsUnknown => "请勿重复放货，核对原请求。",
            ParcelState.WcsRejected => "查看详情，核对拒绝原因。",
            ParcelState.UnpackRequired => "本位等待检查处置；查看原回执，不重复放货。",
            ParcelState.ResolvingWcs or ParcelState.SlotResolved => "",
            _ => BlockingReasons.Length > 0 ? BlockingReasons : CanContinue ? "展开详情填写检查说明，再继续原阶段。" : ""
        };
    public ParcelStatusPresentation Presentation { get; private set; } = ParcelPresentation.Pending("正在初始化");
    public string Status => !owner.Fresh ? "状态已失效" : InputUnadmitted ? Input!.Outcome switch
    {
        ParcelInputOutcome.Rejected => ParcelPresentation.InputText(Input),
        ParcelInputOutcome.Blocked => "本次输入未准入",
        ParcelInputOutcome.Duplicate => "重复输入",
        _ => Presentation.Text
    } : Operation?.State switch
    {
        ParcelState.SlotResolved => "格口已取得",
        ParcelState.ResolvingWcs => "正在取格口",
        ParcelState.WcsUnknown => "结果待核对",
        ParcelState.WcsRejected => "业务拒绝",
        _ => Presentation.Text
    };
    public string OriginalResultSummary => InputUnadmitted && Operation is { } op ? "原件保留 · " +
        (!owner.Fresh ? "状态已失效" : op.State is ParcelState.WcsUnknown or ParcelState.ImageUnknown ? "结果待核对" : ParcelPresentation.Operation(op, true).Text) : "";
    public string OriginalResponsibility => InputUnadmitted && Operation is { } op ? "当前保留原作业：" + op.ParcelCode + " · " + op.Id + " · " + ParcelPresentation.Operation(op, owner.Fresh).Text + "；下方原请求只属于该原作业，格口不属于本次观察。" : "";
    public string DeviceStatus { get; private set; } = "设备状态未取得";
    public string DeviceLabel => DeviceStatus.Split(" · ", 2)[0];
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
    public bool CanQueryOriginal => CanReconcile && Operation is { } op && new[] { op.Wcs, op.Capture, op.Upload }.OfType<StageFact>().Any(s => s.State is StageState.Unknown or StageState.Conflict);
    public bool CanContinue { get; private set; }
    public ParcelOperation[] Recent { get; private set; } = [];
    public string InspectionReason { get; set; } = "";
    public string ParcelCode { get; set; } = "000123456789";
    public string AdmissionId { get; private set; } = "parcel-" + Wire.Id();
    public SupplyStationPositionViewModel(SupplyStationViewModel owner, string position)
    { this.owner = owner; requestedPosition = PositionId = position; }
    public void NewParcel() { AdmissionId = "parcel-" + Wire.Id(); PropertyChanged?.Invoke(this, new(nameof(AdmissionId))); }
    internal void NotifyLayout() { PropertyChanged?.Invoke(this, new(nameof(IsCompact))); PropertyChanged?.Invoke(this, new(nameof(IsTight))); PropertyChanged?.Invoke(this, new(nameof(SlotFontSize))); PropertyChanged?.Invoke(this, new(nameof(CanStartOperation))); }
    internal void Update(ParcelSnapshot? snapshot, DateTimeOffset now, bool fresh, bool viewportReady)
    {
        var legacy = snapshot?.Configuration.Effective.Topology is null;
        PositionId = legacy && requestedPosition == "left" ? "main" : requestedPosition;
        var position = snapshot?.Positions?.SingleOrDefault(x => x.Id == PositionId);
        var binding = snapshot is null ? null : ParcelBindings.Positions(snapshot.Configuration.Effective).SingleOrDefault(x => x.Id == PositionId);
        ReaderId = binding?.ReaderDeviceId ?? "未配置";
        var recent = snapshot?.Operations.Where(x => (x.PositionId ?? "main") == PositionId)
            .OrderByDescending(x => x.ObservedAt).ThenByDescending(x => x.Id, StringComparer.Ordinal).ToArray() ?? [];
        Input = snapshot?.Inputs?.SingleOrDefault(x => x.PositionId == PositionId);
        var previousOperation = Operation?.Id;
        // A named current operation must match both identities. Never fall back to another parcel.
        Operation = position?.CurrentOperation is { } id ? recent.FirstOrDefault(x => x.Id == id) : null;
        // The small recent grid displays only these facts. The command/detail owner
        // above always comes from the current authoritative snapshot, never this grid.
        if (!recent.Select(x => (x.Id, x.ParcelCode, x.State, x.Slot)).SequenceEqual(Recent.Select(x => (x.Id, x.ParcelCode, x.State, x.Slot)))) Recent = recent;
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
        // A wildcard invalidation reassigns every TextBox/ItemsSource on every poll,
        // including local drafts. Publish only changed display facts, never drafts.
        foreach (var (name, value) in new (string, object?)[] {
            (nameof(PositionId), PositionId), (nameof(Heading), Heading), (nameof(ReaderId), ReaderId),
            (nameof(Barcode), Barcode), (nameof(Slot), Slot), (nameof(SlotAvailable), SlotAvailable), (nameof(SlotFontSize), SlotFontSize),
            (nameof(CurrentTaskAvailable), CurrentTaskAvailable), (nameof(WaveNo), WaveNo), (nameof(TaskId), TaskId),
            (nameof(SkuId), SkuId), (nameof(ResponseQuantity), ResponseQuantity), (nameof(RawCandidates), RawCandidates),
            (nameof(SelectionSummary), SelectionSummary), (nameof(SelectedCode), SelectedCode), (nameof(ScanTime), ScanTime), (nameof(ObservationIdentity), ObservationIdentity),
            (nameof(Presentation), Presentation), (nameof(Status), Status), (nameof(OperatorInstruction), OperatorInstruction),
            (nameof(OriginalResultSummary), OriginalResultSummary), (nameof(OriginalResponsibility), OriginalResponsibility),
            (nameof(DeviceStatus), DeviceStatus), (nameof(DeviceLabel), DeviceLabel), (nameof(BlockingReasons), BlockingReasons), (nameof(OperationId), OperationId),
            (nameof(Admission), Admission), (nameof(RequestId), RequestId), (nameof(ResultReason), ResultReason),
            (nameof(ObservedAt), ObservedAt), (nameof(LatestInputSummary), LatestInputSummary), (nameof(ImageStatus), ImageStatus),
            (nameof(CanAdmit), CanAdmit), (nameof(CanReconcile), CanReconcile), (nameof(CanQueryOriginal), CanQueryOriginal), (nameof(CanContinue), CanContinue),
            (nameof(Recent), Recent), (nameof(InspectionReason), InspectionReason) })
        {
            if (published.TryGetValue(name, out var prior) && Equals(prior, value)) continue;
            published[name] = value; PropertyChanged?.Invoke(this, new(name));
        }
    }
    readonly Dictionary<string, object?> published = [];
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

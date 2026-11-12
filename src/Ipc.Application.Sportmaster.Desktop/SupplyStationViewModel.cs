using System.ComponentModel;
using System.Windows;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Platform;

namespace Ipc.Application.Sportmaster.Desktop;

// Presentation only. Every fact/permission comes from the existing application contract.
public sealed partial class SupplyStationViewModel : ParcelViewModel
{
    string selectedPage = "workstation";
    string inputTargetPosition = "";
    public string InputTargetPosition => inputTargetPosition;
    internal void SetInputTarget(string value)
    {
        if (inputTargetPosition == value) return;
        inputTargetPosition = value; Changed(nameof(InputTargetPosition)); Left.NotifyInputTarget(); Right.NotifyInputTarget();
    }
    bool isTight;
    double chuteScale = 1;
    public double ChuteScale { get => chuteScale; internal set { if (Math.Abs(chuteScale - value) < .001) return; chuteScale = value; Left.NotifyLayout(); Right.NotifyLayout(); } }
    public bool IsTight { get => isTight; set { if (isTight == value) return; isTight = value; Changed(); } }
    public SupplyStationPositionViewModel Left { get; }
    public SupplyStationPositionViewModel Right { get; }
    public string Station => Snapshot?.Configuration.Effective.StationId ?? "等待供包台配置";
    public enum RcsConnectionState { Unknown, Deferred, Simulation, Offline, AwaitingConfirmation, Online }
    public RcsConnectionState RcsState
    {
        get
        {
            if (!Fresh || Snapshot is not { } s) return RcsConnectionState.Unknown;
            if (s.Configuration.Effective.Wcs is { } deferred && ParcelConnectionSetup.IsDeferred(s.Configuration.Effective, deferred.InstanceId)) return RcsConnectionState.Deferred;
            if (s.Configuration.Effective.Mode == RunMode.Simulation) return RcsConnectionState.Simulation;
            if (s.Configuration.Effective.Wcs is not { Contract: "SPORTMASTER-RCS-SORTER-v1" } binding) return RcsConnectionState.Deferred;
            var health = s.Health.SingleOrDefault(h => h.InstanceId == binding.InstanceId); var now = DateTimeOffset.UtcNow;
            if (health is null || health.ConfirmedAt > now || health.ValidUntil <= now || health.ValidUntil <= health.ConfirmedAt) return RcsConnectionState.Unknown;
            if (!health.Ready) return RcsConnectionState.Offline;
            return health.RemoteConfirmation == RemoteConnectionConfirmation.ResponseReceived ? RcsConnectionState.Online : RcsConnectionState.AwaitingConfirmation;
        }
    }
    public string RcsServiceStatus => RcsState switch
    {
        RcsConnectionState.Online => "RCS 服务：在线",
        RcsConnectionState.Offline => "RCS 服务：离线",
        RcsConnectionState.Simulation => "RCS 服务：模拟",
        RcsConnectionState.Deferred => "RCS 服务：待配置",
        RcsConnectionState.AwaitingConfirmation => "RCS 服务：等待连接确认",
        _ => "RCS 服务：状态未知"
    };
    public ParcelStatusPresentation RcsPresentation => RcsState switch
    {
        RcsConnectionState.Online => ParcelPresentation.Accepted("RCS：在线"),
        RcsConnectionState.Offline => ParcelPresentation.Rejected("RCS：离线"),
        RcsConnectionState.Simulation => ParcelPresentation.Neutral("RCS：模拟连接"),
        RcsConnectionState.Deferred => ParcelPresentation.Unknown("RCS：待配置"),
        _ => ParcelPresentation.Unknown("RCS：待确认")
    };
    bool checkingRcs;
    public string RcsButtonText => checkingRcs ? "RCS：检查中" : RcsPresentation.Text;
    public bool CanCheckRcs => !checkingRcs && Fresh && Application is IStationRcsHeartbeat &&
        RcsState is not (RcsConnectionState.Deferred or RcsConnectionState.Simulation);
    internal async Task CheckRcsAsync()
    {
        if (!CanCheckRcs || Application is not IStationRcsHeartbeat heartbeat) return;
        checkingRcs = true;
        Changed(nameof(CanCheckRcs)); Changed(nameof(RcsButtonText));
        try
        {
            var result = await ServiceAsync(() => heartbeat.CheckRcsHeartbeatAsync(CancellationToken.None));
            Message = result switch
            {
                RcsHeartbeatResult.Received => "已收到 RCS 心跳响应。",
                RcsHeartbeatResult.Failed => "未收到有效的 RCS 心跳响应，请检查 RCS 连接后重试。",
                RcsHeartbeatResult.NoPositions => "没有可发送心跳的相机编码，请检查 RCS 相机编码配置。",
                RcsHeartbeatResult.Busy => "正在检查 RCS 心跳，请稍候。",
                _ => "当前无法检查 RCS 心跳，请稍后重试。"
            };
        }
        catch (Exception) { Message = "RCS 心跳检查未完成，请稍后重试。"; }
        finally { checkingRcs = false; Changed(nameof(CanCheckRcs)); Changed(nameof(RcsButtonText)); }
    }
    // Display copy only: never infer availability or remove the original blocking facts.
    public string WorkNotice => BlockingReasons;
    public bool IsInternalWorkspace => SelectedPage != "workstation";
    public bool IsSettingsNavigation => SelectedPage == "maintenance";
    public bool IsSettingsWorkspace => SelectedPage is "maintenance" or "logs";
    public string SettingsRunMode => (Snapshot?.Configuration.Effective.Mode switch
    { RunMode.Simulation => "模拟模式", RunMode.Production => "生产模式", _ => "模式未确认" }) + " · 软件暂停不是安全急停";
    public string WorkspaceStatusBackground => OperatorTokens.Neutral;
    public string WorkspaceStatusForeground => Fresh && !Left.SupplyEnabled && !Right.SupplyEnabled ? OperatorTokens.Warning : OperatorTokens.Ink;
    internal void ReportBarcodeFeedback(ParcelBarcodeReceipt receipt, string value) => SetPresentationMessage(value, receipt.Status == "Admitted" ? PresentationFeedbackKind.ResultMirrored : PresentationFeedbackKind.General);
    public string WorkFeedback => SelectedPage == "maintenance" ? ChineseInterfaceText.ParameterMessage(TaskFeedback) : TaskFeedback;
    // This release hides page entrances only; the business ledger and retained pages remain.
    public bool ShowExceptionAndStatisticsEntries => false;
    public Visibility ExceptionAndStatisticsEntryVisibility => ShowExceptionAndStatisticsEntries ? Visibility.Visible : Visibility.Collapsed;
    public string SelectedPage
    {
        get => selectedPage;
        set { if (value is not ("workstation" or "history" or "maintenance" or "diagnostics" or "logs" or "debug" or "statistics" or "exceptions")) throw new ArgumentException("未安装的页面"); if (selectedPage == value) return; selectedPage = value; Changed(); Changed(nameof(PageTitle)); Changed(nameof(WorkFeedback)); Changed(nameof(IsInternalWorkspace)); Changed(nameof(IsSettingsNavigation)); Changed(nameof(IsSettingsWorkspace)); Changed(nameof(WorkspaceStatusBackground)); Changed(nameof(WorkspaceStatusForeground)); }
    }
    public string PageTitle => SelectedPage switch { "statistics" => "统计与班次", "exceptions" => "异常处理", "history" => "记录", "maintenance" => "设置", "diagnostics" => "设备与诊断", "debug" => "调试", "logs" => "日志", _ => "作业" };
    public SupplyStationViewModel(IParcelApplication application) : base(application, "供包工作台", "扫码 → 上游格口")
    {
        Left = new(this, "left"); Right = new(this, "right");
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(StationDisplaySummary)) Changed(nameof(Station));
            if (e.PropertyName is nameof(IsCompact) or nameof(IsTight) or nameof(Busy)) { Left.NotifyLayout(); Right.NotifyLayout(); }
            if (e.PropertyName == nameof(SelectedPage)) SetFeedbackContext(SelectedPage);
            if (e.PropertyName == nameof(TaskFeedback)) Changed(nameof(WorkFeedback));
            if (e.PropertyName is nameof(Busy) or nameof(IsManagementMode)) NotifyOperatorWorkspace();
        };
    }
    protected override void SnapshotValidityChanged(DateTimeOffset now)
    {
        Left.Update(Snapshot, now, Fresh, ViewportReady); Right.Update(Snapshot, now, Fresh, ViewportReady);
        NotifyOperatorWorkspace();
        foreach (var name in new[] { nameof(Station), nameof(WorkNotice), nameof(WorkspaceStatusBackground), nameof(WorkspaceStatusForeground), nameof(SettingsRunMode), nameof(RcsState), nameof(RcsServiceStatus), nameof(RcsPresentation), nameof(RcsButtonText), nameof(CanCheckRcs) }) Changed(name);
    }
    public Task RefreshDisplayAsync() => RefreshAsync();
    public async Task ReconcilePositionAsync(SupplyStationPositionViewModel position)
    {
        var op = position.Operation;
        if (!CanStartOperation || !CanUseWorkspace || op is null) { Message = "本位状态未就绪"; return; }
        Selected = op;
        await RunAsync(async () => { var result = await ServiceAsync(() => Application.ReconcileAsync(op.Id)); Message = result.Status + " · " + result.Reason; }, "查询本位作业结果");
    }
    public async Task ContinuePositionAsync(SupplyStationPositionViewModel position)
    {
        var op = position.Operation;
        if (!CanUseWorkspace || op is null || !position.CanContinue || Snapshot is not { } displayed) { Message = "当前作业不能继续"; return; }
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
            if (Selected is null) Message = "作业详情读取失败：" + operationId;
        }
    }
    public async Task EmitPositionAsync(SupplyStationPositionViewModel position)
    {
        if (!CanUseWorkspace || !position.CanAdmit) { Message = "当前位不可接纳参考输入"; return; }
        await RunAsync(() =>
        {
            var queued = Application.EmitReferenceAt(position.PositionId, position.AdmissionId, position.ParcelCode, Wire.Id());
            SetPresentationMessage(queued ? "模拟扫描已提交，等待处理结果。" : "模拟扫描未受理，请查看本位受阻原因。", queued ? PresentationFeedbackKind.ObservationQueued : PresentationFeedbackKind.General);
            return Task.CompletedTask;
        }, "模拟本位读码");
    }
}

// Stable objects retain each window's drafts and selection when snapshots are refreshed.
public sealed partial class SupplyStationPositionViewModel : INotifyPropertyChanged
{
    readonly string requestedPosition;
    readonly SupplyStationViewModel owner;
    public event PropertyChangedEventHandler? PropertyChanged;
    public string PositionId { get; private set; }
    public string Heading => requestedPosition == "left" ? "左投递位" : "右投递位";
    StationVehicleState? vehicle;
    public string VehicleNumberText => vehicle is null ? "小车" : "小车 " + vehicle.VehicleNo;
    public string VehicleStatusText => Arrival is { RecoveryReason: not null } ? "小车：请求待核对" : Arrival is { PlacementClosed: true } ? "小车：等待取件确认" : Arrival is { Confirmed: false } ? "小车：待确认" : Arrival is { Submitted: true } ? VehicleNumberText + (Operation?.Wcs?.State is StageState.Prepared or StageState.Dispatching ? " · 处理中" : " · 本车已处理") : vehicle is null ? "小车：待确认" : VehicleNumberText + " · " + (vehicle.Status switch
    { "IDLE" => CanAdmit || CanSubmitBarcode ? "空闲，可以投包" : "空闲，等待就绪", "TASKING" => "工作中，不可以投包", _ => "状态待确认" });
    public string CompactVehicleStatusText => Arrival?.RecoveryReason is not null ? "请求待核对 勿投包" : Arrival is { PlacementClosed: true } ? "等待取件确认" : Arrival is { Confirmed: false } ? "待确认" : Arrival is { Submitted: true } ? Operation?.Wcs?.State is StageState.Prepared or StageState.Dispatching ? "处理中 勿投包" : "已处理 勿投包" : vehicle?.Status switch { "IDLE" => CanAdmit || CanSubmitBarcode ? "空闲 可投包" : "空闲 待就绪", "TASKING" => "工作中 勿投包", _ => "待确认" };
    public string VehicleStatusForeground => Arrival is { Confirmed: false } or { Submitted: true } or { PlacementClosed: true } or { RecoveryReason: not null } ? OperatorTokens.Muted : vehicle?.Status switch { "IDLE" => CanAdmit || CanSubmitBarcode ? OperatorTokens.Success : OperatorTokens.Muted, "TASKING" => OperatorTokens.Danger, _ => OperatorTokens.Muted };
    public bool InputTargetActive => owner.InputTargetPosition == PositionId;
    public string InputTargetBrush => InputTargetActive ? OperatorTokens.Accent : "Transparent";
    internal void SetInputFocused(bool focused) { if (focused && !SupplyEnabled) return; if (focused || InputTargetActive) owner.SetInputTarget(focused ? PositionId : ""); }
    internal void NotifyInputTarget() { PropertyChanged?.Invoke(this, new(nameof(InputTargetActive))); PropertyChanged?.Invoke(this, new(nameof(InputTargetBrush))); }
    public string ReaderId { get; private set; } = "未配置";
    public string ReaderIdentity { get; private set; } = "未配置";
    public string CameraAddress { get; private set; } = "尚未配置地址";
    public ParcelOperation? Operation { get; private set; }
    public ParcelInputSnapshot? Input { get; private set; }
    public bool InputUnadmitted => ParcelPresentation.IsUnadmitted(Input);
    public string LatestInputSummary => ParcelPresentation.InputSummary(Input);
    public bool InputFailed => (!RoundReadActive || CurrentRound?.AutoExpired == true) && ParcelPresentation.IsInputFailure(Input);
    public SmArrivalSnapshot? Arrival => owner.Snapshot?.Positions?.SingleOrDefault(p => p.Id == PositionId)?.Arrival;
    public SmParcelRound? CurrentRound => owner.Snapshot?.Rounds.SingleOrDefault(r => r.Id == owner.Snapshot.Positions?.SingleOrDefault(p => p.Id == PositionId)?.CurrentRoundId);
    public bool HasCurrentInput => CurrentRound is not null;
    public bool IsCompact => owner.IsCompact;
    public bool IsTight => owner.IsTight;
    public bool CanStartOperation => owner.CanStartOperation;
    public string Barcode => ObservedBarcode;
    public bool CurrentTaskAvailable => owner.Fresh && CurrentRound is { } round && Operation?.RoundId == round.Id &&
        Operation.Wcs?.State == StageState.Accepted && round.SuccessAt is not null;
    public string Slot => !owner.Fresh ? "旧格口已失效" : CurrentTaskAvailable ? string.IsNullOrWhiteSpace(Operation!.Slot) ? OperatorCopy.NotReturned : Operation.Slot : "未取得";
    public string AttentionReason => !owner.Fresh ? "状态中断，旧格口已失效。" :
        ParcelPresentation.RcsTimedOut(Operation) ? OperatorCopy.TimeoutUnconfirmed :
        CenteredRcsRejection ? CenteredStateTitle :
        CenteredAttention ? CenteredStateReason :
        InputUnadmitted ? ParcelPresentation.InputReason(Input!) :
        Operation is null ? IdleReason : "";
    string IdleReason => owner.Snapshot is not { } s ? "等待状态读取。" :
        s.Positions?.SingleOrDefault(p => p.Id == PositionId) is { Admit: true } ? "" :
        ParcelConnectionSetup.IsDeferred(s.Configuration.Effective, ReaderId) ? "相机未启用，可手动输入条码。" :
        s.Configuration.Effective.Wcs is { } w && ParcelConnectionSetup.IsDeferred(s.Configuration.Effective, w.InstanceId) ? "RCS 未配置，请联系维护人员。" :
        s.Paused ? OperatorCopy.Paused : s.Positions?.SingleOrDefault(p => p.Id == PositionId)?.Admit == true ? "" :
        "本位暂不能供包，请联系维护人员。";
    public bool SlotAvailable => CurrentTaskAvailable && !string.IsNullOrWhiteSpace(Operation!.Slot);
    WcsDestinationResult? CurrentDestination => CurrentTaskAvailable
        ? Operation!.Wcs!.Replies.LastOrDefault(r => r.BusinessAccepted == true && r.RequestId == Operation.Wcs.Request.Id &&
            r.PayloadHash == Operation.Wcs.Request.PayloadHash)?.Destination : null;
    string TaskValue(string? value) => !owner.Fresh ? "已失效" : !CurrentTaskAvailable ? "—" : string.IsNullOrWhiteSpace(value) ? OperatorCopy.NotReturned : value;
    public string WaveNo => TaskValue(CurrentDestination?.WaveNo);
    public string TaskId => TaskValue(CurrentDestination?.TaskId);
    public string SkuId => TaskValue(CurrentDestination?.SkuId);
    public string ResponseQuantity => TaskValue(CurrentDestination?.Quantity);
    public string RawCandidates => Input is null ? "尚无观察" : Input.Observation.Codes.Length == 0 ? "未识别到条码" : string.Join("\n", Input.Observation.Codes);
    public string SelectionSummary => Input?.BarcodeSelection is { } selection
        ? selection.Detail + " · 有效条码 " + selection.Matches
        : Input is null ? "等待相机输入" : "旧单码规则 · " + ParcelPresentation.InputText(Input);
    public string SelectedCode => Input?.BarcodeSelection?.SelectedCode ??
        (Input is { Outcome: ParcelInputOutcome.Admitted } && Operation is { } op && Input.OperationId == op.Id ? op.ParcelCode : "未选中");
    public string ScanTime => Input?.Observation.ObservedAt.ToLocalTime().ToString("HH:mm:ss.fff") ?? "尚无观察";
    public string ScanSummary => Input is null ? "等待相机扫码或输入条码" : (Input.Observation.OperatorInput is { } entry ? (entry.Kind == "PdaKeyboard" ? "PDA键盘输入 " : "手动键盘输入 ") : "相机读取 ") + ScanTime + " · 条码 " + Input.Observation.Codes.Length + " 项";
    public string ObservationIdentity => Input is null ? "尚无观察" : "连接周期 " + Input.Observation.Epoch + " · 本周期序号 " + Input.Observation.SourceEventId;
    public double SlotFontSize => !SlotAvailable ? UiTypography.Placeholder : UiTypography.ResponsiveSlotSize(Operation!.Slot!, owner.ChuteScale);
    public bool NeedsAttention => !owner.Fresh || CurrentRound?.AutoExpired == true || HasUncertainOriginal || !RoundReadActive && (InputUnadmitted || Presentation.NeedsAttention);
    public string OperatorInstruction => !owner.Fresh ? OperatorCopy.StaleStep :
        InputUnadmitted ? ParcelPresentation.InputReason(Input!) :
        Operation?.State switch
        {
            ParcelState.WcsUnknown when ParcelPresentation.RcsNotDelivered(Operation) => "RCS未连接，本次请求未送达；连接恢复后请取下包裹再重新放上。",
            ParcelState.WcsUnknown when ParcelPresentation.RcsTimedOut(Operation) => OperatorCopy.TimeoutUnconfirmed,
            ParcelState.WcsUnknown => OperatorCopy.UnconfirmedStep,
            ParcelState.WcsRejected when ParcelPresentation.RcsManualHandling(Operation) => "请取下包裹后人工判断，停用本位，等RCS撤车确认后再启用并等待新车。",
            ParcelState.WcsRejected => ParcelPresentation.RcsException(Operation) ?? OperatorCopy.RejectedStep,
            ParcelState.UnpackRequired => "勿重复上件：按拆包明细检查包裹，处理完再继续。",
            ParcelState.ResolvingWcs or ParcelState.SlotResolved => "",
            _ => BlockingReasons.Length > 0 ? BlockingReasons : CanContinue ? "检查包裹后填写检查说明，再点“继续处理”。" : ""
        };
    public ParcelStatusPresentation Presentation { get; private set; } = ParcelPresentation.Pending("正在初始化");
    public string Status => !owner.Fresh ? "状态中断" : InputUnadmitted ? Input!.Outcome switch
    {
        ParcelInputOutcome.Rejected => ParcelPresentation.InputText(Input),
        ParcelInputOutcome.Blocked => AdmissionMessage(Input).Title,
        ParcelInputOutcome.Duplicate => "重复扫描",
        _ => Presentation.Text
    } : Operation?.Wcs?.State == StageState.Conflict ? "格口结果矛盾" : Operation?.State switch
    {
        ParcelState.SlotResolved => "投递格口",
        ParcelState.ResolvingWcs => "正在获取格口",
        ParcelState.WcsUnknown when ParcelPresentation.RcsNotDelivered(Operation) => "RCS未连接 · 本次未送达",
        ParcelState.WcsUnknown when ParcelPresentation.RcsTimedOut(Operation) => "取格口超时",
        ParcelState.WcsUnknown => "格口未确认",
        ParcelState.WcsRejected => "未分配格口",
        _ => Operation is null ? IdleReason.Length > 0 ? "暂不能供包" : "等待扫码或输入条码" : Presentation.Text
    };
    public string OriginalResponsibility => InputUnadmitted && Operation is { } op ?
        "关联原作业 " + op.Id + " · " + op.ParcelCode + " · " + ParcelPresentation.Operation(op, owner.Fresh).Text : "";
    public string DeviceStatus { get; private set; } = "设备状态未取得";
    public ParcelStatusPresentation DevicePresentation { get; private set; } = ParcelPresentation.Unknown("相机：状态待确认");
    public string RcsServiceStatus => owner.RcsServiceStatus;
    public string DeviceLabel => DeviceStatus.Split(" · ", 2)[0];
    public string NextStep => !owner.Fresh ? OperatorCopy.StaleStep : InputUnadmitted ?
        OperatorCopy.RescanStep :
        SlotAvailable ? "按格口投递；扫描下一件后自动更新。" :
        CurrentTaskAvailable ? "RCS 未返回格口：包裹暂放一旁，通知维护人员。" :
        Operation?.State == ParcelState.ResolvingWcs ? "等待格口返回，勿重复扫描。" :
        ParcelPresentation.RcsNotDelivered(Operation) ? "RCS未连接，本次请求未送达；连接恢复后请取下包裹再重新放上。" :
        ParcelPresentation.RcsTimedOut(Operation) ? OperatorCopy.TimeoutUnconfirmed :
        HasUncertainOriginal ? OperatorCopy.UnconfirmedStep :
        ParcelPresentation.RcsManualHandling(Operation) ? "请取下包裹后人工判断，停用本位，等RCS撤车确认后再启用并等待新车。" :
        Operation?.State == ParcelState.WcsRejected ? OperatorCopy.RejectedStep :
        BlockingReasons.Length > 0 ? "暂不能供包，请通知维护人员。" : "放上包裹由相机扫码，或输入完整条码后按回车。";
    public string BlockingReasons { get; private set; } = "等待状态更新";
    public string OperationId => Operation?.Id ?? "未取得";
    public string Admission => InputUnadmitted ? Input!.Observation.AdmissionId : Operation?.AdmissionId ?? "未取得";
    public string RequestId => Operation?.Wcs?.Request.Id ?? "未创建";
    public string ResultReason => InputUnadmitted ? Input!.Reason : Operation is { } op
        ? string.Join("\n", new[] { op.Wcs?.Failure ?? op.Reason, ParcelViewModel.DestinationSummary(op) }.Where(s => !string.IsNullOrEmpty(s))) : "";
    public string ObservedAt => "作业扫描：" + (Operation?.ObservedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff zzz") ?? "尚无作业") + "\n原结果时间：" + (Operation?.Wcs?.Replies.LastOrDefault()?.At.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff zzz") ?? "尚无返回");
    public string ImageStatus => Operation?.Capture is { } c ? "采集 " + c.State + (Operation.Upload is { } u ? " · 上传 " + u.State : "") : "未参与采集";
    public bool CanAdmit { get; private set; }
    public bool CanSubmitBarcode => SupplyEnabled && owner.CanUseWorkspace && owner.CanStartOperation && owner.Snapshot?.Positions?.SingleOrDefault(p => p.Id == PositionId)?.BarcodeInput?.Manual == true;
    public bool CanReconcile { get; private set; }
    public bool CanQueryOriginal => owner.IsManagementMode && CanReconcile && HasUncertainOriginal;
    public bool HasUncertainOriginal => Operation is { } op &&
        (op.Wcs?.State is StageState.Unknown or StageState.Conflict || op.Capture?.State is StageState.Unknown or StageState.Conflict ||
            op.Upload?.State is StageState.Unknown or StageState.Conflict);
    public bool HasContinuation => Operation is { } op && owner.Snapshot?.Availability?.ContinuePrepared.Contains(op.Id) == true;
    public bool CanContinue { get; private set; }
    public ParcelOperation[] Recent { get; private set; } = [];
    public string InspectionReason { get; set; } = "";
    public string ParcelCode { get; set; } = "000123456789";
    public string AdmissionId { get; private set; } = "parcel-" + Wire.Id();
    public SupplyStationPositionViewModel(SupplyStationViewModel owner, string position)
    { this.owner = owner; requestedPosition = PositionId = position; }
    public void NewParcel() { AdmissionId = "parcel-" + Wire.Id(); PropertyChanged?.Invoke(this, new(nameof(AdmissionId))); }
    internal void NotifyLayout() { PropertyChanged?.Invoke(this, new(nameof(IsCompact))); PropertyChanged?.Invoke(this, new(nameof(IsTight))); PropertyChanged?.Invoke(this, new(nameof(SlotFontSize))); PropertyChanged?.Invoke(this, new(nameof(CanStartOperation))); PropertyChanged?.Invoke(this, new(nameof(CanSubmitBarcode))); NotifySlotDisplay(); }
    internal void Update(ParcelSnapshot? snapshot, DateTimeOffset now, bool fresh, bool viewportReady)
    {
        PositionId = requestedPosition;
        var position = snapshot?.Positions?.SingleOrDefault(x => x.Id == PositionId);
        var binding = snapshot is null ? null : ParcelBindings.Positions(snapshot.Configuration.Effective).SingleOrDefault(x => x.Id == PositionId);
        ReaderId = binding?.ReaderDeviceId ?? "未配置";
        var reader = snapshot is null ? null : ParcelBindings.Readers(snapshot.Configuration.Effective).SingleOrDefault(r => r.DeviceId == ReaderId);
        var extension = snapshot?.Configuration.Effective.Extensions?.Instances.SingleOrDefault(x => reader?.Driver == "extension:" + x.Id);
        var address = extension?.Parameters.GetValueOrDefault("device.ip");
        if (reader?.Driver == "tcp-line") address = reader.Parameters?.GetValueOrDefault("device.ip");
        CameraAddress = string.IsNullOrWhiteSpace(address) ? "尚未配置地址" : "相机地址：" + address;
        ReaderIdentity = string.IsNullOrWhiteSpace(address) ? ReaderId : address + " · " + ReaderId;
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
        DevicePresentation = !fresh ? ParcelPresentation.Unknown("相机：状态中断") :
            snapshot is not null && ParcelConnectionSetup.IsDeferred(snapshot.Configuration.Effective, ReaderId) ? ParcelPresentation.Unknown("相机：待配置") : position?.CameraConnectionSuspended == true ?
            SupplyDisabled ? ParcelPresentation.Neutral("相机：已断开（本位停用）") : ParcelPresentation.Unknown("相机：等待连接") :
            SupplyDisabled ? ParcelPresentation.Unknown("相机：正在断开或释放待确认") : FastReaderBlocked ? ParcelPresentation.Rejected("相机：离线或恢复中") :
            device is null ? ParcelPresentation.Unknown("相机：待确认") :
            device.IsValid(now) ? snapshot?.Configuration.Effective.Mode == RunMode.Simulation
                ? ParcelPresentation.Neutral("相机：模拟连接") : ParcelPresentation.Accepted("相机：在线") :
            device.Health is DeviceHealth.Disconnected or DeviceHealth.Faulted or DeviceHealth.Stopped or DeviceHealth.Stopping
                ? ParcelPresentation.Rejected("相机：离线") : ParcelPresentation.Unknown("相机：待确认");
        DeviceStatus = DevicePresentation.Text + (device?.ConfirmedAt is { } confirmed ? " · 最后确认 " + confirmed.ToLocalTime().ToString("HH:mm:ss") : "");
        vehicle = fresh && position?.Vehicle is { } status && status.ReceivedAt <= now && status.ValidUntil > now ? status : null;
        var identityMissing = position?.CurrentOperation is not null && Operation is null;
        CanAdmit = !FastReaderBlocked && !owner.Busy && fresh && viewportReady && binding is not null && !identityMissing && position?.Admit == true;
        CanReconcile = !owner.Busy && fresh && viewportReady && Operation is not null;
        CanContinue = !owner.Busy && fresh && viewportReady && Operation is { } op && snapshot?.Availability?.ContinuePrepared.Contains(op.Id) == true;
        BlockingReasons = !fresh ? "状态读取中断，等待恢复" : !viewportReady ? "窗口过小，请最大化窗口" : binding is null ? "本投递位未启用" :
            position is null ? "正在读取投递位状态" : identityMissing ? "本位作业信息不一致，已停止新操作并重新读取（作业 " + position.CurrentOperation + "）。\n" + OperatorCopy.JoinReasons(position.BlockingReasons.Select(ChineseInterfaceText.Notice), "\n") : OperatorCopy.JoinReasons(position.BlockingReasons.Select(ChineseInterfaceText.Notice), "\n");
        Presentation = !fresh ? ParcelPresentation.Stale() : binding is null ? ParcelPresentation.Neutral("未启用") :
            InputUnadmitted ? ParcelPresentation.InputPresentation(Input!) :
            position?.CurrentOperation is not null && Operation is null ? ParcelPresentation.Unknown("作业信息不一致 · 正在重新读取") : ParcelPresentation.Operation(Operation, true);
        UpdateSlotMemory(snapshot, now, fresh, viewportReady);
        NotifySlotDisplay();
        // A wildcard invalidation reassigns every TextBox/ItemsSource on every poll,
        // including local drafts. Publish only changed display facts, never drafts.
        foreach (var (name, value) in new (string, object?)[] {
            (nameof(PositionId), PositionId), (nameof(Heading), Heading), (nameof(ReaderId), ReaderId), (nameof(ReaderIdentity), ReaderIdentity), (nameof(CameraAddress), CameraAddress), (nameof(DevicePresentation), DevicePresentation),
            (nameof(VehicleNumberText), VehicleNumberText), (nameof(VehicleStatusText), VehicleStatusText),
            (nameof(CompactVehicleStatusText), CompactVehicleStatusText), (nameof(VehicleStatusForeground), VehicleStatusForeground),
            (nameof(Barcode), Barcode), (nameof(Slot), Slot), (nameof(SlotAvailable), SlotAvailable), (nameof(NeedsAttention), NeedsAttention), (nameof(SlotFontSize), SlotFontSize),
            (nameof(CurrentTaskAvailable), CurrentTaskAvailable), (nameof(HasCurrentInput), HasCurrentInput), (nameof(AttentionReason), AttentionReason), (nameof(WaveNo), WaveNo), (nameof(TaskId), TaskId),
            (nameof(SkuId), SkuId), (nameof(ResponseQuantity), ResponseQuantity), (nameof(RawCandidates), RawCandidates),
            (nameof(SelectionSummary), SelectionSummary), (nameof(SelectedCode), SelectedCode), (nameof(ScanTime), ScanTime), (nameof(ScanSummary), ScanSummary), (nameof(ObservationIdentity), ObservationIdentity),
            (nameof(Presentation), Presentation), (nameof(Status), Status), (nameof(OperatorInstruction), OperatorInstruction),
            (nameof(OriginalResponsibility), OriginalResponsibility),
            (nameof(RcsServiceStatus), RcsServiceStatus), (nameof(DeviceStatus), DeviceStatus), (nameof(DeviceLabel), DeviceLabel), (nameof(BlockingReasons), BlockingReasons), (nameof(OperationId), OperationId),
            (nameof(Admission), Admission), (nameof(RequestId), RequestId), (nameof(ResultReason), ResultReason),
            (nameof(ObservedAt), ObservedAt), (nameof(LatestInputSummary), LatestInputSummary), (nameof(ImageStatus), ImageStatus),
            (nameof(CanAdmit), CanAdmit), (nameof(CanSubmitBarcode), CanSubmitBarcode), (nameof(CanReconcile), CanReconcile), (nameof(CanQueryOriginal), CanQueryOriginal), (nameof(CanContinue), CanContinue),
            (nameof(HasUncertainOriginal), HasUncertainOriginal), (nameof(HasContinuation), HasContinuation), (nameof(NextStep), NextStep),
            (nameof(Recent), Recent), (nameof(InspectionReason), InspectionReason) })
        {
            if (published.TryGetValue(name, out var prior) && Equals(prior, value)) continue;
            published[name] = value; PropertyChanged?.Invoke(this, new(name));
        }
    }
    readonly Dictionary<string, object?> published = [];
    public static string StageText(ParcelState state) => state switch
    {
        ParcelState.SlotResolved => "格口已取得",
        ParcelState.WcsUnknown => "格口未确认 · 勿重复上件",
        ParcelState.WcsRejected => "未分配格口",
        ParcelState.UnpackRequired => "需拆包检查",
        ParcelState.ResolvingWcs => "正在获取格口",
        ParcelState.Prepared => "待处理",
        ParcelState.Blocked => "已受阻",
        ParcelState.WaitingWeight => "等待本件有效重量",
        ParcelState.WeightTimedOut => "本件称重超时",
        ParcelState.ImageUnknown => "图片上传未确认",
        ParcelState.ImageRejected => "图像阶段拒绝",
        ParcelState.CapturingImage => "正在采集本件图像",
        ParcelState.UploadingImage => "正在上传原图",
        ParcelState.Abandoned => "未发送 · 已撤销",
        _ => "阶段：" + state
    };
}

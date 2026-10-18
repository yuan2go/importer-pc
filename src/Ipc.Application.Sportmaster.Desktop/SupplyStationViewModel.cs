using System.ComponentModel;
using System.Windows;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Platform;

namespace Ipc.Application.Sportmaster.Desktop;

// Presentation only. Every fact/permission comes from the existing application contract.
public sealed class SupplyStationViewModel : ParcelViewModel
{
    DateTimeOffset? lastRead;
    string? readFailure;
    bool fresh;
    bool viewportFits = true, viewportRecoveryRequired;
    long successfulReadId, failedReadId;
    string selectedPage = "workstation";
    public SupplyStationPositionViewModel Left { get; }
    public SupplyStationPositionViewModel Right { get; }
    public bool Fresh => fresh;
    public bool ViewportFits => viewportFits;
    public bool ViewportReady => viewportFits && !viewportRecoveryRequired;
    public bool CanUseWorkspace => Fresh && ViewportReady;
    public bool CanResumeInViewport => CanUseWorkspace && CanResume;
    public bool CanAcknowledgeViewport => Fresh && viewportFits && Snapshot?.Paused == true;
    public Visibility ViewportWarningVisibility => ViewportReady ? Visibility.Collapsed : Visibility.Visible;
    public string ViewportStatus => !viewportFits ? "窗口空间不足 · 请最大化或调整显示设置" : "空间已恢复 · 请核对状态并确认视图";
    public bool Legacy => Snapshot?.Configuration.Effective.Topology is null;
    public string Station => Snapshot?.Configuration.Effective.StationId ?? "等待站位配置";
    public string UpdatedAt => lastRead is { } at ? at.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz") : "尚未取得快照";
    public string ReadStatus => !Fresh ? "快照已失效 · " + (readFailure ?? "正在读取或读取超时") + " · 最后成功读取：" + UpdatedAt : "状态更新时间 · " + UpdatedAt;
    public string TopologyStatus => Legacy ? "单读码兼容配置 · 仅 main 位，未启用双位" : "left / right 独立投递位 · 扫码 → WCS 格口";
    public string SelectedPage
    {
        get => selectedPage;
        set { if (value is not ("workstation" or "history" or "maintenance")) throw new ArgumentException("未安装的页面"); selectedPage = value; Changed(); }
    }
    public SupplyStationViewModel(IParcelApplication application) : base(application, "Sportmaster 供包工作台", "扫码 → WCS 格口")
    {
        Left = new(this, "left"); Right = new(this, "right");
        PropertyChanged += SnapshotChanged;
    }
    void SnapshotChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(Snapshot)) return;
        lastRead = DateTimeOffset.UtcNow; readFailure = null; RefreshFreshness();
    }
    public void RefreshFreshness(DateTimeOffset? at = null)
    {
        var now = at ?? DateTimeOffset.UtcNow;
        fresh = lastRead is { } read && now >= read && now - read < TimeSpan.FromSeconds(3) && readFailure is null;
        Left.Update(Snapshot, now, fresh, ViewportReady); Right.Update(Snapshot, now, fresh, ViewportReady);
        foreach (var property in new[] { nameof(Fresh), nameof(Legacy), nameof(Station), nameof(UpdatedAt), nameof(ReadStatus), nameof(TopologyStatus) }) Changed(property);
        NotifyViewport();
    }
    // Geometry is window-owned. Restoring pixels never resumes the shared business.
    public bool SetViewport(double width, double height)
    {
        var fits = double.IsFinite(width) && double.IsFinite(height) && width >= 1280 && height >= 680;
        var entered = viewportFits && !fits;
        viewportFits = fits;
        if (!fits) viewportRecoveryRequired = true;
        RefreshFreshness();
        return entered;
    }
    public void AcknowledgeViewport()
    {
        if (!CanAcknowledgeViewport) { Message = "先恢复有效客户区并确认应用已暂停；读取失败时不能解除视图保护。"; return; }
        viewportRecoveryRequired = false; RefreshFreshness();
        Message = "视图已确认；业务保持暂停，请核对现场后显式恢复新入场。";
    }
    void NotifyViewport()
    {
        foreach (var name in new[] { nameof(ViewportFits), nameof(ViewportReady), nameof(CanUseWorkspace), nameof(CanResumeInViewport), nameof(CanAcknowledgeViewport), nameof(ViewportWarningVisibility), nameof(ViewportStatus) }) Changed(name);
    }
    public void MarkReadFailure(string reason)
    {
        readFailure = reason; Message = "状态读取失败，旧快照仅供参考：" + reason; RefreshFreshness();
    }
    public Task RefreshDisplayAsync() => RefreshAsync();
    protected override void SnapshotReadSucceeded(long id) => AdvanceReadId(ref successfulReadId, id);
    protected override bool CanApplySnapshot(long id) => id > Volatile.Read(ref failedReadId);
    protected override void SnapshotReadFailed(long id, Exception error)
    {
        // Only a success from a later request supersedes this failure. An older
        // request completing while this read was pending cannot certify freshness.
        if (id > Volatile.Read(ref successfulReadId) && AdvanceReadId(ref failedReadId, id)) MarkReadFailure(error.Message);
    }
    static bool AdvanceReadId(ref long current, long next)
    {
        var previous = Volatile.Read(ref current);
        while (next > previous)
        {
            var actual = Interlocked.CompareExchange(ref current, next, previous);
            if (actual == previous) return true;
            previous = actual;
        }
        return false;
    }
    public async Task ReconcilePositionAsync(SupplyStationPositionViewModel position)
    {
        var op = position.Operation;
        if (!CanUseWorkspace || op is null) { Message = "需取得当前位的有效作业快照及可用视图"; return; }
        Selected = op;
        await RunAsync(async () => { var result = await Application.ReconcileAsync(op.Id); Message = result.Status + " · " + result.Reason; });
    }
    public async Task ContinuePositionAsync(SupplyStationPositionViewModel position)
    {
        var op = position.Operation;
        if (!CanUseWorkspace || op is null || !position.CanContinue || Snapshot is not { } displayed) { Message = "当前原阶段不可继续"; return; }
        var inspection = position.InspectionReason;
        await RunAsync(async () =>
        {
            var result = await Application.CommandAsync(new(Wire.Id(), displayed.Revision, "ContinuePrepared", op.Id, inspection));
            Message = result.Status + " · " + result.Reason;
        });
    }
    public async Task OpenHistoryAsync(string? operationId = null)
    {
        await LoadHistoryAsync();
        if (operationId is null) return;
        SelectedHistory = HistoryItems.FirstOrDefault(x => x.Operation.Id == operationId);
        if (SelectedHistory is null)
        {
            Selected = await Application.OperationAsync(operationId);
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
        });
    }
}

// Stable objects retain each window's drafts and selection when snapshots are refreshed.
public sealed class SupplyStationPositionViewModel : INotifyPropertyChanged
{
    readonly string requestedPosition;
    public event PropertyChangedEventHandler? PropertyChanged;
    public string PositionId { get; private set; }
    public string Heading => PositionId == "main" ? "MAIN · 单读码兼容位" : requestedPosition == "left" ? "LEFT · 左投递位" : "RIGHT · 右投递位";
    public string Accent => requestedPosition == "left" ? "#2563EB" : "#7C3AED";
    public string ReaderId { get; private set; } = "未配置";
    public ParcelOperation? Operation { get; private set; }
    public string Barcode => Operation?.ParcelCode ?? "等待包裹";
    public string Slot => Operation?.Wcs?.State == StageState.Accepted ? Operation.Slot ?? "未取得" : "未取得";
    public string Status { get; private set; } = "正在初始化";
    public string DeviceStatus { get; private set; } = "设备状态未取得";
    public string BlockingReasons { get; private set; } = "等待有效快照";
    public string OperationId => Operation?.Id ?? "未取得";
    public string Admission => Operation?.AdmissionId ?? "未取得";
    public string RequestId => Operation?.Wcs?.Request.Id ?? "未创建";
    public string ResultReason => Operation is { } op
        ? string.Join("\n", new[] { op.Wcs?.Failure ?? op.Reason, ParcelViewModel.DestinationSummary(op) }.Where(s => !string.IsNullOrEmpty(s))) : "";
    public string ObservedAt => Operation?.ObservedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz") ?? "尚无观察";
    public string ImageStatus => Operation?.Capture is { } c ? "采集 " + c.State + (Operation.Upload is { } u ? " · 上传 " + u.State : "") : "未参与采集";
    public bool CanAdmit { get; private set; }
    public bool CanReconcile { get; private set; }
    public bool CanContinue { get; private set; }
    public ParcelOperation[] Recent { get; private set; } = [];
    public string InspectionReason { get; set; } = "";
    public string ParcelCode { get; set; } = "000123456789";
    public string AdmissionId { get; private set; } = "parcel-" + Wire.Id();
    public SupplyStationPositionViewModel(SupplyStationViewModel owner, string position)
    { requestedPosition = PositionId = position; }
    public void NewParcel() { AdmissionId = "parcel-" + Wire.Id(); PropertyChanged?.Invoke(this, new(nameof(AdmissionId))); }
    internal void Update(ParcelSnapshot? snapshot, DateTimeOffset now, bool fresh, bool viewportReady)
    {
        var legacy = snapshot?.Configuration.Effective.Topology is null;
        PositionId = legacy && requestedPosition == "left" ? "main" : requestedPosition;
        var position = snapshot?.Positions?.SingleOrDefault(x => x.Id == PositionId);
        var binding = snapshot is null ? null : ParcelBindings.Positions(snapshot.Configuration.Effective).SingleOrDefault(x => x.Id == PositionId);
        ReaderId = binding?.ReaderDeviceId ?? "未配置";
        Recent = snapshot?.Operations.Where(x => (x.PositionId ?? "main") == PositionId)
            .OrderByDescending(x => x.ObservedAt).ThenByDescending(x => x.Id, StringComparer.Ordinal).ToArray() ?? [];
        var previousOperation = Operation?.Id;
        // A named current operation must match both identities. Never fall back to another parcel.
        Operation = position?.CurrentOperation is { } id ? Recent.FirstOrDefault(x => x.Id == id) : Recent.FirstOrDefault();
        if (Operation?.Id != previousOperation) InspectionReason = "";
        var device = snapshot?.Devices?.SingleOrDefault(x => x.DeviceId == ReaderId);
        DeviceStatus = !fresh ? "设备旧状态已失效" : device is null ? "设备状态未取得" : device.IsValid(now) ? "读码就绪 · " + device.ConfirmedAt?.ToLocalTime().ToString("HH:mm:ss") : "未就绪 · " + device.ValidityReason(now);
        CanAdmit = fresh && viewportReady && binding is not null && position?.Admit == true;
        CanReconcile = fresh && viewportReady && Operation is not null;
        CanContinue = fresh && viewportReady && Operation is { } op && snapshot?.Availability?.ContinuePrepared.Contains(op.Id) == true;
        BlockingReasons = !fresh ? "快照失效；等待状态读取恢复" : !viewportReady ? "视图空间保护；恢复客户区并人工确认" : binding is null ? "当前配置未启用该投递位" :
            position is null ? "尚未取得投递位状态" : string.Join("\n", position.BlockingReasons);
        Status = binding is null ? "未启用" : position?.CurrentOperation is not null && Operation is null ? "当前作业身份未匹配 · 等待新快照" : Operation is null ? "等待包裹入场" : StageText(Operation.State);
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

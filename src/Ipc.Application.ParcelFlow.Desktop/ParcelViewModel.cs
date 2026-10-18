using System.IO;
using System.Windows;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow.Desktop;

// Window owns selection/draft only; every command and fact belongs to the shared application.
public partial class ParcelViewModel(IParcelApplication application, string title, string flow) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public IParcelApplication Application => application;
    public string Title => title;
    public Visibility SimulationVisibility => Snapshot?.Configuration.Effective.Mode == RunMode.Simulation ? Visibility.Visible : Visibility.Collapsed;
    public string EnvironmentStatus => Snapshot?.Configuration.Effective.Mode == RunMode.Production ? "Production · 软件暂停不是安全急停 · 资格不代替现场安全联锁" : "Simulation · 软件暂停不是安全急停 · 不操作真实设备";
    public string Flow => flow;
    ParcelSnapshot? snapshot;
    ParcelOperation? selected;
    string message = "等待相机读码；模拟工程不操作真实设备。";
    public ParcelSnapshot? Snapshot { get => snapshot; private set { snapshot = value; Changed(); Changed(nameof(Operations)); Changed(nameof(Current)); Changed(nameof(BlockingReasons)); Changed(nameof(Status)); Changed(nameof(CurrentWeight)); Changed(nameof(CurrentStage)); Changed(nameof(CurrentSlot)); Changed(nameof(Health)); Changed(nameof(ConfigurationState)); Changed(nameof(LoginVisibility)); Changed(nameof(Session)); Changed(nameof(SimulationVisibility)); Changed(nameof(EnvironmentStatus)); Changed(nameof(OperationOperator)); Changed(nameof(CurrentImage)); Changed(nameof(UploadState)); Changed(nameof(CaptureState)); Changed(nameof(CanAdmit)); Changed(nameof(CanResume)); Changed(nameof(CanContinue)); Changed(nameof(CanContinueUpload)); Changed(nameof(CanAbandon)); Changed(nameof(CanConfigure)); } }
    public ParcelOperation[] Operations => Snapshot?.Operations ?? [];
    public ParcelOperation? Current => Snapshot?.Operations.FirstOrDefault(x => (x.PositionId ?? "main") == SelectedPositionId);
    public string BlockingReasons => string.Join("\n", Snapshot?.BlockingReasons ?? []);
    public string Status => Snapshot?.State ?? "正在初始化";
    public string Health => string.Join("   ·   ", Snapshot?.Health.Select(x => x.InstanceId + " " + (x.Ready ? "就绪" : "未就绪") + " " + x.ConfirmedAt.ToLocalTime().ToString("HH:mm:ss")) ?? []);
    public Visibility LoginVisibility => Snapshot?.Configuration.Effective.Login is null ? Visibility.Collapsed : Visibility.Visible;
    public string OperatorSubject { get; set; } = "alice";
    public string Session => Snapshot?.Configuration.Effective.Login is null ? "登录未启用" : Snapshot.CurrentOperator is { } user ? "当前操作员：" + user.DisplayName + (user.Simulation ? " · 模拟身份" : " · " + user.Authority) : "等待操作员认证";
    public string OperationOperator => Current?.Operator is { } user ? "原作业操作员：" + user.DisplayName + " · " + user.SessionId : "原作业没有登录身份";
    public bool CanAdmit => Snapshot?.Availability?.Admit == true;
    public bool CanResume => Snapshot?.Availability?.Resume == true;
    public bool CanConfigure => Snapshot?.Availability?.Configure == true;
    public bool CanContinue => Snapshot?.Availability?.ContinuePrepared.Contains(Current?.Id ?? "") == true;
    public bool CanContinueUpload => Snapshot?.Availability?.ContinueUpload.Contains(Selected?.Id ?? "") == true;
    public bool CanAbandon => Snapshot?.Availability?.AbandonUnsubmitted.Contains(Selected?.Id ?? "") == true;
    public string CaptureState => "采集：" + (Current?.Capture?.State.ToString() ?? "未准备") + " · " + Current?.Capture?.Failure;
    public string UploadState => Current?.Upload is { } upload ? "附加/前置上传：" + upload.State + " · 尝试 " + upload.Attempts + " · " + upload.Failure : "上传未准备";
    public string CurrentImage => Current?.Image is { } image ? (image.UploadedUri is null ? "图像已采集，上传未确认" : "图像已上传 · " + image.UploadedUri + (Current.ImageReleased ? " · 临时原件已释放" : "")) : Snapshot?.Configuration.Effective.Images is null ? "传图未启用" : "图像尚未取得";
    public async Task LoginAsync(string proof) { var r = await application.LoginAsync(OperatorSubject, proof); Message = r.Status + " · " + r.Reason; }
    public async Task LogoutAsync() { var r = await application.LogoutAsync(); Message = r.Status + " · " + r.Reason; }
    public string CurrentSlot => Current?.Slot ?? "未取得";
    public string CurrentWeight => Current?.Weight is { } w ? w.Value.ToString("F" + w.Precision, CultureInfo.InvariantCulture) + " " + w.Unit + " · " + (w.Source == WeightSource.Measured ? "实测" : "配置补全") : "未参与或未取得；见当前重量策略";
    public string CurrentStage => Current is null ? "等待包裹入场" : Current.State switch
    {
        ParcelState.ImageUnknown => "图像阶段待核对 · 禁止重传或重复放货",
        ParcelState.ImageRejected => "图像阶段拒绝 · 后续业务未发送",
        ParcelState.CapturingImage => "正在采集当前包裹图像",
        ParcelState.UploadingImage => "正在上传原图像",
        ParcelState.WeightTimedOut => "本件称重超时 · 暂停并检查实体后处置",
        ParcelState.Abandoned => "未发送意图已处置 · 原接受与上传事实保留",
        ParcelState.WaitingWeight => "等待同一包裹的有效重量",
        ParcelState.SlotResolved => "WCS 格口已取得",
        ParcelState.UnpackRequired => "需拆包 · 本位等待检查处置",
        ParcelState.PlcAccepted => "PLC 业务已接受 · 物理完成待核对",
        ParcelState.PhysicalCompleted => "组件报告物理完成 · 见原回执",
        ParcelState.PlcUnknown or ParcelState.WcsUnknown => "结果待核对 · 请勿重复放货",
        ParcelState.WcsRejected or ParcelState.PlcRejected => "业务拒绝 · 查看原始原因",
        ParcelState.Blocked => "作业阻断 · 保留现场包裹",
        _ => "正在处理 · " + Current.State
    };
    public ParcelOperation? Selected { get => selected; set { selected = value; Changed(); NotifyTargets(); } }
    public string Details => Selected is null ? "选择一条作业查看原阶段、目标、报文和回执。" : DestinationDetails(Selected) + Wire.Encode(Selected);
    static string DestinationDetails(ParcelOperation operation)
    {
        var reply = operation.Wcs?.Replies.LastOrDefault(r => r.Destination is not null);
        if (reply?.Destination is not { } result) return "";
        string Known(string? text) => text ?? "未取得";
        string Measure(decimal? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "未取得";
        var outcome = result.Outcome switch
        {
            WcsDestinationOutcome.Assigned => "目标格口已取得；物理完成未确认",
            WcsDestinationOutcome.UnpackRequired => "需拆包；本位等待检查处置",
            _ => "业务拒绝"
        };
        var lines = new List<string>
        {
            "原 RCS 回执 · " + outcome + "（作业状态以原快照为准）",
            "任务 " + Known(result.TaskId) + " · SKU " + Known(result.SkuId) + " · 格口 " + Known(reply.Slot),
            "长度 " + Measure(result.Length) + " · 宽度 " + Measure(result.Width) + " · 高度 " + Measure(result.Height) + " · 重量 " + Measure(result.Weight) + "（接口未提供单位）"
        };
        if (result.Code is not null || result.Message is not null) lines.Add("业务码 " + Known(result.Code) + " · 原因 " + Known(result.Message));
        if (result.Unpack is { } unpack)
        {
            lines.Add("拆包条码 " + unpack.Barcode + " · SKU " + unpack.SkuId + " · 扫描数量 " + unpack.ScanQty.ToString(CultureInfo.InvariantCulture));
            if (unpack.Chutes.Length == 0) lines.Add("上游未返回拆包格口明细");
            foreach (var chute in unpack.Chutes)
                lines.Add("格口 " + chute.ChuteNo + " · 订单 " + chute.OrderNo + " · 剩余 " + chute.Remaining.ToString(CultureInfo.InvariantCulture) + " · 阶段 " + chute.Phase);
            lines.Add("本接口未提供拆包提交；暂停并检查移除原实体后，使用已有受守卫的本地处置。");
        }
        return string.Join("\n", lines) + "\n\n原作业、请求与回执完整快照：\n";
    }
    public string Message { get => message; set { message = value; Changed(); } }
    public string ConfigurationState => Snapshot is null ? "未加载" : Snapshot.Configuration.Status + " · 修订 " + Snapshot.Configuration.Revision + (Snapshot.Configuration.Failure is { } f ? "\n" + f : "");
    public string ConfigurationDraft { get; set; } = "";
    public string ParcelCode { get; set; } = "000123456789";
    public string AdmissionId { get; set; } = "parcel-" + Wire.Id();
    public string MeasurementValue { get; set; } = "1.250";
    public string BackupPath { get; set; } = "";
    public bool Busy { get; private set; }
    long refreshGeneration;
    public async Task RefreshAsync()
    {
        var currentId = Selected?.Id;
        var generation = Interlocked.Increment(ref refreshGeneration);
        var next = await application.SnapshotAsync();
        if (generation != Volatile.Read(ref refreshGeneration) || Snapshot is { } previous && next.RuntimeId == previous.RuntimeId && next.Revision < previous.Revision) return;
        Snapshot = next;
        if (!PositionIds.Contains(SelectedPositionId)) SelectedPositionId = PositionIds.FirstOrDefault() ?? "main";
        NotifyTargets();
        if (currentId is not null) Selected = Operations.FirstOrDefault(x => x.Id == currentId) ?? Selected;
    }
    long? draftConfigurationRevision;
    Guid? draftRuntimeId;
    public string DraftRevisionState => draftConfigurationRevision is { } revision ? "本窗草稿基于配置修订 " + revision + "；自动刷新不改变草稿CAS" : "尚未读取草稿修订";
    void SetConfigurationDraft(ParcelConfiguration value, long revision, Guid runtime)
    { ConfigurationDraft = Wire.Encode(value); draftConfigurationRevision = revision; draftRuntimeId = runtime; Changed(nameof(ConfigurationDraft)); Changed(nameof(DraftRevisionState)); }
    public void LoadConfigurationDraft()
    { var loaded = Snapshot; if (loaded is not null) SetConfigurationDraft(loaded.Configuration.Pending ?? loaded.Configuration.Effective, loaded.Configuration.Revision, loaded.RuntimeId); }
    long RequireDraftRevision()
    {
        if (draftConfigurationRevision is not { } revision || Snapshot?.RuntimeId != draftRuntimeId) throw new InvalidOperationException("先读取本运行实例的完整修订，再编辑保存；自动刷新不会更新旧草稿的CAS");
        return revision;
    }
    void AdvanceOwnDraftRevision(long expected, BusinessReceipt receipt)
    { if (receipt.Status == "PendingRestart") { draftConfigurationRevision = expected + 1; Changed(nameof(DraftRevisionState)); } }
    public async Task RunAsync(Func<Task> action)
    {
        if (Busy) { Message = "已有操作进行中，请等待。"; return; }
        Busy = true; Changed(nameof(Busy));
        try { await action(); await RefreshAsync(); }
        catch (Exception ex) { Message = ex.GetType().Name + ": " + ex.Message; }
        finally { Busy = false; Changed(nameof(Busy)); }
    }
    public async Task PauseAsync()
    {
        // Independent control lane: ordinary write mutual exclusion cannot hide Pause.
        try
        {
            for (var retry = 0; retry < 3; retry++)
            {
                var s = await application.SnapshotAsync();
                var r = await application.CommandAsync(new(Wire.Id(), s.Revision, "Pause"));
                if (r.Status == "Conflict") continue;
                Message = r.Status + " · " + r.Reason; await RefreshAsync(); return;
            }
            Message = "暂停遇到连续修订冲突，请重试；状态仍由应用服务确认。";
        }
        catch (Exception ex) { Message = "暂停失败：" + ex.Message; }
    }
    public async Task CommandAsync(string kind, string? owner = null)
    {
        var s = Snapshot ?? throw new InvalidOperationException("先刷新当前投影");
        var r = await application.CommandAsync(new(Wire.Id(), s.Revision, kind, owner)); Message = r.Status + " · " + r.Reason;
    }
    public void Emit()
    {
        Message = application.EmitReferenceAt(SelectedPositionId, AdmissionId, ParcelCode, Wire.Id()) ? "模拟观察已排队；状态由应用服务更新。" : "未接纳观察；检查暂停、过载或设备状态。";
        // The same admission is deliberately retained until the operator explicitly selects
        // a new parcel. Repeated clicking cannot invent a new physical entity.
    }
    public void NewParcel() { AdmissionId = "parcel-" + Wire.Id(); Changed(nameof(AdmissionId)); }
    public void EmitWeight()
    {
        var op = Current ?? throw new InvalidOperationException("尚无包裹");
        if (!decimal.TryParse(MeasurementValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)) throw new InvalidDataException("重量数值无效");
        var scale = ParcelBindings.Scale(op.FrozenConfiguration, op.PositionId) ?? throw new InvalidOperationException("当前业务未启用实测重量");
        Message = application.EmitReferenceWeight(new(Wire.Id(), op.AdmissionId, scale.Target, value, op.WeightPolicy.Unit, op.WeightPolicy.Precision, MeasurementQuality.Valid, true, DateTimeOffset.UtcNow))
            ? "模拟测量已排队，等待应用关联和校验。" : "测量未接纳；原作业仍保留。";
    }
    public async Task SaveConfigurationAsync()
    { if (Snapshot is null) return; var expected = RequireDraftRevision(); var r = await application.StageConfigurationAsync(expected, Wire.Decode<ParcelConfiguration>(ConfigurationDraft), ConfigurationReason); AdvanceOwnDraftRevision(expected, r); Message = r.Status + " · " + r.Reason; }
    public async Task ReconcileAsync()
    { var op = Current ?? throw new InvalidOperationException("当前投递位没有作业"); var r = await application.ReconcileAsync(op.Id); Message = r.Status + " · " + r.Reason; }
    public async Task ContinueAsync() { var op = Current ?? throw new InvalidOperationException("当前投递位没有作业"); var r = await application.CommandAsync(new(Wire.Id(), Snapshot!.Revision, "ContinuePrepared", op.Id, InspectionReason)); Message = r.Status + " · " + r.Reason; }
    public async Task ReconnectAsync() { var r = await application.ReconnectAsync(); Message = r.Status + " · " + r.Reason; }
    public async Task BackupAsync() { var r = await application.BackupAsync(BackupPath); Message = r.Status + " · " + r.Reason; }
    protected void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

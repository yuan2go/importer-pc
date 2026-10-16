using System.Globalization;
using System.Windows.Data;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using Ipc.Platform;
using Ipc.Supply;

namespace Ipc.Desktop;
public class Bindable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    protected void Put<T>(ref T field, T value, [CallerMemberName] string? name = null) { if (EqualityComparer<T>.Default.Equals(field, value)) return; field = value; Changed(name); }
}
public static class OperatorText
{
    public static string Known(string? value) => string.IsNullOrWhiteSpace(value) ? "未取得" : value;
    public static string Operation(OperationStatus status) => status switch
    {
        OperationStatus.Observed => "○ 已入场 · 等待读码", OperationStatus.ValidationPending => "◷ 已保存 · 等待校验", OperationStatus.ValidationRejected => "✕ 校验未通过",
        OperationStatus.AwaitingAction => "● 待确认供包", OperationStatus.SplitRequired => "◐ 待拆包计数", OperationStatus.SubmitPending => "◷ 已保存 · 待发送 / 处理中",
        OperationStatus.Accepted => "✓ 业务已接受", OperationStatus.Rejected => "✕ 业务已拒绝", OperationStatus.OutcomeUnknown => "? 结果待核对", OperationStatus.Cancelled => "— 草稿已处置", OperationStatus.AwaitingDisposition => "! 请求已本地终结 · 实体待处置", _ => "未取得"
    };
    public static string Session(SessionStatus status) => status switch { SessionStatus.Validating => "校验中", SessionStatus.Active => "作业中", SessionStatus.ClosingCheck => "关闭检查中", SessionStatus.AwaitingShortageDecision => "少货待确认", SessionStatus.Closing => "关闭请求处理中", SessionStatus.CloseUnknown => "关闭结果待核对", SessionStatus.Closed => "已关闭", SessionStatus.Rejected => "已拒绝", SessionStatus.AwaitingDisposition => "已本地终结 · 待处置", _ => "未取得" };
    public static string Request(RequestStatus status) => status switch { RequestStatus.Prepared => "已保存 · 待发送", RequestStatus.Dispatching => "处理中 · 已授权交付", RequestStatus.Accepted => "已接受", RequestStatus.Rejected => "已拒绝", RequestStatus.Unknown => "待核对", RequestStatus.CancelledBeforeDispatch => "已本地终结", _ => "未取得" };
    public static string Reason(string value)
    {
        var map = new Dictionary<string, string> { ["WcsHealth"]="接收端健康未确认或已过期", ["OperatorNotificationBacklog"]="提示事件积压，已阻断新业务，请维护核对", ["No code"]="本次观察未识别到条码", ["Multiple codes"]="本次观察包含多个条码", ["Invalid barcode/quantity"]="本次条码或数量格式无效", ["IdentityRequired"]="请在工程控制页登录模拟角色", ["IdentityExpired"]="身份已过期，请重新登录", ["OperatorPaused"]="人工暂停中，请确认后恢复", ["ContextNotActive"]="当前箱未就绪，请先校验箱码", ["OperationNotReady"]="作业尚未达到可供包状态", ["RetainedCountIncomplete"]="保留件数量未完成，请继续扫码", ["WcsUnavailable"]="接收端不可用，请检查模拟接收端", ["Overload"]="观察过载，请核对来源与实体", ["DeviceFault"]="设备故障，请检查连接", ["UnexpiredMaintenanceIdentityRequired"]="需要有效维护身份", ["OutboxCapacity"]="发送队列已满，请等待处理", ["NoActiveContext"]="请先校验当前箱", ["Quarantine"]="恢复隔离中，请完成外部账与实体核对" };
        var extra = new Dictionary<string,string> {
            ["OriginalActorExpired"]="原请求身份已过期；重新登录不能改写原授权", ["AdmittedObservationsDraining"]="已接纳观察正在排空", ["ReconcileSentOrTerminateProvenUndeliveredPrepared"]="先核对已交付或未知请求，或受控终结有未交付证明的请求", ["OnlyPreparedCanTerminateLocally"]="仅待发送请求可本地终结", ["NoDurableNonDeliveryProof"]="缺少持久未交付证明，禁止本地终结", ["RequestNotBlocked"]="请求仍可发送；请先暂停并核对", ["UnresolvedRuntimeFault"]="运行故障尚未解决", ["SIMULATION accepted"]="模拟接收端已接受", ["SIMULATION business rejection"]="模拟接收端业务拒绝", ["Context already opened"]="此箱已建立上下文", ["Station already has an open context"]="工位仍有未关闭的箱", ["Invalid synthetic item"]="合成 Item 数据无效", ["No remaining demand"]="需求已满足，没有剩余需求", ["Stale close check"]="关闭检查已过期，请重新检查", ["Unsupported logical intent"]="未支持的业务意图", ["IdentityUnavailable"]="身份未取得或已过期，请登录", ["NoActiveBoxOrCloseBarrier"]="当前箱未激活或正在关闭检查", ["RestoreQuarantine"]="备份恢复隔离中，请核对外部账与实体", ["OutcomeUnknown"]="存在结果待核对请求，请查询原请求", ["UnknownRequest"]="原请求结果待核对", ["PendingExternalEffect"]="仍有未决外部请求", ["PhysicalOccupancyNotClear"]="实体位未复位，请核对实体离场", ["EntityPermitExpired"]="实体许可已过期，需要维护核对", ["EntityEpochChanged"]="设备代次已变化，需要显式实体核对", ["EntityNotOccupied"]="实体不在位", ["QuantityAuthorityMissing"]="尚未取得有效数量依据", ["OriginalBindingNotActive"]="原请求绑定不是当前有效版本，保留原请求核对", ["SessionChanged"]="当前箱身份已变化", ["OperationRevisionOrOccupancyChanged"]="作业版本或实体占位已变化", ["CloseBarrier"]="关闭屏障中", ["Stopping"]="工位正在停止", ["OperationRejected"]="作业已拒绝，请检查原因", ["LocalDispositionRequired"]="请求已本地终结，原预留和实体仍待处置", ["License"]="许可不可用", ["EntityPermit"]="实体许可阻断", ["UnresolvedRequests"]="仍有未解决请求", ["InputNotDrained"]="已接纳观察尚未排空", ["NoActiveOverload"]="当前无活动过载", ["Conflict"]="状态已变化，请重新核对", ["Snapshot changed; review current state"]="状态已变化，请核对最新投影后重试", ["Original request locally terminated; prior reservations/entity require disposition"]="原请求已本地终结；此前预留与实体仍须处置"
        };
        if(map.TryGetValue(value,out var known)||extra.TryGetValue(value,out known))return known;
        var separator=value.IndexOf(':');
        if(separator>0)
        {
            var prefix=value[..separator];var rest=value[(separator+1)..];
            if(prefix is "left" or "right")return (prefix=="left"?"左通道：":"右通道：")+Reason(rest);
            if(prefix=="Device")return "设备未就绪："+string.Join(" / ",rest.Split(':').Select(x=>x is "left"?"左通道":x is "right"?"右通道":Health(x)));
            if(map.TryGetValue(prefix,out known)||extra.TryGetValue(prefix,out known))return known+"（"+rest+"）";
        }
        return value;
    }
    public static string Health(string value) => value switch { "Ready"=>"就绪", "Connecting"=>"连接中", "Disconnected"=>"已断开", "Reconnecting"=>"重连中", "Faulted"=>"故障", "Stopping"=>"停止中", "Stopped"=>"已停止", "Unknown"=>"未取得", _=>value };
    public static string Kind(RequestKind value) => value switch { RequestKind.Context=>"箱校验", RequestKind.Validate=>"Item 校验", RequestKind.Submit=>"供包提交", RequestKind.CheckClose=>"关闭检查", RequestKind.Close=>"关闭箱", _=>"未取得" };
    public static string Reasons(IEnumerable<string> reasons) => string.Join("；", reasons.Select(Reason));
}
public sealed class LaneViewModel(string lane) : Bindable
{
    public string Lane => lane;
    public string Label => lane == "left" ? "左通道 · CAM0" : "右通道 · CAM1";
    public SupplyOperation? Current { get; private set; }
    string health = "状态未取得", status = "等待实体", barcode = "未取得", package = "未取得", remaining = "未取得", supply = "未取得", reason = "", occupancy = "", splitProgress = "", task = "未取得", provenance = "未取得", next = "请先校验当前箱";
    string keyTask = "任务未取得", quantities = "数量未取得", previousResult = "无上次已证实结果";
    bool splitVisible;
    public string KeyTask { get => keyTask; private set => Put(ref keyTask, value); }
    public string Quantities { get => quantities; private set => Put(ref quantities, value); }
    public string PreviousResult { get => previousResult; private set => Put(ref previousResult, value); }
    public bool SplitVisible { get => splitVisible; private set => Put(ref splitVisible, value); }
    string barcodeDraft = "000123", quantityDraft = "1", retainedDraft = "", disabledReason = "等待业务投影";
    bool canSubmit, canScan, canClear, canCancel;
    Brush statusBrush = Brushes.SteelBlue;
    public string Health { get => health; private set => Put(ref health, value); }
    public string Status { get => status; private set => Put(ref status, value); }
    public Brush StatusBrush { get => statusBrush; private set => Put(ref statusBrush, value); }
    public string Barcode { get => barcode; private set => Put(ref barcode, value); }
    public string Package { get => package; private set => Put(ref package, value); }
    public string Remaining { get => remaining; private set => Put(ref remaining, value); }
    public string Supply { get => supply; private set => Put(ref supply, value); }
    public string Reason { get => reason; private set => Put(ref reason, value); }
    public string Next { get => next; private set => Put(ref next, value); }
    public string Task { get => task; private set => Put(ref task, value); }
    public string Provenance { get => provenance; private set => Put(ref provenance, value); }
    public string Occupancy { get => occupancy; private set => Put(ref occupancy, value); }
    public string SplitProgress { get => splitProgress; private set => Put(ref splitProgress, value); }
    public string DisabledReason { get => disabledReason; private set => Put(ref disabledReason, value); }
    public bool CanSubmit { get => canSubmit; private set => Put(ref canSubmit, value); }
    public bool CanScan { get => canScan; private set => Put(ref canScan, value); }
    public bool CanCancel { get => canCancel; private set => Put(ref canCancel, value); }
    public bool CanClear { get => canClear; private set => Put(ref canClear, value); }
    public string BarcodeDraft { get => barcodeDraft; set => Put(ref barcodeDraft, value); }
    public string QuantityDraft { get => quantityDraft; set => Put(ref quantityDraft, value); }
    public string RetainedDraft { get => retainedDraft; set => Put(ref retainedDraft, value); }
    public void Apply(StationSnapshot snapshot, bool available)
    {
        Current = snapshot.State.Lanes.GetValueOrDefault(lane);
        var d = snapshot.State.Devices.GetValueOrDefault(lane);
        Health = d is not null && !d.IsValid(DateTimeOffset.UtcNow) ? "! 设备未就绪 · " + d.ValidityReason(DateTimeOffset.UtcNow) : d?.Health switch { DeviceHealth.Ready => "● 设备就绪", DeviceHealth.Connecting => "连接中", DeviceHealth.Reconnecting => "重连中", DeviceHealth.Disconnected => "已断开", DeviceHealth.Faulted => "设备故障", DeviceHealth.Stopping => "停止中", DeviceHealth.Stopped => "已停止", _ => "未就绪" };
        Status = Current is null ? "等待实体" : OperatorText.Operation(Current.Status);
        if(Current?.Status is OperationStatus.SubmitPending or OperationStatus.ValidationPending)
        {
            var pending = snapshot.Requests.Where(x=>x.OwnerId==Current.Id).OrderByDescending(x=>x.CreatedAt).FirstOrDefault();
            if(pending is not null) Status = OperatorText.Kind(pending.Kind)+" · "+OperatorText.Request(pending.Status);
        }
        StatusBrush = Current?.Status switch { OperationStatus.Accepted => new SolidColorBrush(Color.FromRgb(20,116,80)), OperationStatus.Rejected or OperationStatus.ValidationRejected => new SolidColorBrush(Color.FromRgb(181,47,56)), OperationStatus.OutcomeUnknown or OperationStatus.AwaitingDisposition => new SolidColorBrush(Color.FromRgb(137,85,9)), _ => new SolidColorBrush(Color.FromRgb(32,95,206)) };
        Barcode = OperatorText.Known(Current?.Barcode);
        Package = Current?.Package?.ToString() ?? "未取得"; Remaining = Current?.Remaining?.ToString() ?? "未取得"; Supply = Current?.Supply?.ToString() ?? "未取得";
        Reason = string.IsNullOrWhiteSpace(Current?.Reason) ? "无异常记录" : OperatorText.Reason(Current.Reason);
        Occupancy = Current?.Occupancy switch { Ipc.Supply.Occupancy.Occupied => "实体在位", Ipc.Supply.Occupancy.Departed => "已离场 · 待复位", Ipc.Supply.Occupancy.Clear => "实体位已复位", _ => "实体位待入场" };
        var t = Current?.Task;
        Task = $"SKU  {OperatorText.Known(t?.Sku)}\nItem 波次  {OperatorText.Known(t?.ItemWave)}\n任务 ID  {OperatorText.Known(t?.TaskId)}\n目标格口  {OperatorText.Known(t?.Destination)}   单位  {OperatorText.Known(t?.Unit)}\n入场箱  {OperatorText.Known(Current?.BoxBarcode)}   箱波次  {OperatorText.Known(Current?.BoxWave)}";
        Provenance = t?.Provenance is { } p ? $"来源 {p.Source} · 版本 {p.Version}\n证据 {p.EvidenceId} · {p.AcquiredAt:O}" : "语义来源 / 版本 / 证据：未取得";
        SplitProgress = Current?.Status == OperationStatus.SplitRequired ? $"保留件 {Current.SplitScans.Count} / {Current.Supply} · 应移除 {Current.Package - Current.Supply}" : "当前无需拆包计数";
        Next = Current?.Status switch { OperationStatus.AwaitingAction => "核对条码、任务和数量后确认供包", OperationStatus.SplitRequired => "扫描每个保留件的独立身份，达到要求后确认供包", OperationStatus.OutcomeUnknown => "到报警与恢复页查询原请求，保留实体与原请求身份", OperationStatus.AwaitingDisposition => "保留实体和原预留；取得处置依据前不得继续", OperationStatus.Accepted => "接受不代表物理完成；等待实体离场和复位", OperationStatus.ValidationRejected or OperationStatus.Rejected => "检查原因，核对实体后再按允许流程处理", OperationStatus.SubmitPending or OperationStatus.ValidationPending => "等待原请求结果；不要重复提交", _ => "核对当前箱与设备状态后等待实体" };
        KeyTask = $"SKU {OperatorText.Known(t?.Sku)} · 任务 {OperatorText.Known(t?.TaskId)} · 格口 {OperatorText.Known(t?.Destination)}";
        Quantities = $"包装 {Package} / 需求 {Remaining} / 供给或保留 {Supply} {OperatorText.Known(t?.Unit)}";
        SplitVisible = Current?.Status == OperationStatus.SplitRequired;
        PreviousResult = Current?.PreviousConfirmed is { } prior ? $"仅历史证实结果 r{prior.Revision} · Item {prior.Barcode} · SKU {OperatorText.Known(prior.Task?.Sku)} · 数量 {prior.Supply?.ToString() ?? "未取得"}\n此结果不授权当前输入；原请求详情保留。" : "无上次已证实结果";
        var guard = snapshot.OperationGuards.GetValueOrDefault(lane);
        DisabledReason = !available ? "投影未就绪、正在执行或窗口有效区域不足" : guard is null ? "当前无可提交作业" : OperatorText.Reasons(guard.Reasons);
        CanSubmit = available && guard?.Allowed == true;
        CanScan = available && Current?.Status == OperationStatus.SplitRequired && snapshot.Submit.Allowed && snapshot.State.Actor?.ExpiresAt > DateTimeOffset.UtcNow && Current.SplitScans.Count < Current.Supply;
        CanClear = available && Current?.Status == OperationStatus.SplitRequired && snapshot.State.Actor?.ExpiresAt > DateTimeOffset.UtcNow;
        CanCancel = available && snapshot.State.Actor?.ExpiresAt > DateTimeOffset.UtcNow && Current?.Status is OperationStatus.Rejected or OperationStatus.ValidationRejected or OperationStatus.AwaitingAction or OperationStatus.SplitRequired;
    }
}
public sealed class StationViewModel : Bindable
{
    string identity = "未登录", globalStatus = "正在核验装配…", context = "等待箱", devices = "", lastResult = "准备就绪后可执行操作", boxDraft = "000001", projection = "投影未取得", contextSource = "来源未取得", accepted = "未取得", alarmSummary = "活动报警待取得", recoveryHint = "";
    string station = "工位未取得", healthSummary = "健康状态未取得", otherLane = "", reviewLayoutHint = "", soundStatus = "提示音等待事件";
    public string Station { get => station; set => Put(ref station, value); }
    public string HealthSummary { get => healthSummary; set => Put(ref healthSummary, value); }
    public string OtherLane { get => otherLane; set => Put(ref otherLane, value); }
    public string ReviewLayoutHint { get => reviewLayoutHint; set => Put(ref reviewLayoutHint, value); }
    public string SoundStatus { get => soundStatus; set => Put(ref soundStatus, value); }
    public string AlarmSummary { get => alarmSummary; set => Put(ref alarmSummary, value); }
    public string RecoveryHint { get => recoveryHint; set => Put(ref recoveryHint, value); }
    public string Identity { get => identity; set => Put(ref identity, value); }
    public string GlobalStatus { get => globalStatus; set => Put(ref globalStatus, value); }
    public string Context { get => context; set => Put(ref context, value); }
    public string ContextSource { get => contextSource; set => Put(ref contextSource, value); }
    public string Accepted { get => accepted; set => Put(ref accepted, value); }
    public string Projection { get => projection; set => Put(ref projection, value); }
    public string Devices { get => devices; set => Put(ref devices, value); }
    public string LastResult { get => lastResult; set => Put(ref lastResult, value); }
    public string BoxDraft { get => boxDraft; set => Put(ref boxDraft, value); }
}
public sealed record RequestRecoveryRow(RequestRecovery Recovery)
{
    public string Id => Recovery.Request.Id;
    public string Status => OperatorText.Request(Recovery.Request.Status);
    public string Reasons => OperatorText.Reasons(Recovery.Dispatch.Reasons.Concat(Recovery.LocalTermination.Reasons).Distinct());
    public string NextStep => Recovery.Request.Status switch { RequestStatus.Unknown => "查询原请求；继续保留未知结果", RequestStatus.Dispatching => "等待原响应；中断后核对", _ => "恢复许可，或按持久未交付证据终结" };
}
public sealed record HistoryRow(SupplyOperation Operation)
{
    public string Id => Operation.Id;
    public string Time => Operation.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz");
    public string Lane => Operation.Lane == "left" ? "左" : "右";
    public string Barcode => OperatorText.Known(Operation.Barcode);
    public string Box => OperatorText.Known(Operation.BoxBarcode);
    public string BoxWave => OperatorText.Known(Operation.BoxWave);
    public string Task => OperatorText.Known(Operation.Task?.TaskId);
    public string ItemWave => OperatorText.Known(Operation.Task?.ItemWave);
    public string Sku => OperatorText.Known(Operation.Task?.Sku);
    public string Destination => OperatorText.Known(Operation.Task?.Destination);
    public string Quantity => $"{Operation.Supply?.ToString() ?? "未取得"} {OperatorText.Known(Operation.Task?.Unit)}";
    public string Status => OperatorText.Operation(Operation.Status);
}

public sealed class OperatorLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        RequestStatus status => OperatorText.Request(status),
        OperationStatus status => OperatorText.Operation(status),
        SessionStatus status => OperatorText.Session(status),
        string text => text switch { "Validated"=>"已验证 · 待激活", "Rejected"=>"已拒绝", "PendingActivation"=>"待重启激活", "Effective"=>"当前有效", "ActivationFailed"=>"激活失败 · 保留旧版", _=>OperatorText.Reason(text) },
        _ => value
    };
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

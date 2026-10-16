using System.ComponentModel;
using System.Runtime.CompilerServices;
using Ipc.Platform;
using Ipc.Supply;

namespace Ipc.Desktop;
public class Bindable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    protected void Put<T>(ref T field, T value, [CallerMemberName] string? name = null) { if (EqualityComparer<T>.Default.Equals(field, value)) return; field = value; Changed(name); }
}
public sealed class LaneViewModel(string lane) : Bindable
{
    public string Lane => lane;
    public string Label => lane == "left" ? "左通道" : "右通道";
    public SupplyOperation? Current { get; private set; }
    string health = "状态未取得", status = "等待实体", barcode = "—", package = "—", remaining = "—", supply = "—", reason = "", occupancy = "", splitProgress = "";
    string barcodeDraft = "000123", quantityDraft = "1", retainedDraft = "";
    bool canSubmit;
    public string Health { get => health; private set => Put(ref health, value); }
    public string Status { get => status; private set => Put(ref status, value); }
    public string Barcode { get => barcode; private set => Put(ref barcode, value); }
    public string Package { get => package; private set => Put(ref package, value); }
    public string Remaining { get => remaining; private set => Put(ref remaining, value); }
    public string Supply { get => supply; private set => Put(ref supply, value); }
    public string Reason { get => reason; private set => Put(ref reason, value); }
    public string Occupancy { get => occupancy; private set => Put(ref occupancy, value); }
    public string SplitProgress { get => splitProgress; private set => Put(ref splitProgress, value); }
    public bool CanSubmit { get => canSubmit; private set => Put(ref canSubmit, value); }
    public string BarcodeDraft { get => barcodeDraft; set => Put(ref barcodeDraft, value); }
    public string QuantityDraft { get => quantityDraft; set => Put(ref quantityDraft, value); }
    public string RetainedDraft { get => retainedDraft; set => Put(ref retainedDraft, value); }
    public void Apply(StationSnapshot snapshot)
    {
        Current = snapshot.State.Lanes.GetValueOrDefault(lane);
        var d = snapshot.State.Devices.GetValueOrDefault(lane);
        Health = d?.Health switch { DeviceHealth.Ready => "设备就绪", DeviceHealth.Connecting => "连接中", DeviceHealth.Reconnecting => "重连中", DeviceHealth.Disconnected => "已断开", DeviceHealth.Faulted => "设备故障", DeviceHealth.Stopping => "停止中", DeviceHealth.Stopped => "已停止", _ => "状态未取得 / 禁止入场" };
        Status = Current?.Status switch
        {
            OperationStatus.Observed => "已入场 · 等待读码", OperationStatus.ValidationPending => "已保存 · 等待校验", OperationStatus.ValidationRejected => "校验未通过",
            OperationStatus.AwaitingAction => "待确认供包", OperationStatus.SplitRequired => "待拆包计数", OperationStatus.SubmitPending => "已保存 · 待发送 / 处理中",
            OperationStatus.Accepted => "业务已接受", OperationStatus.Rejected => "业务已拒绝", OperationStatus.OutcomeUnknown => "结果待核对", OperationStatus.Cancelled => "草稿已处置", null => "等待实体", _ => "状态未取得"
        };
        Barcode = Current?.Barcode is { Length: > 0 } code ? code : "—";
        Package = Current?.Package?.ToString() ?? "—"; Remaining = Current?.Remaining?.ToString() ?? "—"; Supply = Current?.Supply?.ToString() ?? "—";
        Reason = Current?.Reason ?? "";
        Occupancy = Current is null ? "实体位待入场" : "实体占位：" + Current.Occupancy;
        SplitProgress = Current?.Status == OperationStatus.SplitRequired ? $"有效保留计数 {Current.SplitScans.Count} / {Current.Supply} · 移除 {Current.Package - Current.Supply}" : "";
        if (Current?.Occupancy == Ipc.Supply.Occupancy.Clear)
        {
            Status = "等待下一实体"; Barcode = "—"; Package = Remaining = Supply = "—";
            Reason = "上一作业：" + Current.Status; SplitProgress = "";
        }
        CanSubmit = snapshot.OperationGuards.GetValueOrDefault(lane)?.Allowed == true;
    }
}
public sealed class StationViewModel : Bindable
{
    string identity = "未登录", globalStatus = "正在核验装配…", context = "等待箱", devices = "", lastResult = "SIMULATION · 工程参考产品", boxDraft = "000001";
    public string Identity { get => identity; set => Put(ref identity, value); }
    public string GlobalStatus { get => globalStatus; set => Put(ref globalStatus, value); }
    public string Context { get => context; set => Put(ref context, value); }
    public string Devices { get => devices; set => Put(ref devices, value); }
    public string LastResult { get => lastResult; set => Put(ref lastResult, value); }
    public string BoxDraft { get => boxDraft; set => Put(ref boxDraft, value); }
}

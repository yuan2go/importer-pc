using System.Text.Json;
using Ipc.Platform;

namespace Ipc.Application.ParcelFlow.Desktop;

public sealed record IngressInspectionItem(string Id, string Position, string At, string State, string Detail)
{
    public string Label => (Position == "left" ? "左位" : Position == "right" ? "右位" : Position) + " · " + At + " · " + (State == "Interrupted" ? "待核对" : "处理中 / 待停止恢复") + " · " + Id;
}
public partial class ParcelViewModel
{
    Guid? ingressRuntime;
    long ingressGeneration;
    public IngressInspectionItem[] InterruptedInputs { get; private set; } = [];
    IngressInspectionItem? selectedIngress;
    public IngressInspectionItem? SelectedIngress
    { get => selectedIngress; set { selectedIngress = value; Changed(nameof(SelectedIngress)); Changed(nameof(IngressInspectionDetail)); } }
    public string IngressInspectionReason { get; set; } = "";
    public string IngressInspectionStatus { get; private set; } = "读取当前输入责任后逐笔核对实体。此操作不重发、不清偿未知请求。";
    public string IngressInspectionDetail => SelectedIngress?.Detail ?? "未选择输入";
    public async Task LoadIngressResponsibilitiesAsync()
    {
        RequireFreshSnapshot(); if (!IsManagementMode) throw new InvalidOperationException("先进入管理模式");
        var runtime = Snapshot!.RuntimeId; var generation = ++ingressGeneration;
        var response = await ServiceAsync(() => application.CommandAsync(new(Wire.Id(), Snapshot.Revision, "IngressResponsibilities")));
        if (generation != ingressGeneration || Snapshot?.RuntimeId != runtime || !IsManagementMode) return;
        if (response.Status != "IngressResponsibilities") throw new InvalidOperationException(response.Reason);
        using var json = JsonDocument.Parse(response.Reason);
        InterruptedInputs = json.RootElement.EnumerateArray().Select(row => new IngressInspectionItem(row.GetProperty("Id").GetString()!,
            row.GetProperty("Position").GetString()!, row.GetProperty("At").GetString()!, row.GetProperty("State").GetString()!, row.GetRawText())).ToArray();
        ingressRuntime = runtime; SelectedIngress = null;
        IngressInspectionStatus = InterruptedInputs.Length == 0 ? "当前没有未决输入责任。原业务请求的恢复条件仍分别检查。" : "本批 " + InterruptedInputs.Length + " 笔。确认后重新读取下一批；处理中输入须先完成受控停止与重启恢复。";
        Changed(nameof(InterruptedInputs)); Changed(nameof(IngressInspectionStatus));
    }
    public async Task InspectIngressAsync()
    {
        RequireFreshSnapshot(); var selected = SelectedIngress;
        if (!IsManagementMode || Snapshot!.RuntimeId != ingressRuntime || selected is null || selected.State != "Interrupted" || !InterruptedInputs.Contains(selected))
            throw new InvalidOperationException("重新读取当前实例，并选择一笔待核对输入");
        if (string.IsNullOrWhiteSpace(IngressInspectionReason)) throw new InvalidOperationException("填写已核对实体、原作业和原回执的事实");
        var runtime = ingressRuntime; var generation = ingressGeneration;
        var receipt = await ServiceAsync(() => application.CommandAsync(new(Wire.Id(), Snapshot.Revision, "InspectInterruptedIngress", selected.Id, IngressInspectionReason)));
        if (Snapshot?.RuntimeId != runtime || generation != ingressGeneration || !IsManagementMode) return;
        IngressInspectionStatus = receipt.Status == "Persisted" ? "已记录本笔核对，原请求及未知结果保留。重新读取输入责任后继续。" : receipt.Reason;
        Changed(nameof(IngressInspectionStatus));
        if (receipt.Status == "Persisted") { InterruptedInputs = InterruptedInputs.Where(x => x.Id != selected.Id).ToArray(); SelectedIngress = null; Changed(nameof(InterruptedInputs)); }
    }
}

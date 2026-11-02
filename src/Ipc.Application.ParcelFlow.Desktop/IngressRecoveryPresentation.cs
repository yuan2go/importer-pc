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
    public string IngressInspectionStatus { get; private set; } = "读取未处理的扫码后逐笔核对包裹。此操作不会重发，也不会解除格口未确认的作业。";
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
        IngressInspectionStatus = InterruptedInputs.Length == 0 ? "没有未处理的扫码。" : "本批 " + InterruptedInputs.Length + " 笔。确认后读取下一批；正在处理的扫码需先暂停并重启软件后再处理。";
        Changed(nameof(InterruptedInputs)); Changed(nameof(IngressInspectionStatus));
    }
    public async Task InspectIngressAsync()
    {
        RequireFreshSnapshot(); var selected = SelectedIngress;
        if (!IsManagementMode || Snapshot!.RuntimeId != ingressRuntime || selected is null || selected.State != "Interrupted" || !InterruptedInputs.Contains(selected))
            throw new InvalidOperationException("请重新读取，并选择一笔待核对的扫码");
        if (string.IsNullOrWhiteSpace(IngressInspectionReason)) throw new InvalidOperationException("填写核对结果：包裹实际情况、对应作业和 RCS 返回");
        var runtime = ingressRuntime; var generation = ingressGeneration;
        var receipt = await ServiceAsync(() => application.CommandAsync(new(Wire.Id(), Snapshot.Revision, "InspectInterruptedIngress", selected.Id, IngressInspectionReason)));
        if (Snapshot?.RuntimeId != runtime || generation != ingressGeneration || !IsManagementMode) return;
        IngressInspectionStatus = receipt.Status == "Persisted" ? "已记录本笔核对结果。重新读取后继续下一笔。" : receipt.Reason;
        Changed(nameof(IngressInspectionStatus));
        if (receipt.Status == "Persisted") { InterruptedInputs = InterruptedInputs.Where(x => x.Id != selected.Id).ToArray(); SelectedIngress = null; Changed(nameof(InterruptedInputs)); }
    }
}

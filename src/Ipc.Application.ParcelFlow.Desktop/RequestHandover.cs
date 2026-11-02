using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.ParcelFlow.Desktop;

public partial class ParcelViewModel
{
    // Only a projection of the selected original. Reading/copying never issues a command.
    public string RequestHandover => Selected is not { } op ? "选择作业后查看交接信息。" : Handover(op, IsManagementMode);
    public bool SelectedRcsQueryUnavailable => Selected?.Wcs is { State: StageState.Unknown, Request.Binding.Contract: "SPORTMASTER-RCS-SORTER-v1" } &&
        !new[] { Selected.Plc, Selected.Capture, Selected.Upload }.OfType<StageFact>().Any(s => s.State == StageState.Unknown);
    public string HistoryQueryGuidance => SelectedRcsQueryUnavailable ? "当前 RCS 接口不支持查询结果，该作业不会被重发。请复制交接信息，交给维护人员处理。" : "“查询”和“继续”只针对选中的作业。";
    public static string Handover(ParcelOperation op, bool management)
    {
        var lines = new List<string>
        {
            "作业交接信息",
            "工位：" + op.FrozenConfiguration.StationId + " · 投递位：" + (op.PositionId ?? "main"),
            "观察时间：" + op.ObservedAt.ToString("yyyy-MM-dd HH:mm:ss.fff zzz"),
            "包裹/受理：" + op.AdmissionId + " · 作业：" + op.Id,
            "条码：" + op.ParcelCode + "（相同条码不代表同一件包裹）",
            "本地状态：" + ChineseInterfaceText.Status(op.State.ToString())
        };
        foreach (var fact in new[] { op.Wcs, op.Plc, op.Capture, op.Upload }.OfType<StageFact>())
        {
            var request = fact.Request; var reply = fact.Replies.LastOrDefault();
            static string Known(bool? value) => value is true ? "是" : value is false ? "否" : "未知";
            lines.Add("阶段：" + request.Stage + " · 请求：" + request.Id + " · 尝试次数：" + fact.Attempts);
            lines.Add("已知事实：本地阶段 " + ChineseInterfaceText.Status(fact.State.ToString()) + " · 网络交付 " + Known(reply?.TransportSucceeded) + " · 协议确认 " + Known(reply?.ProtocolAcknowledged) + " · 业务接受 " + Known(reply?.BusinessAccepted) + " · 包裹完成 " + Known(reply?.PhysicalCompleted));
            lines.Add("原目标版本：" + request.Binding.Implementation + " · 合同：" + request.Binding.Contract + " · 实现指纹：" + request.ImplementationHash);
            if (management) lines.Add("原目标指纹：" + Wire.Hash(request.Binding.Target) + " · 配置指纹：" + request.ConfigurationHash + " · 报文指纹：" + request.PayloadHash);
            if (management && !string.IsNullOrEmpty(fact.Failure)) lines.Add("受阻原因：" + DiagnosticText.Protect(fact.Failure));
        }
        lines.Add("下一步：保管好该包裹，按作业详情处理。格口未确认的作业不会因网络恢复、重连或重启而自动解除；请勿重新上件、重发或更换格口。");
        lines.Add(op.Wcs?.Request.Binding.Contract == "SPORTMASTER-RCS-SORTER-v1"
            ? "G01：当前 RCS 接口不支持查询结果，也没有人工裁决流程。请交由项目负责人与 RCS 方确认包裹去向；本交接不会解除限制。"
            : "需与 RCS 方核实结果；无法查询结果时该作业保持受阻。本交接不会解除限制。");
        lines.Add("交接不含原报文、凭据或完整配置；对外发送仍需相应材料授权。");
        return string.Join("\n", lines);
    }
}

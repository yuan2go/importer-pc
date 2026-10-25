using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.ParcelFlow.Desktop;

public partial class ParcelViewModel
{
    // Only a projection of the selected original. Reading/copying never issues a command.
    public string RequestHandover => Selected is not { } op ? "选择原作业后查看交接信息。" : Handover(op, IsManagementMode);
    public bool SelectedRcsQueryUnavailable => Selected?.Wcs is { State: StageState.Unknown, Request.Binding.Contract: "SPORTMASTER-RCS-SORTER-v1" } &&
        !new[] { Selected.Plc, Selected.Capture, Selected.Upload }.OfType<StageFact>().Any(s => s.State == StageState.Unknown);
    public string HistoryQueryGuidance => SelectedRcsQueryUnavailable ? "当前原RCS合同没有权威结果查询（G01）。原请求保持阻断；可复制必要交接信息，不重发。" : "查询或继续始终针对选中的原作业；能力和资格由应用服务再次核验。";
    public static string Handover(ParcelOperation op, bool management)
    {
        var lines = new List<string>
        {
            "原作业交接 · 只读",
            "工位：" + op.FrozenConfiguration.StationId + " · 投递位：" + (op.PositionId ?? "main"),
            "观察时间：" + op.ObservedAt.ToString("yyyy-MM-dd HH:mm:ss.fff zzz"),
            "原实体/准入：" + op.AdmissionId + " · 原作业：" + op.Id,
            "条码：" + op.ParcelCode + "（同码不等于同一实体）",
            "本地状态：" + ChineseInterfaceText.Status(op.State.ToString())
        };
        foreach (var fact in new[] { op.Wcs, op.Plc, op.Capture, op.Upload }.OfType<StageFact>())
        {
            var request = fact.Request; var reply = fact.Replies.LastOrDefault();
            static string Known(bool? value) => value is true ? "是" : value is false ? "否" : "未知";
            lines.Add("原阶段：" + request.Stage + " · 原请求：" + request.Id + " · 尝试次数：" + fact.Attempts);
            lines.Add("已知事实：本地阶段 " + ChineseInterfaceText.Status(fact.State.ToString()) + " · 网络交付 " + Known(reply?.TransportSucceeded) + " · 协议确认 " + Known(reply?.ProtocolAcknowledged) + " · 业务接受 " + Known(reply?.BusinessAccepted) + " · 实体完成 " + Known(reply?.PhysicalCompleted));
            lines.Add("原目标版本：" + request.Binding.Implementation + " · 合同：" + request.Binding.Contract + " · 实现指纹：" + request.ImplementationHash);
            if (management) lines.Add("原目标指纹：" + Wire.Hash(request.Binding.Target) + " · 配置指纹：" + request.ConfigurationHash + " · 报文指纹：" + request.PayloadHash);
            if (management && !string.IsNullOrEmpty(fact.Failure)) lines.Add("原阻断：" + DiagnosticText.Protect(fact.Failure));
        }
        lines.Add("下一步：保管并核对原实体，按原作业详情/对象诊断处理。未知请求保持阻断；网络恢复、重连或重启不清偿原责任。禁止重投、盲重发或更换原目标。");
        lines.Add(op.Wcs?.Request.Binding.Contract == "SPORTMASTER-RCS-SORTER-v1"
            ? "G01：当前RCS未提供权威结果查询或已批准人工裁决合同。由获授权负责人提供合同及实体处置证据后再处理；本交接不会解锁业务。"
            : "外部核对须使用原合同及获授权的事实证据；缺少权威查询能力时保持阻断。本交接不会解锁业务。");
        lines.Add("交接不含原报文、凭据或完整配置；对外发送仍需相应材料授权。");
        return string.Join("\n", lines);
    }
}

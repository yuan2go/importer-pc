using Ipc.Application.Contracts;

namespace Ipc.Application.ParcelFlow.Desktop;

public partial class ParcelViewModel
{
    public bool HasCurrentOperation => Current is not null;
    // Presentation only. All availability and command decisions remain application-owned.
    public bool HasOperationalAttention => !Fresh || Snapshot?.Paused == true || Snapshot?.BlockingReasons.Length > 0 ||
        Snapshot?.Operations.Any(o => new[] { o.Wcs, o.Plc, o.Capture, o.Upload }.OfType<StageFact>()
            .Any(s => s.State is StageState.Unknown or StageState.Conflict)) == true;

    // These idle/read-success descriptions duplicate the workstation and parameter save bar.
    // Keep the original Feedback in diagnostics; never suppress a rejection or pending result.
    public string TaskFeedback => !Busy && Message is ("等待相机读码；模拟工程不操作真实设备。" or
        "模拟观察已排队；状态由应用服务更新。" or
        "作业记录已完成；已读取最新状态。" or "读取作业记录已完成；已读取最新状态。" or
        "读取最新50条已完成；已读取最新状态。" or "下一页已完成；已读取最新状态。" or
        "已读取完整拓扑与能力库存；表单为当前编辑来源。保存后待受控重启生效。") ? "" : OperatorFeedback;
}

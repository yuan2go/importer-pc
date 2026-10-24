using Ipc.Application.Contracts;

namespace Ipc.Application.ParcelFlow.Desktop;

public partial class ParcelViewModel
{
    long globalOperationGeneration;
    long globalMessageRevision = -1;
    string feedbackContext = "workstation";
    long feedbackScope, messageScope;
    long? runningFeedbackScope;
    Exception? globalOperationError;
    public void SetFeedbackContext(string context)
    {
        if (feedbackContext == context) return;
        feedbackContext = context; feedbackScope++;
        Changed(nameof(TaskFeedback));
    }
    public string GlobalOperationFeedback { get; private set; } = "";
    public bool HasGlobalOperationFeedback => GlobalOperationFeedback.Length > 0;
    long BeginGlobalOperation(string label)
    {
        var generation = ++globalOperationGeneration;
        SetGlobalFeedback("正在" + label + "… 以应用回执和最新状态为准。");
        return generation;
    }
    void SetGlobalFeedback(string value)
    {
        GlobalOperationFeedback = value;
        Changed(nameof(GlobalOperationFeedback)); Changed(nameof(HasGlobalOperationFeedback));
    }
    void CompleteGlobalOperation(long generation, string label, string result)
    {
        // Independent Pause may finish while another command/read is still running.
        // Only the latest global intent owns this banner; page reads never clear it.
        if (generation != globalOperationGeneration) return;
        globalMessageRevision = messageRevision + 1; Message = result;
        SetGlobalFeedback(label + " · " + ChineseInterfaceText.Message(result));
        Changed(nameof(TaskFeedback));
    }
    public void DismissGlobalOperationFeedback() { SetGlobalFeedback(""); }
    public bool HasCurrentOperation => Current is not null;
    // Presentation only. All availability and command decisions remain application-owned.
    public bool HasOperationalAttention => !Fresh || Snapshot?.Paused == true || Snapshot?.BlockingReasons.Length > 0 ||
        Snapshot?.Operations.Any(o => new[] { o.Wcs, o.Plc, o.Capture, o.Upload }.OfType<StageFact>()
            .Any(s => s.State is StageState.Unknown or StageState.Conflict)) == true;

    // These idle/read-success descriptions duplicate the workstation and parameter save bar.
    // Keep the original Feedback in diagnostics; never suppress a rejection or pending result.
    public string TaskFeedback => (Busy ? runningFeedbackScope : messageScope) != feedbackScope ? "" :
        !Busy && messageRevision == globalMessageRevision ? "" : !Busy && Message is ("等待相机读码；模拟工程不操作真实设备。" or
        "模拟观察已排队；状态由应用服务更新。" or
        "作业记录已完成；已读取最新状态。" or "读取作业记录已完成；已读取最新状态。" or
        "读取最新50条已完成；已读取最新状态。" or "下一页已完成；已读取最新状态。" or
        "已读取完整拓扑与能力库存；表单为当前编辑来源。保存后待受控重启生效。") ? "" : OperatorFeedback;
}

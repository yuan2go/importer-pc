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
    bool globalReceiptCommitted;
    public void SetFeedbackContext(string context)
    {
        if (feedbackContext == context) return;
        feedbackContext = context; feedbackScope++;
        // A durable successful receipt has already been shown on its originating
        // page. Navigation can retire it; rejected/unknown/conflict replies persist.
        if (globalReceiptCommitted) SetGlobalFeedback("");
        Changed(nameof(TaskFeedback));
    }
    public string GlobalOperationFeedback { get; private set; } = "";
    public bool GlobalReceiptCommitted => globalReceiptCommitted;
    public bool HasGlobalOperationFeedback => GlobalOperationFeedback.Length > 0;
    long BeginGlobalOperation(string label)
    {
        var generation = ++globalOperationGeneration;
        globalReceiptCommitted = false;
        SetGlobalFeedback("正在" + label + "… 以应用回执和最新状态为准。");
        return generation;
    }
    void SetGlobalFeedback(string value)
    {
        GlobalOperationFeedback = value;
        Changed(nameof(GlobalOperationFeedback)); Changed(nameof(HasGlobalOperationFeedback)); Changed(nameof(GlobalReceiptCommitted));
    }
    void CompleteGlobalOperation(long generation, string label, string result, bool committed = false)
    {
        // Independent Pause may finish while another command/read is still running.
        // Only the latest global intent owns this banner; page reads never clear it.
        if (generation != globalOperationGeneration) return;
        globalReceiptCommitted = committed;
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
        !Busy && messageRevision == globalMessageRevision ? "" : !Busy && MessageKind is (PresentationFeedbackKind.Idle or PresentationFeedbackKind.ConfigurationRead or PresentationFeedbackKind.ReadCompleted or PresentationFeedbackKind.ObservationQueued or PresentationFeedbackKind.ResultMirrored) ? "" : OperatorFeedback;
}

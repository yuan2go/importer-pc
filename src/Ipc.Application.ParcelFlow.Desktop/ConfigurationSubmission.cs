using Ipc.Application.Contracts;
namespace Ipc.Application.ParcelFlow.Desktop;

public partial class ParcelViewModel
{
    (Guid Owner, long Revision, ParcelConfiguration Candidate)? unconfirmedSubmission;
    bool submittingConfiguration;
    string submissionEditorSignature = "", submissionEditorDocument = "";
    async Task<ConfigurationSubmissionResult> SubmitConfigurationAsync(long expected, ParcelConfiguration candidate)
    {
        if (submittingConfiguration || unconfirmedSubmission is not null)
            throw new InvalidOperationException("上次保存尚未确认；先核对权威修订，当前输入保留，禁止重复提交");
        var capturedSignature = ConfigurationEditSignature();
        var capturedDocument = configurationAuthority == "高级JSON" ? ConfigurationDraft : Ipc.Platform.Wire.Encode(candidate);
        var capturedReason = ConfigurationReason;
        submittingConfiguration = true;
        try
        {
            var validation = await ServiceAsync(() => application.ValidateConfigurationAsync(candidate));
            if (validation.Status != "Validated")
            {
                validationState = "校验失败 · " + validation.Reason; ParameterEditor?.Report(validation.Reason);
                Changed(nameof(ConfigurationEditorState)); Changed(nameof(ParameterConfigurationSummary)); Message = validationState; return new("Rejected", validation.Reason, expected, "");
            }
            var owner = draftRuntimeId ?? throw new InvalidOperationException("先读取原运行实例");
            submissionEditorSignature = capturedSignature; submissionEditorDocument = capturedDocument;
            unconfirmedSubmission = (owner, expected, candidate); validationState = "正在保存完整候选"; Changed(nameof(ConfigurationEditorState)); Changed(nameof(ParameterConfigurationSummary));
            string? delivery;
            try { var receipt = await ServiceAsync(() => application.StageConfigurationAsync(expected, candidate, capturedReason)); delivery = receipt.Status + " · " + receipt.Reason; }
            catch (Exception ex) { delivery = ex.GetType().Name + " · " + ex.Message; }
            return await InspectSubmissionAsync(delivery);
        }
        finally { submittingConfiguration = false; }
    }
    async Task<ConfigurationSubmissionResult> InspectSubmissionAsync(string? delivery = null)
    {
        var submitted = unconfirmedSubmission ?? throw new InvalidOperationException("本窗没有待核对的保存；当前输入保留");
        ConfigurationSubmissionResult result;
        try { result = await ServiceAsync(() => application.InspectConfigurationSubmissionAsync(submitted.Owner, submitted.Revision, submitted.Candidate)); }
        catch (Exception ex) { result = new("Unknown", "核对暂不可用：" + ex.Message, submitted.Revision, ""); }
        if (result.Status != "Unknown") unconfirmedSubmission = null;
        if (result.Status == "PendingRestart")
        {
            savedEditorSignature = submissionEditorSignature; savedCandidate = submissionEditorDocument;
            Changed(nameof(HasUnsavedConfiguration));
            draftConfigurationRevision = result.Revision; Changed(nameof(DraftRevisionState));
            // The new draft hash comes from the same Application authority, never a UI file read.
            if (application is IParcelConfigurationDraftApplication access)
                try { editorConfigurationHash = await ServiceAsync(() => access.ConfigurationEditorHashAsync(result.Revision)); }
                catch (Exception ex) { editorConfigurationHash = null; delivery = (delivery ?? "") + "；草稿基线暂不可读：" + ex.Message; }
        }
        validationState = (result.Status == "Unknown" ? "保存结果待核对；禁止重复提交" : result.Status) + " · " + result.Reason;
        Changed(nameof(ConfigurationEditorState)); Changed(nameof(ParameterConfigurationSummary)); Message = validationState + (delivery is null ? "" : "\n原保存回执：" + delivery); return result;
    }
    public async Task InspectLastSaveAsync() { _ = await InspectSubmissionAsync(); }
}

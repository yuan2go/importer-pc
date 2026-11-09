using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow.Desktop;

public partial class ParcelViewModel
{
    (Guid Owner, long Revision, ParcelConfiguration Candidate)? unconfirmedSubmission;
    bool submittingConfiguration;
    string submissionEditorSignature = "", submissionEditorDocument = "";
    async Task<ConfigurationSubmissionResult> SubmitConfigurationAsync(long expected, ParcelConfiguration candidate)
    {
        if (submittingConfiguration || unconfirmedSubmission is not null)
            throw new InvalidOperationException("上次保存结果未确认，请先重新读取设置；已填写的内容保留，请勿重复提交");
        var capturedSignature = ConfigurationEditSignature();
        var capturedDocument = configurationAuthority == "高级JSON" ? ConfigurationDraft : Ipc.Platform.Wire.Encode(candidate);
        var capturedReason = ConfigurationReason;
        submittingConfiguration = true;
        try
        {
            var validation = await ServiceAsync(() => application.ValidateConfigurationAsync(candidate));
            if (validation.Status != "Validated")
            {
                configurationSubmissionOutcome = "Rejected";
                configurationSubmissionDetail = ChineseInterfaceText.ParameterMessage(validation.Reason, "请修正标出的设置后再保存。");
                NotifyConfigurationReview(); validationState = configurationSubmissionDetail; ParameterEditor?.Report(validation.Reason);
                Changed(nameof(ConfigurationEditorState)); Changed(nameof(ParameterConfigurationSummary)); Message = validationState; return new("Rejected", validation.Reason, expected, "");
            }
            var owner = draftRuntimeId ?? throw new InvalidOperationException("先读取原运行实例");
            submissionEditorSignature = capturedSignature; submissionEditorDocument = capturedDocument;
            unconfirmedSubmission = (owner, expected, candidate); NotifyConfigurationReview(); validationState = "正在保存设置"; Changed(nameof(ConfigurationEditorState)); Changed(nameof(ParameterConfigurationSummary));
            BusinessReceipt? receipt = null;
            try { receipt = await ServiceAsync(() => application.StageConfigurationAsync(expected, candidate, capturedReason)); }
            catch (Exception) { /* Delivery alone cannot tell whether the write committed. Read back the original submission. */ }
            return await InspectSubmissionAsync(receipt);
        }
        finally { submittingConfiguration = false; }
    }
    async Task<ConfigurationSubmissionResult> InspectSubmissionAsync(BusinessReceipt? receipt = null)
    {
        var submitted = unconfirmedSubmission ?? throw new InvalidOperationException("没有需要确认的保存记录");
        ConfigurationSubmissionResult result;
        try { result = await ServiceAsync(() => application.InspectConfigurationSubmissionAsync(submitted.Owner, submitted.Revision, submitted.Candidate)); }
        catch (Exception) { result = new("Unknown", "", submitted.Revision, ""); }
        configurationSubmissionOutcome = result.Status;
        configurationSubmissionDetail = result.Status switch
        {
            "Effective" when receipt?.Status == "Effective" => ChineseInterfaceText.ParameterMessage(receipt.Reason, "设置已保存并生效。"),
            "Effective" => "设置已保存并生效。设备连接状态请查看“设备与诊断”。",
            "PendingRestart" => "更改已保存，重启软件后生效。",
            "Unchanged" => "设置与当前一致。",
            "Unknown" => "请先查看上次保存结果。已填写的内容会保留。",
            "Conflict" => "设置已被其他操作修改。请返回设置重新读取后再保存。",
            "NotSaved" when receipt?.Status == "Rejected" => ChineseInterfaceText.ParameterMessage(receipt.Reason, "本次未保存，请检查当前设置和操作条件后重试。"),
            "NotSaved" => "本次未保存。原设置保留，可以重试。",
            _ => "请检查当前设置后重试。"
        };
        if (result.Status != "Unknown") unconfirmedSubmission = null;
        if (result.Status is "PendingRestart" or "Unchanged" or "Effective")
        {
            ConfigurationReason = "";
            savedEditorSignature = submissionEditorSignature; savedCandidate = submissionEditorDocument; configurationReviewBaseline = submitted.Candidate; reviewedConfigurationSignature = null;
            Changed(nameof(HasUnsavedConfiguration));
            draftConfigurationRevision = result.Revision; Changed(nameof(DraftRevisionState));
            // The new draft hash comes from the same Application authority, never a UI file read.
            if (application is IParcelConfigurationDraftApplication access)
                try { editorConfigurationHash = await ServiceAsync(() => access.ConfigurationEditorHashAsync(result.Revision)); }
                catch (Exception) { editorConfigurationHash = null; }
        }
        validationState = result.Status == "Unknown" ? "保存结果待确认。" + configurationSubmissionDetail : configurationSubmissionDetail;
        NotifyConfigurationReview(); Changed(nameof(ConfigurationEditorState)); Changed(nameof(ParameterConfigurationSummary)); Message = validationState; return result;
    }
    public async Task InspectLastSaveAsync() { _ = await InspectSubmissionAsync(); }
}

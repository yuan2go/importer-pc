using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.ParcelFlow.Desktop;

public partial class ParcelViewModel
{
    const int DetailPreviewLimit = 65536;
    const string DetailPrompt = "选择一条记录，查看这件包裹的处理结果。";
    string detailsText = DetailPrompt, structuredDetailsText = DetailPrompt;
    ParcelDetailSheet? structuredSheet;
    bool structuredDetailsVisible, rawDetailsVisible, detailsWorker, detailsAuthorityPending;
    internal Func<Task>? BeforeDetailsFormatForVerification { get; set; }
    object? detailsRevision, structuredPublished, rawPublished;
    long detailsGeneration;
    void InvalidateDetailsAuthority()
    {
        detailsAuthorityPending = true; detailsGeneration++; structuredPublished = rawPublished = null;
        detailsText = structuredDetailsText = "管理授权变化，正在读取当前权限的作业详情…"; structuredSheet = null;
        Changed(nameof(Details)); Changed(nameof(StructuredDetails)); Changed(nameof(StructuredSheet));
    }
    void DetailsSnapshotApplied() { detailsAuthorityPending = false; RefreshDetailsProjection(); }
    // Original request/reply identities and configuration hashes are immutable.
    // Revisions consist of explicit mutable state and append-only reply identity.
    static object? StageRevision(StageFact? stage) => stage is null ? null :
        (stage.Request.Id, stage.Request.PayloadHash, stage.State, stage.Attempts, stage.Failure, stage.Replies.Length, stage.Replies.LastOrDefault()?.Id);
    object? DetailsRevision(ParcelOperation? op) => op is null ? null :
        (selectionGeneration, op.Id, op.ConfigurationHash, op.State, op.Reason, op.Slot, op.RecoveryRequired, op.ImageReleased, op.Weight, op.Operator, op.Image, op.OriginalFrame,
         StageRevision(op.Wcs), StageRevision(op.Plc), StageRevision(op.Capture), StageRevision(op.Upload), IsManagementMode);
    internal void SetDetailsVisibility(bool structured, bool raw)
    {
        if (structuredDetailsVisible == structured && rawDetailsVisible == raw) return;
        structuredDetailsVisible = structured; rawDetailsVisible = raw;
        detailsGeneration++; RefreshDetailsProjection();
    }
    void RefreshDetailsProjection()
    {
        var key = DetailsRevision(Selected);
        if (!Equals(key, detailsRevision))
        {
            detailsRevision = key; detailsGeneration++; structuredPublished = rawPublished = null;
            detailsText = structuredDetailsText = Selected is null ? DetailPrompt : "正在整理所选作业详情…"; structuredSheet = null;
            Changed(nameof(Details)); Changed(nameof(StructuredDetails)); Changed(nameof(StructuredSheet));
        }
        if (!IsManagementMode && rawPublished is not null)
        { rawPublished = null; detailsText = "查看详细信息需要进入管理模式。"; Changed(nameof(Details)); }
        if (!detailsAuthorityPending && !detailsWorker && Selected is not null &&
            (structuredDetailsVisible && !Equals(structuredPublished, key) || rawDetailsVisible && IsManagementMode && !Equals(rawPublished, key)))
            Own(BuildDetailsAsync());
    }
    async Task BuildDetailsAsync()
    {
        detailsWorker = true;
        try
        {
            while (Selected is { } operation)
            {
                var key = detailsRevision; var generation = detailsGeneration;
                var structured = structuredDetailsVisible && !Equals(structuredPublished, key);
                var raw = rawDetailsVisible && IsManagementMode && !Equals(rawPublished, key);
                if (detailsAuthorityPending || !structured && !raw) break;
                var result = await Task.Run(async () =>
                {
                    if (BeforeDetailsFormatForVerification is { } before) await before().ConfigureAwait(false);
                    static string Clip(string value)
                    {
                        if (value.Length <= DetailPreviewLimit) return value;
                        var length = char.IsHighSurrogate(value[DetailPreviewLimit - 1]) ? DetailPreviewLimit - 1 : DetailPreviewLimit;
                        return value[..length] + "\n[内容过长已截断；点击“复制完整数据”获取全部]";
                    }
                    var summary = raw ? DestinationSummary(operation) : "";
                    var sheet = structured ? ParcelDetailSheets.Build(operation) : null;
                    return (Sheet: sheet, Structured: sheet is null ? null : Clip(sheet.ToText()),
                        Raw: raw ? Clip((summary.Length == 0 ? "" : summary + "\n\n作业、请求与 RCS 返回完整数据：\n") + Wire.Encode(operation)) : null);
                });
                if (generation != detailsGeneration || !Equals(key, detailsRevision)) continue;
                if (!Equals(key, DetailsRevision(Selected))) { InvalidateDetailsAuthority(); continue; }
                if (result.Structured is { } summary && structuredDetailsVisible)
                { structuredDetailsText = summary; structuredSheet = result.Sheet; structuredPublished = key; Changed(nameof(StructuredDetails)); Changed(nameof(StructuredSheet)); }
                if (result.Raw is { } full && rawDetailsVisible && IsManagementMode)
                { detailsText = full; rawPublished = key; Changed(nameof(Details)); }
                break;
            }
        }
        catch (Exception)
        {
            // Formatting is a local projection of already-read data; reselecting rebuilds it.
            detailsText = structuredDetailsText = "详情暂时没能整理出来，请重新选择这条记录。"; structuredSheet = null;
            Changed(nameof(Details)); Changed(nameof(StructuredDetails)); Changed(nameof(StructuredSheet));
        }
        finally { detailsWorker = false; }
    }
    public async Task<string> FullDetailsAsync()
    {
        var owner = Selected?.Id ?? throw new InvalidOperationException("请先选择作业"); var generation = selectionGeneration;
        await RequireManagementModeAsync(); var authority = ManagementStatus;
        // Local custody is re-read through current authority; no upstream Query.
        var operation = await ServiceAsync(() => application.OperationAsync(owner)) ?? throw new InvalidOperationException("作业详情未读取，请重试");
        var full = await ServiceAsync(() => Task.FromResult(Wire.Encode(operation)));
        await RequireManagementModeAsync();
        if (generation != selectionGeneration || Selected?.Id != owner || ManagementStatus != authority) throw new InvalidOperationException("选择或管理授权已变化；未复制旧详情");
        return full;
    }
}

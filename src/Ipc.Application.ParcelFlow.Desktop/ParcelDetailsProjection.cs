using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.ParcelFlow.Desktop;

public partial class ParcelViewModel
{
    const int DetailPreviewLimit = 65536;
    const string DetailPrompt = "选择一条作业查看原阶段、目标、报文和回执。";
    string detailsText = DetailPrompt, structuredDetailsText = DetailPrompt;
    bool structuredDetailsVisible, rawDetailsVisible, detailsWorker, detailsAuthorityPending;
    internal Func<Task>? BeforeDetailsFormatForVerification { get; set; }
    object? detailsRevision, structuredPublished, rawPublished;
    long detailsGeneration;
    void InvalidateDetailsAuthority()
    {
        detailsAuthorityPending = true; detailsGeneration++; structuredPublished = rawPublished = null;
        detailsText = structuredDetailsText = "管理授权变化，正在读取当前权限的作业详情…";
        Changed(nameof(Details)); Changed(nameof(StructuredDetails));
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
            detailsText = structuredDetailsText = Selected is null ? DetailPrompt : "正在后台整理所选作业详情…";
            Changed(nameof(Details)); Changed(nameof(StructuredDetails));
        }
        if (!IsManagementMode && rawPublished is not null)
        { rawPublished = null; detailsText = "诊断详情需要当前管理模式；原始责任数据保持。"; Changed(nameof(Details)); }
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
                        return value[..length] + "\n[显示预览已截断；复制完整快照可取得原文]";
                    }
                    var summary = raw ? DestinationSummary(operation) : "";
                    return (Structured: structured ? Clip(FormatStructuredDetails(operation)) : null,
                        Raw: raw ? Clip((summary.Length == 0 ? "" : summary + "\n\n原作业、请求与回执完整快照：\n") + Wire.Encode(operation)) : null);
                });
                if (generation != detailsGeneration || !Equals(key, detailsRevision)) continue;
                if (!Equals(key, DetailsRevision(Selected))) { InvalidateDetailsAuthority(); continue; }
                if (result.Structured is { } summary && structuredDetailsVisible)
                { structuredDetailsText = summary; structuredPublished = key; Changed(nameof(StructuredDetails)); }
                if (result.Raw is { } full && rawDetailsVisible && IsManagementMode)
                { detailsText = full; rawPublished = key; Changed(nameof(Details)); }
                break;
            }
        }
        catch (Exception error)
        { detailsText = structuredDetailsText = "详情生成未完成：" + error.GetType().Name + "；原件保持，可重新选择后读取。"; Changed(nameof(Details)); Changed(nameof(StructuredDetails)); }
        finally { detailsWorker = false; }
    }
    public async Task<string> FullDetailsAsync()
    {
        var owner = Selected?.Id ?? throw new InvalidOperationException("先选择原作业"); var generation = selectionGeneration;
        await RequireManagementModeAsync(); var authority = ManagementStatus;
        // Local custody is re-read through current authority; no upstream Query.
        var operation = await ServiceAsync(() => application.OperationAsync(owner)) ?? throw new InvalidOperationException("原作业未读取，保留选择后重试");
        var full = await ServiceAsync(() => Task.FromResult(Wire.Encode(operation)));
        await RequireManagementModeAsync();
        if (generation != selectionGeneration || Selected?.Id != owner || ManagementStatus != authority) throw new InvalidOperationException("选择或管理授权已变化；未复制旧详情");
        return full;
    }
}

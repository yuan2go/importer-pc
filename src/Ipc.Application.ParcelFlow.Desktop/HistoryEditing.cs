using System.Globalization;
using System.Text.RegularExpressions;
using Ipc.Application.Contracts;

namespace Ipc.Application.ParcelFlow.Desktop;

public partial class ParcelViewModel
{
    string historyBarcode = "", historyPosition = "", historyState = "", historyRequest = "";
    void EditHistory(ref string field, string value, string name)
    { if (field == value) return; field = value; historyQueryCancellation?.Cancel(); Changed(name); NotifyHistory(); }
    public string HistoryBarcode { get => historyBarcode; set => EditHistory(ref historyBarcode, value, nameof(HistoryBarcode)); }
    public string HistoryPosition { get => historyPosition; set => EditHistory(ref historyPosition, value ?? "", nameof(HistoryPosition)); }
    public string HistoryState { get => historyState; set => EditHistory(ref historyState, value, nameof(HistoryState)); }
    string historyFrom = "";
    public string HistoryFrom { get => historyFrom; set => EditHistory(ref historyFrom, value, nameof(HistoryFrom)); }
    string historyTo = "";
    public string HistoryToExclusive { get => historyTo; set => EditHistory(ref historyTo, value, nameof(HistoryToExclusive)); }
    public string HistoryRequest { get => historyRequest; set => EditHistory(ref historyRequest, value, nameof(HistoryRequest)); }
    public bool HistoryFiltersChanged => appliedHistorySignature is not null && appliedHistorySignature != HistoryFilterSignature();
    public bool CanExportHistory => !historyLoading && !HistoryExportRunning && historySelection is not null && !HistoryFiltersChanged;
    public string AppliedHistoryRange => historySelection is not { } s ? "尚无固定查询范围" :
        $"已应用：条码 {s.Filter.Barcode ?? "全部"} · 投递位 {s.Filter.Position ?? "全部"} · 状态 {s.Filter.State?.ToString() ?? "全部"} · 原请求 {s.Filter.RequestId ?? "全部"} · 开始（含）{s.Filter.From?.ToString("yyyy-MM-dd HH:mm:ss zzz") ?? "不限"} · 结束（不含）{s.Filter.ToExclusive?.ToString("yyyy-MM-dd HH:mm:ss zzz") ?? "不限"} · 固定序号 ≤ {s.UpperSequence} · 第 {historyPageIndex + 1} 页";
    public sealed record HistoryPositionChoice(string Id, string Label);
    readonly HashSet<string> historicalPositions = [];
    public HistoryPositionChoice[] HistoryPositionChoices => [new("", "全部投递位"), .. (Snapshot is { } s ? ParcelBindings.Positions(s.Configuration.Effective) : []).Select(p => new HistoryPositionChoice(p.Id,
        (p.Id switch { "left" => "左相机", "right" => "右相机", "main" => "主投递位", _ => p.Id }) + " · " + p.Id)),
        .. historicalPositions.Where(id => Snapshot is null || !ParcelBindings.Positions(Snapshot.Configuration.Effective).Any(p => p.Id == id)).Order().Select(id => new HistoryPositionChoice(id, "历史投递位 · " + id))];
    public void ClearHistoryFilters()
    {
        HistoryBarcode = HistoryPosition = HistoryState = HistoryRequest = "";
        HistoryFrom = HistoryToExclusive = "";
        foreach (var name in new[] { nameof(HistoryBarcode), nameof(HistoryPosition), nameof(HistoryState), nameof(HistoryRequest) }) Changed(name);
        Message = "筛选已清除。请重新查询，当前记录仍属于上次查询范围。";
    }
    public string[] HistoryStates => ["", .. Enum.GetNames<ParcelState>()];
    public string HistoryMetricsSummary { get; private set; } = "指标口径：实体作业 / WCS请求 / WCS接受 / 待核对 / 实体完成 / 发送尝试，均不合并同码实体。";
    ParcelHistorySelection? historySelection;
    string? appliedHistorySignature;
    string HistoryFilterSignature() => Ipc.Platform.Wire.Encode(new[] { HistoryBarcode, HistoryPosition, HistoryState, HistoryFrom, HistoryToExclusive, HistoryRequest });
    ParcelHistoryFilter HistoryFilter()
    {
        static string? Optional(string s) => s.Length == 0 ? null : s;
        static DateTimeOffset? Time(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            if (s.Length > 64 || !Regex.IsMatch(s, @"(Z|[+-]\d{2}:\d{2})$") || !DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value))
                throw new InvalidOperationException("时间须含明确时区，例如2026-10-19T08:00:00+08:00；结束时间不包含在范围内");
            return value;
        }
        return new(Optional(HistoryBarcode), Optional(HistoryPosition), HistoryState.Length == 0 ? null : Enum.Parse<ParcelState>(HistoryState), Time(HistoryFrom), Time(HistoryToExclusive), Optional(HistoryRequest));
    }
    CancellationTokenSource? historyExportCancellation;
    CancellationTokenSource? historyQueryCancellation;
    public void CancelHistoryRead() => historyQueryCancellation?.Cancel();
    public bool HistoryExportRunning => historyExportCancellation is not null;
    public void CancelHistoryExport() => historyExportCancellation?.Cancel();
    public async Task ExportHistoryAsync(string destination)
    {
        if (application is not IParcelHistoryApplication query || historySelection is not { } selection || appliedHistorySignature != HistoryFilterSignature())
            throw new InvalidOperationException("先查询并固定当前筛选，再导出同一边界的记录");
        if (historyExportCancellation is not null) throw new InvalidOperationException("已有导出正在执行");
        using var cancellation = new CancellationTokenSource(); historyExportCancellation = cancellation; Changed(nameof(HistoryExportRunning));
        try
        {
            var receipt = await ServiceAsync(() => query.ExportHistoryAsync(selection, destination, cancellation.Token));
            Message = $"导出{receipt.Status} · {receipt.Rows}条 · {receipt.Bytes}字节 · SHA256 {receipt.Sha256}。条码文本带显式文本标记，精确UTF-8另存base64列。";
        }
        catch (OperationCanceledException) { Message = "导出已取消；未发布部分CSV，结果回执保留。"; }
        finally { historyExportCancellation = null; Changed(nameof(HistoryExportRunning)); }
    }
}

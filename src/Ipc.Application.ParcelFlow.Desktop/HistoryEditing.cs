using System.Globalization;
using System.Text.RegularExpressions;
using Ipc.Application.Contracts;

namespace Ipc.Application.ParcelFlow.Desktop;

public partial class ParcelViewModel
{
    public string HistoryBarcode { get; set; } = "";
    public string HistoryPosition { get; set; } = "";
    public string HistoryState { get; set; } = "";
    public string HistoryFrom { get; set; } = "";
    public string HistoryToExclusive { get; set; } = "";
    public string HistoryRequest { get; set; } = "";
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

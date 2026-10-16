using System.Globalization;
using Ipc.Supply;

namespace Ipc.Desktop;

public sealed record HistoryQueryRow(HistoryEntry Entry)
{
    public string Id => Entry.Id;
    public string SessionId => Entry.SessionId;
    public bool IsSession => Entry.Operation is null;
    public SupplyOperation? Operation => Entry.Operation;
    public string Time => FormatDate(Entry.OccurredAt);
    public string Lane => Entry.Operation?.Lane switch { "left" => "左", "right" => "右", null => "会话", var lane => OperatorText.Known(lane) };
    public string Barcode => OperatorText.Known(Entry.Operation?.Barcode ?? Entry.Session.Barcode);
    public string Box => OperatorText.Known(Entry.Session.Barcode);
    public string BoxWave => OperatorText.Known(Entry.Operation?.BoxWave ?? Entry.Session.Context?.BoxWave);
    public string Task => OperatorText.Known(Entry.Operation?.Task?.TaskId);
    public string ItemWave => OperatorText.Known(Entry.Operation?.Task?.ItemWave);
    public string Sku => OperatorText.Known(Entry.Operation?.Task?.Sku);
    public string Destination => OperatorText.Known(Entry.Operation?.Task?.Destination);
    public string Status => Entry.Operation is { } operation ? OperatorText.Operation(operation.Status) : OperatorText.Session(Entry.Session.Status);
    public string Quantity => Entry.Operation is { } operation ?
        $"{operation.Supply?.ToString(CultureInfo.InvariantCulture) ?? "未取得"} {OperatorText.Known(operation.Task?.Unit)}" :
        $"接受账 {Entry.AcceptedQuantity.ToString(CultureInfo.InvariantCulture)}";
    public string Summary => $"作业 {Entry.OperationCount} · 请求 {Entry.RequestCount} · 接受账 {Entry.AcceptedQuantity}";
    public static string FormatDate(DateTimeOffset? date) => date?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture) ?? "未记录日期";
}

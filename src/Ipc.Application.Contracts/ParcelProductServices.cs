namespace Ipc.Application.Contracts;

public sealed record ParcelHistoryFilter(string? Barcode = null, string? Position = null, ParcelState? State = null,
    DateTimeOffset? From = null, DateTimeOffset? ToExclusive = null, string? RequestId = null);
public sealed record ParcelHistorySelection(ParcelHistoryFilter Filter, long UpperSequence);
public sealed record ParcelHistoryMetrics(long Operations, long WcsRequests, long WcsAccepted, long Unknown, long PhysicalCompleted, long SendAttempts);
public sealed record ParcelExportReceipt(string Status, long Rows, long Bytes, string? Sha256, ParcelHistorySelection Selection, string Path);
// Optional query/support capabilities do not make UI own SQL or business facts.
public interface IParcelHistoryApplication
{
    Task<ParcelHistorySelection> SelectHistoryAsync(ParcelHistoryFilter filter);
    Task<ParcelHistoryPage> QueryHistoryAsync(ParcelHistorySelection selection, long beforeSequence = long.MaxValue, int limit = 50);
    Task<ParcelHistoryMetrics> HistoryMetricsAsync(ParcelHistorySelection selection);
    Task<ParcelExportReceipt> ExportHistoryAsync(ParcelHistorySelection selection, string destination, CancellationToken token);
}
public interface IParcelConfigurationDraftApplication
{
    Task<string> ConfigurationEditorHashAsync(long expectedRevision);
    Task<StartupSavedDraft?> ReadEditorDraftAsync();
    Task<StartupSavedDraft> SaveEditorDraftAsync(string expectedConfigurationHash, string? expectedDraftHash, StartupEditorState editor);
}

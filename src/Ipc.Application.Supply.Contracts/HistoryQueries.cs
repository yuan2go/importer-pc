using Ipc.Platform;
namespace Ipc.Supply;

public enum HistoryEntity { Sessions, Operations }

// Text values are exact field matches, not searches through serialized documents.
// Date bounds describe session opening / operation creation, with an exclusive end.
// In session mode operation and request filters mean independently matching children.
// Reuse the returned Query when paging: its rowid/identity boundary excludes newly
// inserted parent records. Existing records, child facts and their statuses remain live;
// edits or retention can change counts/positions. This is not an immutable DB snapshot.
public sealed record HistoryQuery(
    HistoryEntity Entity = HistoryEntity.Sessions,
    string? SessionId = null,
    string? BoxBarcode = null,
    string? ItemBarcode = null,
    string? Sku = null,
    string? TaskId = null,
    SessionStatus? SessionStatus = null,
    OperationStatus? OperationStatus = null,
    RequestStatus? RequestStatus = null,
    RequestKind? RequestKind = null,
    string? Lane = null,
    DateTimeOffset? FromInclusive = null,
    DateTimeOffset? UntilExclusive = null,
    int Offset = 0,
    int Limit = 50,
    long? ThroughRowId = null,
    string? ThroughId = null);

public sealed record HistoryEntry(string Id, string SessionId, StationSession Session,
    SupplyOperation? Operation, DateTimeOffset? OccurredAt, int OperationCount, int RequestCount, int AcceptedQuantity)
{
    public int AttemptCount { get; init; }
    public int IncompleteReceiptCount { get; init; }
}

public sealed record HistoryPage(HistoryQuery Query, HistoryEntry[] Items, int TotalCount)
{
    public bool HasNext => (long)Query.Offset + Items.Length < TotalCount;
}

public sealed record SessionHistoryDetail(StationSession Session, DateTimeOffset? OpenedAt,
    RequestDetail[] Requests, Audit[] Audit, int OperationCount, int AcceptedQuantity);


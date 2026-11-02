namespace Ipc.Application.Contracts;

public enum StationExceptionKind { Unconfirmed, Rejected, Observation }
public sealed record StationExceptionQuery(StationExceptionKind Kind, long? BeforeSequence = null);
// Thin, immutable read model. Commands must resolve the original ID again.
public sealed record StationExceptionItem(long Sequence, string Key, string? OperationId, string? RequestId,
    DateTimeOffset ObservedAt, string Position, string Barcode, string ReasonCode, string? ResponseCode,
    string? ResponseMessage, bool QuerySupported);
public sealed record StationExceptionPage(StationExceptionKind Kind, long UnconfirmedCount, long RejectedCount,
    long ObservationCount, DateTimeOffset ReadAt, StationExceptionItem[] Items, long? NextSequence);
public interface IStationExceptions
{
    Task<StationExceptionPage> ReadExceptionsAsync(StationExceptionQuery query, CancellationToken token);
}

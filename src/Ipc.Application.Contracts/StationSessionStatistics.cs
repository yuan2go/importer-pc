namespace Ipc.Application.Contracts;

public enum StationReportRange { CurrentShift, PreviousShift, Today, LastSevenDays }
public sealed record StationReportQuery(StationReportRange Range, bool IncludeDetails = true);
public sealed record StationRunShift(string Id, long Sequence, string Status, DateTimeOffset OpenedAt,
    DateTimeOffset LastSeenAt, DateTimeOffset? ClosedAt, double ElapsedMilliseconds, bool ElapsedReliable, bool ClockAdjusted);
public sealed record StationPositionTotals(string Position, long Recognitions, long Requests, long Assigned, long Rejected, long Unknown, long ManualRequests);
public sealed record StationReasonCount(string Scope, string Code, long Count);
public sealed record StationSessionReport(string? SelectionId, DateTimeOffset? ExpiresAt, StationReportRange Range,
    StationRunShift? Shift, DateTimeOffset From, DateTimeOffset ToExclusive, DateTimeOffset ReadAt, string TimeZoneId,
    StationPositionTotals[] Totals, StationMetricBucket[] Buckets, StationReasonCount[] Reasons,
    double? RecentAssignedPerMinute, double? EffectiveElapsedMilliseconds, DateTimeOffset DetailsFrom, string[] Notices);
public interface IStationSessionStatistics
{
    Task<StationSessionReport> ReadSessionStatisticsAsync(StationReportQuery query, CancellationToken token);
    Task<string> ExportSessionStatisticsAsync(string selectionId, string destination, CancellationToken token);
}

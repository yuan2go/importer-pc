using Ipc.Platform;
namespace Ipc.Application.Contracts;

public sealed record StationShift(string Name, int StartMinute, int EndMinute);
public sealed record StationOperationsPolicy(string TimeZoneId, StationShift[] Shifts, bool AutomaticHistoryCleanup = false,
    int CleanupHour = 3, int MaximumBackupSets = 7);
public sealed record StationShiftWindow(string Name, DateTimeOffset From, DateTimeOffset ToExclusive);
// These are software facts. They never assert a physical parcel count.
public sealed record StationMetricBucket(DateTimeOffset Minute, string Position, long Recognitions, long Requests,
    long Assigned, long Rejected, long Unknown, long ManualRequests);
public sealed record StationStatistics(DateTimeOffset From, DateTimeOffset ToExclusive, string TimeZoneId,
    StationMetricBucket[] Buckets, StationOperationsPolicy Policy, string[] Notices);
public sealed record StationCleanupStatus(bool Enabled, string State, DateTimeOffset? LastCompleted,
    DateTimeOffset? NextCheck, string? Detail);
public interface IStationOperations
{
    Task<StationOperationsPolicy> ReadOperationsPolicyAsync();
    Task<StationStatistics> ReadStatisticsAsync(DateTimeOffset from, DateTimeOffset toExclusive, CancellationToken token);
    Task<BusinessReceipt> SaveOperationsPolicyAsync(long expectedRevision, StationOperationsPolicy policy);
    Task<StationCleanupStatus> ReadCleanupStatusAsync();
    Task<string> ExportDiagnosticPackageAsync(string destination, CancellationToken token);
}

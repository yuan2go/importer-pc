using Ipc.Platform;
namespace Ipc.Application.Contracts;

public sealed record ParcelRetentionPolicy(int ResidentHistory = SystemParameterDefaults.ResidentHistory, long MinimumFreeBytes = SystemParameterDefaults.MinimumFreeBytes,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] int? DiagnosticLogDays = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] int? HistoryDays = null)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] public long? WarningFreeBytes { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] public long? CriticalFreeBytes { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] public long? WalWarningBytes { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] public int? WalCheckpointSeconds { get; init; }
}
public sealed record ParcelWeightRange(decimal Minimum, decimal Maximum);
public sealed record ParcelBackupSchedule(string Directory, int IntervalMinutes, int MaximumSets);
public sealed record ParcelCapacity(int Unresolved, int Resident, long Archived, int MaximumUnresolved, bool Available, string Policy, long FreeStorageBytes = 0, long MinimumFreeBytes = 0);
public sealed record ParcelHistoryItem(long Sequence, bool Archived, ParcelOperation Operation);
public sealed record ParcelHistoryPage(ParcelHistoryItem[] Items, long? NextBeforeSequence);
public sealed record IdentityConflict(string Id, string OriginalOperation, string[] Positions, string Detail, DateTimeOffset At, bool Acknowledged = false);
public sealed record ConfigurationChangeAudit(string Id, DateTimeOffset At, OperatorIdentity? Actor, string Reason,
    long BeforeRevision, long AfterRevision, string BeforeHash, string CandidateHash, string Status, string? Result = null, ParcelConfiguration? BeforeEffective = null, ParcelConfiguration? BeforePending = null, ParcelConfiguration? Candidate = null, ManagementModeEvidence? Authorization = null);

using Ipc.Platform;

namespace Ipc.Supply;

public enum SessionStatus { Validating, Active, ClosingCheck, AwaitingShortageDecision, Closing, CloseUnknown, Closed, Rejected, AwaitingDisposition }
public enum OperationStatus { Observed, ValidationPending, ValidationRejected, AwaitingAction, SplitRequired, SubmitPending, Accepted, Rejected, OutcomeUnknown, Cancelled, AwaitingDisposition }
public enum Occupancy { Occupied, Departed, Clear }
public enum RequestStatus { Prepared, Dispatching, Accepted, Rejected, Unknown, CancelledBeforeDispatch }
public enum RequestKind { Context, Validate, Submit, CheckClose, Close }
public enum DeviceHealth { Connecting, Ready, Disconnected, Reconnecting, Faulted, Stopping, Stopped, Unknown }
public enum SimulationRole { Operator, Maintenance }
public sealed record Actor(string Id, SimulationRole Role, DateTimeOffset ExpiresAt);
public sealed record BlockReason(string Code, string Scope, string Source, string SourceEpoch, long EvidenceVersion, string ClearAuthority, DateTimeOffset? ExpiresAt = null);
public sealed record DeviceState(string DeviceId, string Epoch, long Sequence, DeviceHealth Health, DateTimeOffset UpdatedAt);
public sealed record Observation(string Id, string DeviceId, string Epoch, string SourceEventId, long Sequence,
    string AdmissionId, string Kind, string[] Codes, int? PackageQuantity, DateTimeOffset ObservedAt);
public sealed class SupplyOperation
{
    public string Id { get; set; } = Wire.Id();
    public string SessionId { get; set; } = "";
    public string Lane { get; set; } = "";
    public string AdmissionId { get; set; } = "";
    public long Revision { get; set; } = 1;
    public string PermitEpoch { get; set; } = "";
    public long PermitRevision { get; set; } = 1;
    public DateTimeOffset PermitExpiresAt { get; set; }
    public string Barcode { get; set; } = "";
    public int? Package { get; set; }
    public int? Remaining { get; set; }
    public int? Supply { get; set; }
    public OperationStatus Status { get; set; }
    public Occupancy Occupancy { get; set; } = Occupancy.Occupied;
    public HashSet<string> SplitScans { get; set; } = [];
    public List<string> ObservationIds { get; set; } = [];
    public string Reason { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
public sealed class StationSession
{
    public string Id { get; set; } = Wire.Id();
    public string Barcode { get; set; } = "";
    public SessionStatus Status { get; set; }
    public string? CloseCheckRequest { get; set; }
    public int? CloseRemaining { get; set; }
    public string? CloseToken { get; set; }
    public string? CloseRequest { get; set; }
    public DateTimeOffset OpenedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ClosedAt { get; set; }
    public Actor? OpenedBy { get; set; }
}
public sealed class StationState
{
    public long Revision { get; set; }
    public StationSession? Session { get; set; }
    public Dictionary<string, SupplyOperation> Lanes { get; set; } = [];
    public Dictionary<string, DeviceState> Devices { get; set; } = [];
    public List<BlockReason> Blocks { get; set; } = [];
    public Actor? Actor { get; set; }
    public bool Quarantined { get; set; }
    public bool Stopping { get; set; }
    public string NextFault { get; set; } = "None";
}
public sealed record BusinessRequest(string Id, string OwnerId, string SessionId, RequestKind Kind, long OwnerRevision,
    string IntentKey, string Payload, string PayloadHash, IntegrationBinding Binding, RequestStatus Status, DateTimeOffset CreatedAt);
public sealed record RequestEnvelope(string RequestId, string PayloadHash, string Contract, string Payload);
public sealed record IntegrationResponse(string RequestId, string PayloadHash, string EvidenceId, string Result, string Reason,
    int? Remaining = null, int? Quantity = null, string? CheckToken = null);
public sealed record SendAttempt(string Id, string RequestId, int Ordinal, DateTimeOffset StartedAt, DateTimeOffset? EndedAt, string Disposition, string? Error);
public sealed record Alarm(string Id, string Code, string Scope, string Source, string Epoch, string? OwnerId,
    DateTimeOffset RaisedAt, string? AckedBy, DateTimeOffset? AckedAt, DateTimeOffset? ClearedAt);
public sealed record Audit(string Id, string OwnerId, string Actor, string CommandId, string Kind, string Evidence, DateTimeOffset At);
public sealed record Command(string Id, long ExpectedRevision, string Kind, string? OwnerId = null, string? Value = null, int? Quantity = null, long? EvidenceVersion = null);
public sealed record CommandReceipt(string Status, string Reason, long Revision, string? OwnerId = null, string? RequestId = null);
public sealed record Guard(bool Allowed, string[] Reasons);
public sealed record StationSnapshot(StationState State, BusinessRequest[] Requests, Alarm[] Alarms, Guard Admit, Guard Submit, Guard Close, Dictionary<string, Guard> OperationGuards, string[] RuntimeFaults, RequestRecovery[] Recovery, int ActiveAlarmCount, long OverloadVersion, Guard OverloadRecovery);
public sealed record RequestRecovery(BusinessRequest Request, Guard Dispatch, Guard LocalTermination, string NextStep);
public sealed record RequestDetail(BusinessRequest Request, SendAttempt[] Attempts, IntegrationResponse[] Responses, Audit[] Audit);
public sealed record ConfigRevision(string Id, string BaseVersion, StationConfig Content, string Actor, string Status, string Reason, DateTimeOffset At);

public interface IIntegrationPort : IAsyncDisposable
{
    Task<IntegrationResponse> ExecuteAsync(BusinessRequest request, CancellationToken token);
    Task<IntegrationResponse?> QueryAsync(BusinessRequest request, CancellationToken token);
    Task<bool> HealthAsync(CancellationToken token);
}
public interface IObservationSource : IAsyncDisposable
{
    string DeviceId { get; }
    string Epoch { get; }
    Task StartAsync(Func<Observation, bool> receive, Action<string, string, DeviceHealth> health, CancellationToken token);
    Task StopAsync(CancellationToken token);
}
public interface IStationApplication
{
    Task<CommandReceipt> ExecuteAsync(Command command);
    Task<StationSnapshot> SnapshotAsync();
    Task<Alarm[]> AlarmHistoryAsync(int offset = 0, int limit = 100);
    Task<SupplyOperation[]> HistoryAsync(string? filter = null, int offset = 0, int limit = 100);
    Task<RequestDetail[]> DetailsAsync(string ownerId);
    Task<ConfigRevision[]> ConfigsAsync();
    Task BackupAsync(string destination);
    Task ReviewObservationsAsync(string evidence);
    Task<int> PruneHistoryAsync(int retentionDays, string evidence);
}
public sealed class GuardException(string message) : Exception(message);
public sealed class FaultInjection
{
    public string? CrashAt { get; set; }
    public bool FailT1 { get; set; }
    public bool FailT3 { get; set; }
    public Action<string>? Hook { get; set; }
    public void Hit(string point)
    {
        Hook?.Invoke(point);
        if (CrashAt == point) Environment.FailFast("SIMULATION crash window " + point);
        if ((point == "T1" && FailT1) || (point == "T3" && FailT3)) throw new IOException("SIMULATION injected store failure at " + point);
    }
}

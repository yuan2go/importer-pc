using Ipc.Platform;

namespace Ipc.Application.Contracts;

public enum StationPositionMode { Enabled, Disabling, Disabled }
public sealed record StationPositionState(string PositionId, StationPositionMode Mode, long Revision,
    DateTimeOffset? ChangedAt, string? ChangedBy, string? OperationId, string? TaskId,
    string? PlacementCode = null, bool PlacementCameraSeen = false);
public sealed record SetStationPositionEnabled(string CommandId, Guid RuntimeId, string PositionId,
    long ExpectedControlRevision, bool Enabled);
public sealed record StationPositionControlRecord(long Sequence, StationPositionState State);

public interface IStationPositionControl
{
    Task<BusinessReceipt> SetPositionEnabledAsync(SetStationPositionEnabled command, CancellationToken token);
    Task<StationPositionControlRecord[]> ReadPositionControlHistoryAsync(long beforeSequence, int limit, CancellationToken token);
}

public enum RcsHeartbeatResult { Received, Failed, Unavailable, NoPositions, Busy, SelectionChanged }

public interface IStationRcsHeartbeat
{
    Task<RcsHeartbeatResult> CheckRcsHeartbeatAsync(CancellationToken token);
}

// Application supplies operational position state; the RCS integration owns its
// existing heartbeat protocol and never infers this state from camera health.
public interface IWcsPositionHeartbeat
{
    void SetEnabledPositions(string[] positionIds);
    Task<RcsHeartbeatResult> SendHeartbeatAsync(CancellationToken token);
}

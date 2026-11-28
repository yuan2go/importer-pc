using Ipc.Platform;
namespace Ipc.Application.Contracts;

public sealed record StationHeartbeatMember(string PositionId, string StationCode, string BcrCode, long ControlRevision);
public sealed record StationHeartbeatIntent(long RequestId, string Configuration, StationHeartbeatMember[] Members)
{
    public Dictionary<string, long> Controls { get; init; } = [];
    public bool MaintenanceLease { get; init; }
}
public sealed record StationHeartbeatConfirmation(bool HttpResponded, bool ProtocolConfirmed, StationVehicleState[] Vehicles,
    DateTimeOffset ReceivedAt, DateTimeOffset ValidUntil)
{
    public DateTimeOffset? AttemptStartedAt { get; init; }
    public string? Target { get; init; }
    public string? RequestBody { get; init; }
    public int? HttpStatus { get; init; }
    public string? ResponseBody { get; init; }
    public int ResponseBytes { get; init; }
    public bool ResponseComplete { get; init; }
    public double ElapsedMs { get; init; }
    public string? Failure { get; init; }
}
// The Application journals the exact intent before HTTP, without holding its
// business gate across I/O. A timeout is not remote cancellation evidence.
public interface IStationHeartbeatOwner
{
    Task<StationHeartbeatIntent?> BeginHeartbeatAsync(long requestId, CancellationToken token);
    Task CompleteHeartbeatAsync(StationHeartbeatIntent intent, StationHeartbeatConfirmation confirmation);
}
public interface IStationHeartbeatControl
{
    void SetHeartbeatOwner(IStationHeartbeatOwner owner);
    void RefreshHeartbeat();
}

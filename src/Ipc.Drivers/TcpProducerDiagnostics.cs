namespace Ipc.Drivers;

public sealed record TcpProducerDiagnostics(string ConnectionState, long ConnectionAttempts, long Connections,
    long Disconnections, long Heartbeats, double HeartbeatDelayMs, double MaximumHeartbeatDelayMs,
    IReadOnlyDictionary<string, long> Failures);

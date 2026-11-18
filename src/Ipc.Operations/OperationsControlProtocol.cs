using System.Text.Json;
namespace Ipc.Operations;

internal sealed record ControlRequest(int Protocol, string Installation, Guid Runtime, Guid Session,
    Guid Request, string Operation, JsonElement[] Arguments, string? ExpectedGrant = null);
internal sealed record ControlReply(int Protocol, string Installation, Guid Runtime, Guid Session,
    Guid Request, int ProcessId, long StartedTicks, DateTimeOffset ProducedAt, JsonElement Result, string? Error, string? ErrorKind)
{
    public Ipc.Platform.MaintenanceProgress? Progress { get; init; }
}

internal static class OperationsControlProtocol
{
    internal const int Version = 2;
    internal const int RequestBudget = 2 * 1024 * 1024;
    // Existing original-image contract caps Mono8 at 32 MiB. This allows its
    // base64 JSON plus metadata; keep the passive overview at 128 KiB.
    internal const int ReplyBudget = 48 * 1024 * 1024;
    internal const string Open = "Session.Open";
}

using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Operations;

// Explicit read-only wire model. Never serialize ParcelSnapshot, configuration,
// frozen requests, images or credentials across the process boundary.
public sealed record OperationsPosition(string Id, bool Admit, bool Manual, string Control,
    string? RoundId, string? OperationId, string? Vehicle, string? VehicleStatus, string[] Reasons);
public sealed record OperationsDevice(string Name, bool Ready, DateTimeOffset ConfirmedAt, DateTimeOffset ValidUntil);
public sealed record OperationsOverview(Guid RuntimeId, long Revision, DateTimeOffset CapturedAt,
    string Station, string Mode, string State, bool Paused, bool PendingConfiguration,
    int VisibleOperations, int UnconfirmedOperations, OperationsPosition[] Positions,
    OperationsDevice[] Devices, string[] Reasons)
{
    public long? FreeStorageBytes { get; init; }
    public long MinimumFreeBytes { get; init; }
    public int? ExecutingRequests { get; init; }
    public long? HistoricalUnknown { get; init; }
}

internal sealed record OperationsRequest(int Protocol, string Installation, string Permission, Guid RequestId, Guid RuntimeId);
internal sealed record OperationsReply(int Protocol, string Installation, string Permission, Guid RequestId,
    Guid RuntimeId, int ProcessId, long StartedTicks, OperationsOverview? Overview, string? Error);

public static class OperationsProtocol
{
    public const int Version = 1;
    public const string Permission = "RuntimeOverview.Read";
    public static readonly TimeSpan Freshness = TimeSpan.FromSeconds(5);
    internal const int MaximumFrame = 128 * 1024;

    internal static OperationsOverview Project(ParcelSnapshot snapshot, DateTimeOffset captured)
    {
        string Text(string text)
        {
            var safe = DiagnosticText.Protect(text);
            return safe.Length > 1024 ? safe[..1024] + "…（已截断）" : safe;
        }
        string[] Reasons(string[] reasons, int limit) => reasons.Length > limit ?
            [.. reasons.Take(limit).Select(Text), $"另有 {reasons.Length - limit} 项原因未列出，请在工作站查看完整信息。"] :
            reasons.Select(Text).ToArray();
        var config = snapshot.Configuration.Effective;
        return new OperationsOverview(snapshot.RuntimeId, snapshot.Revision, captured, config.StationId, config.Mode.ToString(),
            snapshot.State, snapshot.Paused, snapshot.HasPendingConfiguration == true, snapshot.Operations.Length,
            snapshot.Operations.Count(o => o.Wcs?.State == StageState.Unknown || o.Plc?.State == StageState.Unknown),
            (snapshot.Positions ?? []).Select(p => new OperationsPosition(p.Id, p.Admit, p.BarcodeInput?.Manual == true,
                p.SupplyControl?.Mode.ToString() ?? "Unknown", p.CurrentRoundId, p.CurrentOperation,
                p.Vehicle?.VehicleNo, p.Vehicle?.Status, Reasons(p.BlockingReasons, 16))).ToArray(),
            snapshot.Health.Take(32).Select(h => new OperationsDevice(
                h.InstanceId == config.Wcs?.InstanceId ? "RCS" :
                h.InstanceId == (snapshot.Positions ?? []).FirstOrDefault(p => p.Id == "left")?.ReaderDeviceId ? "左相机" :
                h.InstanceId == (snapshot.Positions ?? []).FirstOrDefault(p => p.Id == "right")?.ReaderDeviceId ? "右相机" : Text(h.InstanceId),
                h.Ready, h.ConfirmedAt, h.ValidUntil)).ToArray(), Reasons(snapshot.BlockingReasons, 32))
        {
            FreeStorageBytes = snapshot.Capacity?.FreeStorageBytes,
            MinimumFreeBytes = snapshot.Capacity?.MinimumFreeBytes ?? 0,
            ExecutingRequests = snapshot.Capacity?.ExecutingRequests,
            HistoricalUnknown = snapshot.Capacity?.HistoricalUnknown
        };
    }
}

using Ipc.Platform;
using System.Text.Json.Serialization;
namespace Ipc.Application.Contracts;

// An observation is independent of an admitted entity and of its external result.
public enum ParcelInputOutcome { Admitted, Rejected, Blocked, Duplicate }
public sealed record ParcelInputSnapshot(string PositionId, Observation Observation, ParcelInputOutcome Outcome,
    string Reason, string? OperationId, DateTimeOffset RecordedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DeviceBinding? ReaderBinding = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] BarcodeFilterResult? BarcodeSelection = null)
{
    // Duplicate observation of the parcel that already produced OperationId's request and
    // has not left the reading area (SM-PLACEMENT-PRESENCE-29). It belongs to that
    // placement: displays show its operation's result; it is never a new parcel.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool SamePlacement { get; init; }
}

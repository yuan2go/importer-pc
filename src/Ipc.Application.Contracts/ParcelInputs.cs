using Ipc.Platform;
using System.Text.Json.Serialization;
namespace Ipc.Application.Contracts;

// An observation is independent of an admitted entity and of its external result.
public enum ParcelInputOutcome { Admitted, Rejected, Blocked, Duplicate }
// A later read of the parcel still resting on the tray: recorded, owned by the
// operation that admitted this placement, never a new request (SM-SAME-PLACEMENT-29).
public static class ParcelInputReasons
{
    public const string SamePlacement = "SamePlacement";
    public static bool IsSamePlacement(ParcelInputSnapshot? input) => input is { Outcome: ParcelInputOutcome.Duplicate, OperationId: not null } &&
        input.Observation.OperatorInput is null && input.Reason.StartsWith(SamePlacement + ";", StringComparison.Ordinal);
}
public sealed record ParcelInputSnapshot(string PositionId, Observation Observation, ParcelInputOutcome Outcome,
    string Reason, string? OperationId, DateTimeOffset RecordedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DeviceBinding? ReaderBinding = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] BarcodeFilterResult? BarcodeSelection = null);

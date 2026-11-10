using Ipc.Platform;
using System.Text.Json.Serialization;
namespace Ipc.Application.Contracts;

// An observation is independent of an admitted entity and of its external result.
public enum ParcelInputOutcome { Admitted, Rejected, Blocked, Duplicate }
public enum ParcelInputBlockReason
{
    StationStartingOrStopping, ConfigurationUnconfirmed, RestoreUnconfirmed, ConnectionNotConfigured, InstallationNotReady,
    OperatorSessionExpired, RuntimeFault, StationMaintenance, ManagementOpen, PositionDisabled, SoftwarePaused,
    PositionMaintenance, IdentityConflict, InterruptedScan, InputOverload, CameraNotConfigured, ReaderConfirmationExpired,
    RcsUnavailable, CapabilityUnavailable, PreviousResultPending, PreviousResultUnconfirmed, PreviousWorkBlocked,
    StorageUnavailable, CapacityExhausted, ImageCapacityUnavailable, CameraInspectionIsolated, ExpiredIdentity, ManualConditionsChanged
}
// A later read of the parcel still resting on the tray: recorded, owned by the
// operation that admitted this placement, never a new request (AGENTS.md: one placement, one request).
public static class ParcelInputReasons
{
    public const string SamePlacement = "SamePlacement";
    public static ParcelInputBlockReason? BlockReason(ParcelInputSnapshot input)
    {
        if (input.Outcome != ParcelInputOutcome.Blocked) return null;
        var end = input.Reason.IndexOfAny([';', ':']);
        var code = end < 0 ? input.Reason : input.Reason[..end];
        return Enum.TryParse<ParcelInputBlockReason>(code, out var reason) && Enum.IsDefined(reason) && code == reason.ToString() ? reason : null;
    }
    public static bool IsSamePlacement(ParcelInputSnapshot? input) => input is { Outcome: ParcelInputOutcome.Duplicate, OperationId: not null } &&
        input.Observation.OperatorInput is null && input.Reason.StartsWith(SamePlacement + ";", StringComparison.Ordinal);
}
public sealed record ParcelInputSnapshot(string PositionId, Observation Observation, ParcelInputOutcome Outcome,
    string Reason, string? OperationId, DateTimeOffset RecordedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DeviceBinding? ReaderBinding = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] BarcodeFilterResult? BarcodeSelection = null);

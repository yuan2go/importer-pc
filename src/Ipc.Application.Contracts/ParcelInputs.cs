using Ipc.Platform;
using System.Text.Json.Serialization;
namespace Ipc.Application.Contracts;

// An observation is independent of an admitted entity and of its external result.
public enum ParcelInputOutcome { Admitted, Rejected, Blocked, Duplicate }
public sealed record ParcelInputSnapshot(string PositionId, Observation Observation, ParcelInputOutcome Outcome,
    string Reason, string? OperationId, DateTimeOffset RecordedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DeviceBinding? ReaderBinding = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] BarcodeFilterResult? BarcodeSelection = null);

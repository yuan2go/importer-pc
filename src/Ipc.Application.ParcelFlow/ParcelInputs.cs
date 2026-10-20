using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRepository
{
    // Existing audit sequence is the durable ordering authority. Only startup scans;
    // the runtime retains one input per configured position, never the whole stream.
    public ParcelInputSnapshot[] LatestInputs(IReadOnlyDictionary<string, DeviceBinding> readers)
    {
        var latest = new Dictionary<string, ParcelInputSnapshot>(StringComparer.Ordinal);
        using var command = db.CreateCommand();
        command.CommandText = "SELECT detail FROM parcel_audit WHERE event='InputObserved' ORDER BY seq DESC";
        using var result = command.ExecuteReader();
        while (latest.Count < readers.Count && result.Read())
        {
            var input = Wire.Decode<ParcelInputSnapshot>(result.GetString(0));
            if (readers.TryGetValue(input.PositionId, out var binding) && input.Observation.DeviceId == binding.DeviceId &&
                input.ReaderBinding is not null && Wire.Encode(input.ReaderBinding) == Wire.Encode(binding))
                latest.TryAdd(input.PositionId, input);
        }
        return latest.Values.ToArray();
    }
}

public sealed partial class ParcelRuntime
{
    readonly Dictionary<string, ParcelInputSnapshot> latestInputs = new(StringComparer.Ordinal);
    ParcelInputSnapshot Input(string position, Observation observation, ParcelInputOutcome outcome, string reason, string? operation = null, BarcodeFilterResult? selection = null) =>
        new(position, observation, outcome, reason, operation, DateTimeOffset.UtcNow, ReaderBinding(position), selection);
    DeviceBinding ReaderBinding(string position) => ParcelBindings.Readers(Effective).Single(r => r.DeviceId == positions[position].ReaderDeviceId);
    void RecordInput(ParcelInputSnapshot input, string eventName, bool? paused = null)
    {
        repository!.Commit(null, eventName, input.Reason, paused: paused, input: input);
        latestInputs[input.PositionId] = input;
    }
    // A failed latest observation must not borrow a previous terminal result.
    // Unresolved original responsibility remains visible and guarded independently.
    ParcelOperation? DisplayedOperation(string position)
    {
        var current = repository!.Latest(position);
        if (current is null || !MainTerminated(current)) return current;
        var originalReader = current.ReaderBinding ?? ParcelBindings.Readers(current.FrozenConfiguration).Single(r =>
            r.DeviceId == ParcelBindings.Positions(current.FrozenConfiguration).Single(p => p.Id == position).ReaderDeviceId);
        if (Wire.Encode(originalReader) != Wire.Encode(ReaderBinding(position))) return null;
        return latestInputs.TryGetValue(position, out var input) && input.Outcome is ParcelInputOutcome.Rejected or ParcelInputOutcome.Blocked ? null : current;
    }
}

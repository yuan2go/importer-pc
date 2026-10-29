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
        repository!.Commit(null, eventName, input.Reason, paused: paused, input: input, ingressIdentity: activeIngress.Value);
        latestInputs[input.PositionId] = input;
        LogInput(input);
    }
    void LogInput(ParcelInputSnapshot input)
    {
        if (input.Observation.OperatorInput is null && input.Observation.InspectionSessionId is null)
            parcelPictures?.LinkInput(input.Observation, repository!.LastInputAuditSequence(), input.OperationId);
        if (input.Observation.CaptureSessionId is not null && inspectionStore is { } inspection)
            inspection.LinkInput(input.Observation, repository!.LastInputAuditSequence(), input.OperationId);
        Diagnostic("扫码与准入", "输入处理完成", input.Outcome is ParcelInputOutcome.Blocked or ParcelInputOutcome.Rejected ? "警告" : "信息",
            Wire.Encode(new { input.PositionId, input.Outcome, input.OperationId, input.Observation.AdmissionId, input.Observation.SourceEventId, input.Reason }));
        DebugDiagnostic("扫码与准入", "扫码原始候选与筛选结果", () => new { input.PositionId, input.Outcome, input.OperationId,
            input.Observation.DeviceId, input.Observation.Epoch, input.Observation.SourceEventId, input.Observation.Sequence,
            input.Observation.AdmissionId, input.Observation.ObservedAt, input.RecordedAt,
            CandidateCount = input.Observation.Codes.Length, Candidates = input.Observation.Codes.Take(16).Select(c => DiagnosticFragment(c, 256)).ToArray(),
            CandidatesOmitted = Math.Max(0, input.Observation.Codes.Length - 16), Selection = input.BarcodeSelection, Reason = DiagnosticFragment(input.Reason) });
    }
    // A failed latest observation must not borrow a previous terminal result.
    // Unresolved original responsibility remains visible and guarded independently.
    ParcelOperation? DisplayedOperation(string position)
    {
        var current = repository!.Latest(position);
        if (current?.DebugRequest is not null) return null; // Test destinations never appear as a live production parcel.
        if (current is null || !MainTerminated(current)) return current;
        var originalReader = current.ReaderBinding ?? ParcelBindings.Readers(current.FrozenConfiguration).Single(r =>
            r.DeviceId == ParcelBindings.Positions(current.FrozenConfiguration).Single(p => p.Id == position).ReaderDeviceId);
        if (Wire.Encode(originalReader) != Wire.Encode(ReaderBinding(position))) return null;
        return latestInputs.TryGetValue(position, out var input) && input.Outcome is ParcelInputOutcome.Rejected or ParcelInputOutcome.Blocked ? null : current;
    }
}

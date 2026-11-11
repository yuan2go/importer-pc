using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime
{
    readonly System.Collections.Concurrent.ConcurrentDictionary<string, ParcelInputSnapshot> blockedInputs = new();
    ParcelInputSnapshot? ContinuingBlockedObservation(string position, Observation observation)
    {
        if (Id != "sportmaster" || observation.OperatorInput is not null || observation.InspectionSessionId is not null ||
            observation.Kind != "Read" || observation.CandidateIssue is not null ||
            !readers.TryGetValue(observation.DeviceId, out var reader) || reader.Epoch != observation.Epoch ||
            !blockedInputs.TryGetValue(position, out var input) || input.Observation.Epoch != observation.Epoch ||
            input.Observation.DeviceId != observation.DeviceId || !input.Observation.Codes.SequenceEqual(observation.Codes, StringComparer.Ordinal)) return null;
        return input;
    }

    ParcelInputSnapshot? DisplayedInput(string position, ManualWorkCardExpiry? expired = null)
    {
        if (Id == "sportmaster")
        {
            var round = CurrentRound(position);
            return round?.Observation is { } observation ? new(position, observation,
                round.OperationId is null ? ParcelInputOutcome.Rejected : ParcelInputOutcome.Admitted,
                round.ReadOutcome ?? "正在识读", round.OperationId, round.StartedAt, ReaderBinding(position),
                BarcodeFiltering.Select(round.Filter ?? new BarcodeFilterPolicy(""), observation.Codes, observation.CandidateIssue)) : null;
        }
        if (!latestInputs.TryGetValue(position, out var input) ||
            expired is not null && input.OperationId == expired.OperationId) return null;
        return ContinuingBlockedObservation(position, input.Observation) is { } retained
            ? retained with { Outcome = input.Outcome, Reason = input.Reason, RecordedAt = input.RecordedAt }
            : input;
    }

    void RetainUnadmittedPicture(ParcelInputSnapshot input)
    {
        var eligible = input.Observation.OperatorInput is null && input.Observation.InspectionSessionId is null &&
            (input.Outcome == ParcelInputOutcome.Blocked ||
             input.Outcome == ParcelInputOutcome.Rejected && input.Reason.StartsWith("ObservationExpired;", StringComparison.Ordinal));
        if (eligible && ContinuingBlockedObservation(input.PositionId, input.Observation) is not null) return;
        blockedInputPictures.Remove(input.PositionId);
        blockedInputs.TryRemove(input.PositionId, out _);
        if (Id != "sportmaster" || !eligible || !readers.TryGetValue(input.Observation.DeviceId, out var reader) ||
            RetainInputPicture(input.Observation, reader) is not { } image) return;
        blockedInputPictures[input.PositionId] = image;
        blockedInputs[input.PositionId] = input;
        WriteRuntimeLog("原图留档", "受阻扫码原图已保留", new
        {
            input.PositionId, ObservationId = input.Observation.Id, input.Observation.SourceEventId,
            input.Observation.Epoch, input.Observation.ObservedAt, input.Outcome, input.Reason,
            image.Metadata.OriginalSha256, image.Metadata.Width, image.Metadata.Height
        });
    }
}

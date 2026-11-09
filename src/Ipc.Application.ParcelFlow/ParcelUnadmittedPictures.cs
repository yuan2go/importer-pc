using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime
{
    // Keep the first exact image for this physical placement. Subsequent source
    // observations still pass the ordinary admission and original-request guards.
    ParcelInputSnapshot? ContinuingUnadmittedPlacement(string position, Observation observation)
    {
        if (Id != "sportmaster" || observation.OperatorInput is not null || observation.InspectionSessionId is not null ||
            observation.Kind != "Read" || observation.CandidateIssue is not null ||
            !readers.TryGetValue(observation.DeviceId, out var reader) || reader.Epoch != observation.Epoch) return null;
        lock (placements)
        {
            if (!frameFedPositions.Contains(position) || !placements.TryGetValue(position, out var placement) ||
                placement.EndedAt is not null || placement.UnadmittedInput is not { } input ||
                input.Observation.DeviceId != observation.DeviceId || input.Observation.Epoch != observation.Epoch ||
                observation.Codes is not [var code] || placement.Code != code ||
                !input.Observation.Codes.SequenceEqual(observation.Codes, StringComparer.Ordinal)) return null;
            return input;
        }
    }

    ParcelInputSnapshot[] DisplayedInputs() => latestInputs.Values.Select(input =>
        ContinuingUnadmittedPlacement(input.PositionId, input.Observation) ?? input).ToArray();

    void RetainUnadmittedPicture(ParcelInputSnapshot input)
    {
        var eligible = input.Observation.OperatorInput is null && input.Observation.InspectionSessionId is null &&
            (input.Outcome == ParcelInputOutcome.Blocked ||
             input.Outcome == ParcelInputOutcome.Rejected && input.Reason.StartsWith("ObservationExpired;", StringComparison.Ordinal));
        if (eligible && ContinuingUnadmittedPlacement(input.PositionId, input.Observation) is not null) return;
        blockedInputPictures.Remove(input.PositionId);
        lock (placements)
            if (placements.TryGetValue(input.PositionId, out var old)) old.UnadmittedInput = null;
        if (Id != "sportmaster" || !eligible || !readers.TryGetValue(input.Observation.DeviceId, out var reader) ||
            RetainInputPicture(input.Observation, reader) is not { } image) return;
        blockedInputPictures[input.PositionId] = image;
        lock (placements)
            if (placements.TryGetValue(input.PositionId, out var placement) && placement.EndedAt is null &&
                input.Observation.Codes is [var code] && placement.Code == code)
                placement.UnadmittedInput = input;
        WriteRuntimeLog("原图留档", "受阻扫码原图已保留", new
        {
            input.PositionId, ObservationId = input.Observation.Id, input.Observation.SourceEventId,
            input.Observation.Epoch, input.Observation.ObservedAt, input.Outcome, input.Reason,
            image.Metadata.OriginalSha256, image.Metadata.Width, image.Metadata.Height
        });
    }
}

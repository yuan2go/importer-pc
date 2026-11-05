using System.Security.Cryptography;
using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime
{
    ParcelInputSnapshot? ContinuingRejectedPlacement(string position, Observation observation)
    {
        if (Id != "sportmaster" || observation.OperatorInput is not null || observation.Kind != "Read" ||
            observation.CandidateIssue is not null || observation.Codes.Length == 0 || observation.InspectionSessionId is not null ||
            !ExtensionSchema.IsId(observation.AdmissionId) ||
            !readers.TryGetValue(observation.DeviceId, out var reader) || reader.Epoch != observation.Epoch ||
            observation.ObservedAt > DateTimeOffset.UtcNow) return null;
        lock (placements)
        {
            if (!frameFedPositions.Contains(position) || !placements.TryGetValue(position, out var placement) ||
                placement.EndedAt is not null || placement.RejectedInput is not { } input ||
                !observation.Codes.Any(code => input.Observation.Codes.Contains(code, StringComparer.Ordinal))) return null;
            // A partial read of the same rejected labels is still this parcel,
            // never permission to submit the one label that happened to decode.
            if (observation.ObservedAt > placement.LastSeen) placement.LastSeen = observation.ObservedAt;
            placement.EmptyFrames = 0; placement.EmptySince = null;
            return input;
        }
    }

    void ClaimRejectedPlacement(ParcelInputSnapshot input, ObservationSession reader)
    {
        if (Id != "sportmaster" || input.Observation.Codes.Length == 0) return;
        var o = input.Observation;
        CameraOriginalImage? image = null;
        if (reader.Source is IOriginalFrameSource source)
        {
            try
            {
                using var original = source.RetainOriginalFrame(new(o.DeviceId, o.Epoch, o.SourceEventId, o.AdmissionId, o.ObservedAt));
                var pixels = original.Pixels.ToArray();
                _ = uint.TryParse(o.SourceEventId.Split('.').Last(), out var frame);
                image = new(new(o.Id, "", o.DeviceId, o.Epoch, frame, o.ObservedAt, original.Width, original.Height,
                    o.Symbols ?? [], 0, o.Id, o.SourceEventId, null, Convert.ToHexString(SHA256.HashData(pixels)), true, "Available"), pixels);
            }
            catch (Exception error) when (error is IOException or InvalidOperationException)
            { Diagnostic("原图留档", "拒绝包裹原图不可用", "警告", "Observation=" + o.Id + "; " + error); }
        }
        lock (placements)
            placements[input.PositionId] = new(o.Codes[0], o.ObservedAt, true) { RejectedInput = input, RejectedImage = image, RejectedCodes = o.Codes.ToArray() };
    }

    string? RetainedRejectedFrame(string position, ObservationReadFeedback frame)
    {
        if (frame.Outcome == ObservationReadOutcome.Codes || frame.Scene is not { } scene) return null;
        lock (placements) return codeScenes.TryGetValue(position, out var previous) && previous == (frame.Epoch, scene) &&
            placements.TryGetValue(position, out var placement) && placement.EndedAt is null ? placement.RejectedInput?.Observation.Id : null;
    }

    void RetainRejectedPresentation(string position, Observation observation, string input)
    {
        lock (presentationGate)
        {
            if (presentationFeedback.TryGetValue(position, out var read) && read.ObservationId == observation.Id)
            { presentationFeedback[position] = read with { RetainedObservationId = input }; OfferPresentation(); }
        }
    }
}

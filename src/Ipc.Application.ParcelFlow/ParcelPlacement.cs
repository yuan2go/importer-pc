using Ipc.Platform;

namespace Ipc.Application.ParcelFlow;

// One physical placement on a supply tray is one business request (SM-SAME-PLACEMENT-29).
// Continuous acquisition sees a parcel resting on the tray in every frame; each frame
// is still recorded as its own observation, but only the first admitted read of a
// placement may create an operation/request. A placement ends when:
//   * the camera decodes consecutive no-code frames (empty tray: the vehicle carried
//     the parcel away or the operator lifted it) - reads captured after that moment
//     are a new placement, reads captured before it still belong to the old one; or
//   * only where the position has no per-frame decode facts (manual entry, line readers),
//     its code has not been read at the position for the release window. A camera that
//     reports every decoded frame must show the empty tray: decode can be slower than the
//     window (about 1.5 s per frame on site), so elapsed time alone is no evidence.
// A different code at the position is always a new placement.
public sealed partial class ParcelRuntime
{
    sealed class Placement(string code, DateTimeOffset lastSeen, bool cameraSeen)
    {
        public readonly string Code = code;
        public DateTimeOffset LastSeen = lastSeen;
        public string? Operation;
        // A manually entered parcel may simply be unreadable; camera no-code frames
        // only end a placement whose code the camera has actually read.
        public bool CameraSeen = cameraSeen;
        public int EmptyFrames;
        public DateTimeOffset? EmptySince, EndedAt;
    }
    // Written by the ingress worker (under gate) and by decode callbacks.
    readonly Dictionary<string, Placement> placements = [];
    // Positions whose camera publishes every decoded frame (codes, no-code, rejected).
    readonly HashSet<string> frameFedPositions = [];
    void RegisterPlacementFrames(string position) { lock (placements) frameFedPositions.Add(position); }

    // Returns the operation that already owns this placement, or null when the
    // read may proceed to the normal admission guards. Always records presence.
    string? SamePlacementOwner(string position, string code, DateTimeOffset observedAt)
    {
        var release = definition.SamePlacementReleaseMs;
        if (release <= 0) return null;
        lock (placements)
        {
            if (!placements.TryGetValue(position, out var placement) || placement.Code != code ||
                placement.EndedAt is { } ended && observedAt > ended ||
                !(placement.CameraSeen && frameFedPositions.Contains(position)) && observedAt - placement.LastSeen > TimeSpan.FromMilliseconds(release))
            {
                placements[position] = new(code, observedAt, cameraSeen: true);
                return null;
            }
            if (observedAt > placement.LastSeen) placement.LastSeen = observedAt;
            placement.CameraSeen = true;
            return placement.Operation;
        }
    }

    void ClaimPlacement(string position, string code, DateTimeOffset observedAt, string operation, bool camera)
    {
        if (definition.SamePlacementReleaseMs <= 0) return;
        lock (placements)
        {
            if (!placements.TryGetValue(position, out var placement) || placement.Code != code || placement.EndedAt is not null)
                placements[position] = placement = new(code, observedAt, camera);
            else if (observedAt > placement.LastSeen) placement.LastSeen = observedAt;
            placement.CameraSeen |= camera;
            placement.Operation = operation;
        }
    }

    // Decode-order frame facts of the position's camera (current epoch only).
    void NotePlacementFrame(string position, ObservationReadFeedback frame)
    {
        var emptyFrames = definition.SamePlacementEmptyFrames;
        if (definition.SamePlacementReleaseMs <= 0 || emptyFrames <= 0 || frame.Outcome == ObservationReadOutcome.RejectedFrame) return;
        lock (placements)
        {
            if (!placements.TryGetValue(position, out var placement) || placement.EndedAt is not null || frame.ObservedAt <= placement.LastSeen) return;
            if (frame.Outcome == ObservationReadOutcome.Codes) { placement.EmptyFrames = 0; placement.EmptySince = null; return; }
            if (!placement.CameraSeen) return;
            placement.EmptySince ??= frame.ObservedAt;
            if (++placement.EmptyFrames >= emptyFrames &&
                frame.ObservedAt - placement.EmptySince.Value >= TimeSpan.FromMilliseconds(definition.SamePlacementEmptyMinimumMs))
                placement.EndedAt = placement.EmptySince;
        }
    }
}

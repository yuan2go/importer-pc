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
// A different code at the position is a new placement at once (the next parcel can arrive
// without an empty tray being decoded), except a code already requested since the tray was
// last seen empty: one resting parcel can show two labels, or decode differently under glare
// or occlusion, and switching back to an earlier code must not request it again.
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
        // Other codes requested at this position since the tray was last seen empty.
        public Dictionary<string, Placement> Earlier = [];
    }
    // Written by the ingress worker (under gate) and by decode callbacks.
    readonly Dictionary<string, Placement> placements = [];
    // Positions whose camera publishes every decoded frame (codes, no-code, rejected).
    readonly HashSet<string> frameFedPositions = [];
    void RegisterPlacementFrames(string position) { lock (placements) frameFedPositions.Add(position); }

    bool Released(string position, Placement placement, DateTimeOffset observedAt) =>
        placement.EndedAt is { } ended && observedAt > ended ||
        !(placement.CameraSeen && frameFedPositions.Contains(position)) && observedAt - placement.LastSeen > TimeSpan.FromMilliseconds(definition.SamePlacementReleaseMs);

    // Moves the position to another code. Codes requested since the tray was last seen empty
    // stay with their operation; an empty tray (or the fallback window) forgets all of them.
    Placement SwitchPlacement(string position, Placement? current, string code, DateTimeOffset observedAt, bool camera)
    {
        if (current is null || Released(position, current, observedAt)) return placements[position] = new(code, observedAt, camera);
        var earlier = current.Earlier; current.Earlier = [];
        if (current.Operation is not null) earlier[current.Code] = current;
        foreach (var (other, held) in earlier) if (Released(position, held, observedAt)) earlier.Remove(other);
        if (earlier.Remove(code, out var next)) { if (observedAt > next.LastSeen) next.LastSeen = observedAt; next.EmptyFrames = 0; next.EmptySince = null; }
        else next = new(code, observedAt, camera);
        // A queued frame captured before the tray emptied still belongs to the ended placement.
        next.EndedAt = current.EndedAt;
        next.Earlier = earlier;
        return placements[position] = next;
    }

    // Returns the operation that already owns this placement, or null when the
    // read may proceed to the normal admission guards. Always records presence.
    string? SamePlacementOwner(string position, string code, DateTimeOffset observedAt)
    {
        if (definition.SamePlacementReleaseMs <= 0) return null;
        lock (placements)
        {
            placements.TryGetValue(position, out var placement);
            if (placement is null || placement.Code != code || Released(position, placement, observedAt))
                placement = SwitchPlacement(position, placement, code, observedAt, camera: true);
            else if (observedAt > placement.LastSeen) placement.LastSeen = observedAt;
            placement.CameraSeen = true;
            return placement.Operation;
        }
    }

    void ClaimPlacement(string position, string code, DateTimeOffset observedAt, string operation, bool camera)
    {
        if (definition.SamePlacementReleaseMs <= 0) return;
        lock (placements)
        {
            placements.TryGetValue(position, out var placement);
            if (placement?.EndedAt is not null) placements[position] = placement = new(code, observedAt, camera);
            else if (placement is null || placement.Code != code) placement = SwitchPlacement(position, placement, code, observedAt, camera);
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

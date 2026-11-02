namespace Ipc.Application.ParcelFlow;

// One physical placement on a supply tray is one business request.
// Continuous acquisition sees a parcel resting on the tray in every frame; each
// frame is still recorded as its own observation, but only the first admitted
// read of a placement may create an operation/request. The parcel counts as
// re-placed (operator lifted it and put it back after a failure) only after
// its code has been absent from this position's reads for the release window.
// A different code at the position is always a new placement.
public sealed partial class ParcelRuntime
{
    sealed class Placement(string code, DateTimeOffset lastSeen)
    {
        public readonly string Code = code;
        public DateTimeOffset LastSeen = lastSeen;
        public string? Operation;
    }
    readonly Dictionary<string, Placement> placements = []; // Mutated only under gate.

    // Returns the operation that already owns this placement, or null when the
    // read may proceed to the normal admission guards. Always records presence.
    string? SamePlacementOwner(string position, string code, DateTimeOffset observedAt)
    {
        var release = definition.SamePlacementReleaseMs;
        if (release <= 0) return null;
        if (!placements.TryGetValue(position, out var placement) || placement.Code != code)
        {
            placements[position] = new(code, observedAt);
            return null;
        }
        var absent = observedAt - placement.LastSeen;
        if (observedAt > placement.LastSeen) placement.LastSeen = observedAt;
        if (absent > TimeSpan.FromMilliseconds(release)) { placement.Operation = null; return null; }
        return placement.Operation;
    }

    void ClaimPlacement(string position, string code, DateTimeOffset observedAt, string operation)
    {
        if (definition.SamePlacementReleaseMs <= 0) return;
        if (!placements.TryGetValue(position, out var placement) || placement.Code != code)
            placements[position] = placement = new(code, observedAt);
        else if (observedAt > placement.LastSeen) placement.LastSeen = observedAt;
        placement.Operation = operation;
    }
}

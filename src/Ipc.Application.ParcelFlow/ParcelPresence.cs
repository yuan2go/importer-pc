using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.ParcelFlow;

// SM-PLACEMENT-PRESENCE-29: one placement of a parcel on the tray is one request.
// A continuous camera keeps decoding the parcel while it stays under the lens
// (while RCS answers, while the cart leaves, after a failed request). Those frames
// belong to the placement that already produced a request and are not new parcels.
// The hold is released only by positive evidence that the placement ended: the same
// camera reports frames without any code for a continuous clear interval (parcel
// lifted off / tray carried away), or a different selected code appears.
// This is presence gating per position, not "same code = same entity": after a
// clear interval the same code is a new placement and gets a new request.
// Only sources that report every decoded frame (IObservationReadFeedbackSource) are
// gated; trigger/line sources already emit one event per physical read.
internal sealed record PlacementPresence(string PositionId, string? OperationId, DateTimeOffset LastSeen, long SuppressedFrames, bool Cleared);

public sealed partial class ParcelRuntime
{
    sealed class PresenceHold(string code, string? operationId, DateTimeOffset lastSeen)
    {
        public readonly string Code = code;
        public string? OperationId = operationId;
        public DateTimeOffset LastSeen = lastSeen;
        public DateTimeOffset? EmptySince;
        // Start of the empty interval that ended this placement. Frames observed before it
        // may still be waiting in the position queue; they remain this placement.
        public DateTimeOffset? ClearedFrom;
        public long Suppressed;
    }
    readonly object presenceGate = new();
    readonly Dictionary<string, PresenceHold> presenceHolds = new(StringComparer.Ordinal);
    readonly HashSet<string> presenceDevices = new(StringComparer.Ordinal);

    bool PresenceGated(Observation o) => definition.PlacementClearMs is > 0 && o.OperatorInput is null && o.InspectionSessionId is null &&
        presenceDevices.Contains(o.DeviceId);

    void TrackPresenceDevice(string deviceId)
    {
        if (definition.PlacementClearMs is not > 0) return;
        lock (presenceGate) presenceDevices.Add(deviceId);
    }

    // Startup: a parcel left under the lens across a restart must not be sent again
    // before the camera has seen the tray empty. Debug/test requests never seed.
    void SeedPresenceHolds()
    {
        if (definition.PlacementClearMs is not > 0) return;
        lock (presenceGate)
            foreach (var position in positions.Keys)
                if (repository!.Latest(position) is { } latest && !IsDebugRequest(latest) && !string.IsNullOrEmpty(latest.ParcelCode))
                    presenceHolds[position] = new(latest.ParcelCode, latest.Id, latest.ObservedAt);
    }

    // Camera frame watermark (all frames, including frames without codes). Runs on the
    // acquisition callback; never touches the database or business state.
    void ObservePresenceFrame(string position, ObservationReadFeedback frame)
    {
        var clearMs = definition.PlacementClearMs;
        if (clearMs is not > 0) return;
        PresenceHold? released = null;
        lock (presenceGate)
        {
            if (!presenceHolds.TryGetValue(position, out var hold) || hold.ClearedFrom is not null || frame.ObservedAt < hold.LastSeen) return;
            if (frame.Outcome != ObservationReadOutcome.NoCode) { hold.EmptySince = null; return; }
            hold.EmptySince ??= frame.ObservedAt;
            if (frame.ObservedAt - hold.EmptySince.Value < TimeSpan.FromMilliseconds(clearMs.Value)) return;
            hold.ClearedFrom = hold.EmptySince; released = hold;
        }
        Diagnostic("扫码与准入", "包裹已离开读码区", "信息", Wire.Encode(new { Position = position, released!.OperationId,
            SuppressedFrames = released.Suppressed, EmptyForMs = clearMs.Value, Policy = "Next decoded code is a new placement" }));
    }

    // True when this trusted camera observation is the parcel that already produced a
    // request at this position and has not left the reading area since.
    bool SamePlacement(string position, Observation o, string code)
    {
        if (!PresenceGated(o)) return false;
        lock (presenceGate)
        {
            if (!presenceHolds.TryGetValue(position, out var hold)) return false;
            // After the empty interval, or with another code, this is a new placement.
            if (hold.ClearedFrom is { } cleared && o.ObservedAt >= cleared || !string.Equals(hold.Code, code, StringComparison.Ordinal))
            { presenceHolds.Remove(position); return false; }
            if (hold.ClearedFrom is null)
            {
                if (o.ObservedAt > hold.LastSeen) hold.LastSeen = o.ObservedAt;
                hold.EmptySince = null;
            }
            hold.Suppressed++;
            return true;
        }
    }

    // Presentation hint at ingress, before selection: the held code is in view again.
    bool PlacementCueHeld(string position, Observation o)
    {
        if (!PresenceGated(o)) return false;
        lock (presenceGate)
            return presenceHolds.TryGetValue(position, out var hold) && (hold.ClearedFrom is not { } cleared || o.ObservedAt < cleared) &&
                o.Codes.Contains(hold.Code, StringComparer.Ordinal);
    }

    // Any admitted request (camera or keyboard/PDA) starts the hold for its placement.
    void HoldPlacement(string position, string code, string operationId, DateTimeOffset observedAt)
    {
        if (definition.PlacementClearMs is not > 0) return;
        lock (presenceGate) presenceHolds[position] = new(code, operationId, observedAt);
    }

    // A suppressed frame remains an observation fact (input audit, read cue matching),
    // but it is the placement's own observation: no operation, request or statistic.
    void RecordSamePlacement(string position, Observation o, BarcodeFilterResult selection)
    {
        string? operation;
        lock (presenceGate) operation = presenceHolds.GetValueOrDefault(position)?.OperationId;
        RecordInput(Input(position, o, ParcelInputOutcome.Duplicate,
            "SamePlacement; 包裹仍在读码区，本次放置已发出请求；取下包裹或托盘离开后再次识别才发起新请求", operation, selection) with { SamePlacement = true },
            "SamePlacementObserved");
    }

    // Verification/diagnostic read of the current holds; not an operator contract.
    internal PlacementPresence[] PlacementPresences()
    {
        lock (presenceGate)
            return presenceHolds.Select(pair => new PlacementPresence(pair.Key, pair.Value.OperationId, pair.Value.LastSeen, pair.Value.Suppressed,
                pair.Value.ClearedFrom is not null)).ToArray();
    }
}

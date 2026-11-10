using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRepository
{
    internal string[] ReadReleasedPlacements()
    {
        using var command = db.CreateCommand();
        command.CommandText = "SELECT DISTINCT o.id FROM parcel_audit a, json_each(a.detail,'$.OperationIds') i JOIN parcel_operations o ON o.id=i.value WHERE a.event='StationPlacementReleased'";
        using var reader = command.ExecuteReader();
        var ids = new List<string>();
        while (reader.Read()) ids.Add(reader.GetString(0));
        return ids.ToArray();
    }
}

// One physical placement on a supply tray is one business request (AGENTS.md: one placement, one request).
// Continuous acquisition sees a parcel resting on the tray in every frame; each frame
// is still recorded as its own observation, but only the first admitted read of a
// placement may create an operation/request. A placement ends when:
//   * the camera decodes consecutive no-code frames (empty tray: the vehicle carried
//     the parcel away or the operator lifted it) - reads captured after that moment
//     are a new placement, reads captured before it still belong to the old one. A no-code
//     frame whose scene has not changed since the code was last read (the camera's stillness
//     check gives both frames the same scene) is the decoder missing a parcel that never
//     moved, not an empty tray, and is not counted; or
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
        public ParcelInputSnapshot? RejectedInput;
        public ParcelInputSnapshot? UnadmittedInput;
        public CameraOriginalImage? RejectedImage;
        public string[]? RejectedCodes;
        // A manually entered parcel may simply be unreadable; camera no-code frames
        // only end a placement whose code the camera has actually read.
        public bool CameraSeen = cameraSeen;
        public int EmptyFrames;
        public string? EmptyEpoch;
        public DateTimeOffset? EmptySince, EndedAt;
    }
    // Written by the ingress worker (under gate) and by decode callbacks.
    readonly Dictionary<string, Placement> placements = [];
    // Positions whose camera publishes every decoded frame (codes, no-code, rejected).
    readonly HashSet<string> frameFedPositions = [];
    // Scene of the latest camera frame that read a code at each position. Kept per position, not per placement:
    // the frame fact can arrive before the read creates its placement.
    readonly Dictionary<string, (string Epoch, long? Scene)> codeScenes = [];
    readonly Dictionary<string, ObservationReadFeedback> pendingEmptyTrays = [];
    readonly Dictionary<string, DateTimeOffset> confirmedEmptyTrays = [];
    readonly HashSet<string> releasedPlacements = new(StringComparer.Ordinal);
    void RegisterPlacementFrames(string position) { lock (placements) frameFedPositions.Add(position); }

    bool CanConfirmPlacementRemoval(string position, ParcelOperation? operation)
    {
        if (Id != "sportmaster" || operation is null || IsDebugRequest(operation) ||
            !(NeedsReconciliation(operation) || operation.State == ParcelState.UnpackRequired) ||
            !SessionValid || ManagementWorkspaceOpen || configurationSaving || stopping.IsCancellationRequested) return false;
        var reader = readers[positions[position].ReaderDeviceId];
        if (reader.Health?.IsValid(DateTimeOffset.UtcNow) != true ||
            reader.Source is IObservationInspectionSource && !CameraDraining(reader.Source.DeviceId) && !AutomaticCameraReadsAllowed(reader.Source.DeviceId)) return true;
        lock (placements)
            return !frameFedPositions.Contains(position) ||
                operation.OperatorInput is not null && (!placements.TryGetValue(position, out var placement) || !placement.CameraSeen);
    }

    bool ConfirmPlacementRemoval(BusinessCommand command)
    {
        if (command.Owner is not ("left" or "right") || !positions.ContainsKey(command.Owner) ||
            DisplayedOperation(command.Owner) is not { } operation || command.Value != operation.Id ||
            !CanConfirmPlacementRemoval(command.Owner, operation)) return false;
        var at = DateTimeOffset.UtcNow;
        var detail = new { PositionId = command.Owner, ObservedAt = at, OperationIds = new[] { operation.Id }, Source = "OperatorConfirmedRemoval" };
        repository!.Commit(null, "StationPlacementReleased", Wire.Encode(detail), command: command);
        releasedPlacements.Add(operation.Id);
        confirmedEmptyTrays[command.Owner] = at;
        lock (placements)
            if (placements.TryGetValue(command.Owner, out var placement))
            { placement.EndedAt = at; placement.RejectedImage = null; }
        WriteRuntimeLog("扫码与准入", "操作员确认包裹已取下，原作业已退出本位", detail);
        return true;
    }

    // Empty-tray facts arrive outside the business gate. Commit the release under
    // that gate before admitting another parcel, without touching any original request.
    void CommitEmptyTray(string position)
    {
        if (Id != "sportmaster") return;
        ObservationReadFeedback? empty;
        lock (placements) empty = pendingEmptyTrays.GetValueOrDefault(position);
        if (empty is null) return;
        var operationIds = operations.Values.Where(o => o.PositionId == position && !IsDebugRequest(o) &&
            o.ObservedAt <= empty.ObservedAt && (!MainTerminated(o) || NeedsReconciliation(o)) && !releasedPlacements.Contains(o.Id)).Select(o => o.Id).ToArray();
        if (operationIds.Length > 0)
        {
            var detail = new { PositionId = position, empty.ObservedAt, empty.DeviceId, empty.Epoch, empty.FrameNumber, OperationIds = operationIds };
            repository!.Commit(null, "StationPlacementReleased", Wire.Encode(detail));
            releasedPlacements.UnionWith(operationIds);
            WriteRuntimeLog("扫码与准入", "空托盘已确认，原作业已退出本位", detail);
        }
        confirmedEmptyTrays[position] = empty.ObservedAt;
        lock (placements)
            if (pendingEmptyTrays.GetValueOrDefault(position) == empty) pendingEmptyTrays.Remove(position);
    }

    void CommitEmptyTrays()
    {
        if (Id != "sportmaster") return;
        foreach (var position in positions.Keys) CommitEmptyTray(position);
    }

    bool IsBeforeEmptyTray(string position, DateTimeOffset observedAt) =>
        confirmedEmptyTrays.TryGetValue(position, out var confirmed) && observedAt <= confirmed;

    void RestoreUnfinishedPlacements()
    {
        if (Id != "sportmaster") return;
        foreach (var position in positions.Keys)
            if (DisplayedOperation(position) is { Acquisition: not null } current &&
                (!MainTerminated(current) || NeedsReconciliation(current)) &&
                !placements.ContainsKey(position))
                placements[position] = new(current.ParcelCode, current.ObservedAt, cameraSeen: true) { Operation = current.Id };
    }

    // Presentation may coalesce observations only while the business placement
    // ledger still owns them. This grants no admission or retry authority.
    string? ContinuingCameraPlacementOwner(string position, Observation observation)
    {
        if (Id != "sportmaster" || observation.Kind != "Read" || observation.CandidateIssue is not null) return null;
        lock (placements) return frameFedPositions.Contains(position) && placements.TryGetValue(position, out var placement) &&
            placement.CameraSeen && placement.Operation is not null &&
            (observation.Codes is [var code] && placement.Code == code || placement.RejectedCodes is { } rejected && observation.Codes.Any(rejected.Contains)) &&
            !(placement.EndedAt is { } ended && observation.ObservedAt > ended) ? placement.Operation : null;
    }

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
                PositionControl(position).Mode != StationPositionMode.Disabling && !(placement.CameraSeen && frameFedPositions.Contains(position)) && observedAt - placement.LastSeen > TimeSpan.FromMilliseconds(release))
            {
                placements[position] = new(code, observedAt, cameraSeen: true);
                return null;
            }
            if (observedAt > placement.LastSeen) placement.LastSeen = observedAt;
            // A read proves the parcel was present then: no-code frames counted before it are not an empty tray,
            // whichever of the read and its frame fact arrives first.
            if (placement.EmptySince is { } since && since < observedAt) { placement.EmptyFrames = 0; placement.EmptySince = null; }
            placement.CameraSeen = true;
            return placement.Operation;
        }
    }

    void ClaimPlacement(string position, string code, DateTimeOffset observedAt, string operation, bool camera)
    {
        if (definition.SamePlacementReleaseMs <= 0) return;
        lock (placements)
        {
            if (!camera && placements.TryGetValue(position, out var resting) && resting.CameraSeen && resting.EndedAt is null)
            {
                // Manual correction owns this physical placement; keep its last camera code
                // as presence evidence so the next unchanged camera read cannot create a new job.
                resting.Operation = operation;
                resting.RejectedInput = null; resting.RejectedImage = null;
                if (observedAt > resting.LastSeen) resting.LastSeen = observedAt;
                return;
            }
            if (!placements.TryGetValue(position, out var placement) || placement.Code != code || placement.EndedAt is not null)
                placements[position] = placement = new(code, observedAt, camera);
            else if (observedAt > placement.LastSeen) placement.LastSeen = observedAt;
            placement.CameraSeen |= camera;
            placement.Operation = operation;
            placement.RejectedInput = null; placement.RejectedImage = null;
        }
    }

    // How long a manually entered result stays on the card when nothing else happens at the position.
    internal static readonly TimeSpan ManualResultDisplay = TimeSpan.FromMinutes(10);

    // A finished (chute assigned/refused) result stays on the work card only while it is the
    // current parcel of this shift: never one from before this run opened (restart, previous
    // shift), never after its placement ended (empty tray) or another code took the position,
    // and a manually entered one for at most ManualResultDisplay.
    bool ShowsFinishedResult(string position, ParcelOperation finished)
    {
        if (definition.SamePlacementReleaseMs <= 0) return true;
        if (finished.ObservedAt < stationOpeningAt) return false;
        lock (placements)
        {
            if (!placements.TryGetValue(position, out var placement) || placement.Operation != finished.Id || placement.EndedAt is not null) return false;
            if (finished.OperatorInput is not null && StationTime.GetUtcNow() - finished.ObservedAt > ManualResultDisplay) return false;
            return placement.CameraSeen && frameFedPositions.Contains(position) || StationTime.GetUtcNow() - placement.LastSeen <= ManualResultDisplay;
        }
    }

    // Decode-order frame facts of the position's camera (current epoch only).
    void NotePlacementFrame(string position, ObservationReadFeedback frame)
    {
        var emptyFrames = definition.SamePlacementEmptyFrames;
        if (definition.SamePlacementReleaseMs <= 0 || emptyFrames <= 0 || ManagementWorkspaceOpen || frame.Outcome == ObservationReadOutcome.RejectedFrame) return;
        lock (placements)
        {
            if (frame.Outcome == ObservationReadOutcome.Codes) codeScenes[position] = (frame.Epoch, frame.Scene);
            if (!placements.TryGetValue(position, out var placement) || placement.EndedAt is not null) return;
            if (frame.Outcome == ObservationReadOutcome.Codes)
            {
                if (frame.ObservedAt >= placement.LastSeen) { placement.EmptyFrames = 0; placement.EmptySince = null; }
                return;
            }
            if (frame.ObservedAt <= placement.LastSeen || !placement.CameraSeen) return;
            if (placement.EmptyEpoch != frame.Epoch)
            { placement.EmptyEpoch = frame.Epoch; placement.EmptyFrames = 0; placement.EmptySince = null; }
            // The scene is unchanged since a code was last read here: the parcel is still there and the decoder missed it.
            var rejectedBackground = placement.RejectedInput is { } rejected &&
                CameraBarcodeCandidates.Excluded(Effective.BarcodeFilter, rejected.Observation.Codes)?.Length == rejected.Observation.Codes.Length;
            if (!rejectedBackground && frame.Scene is { } scene && codeScenes.TryGetValue(position, out var codeScene) && codeScene == (frame.Epoch, scene)) return;
            placement.EmptySince ??= frame.ObservedAt;
            if (++placement.EmptyFrames >= emptyFrames &&
                frame.ObservedAt - placement.EmptySince.Value >= TimeSpan.FromMilliseconds(definition.SamePlacementEmptyMinimumMs))
            {
                placement.EndedAt = placement.EmptySince;
                placement.RejectedImage = null;
                pendingEmptyTrays[position] = frame;
            }
        }
    }

    string? RetainedCameraPlacement(string position, ObservationReadFeedback frame)
    {
        if (frame.Outcome == ObservationReadOutcome.Codes || frame.Scene is not { } scene) return null;
        lock (placements) return codeScenes.TryGetValue(position, out var codeScene) && codeScene == (frame.Epoch, scene) &&
            placements.TryGetValue(position, out var placement) && placement.CameraSeen && placement.EndedAt is null
                ? placement.Operation : null;
    }
}

using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRepository
{
    // These state facts have their own owner, never an operation/ingress owner.
    // History retention therefore cannot retire the current switch with a parcel.
    static string PositionControlOwner(string position) => "position-control:" + position;
    static void ValidatePositionControl(StationPositionState state)
    {
        if (state.PositionId is not ("left" or "right") || !Enum.IsDefined(state.Mode) || state.Revision <= 0 ||
            state.ChangedAt is null || string.IsNullOrWhiteSpace(state.ChangedBy) || state.ChangedBy.Length > 256 ||
            state.OperationId?.Length > 256 || state.TaskId?.Length > 4096 ||
            state.PlacementCode is { } code && (code.Length is 0 or > 4096 || code.Any(char.IsControl)))
            throw new InvalidDataException("投递位启停记录格式无效，保留原记录后核对。");
    }
    internal StationPositionState? ReadPositionControl(string position)
    {
        using var query = db.CreateCommand();
        query.CommandText = "SELECT detail FROM parcel_audit WHERE owner=$owner AND event='PositionSupplyChanged' ORDER BY seq DESC LIMIT 1";
        query.Parameters.AddWithValue("$owner", PositionControlOwner(position));
        if (query.ExecuteScalar() is not string body) return null;
        var state = Wire.Decode<StationPositionState>(body); ValidatePositionControl(state);
        if (state.PositionId != position || state.Revision > Revision) throw new InvalidDataException("投递位启停记录身份不符。");
        return state;
    }
    internal StationPositionControlRecord[] PositionControlHistory(long beforeSequence, int limit)
    {
        if (beforeSequence <= 0 || limit is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(limit));
        using var query = db.CreateCommand();
        query.CommandText = "SELECT seq,detail FROM parcel_audit WHERE owner IN ('position-control:left','position-control:right') AND event='PositionSupplyChanged' AND seq<$before ORDER BY seq DESC LIMIT $limit";
        query.Parameters.AddWithValue("$before", beforeSequence); query.Parameters.AddWithValue("$limit", limit);
        using var reader = query.ExecuteReader(); List<StationPositionControlRecord> result = [];
        while (reader.Read())
        {
            var state = Wire.Decode<StationPositionState>(reader.GetString(1)); ValidatePositionControl(state);
            result.Add(new(reader.GetInt64(0), state));
        }
        return result.ToArray();
    }
}

public sealed partial class ParcelRuntime : IStationPositionControl
{
    readonly System.Collections.Concurrent.ConcurrentDictionary<string, StationPositionState> positionControls = new(StringComparer.Ordinal);
    readonly HashSet<string> supplySuspendedReaders = new(StringComparer.Ordinal);
    bool ReaderSupplyActive(string reader) => Id != "sportmaster" || !positions.Values.Any(p => p.ReaderDeviceId == reader && PositionControl(p.Id).Mode == StationPositionMode.Disabled);
    StationPositionState PositionControl(string position) => positionControls.GetValueOrDefault(position)
        ?? new(position, StationPositionMode.Enabled, 0, null, null, null, null);
    bool PositionSupplyEnabled(string position) => Id != "sportmaster" || PositionControl(position).Mode == StationPositionMode.Enabled;
    void InitializePositionControls()
    {
        if (Id != "sportmaster") return;
        foreach (var position in positions.Keys)
            if (repository!.ReadPositionControl(position) is { } state)
            {
                positionControls[position] = state;
                // Restart is not evidence that the draining parcel physically left.
                // Keep only its placement guard; the new shift does not restore a finished work card.
                if (state.Mode == StationPositionMode.Disabling && state.PlacementCode is { } code)
                    placements[position] = new(code, state.ChangedAt!.Value, state.PlacementCameraSeen) { Operation = state.OperationId };
            }
        PublishPositionHeartbeat(ports?.Wcs);
    }
    void PublishPositionHeartbeat(IWcsPort? wcs)
    {
        if (Id == "sportmaster" && wcs is IWcsPositionHeartbeat heartbeat)
            heartbeat.SetEnabledPositions(positions.Keys.Where(PositionSupplyEnabled).ToArray());
    }
    bool CameraDraining(string device) => positions.Values.Any(p => p.ReaderDeviceId == device && PositionControl(p.Id).Mode == StationPositionMode.Disabling);
    bool PositionDrainRequired(string position)
    {
        var pending = repository!.PendingReceipts().Select(r => r.Operation).ToHashSet(StringComparer.Ordinal);
        if (operations.Values.Any(op => op.PositionId == position && !IsDebugRequest(op) &&
            (StageRunning(op) || pending.Contains(op.Id) || !releasedPlacements.Contains(op.Id) && (!MainTerminated(op) || NeedsReconciliation(op))))) return true;
        lock (placements) return placements.TryGetValue(position, out var placement) &&
            (placement.CameraSeen || placement.Operation is not null) && placement.EndedAt is null;
    }
    void MaintainPositionControls()
    {
        if (Id != "sportmaster" || repository is null || backupInProgress) return;
        foreach (var state in positionControls.Values.Where(s => s.Mode == StationPositionMode.Disabling).ToArray())
            if (!maintainedResources.ContainsKey(positions[state.PositionId].ReaderDeviceId) && !PositionDrainRequired(state.PositionId))
            {
                var completed = state with
                {
                    Mode = StationPositionMode.Disabled,
                    Revision = repository.Revision + 1,
                    ChangedAt = StationTime.GetUtcNow(),
                    ChangedBy = "application"
                };
                repository.Commit(null, "PositionSupplyChanged", "", positionControl: completed);
                positionControls[state.PositionId] = completed;
                WriteRuntimeLog("投递位启停", "本次放置结束，投递位已停用", completed);
                WriteStationState("PositionDrainCompleted", force: true);
            }
    }
    public async Task<BusinessReceipt> SetPositionEnabledAsync(SetStationPositionEnabled request, CancellationToken token)
    {
        if (!Guid.TryParseExact(request.CommandId, "N", out _) || request.PositionId is not ("left" or "right"))
            throw new ArgumentException("需要有效命令和左、右投递位。");
        var command = new BusinessCommand(request.CommandId, request.ExpectedControlRevision, "SetPositionEnabled", request.PositionId, Wire.Encode(request));
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (stopped || stopping.IsCancellationRequested || Id != "sportmaster" || repository is null || !SessionValid)
                return new("Rejected", "供包台尚未就绪或操作员会话已失效。", repository?.Revision ?? 0);
            if (repository.PriorCommand(command) is { } prior) return prior;
            var previous = PositionControl(request.PositionId);
            if (request.RuntimeId != runtimeId || request.ExpectedControlRevision != previous.Revision)
                return new("Conflict", "本位启停状态已变化，请核对当前状态后重试。", repository.Revision);
            if (backupInProgress || configurationSaving || !positions.ContainsKey(request.PositionId) ||
                maintainedResources.ContainsKey(positions[request.PositionId].ReaderDeviceId) || maintenanceOperations.Contains("*"))
                return new("Rejected", "配置或维护正在进行，请等待完成后切换投递位。", repository.Revision);
            token.ThrowIfCancellationRequested();
            var label = request.PositionId == "left" ? "左投递位" : "右投递位";
            if ((previous.Mode == StationPositionMode.Enabled) == request.Enabled)
            {
                var unchanged = new BusinessReceipt("Accepted", label + "启停状态未变化。", repository.Revision + 1);
                repository.Commit(null, "PositionSupplyUnchanged", unchanged.Reason, command: command, commandResult: unchanged);
                return unchanged;
            }
            CommitEmptyTray(request.PositionId);
            var current = DisplayedOperation(request.PositionId);
            string? placementCode = null; bool cameraSeen = false;
            lock (placements)
                if (placements.TryGetValue(request.PositionId, out var placement) && placement.EndedAt is null)
                {
                    placementCode = placement.Code; cameraSeen = placement.CameraSeen;
                    if (placement.Operation is { } owner) current = operations.GetValueOrDefault(owner) ?? current;
                }
            var state = new StationPositionState(request.PositionId, request.Enabled ? StationPositionMode.Enabled :
                PositionDrainRequired(request.PositionId) ? StationPositionMode.Disabling : StationPositionMode.Disabled,
                repository.Revision + 1, StationTime.GetUtcNow(), currentOperator?.Subject ?? "local-workstation", current?.Id,
                current?.Wcs?.Replies.LastOrDefault(r => r.Destination is not null)?.Destination?.TaskId, placementCode, cameraSeen);
            var receipt = new BusinessReceipt("Accepted", label + (state.Mode == StationPositionMode.Enabled ? "已启用。" :
                state.Mode == StationPositionMode.Disabling ? "停用中，不再接收新条码；等待本次放置结束。" : "已停用。"), state.Revision);
            repository.Commit(null, "PositionSupplyChanged", "", command: command, commandResult: receipt, positionControl: state);
            positionControls[request.PositionId] = state;
            WriteRuntimeLog("投递位启停", "投递位启停已记录", new { CommandId = request.CommandId, PreviousMode = previous.Mode.ToString(), State = state });
            PublishPositionHeartbeat(ports?.Wcs);
            WriteStationState("PositionSupplyChanged:" + request.CommandId, force: true);
            return receipt;
        }
        catch (Exception error) when (IsStorageError(error)) { StorageFailed(error, "PositionSupplyControl"); throw; }
        finally { gate.Release(); }
    }
    public async Task<StationPositionControlRecord[]> ReadPositionControlHistoryAsync(long beforeSequence, int limit, CancellationToken token)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (Id != "sportmaster" || repository is null || stopped) throw new InvalidOperationException("供包台尚未就绪。");
            return repository.PositionControlHistory(beforeSequence, limit);
        }
        finally { gate.Release(); }
    }
}

using System.Collections.Concurrent;
using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime : ISmHeartbeatOwner, ISmRemoteRecovery
{
    sealed record ArrivalCapture(string Generation, string Epoch, bool Ready, DateTimeOffset Until, string? Failure = null, long MinimumCaptureTicks = 0, string? BoundaryId = null);
    readonly ConcurrentDictionary<string, ArrivalCapture> arrivalCaptures = new();
    readonly Dictionary<string, StationVehicleState> arrivalConfirmations = [];
    readonly Dictionary<string, (string Generation, string Epoch, string Reference)> arrivalVisualBaselines = [];
    readonly Dictionary<string, (long Revision, bool Included)> membershipConfirmations = [];
    readonly HashSet<string> arrivalBoundaryWorkers = [];
    SmHeartbeatIntent? maintenanceHeartbeatPending;
    readonly HashSet<string> maintenanceHeartbeatUncertain = [];
    void PrepareSmHeartbeatMaintenance()
    {
        if (Id != "sportmaster" || ports?.Wcs is not ISmHeartbeatControl) return;
        var cycles = positions.Keys.Select(p => SmCycle(p) with
        {
            HeartbeatMaintenanceLease = true, HeartbeatIncluded = UsesSmArrival(p) && HeartbeatIncludes(p),
            HeartbeatConfiguration = SmHeartbeatPositionConfiguration(p)
        }).ToArray();
        repository!.Commit(null, "HeartbeatMaintenanceLease", "Frozen heartbeat membership may be sent during consistent backup; no parcel admission", cycles: cycles);
        foreach (var cycle in cycles) smCycles[cycle.PositionId] = cycle;
        maintenanceHeartbeatPending = null; maintenanceHeartbeatUncertain.Clear();
    }
    void CompleteSmHeartbeatMaintenance()
    {
        if (Id != "sportmaster" || ports?.Wcs is not ISmHeartbeatControl) return;
        var cycles = positions.Keys.Select(p => SmCycle(p) with
        {
            HeartbeatMaintenanceLease = false, HeartbeatPending = maintenanceHeartbeatPending?.RequestId,
            RecoveryToken = maintenanceHeartbeatUncertain.Contains(p) ? Wire.Id() : SmCycle(p).RecoveryToken,
            UncertainMembership = SmCycle(p).UncertainMembership ?? (maintenanceHeartbeatUncertain.Contains(p) ? SmCycle(p).HeartbeatIncluded : (bool?)null)
        }).ToArray();
        repository!.Commit(null, "HeartbeatMaintenanceReleased", "Original heartbeat uncertainty retained; backup does not cancel remote effects", cycles: cycles);
        foreach (var cycle in cycles) smCycles[cycle.PositionId] = cycle;
        RefreshSmHeartbeat();
    }
    string SmHeartbeatConfiguration => Wire.Hash(Wire.Encode(positions.Keys.Select(p => SmHeartbeatPositionConfiguration(p)).ToArray()));
    string SmHeartbeatPositionConfiguration(string position, ParcelConfiguration? candidate = null)
    {
        var effective = candidate ?? Effective;
        return Wire.Hash(Wire.Encode(new
        {
            Target = effective.Wcs?.Target, effective.StationCode,
            Bcr = effective.Wcs?.Parameters.GetValueOrDefault(position + "-bcr-code")
        }));
    }
    bool SmHeartbeatChangeUnconfirmed(ParcelConfiguration candidate) => smCycles.Values.Any(c =>
        (c.HeartbeatPending is not null || c.UncertainMembership is not null || c.HeartbeatMaintenanceLease) &&
        c.HeartbeatConfiguration != SmHeartbeatPositionConfiguration(c.PositionId, candidate));
    bool UsesSmArrival(string position) => Id == "sportmaster" && Effective.Wcs?.Contract == RcsRequestPolicy.Contract;
    bool HeartbeatIncludes(string position) => PositionControl(position).Mode != StationPositionMode.Disabled;
    void StoreSmCycle(SmPositionCycle cycle, string reason)
    {
        repository!.Commit(null, reason, Wire.Encode(cycle), cycle: cycle);
        smCycles[cycle.PositionId] = cycle;
        if (cycle.RecoveryReason is not null && arrivalCaptures.TryGetValue(positions[cycle.PositionId].ReaderDeviceId, out var closed))
            arrivalCaptures[positions[cycle.PositionId].ReaderDeviceId] = closed with { Until = DateTimeOffset.MinValue };
        WriteRuntimeLog("小车到位", reason, cycle);
    }
    void BindSmHeartbeat(ICapabilityPort? port)
    {
        if (Id == "sportmaster" && port is ISmHeartbeatControl control) control.SetHeartbeatOwner(this);
    }
    void RefreshSmHeartbeat() { if (ports?.Wcs is ISmHeartbeatControl control) control.RefreshHeartbeat(); }
    bool SmMembershipConfirmed(string position, bool included) => membershipConfirmations.TryGetValue(position, out var confirmation) &&
        confirmation == (PositionControl(position).Revision, included);
    bool SmArrivalConfirmed(string position) => PositionSupplyEnabled(position) && SmMembershipConfirmed(position, true) &&
        arrivalConfirmations.TryGetValue(position, out var vehicle) && vehicle.VehicleNo == SmCycle(position).VehicleNo &&
        vehicle.ReceivedAt <= DateTimeOffset.UtcNow && vehicle.ValidUntil > DateTimeOffset.UtcNow;
    string? SmTargetBlock(string position, string? exceptOperation = null, bool debug = false)
    {
        if (Id != "sportmaster") return null;
        if (!PositionHasRcsCode(position)) return "本位相机编码未配置，保持停用；填写后可启用。";
        var cycle = SmCycle(position);
        if (cycle.RecoveryReason is not null) return cycle.RecoveryReason;
        if (cycle.TargetOperation is { } target && target != exceptOperation)
            return "本位原请求的远端目标责任未确认；请停用本位并联系维护核对原请求，勿重复提交。";
        if (debug && CurrentRound(position)?.OperationId is { } production && production != exceptOperation)
            return "本位生产请求尚未结束；请先处理本位原责任，再明确接管调试。";
        if (!UsesSmArrival(position)) return null;
        if (!SmArrivalConfirmed(position)) return "等待小车确认；保留输入，确认后再提交。";
        if (UsesSmArrival(position) && cycle.AutoSuppressed && CurrentRound(position) is null && cycle.SubmittedOperation is null)
            return "本车当前放置已结束；请取件、停用并等撤车确认后再启用，等待新车，不重开自动识读期限。";
        if (cycle.SubmittedOperation is { } submitted && submitted != exceptOperation)
            return "本车已处理；等待换车。需留车处理时，取下包裹、停用本位，待撤车确认后再启用。";
        if (debug && !PositionSupplyEnabled(position)) return "调试真实发送须先启用并确认本位小车；不能向已撤车的位置发送。";
        return null;
    }
    public async Task<SmHeartbeatIntent?> BeginHeartbeatAsync(long requestId, CancellationToken token)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            token.ThrowIfCancellationRequested();
            if (backupInProgress)
            {
                var leased = positions.Keys.Where(p => SmCycle(p).HeartbeatIncluded)
                    .Select(p => new SmHeartbeatMember(p, Effective.StationCode!, Effective.Wcs!.Parameters[p + "-bcr-code"], PositionControl(p).Revision)).ToArray();
                return maintenanceHeartbeatPending = new(requestId, SmHeartbeatConfiguration, leased)
                { Controls = positions.Keys.ToDictionary(p => p, p => PositionControl(p).Revision), MaintenanceLease = true };
            }
            // Startup owns configuration/recovery writes before its workers are
            // published. Do not let the integration's immediate first tick race
            // those writes on the repository's single connection.
            if (!Volatile.Read(ref stationStartupCompleted) || configurationSaving || stopping.IsCancellationRequested) return null;
            if (repository is null || stopped) throw new OperationCanceledException("Runtime stopped", token);
            var members = new List<SmHeartbeatMember>();
            foreach (var position in positions.Keys)
            {
                var included = UsesSmArrival(position) && HeartbeatIncludes(position);
                var cycle = SmCycle(position);
                var uncertain = cycle.UncertainMembership ?? (cycle.HeartbeatPending is not null || cycle.HeartbeatMaintenanceLease ? cycle.HeartbeatIncluded : (bool?)null);
                var cause = cycle.RecoveryCause;
                if (cycle.HeartbeatConfiguration is { } oldConfiguration && oldConfiguration != SmHeartbeatPositionConfiguration(position) && uncertain is not null)
                    cause |= SmRemoteRecoveryCause.MembershipMayApply;
                if (uncertain is { } old && old != included)
                    cause |= SmRemoteRecoveryCause.MembershipMayApply;
                if (!HeartbeatIncludes(position) && cycle.TargetOperation is not null)
                    cause |= SmRemoteRecoveryCause.RequestMayBind;
                StoreSmCycle(cycle with
                {
                    HeartbeatPending = requestId, HeartbeatIncluded = included, HeartbeatConfiguration = SmHeartbeatPositionConfiguration(position), UncertainMembership = uncertain, HeartbeatMaintenanceLease = false,
                    Withdrawn = cycle.Withdrawn || !included, RecoveryCause = cause
                }, "HeartbeatIntentPrepared");
                if (included) members.Add(new(position, Effective.StationCode!, Effective.Wcs!.Parameters[position + "-bcr-code"], PositionControl(position).Revision));
            }
            return new(requestId, SmHeartbeatConfiguration, members.ToArray())
            { Controls = positions.Keys.ToDictionary(p => p, p => PositionControl(p).Revision) };
        }
        finally { gate.Release(); }
    }
    public async Task CompleteHeartbeatAsync(SmHeartbeatIntent intent, SmHeartbeatConfirmation confirmation)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (repository is null || intent.Configuration != SmHeartbeatConfiguration) return;
            if (intent.MaintenanceLease && backupInProgress)
            {
                if (!confirmation.ProtocolConfirmed) foreach (var position in positions.Keys) maintenanceHeartbeatUncertain.Add(position);
                if (maintenanceHeartbeatPending?.RequestId == intent.RequestId) maintenanceHeartbeatPending = null;
                return; // Frozen lease is already durable in both source and backup.
            }
            WriteRuntimeLog("RCS心跳", "心跳实际响应", new { intent.RequestId, confirmation.HttpResponded, confirmation.ProtocolConfirmed, confirmation.ReceivedAt, confirmation.ValidUntil, Members = intent.Members });
            foreach (var position in positions.Keys)
            {
                var member = intent.Members.SingleOrDefault(m => m.PositionId == position);
                var included = member is not null;
                var cycle = SmCycle(position);
                if (cycle.HeartbeatPending != intent.RequestId) continue;
                cycle = cycle with
                {
                    HeartbeatPending = null, RecoveryToken = confirmation.ProtocolConfirmed ? cycle.RecoveryToken : Wire.Id(),
                    UncertainMembership = confirmation.ProtocolConfirmed ? cycle.UncertainMembership : cycle.UncertainMembership ?? included
                };
                StoreSmCycle(cycle, confirmation.ProtocolConfirmed ? "HeartbeatProtocolConfirmed" : "HeartbeatUnconfirmed");
                var current = included == (UsesSmArrival(position) && HeartbeatIncludes(position)) &&
                    intent.Controls.GetValueOrDefault(position, -1) == PositionControl(position).Revision;
                if (!confirmation.ProtocolConfirmed || !current)
                {
                    arrivalConfirmations.Remove(position);
                    if (arrivalCaptures.TryGetValue(positions[position].ReaderDeviceId, out var stale))
                        arrivalCaptures[positions[position].ReaderDeviceId] = stale with { Until = DateTimeOffset.MinValue };
                    continue;
                }
                var wasConfirmed = SmArrivalConfirmed(position);
                membershipConfirmations[position] = (PositionControl(position).Revision, included);
                if (!included) { arrivalConfirmations.Remove(position); continue; }
                var vehicle = confirmation.Vehicles.SingleOrDefault(v => v.StationCode == member!.StationCode && v.CameraCode == member.BcrCode);
                if (vehicle is null)
                {
                    arrivalConfirmations.Remove(position);
                    if (arrivalCaptures.TryGetValue(positions[position].ReaderDeviceId, out var stale))
                        arrivalCaptures[positions[position].ReaderDeviceId] = stale with { Until = DateTimeOffset.MinValue };
                    continue;
                }
                if (!wasConfirmed && cycle.VehicleNo == vehicle.VehicleNo)
                    arrivalCaptures.TryRemove(positions[position].ReaderDeviceId, out _);
                arrivalConfirmations[position] = vehicle;
                if (cycle.VehicleNo != vehicle.VehicleNo && cycle.RecoveryReason is null)
                {
                    // Without withdrawal, the RCS hold contract makes a changed
                    // vehicle evidence that the old request already bound its car.
                    if (cycle.TargetOperation is not null && cycle.Withdrawn)
                    {
                        StoreSmCycle(cycle with { RecoveryCause = cycle.RecoveryCause | SmRemoteRecoveryCause.RequestMayBind }, "ArrivalRecoveryBlocked");
                        continue;
                    }
                    var next = cycle with
                    {
                        Generation = Wire.Id(), VehicleNo = vehicle.VehicleNo, ArrivalAt = vehicle.ReceivedAt,
                        ArrivalEvidence = "heartbeat:" + intent.RequestId, SubmittedOperation = null, SubmissionAttempted = false, TargetOperation = null,
                        AutoSuppressed = false, Presence = TrayPresence.Unknown, Epoch = null, Sequence = 0,
                        Evidence = null, ClearedAt = vehicle.ReceivedAt, Withdrawn = false
                    };
                    if (CurrentRound(position) is { } round)
                        EndSmRound(round, "VehicleChanged", replacementCycle: next);
                    else
                    {
                        repository!.Commit(null, "VehicleArrived", Wire.Encode(new { Cycle = next, BoundOperation = cycle.TargetOperation ?? cycle.SubmittedOperation, Proof = "RCS binds current car before releasing it; this lane was not withdrawn" }), cycle: next);
                        smCycles[position] = next;
                        WriteRuntimeLog("小车到位", "车辆到位与原目标绑定已核对", new { Position = position, Cycle = next, BoundOperation = cycle.TargetOperation ?? cycle.SubmittedOperation });
                    }
                    arrivalVisualBaselines.Remove(position);
                    smPresenceTimes.Remove(position); smPresenceCaptureTicks.Remove(position); blockedInputs.TryRemove(position, out _); blockedInputPictures.Remove(position);
                    arrivalCaptures.TryRemove(positions[position].ReaderDeviceId, out _);
                    InvalidatePresentation();
                }
            }
            MaintainSmArrivalCaptures();
        }
        catch (Exception error) when (IsStorageError(error)) { StorageFailed(error, "HeartbeatJournal"); throw; }
        finally { gate.Release(); }
    }
    void MaintainSmArrivalCaptures()
    {
        if (Id != "sportmaster" || stopping.IsCancellationRequested) return;
        foreach (var position in positions.Keys.Where(UsesSmArrival))
        {
            var reader = readers[positions[position].ReaderDeviceId];
            var generation = SmCycle(position).Generation;
            if (!SmArrivalConfirmed(position) || SmCycle(position).RecoveryReason is not null) continue;
            var until = arrivalConfirmations[position].ValidUntil;
            if (arrivalCaptures.TryGetValue(reader.Source.DeviceId, out var current) && current.Generation == generation && current.Epoch == reader.Epoch)
            {
                arrivalCaptures[reader.Source.DeviceId] = current with { Until = until };
                continue;
            }
            if (reader.Health?.IsValid(DateTimeOffset.UtcNow) != true || reader.Source is not IProductionArrivalSource source ||
                !arrivalBoundaryWorkers.Add(reader.Source.DeviceId)) continue;
            var boundaryTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            var boundary = new ProductionArrivalCapture(generation, Wire.Id());
            arrivalCaptures[reader.Source.DeviceId] = new(generation, reader.Epoch, false, until, MinimumCaptureTicks: boundaryTicks, BoundaryId: boundary.BoundaryId);
            var epoch = reader.Epoch;
            auxiliary.Add(Task.Run(async () =>
            {
                Exception? failure = null;
                var started = System.Diagnostics.Stopwatch.GetTimestamp();
                try { await DeviceCallAsync(reader.Source.DeviceId, () => source.SetArrivalAsync(boundary, stopping.Token), stopping.Token).ConfigureAwait(false); }
                catch (Exception error) { failure = error; }
                await gate.WaitAsync().ConfigureAwait(false);
                try
                {
                    if (SmCycle(position).Generation == generation && reader.Epoch == epoch && ReferenceEquals(readers[reader.Source.DeviceId], reader) &&
                        SmArrivalConfirmed(position) && SmCycle(position).RecoveryReason is null &&
                        arrivalCaptures.TryGetValue(reader.Source.DeviceId, out var pending) && pending.BoundaryId == boundary.BoundaryId)
                    {
                        arrivalCaptures[reader.Source.DeviceId] = new(generation, epoch, failure is null, arrivalConfirmations[position].ValidUntil, failure?.Message, boundaryTicks, boundary.BoundaryId);
                        smPresenceTimes.Remove(position); smPresenceCaptureTicks.Remove(position); arrivalVisualBaselines.Remove(position);
                        var cycle = SmCycle(position);
                        StoreSmCycle(cycle with { Presence = TrayPresence.Unknown, Epoch = epoch, Sequence = 0, Evidence = null },
                            failure is null ? "ArrivalCaptureReady" : "ArrivalCaptureFailed");
                    }
                    WriteRuntimeLog("小车到位", failure is null ? "相机到位边界完成" : "相机到位边界未确认", new
                    { Position = position, Generation = generation, Epoch = epoch, ElapsedMs = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds, Error = failure?.ToString() }, failure is null ? "信息" : "警告");
                }
                finally { arrivalBoundaryWorkers.Remove(reader.Source.DeviceId); gate.Release(); }
            }));
        }
    }
    bool SmCameraArrivalCurrent(string position, Observation observation) => !UsesSmArrival(position) ||
        PositionSupplyEnabled(position) && arrivalCaptures.TryGetValue(observation.DeviceId, out var capture) && capture.Ready && capture.Epoch == observation.Epoch &&
        capture.Generation == observation.ArrivalGeneration && capture.BoundaryId == observation.CaptureBoundaryId && observation.CaptureTicks >= capture.MinimumCaptureTicks && capture.Until > DateTimeOffset.UtcNow;
    SmArrivalSnapshot? SmArrivalProjection(string position)
    {
        if (Id != "sportmaster") return null;
        var cycle = SmCycle(position);
        arrivalCaptures.TryGetValue(positions[position].ReaderDeviceId, out var capture);
        return new(cycle.VehicleNo, cycle.Generation, !UsesSmArrival(position) || SmArrivalConfirmed(position), cycle.SubmittedOperation is not null,
            SmArrivalConfirmed(position) && capture is { Ready: true } && capture.Generation == cycle.Generation &&
                capture.Epoch == readers[positions[position].ReaderDeviceId].Epoch && capture.Until > DateTimeOffset.UtcNow,
            cycle.RecoveryReason ?? (CurrentRound(position) is null && cycle.TargetOperation is not null ? "原请求的远端目标责任待核对；请停用本位，维护核对后恢复。" : null), cycle.TargetOperation)
        { Configured = PositionHasRcsCode(position), CaptureFailure = capture?.Failure is not null ? "相机换车边界未确认；可核对后人工提交，或联系维护检查相机。" : null,
            SubmissionAttempted = cycle.SubmissionAttempted, RecoveryToken = cycle.RecoveryToken, PlacementClosed = UsesSmArrival(position) && cycle.AutoSuppressed && cycle.SubmittedOperation is null && CurrentRound(position) is null };
    }
    SmPositionCycle[] SmCyclesAfterOperation(ParcelOperation op, bool firstAttempt)
    {
        if (Id != "sportmaster" || op.PositionId is not { } position) return [];
        var result = new List<SmPositionCycle>();
        foreach (var target in new[] { position })
        {
            var cycle = SmCycle(target);
            if (firstAttempt && op.Wcs is { Attempts: 1, State: StageState.Dispatching } &&
                (cycle.TargetOperation is null || cycle.TargetOperation == op.Id))
                result.Add(cycle with { TargetOperation = op.Id, SubmissionAttempted = true, SubmittedOperation = UsesSmArrival(target) ? op.Id : cycle.SubmittedOperation });
            else if (cycle.TargetOperation == op.Id && op.Wcs is { } stage &&
                (stage.State is StageState.Accepted or StageState.Rejected || stage.Replies.Any(r => r.SenderDelivery == SenderDeliveryEvidence.ConnectionRefusedBeforeBody)))
                result.Add(cycle with { TargetOperation = null });
        }
        return result.ToArray();
    }
    bool SmTargetDispatchAllowed(ParcelOperation op) => Id != "sportmaster" ||
        SmTargetBlock(op.PositionId!, op.Id, IsDebugRequest(op)) is null &&
        (!UsesSmArrival(op.PositionId!) || op.ArrivalGeneration == SmCycle(op.PositionId!).Generation);
    public async Task<BusinessReceipt> ConfirmRemoteRecoveryAsync(ConfirmSmRemoteRecovery request, CancellationToken token)
    {
        var authority = Management.Require();
        if (!Guid.TryParseExact(request.CommandId, "N", out _) || request.PositionId is not ("left" or "right") ||
            string.IsNullOrWhiteSpace(request.ExternalEvidence) || request.ExternalEvidence.Length > 2048 || request.ExternalEvidence.Any(char.IsControl))
            throw new ArgumentException("请填写客户RCS原请求已结束及本位调度状态的外部核对依据。");
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            authority.ThrowIfCancellationRequested(); Management.Require();
            var command = new BusinessCommand(request.CommandId, 0, "ConfirmRemoteRecovery", request.PositionId, Wire.Encode(request));
            if (repository!.PriorCommand(command) is { } prior) return prior;
            var cycle = SmCycle(request.PositionId);
            string[] related = [request.PositionId];
            if (backupInProgress || configurationSaving || stopping.IsCancellationRequested || request.RuntimeId != runtimeId ||
                cycle.Generation != request.ExpectedGeneration || cycle.TargetOperation != request.ExpectedTargetOperation ||
                cycle.RecoveryToken != request.ExpectedRecoveryToken || PositionControl(request.PositionId).Revision != request.ExpectedControlRevision || related.Any(p =>
                PositionControl(p).Mode != StationPositionMode.Disabled || !SmMembershipConfirmed(p, false) ||
                PositionDrainRequired(p) || SmCycle(p).HeartbeatPending is not null || arrivalBoundaryWorkers.Contains(positions[p].ReaderDeviceId)))
                return new("Rejected", "先停用本位并等实际成员移除确认、调用及回执收口，再核对客户RCS。", repository.Revision);
            var next = related.Select(p => SmCycle(p) with { RecoveryCause = SmRemoteRecoveryCause.None, TargetOperation = null, UncertainMembership = null, RecoveryToken = Wire.Id() }).ToArray();
            var receipt = new BusinessReceipt("Accepted", "已记录外部核对；仍须启用并等可信新车到位。同车已提交事实保留。", repository.Revision + 1);
            repository.Commit(null, "RemoteTargetRecoveryConfirmed", Wire.Encode(new { request, Actor = ManagementEvidence }), command: command, commandResult: receipt, cycles: next);
            foreach (var confirmed in next) smCycles[confirmed.PositionId] = confirmed;
            return receipt;
        }
        catch (Exception error) when (IsStorageError(error)) { StorageFailed(error, "RemoteTargetRecovery"); throw; }
        finally { gate.Release(); }
    }
}

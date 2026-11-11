using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime
{
    readonly Dictionary<string, SmParcelRound> smRounds = [];
    readonly Dictionary<string, string> smCurrentRounds = [];
    readonly Dictionary<string, SmPositionCycle> smCycles = [];
    readonly Dictionary<string, CameraOriginalImage> smRoundPictures = [];
    readonly HashSet<string> smExecutions = [];
    // Rounds can end before HTTP finishes. Bound retained tasks per position so
    // a stalled peer cannot grow memory indefinitely or consume the other lane.
    const int MaximumSmExecutionsPerPosition = 128;
    internal int SmExecutionCapacityForVerification { get; set; } = MaximumSmExecutionsPerPosition;
    readonly Dictionary<string, (long At, int Count)> smSuppressed = [];
    internal Func<string, Task>? BeforeSmAttemptForVerification;
    internal Func<string, Task>? BeforeSmRoundImageForVerification { get; set; }
    SmParcelRound? CurrentRound(string position) => smCurrentRounds.TryGetValue(position, out var id) ? smRounds.GetValueOrDefault(id) : null;
    SmPositionCycle SmCycle(string position) => smCycles.GetValueOrDefault(position) ?? new(position, false, TrayPresence.Unknown, null, 0, Wire.Id(), null);
    bool SmRoundOwns(ParcelOperation op) => op.RoundId is null || CurrentRound(op.PositionId!)?.Id == op.RoundId;
    readonly Dictionary<string, DateTimeOffset> smPresenceTimes = [];
    bool SmVisualReliable(string position) => SmCycle(position) is { Presence: TrayPresence.Present } cycle &&
        readers.TryGetValue(positions[position].ReaderDeviceId, out var reader) && reader.Epoch == cycle.Epoch &&
        reader.Health?.IsValid(DateTimeOffset.UtcNow) == true &&
        smPresenceTimes.TryGetValue(position, out var at) && DateTimeOffset.UtcNow - at < TimeSpan.FromMilliseconds(1500);
    void StoreSmRound(SmParcelRound round, string name, SmPositionCycle? cycle = null, ParcelOperation? operation = null, BusinessCommand? command = null)
    {
        repository!.Commit(operation, name, Wire.Encode(new
        {
            RoundId = round.Id,
            round.PositionId,
            round.Version,
            round.OperationId,
            round.State,
            round.EndReason,
            round.ReadOutcome,
            OperationIds = round.OperationId is { } id ? new[] { id } : []
        }),
            round: round, cycle: cycle, command: command);
        ApplySmRound(round);
        if (cycle is not null) smCycles[cycle.PositionId] = cycle;
        if (operation is not null) operations[operation.Id] = operation;
        WriteRuntimeLog("包裹轮次", name, new
        {
            RoundId = round.Id,
            round.PositionId,
            round.State,
            round.EndReason,
            round.ReadOutcome,
            round.OperationId,
            round.RequestId,
            round.StartedAt,
            round.EndedAt,
            RoundElapsedMs = round.ClockRuntime == runtimeId ? StationTime.GetElapsedTime(round.StartedTicks).TotalMilliseconds : (double?)null
        });
    }
    void ApplySmRound(SmParcelRound round)
    {
        smRounds[round.Id] = round;
        if (round.EndedAt is null) smCurrentRounds[round.PositionId] = round.Id;
        else if (smCurrentRounds.GetValueOrDefault(round.PositionId) == round.Id) smCurrentRounds.Remove(round.PositionId);
    }
    SmParcelRound StartSmRound(string position, bool manual)
    {
        var cycle = SmCycle(position) with { AutoSuppressed = true };
        var round = new SmParcelRound(Wire.Id(), position, 1, runtimeId, StationTime.GetUtcNow(), StationTime.GetTimestamp(),
            Effective.EffectiveReadTimeoutMs, configuration!.Current.Revision, Effective.BarcodeFilter, SmRoundState.Reading, [])
        { Manual = manual, PresenceEvidence = cycle.Evidence };
        StoreSmRound(round, "RoundStarted", cycle); return round;
    }
    void EndSmRound(SmParcelRound round, string reason, bool physicallyRemoved = false, BusinessCommand? command = null, DateTimeOffset? removalAt = null)
    {
        if (CurrentRound(round.PositionId)?.Id != round.Id) return;
        var cycle = SmCycle(round.PositionId);
        if (physicallyRemoved)
        {
            cycle = cycle with
            {
                AutoSuppressed = false,
                Generation = Wire.Id(),
                Presence = TrayPresence.Absent,
                ClearedAt = removalAt ?? StationTime.GetUtcNow()
            };
            blockedInputs.TryRemove(round.PositionId, out _); blockedInputPictures.Remove(round.PositionId);
        }
        var ended = round with { Version = round.Version + 1, State = SmRoundState.Ended, EndedAt = StationTime.GetUtcNow(), EndReason = reason };
        ParcelOperation? operation = null;
        if (round.OperationId is { } id && operations.TryGetValue(id, out var op))
        {
            // No cancellation is invented after T2. A never-attempted intent can
            // be closed locally without modifying its original frozen identity.
            if (op.Wcs is null || op.Wcs is { Attempts: 0, State: StageState.Prepared })
                operation = op with
                {
                    State = ParcelState.Abandoned,
                    Reason = "Round ended before first RCS attempt; no dispatch",
                    Wcs = op.Wcs is { } wcs ? wcs with { State = StageState.Cancelled } : null
                };
        }
        StoreSmRound(ended, "RoundEnded", cycle, operation, command);
        if (round.OperationId is { } released) releasedPlacements.Add(released);
        if (physicallyRemoved) confirmedEmptyTrays[round.PositionId] = cycle.ClearedAt!.Value;
        smRoundPictures.Remove(round.Id);
    }
    void MaintainSmRounds()
    {
        if (Id != "sportmaster") return;
        foreach (var id in smCurrentRounds.Values.ToArray())
        {
            var round = CheckSmReadDeadline(smRounds[id]);
            if ((round.Manual || !SmVisualReliable(round.PositionId)) && round.SuccessTicks is { } accepted && round.ClockRuntime == runtimeId &&
                StationTime.GetElapsedTime(accepted).TotalMilliseconds >= 3000)
                EndSmRound(round, "SuccessDisplayElapsed");
        }
        foreach (var round in smRounds.Values.Where(r => r.EndedAt is not null &&
            (r.OperationId is null || !smExecutions.Contains(r.OperationId))).ToArray()) smRounds.Remove(round.Id);
    }
    SmParcelRound CheckSmReadDeadline(SmParcelRound round)
    {
        if (round.OperationId is null && !round.AutoExpired &&
            (round.ClockRuntime != runtimeId || StationTime.GetElapsedTime(round.StartedTicks).TotalMilliseconds >= round.ReadTimeoutMs))
        {
            round = round with { Version = round.Version + 1, State = SmRoundState.ReadException, AutoExpired = true, ReadOutcome = "ReadWindowExpired" };
            StoreSmRound(round, "ReadWindowExpired");
        }
        return round;
    }
    void RecordSmReadObservation(SmParcelRound round, Observation observation, ObservationSession reader, BarcodeFilterResult selection, string[] formal)
    {
        var outcome = round.AutoExpired ? "ReadWindowExpired" : selection.Outcome;
        if (round.Observation is not null && round.ReadOutcome == outcome && round.FormalCodes.SequenceEqual(formal)) return;
        StoreSmRound(round with { Version = round.Version + 1, FormalCodes = formal, Observation = observation, ReadOutcome = outcome }, "RoundReadObserved");
        RecordInput(Input(round.PositionId, observation, ParcelInputOutcome.Rejected,
            round.AutoExpired ? "ReadWindowExpired; 可原位PDA补录，尚未发送请求" : selection.Outcome, selection: selection), "RoundReadFeedback");
        if (RetainInputPicture(observation, reader) is { } picture) smRoundPictures[round.Id] = picture;
        ArchiveSmRoundFrame(smRounds[round.Id], observation, reader);
    }
    void RestoreSmRounds()
    {
        if (Id != "sportmaster") return;
        while (repository!.PendingRoundImages() is { Length: > 0 } pending)
            foreach (var round in pending)
                StoreSmRound(round with
                {
                    Version = round.Version + 1,
                    OriginalFrame = round.OriginalFrame! with
                    { State = "Interrupted", Failure = "Previous image write interrupted; original files retained; no replay" }
                }, "RoundImageInterrupted");
        foreach (var position in positions.Keys)
            if (repository!.Cycle(position) is { } cycle)
                smCycles[position] = cycle with { Presence = TrayPresence.Unknown };
        foreach (var round in repository!.Rounds(currentOnly: true))
        {
            ApplySmRound(round);
            var operation = round.OperationId is { } id ? operations.GetValueOrDefault(id) ?? repository.ReadOperation(id) : null;
            if (operation?.State != ParcelState.SlotResolved && (round.RequireRemoval || operation is { Wcs.Attempts: > 0 } &&
                (round.Manual || SmCycle(round.PositionId).Presence == TrayPresence.Unknown) && operation.State != ParcelState.SlotResolved)
                )
                StoreSmRound(round with
                {
                    Version = round.Version + 1,
                    State = SmRoundState.AwaitingRemoval,
                    RequireRemoval = true,
                    AutoExpired = true
                }, "RoundRemovalRestored");
            else EndSmRound(round, "InterruptedOnRestart");
        }
    }
    SmParcelRound? SmRoundAfterOperation(ParcelOperation op)
    {
        if (op.RoundId is not { } id) return null;
        var round = smRounds.GetValueOrDefault(id) ?? repository!.Round(id) ?? throw new InvalidDataException("Operation round missing");
        var next = round with
        {
            Version = round.Version + 1,
            OperationId = op.Id,
            AttemptStarted = round.AttemptStarted || op.Wcs?.Attempts > 0,
            RequestId = op.Wcs?.Request.Id ?? round.RequestId,
            ProtocolRequestId = op.Wcs?.Request.IntegrationRequest?.RequestId ?? round.ProtocolRequestId,
            RcsOutcome = op.Wcs?.State.ToString() ?? round.RcsOutcome
        };
        if (round.EndedAt is not null) return next;
        if (op.State == ParcelState.SlotResolved && !string.IsNullOrWhiteSpace(op.Slot))
            return next with
            {
                State = SmRoundState.Result,
                RequireRemoval = false,
                SuccessAt = round.SuccessAt ?? StationTime.GetUtcNow(),
                SuccessTicks = round.SuccessTicks ?? StationTime.GetTimestamp()
            };
        if (op.Wcs?.State is StageState.Rejected or StageState.Unknown or StageState.Conflict || op.State is ParcelState.Blocked or ParcelState.ImageRejected)
        {
            var confirm = round.Manual || !SmVisualReliable(round.PositionId);
            return next with { State = confirm ? SmRoundState.AwaitingRemoval : SmRoundState.Result, RequireRemoval = confirm };
        }
        return next with { State = SmRoundState.Submitted };
    }
    void ObserveSmPresence(string position, Observation observation)
    {
        var fact = observation.TrayPresence!;
        if (!Enum.IsDefined(fact.State) || string.IsNullOrWhiteSpace(fact.Evidence) || fact.Evidence.Length > 1024 || fact.Evidence.Any(char.IsControl))
            throw new InvalidDataException("Physical evidence is incomplete");
        smPresenceTimes[position] = observation.ObservedAt;
        var cycle = SmCycle(position) with { Presence = fact.State, Epoch = observation.Epoch, Sequence = observation.Sequence, Evidence = fact.Evidence };
        var round = CurrentRound(position);
        if (fact.State == TrayPresence.Absent && round is not { RequireRemoval: true })
        {
            blockedInputs.TryRemove(position, out _); blockedInputPictures.Remove(position);
            if (round is not null) { smCycles[position] = cycle; EndSmRound(round, "TrustedRemoval", physicallyRemoved: true, removalAt: observation.ObservedAt); }
            else
            {
                cycle = cycle with { AutoSuppressed = false, Generation = Wire.Id(), ClearedAt = observation.ObservedAt };
                repository!.Commit(null, "TrayRemovalObserved", Wire.Encode(cycle), cycle: cycle); smCycles[position] = cycle;
                confirmedEmptyTrays[position] = observation.ObservedAt;
            }
            return;
        }
        repository!.Commit(null, "TrayPresenceObserved", Wire.Encode(cycle), cycle: cycle); smCycles[position] = cycle;
        if (fact.State == TrayPresence.Present && round is null && !cycle.AutoSuppressed && CameraAdmissionBlock(position) is null)
            _ = StartSmRound(position, false);
    }
    async Task ProcessSmObservationAsync(string position, Observation observation, OperatorIdentity? actor, ObservationSession reader, bool manual)
    {
        var beforeRound = !manual && (SmCycle(position).ClearedAt is { } cleared && observation.ObservedAt <= cleared ||
            CurrentRound(position) is { } target && observation.ObservedAt < target.StartedAt);
        if (observation.TrayPresence is not null && !manual)
        {
            // A physical frame captured before this round (or left in delivery
            // too long) cannot remove a later deliberately submitted parcel.
            if (beforeRound || DateTimeOffset.UtcNow - observation.ObservedAt > TimeSpan.FromMilliseconds(definition.QueuedObservationTimeoutMs(Effective)))
            {
                RecordInput(Input(position, observation, ParcelInputOutcome.Rejected,
                    "PhysicalEvidenceExpired; 旧物理观察只留证，不改变当前轮次。"), "PhysicalEvidenceIgnored");
                return;
            }
            if (reader.Health?.IsValid(DateTimeOffset.UtcNow) == true && reader.Epoch == observation.Epoch)
                ObserveSmPresence(position, observation);
            return;
        }
        MaintainSmRounds();
        var round = CurrentRound(position);
        var policy = round is not null ? round.Filter : Effective.BarcodeFilter;
        var selection = BarcodeFiltering.Select(policy ?? new BarcodeFilterPolicy(""), observation.Codes, observation.CandidateIssue);
        var formal = selection.Candidates?.Where(c => c.Outcome == "Eligible").Select(c => observation.Codes[c.Index]).ToArray()
            ?? (selection.SelectedCode is { } only ? new[] { only } : []);
        if (selection.Outcome is "IncompleteCandidates" or "FilterTimeout" or "InvalidPattern") formal = [];
        var observationHash = Wire.Hash(observation.DeviceId + ":" + observation.Epoch + ":" + observation.SourceEventId);
        if (repository!.RetiredIdentity(observation.AdmissionId, observationHash) is { } retired)
        {
            if (retired.Admission != observation.AdmissionId || retired.Observation != observationHash || retired.Position != position)
                RecordRetiredConflict(retired.Id, [retired.Position, position], "Expired identity reused with changed admission/observation/position; no new request");
            RecordInput(Input(position, observation, ParcelInputOutcome.Blocked, "ExpiredIdentity: original=" + retired.Id, selection: selection), "RetiredIdentityObserved");
            return;
        }
        var priors = repository.FindIdentities(observation.AdmissionId, observationHash);
        if (priors.Length != 0)
        {
            // Repeated source identity remains bound to its frozen selection policy.
            var originalSelection = BarcodeFiltering.Select(priors[0].FrozenConfiguration.BarcodeFilter ?? new BarcodeFilterPolicy(""), observation.Codes, observation.CandidateIssue);
            var contradictions = priors.Where(p => p.AdmissionId != observation.AdmissionId || p.ParcelCode != originalSelection.SelectedCode ||
                p.ObservationId != observationHash || (p.PositionId ?? "main") != position).ToArray();
            foreach (var prior in contradictions) RecordIdentityConflict(prior, position, "Observation/admission identity reused with changed content/position; source=" + observation.SourceEventId);
            RecordInput(Input(position, observation, contradictions.Length == 0 ? ParcelInputOutcome.Duplicate : ParcelInputOutcome.Blocked,
                contradictions.Length == 0 ? "DuplicateIdentity; original retained" : "IdentityConflict; original retained", priors[0].Id, originalSelection), "ObservationIdentityObserved");
            return;
        }
        // Keep original identity/conflict checks above this boundary. A unique
        // acquisition can still finish decoding after the parcel was removed.
        if (beforeRound)
        {
            RecordInput(Input(position, observation, ParcelInputOutcome.Rejected,
                "BeforeRoundBoundary; 取下或当前轮次边界前的相机观察只留证，不建轮或提交。", selection: selection), "ObservationBeforeRoundBoundary");
            return;
        }
        // Source freshness is an input trust guard, not a round timeout. A stale
        // handoff cannot manufacture a fresh parcel or renew an existing window.
        if (!manual && DateTimeOffset.UtcNow - observation.ObservedAt > TimeSpan.FromMilliseconds(definition.QueuedObservationTimeoutMs(Effective)))
        {
            RecordInput(Input(position, observation, ParcelInputOutcome.Rejected,
                "ObservationExpired; 过期相机观察已忽略，未发送请求；当前轮次自动识读期限不变。", selection: selection), "ObservationRejected");
            if (round is not null) ArchiveSmRoundFrame(round, observation, reader);
            return;
        }
        if (round?.OperationId is not null || !manual && round is null && SmCycle(position).AutoSuppressed)
        {
            // Codes (including a changed code) are observations, never a new physical boundary.
            RecordFlowPerformance(position, "AutoRepeatSuppressed", observation.Id,
                new(observation.DeviceId, observation.Epoch, observation.SourceEventId, observation.AdmissionId, observation.ObservedAt),
                round?.OperationId, decodedTicks: observation.DecodedTicks, roundId: round?.Id);
            smSuppressed.TryGetValue(position, out var prior);
            var count = prior.Count + 1;
            if (!smSuppressed.ContainsKey(position) || StationTime.GetElapsedTime(prior.At).TotalMilliseconds >= 5000)
            {
                WriteRuntimeLog("包裹轮次", "AutoRepeatSuppressed", new
                {
                    PositionId = position,
                    RoundId = round?.Id,
                    Generation = SmCycle(position).Generation,
                    Observations = count,
                    Reason = "Automatic opportunity already consumed; no new request"
                });
                smSuppressed[position] = (StationTime.GetTimestamp(), 0);
            }
            else smSuppressed[position] = (prior.At, count);
            return;
        }
        if (round is null)
        {
            if (!manual && formal.Length == 0) return;
            var block = manual ? ManualInputReasons(position) : CameraAdmissionBlock(position) is { } reason ? new[] { reason.ToString() } : [];
            if (block.Length != 0) { RecordInput(Input(position, observation, ParcelInputOutcome.Blocked, string.Join("; ", block), selection: selection), "ObservationBlocked"); return; }
            round = StartSmRound(position, manual);
        }
        if (observation.InspectionSessionId is not null || observation.Kind != "Read" || !ExtensionSchema.IsId(observation.AdmissionId)) return;
        var decision = round with
        {
            Version = round.Version + 1,
            FormalCodes = formal,
            Observation = observation,
            ReadOutcome = round.AutoExpired ? "ReadWindowExpired" : selection.Outcome
        };
        if (selection.SelectedCode is null || !manual && round.AutoExpired)
        {
            RecordSmReadObservation(round, observation, reader, selection, formal);
            return;
        }
        if (!manual && CameraAdmissionBlock(position) is { } blocked)
        { RecordInput(Input(position, observation, ParcelInputOutcome.Blocked, blocked.ToString(), selection: selection), "ObservationBlocked"); return; }
        if (smExecutions.Count(id => operations[id].PositionId == position) >= SmExecutionCapacityForVerification)
        {
            StoreSmRound(decision with { State = SmRoundState.ReadException, ReadOutcome = "ExecutionCapacityBlocked" }, "ExecutionCapacityBlocked");
            RecordInput(Input(position, observation, ParcelInputOutcome.Blocked, "本位实际在途资源已满；本轮尚未取得提交权，未发送请求。", selection: selection), "ExecutionCapacityBlocked");
            return;
        }
        var weight = WeightRules.Configured(Effective.Weight);
        var op = new ParcelOperation(Wire.Id(), observation.AdmissionId, selection.SelectedCode, observationHash, observation.ObservedAt,
            Wire.Hash(Wire.Encode(Effective)), Wire.Decode<ParcelConfiguration>(Wire.Encode(Effective)), new(ports!.ImplementationHashes),
            Effective.Weight, weight, ParcelState.Prepared, null, null, null, null, Operator: actor,
            Acquisition: manual ? null : new(observation.DeviceId, observation.Epoch, observation.SourceEventId, observation.AdmissionId, observation.ObservedAt),
            PositionId: position, ReaderBinding: ReaderBinding(position), BarcodeSelection: selection)
        { OperatorInput = observation.OperatorInput, RoundId = round.Id, IngressId = activeIngress.Value };
        var claimed = decision with
        {
            OperationId = op.Id,
            CommandId = observation.OperatorInput?.CommandId,
            Manual = manual,
            SelectedCode = selection.SelectedCode,
            State = SmRoundState.Submitted
        };
        var admitted = Input(position, observation, ParcelInputOutcome.Admitted, "DispatchClaimed; 本轮已持久接纳，外部结果待快照确认", op.Id, selection);
        IOriginalFrameLease? frame = null;
        try
        {
            if (!manual && definition.ArchiveOriginalFrames && reader.Source is IOriginalFrameSource originals)
            {
                frame = originals.RetainOriginalFrame(op.Acquisition!);
                if (frame is not null) op = op with
                {
                    OriginalFrame = new("Pending", op.Acquisition!, frame.Width, frame.Height,
                    "images/original-frames/" + op.Id + ".png", StationTime.GetUtcNow())
                };
            }
            // Filtering, identity checks and frame retention consume the same window.
            // The gate serializes claims, but cannot prevent time passing before T1.
            if (!manual && CheckSmReadDeadline(round) is { AutoExpired: true } expired)
            {
                frame?.Dispose(); frame = null;
                RecordSmReadObservation(expired, observation, reader, selection, formal);
                return;
            }
            repository.Commit(op, "DispatchClaimed", Wire.Encode(new { RoundId = round.Id, OperationId = op.Id }), round: claimed, input: admitted, ingressIdentity: op.IngressId);
        }
        catch { frame?.Dispose(); throw; }
        operations[op.Id] = op; admissionPositions.TryAdd(op.AdmissionId, position); ApplySmRound(claimed); PublishInput(admitted);
        if (frame is not null) ScheduleOriginalFrame(op, frame);
        if (!manual && RetainInputPicture(observation, reader) is { } original) smRoundPictures[round.Id] = original;
        if (op.FrozenConfiguration.Images is not null && reader.Source is IReaderImageCapture capture && op.Acquisition is { } identity)
            capture.RetainAcquisition(identity, "operation:" + op.Id);
        smExecutions.Add(op.Id); activeStages.Add(op.Id + ":RoundExecution");
        auxiliary.Add(ExecuteSmRoundAsync(op.Id));
        await Task.CompletedTask;
    }
    async Task ExecuteSmRoundAsync(string operation)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var op = operations[operation];
            if (SmRoundOwns(op) && !stopping.IsCancellationRequested) await ProcessObservedParcelAsync(op).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            failure = "SM round execution failed: " + error; FenceIngress();
            if (IsStorageError(error)) StorageFailed(error, "SmRoundExecution");
            throw;
        }
        finally { activeStages.Remove(operation + ":RoundExecution"); smExecutions.Remove(operation); gate.Release(); }
    }
    public Task<SmRoundHistoryPage> RoundHistoryAsync(string? position = null, int limit = 100, string? search = null, DateTimeOffset? from = null, DateTimeOffset? to = null) => OwnedRead(async token =>
    {
        if (position is not null && position is not ("left" or "right") || search?.Length > 512 || from >= to || limit is < 1 or > 200)
            throw new ArgumentException("请提供有效投递位、查询文本和时间范围。");
        string database;
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (Id != "sportmaster" || stopped) throw new InvalidOperationException("当前轮次记录不可用。");
            database = Path.Combine(directory!, Id + ".db");
        }
        finally { gate.Release(); }
        return new SmRoundHistoryPage(ParcelRepository.ReadRoundHistory(database, position, limit, search, from, to, token));
    });
    void ArchiveSmRoundFrame(SmParcelRound round, Observation observation, ObservationSession reader)
    {
        if (round.OriginalFrame is not null || observation.OperatorInput is not null || reader.Source is not IOriginalFrameSource source) return;
        var identity = new AcquisitionIdentity(observation.DeviceId, observation.Epoch, observation.SourceEventId, observation.AdmissionId, observation.ObservedAt);
        IOriginalFrameLease pixels;
        try { pixels = source.RetainOriginalFrame(identity); }
        catch (Exception error) when (error is IOException or InvalidOperationException)
        { Diagnostic("原图留档", "本轮原图不可用", "警告", "Round=" + round.Id + "; " + error); return; }
        var metadata = new OriginalFrameArchive("Pending", identity, pixels.Width, pixels.Height,
            "images/original-frames/round." + round.Id + ".png", StationTime.GetUtcNow());
        try { StoreSmRound(round with { Version = round.Version + 1, OriginalFrame = metadata }, "RoundImagePrepared"); }
        catch { pixels.Dispose(); throw; }
        var key = "round:" + round.Id + ":OriginalFrame";
        activeStages.Add(key);
        auxiliary.Add(Task.Run(async () =>
        {
            OriginalFrameArchive result;
            await originalFrameWriters.WaitAsync().ConfigureAwait(false);
            try
            {
                if (BeforeSmRoundImageForVerification is { } before) await before(round.Id).ConfigureAwait(false);
                using (pixels) result = SaveOriginalFrameFile(directory!, metadata, pixels, Retention.MinimumFreeBytes);
            }
            catch (Exception error) { pixels.Dispose(); result = metadata with { State = "Failed", Failure = error.ToString() }; }
            finally { originalFrameWriters.Release(); }
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                var current = smRounds.GetValueOrDefault(round.Id) ?? repository!.Round(round.Id)!;
                StoreSmRound(current with { Version = current.Version + 1, OriginalFrame = result }, "RoundImage" + result.State);
            }
            finally { activeStages.Remove(key); gate.Release(); }
        }));
    }
}

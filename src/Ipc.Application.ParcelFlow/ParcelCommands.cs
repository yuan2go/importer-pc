using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime
{
    readonly SemaphoreSlim shutdown = new(1, 1);
    public async Task<ParcelSnapshot> SnapshotAsync()
    {
        string encoded;
        while (true)
        {
            await RefreshArchiveCountAsync().ConfigureAwait(false);
            await gate.WaitAsync().ConfigureAwait(false);
            if (archiveCount?.Revision == repository?.ArchiveRevision) break;
            gate.Release(); // Archive changed while the independent count was read.
        }
        try
        {
            Boundary?.Invoke("SnapshotGateAcquired");
            if (configuration is null || repository is null) throw new InvalidOperationException("Application has not started");
            ObjectDisposedException.ThrowIf(disposed, this);
            // Single consistent read per projection; this scope never releases the business gate.
            repository.BeginReceiptProjection();
            projectedCapacity = operations.Values.Count(x => !ArchiveEligible(x));
            RefreshIntegrationBudget();
            List<string> reasons = []; List<string> auxiliaryReasons = []; var now = DateTimeOffset.UtcNow;
            if (CameraBusinessFenced) reasons.Add(singleReadInspection || inspectionFence?.SingleReadRounds == true
                ? "扫码调试正在按轮运行；新相机输入不产生RCS业务请求，原请求及Unknown责任保持。"
                : "实机调试隔离：新输入不产生RCS业务请求；测试件移除并在调试页显式确认前保持阻断，软件隔离不是安全急停。");
            if (ParcelConnectionSetup.BlocksWork(Effective) && Effective.DeferredConnections is { Length: > 0 } deferred)
                reasons.Add("连接待配置：" + string.Join("、", deferred.Select(id => Effective.Wcs?.InstanceId == id ? "RCS" : ParcelBindings.Positions(Effective).Single(p => p.ReaderDeviceId == id).Id == "left" ? "左相机" : "右相机")) + "。可进入参数设置；完成连接后保存并受控重启，当前不接收新包裹。");
            if (factory.AdmissionFailure(Effective) is { } qualificationFailure) reasons.Add(qualificationFailure);
            if (failure is not null) reasons.Add(failure);
            if (backupInProgress) reasons.Add("Consistent backup in progress; original custody and configuration mutations are fenced");
            foreach (var position in interruptedPositions.Keys) reasons.Add("IngressInterrupted:" + position + "; pause, enter management mode and inspect original input responsibilities");
            if (journal is not null) auxiliaryReasons.Add("Diagnostics:" + Wire.Encode(journal.Status));
            if (integrationBudget is not null) auxiliaryReasons.Add("IntegrationIdBudget:" + Wire.Encode(integrationBudget));
            if (storageCapacityWarning is not null) auxiliaryReasons.Add(storageCapacityWarning);
            if (storageCapacityEstimate is not null) auxiliaryReasons.Add(storageCapacityEstimate);
            if (Volatile.Read(ref walObservation) is { } wal) auxiliaryReasons.Add(wal);
            if (retentionWarning is not null) auxiliaryReasons.Add(retentionWarning);
            auxiliaryReasons.AddRange(operations.Values.Where(o => o.OriginalFrame?.State is "Failed" or "Interrupted")
                .Select(o => "OriginalFrame:" + o.Id + "; " + o.OriginalFrame!.State + "; " + o.OriginalFrame.Failure));
            auxiliaryReasons.Add("Workers:" + Wire.Encode(WorkerProgress()));
            if (configuration.Current.Confirmation?.Required == true) reasons.Add("ConfigurationConfirmationRequired: current resources must start successfully before admission");
            if (!CapacityAvailable) reasons.Add(storageCapacityFailure ?? "Unresolved operation capacity exhausted; original responsibilities cannot be archived");
            foreach (var conflict in identityConflicts.Values) reasons.Add("IdentityConflict:" + conflict.Id + "; positions=" + string.Join(",", conflict.Positions));
            if (overload != 0) reasons.Add("Overload: rejected callback; inspect parcel and acknowledge while paused");
            if (!SessionValid) reasons.Add("Operator authentication required for new admission");
            if (repository.Paused) reasons.Add("Software admission paused; this is not an emergency stop");
            if (!RestoreConfirmed) reasons.Add("RestoreHandoffRequired: keep isolated; verify same-machine source retirement and authenticate maintenance; cross-machine evidence still required");
            reasons.AddRange(maintainedResources.Keys.Order().Select(resource => "MaintenanceInProgress:" + resource));
            if (readers.Values.Any(r => !ParcelConnectionSetup.IsDeferred(Effective, r.Source.DeviceId) && r.Health?.IsValid(DateTimeOffset.UtcNow) != true)) (Id == "sportmaster" ? auxiliaryReasons : reasons).Add("Reader confirmation missing/expired");
            if (!ImageCapacityAvailable()) reasons.Add("Temporary image capacity/backlog exhausted; existing originals retained");
            if (operations.Values.Any(x => NeedsReconciliation(x) && !ReleasedForNextAdmission(x))) reasons.Add("Original stage outcome or receipt responsibility is unresolved");
            var retainedTimeouts = operations.Values.Count(ReleasedForNextAdmission);
            if (retainedTimeouts > 0) auxiliaryReasons.Add("RetainedRequestTimeouts:" + retainedTimeouts + "; outcomes unknown; new scans may create separate requests; originals are never replayed");
            foreach (var op in operations.Values.Where(x => !ReleasedForNextAdmission(x) && (x.State == ParcelState.WeightTimedOut || WeightExpired(x) || x.Weight is not null && !WeightReady(x) && !MainTerminated(x))))
                reasons.Add("Measurement missing/expired for admission " + op.AdmissionId);
            List<CapabilityHealth> health = [.. capabilityHealth.Values.Select(h =>
                h with { Ready = h.Ready && h.ConfirmedAt <= now && h.ValidUntil > now && h.ValidUntil > h.ConfirmedAt })];
            foreach (var reader in readers.Values.Select(r => r.Health).OfType<DeviceState>()) health.Add(new(reader.DeviceId, reader.IsValid(DateTimeOffset.UtcNow), reader.ConfirmedAt ?? reader.UpdatedAt, reader.ValidUntil ?? reader.UpdatedAt, reader.ValidityReason(DateTimeOffset.UtcNow) ?? reader.Source));
            foreach (var h in health.Where(x => !x.Ready && !ParcelConnectionSetup.IsDeferred(Effective, x.InstanceId)))
                (Id == "sportmaster" && readers.ContainsKey(h.InstanceId) || Effective.Images?.Strategy == UploadStrategy.Attached && h.InstanceId == Effective.Images.Uploader?.InstanceId ? auxiliaryReasons : reasons).Add("Capability unavailable: " + h.InstanceId);
            auxiliaryReasons.AddRange(repository.PendingReceipts().Select(x=>x.Operation+": original receipt custody "+x.Id+"; stage="+x.Stage+"; raw="+(x.Reply is null?"missing":"durable pending T3")+"; outcome="+(repository.HasReceiptOutcomeBasis(x.Id)?"known by separate Query":"unresolved")));
            if (repository.ArchiveCleanupFailure is { } cleanup) auxiliaryReasons.Add(cleanup);
            auxiliaryReasons.AddRange(repository.PendingCommands().Select(id => "CommandPending:" + id + "; inspect CommandAdmitted audit and original facts; this ID is never replayed"));
            if (Volatile.Read(ref lastExportFailure) is { } exportFailure) auxiliaryReasons.Add("HistoryExport:" + exportFailure);
            if ((parcelPictureFailure ?? parcelPictures?.Fault) is { } pictureFailure) auxiliaryReasons.Add("ParcelPictures:" + pictureFailure);
            if (backupScheduleState is { } scheduled) auxiliaryReasons.Add("BackupSchedule:" + scheduled.Outcome + "; next=" + scheduled.NextDue.ToString("O") + "; checked=" + scheduled.At.ToString("O"));
            auxiliaryReasons.AddRange(operations.Values.Where(x => x.FrozenConfiguration.Images?.Strategy == UploadStrategy.Attached && x.Upload?.Failure is not null).Select(x => x.Id + ": upload " + x.Upload!.Failure));
            var projection = new ParcelSnapshot(runtimeId, Id, repository.Revision, stopped ? "Stopped" : reasons.Count == 0 ? "Ready" : CanAdmit() ? "PartiallyReady" : "Blocked", repository.Paused,
                reasons.ToArray(), operations.Values.Concat(positions.Keys.Select(p => repository.Latest(p)).OfType<ParcelOperation>()).DistinctBy(x => x.Id).OrderByDescending(x => x.ObservedAt).ToArray(), health.ToArray(), configuration.Current, currentOperator, Availability(), PositionSnapshots(), readers.Values.Select(r => r.Health).OfType<DeviceState>().ToArray(), Capacity(), IdentityConflicts(), auxiliaryReasons.ToArray(), latestInputs.Values.ToArray(), BackupStatus(), configuration.Current.Pending is not null);
            encoded = EncodeSnapshot(projection);
        }
        finally
        {
            projectedCapacity = null;
            try { repository?.EndReceiptProjection(); Boundary?.Invoke("SnapshotGateReleasing"); }
            finally { gate.Release(); }
        }
        // Deserialize the detached UI copy after releasing the business gate.
        return Wire.Decode<ParcelSnapshot>(encoded);
    }
    public async Task<BusinessProjection> QueryAsync()
    {
        var s = await SnapshotAsync().ConfigureAwait(false);
        return new(s.RuntimeId, s.Revision, s.State, new() { ["Application"] = Id, ["Operations"] = s.Operations.Length.ToString(),
            ["Unresolved"] = s.Operations.Count(NeedsReconciliation).ToString(), ["Reasons"] = string.Join("; ", s.BlockingReasons) });
    }
    public async Task<BusinessReceipt> CommandAsync(BusinessCommand command)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (stopped || repository is null) return new("NotReady", "Application stopped", repository?.Revision ?? 0);
            if (!Guid.TryParseExact(command.Id, "N", out _)) return new("Rejected", "Stable command identity is required", repository.Revision);
            if (repository.PriorCommand(command) is { } prior) return prior;
            if (backupInProgress && command.Kind is not ("RuntimeProgress" or "InspectRestore" or "ReceiptResponsibilities" or "IngressResponsibilities"))
                return new("Rejected", "Consistent backup is in progress; original responsibility and configuration remain fenced", repository.Revision);
            if (command.ExpectedRevision != repository.Revision) return new("Conflict", "Refresh the current application projection", repository.Revision);
            switch (command.Kind)
            {
                case "RuntimeProgress": return new("RuntimeProgress", Wire.Encode(RuntimeProgress()), repository.Revision);
                case "IngressResponsibilities" when MaintenanceAuthorized: return new("IngressResponsibilities", Wire.Encode(repository.IngressResponsibilities()), repository.Revision);
                case "InspectInterruptedIngress": return InspectInterruptedIngress(command);
                case "ReceiptResponsibilities" when command.Owner is not null:
                    return new("ReceiptResponsibilities", Wire.Encode(repository.ReceiptResponsibilities(command.Owner)), repository.Revision);
                case "ReconcileReceipt": return await ReconcileReceiptAsync(command).ConfigureAwait(false);
                case "Pause": repository.Commit(null, "AdmissionPaused", "Software pause only", paused: true, command: command); resumePriorPause = false; break;
                case "Resume" when CanResume():
                    repository.Commit(null, "AdmissionResumed", "Other stage guards remain independent", paused: false, command: command); break;
                case "AcknowledgeOverload" when repository.Paused && overload != 0 && Volatile.Read(ref pendingOverloadWrites) == 0 && positionQueues.Values.All(q => q.Reader.Count == 0):
                    var originalOverload = OverloadIdentity();
                    repository.Commit(null, "OverloadAcknowledged", "Paused operator inspection acknowledged; original=" + originalOverload, command: command, acknowledgedOverload: originalOverload);
                    Boundary?.Invoke("AfterOverloadAcknowledged"); await ExternalAsync(async () => { await RemoveAcknowledgedOverload(originalOverload).ConfigureAwait(false); return true; }).ConfigureAwait(false); break;
                case "AcknowledgeIdentityConflict" when CanConfigure && command.Owner is not null && identityConflicts.ContainsKey(command.Owner) && !string.IsNullOrWhiteSpace(command.Value):
                    repository.Commit(null, "IdentityConflictAcknowledged", "Maintenance=" + ManagementEvidence!.AuditActor + "; original=" + command.Owner + "; " + command.Value, command: command, clearConflict: command.Owner);
                    identityConflicts.Remove(command.Owner); break;
                case "CloseInterruptedCommand" when CanConfigure && command.Owner != command.Id && repository.IsPendingCommand(command.Owner) &&
                    !string.IsNullOrWhiteSpace(command.Value) && command.Value.Length <= 1024:
                    repository.Commit(null, "InterruptedCommandInspected", Wire.Encode(new { OriginalCommand = command.Owner, InspectionCommand = command.Id,
                        Actor = ManagementEvidence, Reason = command.Value, Policy = "Inspection only; no business fact, request or recovery guard is cleared" }), command: command, interruptedCommand: command.Owner);
                    break;
                case "ArchiveHistory" when CanConfigure && !string.IsNullOrWhiteSpace(command.Value):
                    ArchiveSettled(true, "Maintenance=" + ManagementEvidence?.AuditActor + "; " + command.Value, command); break;
                case "ContinuePrepared" when command.Owner is not null && operations.TryGetValue(command.Owner, out var op) &&
                    CanContinue(op):
                    var continuationEvent = op.RecoveryRequired ? "RecoveryConfirmed" : "ContinuationAdmitted";
                    if (op.RecoveryRequired)
                    {
                        if (string.IsNullOrWhiteSpace(command.Value)) return new("Rejected", "Explicit original-entity inspection reason is required after restart", repository.Revision);
                        op = op with { RecoveryRequired = false };
                    }
                    Save(op, continuationEvent, command.Value ?? "Continue original never-attempted intention", command: command);
                    Boundary?.Invoke("AfterContinuationAdmitted");
                    await AdvanceAsync(op).ConfigureAwait(false); break;
                case "ContinueUpload" when command.Owner is not null && operations.TryGetValue(command.Owner, out var imageOp) && CanContinueUpload(imageOp):
                    repository.Commit(null, "UploadContinuationAdmitted", Wire.Encode(new { Operation = imageOp.Id }), command: command);
                    Boundary?.Invoke("AfterContinuationAdmitted"); ScheduleAttached(imageOp); break;
                case "AbandonUnsubmitted" when command.Owner is not null && operations.TryGetValue(command.Owner, out var abandoned) && CanAbandon(abandoned) &&
                    !string.IsNullOrWhiteSpace(command.Value):
                    Save(abandoned with { State = ParcelState.Abandoned, RecoveryRequired = false,
                        Wcs = CancelUnsent(abandoned.Wcs), Plc = CancelUnsent(abandoned.Plc),
                        Upload = abandoned.Upload is { State: StageState.Prepared, Attempts: 0 } u ? u with { State = StageState.Cancelled, Failure = "Local cancellation before any external upload attempt" } : abandoned.Upload,
                        Capture = abandoned.Capture is { State: StageState.Prepared, Attempts: 0 } c ? c with { State = StageState.Cancelled, Failure = "Local cancellation before acquisition" } : abandoned.Capture,
                        Reason = "Unsubmitted parcel inspected and removed: " + command.Value }, "Abandoned", "Unsent intentions cancelled locally; original accepted WCS/slot and all request identities retained; no physical completion asserted", command: command);
                    Boundary?.Invoke("AfterAbandonedDurable"); ReleaseCompletedImages(); break;
                case "InspectRestore": return new("RestoreHandoff", Wire.Encode(ReadRestoreHandoff()), repository.Revision);
                case "ConfirmRestore": ConfirmRestore(command); break;
                default: return new("Rejected", "Unsupported command or an application guard is closed", repository.Revision);
            }
            return repository.PriorCommand(command) ?? throw new InvalidDataException("Accepted command lacks a durable receipt");
        }
        catch (Exception error) when (IsStorageError(error)) { StorageFailed(error, "CommandCommit"); throw; }
        finally { gate.Release(); }
    }
    static StageFact? CancelUnsent(StageFact? stage) => stage is { State: StageState.Prepared, Attempts: 0 }
        ? stage with { State = StageState.Cancelled, Failure = "Inspected local cancellation before any external attempt" } : stage;
    public async Task<BusinessReceipt> StageConfigurationAsync(long expectedRevision, ParcelConfiguration candidate, string? reason = null)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        ConfigurationChangeAudit? audit = null;
        var ownsQuiescence = false;
        try
        {
            if (stopped || stopping.IsCancellationRequested || configuration is null || !CanSaveConfiguration) return new("Rejected", "Configuration maintenance is unavailable; enter management mode and wait for the current operation", repository?.Revision ?? 0);
            if (string.IsNullOrWhiteSpace(reason) || reason.Length > 1024) return new("Rejected", "A maintenance change reason of 1..1024 characters is required", repository!.Revision);
            if (definition.AutomaticConfigurationQuiescence)
            {
                configurationSaving = ownsQuiescence = true;
                var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
                while (externalWaits != 0 || activeStages.Count != 0 || attachedRunning.Count != 0)
                {
                    if (DateTimeOffset.UtcNow >= deadline) return new("Rejected", "当前请求尚未完成，设置未保存；原配置及请求保持。", repository!.Revision);
                    gate.Release();
                    try { await Task.Delay(25, stopping.Token).ConfigureAwait(false); }
                    finally { await gate.WaitAsync().ConfigureAwait(false); }
                }
                stopping.Token.ThrowIfCancellationRequested(); Management.Require();
            }
            candidate = Wire.Decode<ParcelConfiguration>(Wire.Encode(candidate));
            audit = new(Wire.Id(), DateTimeOffset.UtcNow, currentOperator, reason, configuration.Current.Revision, configuration.Current.Revision,
                Wire.Hash(Wire.Encode(Effective)), Wire.Hash(Wire.Encode(candidate)), "Prepared", BeforeEffective: Effective, BeforePending: configuration.Current.Pending, Candidate: candidate, Authorization: ManagementEvidence);
            repository!.Commit(null, "ConfigurationChangePrepared", Wire.Encode(audit), configurationAudit: audit);
            if (candidate.SchemaVersion != 3) throw new InvalidDataException("Topology schema downgrade/flattening is forbidden");
            var errors = definition.Validate(candidate);
            if (errors.Length != 0) throw new InvalidDataException(string.Join("; ", errors));
            RequireSameIdentity(Effective, candidate); candidate = factory.Normalize(candidate);
            using var configurationUse = factory.ProtectConfiguration(candidate);
            audit = audit with { CandidateHash = Wire.Hash(Wire.Encode(candidate)), Candidate = candidate };
            repository.Commit(null, "ConfigurationCandidateNormalized", Wire.Encode(audit), configurationAudit: audit);
            factory.ValidateStructure(candidate);
            if (configuration.Current.Revision == expectedRevision && configuration.Current.Pending is null && Wire.Encode(Effective) == Wire.Encode(candidate))
            {
                audit = audit with { AfterRevision = expectedRevision, Status = "Unchanged", Result = "Candidate equals Effective; no configuration revision created" };
                repository.Commit(null, "ConfigurationUnchanged", Wire.Encode(audit), configurationAudit: audit);
                return new("Unchanged", "No configuration changes; Effective and revision retained", repository.Revision);
            }
            if (configuration.Current.Revision == expectedRevision && configuration.Current.Status == "PendingRestart" && configuration.Current.Pending is { } pending && Wire.Encode(pending) == Wire.Encode(candidate))
            {
                audit = audit with { AfterRevision = expectedRevision, Status = "PendingRestart", Result = "Existing identical Pending retained; no second configuration revision" };
                repository.Commit(null, "ConfigurationPendingAlreadySaved", Wire.Encode(audit), configurationAudit: audit);
                return new("PendingRestart", "Identical candidate already saved; Effective unchanged", repository.Revision);
            }
            Management.Require();
            configuration.Stage(expectedRevision, candidate);
            Diagnostic("参数", "配置已保存待重启", detail: "Revision=" + configuration.Current.Revision);
            Boundary?.Invoke("AfterConfigurationStaged");
            audit = audit with { AfterRevision = configuration.Current.Revision, Status = "PendingRestart", Result = "Validated and saved; Effective retained" };
            repository.Commit(null, "ConfigurationPendingRestart", Wire.Encode(audit), configurationAudit: audit);
            return new("PendingRestart", "Saved; Effective unchanged until controlled restart and application barriers", repository.Revision);
        }
        catch (Exception error) when (IsStorageError(error)) { StorageFailed(error, "ConfigurationCommit"); throw; }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException)
        {
            if (audit is not null)
            {
                audit = audit with { AfterRevision = configuration!.Current.Revision, Status = "Rejected", Result = ex.GetType().Name + ": " + ex.Message };
                repository!.Commit(null, "ConfigurationRejected", Wire.Encode(audit), configurationAudit: audit);
            }
            return new("Rejected", ex.Message, repository?.Revision ?? 0);
        }
        finally { if (ownsQuiescence) configurationSaving = false; gate.Release(); }
    }
    public Task<BusinessReceipt> ReconnectAsync() => MaintainDevicesAsync(null);
    public Task<BusinessReceipt> ReconnectDeviceAsync(string deviceId) => MaintainDevicesAsync(deviceId);
    public Task<BusinessReceipt> BackupAsync(string destination) => OwnedRead(token => BackupCoreAsync(destination, token));
    bool backupInProgress;
    async Task<BusinessReceipt> BackupCoreAsync(string destination, CancellationToken token)
    {
        ParcelRepository original; ParcelConfiguration effective; ParcelConfiguration? pending; ParcelOperation[] images; long bytes;
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (operations.Values.Any(o => o.OriginalFrame?.State == "Pending"))
                return new("Rejected", "等待识码原图后台保存完成后再创建一致性备份；原任务和原请求保留。", repository?.Revision ?? 0);
            if (repository is null || !repository.Paused || !MaintenanceAuthorized || externalWaits != 0 || activeStages.Count != 0 || attachedRunning.Count != 0 || backupInProgress)
                return new("Rejected", "Pause and drain external work before consistent maintenance backup", repository?.Revision ?? 0);
            original = repository; effective = Effective; pending = configuration!.Current.Pending; bytes = original.StorageBytes;
            images = operations.Values.Where(x => x.Image is not null && !x.ImageReleased).ToArray();
            backupInProgress = true;
        }
        finally { gate.Release(); }
        try
        {
            foreach (var op in images) ValidateBackupImage(directory!, op, op.Image!, token);
            ParcelRecovery.CreateBackup(directory!, destination, effective, pending, bytes,
                path => original.Backup(path, token), factory.RegisterBackup, () => { token.ThrowIfCancellationRequested(); Management.Require(); }, token);
            return new("BackupCreated", "Consistent database plus configuration/receipt custody; COMPLETED.json identifies the whole recovery set", original.Revision);
        }
        finally
        {
            await gate.WaitAsync().ConfigureAwait(false);
            try { backupInProgress = false; } finally { gate.Release(); }
        }
    }
    readonly object stopOwnership = new();
    Task? stopOperation;
    public Task StopAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        lock (startupOwnership) { stopRequested = true; Interlocked.Exchange(ref accepting, 0); }
        lock (stopOwnership)
        {
            // Even the synchronous prefix of an adapter Stop may block. One real
            // owner survives cancellation of any caller's wait; retries join it.
            if (stopOperation is null || stopOperation.IsFaulted || stopOperation.IsCanceled)
                stopOperation = Task.Run(async () =>
                {
                    await shutdown.WaitAsync().ConfigureAwait(false);
                    try { await StopCoreAsync(CancellationToken.None).ConfigureAwait(false); }
                    finally { shutdown.Release(); }
                });
            return stopOperation.WaitAsync(token);
        }
    }
    async Task StopCoreAsync(CancellationToken token)
    {
        if (stopped) return;
        Task startup;
        lock (startupOwnership) { stopRequested = true; startup = started ? startupEnded.Task : Task.CompletedTask; }
        Interlocked.Exchange(ref accepting, 0); stopping.Cancel();
        // Cancellation ends this caller's wait only. Startup may still be inside
        // an uncancellable adapter call or writing its terminal audit.
        await startup.WaitAsync(token).ConfigureAwait(false);
        ingressStartup.TrySetResult(false);
        // A timeout leaves every downstream resource owned; a subsequent Stop waits again.
        if (ports is not null)
        {
            List<Exception> sourceFailures = [];
            foreach (var source in ReaderPorts().Cast<object>().Concat(CapabilityPorts().OfType<IControlledWeightSource>()))
                try
                {
                    if (source is IControlledObservationSource reader) await DeviceCallAsync(reader.DeviceId, () => reader.StopAsync(token), token).ConfigureAwait(false);
                    else await DeviceCallAsync(DeviceKey((IControlledWeightSource)source), () => ((IControlledWeightSource)source).StopAsync(token), token).ConfigureAwait(false);
                }
                catch (Exception ex) { sourceFailures.Add(ex); }
            if (sourceFailures.Count != 0)
            {
                if (sourceFailures.All(x => x is OperationCanceledException)) throw new OperationCanceledException(token);
                throw new AggregateException("Source stop failures; all downstream ownership retained", sourceFailures);
            }
        }
        // Also fence direct/reference handoffs and adapters whose Stop returned
        // before a synchronous receiver finished. Never close behind a producer.
        ingressDrain ??= Task.Run(() =>
        {
            foreach (var pair in ingressLanes)
                lock (pair.Value.Sync) positionQueues[pair.Key].Writer.TryComplete();
        });
        await ingressDrain.WaitAsync(token).ConfigureAwait(false);
        overloadWrites.Writer.TryComplete();
        await DrainTerminalWorkerAsync(overloadWorker, token).ConfigureAwait(false);
        await DrainReadOperations(token).ConfigureAwait(false);
        await DrainTerminalWorkerAsync(worker, token).ConfigureAwait(false);
        await DrainTerminalWorkerAsync(maintenanceWorker, token).ConfigureAwait(false);
        await DrainTerminalWorkerAsync(supervisor, token).ConfigureAwait(false);
        foreach (var task in auxiliary.ToArray()) await DrainTerminalWorkerAsync(task, token).ConfigureAwait(false);
        Task drain;
        await gate.WaitAsync(token).ConfigureAwait(false);
        try { drain = externalDrained?.Task ?? Task.CompletedTask; } finally { gate.Release(); }
        await drain.WaitAsync(token).ConfigureAwait(false);
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            List<Exception> portFailures = [];
            // A reconnect already in flight when Stop began may finish after the first
            // source stop. External drain has now fenced all registrations.
            foreach (var reader in ReaderPorts())
                try { await DeviceCallAsync(reader.DeviceId, () => reader.StopAsync(token), token).ConfigureAwait(false); } catch (Exception ex) { portFailures.Add(ex); }
            foreach (var port in CapabilityPorts())
                try { await DeviceCallAsync(DeviceKey(port), () => port.StopAsync(token), token).ConfigureAwait(false); } catch (Exception ex) { portFailures.Add(ex); }
            if (portFailures.Count != 0) throw new AggregateException("Capability stop failures; ownership retained", portFailures);
            stopped = true;
            if (terminalWorkerFailure is not null) throw new InvalidOperationException("Terminal worker failed; sources and ports are stopped, disposal is still required", terminalWorkerFailure);
        }
        finally { gate.Release(); }
    }
    async Task DrainTerminalWorkerAsync(Task? task, CancellationToken token)
    {
        if (task is null) return;
        try { await task.WaitAsync(token).ConfigureAwait(false); }
        catch (Exception error) when (task.IsFaulted || task.IsCanceled)
        {
            // Actual termination permits cleanup, not a successful work result.
            // Keep every failure for the final lifecycle receipt, once per owner.
            // A cancelled wait on unfinished work still escapes and retains resources.
            terminalWorkerFailures.TryAdd(task, (Exception?)task.Exception ?? error);
        }
    }
    public async ValueTask DisposeAsync()
    {
        await shutdown.WaitAsync().ConfigureAwait(false);
        try { await DisposeCoreAsync().ConfigureAwait(false); }
        finally { shutdown.Release(); }
    }
    async Task DisposeCoreAsync()
    {
        if (disposed) return;
        Exception? stopFailure = stopped ? terminalWorkerFailure : null;
        try { await StopCoreAsync(CancellationToken.None).ConfigureAwait(false); }
        catch (Exception ex) when (stopped) { stopFailure = ex; }
        if (ports is not null)
        {
            List<Exception> disposalFailures = [];
            foreach (var reader in ReaderPorts())
                try { await reader.DisposeAsync().ConfigureAwait(false); } catch (Exception ex) { disposalFailures.Add(ex); }
            foreach (var port in CapabilityPorts())
                try { await port.DisposeAsync().ConfigureAwait(false); } catch (Exception ex) { disposalFailures.Add(ex); }
            if (disposalFailures.Count != 0) throw new AggregateException("Capability disposal failures; shared factory/package/database leases retained", disposalFailures);
        }
        await factory.DisposeAsync().ConfigureAwait(false);
        if (inspectionStore is { } inspection) await inspection.DisposeAsync().ConfigureAwait(false);
        if (parcelPictures is { } pictures) await pictures.DisposeAsync().ConfigureAwait(false);
        readRound?.Cancel.Dispose();
        logReader?.Dispose();
        if (journal is not null) await journal.DisposeAsync().ConfigureAwait(false);
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            foreach (var lane in ingressLanes.Values) lane.Dispose();
            repository?.Dispose(); lease?.Dispose(); stopping.Dispose(); disposed = true;
        }
        finally { gate.Release(); }
        if (stopFailure is not null) throw new InvalidOperationException("Application resources released after terminal worker failure", stopFailure);
    }
}

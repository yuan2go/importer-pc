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
            MaintainSmRounds();
            if (configuration is null || repository is null) throw new InvalidOperationException("Application has not started");
            ObjectDisposedException.ThrowIf(disposed, this);
            // Single consistent read per projection; this scope never releases the business gate.
            repository.BeginReceiptProjection();
            projectedCapacity = operations.Values.Count(x => !ArchiveEligible(x));
            RefreshIntegrationBudget();
            List<string> reasons = []; List<string> auxiliaryReasons = []; var now = DateTimeOffset.UtcNow;
            if (ParcelConnectionSetup.BlocksWork(Effective) && Effective.DeferredConnections is { Length: > 0 } deferred)
                reasons.Add("连接待配置：" + string.Join("、", deferred.Select(id => Effective.Wcs?.InstanceId == id ? "RCS" : ParcelBindings.Positions(Effective).Single(p => p.ReaderDeviceId == id).Id == "left" ? "左相机" : "右相机")) + "。可进入参数设置；填写连接参数后保存并重启软件，当前不接收新包裹。");
            if (factory.AdmissionFailure(Effective) is { } installationFailure)
            {
                reasons.Add(Id == "sportmaster" ? nameof(ParcelInputBlockReason.InstallationNotReady) : installationFailure);
                if (Id == "sportmaster") auxiliaryReasons.Add(installationFailure);
            }
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
            if (!CapacityAvailable) reasons.Add(CapacityBlock.ToString());
            foreach (var conflict in identityConflicts.Values) reasons.Add("IdentityConflict:" + conflict.Id + "; positions=" + string.Join(",", conflict.Positions));
            if (overload != 0) reasons.Add("Overload: rejected callback; inspect parcel and acknowledge while paused");
            if (!SessionValid) reasons.Add("Operator authentication required for new admission");
            if (repository.Paused) reasons.Add("Software admission paused; this is not an emergency stop");
            if (Id == "sportmaster" && positions.Keys.All(p => !PositionSupplyEnabled(p))) reasons.Add("左右投递位均未启用，不再接收新包裹。");
            if (!RestoreConfirmed) reasons.Add("RestoreHandoffRequired: keep isolated; verify same-machine source retirement and authenticate maintenance; cross-machine evidence still required");
            reasons.AddRange(maintainedResources.Keys.Order().Select(resource => "MaintenanceInProgress:" + resource));
            if (readers.Values.Any(r => !ParcelConnectionSetup.IsDeferred(Effective, r.Source.DeviceId) && r.Health?.IsValid(DateTimeOffset.UtcNow) != true)) (Id == "sportmaster" ? auxiliaryReasons : reasons).Add("Reader confirmation missing/expired");
            if (!ImageCapacityAvailable()) reasons.Add("Temporary image capacity/backlog exhausted; existing originals retained");
            if (operations.Values.Any(x => NeedsReconciliation(x) && !ReleasedForNextAdmission(x)))
                reasons.Add(Id == "sportmaster" ? nameof(ParcelInputBlockReason.PreviousResultUnconfirmed) : "Original stage outcome or receipt responsibility is unresolved");
            if (Id != "sportmaster")
            {
                var retainedOriginals = operations.Values.Count(x => NeedsReconciliation(x) && ReleasedForNextAdmission(x));
                if (retainedOriginals > 0) auxiliaryReasons.Add("RetainedOriginalRequests:" + retainedOriginals + "; outcomes remain unchanged; new placements do not replay originals");
            }
            foreach (var op in operations.Values.Where(x => !ReleasedForNextAdmission(x) && (x.State == ParcelState.WeightTimedOut || WeightExpired(x) || x.Weight is not null && !WeightReady(x) && !MainTerminated(x))))
                reasons.Add("Measurement missing/expired for admission " + op.AdmissionId);
            List<CapabilityHealth> health = [.. capabilityHealth.Values.Select(h =>
                h with { Ready = h.Ready && h.ConfirmedAt <= now && h.ValidUntil > now && h.ValidUntil > h.ConfirmedAt })];
            foreach (var reader in readers.Values.Select(r => r.Health).OfType<DeviceState>()) health.Add(new(reader.DeviceId, reader.IsValid(DateTimeOffset.UtcNow), reader.ConfirmedAt ?? reader.UpdatedAt, reader.ValidUntil ?? reader.UpdatedAt, reader.ValidityReason(DateTimeOffset.UtcNow) ?? reader.Source));
            foreach (var h in health.Where(x => !x.Ready && !ParcelConnectionSetup.IsDeferred(Effective, x.InstanceId)))
                (Id == "sportmaster" && readers.ContainsKey(h.InstanceId) || Effective.Images?.Strategy == UploadStrategy.Attached && h.InstanceId == Effective.Images.Uploader?.InstanceId ? auxiliaryReasons : reasons).Add("Capability unavailable: " + h.InstanceId);
            var receipts = repository.PendingReceipts();
            if (Id == "sportmaster" && receipts.Any(receipt =>
                (!operations.TryGetValue(receipt.Operation, out var owner) || !StageRunning(owner)) && !repository.HasReceiptOutcomeBasis(receipt.Id)))
                reasons.Add(nameof(ParcelInputBlockReason.ReceiptPersistencePending));
            auxiliaryReasons.AddRange(receipts.Select(x => x.Operation + ": original receipt custody " + x.Id + "; stage=" + x.Stage + "; raw=" + (x.Reply is null ? "missing" : "durable pending T3") + "; outcome=" + (repository.HasReceiptOutcomeBasis(x.Id) ? "known by separate Query" : "unresolved")));
            if (repository.ArchiveCleanupFailure is { } cleanup) auxiliaryReasons.Add(cleanup);
            auxiliaryReasons.AddRange(repository.PendingCommands().Select(id => "CommandPending:" + id + "; inspect CommandAdmitted audit and original facts; this ID is never replayed"));
            if (Volatile.Read(ref lastExportFailure) is { } exportFailure) auxiliaryReasons.Add("HistoryExport:" + exportFailure);
            if ((parcelPictureFailure ?? parcelPictures?.Fault) is { } pictureFailure) auxiliaryReasons.Add("ParcelPictures:" + pictureFailure);
            if (backupScheduleFault is { } scheduleFault) auxiliaryReasons.Add(scheduleFault);
            if (backupScheduleState is { } scheduled) auxiliaryReasons.Add("BackupSchedule:" + scheduled.Outcome + "; next=" + scheduled.NextDue.ToString("O") + "; checked=" + scheduled.At.ToString("O"));
            auxiliaryReasons.AddRange(operations.Values.Where(x => x.FrozenConfiguration.Images?.Strategy == UploadStrategy.Attached && x.Upload?.Failure is not null).Select(x => x.Id + ": upload " + x.Upload!.Failure));
            var positionSnapshots = PositionSnapshots();
            var inputs = positionSnapshots.Select(p => DisplayedInput(p.Id, p.ExpiredManualResult)).OfType<ParcelInputSnapshot>().ToArray();
            var projection = new ParcelSnapshot(runtimeId, Id, repository.Revision, stopped ? "Stopped" : reasons.Count == 0 ? "Ready" : CanAdmit() ? "PartiallyReady" : "Blocked", repository.Paused,
                reasons.ToArray(), operations.Values.Concat(positions.Keys.Select(LatestWorkCardOperation).OfType<ParcelOperation>()).DistinctBy(x => x.Id).OrderByDescending(x => x.ObservedAt).ToArray(), health.ToArray(), configuration.Current, currentOperator, Availability(), positionSnapshots, readers.Values.Select(r => r.Health).OfType<DeviceState>().ToArray(), Capacity(), IdentityConflicts(), auxiliaryReasons.ToArray(), inputs, BackupStatus(), configuration.Current.Pending is not null)
            {
                ConfigurationActivationBlock = Id == "sportmaster" && configuration.Current.Pending is not null ? SmConfigurationBlock() : null,
                Rounds = Id == "sportmaster" ? smCurrentRounds.Values.Select(id => smRounds[id]).ToArray() : []
            };
            encoded = EncodeSnapshot(WithReadFeedbackSnapshot(projection));
        }
        finally
        {
            projectedCapacity = null;
            try { repository?.EndReceiptProjection(); Boundary?.Invoke("SnapshotGateReleasing"); }
            finally { gate.Release(); }
        }
        // Deserialize the detached UI copy after releasing the business gate.
        return Wire.Decode<ParcelSnapshot>(encoded) with { PublishedTicks = System.Diagnostics.Stopwatch.GetTimestamp() };
    }
    public async Task<BusinessProjection> QueryAsync()
    {
        var s = await SnapshotAsync().ConfigureAwait(false);
        return new(s.RuntimeId, s.Revision, s.State, new()
        {
            ["Application"] = Id,
            ["Operations"] = s.Operations.Length.ToString(),
            ["Unresolved"] = s.Operations.Count(NeedsReconciliation).ToString(),
            ["Reasons"] = string.Join("; ", s.BlockingReasons)
        });
    }
    public async Task<BusinessReceipt> CommandAsync(BusinessCommand command)
    {
        if (command.Kind is not ("Pause" or "Resume")) return await ExecuteCommandAsync(command).ConfigureAwait(false);
        WriteRuntimeLog("业务控制", "供包命令开始", new { CommandId = command.Id, command.Kind, command.ExpectedRevision, TriggerReason = command.Value, Actor = currentOperator?.Subject ?? "local-workstation" });
        try
        {
            var receipt = await ExecuteCommandAsync(command).ConfigureAwait(false);
            WriteRuntimeLog("业务控制", "供包命令结果", new { CommandId = command.Id, command.Kind, receipt.Status, receipt.Reason, receipt.Revision });
            await gate.WaitAsync().ConfigureAwait(false);
            try { WriteStationState(command.Kind + ":" + command.Id, force: true); }
            finally { gate.Release(); }
            return receipt;
        }
        catch (Exception error)
        {
            WriteRuntimeLog("业务控制", "供包命令异常，结果须核对", new { CommandId = command.Id, command.Kind, Error = error.ToString() }, "错误");
            throw;
        }
    }
    async Task<BusinessReceipt> ExecuteCommandAsync(BusinessCommand command)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (stopped || repository is null) return new("NotReady", "Application stopped", repository?.Revision ?? 0);
            if (!Guid.TryParseExact(command.Id, "N", out _)) return new("Rejected", "Stable command identity is required", repository.Revision);
            if (repository.PriorCommand(command) is { } prior) return prior;
            if (backupInProgress && command.Kind is not ("RuntimeProgress" or "InspectRestore" or "ReceiptResponsibilities" or "IngressResponsibilities"))
                return new("Rejected", "Consistent backup is in progress; original responsibility and configuration remain fenced", repository.Revision);
            // Removal uses its exact round/version token below. An unrelated lane's
            // receipt must not invalidate this operator's correctly targeted click.
            if (command.ExpectedRevision != repository.Revision && !(Id == "sportmaster" && command.Kind == "ConfirmPlacementRemoval"))
                return new("Conflict", "Refresh the current application projection", repository.Revision);
            switch (command.Kind)
            {
                case "RuntimeProgress": return new("RuntimeProgress", Wire.Encode(RuntimeProgress()), repository.Revision);
                case "IngressResponsibilities" when MaintenanceAuthorized: return new("IngressResponsibilities", Wire.Encode(repository.IngressResponsibilities()), repository.Revision);
                case "InspectInterruptedIngress": return InspectInterruptedIngress(command);
                case "ReceiptResponsibilities" when command.Owner is not null:
                    return new("ReceiptResponsibilities", Wire.Encode(repository.ReceiptResponsibilities(command.Owner)), repository.Revision);
                case "ReconcileReceipt": return await ReconcileReceiptAsync(command).ConfigureAwait(false);
                case "ConfirmPlacementRemoval" when ConfirmPlacementRemoval(command): break;
                case "CalibrateEmptyTray" when Id == "sportmaster" && MaintenanceAuthorized && ManagementWorkspaceOpen &&
                    command.Owner is { } camera && readers.TryGetValue(camera, out var calibratedReader) &&
                    calibratedReader.Health?.IsValid(DateTimeOffset.UtcNow) == true &&
                    positions.Values.SingleOrDefault(p => p.ReaderDeviceId == camera) is { } calibratedPosition &&
                    CurrentRound(calibratedPosition.Id) is null && calibratedReader.Source is ITrayPresenceSource presenceSource:
                    presenceSource.CalibrateEmptyTray();
                    repository.Commit(null, "EmptyTrayCalibrationRequested", Wire.Encode(new { Camera = camera, calibratedReader.Epoch, Actor = ManagementEvidence }), command: command);
                    break;
                case "Pause": repository.Commit(null, "AdmissionPaused", command.Value ?? "Software pause only; caller did not supply a trigger", paused: true, command: command); resumePriorPause = false; break;
                case "Resume" when CanResume():
                    repository.Commit(null, "AdmissionResumed", "Other stage guards remain independent",
                        paused: false, command: command);
                    resumePriorPause = false; break;
                case "AcknowledgeOverload" when repository.Paused && overload != 0 && Volatile.Read(ref pendingOverloadWrites) == 0 && positionQueues.Values.All(q => q.Reader.Count == 0):
                    var originalOverload = OverloadIdentity();
                    repository.Commit(null, "OverloadAcknowledged", "Paused operator inspection acknowledged; original=" + originalOverload, command: command, acknowledgedOverload: originalOverload);
                    Boundary?.Invoke("AfterOverloadAcknowledged"); await ExternalAsync(async () => { await RemoveAcknowledgedOverload(originalOverload).ConfigureAwait(false); return true; }).ConfigureAwait(false); break;
                case "AcknowledgeIdentityConflict" when CanConfigure && command.Owner is not null && identityConflicts.ContainsKey(command.Owner) && !string.IsNullOrWhiteSpace(command.Value):
                    repository.Commit(null, "IdentityConflictAcknowledged", "Maintenance=" + ManagementEvidence!.AuditActor + "; original=" + command.Owner + "; " + command.Value, command: command, clearConflict: command.Owner);
                    identityConflicts.Remove(command.Owner); break;
                case "CloseInterruptedCommand" when CanConfigure && command.Owner != command.Id && repository.IsPendingCommand(command.Owner) &&
                    !string.IsNullOrWhiteSpace(command.Value) && command.Value.Length <= 1024:
                    repository.Commit(null, "InterruptedCommandInspected", Wire.Encode(new
                    {
                        OriginalCommand = command.Owner,
                        InspectionCommand = command.Id,
                        Actor = ManagementEvidence,
                        Reason = command.Value,
                        Policy = "Inspection only; no business fact, request or recovery guard is cleared"
                    }), command: command, interruptedCommand: command.Owner);
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
                    Save(ParcelUnsubmittedIntent.Abandon(abandoned, command.Value), "Abandoned", "Unsent intentions cancelled locally; original accepted WCS/slot and all request identities retained; no physical completion asserted", command: command);
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
    static StageFact? CancelUnsent(StageFact? stage) => ParcelUnsubmittedIntent.Cancel(stage);
    public async Task<BusinessReceipt> StageConfigurationAsync(long expectedRevision, ParcelConfiguration candidate, string? reason = null)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        ConfigurationChangeAudit? audit = null;
        var ownsQuiescence = false;
        try
        {
            if (stopped || stopping.IsCancellationRequested || configuration is null || !CanSaveConfiguration) return new("Rejected", "Configuration maintenance is unavailable; enter management mode and wait for the current operation", repository?.Revision ?? 0);
            if (fatalStorage.Task.IsCompleted) return new("Rejected", ParcelInputReasons.ActionableNotice(ParcelInputBlockReason.StorageUnavailable)!, repository!.Revision);
            if (string.IsNullOrWhiteSpace(reason) || reason.Length > 1024) return new("Rejected", "A maintenance change reason of 1..1024 characters is required", repository!.Revision);
            if (definition.AutomaticConfigurationQuiescence)
            {
                configurationSaving = ownsQuiescence = true;
                var waitStarted = System.Diagnostics.Stopwatch.GetTimestamp();
                while (externalWaits != 0 || activeStages.Count != 0 || attachedRunning.Count != 0)
                {
                    if (System.Diagnostics.Stopwatch.GetElapsedTime(waitStarted) >= TimeSpan.FromSeconds(10))
                        return new("Rejected", "设置尚未保存。" + ParcelInputReasons.ActionableNotice(ParcelInputBlockReason.PreviousResultPending), repository!.Revision);
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
            if (Id == "sportmaster")
            {
                RetainReleasedSmUnknowns();
            }
            var activationBlock = Id == "sportmaster" ? SmConfigurationBlock() : null;
            if (activationBlock is null && ConfigurationActivation.RulesOnly(Id, Effective, configuration.Current.Pending, candidate))
            {
                // No queued observation may silently acquire a new interpretation.
                // Existing operations retain their complete FrozenConfiguration.
                if (Volatile.Read(ref pendingObservations) != 0 || positionQueues.Values.Any(q => q.Reader.CanCount && q.Reader.Count != 0))
                    throw new InvalidOperationException("仍有待处理扫描，规则未保存；请等待处理完成后再保存。");
                configuration.ApplyLive(expectedRevision, candidate);
                audit = audit with { AfterRevision = configuration.Current.Revision, Status = "Effective", Result = "Live rules confirmed; prior operations retain frozen rules" };
                repository.Commit(null, "ConfigurationLiveApplied", Wire.Encode(audit), configurationAudit: audit);
                Diagnostic("参数", "条码规则已生效", detail: "Revision=" + configuration.Current.Revision);
                return new("Effective", "条码规则已保存并生效；已受理作业及原请求保持原规则。", repository.Revision);
            }
            // Camera/RCS-only changes reconnect just those devices; anything else restarts as before.
            if (activationBlock is null && definition.AutomaticConfigurationQuiescence && ConfigurationActivation.LiveDevices(Id, Effective, configuration.Current.Pending, candidate) is { } live &&
                await ApplyDevicesLiveAsync(expectedRevision, candidate, live, audit).ConfigureAwait(false) is { } applied)
                return applied.Receipt;
            configuration.Stage(expectedRevision, candidate);
            Diagnostic("参数", "配置已保存待重启", detail: "Revision=" + configuration.Current.Revision + "; ActivationBlock=" + activationBlock);
            Boundary?.Invoke("AfterConfigurationStaged");
            audit = audit with { AfterRevision = configuration.Current.Revision, Status = "PendingRestart", Result = "Validated and saved; Effective retained" };
            repository.Commit(null, "ConfigurationPendingRestart", Wire.Encode(audit), configurationAudit: audit);
            return new("PendingRestart", Id != "sportmaster" ? "Saved; Effective unchanged until controlled restart and application barriers" :
                activationBlock is { } block ? "设置已保存，尚未生效。" + ParcelInputReasons.ActionableNotice(block) + "处理后重新打开供包台。" :
                "设置已保存，重新打开供包台后生效。", repository.Revision);
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
    public Task<BusinessReceipt> BackupAsync(string destination) => BackupAsync(destination, CancellationToken.None);
    internal Task<BusinessReceipt> BackupAsync(string destination, CancellationToken token) => OwnedRead(owned => BackupCoreAsync(destination, owned), token);
    bool backupInProgress;
    internal Action<string>? BackupBoundary { get; set; }
    async Task<BusinessReceipt> BackupCoreAsync(string destination, CancellationToken token)
    {
        ParcelRepository original; ParcelConfiguration effective; ParcelConfiguration? pending; ParcelOperation[] images; long bytes;
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (operations.Values.Any(o => o.OriginalFrame?.State == "Pending"))
                return new("Rejected", "等待识码原图后台保存完成后再创建一致性备份；原任务和原请求保留。", repository?.Revision ?? 0);
            if (repository is null || !repository.Paused || !MaintenanceAuthorized || externalWaits != 0 || activeStages.Count != 0 || attachedRunning.Count != 0 || backupInProgress)
                return new("Rejected", "当前尚未满足完整备份条件。请先暂停供包、验证管理授权，并等待在途任务、设备调用或另一项备份结束；原请求和原回执保留。", repository?.Revision ?? 0);
            original = repository; effective = Effective; pending = configuration!.Current.Pending; bytes = original.StorageBytes;
            images = operations.Values.Where(x => x.Image is not null && !x.ImageReleased).ToArray();
            backupInProgress = true;
        }
        catch (Exception error) when (IsStorageError(error) || error is InvalidDataException)
        { GuardBackupSourceFailure(error, "BackupSourcePreparation", knownSource: true); throw; }
        finally { gate.Release(); }
        try
        {
            try { foreach (var op in images) ValidateBackupImage(directory!, op, op.Image!, token); }
            catch (Exception error) when (IsStorageError(error) || error is InvalidDataException)
            { throw new RecoveryFileFailure(RecoveryFailureOrigin.Source, "SourceImageValidation", error); }
            ParcelRecovery.CreateBackup(directory!, destination, effective, pending, bytes,
                path => original.Backup(path, token), factory.RegisterBackup, () => { token.ThrowIfCancellationRequested(); Management.Require(); }, token, boundary: BackupBoundary);
            return new("BackupCreated", "Consistent database plus configuration/receipt custody; COMPLETED.json identifies the whole recovery set", original.Revision);
        }
        catch (Exception error) when (IsStorageError(error) || error is InvalidDataException)
        {
            // Fence proven source failure before releasing the backup admission fence.
            GuardBackupSourceFailure(error, "BackupSourceCopy");
            throw;
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
        lock (startupOwnership) { stopRequested = true; FenceIngress(); }
        lock (stopOwnership)
        {
            // Even the synchronous prefix of an adapter Stop may block. One real
            // owner survives cancellation of any caller's wait; retries join it.
            if (stopOperation is null || stopOperation.IsFaulted || stopOperation.IsCanceled)
                stopOperation = Task.Run(async () =>
                {
                    await shutdown.WaitAsync().ConfigureAwait(false);
                    try { await StopCoreAsync(CancellationToken.None).ConfigureAwait(false); }
                    catch (Exception error)
                    {
                        WriteRuntimeLog("供包状态", "供包台停止失败，所有权仍须核对", new { Error = error.ToString(), DevicePortsReleased = stopped, ResourcesReleased = false }, "错误");
                        throw;
                    }
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
        FenceIngress(); stopping.Cancel(); StopDebugLogging();
        WriteRuntimeLog("供包状态", "供包台开始停止", new { ResourcesReleased = false });
        // Cancellation ends this caller's wait only. Startup may still be inside
        // an uncancellable adapter call or writing its terminal audit.
        await startup.WaitAsync(token).ConfigureAwait(false);
        ReleaseCameraReadFeedback();
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
        await DrainTerminalWorkerAsync(presentationPublisher, token).ConfigureAwait(false);
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
            blockedInputPictures.Clear();
            FlushInputLogSummaries();
            FlushIngressTimings(force: true);
            WriteStationState("StopCompleted", force: true);
            blockedInputs.Clear(); smRoundPictures.Clear();
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
            foreach (var resource in pendingLiveDeviceCleanup.ToArray())
                try { await resource.DisposeAsync().ConfigureAwait(false); pendingLiveDeviceCleanup.Remove(resource); } catch (Exception ex) { disposalFailures.Add(ex); }
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
            ReleaseStationReports();
            repository?.CloseStationShift(stationStartupCompleted && stopFailure is null);
            repository?.Dispose(); lease?.Dispose(); rootOwnership?.Dispose(); stopping.Dispose(); disposed = true;
        }
        finally { gate.Release(); }
        if (stopFailure is not null) throw new InvalidOperationException("Application resources released after terminal worker failure", stopFailure);
    }
}

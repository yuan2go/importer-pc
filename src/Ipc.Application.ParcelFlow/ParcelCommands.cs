using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime
{
    public async Task<ParcelSnapshot> SnapshotAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (configuration is null || repository is null) throw new InvalidOperationException("Application has not started");
            List<string> reasons = []; List<string> auxiliaryReasons = []; var now = DateTimeOffset.UtcNow;
            if (factory.AdmissionFailure(Effective) is { } qualificationFailure) reasons.Add(qualificationFailure);
            if (failure is not null) reasons.Add(failure);
            if (!CapacityAvailable) reasons.Add(storageCapacityFailure ?? "Unresolved operation capacity exhausted; original responsibilities cannot be archived");
            foreach (var conflict in identityConflicts.Values) reasons.Add("IdentityConflict:" + conflict.Id + "; positions=" + string.Join(",", conflict.Positions));
            if (overload != 0) reasons.Add("Overload: rejected callback; inspect parcel and acknowledge while paused");
            if (!SessionValid) reasons.Add("Operator authentication required for new admission");
            if (repository.Paused) reasons.Add("Software admission paused; this is not an emergency stop");
            if (readers.Values.Any(r => r.Health?.IsValid(DateTimeOffset.UtcNow) != true)) reasons.Add("Reader confirmation missing/expired");
            if (!ImageCapacityAvailable()) reasons.Add("Temporary image capacity/backlog exhausted; existing originals retained");
            if (operations.Values.Any(NeedsReconciliation)) reasons.Add("Original stage outcome or receipt responsibility is unresolved");
            foreach (var op in operations.Values.Where(x => x.State == ParcelState.WeightTimedOut || WeightExpired(x) || x.Weight is not null && !WeightReady(x) && !MainTerminated(x)))
                reasons.Add("Measurement missing/expired for admission " + op.AdmissionId);
            List<CapabilityHealth> health = [.. capabilityHealth.Values.Where(x => x.InstanceId != ports?.Maintenance?.InstanceId).Select(h =>
                h with { Ready = h.Ready && h.ConfirmedAt <= now && h.ValidUntil > now && h.ValidUntil > h.ConfirmedAt })];
            foreach (var reader in readers.Values.Select(r => r.Health).OfType<DeviceState>()) health.Add(new(reader.DeviceId, reader.IsValid(DateTimeOffset.UtcNow), reader.ConfirmedAt ?? reader.UpdatedAt, reader.ValidUntil ?? reader.UpdatedAt, reader.ValidityReason(DateTimeOffset.UtcNow) ?? reader.Source));
            foreach (var h in health.Where(x => !x.Ready))
                (Effective.Images?.Strategy == UploadStrategy.Attached && h.InstanceId == Effective.Images.Uploader?.InstanceId ? auxiliaryReasons : reasons).Add("Capability unavailable: " + h.InstanceId);
            if (repository.ArchiveCleanupFailure is { } cleanup) auxiliaryReasons.Add(cleanup);
            auxiliaryReasons.AddRange(operations.Values.Where(x => x.FrozenConfiguration.Images?.Strategy == UploadStrategy.Attached && x.Upload?.Failure is not null).Select(x => x.Id + ": upload " + x.Upload!.Failure));
            var projection = new ParcelSnapshot(runtimeId, Id, repository.Revision, stopped ? "Stopped" : reasons.Count == 0 ? "Ready" : Effective.SchemaVersion >= 3 && CanAdmit() ? "PartiallyReady" : "Blocked", repository.Paused,
                reasons.ToArray(), operations.Values.Concat(positions.Keys.Select(p => repository.Latest(p)).OfType<ParcelOperation>()).DistinctBy(x => x.Id).OrderByDescending(x => x.ObservedAt).ToArray(), health.ToArray(), configuration.Current, currentOperator, Availability(), PositionSnapshots(), readers.Values.Select(r => r.Health).OfType<DeviceState>().ToArray(), Capacity(), IdentityConflicts(), auxiliaryReasons.ToArray());
            return Wire.Decode<ParcelSnapshot>(Wire.Encode(projection));
        }
        finally { gate.Release(); }
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
            if (command.ExpectedRevision != repository.Revision) return new("Conflict", "Refresh the current application projection", repository.Revision);
            switch (command.Kind)
            {
                case "Pause": repository.Commit(null, "AdmissionPaused", "Software pause only", paused: true, command: command); break;
                case "Resume" when CanResume():
                    repository.Commit(null, "AdmissionResumed", "Other stage guards remain independent", paused: false, command: command); break;
                case "AcknowledgeOverload" when repository.Paused && overload != 0 && positionQueues.Values.All(q => q.Reader.Count == 0):
                    var originalOverload = OverloadIdentity();
                    repository.Commit(null, "OverloadAcknowledged", "Paused operator inspection acknowledged; original=" + originalOverload, command: command, acknowledgedOverload: originalOverload);
                    Boundary?.Invoke("AfterOverloadAcknowledged"); RecoverOverloadAcknowledgement(); break;
                case "AcknowledgeIdentityConflict" when CanConfigure && command.Owner is not null && identityConflicts.ContainsKey(command.Owner) && !string.IsNullOrWhiteSpace(command.Value):
                    repository.Commit(null, "IdentityConflictAcknowledged", "Maintenance=" + maintenanceSession.Identity!.Subject + "; original=" + command.Owner + "; " + command.Value, command: command, clearConflict: command.Owner);
                    identityConflicts.Remove(command.Owner); break;
                case "ArchiveHistory" when CanConfigure && !string.IsNullOrWhiteSpace(command.Value):
                    ArchiveSettled(true, "Maintenance=" + maintenanceSession.Identity?.Subject + "; " + command.Value); break;
                case "ContinuePrepared" when command.Owner is not null && operations.TryGetValue(command.Owner, out var op) &&
                    CanContinue(op):
                    if (op.RecoveryRequired)
                    {
                        if (string.IsNullOrWhiteSpace(command.Value)) return new("Rejected", "Explicit original-entity inspection reason is required after restart", repository.Revision);
                        op = op with { RecoveryRequired = false }; Save(op, "RecoveryConfirmed", command.Value);
                    }
                    await AdvanceAsync(op).ConfigureAwait(false); break;
                case "ContinueUpload" when command.Owner is not null && operations.TryGetValue(command.Owner, out var imageOp) && CanContinueUpload(imageOp):
                    ScheduleAttached(imageOp); break;
                case "AbandonUnsubmitted" when command.Owner is not null && operations.TryGetValue(command.Owner, out var abandoned) && CanAbandon(abandoned) &&
                    !string.IsNullOrWhiteSpace(command.Value):
                    Save(abandoned with { State = ParcelState.Abandoned,
                        Wcs = CancelUnsent(abandoned.Wcs), Plc = CancelUnsent(abandoned.Plc),
                        Upload = abandoned.Upload is { State: StageState.Prepared, Attempts: 0 } u ? u with { State = StageState.Cancelled, Failure = "Local cancellation before any external upload attempt" } : abandoned.Upload,
                        Capture = abandoned.Capture is { State: StageState.Prepared, Attempts: 0 } c ? c with { State = StageState.Cancelled, Failure = "Local cancellation before acquisition" } : abandoned.Capture,
                        Reason = "Unsubmitted parcel inspected and removed: " + command.Value }, "Abandoned", "Unsent intentions cancelled locally; original accepted WCS/slot and all request identities retained; no physical completion asserted"); ReleaseCompletedImages(); break;
                default: return new("Rejected", "Unsupported command or an application guard is closed", repository.Revision);
            }
            var receipt = new BusinessReceipt("Persisted", "Application command committed", repository.Revision);
            if (repository.PriorCommand(command) is null) repository.SaveCommand(command, receipt);
            return receipt;
        }
        finally { gate.Release(); }
    }
    static StageFact? CancelUnsent(StageFact? stage) => stage is { State: StageState.Prepared, Attempts: 0 }
        ? stage with { State = StageState.Cancelled, Failure = "Inspected local cancellation before any external attempt" } : stage;
    public async Task<BusinessReceipt> StageConfigurationAsync(long expectedRevision, ParcelConfiguration candidate, string? reason = null)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        ConfigurationChangeAudit? audit = null;
        try
        {
            if (stopped || configuration is null || !CanConfigure) return new("Rejected", "Pause and authenticate maintenance before configuration", repository?.Revision ?? 0);
            if (string.IsNullOrWhiteSpace(reason) || reason.Length > 1024) return new("Rejected", "A maintenance change reason of 1..1024 characters is required", repository!.Revision);
            candidate = Wire.Decode<ParcelConfiguration>(Wire.Encode(candidate));
            audit = new(Wire.Id(), DateTimeOffset.UtcNow, maintenanceSession.Identity, reason, configuration.Current.Revision, configuration.Current.Revision,
                Wire.Hash(Wire.Encode(Effective)), Wire.Hash(Wire.Encode(candidate)), "Prepared", BeforeEffective: Effective, BeforePending: configuration.Current.Pending, Candidate: candidate);
            repository!.Commit(null, "ConfigurationChangePrepared", Wire.Encode(audit), configurationAudit: audit);
            if (Effective.SchemaVersion >= 3 && candidate.SchemaVersion < 3) throw new InvalidDataException("Topology schema downgrade/flattening is forbidden");
            var errors = definition.Validate(candidate);
            if (errors.Length != 0) throw new InvalidDataException(string.Join("; ", errors));
            RequireSameIdentity(Effective, candidate); candidate = factory.Normalize(candidate);
            using var configurationUse = factory.ProtectConfiguration(candidate);
            audit = audit with { CandidateHash = Wire.Hash(Wire.Encode(candidate)), Candidate = candidate };
            repository.Commit(null, "ConfigurationCandidateNormalized", Wire.Encode(audit), configurationAudit: audit);
            factory.Validate(candidate); configuration.Stage(expectedRevision, candidate);
            Boundary?.Invoke("AfterConfigurationStaged");
            audit = audit with { AfterRevision = configuration.Current.Revision, Status = "PendingRestart", Result = "Validated and saved; Effective retained" };
            repository.Commit(null, "ConfigurationPendingRestart", Wire.Encode(audit), configurationAudit: audit);
            return new("PendingRestart", "Saved; Effective unchanged until controlled restart and application barriers", repository.Revision);
        }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException)
        {
            if (audit is not null)
            {
                audit = audit with { AfterRevision = configuration!.Current.Revision, Status = "Rejected", Result = ex.GetType().Name + ": " + ex.Message };
                repository!.Commit(null, "ConfigurationRejected", Wire.Encode(audit), configurationAudit: audit);
            }
            return new("Rejected", ex.Message, repository?.Revision ?? 0);
        }
        finally { gate.Release(); }
    }
    public Task<BusinessReceipt> ReconnectAsync() => MaintainDevicesAsync(null);
    public Task<BusinessReceipt> ReconnectDeviceAsync(string deviceId) => MaintainDevicesAsync(deviceId);
    public async Task<BusinessReceipt> BackupAsync(string destination)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (repository is null || !repository.Paused || !MaintenanceAuthorized || externalWaits != 0) return new("Rejected", "Pause before reference maintenance backup", repository?.Revision ?? 0);
            destination = Path.GetFullPath(destination);
            RecoveryFiles.RequireOrdinaryAncestors(directory!);
            RecoveryFiles.RequireOrdinaryAncestors(destination);
            var relativeDestination = Path.GetRelativePath(directory!, destination);
            if (!Path.IsPathRooted(relativeDestination) && relativeDestination != ".." && !relativeDestination.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new IOException("Backup must be outside the live application directory");
            var recovery = destination + ".recovery";
            if (Directory.Exists(recovery) || File.Exists(recovery) || File.Exists(destination) || Directory.Exists(destination)) throw new IOException("Backup destination must be new");
            var names = new List<string> { "configuration.json" };
            names.AddRange(RecoveryFiles.Enumerate(directory!, "receipts"));
            names.AddRange(RecoveryFiles.Enumerate(directory!, "images", skipImageLeases: true));
            if (File.Exists(overloadMarker)) names.Add("overload.json");
            RecoveryFiles.ValidateNames(directory!, names);
            foreach (var op in operations.Values.Where(x => x.Image is not null && !x.ImageReleased)) _ = ReadImage(op, op.Image!);
            var expectedFiles = names.ToDictionary(x => x, x => Wire.FileHash(RecoveryFiles.Resolve(directory!, x)), StringComparer.Ordinal);
            // Preserve any interrupted set; only COMPLETED.json makes a set restorable.
            Directory.CreateDirectory(recovery);
            RecoveryFiles.RequireOrdinaryAncestors(recovery);
            RecoveryFiles.RequireOrdinaryAncestors(destination);
            factory.RegisterBackup(destination, Effective);
            if (configuration!.Current.Pending is { } pending) factory.RegisterBackup(destination, pending);
            repository.Backup(destination);
            foreach (var name in names)
            {
                var original = RecoveryFiles.Resolve(directory!, name); var target = RecoveryFiles.Resolve(recovery, name);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!); RecoveryFiles.CopyVerified(original, target, expectedFiles[name]);
            }
            var files = RecoveryFiles.Inventory(recovery);
            var manifest = new ParcelRecoveryManifest(Id, Path.GetFileName(destination), Wire.FileHash(destination), files, DateTimeOffset.UtcNow,
                "Isolated original-deployment restore; reconcile external effects before admission");
            if (System.Text.Encoding.UTF8.GetByteCount(Wire.Encode(manifest)) > RecoveryFiles.MaximumManifestBytes)
                throw new InvalidDataException("Recovery manifest quota exceeded; incomplete backup retained");
            ExtensionStore.AtomicWrite(Path.Combine(recovery, "COMPLETED.json"), manifest);
            return new("BackupCreated", "Consistent database plus configuration/receipt custody; COMPLETED.json identifies the whole recovery set", repository.Revision);
        }
        finally { gate.Release(); }
    }
    public async Task StopAsync(CancellationToken token)
    {
        if (stopped) return;
        Interlocked.Exchange(ref accepting, 0); stopping.Cancel();
        // A timeout leaves every downstream resource owned; a subsequent Stop waits again.
        if (ports is not null)
        {
            List<Exception> sourceFailures = [];
            foreach (var source in ReaderPorts().Cast<object>().Concat(CapabilityPorts().OfType<IWeightSource>()))
                try
                {
                    if (source is IControlledObservationSource reader) await DeviceCallAsync(reader.DeviceId, () => reader.StopAsync(token), token).ConfigureAwait(false);
                    else await DeviceCallAsync(DeviceKey((IWeightSource)source), () => ((IWeightSource)source).StopAsync(token), token).ConfigureAwait(false);
                }
                catch (Exception ex) { sourceFailures.Add(ex); }
            if (sourceFailures.Count != 0)
            {
                if (sourceFailures.All(x => x is OperationCanceledException)) throw new OperationCanceledException(token);
                throw new AggregateException("Source stop failures; all downstream ownership retained", sourceFailures);
            }
        }
        foreach (var queue in positionQueues.Values) queue.Writer.TryComplete();
        if (worker is not null)
        {
            try { await worker.WaitAsync(token).ConfigureAwait(false); }
            catch (Exception ex) when (worker.IsCompleted) { terminalWorkerFailure = ex; }
        }
        if (maintenanceWorker is not null) await maintenanceWorker.WaitAsync(token).ConfigureAwait(false);
        await Task.WhenAll(auxiliary.ToArray()).WaitAsync(token).ConfigureAwait(false);
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
    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        Exception? stopFailure = null;
        try { await StopAsync(CancellationToken.None).ConfigureAwait(false); }
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
        if (journal is not null) await journal.DisposeAsync().ConfigureAwait(false);
        repository?.Dispose(); lease?.Dispose(); stopping.Dispose(); disposed = true;
        if (stopFailure is not null) throw new InvalidOperationException("Application resources released after terminal worker failure", stopFailure);
    }
}

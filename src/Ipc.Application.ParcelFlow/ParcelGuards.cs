using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime
{
    bool SharedReady() => SharedAdmissionBlock() is null;
    ParcelInputBlockReason? SharedAdmissionBlock()
    {
        if (Volatile.Read(ref accepting) != 1 || stopping.IsCancellationRequested) return ParcelInputBlockReason.StationStartingOrStopping;
        if (configuration?.Current.Confirmation?.Required == true) return ParcelInputBlockReason.ConfigurationUnconfirmed;
        if (!RestoreConfirmed) return ParcelInputBlockReason.RestoreUnconfirmed;
        if (ParcelConnectionSetup.BlocksWork(Effective)) return ParcelInputBlockReason.ConnectionNotConfigured;
        if (factory.AdmissionFailure(Effective) is not null) return ParcelInputBlockReason.InstallationNotReady;
        if (!SessionValid) return ParcelInputBlockReason.OperatorSessionExpired;
        if (failure is not null) return ParcelInputBlockReason.RuntimeFault;
        return backupInProgress || configurationSaving ? ParcelInputBlockReason.StationMaintenance : null;
    }
    bool CanResume() =>
        CapacityAvailable && SharedReady() && positions.Keys.Any(p => PositionReady(p) || ManualInputReasons(p, includePause: false).Length == 0);
    // A frozen debug preview has made no external call and owns no production parcel.
    // Production placement release changes lane ownership only. Original receipts and recovery are independent.
    bool ReleasedForNextAdmission(ParcelOperation op) => releasedPlacements.Contains(op.Id) || CanCloseUnsentDebugIntent(op) || definition.AllowsNextAdmissionAfter(op) &&
        !activeStages.Any(key => key.StartsWith(op.Id + ":", StringComparison.Ordinal));
    bool PositionReady(string position) => CameraPositionBlock(position) is null;
    ParcelInputBlockReason? CameraPositionBlock(string position)
    {
        if (SharedAdmissionBlock() is { } shared) return shared;
        var device = positions[position].ReaderDeviceId;
        if (ParcelConnectionSetup.IsDeferred(Effective, device)) return ParcelInputBlockReason.CameraNotConfigured;
        if (!PositionMaintenanceClear(position)) return ParcelInputBlockReason.PositionMaintenance;
        if (HasIdentityConflict(position)) return ParcelInputBlockReason.IdentityConflict;
        if (interruptedPositions.ContainsKey(position)) return ParcelInputBlockReason.InterruptedScan;
        if (overloadedPositions.ContainsKey(position) || overload != 0 && overloadedPositions.IsEmpty) return ParcelInputBlockReason.InputOverload;
        var reader = readers[device];
        if (reader.Health?.IsValid(DateTimeOffset.UtcNow) != true) return ParcelInputBlockReason.ReaderConfirmationExpired;
        if (AdmissionCapabilityBlock(position) is { } capability) return capability;
        if (reader.Source is IObservationInspectionSource && !AutomaticCameraReadsAllowed(device)) return ParcelInputBlockReason.RcsUnavailable;
        var original = operations.Values.FirstOrDefault(x => (x.PositionId ?? "main") == position && !ReleasedForNextAdmission(x) &&
            (NeedsReconciliation(x) || WeightExpired(x) || x.State == ParcelState.WeightTimedOut || !MainTerminated(x) && x.Weight is not null && !WeightReady(x)));
        return original is null ? null : PriorWorkBlock(original);
    }
    static ParcelInputBlockReason PriorWorkBlock(ParcelOperation original) =>
        original.State == ParcelState.WcsUnknown || original.Wcs?.State is StageState.Unknown or StageState.Conflict
            ? ParcelInputBlockReason.PreviousResultUnconfirmed
            : original.State == ParcelState.ResolvingWcs ? ParcelInputBlockReason.PreviousResultPending : ParcelInputBlockReason.PreviousWorkBlocked;
    bool CanContinue(ParcelOperation op) => !IsDebugRequest(op) && CanDispatch(op with { RecoveryRequired = false }) && !MainTerminated(op) && (op.RecoveryRequired && WeightPending(op) || WeightReady(op) || op.Capture is { State: StageState.Prepared, Attempts: 0 }) &&
        (op.RecoveryRequired || op.State == ParcelState.Prepared && op.Wcs is null && op.Plc is null || Enum.GetValues<ParcelStage>().Any(kind => GetStage(op, kind) is { State: StageState.Prepared, Attempts: 0 }));
    bool CanContinueUpload(ParcelOperation op) => op.FrozenConfiguration.Images?.Strategy == UploadStrategy.Attached &&
        CanSendUpload(op) && op.Upload is { State: StageState.Prepared, Attempts: 0 } &&
        (!factory.Requirements(op.FrozenConfiguration.Images.Uploader!).MeasuredWeight || WeightReady(op));
    bool CanSendUpload(ParcelOperation op) => Volatile.Read(ref accepting) == 1 && !ParcelConnectionSetup.BlocksWork(Effective) && op.Upload is { } upload && PortMaintenanceClear(Capability(upload.Request.Binding.InstanceId)) && factory.AdmissionFailure(Effective) is null && !stopping.IsCancellationRequested &&
        !repository!.Paused && failure is null && SessionValid && (Effective.Login is null || currentOperator is not null && currentOperator.Subject == op.Operator?.Subject);
    bool CanAbandon(ParcelOperation op) => repository!.Paused && SessionValid && !MainTerminated(op) &&
        (op.Wcs is null || op.Wcs is { State: StageState.Prepared, Attempts: 0 } || op.Wcs.State is StageState.Accepted or StageState.Rejected) &&
        (op.Plc is null || op.Plc is { State: StageState.Prepared, Attempts: 0 }) &&
        !activeStages.Any(x => x.StartsWith(op.Id + ":", StringComparison.Ordinal)) && !repository.PendingReceipts().Any(x => x.Operation == op.Id) && op.Capture?.State is not (StageState.Unknown or StageState.Conflict);
    bool configurationSaving, resumePriorPause;
    bool CanConfigure => repository?.Paused == true && MaintenanceAuthorized && externalWaits == 0 && !backupInProgress && !configurationSaving;
    bool CanSaveConfiguration => repository is not null && MaintenanceAuthorized && !backupInProgress && !configurationSaving &&
        (definition.AutomaticConfigurationQuiescence || repository.Paused && externalWaits == 0);
    bool CanAdmit(string position) => CameraAdmissionBlock(position) is null;
    ParcelInputBlockReason? CameraAdmissionBlock(string position)
    {
        if (ManagementWorkspaceOpen) return ParcelInputBlockReason.ManagementOpen;
        if (!PositionSupplyEnabled(position)) return ParcelInputBlockReason.PositionDisabled;
        if (!CapacityAvailable) return CapacityBlock;
        if (repository!.Paused) return ParcelInputBlockReason.SoftwarePaused;
        if (CameraPositionBlock(position) is { } positionBlock) return positionBlock;
        if (!ImageCapacityAvailable()) return ParcelInputBlockReason.ImageCapacityUnavailable;
        var original = operations.Values.FirstOrDefault(x => (x.PositionId ?? "main") == position && !ReleasedForNextAdmission(x) && !MainTerminated(x));
        return original is null ? null : PriorWorkBlock(original);
    }
    ParcelInputBlockReason CapacityBlock => storageCapacityFailure is null ? ParcelInputBlockReason.CapacityExhausted : ParcelInputBlockReason.StorageUnavailable;
    bool CanAdmit() => positions.Keys.Any(p => CanAdmit(p) || CanManuallyAdmit(p));
    PositionSnapshot[] PositionSnapshots() => positions.Values.Select(p =>
    {
        var current = DisplayedOperation(p.Id);
        var manual = Id == "sportmaster" ? ManualInputReasons(p.Id) : null;
        var camera = CanAdmit(p.Id);
        var manualReady = manual is { Length: 0 };
        var reasons = new List<string>();
        if (manual is not null && !manualReady && !camera) reasons.AddRange(manual);
        else if (manual is null && !PositionReady(p.Id)) reasons.Add("Position dependency/health or original responsibility blocked");
        foreach (var conflict in identityConflicts.Values.Where(x => x.Positions.Contains(p.Id)))
            reasons.Add("IdentityConflict:" + conflict.Id + "; original=" + conflict.OriginalOperation + "; pause and authenticate maintenance before inspecting this entity; " + conflict.Detail);
        if (!PositionMaintenanceClear(p.Id) && !manualReady) reasons.Add("Position resource maintenance in progress; original intent retained");
        if (current is not null && !MainTerminated(current) && !ReleasedForNextAdmission(current)) reasons.Add("Current entity main result is incomplete");
        if (!CapacityAvailable) reasons.Add("Unresolved capacity exhausted");
        if (repository!.Paused) reasons.Add("Global software pause");
        if (ManagementWorkspaceOpen) reasons.Add("管理中心已打开，自动供包及读码暂停；关闭管理中心后恢复。");
        if (!PositionSupplyEnabled(p.Id)) reasons.Add("本位已请求停用，不再接收新条码。");
        return new PositionSnapshot(p.Id, p.ReaderDeviceId, camera || manualReady, current?.Id, reasons.Distinct().ToArray(), current is null || MainTerminated(current),
            operations.Values.Where(x => (x.PositionId ?? "main") == p.Id).All(IsSettled))
        {
            CanConfirmPlacementRemoval = CanConfirmPlacementRemoval(p.Id, current),
            PlacementClearedAt = confirmedEmptyTrays.TryGetValue(p.Id, out var clearedAt) ? clearedAt : null,
            BarcodeInput = manual is null ? null : new(camera, manualReady, manual),
            Vehicle = PositionVehicle(p.Id, DateTimeOffset.UtcNow),
            SupplyControl = Id == "sportmaster" ? PositionControl(p.Id) : null,
            CameraConnectionSuspended = supplySuspendedReaders.Contains(p.ReaderDeviceId)
        };
    }).ToArray();
    StationVehicleState? PositionVehicle(string position, DateTimeOffset now)
    {
        if (Id != "sportmaster" || !PositionSupplyEnabled(position) || Effective.Wcs is not { } binding ||
            !capabilityHealth.TryGetValue(binding.InstanceId, out var health) || !health.Ready ||
            health.RemoteConfirmation != RemoteConnectionConfirmation.ResponseReceived || health.ConfirmedAt > now || health.ValidUntil <= now) return null;
        var matching = health.StationVehicles.Where(v => v.StationCode == Effective.StationCode &&
            v.CameraCode == binding.Parameters.GetValueOrDefault(position + "-bcr-code") && v.ReceivedAt <= now && v.ValidUntil > now).ToArray();
        return matching.Length == 1 ? matching[0] : null;
    }
    ParcelAvailability Availability() => new(CanAdmit(), CanResume(), CanSaveConfiguration,
        operations.Values.Where(CanContinue).Select(x => x.Id).ToArray(), operations.Values.Where(CanContinueUpload).Select(x => x.Id).ToArray(), operations.Values.Where(CanAbandon).Select(x => x.Id).ToArray());
    async Task<T> ExternalAsync<T>(Func<Task<T>> action)
    {
        if (externalWaits++ == 0) externalDrained = new(TaskCreationOptions.RunContinuationsAsynchronously);
        gate.Release();
        try { return await action().ConfigureAwait(false); }
        finally { await gate.WaitAsync().ConfigureAwait(false); if (--externalWaits == 0) externalDrained!.TrySetResult(); }
    }
    bool WeightReady(ParcelOperation op) => (op.Weight is null || WeightRules.WithinRange(op.Weight.Value, op.FrozenConfiguration.WeightRange)) && (op.WeightPolicy.Source switch
    {
        WeightSource.Disabled => true,
        WeightSource.Configured => op.Weight is { Source: WeightSource.Configured },
        _ => op.Weight is { Source: WeightSource.Measured, MeasurementId: not null, MeasuredAt: { } at } &&
            at >= op.ObservedAt && at <= DateTimeOffset.UtcNow && (op.FrozenConfiguration.WeightValidity == WeightValidity.AtAssociation || DateTimeOffset.UtcNow - at <= TimeSpan.FromMilliseconds(op.WeightPolicy.MaximumAgeMs))
    });
    bool AdmissionCapabilitiesReady(string? position = null) => AdmissionCapabilityBlock(position) is null;
    ParcelInputBlockReason? AdmissionCapabilityBlock(string? position)
    {
        var bindings = position is null ? ParcelBindings.Bound(Effective) : new[] { Effective.Wcs, Effective.Plc, Effective.Login,
            ParcelBindings.Scale(Effective, position), ParcelBindings.Camera(Effective, position), Effective.Images?.Strategy == UploadStrategy.Attached ? null : Effective.Images?.Uploader }.OfType<CapabilityBinding>();
        var now = DateTimeOffset.UtcNow;
        foreach (var binding in bindings)
            if (!capabilityHealth.TryGetValue(binding.InstanceId, out var health) || !health.Ready || health.ConfirmedAt > now || health.ValidUntil <= now)
                return Id == "sportmaster" && binding.InstanceId == Effective.Wcs?.InstanceId ? ParcelInputBlockReason.RcsUnavailable : ParcelInputBlockReason.CapabilityUnavailable;
        return null;
    }
    void ExpireWeights()
    {
        foreach (var op in operations.Values.Where(x => WeightExpired(x) && x.State != ParcelState.WeightTimedOut).ToArray())
            Save(op with { State = ParcelState.WeightTimedOut, Reason = "Same-parcel measurement deadline expired; inspect and explicitly abandon before next parcel" }, "WeightTimedOut", "Waiting deadline is independent of measurement freshness");
    }
    async Task MaintainAsync()
    {
        try
        {
            while (!stopping.IsCancellationRequested)
            {
                await Task.Delay(100, stopping.Token).ConfigureAwait(false);
                await gate.WaitAsync(stopping.Token).ConfigureAwait(false);
                try
                {
                    CheckCameraInspectionLease();
                    CommitEmptyTrays();
                    MaintainParcelPictures();
                    MaintainPositionControls();
                    if (resumePriorPause && CanResume() && maintenanceOperations.Count == 0)
                    { repository!.Commit(null, "AutomaticSupplyRestored", "Retired manual pause restored after startup readiness checks", paused: false); resumePriorPause = false; }
                    if (!backupInProgress) { ExpireWeights(); ReleaseCompletedImages(); ArchiveSettled(); repository!.RetryArchiveCleanup(); TryScheduledBackup(DateTimeOffset.UtcNow); TryAutomaticCleanup(cleanupClock.GetUtcNow()); }
                    RefreshStorageCapacity();
                    WriteStationState("RuntimeMonitor");
                    FlushIngressTimings();
                    auxiliary.RemoveAll(task =>
                    {
                        if (!task.IsCompleted) return false;
                        // Pruning an ended task must not erase its original error
                        // before shutdown observes it. Shutdown can drain other owners
                        // concurrently, so both paths use the same task-keyed ledger.
                        if (task.IsFaulted || task.IsCanceled)
                            terminalWorkerFailures.TryAdd(task, (Exception?)task.Exception ?? new TaskCanceledException(task));
                        return true;
                    });
                    backgroundProgress["maintenance"] = (DateTimeOffset.UtcNow, "CompletedCycle");
                }
                finally { gate.Release(); }
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
        catch (Exception ex) { backgroundProgress["maintenance"] = (DateTimeOffset.UtcNow, "Faulted:" + ex.GetType().Name); failure = "Maintenance worker failed: " + ex.Message; FenceIngress(); if (IsStorageError(ex) || ex is InvalidDataException) StorageFailed(ex, "Maintenance"); }
    }
}

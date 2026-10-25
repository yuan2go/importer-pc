using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime
{
    bool SharedReady() => Volatile.Read(ref accepting) == 1 && configuration?.Current.Confirmation?.Required != true && RestoreConfirmed && Effective.DeferredConnections is not { Length: > 0 } && factory.AdmissionFailure(Effective) is null && SessionValid && failure is null &&
        !stopping.IsCancellationRequested && (overload == 0 || Effective.SchemaVersion >= 3);
    bool CanResume() => CapacityAvailable && SharedReady() && (Effective.SchemaVersion >= 3 ? positions.Keys.Any(PositionReady) : positions.Keys.All(PositionReady));
    bool PositionReady(string position) => SharedReady() && PositionMaintenanceClear(position) && !HasIdentityConflict(position) && !interruptedPositions.ContainsKey(position) && !overloadedPositions.ContainsKey(position) &&
        !(overload != 0 && overloadedPositions.IsEmpty) &&
        readers[positions[position].ReaderDeviceId].Health?.IsValid(DateTimeOffset.UtcNow) == true && AdmissionCapabilitiesReady(position) &&
        !operations.Values.Where(x => (x.PositionId ?? "main") == position).Any(x => NeedsReconciliation(x) || WeightExpired(x) || x.State == ParcelState.WeightTimedOut || !MainTerminated(x) && x.Weight is not null && !WeightReady(x));
    bool CanContinue(ParcelOperation op) => CanDispatch(op with { RecoveryRequired = false }) && !MainTerminated(op) && (op.RecoveryRequired && WeightPending(op) || WeightReady(op) || op.Capture is { State: StageState.Prepared, Attempts: 0 }) &&
        (op.RecoveryRequired || op.State == ParcelState.Prepared && op.Wcs is null && op.Plc is null || Enum.GetValues<ParcelStage>().Any(kind => GetStage(op, kind) is { State: StageState.Prepared, Attempts: 0 }));
    bool CanContinueUpload(ParcelOperation op) => op.FrozenConfiguration.Images?.Strategy == UploadStrategy.Attached &&
        CanSendUpload(op) && op.Upload is { State: StageState.Prepared, Attempts: 0 } &&
        (!factory.Requirements(op.FrozenConfiguration.Images.Uploader!).MeasuredWeight || WeightReady(op));
    bool CanSendUpload(ParcelOperation op) => Volatile.Read(ref accepting) == 1 && Effective.DeferredConnections is not { Length: > 0 } && op.Upload is { } upload && PortMaintenanceClear(Capability(upload.Request.Binding.InstanceId)) && factory.AdmissionFailure(Effective) is null && !stopping.IsCancellationRequested &&
        !repository!.Paused && failure is null && SessionValid && (Effective.Login is null || currentOperator is not null && currentOperator.Subject == op.Operator?.Subject);
    bool CanAbandon(ParcelOperation op) => repository!.Paused && SessionValid && !MainTerminated(op) &&
        (op.Wcs is null || op.Wcs is { State: StageState.Prepared, Attempts: 0 } || op.Wcs.State is StageState.Accepted or StageState.Rejected) &&
        (op.Plc is null || op.Plc is { State: StageState.Prepared, Attempts: 0 }) &&
        !activeStages.Any(x => x.StartsWith(op.Id + ":", StringComparison.Ordinal)) && !repository.PendingReceipts().Any(x => x.Operation == op.Id) && op.Capture?.State is not (StageState.Unknown or StageState.Conflict);
    bool CanConfigure => repository?.Paused == true && MaintenanceAuthorized && externalWaits == 0;
    bool CanAdmit(string position) => CapacityAvailable && !repository!.Paused && PositionReady(position) && ImageCapacityAvailable() &&
        !operations.Values.Any(x => (x.PositionId ?? "main") == position && (Effective.SchemaVersion >= 3 ? !MainTerminated(x) :
            WeightPending(x) || !MainTerminated(x) && x.FrozenConfiguration.Images is not null && x.Capture?.State != StageState.Accepted));
    bool CanAdmit() => positions.Keys.Any(CanAdmit);
    PositionSnapshot[] PositionSnapshots() => positions.Values.Select(p =>
    {
        var current = DisplayedOperation(p.Id);
        var reasons = new List<string>();
        if (!PositionReady(p.Id)) reasons.Add("Position dependency/health or original responsibility blocked");
        foreach (var conflict in identityConflicts.Values.Where(x => x.Positions.Contains(p.Id)))
            reasons.Add("IdentityConflict:" + conflict.Id + "; original=" + conflict.OriginalOperation + "; pause and authenticate maintenance before inspecting this entity; " + conflict.Detail);
        if (!PositionMaintenanceClear(p.Id)) reasons.Add("Position resource maintenance in progress; original intent retained");
        if (current is not null && !MainTerminated(current)) reasons.Add("Current entity main result is incomplete");
        if (!CapacityAvailable) reasons.Add("Unresolved capacity exhausted");
        if (repository!.Paused) reasons.Add("Global software pause");
        return new PositionSnapshot(p.Id, p.ReaderDeviceId, CanAdmit(p.Id), current?.Id, reasons.ToArray(), current is null || MainTerminated(current),
            operations.Values.Where(x => (x.PositionId ?? "main") == p.Id).All(IsSettled));
    }).ToArray();
    ParcelAvailability Availability() => new(CanAdmit(), CanResume(), CanConfigure,
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
    bool AdmissionCapabilitiesReady(string? position = null)
    {
        var bindings = position is null ? ParcelBindings.Bound(Effective) : new[] { Effective.Wcs, Effective.Plc, Effective.Login,
            ParcelBindings.Scale(Effective, position), ParcelBindings.Camera(Effective, position), Effective.Images?.Strategy == UploadStrategy.Attached ? null : Effective.Images?.Uploader }.OfType<CapabilityBinding>();
        return bindings.Where(b => b.InstanceId != Effective.MaintenanceLogin?.InstanceId).All(b =>
            capabilityHealth.TryGetValue(b.InstanceId, out var h) && h.Ready && h.ConfirmedAt <= DateTimeOffset.UtcNow && h.ValidUntil > DateTimeOffset.UtcNow);
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
                try { ExpireWeights(); ReleaseCompletedImages(); RefreshStorageCapacity(); ArchiveSettled(); TryScheduledBackup(DateTimeOffset.UtcNow); auxiliary.RemoveAll(x => x.IsCompleted); backgroundProgress["maintenance"] = (DateTimeOffset.UtcNow, "CompletedCycle"); }
                finally { gate.Release(); }
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
        catch (Exception ex) { backgroundProgress["maintenance"] = (DateTimeOffset.UtcNow, "Faulted:" + ex.GetType().Name); failure = "Maintenance worker failed: " + ex.Message; Interlocked.Exchange(ref accepting, 0); }
    }
}

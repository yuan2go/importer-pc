using Ipc.Application.Contracts;
using Ipc.Platform;
namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime
{
    bool CanResume() => factory.AdmissionFailure(Effective) is null && SessionValid && AdmissionCapabilitiesReady() &&
        !operations.Values.Any(x => x.State == ParcelState.WeightTimedOut || WeightExpired(x) || !MainTerminated(x) && x.Weight is not null && !WeightReady(x)) &&
        failure is null && overload == 0 && !operations.Values.Any(NeedsReconciliation) && readerHealth?.IsValid(DateTimeOffset.UtcNow) == true;
    bool CanContinue(ParcelOperation op) => CanDispatch(op) && !MainTerminated(op) && (WeightReady(op) || op.Capture is { State: StageState.Prepared, Attempts: 0 }) &&
        (op.State == ParcelState.Prepared && op.Wcs is null && op.Plc is null || Enum.GetValues<ParcelStage>().Any(kind => GetStage(op, kind) is { State: StageState.Prepared, Attempts: 0 }));
    bool CanContinueUpload(ParcelOperation op) => op.FrozenConfiguration.Images?.Strategy == UploadStrategy.Attached &&
        CanSendUpload(op) && op.Upload is { State: StageState.Prepared, Attempts: 0 } &&
        (!factory.Requirements(op.FrozenConfiguration.Images.Uploader).MeasuredWeight || WeightReady(op));
    bool CanSendUpload(ParcelOperation op) => factory.AdmissionFailure(Effective) is null && !stopping.IsCancellationRequested &&
        !repository!.Paused && failure is null && SessionValid && (Effective.Login is null || currentOperator is not null && currentOperator.Subject == op.Operator?.Subject);
    bool CanAbandon(ParcelOperation op) => repository!.Paused && SessionValid && !MainTerminated(op) &&
        (op.Wcs is null || op.Wcs is { State: StageState.Prepared, Attempts: 0 } || op.Wcs.State is StageState.Accepted or StageState.Rejected) &&
        (op.Plc is null || op.Plc is { State: StageState.Prepared, Attempts: 0 }) &&
        !activeStages.Any(x => x.StartsWith(op.Id + ":", StringComparison.Ordinal)) && !repository.PendingReceipts().Any(x => x.Operation == op.Id) && op.Capture?.State is not (StageState.Unknown or StageState.Conflict);
    bool CanConfigure => repository?.Paused == true && MaintenanceAuthorized && externalWaits == 0;
    bool CanAdmit() => !repository!.Paused && CanResume() && ImageCapacityAvailable() && !operations.Values.Any(x => WeightPending(x) || !MainTerminated(x) && x.FrozenConfiguration.Images is not null && x.Capture?.State != StageState.Accepted);
    ParcelAvailability Availability() => new(CanAdmit(), CanResume(), CanConfigure,
        operations.Values.Where(CanContinue).Select(x => x.Id).ToArray(), operations.Values.Where(CanContinueUpload).Select(x => x.Id).ToArray(), operations.Values.Where(CanAbandon).Select(x => x.Id).ToArray());
    async Task<T> ExternalAsync<T>(Func<Task<T>> action)
    {
        if (externalWaits++ == 0) externalDrained = new(TaskCreationOptions.RunContinuationsAsynchronously);
        gate.Release();
        try { return await action().ConfigureAwait(false); }
        finally { await gate.WaitAsync().ConfigureAwait(false); if (--externalWaits == 0) externalDrained!.TrySetResult(); }
    }
    bool WeightReady(ParcelOperation op) => op.WeightPolicy.Source switch
    {
        WeightSource.Disabled => true,
        WeightSource.Configured => op.Weight is { Source: WeightSource.Configured },
        _ => op.Weight is { Source: WeightSource.Measured, MeasurementId: not null, MeasuredAt: { } at } &&
            at >= op.ObservedAt && at <= DateTimeOffset.UtcNow && (op.FrozenConfiguration.WeightValidity == WeightValidity.AtAssociation || DateTimeOffset.UtcNow - at <= TimeSpan.FromMilliseconds(op.WeightPolicy.MaximumAgeMs))
    };
    bool AdmissionCapabilitiesReady() => CapabilityPorts().Where(p => !ReferenceEquals(p, ports?.Maintenance) &&
        !(Effective.Images?.Strategy == UploadStrategy.Attached && ReferenceEquals(p, ports?.Uploader)))
        .All(p => capabilityHealth.TryGetValue(p.InstanceId, out var h) && h.Ready && h.ConfirmedAt <= DateTimeOffset.UtcNow && h.ValidUntil > DateTimeOffset.UtcNow);
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
                try { ExpireWeights(); ReleaseCompletedImages(); auxiliary.RemoveAll(x => x.IsCompleted); }
                finally { gate.Release(); }
                foreach (var p in CapabilityPorts())
                {
                    var h = await ProbeHealthAsync(p, stopping.Token).ConfigureAwait(false);
                    await gate.WaitAsync().ConfigureAwait(false);
                    try { capabilityHealth[p.InstanceId] = h; } finally { gate.Release(); }
                }
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
        catch (Exception ex) { failure = "Maintenance worker failed: " + ex.Message; Interlocked.Exchange(ref accepting, 0); }
    }
}

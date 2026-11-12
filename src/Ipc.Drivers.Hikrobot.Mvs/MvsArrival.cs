using Ipc.Platform;
namespace Ipc.Drivers.Hikrobot.Mvs;

internal sealed partial class MvsAcquisition : IProductionArrivalSource
{
    ProductionArrivalCapture? productionArrival, pendingArrival;
    public async Task SetArrivalAsync(ProductionArrivalCapture boundary, CancellationToken token)
    {
        if (!Guid.TryParseExact(boundary.ArrivalGeneration, "N", out _) || !Guid.TryParseExact(boundary.BoundaryId, "N", out _)) throw new ArgumentException("Arrival identity invalid");
        await lifecycle.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposeRequested, this);
            Task completion;
            lock (inspectionGate)
            {
                cacheCutover ??= new(TaskCreationOptions.RunContinuationsAsynchronously);
                afterCacheCutover = inspection;
                pendingArrival = boundary;
                completion = cacheCutover.Task;
            }
            if (worker is null || worker.IsCompleted) ApplyInspectionCacheCutover();
            else
            {
                var completed = await Task.WhenAny(completion, worker).WaitAsync(token).ConfigureAwait(false);
                if (completed == worker && !completion.IsCompleted) RejectInspectionAfterCaptureExit(completion);
            }
            // Cancelling the waiter does not certify that the SDK drained.
            await completion.WaitAsync(token).ConfigureAwait(false);
        }
        finally { lifecycle.Release(); }
    }
}

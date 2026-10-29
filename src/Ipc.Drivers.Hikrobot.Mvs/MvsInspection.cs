using Ipc.Platform;
namespace Ipc.Drivers.Hikrobot.Mvs;

internal sealed partial class MvsAcquisition : IObservationInspectionSource, IParcelFrameSource
{
    Func<InspectedFrame, bool>? parcelFrameReceiver;
    string? parcelFrameFailure;
    public string? ParcelFrameFailure => Volatile.Read(ref parcelFrameFailure);
    public void SetParcelFrameReceiver(Func<InspectedFrame, bool> receiver)
    {
        if (worker is not null || disposeRequested) throw new InvalidOperationException("每日包裹留图接收者只能在原采集启动前安装。");
        if (Interlocked.CompareExchange(ref parcelFrameReceiver, receiver, null) is not null)
            throw new InvalidOperationException("每日包裹留图接收者已安装，禁止第二所有者。");
    }
    void PublishParcelFrame(Func<InspectedFrame, bool> receiver, InspectedFrame frame)
    {
        bool accepted;
        try { accepted = receiver(frame); }
        catch { accepted = false; } // Optional picture custody cannot invent/abort a business read.
        if (!accepted)
        {
            Volatile.Write(ref parcelFrameFailure, "日常同帧留图接收失败，后续留图已停止；业务输入和原请求继续按原守卫处理。");
            Interlocked.CompareExchange(ref parcelFrameReceiver, null, receiver);
        }
    }
    readonly object inspectionGate = new();
    InspectionRegistration? inspection;
    string? inspectionFailure;
    long inspectionGeneration, activeInspectionGeneration;
    readonly List<(long Generation, TaskCompletionSource Completion)> inspectionBarriers = [];
    TaskCompletionSource? cacheCutover;
    InspectionRegistration? afterCacheCutover;
    public string? InspectionFailure => Volatile.Read(ref inspectionFailure);
    public async Task SetInspectionAsync(InspectionRegistration? registration, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposeRequested, this);
        await lifecycle.WaitAsync(token).ConfigureAwait(false);
        try
        {
        Task barrier;
        var applyWithoutWorker = false;
        lock (inspectionGate)
        {
            var clearCache = inspection?.Isolated == true && registration is null || inspection?.Isolated != true && registration?.Isolated == true ||
                registration?.CutoverKey is not null && inspection?.CutoverKey != registration.CutoverKey;
            if (clearCache || cacheCutover is not null)
            {
                cacheCutover ??= new(TaskCreationOptions.RunContinuationsAsynchronously); afterCacheCutover = registration;
                // Retain the isolation tag while the old cycle and SDK cache drain.
                // No diagnostic original is allocated during this transient fence.
                var transient = inspection?.Isolated == true ? inspection : registration;
                inspection = transient is null ? null : transient with { CaptureImages = false, DecodeAllowed = _ => false };
                barrier = cacheCutover.Task; applyWithoutWorker = worker is null || worker.IsCompleted;
            }
            else
            {
            var retired = inspectionGeneration++;
            inspection = registration; inspectionFailure = null;
            if (activeInspectionGeneration != 0 && activeInspectionGeneration <= retired + 1)
            {
                var existing = inspectionBarriers.FirstOrDefault(x => x.Generation == activeInspectionGeneration).Completion;
                if (existing is not null) barrier = existing.Task;
                else
                {
                    var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    inspectionBarriers.Add((activeInspectionGeneration, completion)); barrier = completion.Task;
                }
            }
            else barrier = Task.CompletedTask;
            }
        }
        if (applyWithoutWorker)
        {
            try { await Task.Run(ApplyInspectionCacheCutover, CancellationToken.None).ConfigureAwait(false); }
            catch { await barrier.ConfigureAwait(false); throw; }
        }
        // Cancellation only cancels waiting; it never claims that a native call drained.
        await barrier.WaitAsync(token).ConfigureAwait(false);
        }
        finally { lifecycle.Release(); }
    }
    void ApplyInspectionCacheCutover()
    {
        TaskCompletionSource? completion;
        lock (inspectionGate) completion = cacheCutover;
        if (completion is null) return;
        try
        {
            if (grabbing) { PhaseTo("inspection-sdk-buffer-cutover"); api.ClearBuffers(); }
            lock (inspectionGate)
            {
                inspection = afterCacheCutover; afterCacheCutover = null; cacheCutover = null; inspectionGeneration++; inspectionFailure = null;
            }
            completion.TrySetResult();
        }
        catch (Exception error)
        {
            lock (inspectionGate) { cacheCutover = null; afterCacheCutover = null; inspectionFailure = "SDK缓存切换未完成；保留隔离：" + error.GetType().Name; }
            completion.TrySetException(error); throw;
        }
    }
    (InspectionRegistration? Registration, long Generation) BeginInspectedCycle()
    {
        lock (inspectionGate) { activeInspectionGeneration = inspectionGeneration + 1; return (inspection, activeInspectionGeneration); }
    }
    void EndInspectedCycle(long generation)
    {
        lock (inspectionGate)
        {
            if (activeInspectionGeneration == generation) activeInspectionGeneration = 0;
            foreach (var item in inspectionBarriers.Where(x => x.Generation == generation).ToArray())
            { inspectionBarriers.Remove(item); item.Completion.TrySetResult(); }
        }
    }
    void PublishInspection(InspectionRegistration registration, InspectedFrame frame)
    {
        try
        {
            if (registration.Receive(frame)) return;
            Volatile.Write(ref inspectionFailure, "关联图像采集未接收；会话存在证据缺口，隔离状态不自动解除。");
        }
        catch (Exception error) { Volatile.Write(ref inspectionFailure, "关联证据采集失败：" + error.GetType().Name); }
        lock (inspectionGate)
        {
            // Preserve suppression identity after collection failure. Never silently
            // convert the same isolated frame to a business observation.
            if (inspection?.Id == registration.Id) inspection = registration with { Receive = _ => false, CaptureImages = false };
        }
    }
}

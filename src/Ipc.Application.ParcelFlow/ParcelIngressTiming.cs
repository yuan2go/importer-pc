using System.Diagnostics;
using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime
{
    sealed record IngressTimingSample(string? IngressId, string ObservationId, string DeviceId, string Epoch,
        string SourceEventId, DateTimeOffset ObservedAt, string[] Candidates, string Disposition,
        string? RetainedObservationId, string? OperationId, int QueueDepth, double OldestWaitingMs, double TotalMs, double? SinceDecodeMs, ObservationTiming? Camera,
        Dictionary<string, double> Stages);
    sealed class IngressTimingWindow(string signature)
    {
        public readonly string Signature = signature;
        public long Written = Stopwatch.GetTimestamp();
        public int Count, MaximumDepth;
        public IngressTimingSample? First, Last, Slowest;
        public readonly Dictionary<string, double> Maxima = [];
    }
    readonly object ingressTimingSync = new();
    readonly Dictionary<string, IngressTimingWindow> ingressTimingWindows = [];

    void RecordIngressTiming(string position, Observation observation, string disposition, string? ingress,
        long started, Dictionary<string, double> stages, int depthAtEnqueue = 0, string? retained = null, string? operation = null)
    {
        var lane = ingressLanes[position];
        var waiting = lane.Waiting.Values.ToArray();
        var camera = observation.Timing;
        if (camera is not null)
        {
            stages["CameraAcquire"] = camera.AcquireMs; stages["FrameCopy"] = camera.FrameCopyMs;
            stages["SdkRelease"] = camera.ReleaseMs; stages["DecodeQueue"] = camera.DecodeQueueMs;
            stages["OriginalCapacity"] = camera.OriginalCapacityMs; stages["Decode"] = camera.DecodeMs;
            stages["OriginalCopy"] = camera.OriginalCopyMs;
        }
        var now = Stopwatch.GetTimestamp();
        var sample = new IngressTimingSample(ingress, observation.Id, observation.DeviceId, observation.Epoch,
            observation.SourceEventId, observation.ObservedAt, observation.Codes, disposition, retained, operation, waiting.Length,
            waiting.Length == 0 ? 0 : Math.Max(0, (DateTimeOffset.UtcNow - waiting.Min()).TotalMilliseconds),
            Stopwatch.GetElapsedTime(started, now).TotalMilliseconds,
            observation.DecodedTicks > 0 && observation.DecodedTicks <= now ? Stopwatch.GetElapsedTime(observation.DecodedTicks, now).TotalMilliseconds : null,
            camera, stages);
        KeyValuePair<string, double>? slowestStage = stages.Count == 0 ? null : stages.MaxBy(p => p.Value);
        var signature = Wire.Hash(Wire.Encode(new { observation.Epoch, observation.Codes, disposition, retained, operation,
            SlowStage = slowestStage?.Value >= 1000 ? slowestStage?.Key : null }));
        lock (ingressTimingSync)
        {
            var changed = !ingressTimingWindows.TryGetValue(position, out var window) || window.Signature != signature;
            if (changed)
            {
                if (window is not null) FlushIngressTiming(position, window);
                ingressTimingWindows[position] = window = new(signature);
            }
            window!.Count++; window.First ??= sample; window.Last = sample;
            if (window.Slowest is null || sample.TotalMs > window.Slowest.TotalMs) window.Slowest = sample;
            window.MaximumDepth = Math.Max(window.MaximumDepth, Math.Max(sample.QueueDepth, depthAtEnqueue));
            foreach (var stage in stages) window.Maxima[stage.Key] = Math.Max(window.Maxima.GetValueOrDefault(stage.Key), stage.Value);
            if (changed || Stopwatch.GetElapsedTime(window.Written) >= TimeSpan.FromSeconds(5)) FlushIngressTiming(position, window);
        }
    }
    void FlushIngressTiming(string position, IngressTimingWindow window)
    {
        if (window.Count == 0) return;
        WriteRuntimeLog("扫码节奏", "扫码阶段耗时与队列汇总", new
        {
            PositionId = position, window.Count, WindowMs = Stopwatch.GetElapsedTime(window.Written).TotalMilliseconds,
            window.MaximumDepth, MaximumStageMs = new Dictionary<string, double>(window.Maxima),
            First = TimingIdentity(window.First!), Last = TimingIdentity(window.Last!), window.Slowest,
            Workers = WorkerProgress(),
            Meaning = "阶段耗时使用本机单调时钟；CameraAcquire含等待下一帧，原生Decode包含在SDK调用内，阶段不能直接相加。Total为进入Application至处理结束；曝光时延未标定。QueueDepth含等待业务锁的输入，不含采集端等待容量的帧；OldestWaitingMs由记录时间估算。仅观察不创建或重放请求。"
        });
        window.Count = window.MaximumDepth = 0; window.First = window.Last = window.Slowest = null;
        window.Maxima.Clear(); window.Written = Stopwatch.GetTimestamp();
    }
    static object TimingIdentity(IngressTimingSample sample) => new
    {
        sample.IngressId, sample.ObservationId, sample.DeviceId, sample.Epoch, sample.SourceEventId, sample.ObservedAt,
        sample.Candidates, sample.Disposition, sample.RetainedObservationId, sample.OperationId
    };
    void FlushIngressTimings(bool force = false)
    {
        lock (ingressTimingSync)
            foreach (var pair in ingressTimingWindows)
                if (force || Stopwatch.GetElapsedTime(pair.Value.Written) >= TimeSpan.FromSeconds(5)) FlushIngressTiming(pair.Key, pair.Value);
    }

    bool CoalescePausedObservation(string position, Observation observation, IngressLane lane, long entered, double laneWaitMs)
    {
        // A committed paused observation is presence evidence, not another parcel.
        // Never jump over a pending input, a reconnect, or the transition to resumed admission.
        if (repository?.Paused != true || !lane.Waiting.IsEmpty || Interlocked.Read(ref lane.ProcessingTicks) != 0 ||
            observation.CaptureSessionId is not null || !ExtensionSchema.IsId(observation.AdmissionId) || observation.ObservedAt > DateTimeOffset.UtcNow ||
            DateTimeOffset.UtcNow - observation.ObservedAt > TimeSpan.FromMilliseconds(Effective.TimeoutMs) ||
            string.IsNullOrWhiteSpace(observation.SourceEventId) || observation.SourceEventId.Length > 256 || observation.SourceEventId.Any(char.IsControl) ||
            ContinuingUnadmittedPlacement(position, observation) is not { } retained) return false;
        if (observation.Sequence <= retained.Observation.Sequence || observation.SourceEventId == retained.Observation.SourceEventId ||
            observation.AdmissionId == retained.Observation.AdmissionId ||
            definition.SelectBarcode(Effective, observation).SelectedCode != observation.Codes[0]) return false;
        lock (placements)
        {
            if (repository.Paused != true || !placements.TryGetValue(position, out var placement) ||
                placement.EndedAt is not null || placement.UnadmittedInput != retained) return false;
            if (!readers[observation.DeviceId].AcceptSequence(observation)) return false;
            if (observation.ObservedAt > placement.LastSeen) placement.LastSeen = observation.ObservedAt;
            placement.EmptyFrames = 0; placement.EmptySince = null;
        }
        RecordPresentationRead(position, observation, retainedInput: retained.Observation.Id);
        ReleaseIngressAcquisition(observation);
        Interlocked.Increment(ref lane.ObservedOnly);
        RecordIngressTiming(position, observation, "PausedSamePlacementObservation", null, entered,
            new() { ["LaneWait"] = laneWaitMs }, retained: retained.Observation.Id);
        return true;
    }
}

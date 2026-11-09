using System.Diagnostics;
using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime
{
    sealed class InputLogWindow(string signature)
    {
        internal readonly string Signature = signature;
        internal long Written = Stopwatch.GetTimestamp();
        internal int Count;
        internal ParcelInputSnapshot? First, Last;
    }
    readonly Dictionary<string, InputLogWindow> inputLogWindows = [];

    void LogInputSummary(ParcelInputSnapshot input)
    {
        var signature = Wire.Hash(Wire.Encode(new
        {
            input.Outcome,
            input.OperationId,
            input.Reason,
            input.Observation.Epoch,
            input.BarcodeSelection,
            Codes = input.Observation.Codes.Order(StringComparer.Ordinal).ToArray()
        }));
        if (!inputLogWindows.TryGetValue(input.PositionId, out var window) || window.Signature != signature)
        {
            if (window is not null) FlushInputLogSummary(window);
            WriteInputLog(input, input, 1);
            inputLogWindows[input.PositionId] = new(signature);
            return;
        }
        window.First ??= input; window.Last = input; window.Count++;
        if (Stopwatch.GetElapsedTime(window.Written) >= TimeSpan.FromSeconds(5)) FlushInputLogSummary(window);
    }
    void FlushInputLogSummary(InputLogWindow window)
    {
        if (window.First is not null && window.Last is not null && window.Count > 0)
            WriteInputLog(window.First, window.Last, window.Count);
        window.First = window.Last = null; window.Count = 0; window.Written = Stopwatch.GetTimestamp();
    }
    void FlushInputLogSummaries()
    {
        foreach (var window in inputLogWindows.Values) FlushInputLogSummary(window);
    }
    void WriteInputLog(ParcelInputSnapshot first, ParcelInputSnapshot last, int count) => WriteRuntimeLog("扫码与准入", "输入处理完成", new
    {
        last.PositionId,
        last.Outcome,
        last.OperationId,
        last.Observation.AdmissionId,
        last.Observation.SourceEventId,
        ObservationId = last.Observation.Id,
        last.Observation.DeviceId,
        last.Observation.Epoch,
        CandidateCount = last.Observation.Codes.Length,
        Candidates = last.Observation.Codes,
        Formats = last.Observation.Symbols?.Select(s => s.Format).ToArray(),
        Barcode = last.BarcodeSelection?.SelectedCode,
        Selection = last.BarcodeSelection,
        SourceKind = last.Observation.OperatorInput is null ? "相机" : "人工输入",
        last.Reason,
        ObservationCount = count,
        FirstObservedAt = first.Observation.ObservedAt,
        LastObservedAt = last.Observation.ObservedAt,
        FirstSourceEventId = first.Observation.SourceEventId,
        last.RecordedAt,
        ObservationAgeMs = (last.RecordedAt - last.Observation.ObservedAt).TotalMilliseconds,
        QueuedObservationTimeoutMs = definition.QueuedObservationTimeoutMs(Effective),
        DisplayObservationId = ContinuingUnadmittedPlacement(last.PositionId, last.Observation)?.Observation.Id,
        UnadmittedOriginalImageAvailable = ContinuingUnadmittedPlacement(last.PositionId, last.Observation) is { } retained &&
            blockedInputPictures.TryGetValue(last.PositionId, out var picture) && picture.Metadata.ObservationId == retained.Observation.Id
    }, last.Outcome is ParcelInputOutcome.Blocked or ParcelInputOutcome.Rejected ? "警告" : "信息", last.OperationId);
}

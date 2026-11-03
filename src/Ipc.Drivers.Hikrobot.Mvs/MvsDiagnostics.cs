using System.Diagnostics;
using Ipc.Platform;
namespace Ipc.Drivers.Hikrobot.Mvs;

internal sealed partial class MvsAcquisition : IObservationDiagnosticsSource
#if !MVS_ACQUISITION_EXTENSION
    , IObservationPerformanceSource
#endif
{
    long acquireCalls, noData, decodeStarted, decodeCompleted, decodedCandidates, rejected, lastFrameTicks;
    DateTimeOffset? lastFrameAt, lastDecodeAt;
    bool decodePaused;
    DecodeWindow decodeWindow = new();
    long rejectedFrames, rejectedDecodes, originalFrameCopies;
    bool waitingOriginalCapacity;
    string? lastFrameRejection;
    void RejectFrame(uint frame, MvsFrameRejection error, bool decoding)
    {
        lock (gate)
        {
            rejectedFrames++; if (decoding) rejectedDecodes++;
            lastFrameRejection = $"frame={frame}; reason={error.Reason}; {error.Message}";
        }
        PublishReadFeedback(frame, DateTimeOffset.UtcNow, Stopwatch.GetTimestamp(), 0, rejected: true);
    }
    void ResetDiagnostics()
    {
        decodeWindow = new();
        // Called with gate held, before starting the acquisition worker for a new epoch.
        acquireCalls = noData = decodeStarted = decodeCompleted = decodedCandidates = rejected = lastFrameTicks = 0;
        lastFrameAt = lastDecodeAt = null; decodePaused = false;
        rejectedFrames = rejectedDecodes = originalFrameCopies = 0; waitingOriginalCapacity = false; lastFrameRejection = null;
        Interlocked.Exchange(ref supersededDecodeFrames, 0);
    }
    public ObservationDiagnostics ReadDiagnostics()
    {
        lock (gate)
        {
            double? age = lastFrameTicks == 0 ? null : Stopwatch.GetElapsedTime(lastFrameTicks).TotalMilliseconds;
            var state = failure.Length != 0 ? "Faulted" : phase is "stopped" or "stopping" or "not-started" ? "Stopped" :
                waitingOriginalCapacity ? "WaitingForOriginalCapacity" : decodeStarted > decodeCompleted + rejectedDecodes ? "Decoding" : frames == 0 ? "WaitingForFrame" : age > 3000 ? "FrameStale" :
                decodePaused ? "DecodePaused" : decoder is null ? "DecoderUnavailable" : decodeCompleted == 0 ? "Acquiring" :
                decodedCandidates == 0 ? "NoBarcode" : "Reading";
            return new(DeviceId, epoch, phase, state, acquireCalls, noData, frames, decodeStarted, decodeCompleted,
                emptyFrames, decodedCandidates, recognized, rejected, decodePaused, lastFrameAt, age, lastDecodeAt,
                failure.Length == 0 ? null : failure) { Window = decodeWindow.Read(), SupersededDecodeFrames = Interlocked.Read(ref supersededDecodeFrames)
#if !MVS_ACQUISITION_EXTENSION
                , RejectedFrames = rejectedFrames, LastFrameRejection = lastFrameRejection, OriginalFrameCopies = originalFrameCopies,
                WaitingForOriginalCapacity = waitingOriginalCapacity,
                SdkFrameNumberGaps = skippedVideoFrames, SdkGapEvents = videoGapEvents
#endif
            };
        }
    }
#if !MVS_ACQUISITION_EXTENSION
    public object ReadPerformance()
    {
        lock (gate) return new { Epoch = epoch, Acquisition = Timings.Snapshot(), Decoder = DecoderTimings,
            DecoderConfiguration = (decoder as SoftwareDecoder)?.DiagnosticConfiguration };
    }
#endif
}

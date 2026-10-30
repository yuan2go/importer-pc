using System.Diagnostics;
using Ipc.Platform;
namespace Ipc.Drivers.Hikrobot.Mvs;

internal sealed partial class MvsAcquisition : IObservationDiagnosticsSource
{
    long acquireCalls, noData, decodeStarted, decodeCompleted, decodedCandidates, rejected, lastFrameTicks;
    DateTimeOffset? lastFrameAt, lastDecodeAt;
    bool decodePaused;
    void ResetDiagnostics()
    {
        // Called with gate held, before starting the acquisition worker for a new epoch.
        acquireCalls = noData = decodeStarted = decodeCompleted = decodedCandidates = rejected = lastFrameTicks = 0;
        lastFrameAt = lastDecodeAt = null; decodePaused = false;
    }
    public ObservationDiagnostics ReadDiagnostics()
    {
        lock (gate)
        {
            double? age = lastFrameTicks == 0 ? null : Stopwatch.GetElapsedTime(lastFrameTicks).TotalMilliseconds;
            var state = failure.Length != 0 ? "Faulted" : phase is "stopped" or "stopping" or "not-started" ? "Stopped" :
                decodeStarted > decodeCompleted ? "Decoding" : frames == 0 ? "WaitingForFrame" : age > 3000 ? "FrameStale" :
                decodePaused ? "DecodePaused" : decoder is null ? "DecoderUnavailable" : decodeCompleted == 0 ? "Acquiring" :
                decodedCandidates == 0 ? "NoBarcode" : "Reading";
            return new(DeviceId, epoch, phase, state, acquireCalls, noData, frames, decodeStarted, decodeCompleted,
                emptyFrames, decodedCandidates, recognized, rejected, decodePaused, lastFrameAt, age, lastDecodeAt,
                failure.Length == 0 ? null : failure);
        }
    }
}

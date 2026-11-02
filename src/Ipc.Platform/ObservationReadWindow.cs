namespace Ipc.Platform;

// Business-owned pacing of normal recognition. Acquisition/preview stay running.
// Bind before startup. The predicate must be bounded, lock-free and side-effect free;
// captureTicks is the host monotonic timestamp of this frame, never camera time.
// Isolated inspection owns its separate decode window and bypasses this normal window.
public interface IObservationReadWindowSource
{
    void BindReadWindow(Func<long, bool> canRead);
}

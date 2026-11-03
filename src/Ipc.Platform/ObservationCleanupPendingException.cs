namespace Ipc.Platform;

// A transient transport error interrupted native teardown. The original source
// still owns its handle/lease: retry Stop before Start, never assume release or
// treat this as a clean offline startup dependency.
public sealed class ObservationCleanupPendingException(string message, Exception inner)
    : IOException(message, inner);

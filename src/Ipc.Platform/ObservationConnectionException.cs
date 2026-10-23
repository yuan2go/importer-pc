namespace Ipc.Platform;

// Only a source that has fenced/cleaned its failed transport may report this
// recoverable position dependency. SDK/resource/programming failures are not
// implicitly eligible for partial application startup.
public sealed class ObservationConnectionException(string category, Exception inner)
    : IOException(category, inner);

namespace Ipc.Platform;

// Opt-in transport recovery. Disconnected permits a serialized Stop/Start with a
// new observation epoch; Faulted never does. Stop must retain ownership on error.
// This contract grants no business admission, configuration change or replay.
public interface IAutomaticReconnectObservationSource : IControlledObservationSource { }

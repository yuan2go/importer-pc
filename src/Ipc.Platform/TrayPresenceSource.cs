namespace Ipc.Platform;

// Explicit empty-tray teaching, never inferred from a decoder or first frame.
// The reference belongs to one camera session and is discarded on reconnect.
public interface ITrayPresenceSource
{
    string TrayPresenceStatus { get; }
    TrayPresenceModelSnapshot? ReadTrayPresenceModel() => null;
    void CancelEmptyTrayCalibration() { }
    void CalibrateEmptyTray();
}

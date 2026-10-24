namespace Ipc.Platform;

// Observational only. UI liveness must never issue business commands or grant admission.
public interface IRuntimeProgress
{
    void RegisterUi(string id);
    void ReportUiProgress(string id, double dispatchMilliseconds);
    void UnregisterUi(string id);
}

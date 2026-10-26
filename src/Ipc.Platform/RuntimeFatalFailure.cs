namespace Ipc.Platform;

// Infrastructure notification only. The application fences admission before
// publishing; only the process owner terminates. No business retry is implied.
public sealed record RuntimeFatalFailure(string Category, bool PreventRestart, Exception Error,
    string Instance, string Epochs, string RecentState);
public interface IRuntimeFatalSource
{
    Task<RuntimeFatalFailure> FatalFailure { get; }
}
public static class RuntimeFatalMonitor
{
    public static async Task RunAsync(IRuntimeFatalSource source, CrashDiagnostics diagnostics, CancellationToken cancellationToken)
    {
        RuntimeFatalFailure failure;
        try { failure = await source.FatalFailure.WaitAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
        diagnostics.SetContext(failure.Instance, failure.Epochs, failure.RecentState);
        diagnostics.Fatal(failure.Category, failure.Error, failure.PreventRestart);
    }
}

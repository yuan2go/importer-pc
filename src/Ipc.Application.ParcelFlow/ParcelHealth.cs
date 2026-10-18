using Ipc.Application.Contracts;
namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime
{
    // Only this boundary knows both the configured budget and the lifecycle token.
    // A port receives one token and must not guess why that token was cancelled.
    async Task<CapabilityHealth> ProbeHealthAsync(ICapabilityPort port, CancellationToken caller)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(caller, stopping.Token);
        budget.CancelAfter(Effective.TimeoutMs);
        try
        {
            var result = await port.HealthAsync(budget.Token).ConfigureAwait(false);
            caller.ThrowIfCancellationRequested(); stopping.Token.ThrowIfCancellationRequested();
            // A provider can return a cached success after its request budget elapsed.
            return budget.IsCancellationRequested ? Unavailable("Health check timed out") : result;
        }
        catch (OperationCanceledException) when (!caller.IsCancellationRequested && !stopping.IsCancellationRequested && budget.IsCancellationRequested)
        { return Unavailable("Health check timed out"); }
        catch (Exception ex) when (ex is IOException or TimeoutException)
        {
            caller.ThrowIfCancellationRequested(); stopping.Token.ThrowIfCancellationRequested();
            return Unavailable("Health unavailable: " + ex.GetType().Name);
        }
        CapabilityHealth Unavailable(string reason)
        { var now = DateTimeOffset.UtcNow; return new(port.InstanceId, false, now, now, reason); }
    }
}

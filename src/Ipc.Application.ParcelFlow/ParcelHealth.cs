using Ipc.Application.Contracts;
namespace Ipc.Application.ParcelFlow;

public sealed partial class ParcelRuntime
{
    async Task MonitorAsync(ICapabilityPort port)
    {
        try
        {
            while (!stopping.IsCancellationRequested)
            {
                await Task.Delay(100, stopping.Token).ConfigureAwait(false);
                var epoch = portEpochs.GetValueOrDefault(port.InstanceId);
                var health = await ProbeHealthAsync(port, stopping.Token).ConfigureAwait(false);
                await gate.WaitAsync(stopping.Token).ConfigureAwait(false);
                try { if (epoch == portEpochs.GetValueOrDefault(port.InstanceId)) capabilityHealth[port.InstanceId] = health; } finally { gate.Release(); }
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
        catch (Exception ex) { failure = "Capability monitor failed: " + port.InstanceId + ": " + ex.Message; Interlocked.Exchange(ref accepting, 0); }
    }
    // Only this boundary knows both the configured budget and the lifecycle token.
    // A port receives one token and must not guess why that token was cancelled.
    async Task<CapabilityHealth> ProbeHealthAsync(ICapabilityPort port, CancellationToken caller)
    {
        var epoch = portEpochs.GetValueOrDefault(port.InstanceId);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(caller, stopping.Token);
        budget.CancelAfter(Effective.TimeoutMs);
        try
        {
            var result = await port.HealthAsync(budget.Token).ConfigureAwait(false);
            caller.ThrowIfCancellationRequested(); stopping.Token.ThrowIfCancellationRequested();
            // A provider can return a cached success after its request budget elapsed.
            if (epoch != portEpochs.GetValueOrDefault(port.InstanceId)) return Unavailable("Old maintenance epoch health discarded");
            if (budget.IsCancellationRequested) return Unavailable("Health check timed out");
            var now = DateTimeOffset.UtcNow;
            if (result.InstanceId != port.InstanceId || result.ConfirmedAt > now || result.ValidUntil <= result.ConfirmedAt)
                return Unavailable("Health identity/time invalid for original port " + port.InstanceId);
            return result;
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

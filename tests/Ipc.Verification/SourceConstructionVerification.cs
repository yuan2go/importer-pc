using Ipc.Platform;
using Ipc.Product;
using Microsoft.Extensions.Logging.Abstractions;

static class SourceConstructionVerification
{
    public static async Task Run(BootOptions boot, StationConfig config)
    {
        var blockedStop = new ProductProbeSource(config.Devices[0].DeviceId, "stop-pending");
        var partial = new ProductRuntime(boot, NullLogger<ProductRuntime>.Instance,
            (binding, _) => binding.Role == "left" ? blockedStop : throw new InvalidOperationException("Synthetic second source construction failure"));
        try
        {
            await partial.StartAsync(CancellationToken.None);
            Assert(partial.Diagnostics.Any(x => x.Contains("Startup NotReady")) && partial.HeldResources.Length > 0 && !blockedStop.Disposed,
                "Partial factory cleanup preserves returned source and final resources");
            try { await partial.DisposeAsync(); throw new Exception("Expected retained construction ownership"); } catch (AggregateException) { }
            Assert(partial.HeldResources.Length > 0 && !blockedStop.Disposed && blockedStop.StopCount == 1, "Pending construction Stop reuses original task without releasing leases");
        }
        finally { blockedStop.Release(); await partial.DisposeAsync(); }
        Assert(blockedStop.DisposeCount == 1 && partial.HeldResources.Length == 0, "Late factory cleanup disposes exactly once and releases resources");

        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sources = config.Devices.ToDictionary(x => x.Role, x => new ProductProbeSource(x.DeviceId, "none"));
        var starting = new ProductRuntime(boot, NullLogger<ProductRuntime>.Instance, (binding, _) =>
        {
            if (binding.Role == "right") { entered.TrySetResult(); release.Task.GetAwaiter().GetResult(); }
            return sources[binding.Role];
        });
        var startup = starting.StartAsync(CancellationToken.None);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            try { await starting.DisposeAsync(); throw new Exception("Expected active factory ownership"); } catch (AggregateException) { }
            Assert(starting.HeldResources.Length > 0 && starting.SourceStartAttempts == 0 && sources.Values.All(x => !x.Disposed),
                "Concurrent stop cannot release dependencies or launch cleanup racing a live source constructor");
        }
        finally { release.TrySetResult(); await startup; await starting.DisposeAsync(); }
        Assert(starting.HeldResources.Length == 0 && starting.SourceStartAttempts == 0 && sources.Values.All(x => x.DisposeCount == 1),
            "Late constructor observes cancellation before source start and completes owned cleanup");
        Console.WriteLine("PASS E12 partial source factory failure and concurrent stop ownership");
    }
    static void Assert(bool value, string reason) { if (!value) throw new Exception("E12: " + reason); }
}

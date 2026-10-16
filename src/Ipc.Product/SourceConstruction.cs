using Ipc.Platform;

namespace Ipc.Product;

public sealed partial class ProductRuntime
{
    async Task CleanupConstructionAsync()
    {
        List<Exception> errors = [];
        foreach (var source in constructionSources.Reverse())
        {
            try
            {
                if (!constructionStops.TryGetValue(source, out var stop) || stop.IsFaulted || stop.IsCanceled)
                    constructionStops[source] = stop = Task.Run(() => source.StopAsync(CancellationToken.None));
                await stop.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                if (!constructionDisposals.TryGetValue(source, out var disposal) || disposal.IsFaulted || disposal.IsCanceled)
                    constructionDisposals[source] = disposal = Task.Run(async () => await source.DisposeAsync().ConfigureAwait(false));
                await disposal.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                constructionSources = constructionSources.Where(x => !ReferenceEquals(x, source)).ToArray();
            }
            catch (Exception ex) { errors.Add(new InvalidOperationException("Partially constructed source ownership retained: " + source.DeviceId, ex)); }
        }
        if (errors.Count > 0) throw new AggregateException("Source construction cleanup retains failures and dependencies", errors);
    }
}

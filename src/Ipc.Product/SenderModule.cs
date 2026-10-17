using Ipc.Platform;
using Ipc.Supply;
namespace Ipc.Product;

sealed class SenderModule(StationApplication application, IIntegrationPort port) : IRuntimeModule, IRuntimeOwnership
{
    public string Id => "sender";
    public string[] Dependencies => ["observations"];
    readonly CancellationTokenSource stop = new();
    Task? worker, portDisposal;
    bool portDisposed, stopDisposed;
    public bool HasActiveWork => worker is { IsCompleted: false } || portDisposal is { IsCompleted: false };
    public bool HasUnreleasedResources => HasActiveWork || !portDisposed || !stopDisposed;
    public string[] OwnershipDiagnostics => worker is { IsCompleted: false } ? ["Sender worker still running; integration port retained"] :
        portDisposal is { IsCompleted: false } ? ["Integration port disposal has not terminated"] : [];
    public Task StartAsync(CancellationToken token)
    {
        worker = Task.Run(async () =>
        {
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    await application.SetWcsHealthAsync(await port.HealthAsync(stop.Token));
                    var claimed = await application.ClaimAsync();
                    if (claimed is null) { await Task.Delay(100, stop.Token); continue; }
                    var (request, attempt) = claimed.Value;
                    if (!await application.BeforeSendAsync(request, attempt)) continue;
                    try { var response = await port.ExecuteAsync(request, stop.Token); await application.CompleteAsync(request, attempt, response); }
                    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or InvalidDataException)
                    { await application.UnknownAsync(request, attempt, ex.GetType().Name + ": transport outcome not authoritative"); }
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            catch (Exception ex) { application.Latch("SenderFault", ex.Message); throw; }
        }, CancellationToken.None);
        return Task.CompletedTask;
    }
    public async Task StopAsync(CancellationToken token)
    {
        if (!stopDisposed) stop.Cancel();
        if (worker is not null) await worker.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
    }
    public async ValueTask DisposeAsync()
    {
        List<Exception> errors = [];
        try { await StopAsync(CancellationToken.None).ConfigureAwait(false); } catch (Exception ex) { errors.Add(ex); }
        if (worker is { IsCompleted: false }) errors.Add(new InvalidOperationException("Integration port disposal deferred until sender terminates"));
        else
        {
            if (port is IntegrationRuntime) portDisposed = true; // Product retains shared query/codec ownership until every module terminates.
            if (!portDisposed)
                try
                {
                    if (portDisposal is null || portDisposal.IsFaulted || portDisposal.IsCanceled)
                        portDisposal = Task.Run(async () => await port.DisposeAsync().ConfigureAwait(false), CancellationToken.None);
                    await portDisposal.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); portDisposed = true;
                }
                catch (Exception ex) { errors.Add(ex); }
            if (!stopDisposed) try { stop.Dispose(); stopDisposed = true; } catch (Exception ex) { errors.Add(ex); }
        }
        if (errors.Count != 0) throw new AggregateException("Sender disposal failures", errors);
    }
}

using System.Net;
using System.Net.Http.Json;
using Ipc.Platform;
using Ipc.Supply;

namespace Ipc.Reference.IntegrationExtension;

public sealed class IntegrationModule : IIntegrationExtension
{
    public IControlledIntegration Create(IntegrationExtensionContext context) => new ReferenceIntegration(context);
}

sealed class ReferenceIntegration : IControlledIntegration
{
    readonly IntegrationExtensionContext context;
    readonly SimulationRequestContract codec;
    readonly HttpClient client;
    readonly CancellationTokenSource stopping = new();
    readonly object gate = new();
    TaskCompletionSource drained = Completed();
    Task? stopTask;
    bool disposed;
    int active;
    string? failure;
    public string InstanceId => context.InstanceId;
    public string Contract => "SIMULATION-v1";
    public IntegrationDiagnostic Diagnostic { get { lock (gate) return new(InstanceId, disposed ? "Disposed" : stopping.IsCancellationRequested ? "Stopping" : "Available", active, failure); } }
    static TaskCompletionSource Completed() { var t = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); t.SetResult(); return t; }
    public ReferenceIntegration(IntegrationExtensionContext context)
    {
        if (context.Mode != RunMode.Simulation || context.Target.Contract != "SIMULATION-v1" || context.TimeoutMs is < 100 or > 30000)
            throw new InvalidDataException("Reference integration requires the explicit simulation contract and bounded timeout");
        _ = Endpoint(context.Target, "health");
        if (context.Parameters.GetValueOrDefault("fault.fail-create", "false") == "true") throw new InvalidOperationException("Synthetic integration construction fault");
        this.context = context; codec = new(context);
        client = new(new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false, MaxConnectionsPerServer = 2 }) { Timeout = Timeout.InfiniteTimeSpan };
    }
    static Uri Endpoint(IntegrationBinding binding, string path)
    {
        if (binding.Contract != "SIMULATION-v1" || !Uri.TryCreate(binding.Endpoint, UriKind.Absolute, out var uri) ||
            uri.Scheme != "http" || uri.Host != "127.0.0.1" || uri.AbsolutePath != "/" || uri.UserInfo != "" || uri.Query != "" || uri.Fragment != "")
            throw new InvalidDataException("Only literal IPv4 loopback reference receiver is supported");
        return new(uri, path);
    }
    void Original(BusinessRequest request)
    {
        if (request.Binding != context.Target || Wire.Hash(request.Payload) != request.PayloadHash)
            throw new InvalidDataException("Original target/hash differs from this integration instance");
    }
    async Task<T> Invoke<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellation)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (stopping.IsCancellationRequested) throw new InvalidOperationException("Integration is stopping");
            if (active++ == 0) drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, stopping.Token);
        linked.CancelAfter(context.TimeoutMs);
        try { return await call(linked.Token).ConfigureAwait(false); }
        catch (Exception ex) { lock (gate) failure = ex.GetType().Name; throw; }
        finally { lock (gate) if (--active == 0) drained.TrySetResult(); }
    }
    public string Encode(RequestIntent intent, string scenario) => codec.Encode(intent, scenario);
    public RequestAuthority Authority(BusinessRequest request) { Original(request); return codec.Authority(request); }
    public void ValidateResponse(BusinessRequest request, IntegrationResponse response) { Original(request); codec.ValidateResponse(request, response); }
    public Task<IntegrationResponse> ExecuteAsync(BusinessRequest request, CancellationToken token)
    {
        Original(request);
        return Invoke(async cancellation =>
        {
            using var response = await client.PostAsJsonAsync(Endpoint(request.Binding, "requests"), new RequestEnvelope(request.Id, request.PayloadHash, request.Binding.Contract, request.Payload), Wire.Json, cancellation);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<IntegrationResponse>(Wire.Json, cancellation) ?? throw new InvalidDataException("Empty reference response");
        }, token);
    }
    public Task<IntegrationResponse?> QueryAsync(BusinessRequest request, CancellationToken token)
    {
        Original(request);
        return Invoke<IntegrationResponse?>(async cancellation =>
        {
            using var response = await client.GetAsync(Endpoint(request.Binding, "requests/" + request.Id), cancellation);
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.NotImplemented) return null;
            response.EnsureSuccessStatusCode(); return await response.Content.ReadFromJsonAsync<IntegrationResponse>(Wire.Json, cancellation);
        }, token);
    }
    public Task<bool> HealthAsync(CancellationToken token) => Invoke(async cancellation =>
    {
        try { return await client.GetStringAsync(Endpoint(context.Target, "health"), cancellation) == Contract; }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException) { return false; }
    }, token);
    public Task StopAsync(CancellationToken token)
    {
        lock (gate)
        {
            stopping.Cancel();
            return (stopTask ??= StopCore(drained.Task)).WaitAsync(token);
        }
    }
    async Task StopCore(Task drain)
    {
        await drain.ConfigureAwait(false);
        var delay = int.Parse(context.Parameters.GetValueOrDefault("fault.stop-delay-ms", "0"), System.Globalization.CultureInfo.InvariantCulture);
        if (delay > 0) await Task.Delay(delay).ConfigureAwait(false);
    }
    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        var delay = int.Parse(context.Parameters.GetValueOrDefault("fault.dispose-delay-ms", "0"), System.Globalization.CultureInfo.InvariantCulture);
        if (delay > 0) await Task.Delay(delay).ConfigureAwait(false);
        lock (gate) { if (disposed) return; client.Dispose(); stopping.Dispose(); disposed = true; }
    }
}

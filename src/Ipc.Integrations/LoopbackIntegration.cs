using System.Net;
using System.Net.Http.Json;
using Ipc.Platform;
using Ipc.Supply;

namespace Ipc.Adapters;

public sealed class LoopbackIntegration : IIntegrationPort
{
    readonly HttpClient client;
    readonly string healthEndpoint;
    public LoopbackIntegration(StationConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (config.Mode != RunMode.Simulation) throw new InvalidDataException("Loopback adapter is SIMULATION only");
        if (config.TimeoutMs is < 100 or > 30000) throw new InvalidDataException("Loopback transport timeout must be 100..30000 ms");
        ValidateBinding(config.Integration);
        healthEndpoint = config.Integration.Endpoint;
        client = new(new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false, MaxConnectionsPerServer = 2 }) { Timeout = TimeSpan.FromMilliseconds(config.TimeoutMs) };
    }
    static Uri ValidateBinding(IntegrationBinding binding)
    {
        if (binding is null || binding.Contract != "SIMULATION-v1" ||
            !Uri.TryCreate(binding.Endpoint, UriKind.Absolute, out var uri) || uri.Host != "127.0.0.1" ||
            uri.Scheme != "http" || uri.AbsolutePath != "/" || uri.UserInfo != "" || uri.Query != "" || uri.Fragment != "")
            throw new InvalidDataException("Frozen binding is not an approved loopback contract");
        return uri;
    }
    static Uri Endpoint(BusinessRequest request, string path) => new(ValidateBinding(request.Binding), path);
    public async Task<IntegrationResponse> ExecuteAsync(BusinessRequest request, CancellationToken token)
    {
        var envelope = new RequestEnvelope(request.Id, request.PayloadHash, request.Binding.Contract, request.Payload);
        using var response = await client.PostAsJsonAsync(Endpoint(request, "requests"), envelope, Wire.Json, token);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IntegrationResponse>(Wire.Json, token) ?? throw new InvalidDataException("Empty fixture response");
    }
    public async Task<IntegrationResponse?> QueryAsync(BusinessRequest request, CancellationToken token)
    {
        using var response = await client.GetAsync(Endpoint(request, "requests/" + request.Id), token);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.NotImplemented) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IntegrationResponse>(Wire.Json, token);
    }
    public async Task<bool> HealthAsync(CancellationToken token)
    {
        try { using var response = await client.GetAsync(new Uri(new Uri(healthEndpoint), "health"), token); return response.IsSuccessStatusCode && (await response.Content.ReadAsStringAsync(token)).Contains("SIMULATION-v1", StringComparison.Ordinal); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return false; }
    }
    public ValueTask DisposeAsync() { client.Dispose(); return ValueTask.CompletedTask; }
}

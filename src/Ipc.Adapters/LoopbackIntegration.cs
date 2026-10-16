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
        if (config.Mode != RunMode.Simulation || ProductValidation.ValidateStation(config).Count != 0) throw new InvalidDataException("Loopback adapter is SIMULATION only");
        healthEndpoint = config.Integration.Endpoint;
        client = new(new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false, MaxConnectionsPerServer = 2 }) { Timeout = TimeSpan.FromMilliseconds(config.TimeoutMs) };
    }
    static Uri Endpoint(BusinessRequest request, string path)
    {
        if (request.Binding.Contract != "SIMULATION-v1" || !Uri.TryCreate(request.Binding.Endpoint, UriKind.Absolute, out var uri) || uri.Host != "127.0.0.1" || uri.Scheme != "http" || uri.AbsolutePath != "/" || uri.UserInfo != "") throw new InvalidDataException("Frozen binding is not an approved loopback contract");
        return new(uri, path);
    }
    public async Task<FixtureResponse> ExecuteAsync(BusinessRequest request, CancellationToken token)
    {
        var envelope = new RequestEnvelope(request.Id, request.PayloadHash, request.Binding.Contract, request.Payload);
        using var response = await client.PostAsJsonAsync(Endpoint(request, "requests"), envelope, Wire.Json, token);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<FixtureResponse>(Wire.Json, token) ?? throw new InvalidDataException("Empty fixture response");
    }
    public async Task<FixtureResponse?> QueryAsync(BusinessRequest request, CancellationToken token)
    {
        using var response = await client.GetAsync(Endpoint(request, "requests/" + request.Id), token);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.NotImplemented) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<FixtureResponse>(Wire.Json, token);
    }
    public async Task<bool> HealthAsync(CancellationToken token)
    {
        try { using var response = await client.GetAsync(new Uri(new Uri(healthEndpoint), "health"), token); return response.IsSuccessStatusCode && (await response.Content.ReadAsStringAsync(token)).Contains("SIMULATION-v1", StringComparison.Ordinal); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return false; }
    }
    public ValueTask DisposeAsync() { client.Dispose(); return ValueTask.CompletedTask; }
}

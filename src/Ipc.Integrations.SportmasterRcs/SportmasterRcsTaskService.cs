using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Integrations.SportmasterRcs;

public sealed class SportmasterRcsTaskExtension : IWcsExtension
{
    public IWcsPort Create(CapabilityBinding binding) => new SportmasterRcsTaskService(binding);
}

/// <summary>RCS destination requests and station heartbeat. Heartbeat never resolves a business request.</summary>
public sealed partial class SportmasterRcsTaskService : IWcsPort, IWcsRequestPreparer, IWcsRuntimeConfiguration, IWcsPositionHeartbeat
{
    public const string Contract = RcsConfigurationValidation.Contract;
    public const int MaximumResponseBytes = 262144;
    internal TimeProvider ReceiptTimeForVerification { get; set; } = TimeProvider.System;
    readonly RcsTaskOptions options;
    readonly HttpClient client;
    readonly object lifecycle = new();
    bool running, disposed;
    int active;
    TaskCompletionSource? drained;
    public string InstanceId => options.Binding.InstanceId;

    public SportmasterRcsTaskService(CapabilityBinding binding) : this(binding, null) { }
    internal SportmasterRcsTaskService(CapabilityBinding binding,
        Func<SocketsHttpConnectionContext, CancellationToken, ValueTask<Stream>>? connect)
    {
        options = new(binding);
        client = new(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseProxy = false,
            AutomaticDecompression = DecompressionMethods.None,
            MaxConnectionsPerServer = 8,
            // Destination and heartbeat each supply one finite cancellation budget, including connect.
            ConnectTimeout = Timeout.InfiniteTimeSpan,
            ConnectCallback = connect,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        })
        { Timeout = Timeout.InfiniteTimeSpan };
    }
    public Task StartAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        lock (lifecycle)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (running) return Task.CompletedTask;
            if (active != 0 || heartbeatTask is { IsCompleted: false }) throw new InvalidOperationException("RCS stop is still draining");
            if (heartbeatStations is null) throw new InvalidDataException("RCS requires the effective station/reader configuration before start");
            heartbeatStop?.Dispose(); heartbeatStop = new();
            heartbeatFailure = null; heartbeatResponseReceived = false; stationVehicles = [];
            heartbeatReceivedAt = default; heartbeatValidUntil = default;
            heartbeatEvidence = heartbeatStations.Length == 0
                ? "Camera heartbeat suspended: no configured camera codes; manual request client ready; upstream acceptance unconfirmed"
                : "Heartbeat waiting for first scheduled send; upstream acceptance unconfirmed";
            running = true;
            // No configured reader means no valid heartbeat item. Do not send an
            // empty batch or invent an identity for the deferred cameras.
            heartbeatTask = heartbeatStations.Length == 0 ? Task.CompletedTask : HeartbeatLoopAsync(heartbeatStop.Token);
        }
        return Task.CompletedTask;
    }
    public Task<CapabilityHealth> HealthAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); var now = HeartbeatTimeForVerification.GetUtcNow();
        lock (lifecycle)
        {
            var confirmed = running && !disposed && heartbeatResponseReceived && heartbeatFailure is null && heartbeatReceivedAt <= now && heartbeatValidUntil > now;
            // Local client readiness also serves manual input without a heartbeat identity.
            // Remote timestamps belong to actual responses, never to this health read.
            return Task.FromResult(new CapabilityHealth(InstanceId, running && !disposed && heartbeatStations is not null && heartbeatFailure is null,
                now, now.AddSeconds(3), "RCS client local lifecycle/configuration readiness only; " + (heartbeatFailure ?? heartbeatEvidence))
            {
                RemoteConfirmation = confirmed ? RemoteConnectionConfirmation.ResponseReceived : RemoteConnectionConfirmation.Unconfirmed,
                RemoteConfirmedAt = heartbeatResponseReceived ? heartbeatReceivedAt : null,
                RemoteValidUntil = heartbeatResponseReceived ? heartbeatValidUntil : null,
                StationVehicles = confirmed ? stationVehicles.Where(v => v.ReceivedAt <= now && v.ValidUntil > now).ToArray() : []
            });
        }
    }
    public async Task ReconnectAsync(CancellationToken token)
    { await StopAsync(token).ConfigureAwait(false); await StartAsync(token).ConfigureAwait(false); }
    public bool UsesAutomaticRequestIds => true;
    public IntegrationRequestIdRange? GetRequestIdRange(CapabilityBinding binding) => null;
    public PreparedIntegrationRequest PrepareRequest(FrozenRequest request, long operationSequence) => RcsDestinationProtocol.Prepare(options, request, operationSequence);
    public Task<StageReply> ResolveSlotAsync(FrozenRequest request, CancellationToken token) => RequestDestinationAsync(request, token);
    public Task<StageReply?> QueryAsync(FrozenRequest original, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        // Query capability is false in the signed module. Never substitute POST for query.
        return Task.FromResult<StageReply?>(null);
    }
    public async Task<StageReply> RequestDestinationAsync(FrozenRequest request, CancellationToken token)
    {
        options.ValidateOriginal(request);
        var frozen = request.IntegrationRequest ?? throw new InvalidDataException("Persist the RCS wire intent before delivery");
        if (frozen.RequestId <= 0 || frozen.ContentType != "application/json" ||
            Wire.Hash(frozen.Body) != frozen.BodyHash || Encoding.UTF8.GetByteCount(frozen.Body) > 65536 ||
            !RcsDestinationProtocol.MatchesFrozen(options, request, frozen))
            throw new InvalidDataException("RCS frozen wire intent differs from original payload/configuration");
        lock (lifecycle)
        {
            if (!running || disposed) throw new IOException("RCS client is stopped");
            if (active++ == 0) drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(options.RequestTimeoutMs);
            using var content = new TrackedRequestContent(Encoding.UTF8.GetBytes(frozen.Body));
            using var message = new HttpRequestMessage(HttpMethod.Post, options.Endpoint)
            {
                Version = HttpVersion.Version11,
                VersionPolicy = HttpVersionPolicy.RequestVersionExact,
                Content = content
            };
            message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            HttpResponseMessage response;
            try { response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false); }
            catch (OperationCanceledException e) when (!token.IsCancellationRequested)
            { throw new TimeoutException("RCS请求超时，原请求结果未确认；请核对原请求。", e); }
            catch (HttpRequestException e) when (e.HttpRequestError == HttpRequestError.ConnectionError && !content.BodyStarted && ConnectionRefused(e))
            {
                return new StageReply(Wire.Id(), request.Id, request.PayloadHash, false, null, null, null, null,
                    "RCS_NOT_DELIVERED: connection refused before any serialization of the frozen nonempty request body; sender evidence only",
                    ReceiptTimeForVerification.GetUtcNow())
                { SenderDelivery = SenderDeliveryEvidence.ConnectionRefusedBeforeBody };
            }
            catch (HttpRequestException e) { throw new IOException("RCS HTTP delivery/outcome is unknown", e); }
            using (response)
            {
                var (bytes, complete) = await ReadBoundedAsync(response.Content, deadline.Token).ConfigureAwait(false);
                var original = new IntegrationHttpResponse((int)response.StatusCode, Convert.ToBase64String(bytes), Convert.ToHexString(SHA256.HashData(bytes)), complete);
                StageReply Unknown(string detail) => new(Wire.Id(), request.Id, request.PayloadHash, true, null, null, null, null, detail,
                    ReceiptTimeForVerification.GetUtcNow(), HttpResponse: original);
                if (deadline.IsCancellationRequested && !token.IsCancellationRequested) return Unknown("RCS_REQUEST_TIMEOUT: RCS请求超时，原请求结果未确认；请核对原请求。");
                if (!complete) return Unknown("RCS response incomplete or exceeds quota; retained bytes are a prefix, outcome unknown");
                if (!response.IsSuccessStatusCode) return Unknown("RCS HTTP " + (int)response.StatusCode + "; no authoritative business outcome");
                try
                {
                    var (accepted, slot, destination) = RcsDestinationProtocol.Parse(bytes, frozen);
                    return new(Wire.Id(), request.Id, request.PayloadHash, true, true, accepted, null, slot,
                        accepted ? "RCS destination assigned; physical completion is unconfirmed" : "RCS " + destination.Code + ": " + destination.Message,
                        ReceiptTimeForVerification.GetUtcNow(), Destination: destination, HttpResponse: original);
                }
                catch (RcsResponseException e) { return Unknown("RCS_RESPONSE_ERROR: " + e.Message); }
                catch (Exception e) when (e is JsonException or InvalidDataException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
                { return Unknown("Invalid/unmatched RCS response: " + e.GetType().Name + "; original response retained, outcome unknown"); }
            }
        }
        finally { lock (lifecycle) if (--active == 0) drained!.TrySetResult(); }
    }
    static bool ConnectionRefused(Exception error)
    {
        for (Exception? current = error; current is not null; current = current.InnerException)
            if (current is SocketException socket && socket.SocketErrorCode == SocketError.ConnectionRefused) return true;
        return false;
    }
    sealed class TrackedRequestContent(byte[] bytes) : HttpContent
    {
        int started;
        internal bool BodyStarted => Volatile.Read(ref started) != 0;
        protected override bool TryComputeLength(out long length) { length = bytes.Length; return true; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => SerializeToStreamAsync(stream, context, CancellationToken.None);
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken token)
        {
            // Cumulative across any internal retries of this same content.
            Interlocked.Exchange(ref started, 1);
            await stream.WriteAsync(bytes, token).ConfigureAwait(false);
        }
    }
    static async Task<(byte[] Bytes, bool Complete)> ReadBoundedAsync(HttpContent content, CancellationToken token)
    {
        using var bytes = new MemoryStream();
        try
        {
            await using var stream = await content.ReadAsStreamAsync(token).ConfigureAwait(false);
            var buffer = new byte[8192];
            while (bytes.Length <= MaximumResponseBytes)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, MaximumResponseBytes + 1L - bytes.Length)), token).ConfigureAwait(false);
                if (read == 0) return (bytes.ToArray(), true);
                bytes.Write(buffer, 0, read);
            }
        }
        catch (Exception e) when (e is IOException or HttpRequestException or OperationCanceledException)
        { return (bytes.ToArray(), false); }
        return (bytes.ToArray(), false);
    }
    public async Task StopAsync(CancellationToken token)
    {
        Task wait; lock (lifecycle)
        {
            running = false; stationVehicles = []; heartbeatStop?.Cancel();
            wait = Task.WhenAll(heartbeatTask ?? Task.CompletedTask, active == 0 ? Task.CompletedTask : drained!.Task);
        }
        await wait.WaitAsync(token).ConfigureAwait(false);
    }
    public async ValueTask DisposeAsync()
    {
        lock (lifecycle) disposed = true;
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        lock (lifecycle) { heartbeatStop?.Dispose(); heartbeatStop = null; client.Dispose(); }
    }
}

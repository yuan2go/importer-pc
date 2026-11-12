using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Integrations.SportmasterRcs;

public sealed partial class SportmasterRcsTaskService
{
    internal const string HeartbeatCommand = "sorter.task.station_heartbeat";
    (string Station, string Bcr)[]? heartbeatStations;
    CancellationTokenSource? heartbeatStop;
    Task? heartbeatTask;
    readonly SemaphoreSlim heartbeatSerial = new(1, 1);
    bool manualHeartbeatPending;
    string? heartbeatFailure;
    bool heartbeatResponseReceived;
    DateTimeOffset heartbeatReceivedAt, heartbeatValidUntil;
    internal TimeProvider HeartbeatTimeForVerification { get; set; } = TimeProvider.System;
    string heartbeatEvidence = "RCS client stopped; upstream acceptance unconfirmed";
    StationVehicleState[] stationVehicles = [];
    static long lastHeartbeatId;

    public void ConfigureRuntime(ParcelConfiguration configuration)
    {
        lock (lifecycle)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (running || active != 0 || heartbeatTask is { IsCompleted: false }) throw new InvalidOperationException("Configure RCS only while stopped");
            if (configuration.ApplicationId != "sportmaster" || configuration.Wcs is null ||
                Wire.Encode(configuration.Wcs) != Wire.Encode(options.Binding) ||
                ParcelConnectionSetup.Validate(configuration).Length != 0 || ParcelConnectionSetup.IsDeferred(configuration, InstanceId))
                throw new InvalidDataException("RCS runtime context must match its effective binding");
            heartbeatStations = RcsConfigurationValidation.HeartbeatMapping(configuration);
        }
    }

    async Task HeartbeatLoopAsync(CancellationToken stop)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(options.HeartbeatIntervalMs));
        try
        {
            while (await timer.WaitForNextTickAsync(stop).ConfigureAwait(false))
            {
                if (!heartbeatSerial.Wait(0)) continue;
                try { await SendHeartbeatCoreAsync(stop).ConfigureAwait(false); }
                finally { heartbeatSerial.Release(); }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
    }

    public async Task<RcsHeartbeatResult> SendHeartbeatAsync(CancellationToken token)
    {
        CancellationTokenSource linked;
        lock (lifecycle)
        {
            if (!running || disposed) return RcsHeartbeatResult.Unavailable;
            if (heartbeatStations is not { Length: > 0 }) return RcsHeartbeatResult.NoPositions;
            if (manualHeartbeatPending) return RcsHeartbeatResult.Busy;
            linked = CancellationTokenSource.CreateLinkedTokenSource(token, heartbeatStop!.Token);
            manualHeartbeatPending = true;
            if (active++ == 0) drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        try
        {
            await heartbeatSerial.WaitAsync(linked.Token).ConfigureAwait(false);
            try { return await SendHeartbeatCoreAsync(linked.Token).ConfigureAwait(false); }
            finally { heartbeatSerial.Release(); }
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested && !token.IsCancellationRequested)
        { return RcsHeartbeatResult.Unavailable; }
        finally
        {
            linked.Dispose();
            lock (lifecycle)
            {
                manualHeartbeatPending = false;
                if (--active == 0) drained!.TrySetResult();
            }
        }
    }

    async Task<RcsHeartbeatResult> SendHeartbeatCoreAsync(CancellationToken stop)
    {
        stop.ThrowIfCancellationRequested();
        (string Station, string Bcr)[] mapping;
        lock (lifecycle)
        {
            if (!running || disposed) return RcsHeartbeatResult.Unavailable;
            mapping = heartbeatStations ?? [];
        }
        if (mapping.Length == 0) return RcsHeartbeatResult.NoPositions;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(Math.Min(3000, options.HeartbeatIntervalMs)));
        try
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var requestId = NextHeartbeatId(timestamp);
            var body = JsonSerializer.Serialize(new
            {
                source = options.Source,
                version = 1,
                requestId,
                locale = options.Locale,
                data = mapping.Select((p, i) => new
                {
                    index = i + 1,
                    command = HeartbeatCommand,
                    @params = new { stationCode = p.Station, bcrCode = p.Bcr, timestamp }
                }).ToArray()
            }, RcsDestinationProtocol.Json);
            using var message = new HttpRequestMessage(HttpMethod.Post, options.Endpoint)
            {
                Version = HttpVersion.Version11,
                VersionPolicy = HttpVersionPolicy.RequestVersionExact,
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body))
            };
            message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            using var response = await heartbeatClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) throw new HttpRequestException("HTTP " + (int)response.StatusCode);
            var (bytes, complete) = await ReadBoundedAsync(response.Content, timeout.Token).ConfigureAwait(false);
            stop.ThrowIfCancellationRequested();
            if (!complete) throw new InvalidDataException("Incomplete or oversized heartbeat response");
            var receivedAt = HeartbeatTimeForVerification.GetUtcNow();
            var lifetime = TimeSpan.FromMilliseconds(Math.Max(3000, options.HeartbeatIntervalMs * 3));
            var vehicles = ReadHeartbeatVehicles(bytes, requestId, mapping, receivedAt, lifetime);
            lock (lifecycle)
            {
                if (!running || disposed) return RcsHeartbeatResult.Unavailable;
                stationVehicles = vehicles;
                heartbeatFailure = null; heartbeatResponseReceived = true;
                heartbeatReceivedAt = receivedAt; heartbeatValidUntil = receivedAt + lifetime;
                heartbeatEvidence = "RCS heartbeat HTTP " + (int)response.StatusCode + " at " + timestamp.ToString(CultureInfo.InvariantCulture) + "; application acknowledgement unconfirmed";
            }
            return RcsHeartbeatResult.Received;
        }
        catch (Exception e) when (e is HttpRequestException or IOException or OperationCanceledException or InvalidDataException)
        {
            stop.ThrowIfCancellationRequested();
            lock (lifecycle)
            {
                if (!running || disposed) return RcsHeartbeatResult.Unavailable;
                stationVehicles = []; heartbeatFailure = "RCS heartbeat failed: " + (e is OperationCanceledException ? "timeout" : e.Message) + "; no business request was retried";
            }
            return RcsHeartbeatResult.Failed;
        }
    }

    static long NextHeartbeatId(long timestamp)
    {
        while (true)
        {
            var previous = Interlocked.Read(ref lastHeartbeatId);
            var next = Math.Max(timestamp, checked(previous + 1));
            if (Interlocked.CompareExchange(ref lastHeartbeatId, next, previous) == previous) return next;
        }
    }
}

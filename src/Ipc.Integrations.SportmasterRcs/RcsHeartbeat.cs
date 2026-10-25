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
    string? heartbeatFailure;
    string heartbeatEvidence = "RCS client stopped; upstream acceptance unconfirmed";
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
            var mapping = RcsConfigurationValidation.HeartbeatMapping(configuration);
            heartbeatStations = mapping;
        }
    }


    async Task HeartbeatLoopAsync(CancellationToken stop)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.HeartbeatIntervalSeconds));
        try
        {
            while (await timer.WaitForNextTickAsync(stop).ConfigureAwait(false))
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop);
                timeout.CancelAfter(TimeSpan.FromSeconds(Math.Min(3, options.HeartbeatIntervalSeconds)));
                try
                {
                    var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    var requestId = NextHeartbeatId(timestamp);
                    // Do not consume or collide with the durable destination request range.
                    if (requestId > options.RequestIdBase && requestId <= checked(options.RequestIdBase + options.RequestIdCount))
                        throw new InvalidDataException("Heartbeat clock requestId overlaps the configured destination range; allocate disjoint ranges");
                    var body = JsonSerializer.Serialize(new
                    {
                        source = options.Source, version = 1, requestId, locale = options.Locale,
                        data = heartbeatStations!.Select((p, i) => new
                        {
                            index = i + 1, command = HeartbeatCommand,
                            @params = new { stationCode = p.Station, bcrCode = p.Bcr, timestamp }
                        }).ToArray()
                    }, RcsDestinationProtocol.Json);
                    using var message = new HttpRequestMessage(HttpMethod.Post, options.Endpoint)
                    {
                        Version = HttpVersion.Version11, VersionPolicy = HttpVersionPolicy.RequestVersionExact,
                        Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body))
                    };
                    message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                    using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                    // No response contract has been supplied. HTTP delivery proves no business acceptance.
                    if (!response.IsSuccessStatusCode) throw new HttpRequestException("HTTP " + (int)response.StatusCode);
                    lock (lifecycle)
                    {
                        heartbeatFailure = null;
                        heartbeatEvidence = "RCS heartbeat HTTP " + (int)response.StatusCode + " at " + timestamp.ToString(CultureInfo.InvariantCulture) + "; application acknowledgement unconfirmed";
                    }
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
                catch (Exception e) when (e is HttpRequestException or IOException or OperationCanceledException or InvalidDataException)
                {
                    lock (lifecycle) heartbeatFailure = "RCS heartbeat failed: " + (e is OperationCanceledException ? "timeout" : e.Message) + "; no business request was retried";
                }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
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

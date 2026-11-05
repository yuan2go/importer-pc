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
    (string Station, string Bcr)[]? enabledHeartbeatStations;
    Dictionary<string, string> heartbeatPositions = new(StringComparer.Ordinal);
    long heartbeatSelectionRevision;
    CancellationTokenSource? heartbeatStop;
    Task? heartbeatTask;
    string? heartbeatFailure;
    bool heartbeatResponseReceived;
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
            var mapping = RcsConfigurationValidation.HeartbeatMapping(configuration);
            heartbeatStations = mapping;
            enabledHeartbeatStations = mapping;
            heartbeatPositions = ParcelBindings.Positions(configuration).ToDictionary(p => p.Id,
                p => configuration.Wcs.Parameters.GetValueOrDefault(p.Id + "-bcr-code") ?? "", StringComparer.Ordinal);
            heartbeatSelectionRevision++;
        }
    }

    public void SetEnabledPositions(string[] positionIds)
    {
        lock (lifecycle)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (heartbeatStations is null || positionIds.Distinct(StringComparer.Ordinal).Count() != positionIds.Length ||
                positionIds.Any(p => !heartbeatPositions.ContainsKey(p))) throw new InvalidDataException("RCS heartbeat position identity differs from its configured topology");
            var codes = positionIds.Select(p => heartbeatPositions[p]).ToHashSet(StringComparer.Ordinal);
            var selected = heartbeatStations.Where(p => codes.Contains(p.Bcr)).ToArray();
            if (enabledHeartbeatStations is not null && selected.SequenceEqual(enabledHeartbeatStations)) return;
            enabledHeartbeatStations = selected; heartbeatSelectionRevision++;
            stationVehicles = stationVehicles.Where(v => codes.Contains(v.CameraCode)).ToArray();
            if (selected.Length == 0)
            {
                heartbeatResponseReceived = false; heartbeatFailure = null;
                heartbeatEvidence = "No enabled heartbeat positions; remote connection unconfirmed";
            }
        }
    }


    async Task HeartbeatLoopAsync(CancellationToken stop)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(options.HeartbeatIntervalMs));
        try
        {
            while (await timer.WaitForNextTickAsync(stop).ConfigureAwait(false))
            {
                (string Station, string Bcr)[] mapping; long selectionRevision;
                lock (lifecycle) { mapping = enabledHeartbeatStations ?? []; selectionRevision = heartbeatSelectionRevision; }
                if (mapping.Length == 0) continue; // Never send an empty or synthetic identity heartbeat.
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
                    using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode) throw new HttpRequestException("HTTP " + (int)response.StatusCode);
                    var (bytes, complete) = await ReadBoundedAsync(response.Content, timeout.Token).ConfigureAwait(false);
                    if (!complete) throw new InvalidDataException("Incomplete or oversized heartbeat response");
                    var vehicles = ReadHeartbeatVehicles(bytes, requestId, mapping, DateTimeOffset.UtcNow,
                        TimeSpan.FromMilliseconds(Math.Max(3000, options.HeartbeatIntervalMs * 3)));
                    lock (lifecycle)
                    {
                        if (selectionRevision != heartbeatSelectionRevision) continue; // A prior selection cannot resurrect disabled vehicle hints.
                        stationVehicles = vehicles;
                        heartbeatFailure = null; heartbeatResponseReceived = true;
                        heartbeatEvidence = "RCS heartbeat HTTP " + (int)response.StatusCode + " at " + timestamp.ToString(CultureInfo.InvariantCulture) + "; application acknowledgement unconfirmed";
                    }
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
                catch (Exception e) when (e is HttpRequestException or IOException or OperationCanceledException or InvalidDataException)
                {
                    lock (lifecycle)
                    {
                        if (selectionRevision != heartbeatSelectionRevision) continue;
                        stationVehicles = []; heartbeatFailure = "RCS heartbeat failed: " + (e is OperationCanceledException ? "timeout" : e.Message) + "; no business request was retried";
                    }
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

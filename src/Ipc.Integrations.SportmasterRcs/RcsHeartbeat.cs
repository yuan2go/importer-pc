using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Ipc.Application.Contracts;
using Ipc.Platform;

namespace Ipc.Integrations.SportmasterRcs;

public sealed partial class SportmasterRcsTaskService : ISmHeartbeatControl
{
    internal const string HeartbeatCommand = "sorter.task.station_heartbeat";
    const int FastProbeIntervalMs = 250, FastProbeWindowMs = 2500;
    readonly Dictionary<(string Station, string Bcr), (string VehicleNo, long StartedAt, long UntilTicks)> fastProbes = [];
    (string Station, string Bcr)[]? heartbeatStations;
    CancellationTokenSource? heartbeatStop;
    Task? heartbeatTask;
    readonly SemaphoreSlim heartbeatSerial = new(1, 1);
    bool manualHeartbeatPending;
    string? heartbeatFailure;
    bool heartbeatResponseReceived;
    DateTimeOffset heartbeatReceivedAt, heartbeatValidUntil;
    internal TimeProvider HeartbeatTimeForVerification { get; set; } = TimeProvider.System;
    internal TimeProvider HeartbeatScheduleForVerification { get; set; } = TimeProvider.System;
    string heartbeatEvidence = "RCS client stopped; upstream acceptance unconfirmed";
    StationVehicleState[] stationVehicles = [];
    static long lastHeartbeatId;
    ISmHeartbeatOwner? heartbeatOwner;
    readonly SemaphoreSlim heartbeatRefresh = new(0, 1);
    int heartbeatRefreshPending;
    public void SetHeartbeatOwner(ISmHeartbeatOwner owner)
    {
        lock (lifecycle)
        {
            if (running) throw new InvalidOperationException("Bind heartbeat owner before starting");
            heartbeatOwner = owner;
        }
    }
    public void RefreshHeartbeat()
    {
        Interlocked.Exchange(ref heartbeatRefreshPending, 1);
        SignalHeartbeat();
    }
    void SignalHeartbeat()
    {
        if (heartbeatRefresh.CurrentCount == 0)
            try { heartbeatRefresh.Release(); } catch (SemaphoreFullException) { }
    }

    void StartFastProbe(FrozenRequest request)
    {
        if (options.HeartbeatIntervalMs <= FastProbeIntervalMs) return;
        var target = options.Position(Wire.Decode<ParcelPayload>(request.Payload));
        lock (lifecycle)
        {
            if (!running || disposed || heartbeatFailure is not null) return;
            var now = HeartbeatTimeForVerification.GetUtcNow();
            var vehicle = stationVehicles.SingleOrDefault(v => v.StationCode == target.Station && v.CameraCode == target.Bcr &&
                v.ReceivedAt <= now && v.ValidUntil > now);
            if (vehicle is null) return;
            var clock = HeartbeatScheduleForVerification;
            var startedAt = clock.GetTimestamp();
            if (fastProbes.TryGetValue(target, out var current) && current.UntilTicks > startedAt && current.VehicleNo == vehicle.VehicleNo)
                return;
            fastProbes[target] = (vehicle.VehicleNo, startedAt, startedAt + FastProbeWindowMs * clock.TimestampFrequency / 1000);
        }
        // Recalculate the outstanding wait without requesting an extra heartbeat.
        SignalHeartbeat();
    }

    TimeSpan HeartbeatWait(long cycleStartedAt)
    {
        var clock = HeartbeatScheduleForVerification;
        var now = clock.GetTimestamp();
        lock (lifecycle)
        {
            foreach (var target in fastProbes.Keys.ToArray())
                if (fastProbes[target].UntilTicks <= now) fastProbes.Remove(target);
            var fast = fastProbes.Count != 0;
            var start = fast ? Math.Max(cycleStartedAt, fastProbes.Values.Min(p => p.StartedAt)) : cycleStartedAt;
            var interval = fast ? Math.Min(options.HeartbeatIntervalMs, FastProbeIntervalMs) : options.HeartbeatIntervalMs;
            var remaining = TimeSpan.FromMilliseconds(interval) - clock.GetElapsedTime(start, now);
            if (fast)
            {
                var untilExpiry = clock.GetElapsedTime(now, fastProbes.Values.Min(p => p.UntilTicks));
                if (untilExpiry < remaining) remaining = untilExpiry;
            }
            return remaining > TimeSpan.Zero ? TimeSpan.FromMilliseconds(Math.Ceiling(remaining.TotalMilliseconds)) : TimeSpan.Zero;
        }
    }

    void ClearFastProbes()
    {
        // Called with lifecycle held, including when a manual heartbeat fails.
        if (fastProbes.Count == 0) return;
        fastProbes.Clear();
        SignalHeartbeat();
    }

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
        try
        {
            var clock = HeartbeatScheduleForVerification;
            var cycleStartedAt = clock.GetTimestamp();
            while (!stop.IsCancellationRequested)
            {
                using var tick = CancellationTokenSource.CreateLinkedTokenSource(stop);
                var delay = Task.Delay(HeartbeatWait(cycleStartedAt), clock, tick.Token);
                var refresh = heartbeatRefresh.WaitAsync(tick.Token);
                await Task.WhenAny(delay, refresh).ConfigureAwait(false);
                tick.Cancel();
                try { await Task.WhenAll(delay, refresh).ConfigureAwait(false); }
                catch (OperationCanceledException) when (!stop.IsCancellationRequested) { }
                stop.ThrowIfCancellationRequested();
                if (Volatile.Read(ref heartbeatRefreshPending) == 0 && HeartbeatWait(cycleStartedAt) > TimeSpan.Zero) continue;
                if (heartbeatSerial.Wait(0))
                {
                    try { await SendHeartbeatCoreAsync(stop).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (stop.IsCancellationRequested) { throw; }
                    catch (Exception error) { HeartbeatOwnerFailed(error); }
                    finally { heartbeatSerial.Release(); }
                }
                cycleStartedAt = clock.GetTimestamp();
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
            if (manualHeartbeatPending) return RcsHeartbeatResult.Busy;
            linked = CancellationTokenSource.CreateLinkedTokenSource(token, heartbeatStop!.Token);
            manualHeartbeatPending = true;
            if (active++ == 0) drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        try
        {
            await heartbeatSerial.WaitAsync(linked.Token).ConfigureAwait(false);
            try { return await SendHeartbeatCoreAsync(linked.Token).ConfigureAwait(false); }
            finally { heartbeatSerial.Release(); if (Volatile.Read(ref heartbeatRefreshPending) != 0) SignalHeartbeat(); }
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested && !token.IsCancellationRequested)
        { return RcsHeartbeatResult.Unavailable; }
        catch (Exception error) when (error is not OperationCanceledException)
        { HeartbeatOwnerFailed(error); return RcsHeartbeatResult.Failed; }
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
    void HeartbeatOwnerFailed(Exception error)
    {
        lock (lifecycle)
        {
            ClearFastProbes();
            heartbeatResponseReceived = false; heartbeatValidUntil = default; stationVehicles = [];
            heartbeatFailure = "RCS heartbeat owner failed: " + error;
            heartbeatEvidence = "Heartbeat completion unconfirmed; no business request was repeated";
        }
    }

    async Task<RcsHeartbeatResult> SendHeartbeatCoreAsync(CancellationToken stop)
    {
        stop.ThrowIfCancellationRequested();
        Interlocked.Exchange(ref heartbeatRefreshPending, 0);
        (string Station, string Bcr)[] mapping;
        lock (lifecycle)
        {
            if (!running || disposed) return RcsHeartbeatResult.Unavailable;
            mapping = heartbeatStations ?? [];
        }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(3000));
        SmHeartbeatIntent? intent = null;
        var confirmation = new SmHeartbeatConfirmation(false, false, [], default, default);
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var scheduledAt = HeartbeatScheduleForVerification.GetTimestamp();
        try
        {
            await InstallationResourceBoundary.RequireRcsEndpointAsync(options.Endpoint, timeout.Token).ConfigureAwait(false);
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var requestId = NextHeartbeatId(timestamp);
            if (heartbeatOwner is { } owner)
            {
                intent = await owner.BeginHeartbeatAsync(requestId, timeout.Token).ConfigureAwait(false);
                if (intent is null) return RcsHeartbeatResult.Busy;
                mapping = intent.Members.Select(x => (x.StationCode, x.BcrCode)).ToArray();
            }
            lock (lifecycle)
            {
                foreach (var target in fastProbes.Keys.ToArray())
                    if (!mapping.Contains(target) && fastProbes[target].StartedAt <= scheduledAt)
                    {
                        fastProbes.Remove(target);
                        SignalHeartbeat();
                    }
            }
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
            confirmation = confirmation with { AttemptStartedAt = DateTimeOffset.UtcNow, Target = options.Endpoint.ToString(), RequestBody = body };
            using var response = await heartbeatClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            confirmation = confirmation with { HttpResponded = true, HttpStatus = (int)response.StatusCode };
            var (bytes, complete) = await ReadBoundedAsync(response.Content, timeout.Token).ConfigureAwait(false);
            confirmation = confirmation with
            {
                ResponseBody = Encoding.UTF8.GetString(bytes), ResponseBytes = bytes.Length,
                ResponseComplete = complete, ReceivedAt = HeartbeatTimeForVerification.GetUtcNow()
            };
            stop.ThrowIfCancellationRequested();
            if (!complete) throw new InvalidDataException(timeout.IsCancellationRequested ? "Heartbeat response read timed out" :
                bytes.Length > MaximumResponseBytes ? "Oversized heartbeat response" : "Incomplete heartbeat response");
            if (!response.IsSuccessStatusCode) throw new HttpRequestException("HTTP " + (int)response.StatusCode);
            var receivedAt = confirmation.ReceivedAt;
            var lifetime = TimeSpan.FromMilliseconds(Math.Max(3000, options.HeartbeatIntervalMs * 3L) + 3000);
            var vehicles = ReadHeartbeatVehicles(bytes, requestId, mapping, receivedAt, lifetime);
            var acknowledged = ValidHeartbeatConfirmation(bytes, requestId, mapping);
            confirmation = confirmation with { ProtocolConfirmed = acknowledged, Vehicles = vehicles, ValidUntil = receivedAt + lifetime };
            if (!acknowledged) throw new InvalidDataException("Heartbeat protocol acknowledgement invalid");
            lock (lifecycle)
            {
                if (!running || disposed) return RcsHeartbeatResult.Unavailable;
                stationVehicles = vehicles;
                heartbeatFailure = null; heartbeatResponseReceived = true;
                if (Volatile.Read(ref heartbeatRefreshPending) == 0)
                    foreach (var vehicle in vehicles)
                    {
                        var target = (vehicle.StationCode, vehicle.CameraCode);
                        if (fastProbes.TryGetValue(target, out var probe) && probe.StartedAt <= scheduledAt && probe.VehicleNo != vehicle.VehicleNo)
                        {
                            fastProbes.Remove(target);
                            SignalHeartbeat();
                        }
                    }
                heartbeatReceivedAt = receivedAt; heartbeatValidUntil = receivedAt + lifetime;
                heartbeatEvidence = "RCS heartbeat HTTP " + (int)response.StatusCode + " at " + timestamp.ToString(CultureInfo.InvariantCulture) + "; heartbeat protocol and membership acknowledged; destination outcome unconfirmed";
            }
            return RcsHeartbeatResult.Received;
        }
        catch (Exception e) when (e is HttpRequestException or IOException or OperationCanceledException or InvalidDataException)
        {
            confirmation = confirmation with { Failure = e is OperationCanceledException ? stop.IsCancellationRequested ? "Heartbeat stopped or cancelled" : "Heartbeat timed out" : e.Message };
            lock (lifecycle)
            {
                ClearFastProbes();
                stop.ThrowIfCancellationRequested();
                if (!running || disposed) return RcsHeartbeatResult.Unavailable;
                stationVehicles = []; heartbeatFailure = "RCS heartbeat failed: " + (e is OperationCanceledException ? "timeout" : e.Message) + "; no business request was retried";
            }
            return RcsHeartbeatResult.Failed;
        }
        finally
        {
            confirmation = confirmation with { ElapsedMs = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds };
            if (intent is not null && heartbeatOwner is { } owner)
                await owner.CompleteHeartbeatAsync(intent, confirmation).ConfigureAwait(false);
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

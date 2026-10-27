using System.Net;
using System.Text;
using System.Threading.Channels;
using Ipc.Platform;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Ipc.Drivers.Hikrobot.Probe")]
namespace Ipc.Drivers.Hikrobot;

internal sealed class HikFrameRejectedException(Exception inner) : IOException("Native frame rejected before managed delivery: " + inner.Message, inner);

public sealed class HikrobotModule : IObservationExtension
{
    public IControlledObservationSource Create(ExtensionContext context) => new HikrobotReader(context, new HikrobotApi());
}

internal sealed class HikrobotReader : IControlledObservationSource
{
    readonly ExtensionContext context;
    readonly IHikrobotApi api;
    readonly SemaphoreSlim lifecycle = new(1, 1);
    readonly object gate = new();
    readonly string ip, entityMode, serial;
    readonly Encoding encoding;
    readonly int capacity;
    Channel<Observation>? queue;
    CancellationTokenSource? cancel;
    Task? consumer, heartbeat, stopTask;
    Action<DeviceHealthConfirmation>? health;
    Func<Observation, bool>? receiver;
    IDisposable? physicalLease;
    readonly Func<string, IDisposable> acquireLease;
    bool created, opened, grabbing, accepting, faulted, disposed;
    long sequence;
    string? connectedSerial;
    readonly Dictionary<uint, uint> triggers = [];
    public string DeviceId => context.DeviceId;
    public string Epoch { get; private set; } = "not-started";
    public string ConfirmationSource { get { lock (gate) return "SDK:hikrobot-idmvs:" + context.InstanceId + "; epoch=" + Epoch +
        "; received=" + FramesReceived + "; delivered=" + FramesDelivered + "; rejected=" + FramesRejected + "; inFlight=" + FramesInFlight +
        "; sequence=" + sequence + (LastFailure is null ? "" : "; failure=" + LastFailure); } }
    public string? LastFailure { get; private set; }
    public long FramesReceived { get; private set; }
    public long FramesRejected { get; private set; }
    public long FramesDelivered { get; private set; }
    public long FramesInFlight { get { lock (gate) return FramesReceived - FramesDelivered - FramesRejected; } }
    internal HikrobotReader(ExtensionContext context, IHikrobotApi api, Func<string, IDisposable>? acquireLease = null)
    {
        this.context = context; this.api = api;
        this.acquireLease = acquireLease ?? new Func<string, IDisposable>(actualSerial => new ResourceLease(MachineRuntimePolicy.ResolveLeaseRoot(null), ["device:hikrobot-idmvs:" + actualSerial]));
        ip = context.Parameters.GetValueOrDefault("device.ip", "");
        if (!IPAddress.TryParse(ip, out var address) || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork || address.ToString() != ip || ip is "0.0.0.0" or "255.255.255.255")
            throw new InvalidDataException("Explicit canonical camera IPv4 required");
        serial = context.Parameters.GetValueOrDefault("device.serial", "");
        entityMode = context.Parameters.GetValueOrDefault("entity.mode", "observe-only");
        if (entityMode is not ("observe-only" or "trigger")) throw new InvalidDataException("Unknown entity association policy");
        var charset = context.Parameters.GetValueOrDefault("code.encoding", "utf-8");
        encoding = charset switch { "utf-8" => new UTF8Encoding(false, true), "ascii" => Encoding.GetEncoding("us-ascii", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback), _ => throw new InvalidDataException("Unsupported explicit barcode encoding") };
        capacity = int.Parse(context.Parameters.GetValueOrDefault("queue.capacity", "128"), System.Globalization.CultureInfo.InvariantCulture);
        if (capacity is < 1 or > 1024) throw new InvalidDataException("Reader queue must be 1..1024");
    }

    public async Task StartControlledAsync(string registeredEpoch, Func<Observation, bool> receive, Action<DeviceHealthConfirmation> onHealth, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registeredEpoch);
        await lifecycle.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (created || queue is not null) throw new InvalidOperationException("Previous reader registration still owns resources");
            lock (gate) { if (stopTask is { IsCompleted: false }) throw new InvalidOperationException("Original stop is still running; resources retained"); stopTask = null; }
            lock (gate) { Epoch = registeredEpoch; receiver = receive; health = onHealth; faulted = false; LastFailure = null; sequence = 0; triggers.Clear(); FramesReceived = FramesDelivered = FramesRejected = 0; }
            Confirm(DeviceHealth.Connecting);
            try
            {
                await Task.Run(() =>
                {
                    var matches = api.Enumerate().Where(d => d.Ip == ip && (serial.Length == 0 || d.Serial == serial)).ToArray();
                    token.ThrowIfCancellationRequested();
                    if (matches.Length == 0) throw new ObservationConnectionException("ConfiguredReaderMissing", new IOException("Configured IP/serial not discovered"));
                    if (matches.Length != 1 || string.IsNullOrWhiteSpace(matches[0].Serial)) throw new InvalidDataException("Configured IP/serial has ambiguous identity");
                    physicalLease = acquireLease(matches[0].Serial);
                    connectedSerial = matches[0].Serial;
                    created = true; api.Create(matches[0]); token.ThrowIfCancellationRequested(); api.Open(); opened = true; token.ThrowIfCancellationRequested();
                    var mode = api.TriggerMode(); token.ThrowIfCancellationRequested();
                    if (entityMode == "trigger" && mode != 1) throw new InvalidDataException("Trigger association needs existing TriggerMode=On; device settings were not changed");
                    queue = Channel.CreateBounded<Observation>(new BoundedChannelOptions(capacity) { SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait });
                    cancel = new();
                    api.Register(frame => OnFrame(frame, registeredEpoch), failure =>
                    { lock (gate) if (Epoch == registeredEpoch) { if (failure is HikFrameRejectedException) { FramesReceived++; FramesRejected++; } Fault(failure); } }, encoding);
                    lock (gate) accepting = true;
                    token.ThrowIfCancellationRequested(); grabbing = true; api.Start(); token.ThrowIfCancellationRequested();
                    lock (gate) { if (faulted) throw new IOException(LastFailure); Confirm(DeviceHealth.Ready); }
                    consumer = Task.Run(Consume); heartbeat = Task.Run(() => Heartbeat(cancel.Token));
                }, token).ConfigureAwait(false);
            }
            catch (Exception startFailure)
            {
                Fault(startFailure);
                try { await StopCore().ConfigureAwait(false); }
                catch (Exception cleanup) { throw new AggregateException("Reader startup and cleanup failed; ownership retained", startFailure, cleanup); }
                throw;
            }
        }
        finally { lifecycle.Release(); }
    }
    void OnFrame(HikFrame frame, string registeredEpoch)
    {
        lock (gate)
        {
            if (Epoch != registeredEpoch) return; // Old epoch remains outside this registration's accounting.
            FramesReceived++;
            if (!accepting || faulted) { FramesRejected++; return; }
            if (entityMode == "trigger")
            {
                if (triggers.TryGetValue(frame.Channel, out var prior) && frame.Trigger < prior)
                { FramesRejected++; Fault(new IOException("Trigger counter reversed/wrapped; entity identity needs explicit inspection and reconnect")); return; }
                if (!triggers.ContainsKey(frame.Channel) && triggers.Count >= 16)
                { FramesRejected++; Fault(new IOException("SDK channel inventory exceeds bounded reader policy")); return; }
                triggers[frame.Channel] = frame.Trigger;
            }
            var source = $"c{frame.Channel}.f{frame.Frame}.e{frame.Event}.t{frame.Trigger}.s{frame.TimestampHigh:x8}{frame.TimestampLow:x8}";
            // Trigger is a candidate entity boundary only under the explicit policy;
            // observe-only cannot create a business Read, even if codes are present.
            var admission = "hik." + Wire.Hash(Epoch + ":" + frame.Channel + ":" + frame.Trigger)[..40].ToLowerInvariant();
            var observation = new Observation(Wire.Id(), DeviceId, Epoch, source, ++sequence, admission,
                entityMode == "trigger" ? frame.Kind : "UncorrelatedRead", frame.Codes, null, DateTimeOffset.UtcNow);
            if (queue?.Writer.TryWrite(observation) != true) { FramesRejected++; Fault(new IOException("Reader ingress queue full; inspect parcel and reconnect explicitly; rejected source=" + source)); }
        }
    }
    async Task Consume()
    {
        try
        {
            await foreach (var observation in queue!.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                lock (gate) if (faulted) { FramesRejected++; continue; }
                try
                {
                    if (receiver?.Invoke(observation) == true) { lock (gate) FramesDelivered++; }
                    else { lock (gate) FramesRejected++; Fault(new IOException("Application rejected reader ingress; acquisition fenced; source=" + observation.SourceEventId)); }
                }
                catch (Exception ex) { lock (gate) FramesRejected++; Fault(ex); }
            }
        }
        catch (Exception ex) { Fault(ex); }
    }
    async Task Heartbeat(CancellationToken token)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                lock (gate) if (faulted || !accepting) return;
                // A node parameter can be cached by GenICam. Require fresh network
                // discovery of the original physical identity as well as a live handle.
                if (api.Enumerate().Count(d => d.Ip == ip && d.Serial == connectedSerial) != 1)
                    throw new IOException("Original camera no longer discoverable at configured IP");
                token.ThrowIfCancellationRequested();
                var mode = api.TriggerMode(); token.ThrowIfCancellationRequested();
                if (entityMode == "trigger" && mode != 1) throw new IOException("Device trigger policy changed; reconnect/qualification required");
                lock (gate) if (!faulted && accepting) Confirm(DeviceHealth.Ready);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { Fault(ex); }
    }
    void Confirm(DeviceHealth state)
    {
        var now = DateTimeOffset.UtcNow;
        // Driver liveness lease (two missed one-second polls), not an entity permit
        // or a vendor-specified safety validity. Production needs independent proof.
        health?.Invoke(new(DeviceId, Epoch, state, now, state == DeviceHealth.Ready ? now.AddSeconds(3) : null, ConfirmationSource));
    }
    void Fault(Exception failure)
    {
        lock (gate)
        {
            if (faulted) return;
            faulted = true; accepting = false; LastFailure = failure.GetType().Name + ": " + failure.Message;
            try { Confirm(DeviceHealth.Faulted); }
            catch (Exception healthFailure) { LastFailure += "; health receiver failed: " + healthFailure.GetType().Name; }
        }
    }
    public Task StopAsync(CancellationToken token)
    {
        Task owned;
        lock (gate)
        {
            // Cancellation stops this wait only. The original task keeps lifecycle,
            // delegates, native handle and physical lease until actual termination.
            if (stopTask is null || stopTask.IsFaulted || stopTask.IsCanceled)
            {
                stopTask = Task.Run(async () =>
                {
                    await lifecycle.WaitAsync().ConfigureAwait(false);
                    try { await StopCore().ConfigureAwait(false); }
                    catch (Exception ex) { Fault(ex); throw; }
                    finally { lifecycle.Release(); }
                });
                _ = stopTask.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
            owned = stopTask;
        }
        return owned.WaitAsync(token);
    }
    async Task StopCore()
    {
        lock (gate) { accepting = false; cancel?.Cancel(); if (health is not null) Confirm(DeviceHealth.Stopping); }
        if (heartbeat is not null) await heartbeat.ConfigureAwait(false);
        // Callbacks finish their bounded copy before native StopGrabbing returns;
        // delegates remain rooted until DestroyHandle has succeeded.
        if (grabbing)
        {
            try { await Task.Run(api.Stop).ConfigureAwait(false); grabbing = false; }
            catch (Exception ex) { Fault(ex); throw; }
        }
        queue?.Writer.TryComplete();
        if (consumer is not null) await consumer.ConfigureAwait(false);
        if (opened) { await Task.Run(api.Close).ConfigureAwait(false); opened = false; }
        if (created) { await Task.Run(api.Destroy).ConfigureAwait(false); created = false; }
        physicalLease?.Dispose(); physicalLease = null;
        lock (gate)
        {
            Confirm(DeviceHealth.Stopped); health = null; receiver = null; queue = null; consumer = null; heartbeat = null;
            connectedSerial = null;
            cancel?.Dispose(); cancel = null;
        }
    }
    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        await lifecycle.WaitAsync().ConfigureAwait(false);
        try { await StopCore().ConfigureAwait(false); disposed = true; }
        finally { lifecycle.Release(); }
    }
}

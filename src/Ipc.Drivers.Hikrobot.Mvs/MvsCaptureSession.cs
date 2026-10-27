using System.Diagnostics;
using System.Net;
using Ipc.Platform;

namespace Ipc.Drivers.Hikrobot.Mvs;

// Shared native owner. Both the diagnostic wrapper and the standalone service
// use this sole Get/Free/Stop implementation. It never creates observations.
public sealed class MvsCaptureSession : IControlledObservationSource
{
    readonly ExtensionContext context;
    readonly IMvsFrameSink? frameSink;
    readonly IMvsApi api;
    readonly Func<string, IDisposable> acquireLease;
    readonly Action<string>? phaseChanged;
    readonly SemaphoreSlim lifecycle = new(1, 1);
    readonly object gate = new();
    readonly string ip, serial, triggerRequirement;
    readonly int nodes, maximumPayload;
    readonly uint? triggerSource, triggerSelector;
    IDisposable? lease;
    CancellationTokenSource? cancel;
    Task? worker, stopping, disposal;
    Action<DeviceHealthConfirmation>? health;
    bool initialized, created, opened, grabbing, disposed;
    volatile bool disposeRequested;
    string phase = "not-started", epoch = "not-started", failure = "", physicalSerial = "";
    long frames, bytes;
    uint? priorFrame;
    public string DeviceId => context.DeviceId;
    public string Epoch { get { lock (gate) return epoch; } }
    public string Phase { get { lock (gate) return phase; } }
    public long Frames { get { lock (gate) return frames; } }
    public string Failure { get { lock (gate) return failure; } }
    public string ConfirmationSource { get { lock (gate) return $"SDK:hikrobot-mvs; phase={phase}; frames={frames}; bytes={bytes}; decoder=Unavailable; businessReady=false; failure={failure}"; } }
    internal MvsCaptureSession(ExtensionContext context, IMvsApi api, Func<string, IDisposable>? acquireLease = null, Action<string>? phaseChanged = null, IMvsFrameSink? frameSink = null)
    {
        this.context = context; this.api = api; this.phaseChanged = phaseChanged; this.frameSink = frameSink;
        this.acquireLease = acquireLease ?? (actualSerial => new ResourceLease(MachineRuntimePolicy.ResolveLeaseRoot(null), ["device:hikrobot-gige:" + actualSerial]));
        ip = context.Parameters.GetValueOrDefault("device.ip", ""); serial = context.Parameters.GetValueOrDefault("device.serial", "");
        if (!IPAddress.TryParse(ip, out var address) || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork || address.ToString() != ip || ip is "0.0.0.0" or "255.255.255.255")
            throw new InvalidDataException("device.ip: explicit canonical IPv4 required");
        triggerRequirement = context.Parameters.GetValueOrDefault("acquisition.trigger", "existing");
        if (triggerRequirement is not ("existing" or "on" or "off")) throw new InvalidDataException("Unknown trigger requirement");
        uint? Expected(string name)
        {
            if (!context.Parameters.TryGetValue(name, out var text)) return null;
            if (!uint.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var value))
                throw new InvalidDataException("Invalid explicit trigger configuration");
            return value;
        }
        triggerSource = Expected("acquisition.trigger-source"); triggerSelector = Expected("acquisition.trigger-selector");
        nodes = int.Parse(context.Parameters.GetValueOrDefault("image.nodes", "3"), System.Globalization.CultureInfo.InvariantCulture);
        maximumPayload = int.Parse(context.Parameters.GetValueOrDefault("image.maximum-payload-bytes", "8388608"), System.Globalization.CultureInfo.InvariantCulture);
        if (nodes is < 1 or > 4 || maximumPayload is < 1048576 or > 33554432) throw new InvalidDataException("Image nodes must be 1..4 and payload budget 1..32 MiB");
    }
    void PhaseTo(string value) { lock (gate) phase = value; phaseChanged?.Invoke(value); }
    void Publish(DeviceHealth state)
    {
        // No SDK callback exists. All host work runs on lifecycle/acquisition tasks,
        // outside the metadata lock and after returning any acquired image buffer.
        var now = DateTimeOffset.UtcNow;
        health?.Invoke(new(DeviceId, Epoch, state, now, null, ConfirmationSource));
    }
    public async Task StartControlledAsync(string registeredEpoch, Func<Observation, bool> receive, Action<DeviceHealthConfirmation> onHealth, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registeredEpoch);
        ObjectDisposedException.ThrowIf(disposeRequested, this);
        await lifecycle.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed || disposeRequested, this);
            if (initialized || created || worker is not null || lease is not null) throw new InvalidOperationException("Original acquisition owner retained; stop before restart");
            lock (gate) { epoch = registeredEpoch; failure = ""; frames = bytes = 0; priorFrame = null; health = onHealth; stopping = null; }
            try
            {
                Publish(DeviceHealth.Connecting);
                await Task.Run(() =>
                {
                    PhaseTo("initialize"); initialized = true; api.Initialize(); token.ThrowIfCancellationRequested();
                    PhaseTo("enumerate");
                    var matches = api.Enumerate().Where(d => d.Ip == ip && (serial.Length == 0 || serial == d.Serial)).ToArray();
                    token.ThrowIfCancellationRequested();
                    if (matches.Length != 1 || string.IsNullOrWhiteSpace(matches[0].Serial)) throw new InvalidDataException("Configured MVS IP/serial not uniquely discovered");
                    if (physicalSerial.Length != 0 && physicalSerial != matches[0].Serial)
                        throw new InvalidDataException("Original MVS physical serial changed; revise the approved device binding before replacement");
                    PhaseTo("lease"); lease = acquireLease(matches[0].Serial); physicalSerial = matches[0].Serial;
                    PhaseTo("create"); created = true; api.Create(matches[0]); token.ThrowIfCancellationRequested();
                    PhaseTo("open-exclusive"); api.Open(); opened = true; token.ThrowIfCancellationRequested();
                    PhaseTo("read-parameters");
                    var trigger = api.EnumValue("TriggerMode");
                    if (triggerRequirement == "on" && trigger != 1 || triggerRequirement == "off" && trigger != 0)
                        throw new InvalidDataException("Configured trigger requirement differs from device; no trigger settings were written");
                    ValidateTriggerIdentity();
                    if (api.EnumValue("PixelFormat") != MvsNative.Mono8) throw new InvalidDataException("Current target acquisition requires existing Mono8; pixel format was not changed");
                    var payload = api.PayloadSize();
                    if (payload <= 0 || payload > maximumPayload) throw new InvalidDataException("PayloadSize exceeds configured image byte budget");
                    token.ThrowIfCancellationRequested(); PhaseTo("configure-sdk-buffers"); api.ConfigureBuffers((uint)nodes);
                    token.ThrowIfCancellationRequested(); PhaseTo("start-grabbing"); grabbing = true; api.Start(); token.ThrowIfCancellationRequested();
                    cancel = new(); PhaseTo("acquiring-decoder-unavailable"); Publish(DeviceHealth.Unknown);
                    worker = Task.Run(() => Capture(cancel.Token));
                }, token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                lock (gate) failure = ex.GetType().Name + ": " + ex.Message;
                try { await StopCore().ConfigureAwait(false); }
                catch (Exception stop) { throw new AggregateException("MVS startup/cleanup failed; original owner retained", ex, stop); }
                throw;
            }
        }
        finally { lifecycle.Release(); }
    }
    void Capture(CancellationToken token)
    {
        var poll = Stopwatch.StartNew();
        try
        {
            while (!token.IsCancellationRequested)
            {
                PhaseTo("get-image-buffer");
                var frame = api.Acquire();
                if (frame is not null)
                {
                    IMvsFrameCopy? copy = null;
                    try
                    {
                        ValidateFrame(frame, maximumPayload);
                        lock (gate)
                        {
                            if (priorFrame is { } prior && (frame.Number <= prior || frame.Number - prior != 1))
                                throw new IOException("MVS frame sequence gap/reset; inspect acquisition load and original parcel before restart");
                            priorFrame = frame.Number; frames++; bytes += checked((long)frame.Length);
                        }
                        if (frameSink is not null)
                        {
                            if ((ulong)frame.Width * frame.Height != frame.Length)
                                throw new InvalidDataException("MVS service requires a verified tight Mono8 stride");
                            try
                            {
                                copy = frameSink.Copy(new(frame.Data, checked((int)frame.Width), checked((int)frame.Height),
                                    checked((int)frame.Width), checked((int)frame.Length), frame.Number, frame.DeviceTimestamp, frame.Trigger), Epoch);
                            }
                            catch { throw new IOException("MVS frame copy failed (details omitted)"); }
                            if (copy is null) throw new IOException("MVS service copy rejected or bounded pool exhausted");
                        }
                    }
                    finally
                    {
                        try { PhaseTo("free-image-buffer"); api.Release(); }
                        catch (Exception release)
                        {
                            try { copy?.Reject(); }
                            catch { throw new AggregateException("MVS Free and copied-frame rejection failed", release,
                                new IOException("Copied-frame rejection failed (details omitted)")); }
                            throw;
                        }
                    }
                    // No decode or network operation is permitted in these sink
                    // methods. The service enqueues its OWN copy only after Free.
                    try { copy?.Submit(); }
                    catch { throw new IOException("MVS copied-frame transfer failed (details omitted)"); }
                }
                if (token.IsCancellationRequested) break;
                if (poll.Elapsed >= TimeSpan.FromSeconds(1))
                {
                    PhaseTo("connection-check");
                    if (!api.Connected() || api.Enumerate().Count(d => d.Ip == ip && d.Serial == physicalSerial) != 1)
                        throw new IOException("MVS original camera disconnected or identity changed");
                    if (triggerRequirement == "on" && api.EnumValue("TriggerMode") != 1 || triggerRequirement == "off" && api.EnumValue("TriggerMode") != 0)
                        throw new IOException("MVS device trigger setting changed");
                    ValidateTriggerIdentity();
                    PhaseTo("acquiring-decoder-unavailable"); Publish(DeviceHealth.Unknown); poll.Restart();
                }
            }
        }
        catch (Exception ex)
        {
            lock (gate) failure = ex.GetType().Name + ": " + ex.Message;
            try { Publish(DeviceHealth.Faulted); }
            catch (Exception host) { lock (gate) failure += "; health receiver=" + host.GetType().Name; }
        }
    }
    internal static void ValidateFrame(MvsFrame frame, int limit)
    {
        if (frame.Data == 0 || frame.Width == 0 || frame.Height == 0 || frame.PixelType != MvsNative.Mono8 || frame.Length == 0 || frame.Length > (ulong)limit ||
            (ulong)frame.Width * frame.Height > frame.Length || frame.LostPackets != 0 || frame.ExtraType != 0 || frame.SubImages != 0)
            throw new InvalidDataException("MVS image missing/oversized/incomplete or incompatible with current Mono8 single-image acquisition");
    }
    void ValidateTriggerIdentity()
    {
        if (triggerSource is { } source && api.EnumValue("TriggerSource") != source ||
            triggerSelector is { } selector && api.EnumValue("TriggerSelector") != selector)
            throw new InvalidDataException("MVS trigger source or selector differs from the explicit configuration");
    }
    public Task StopAsync(CancellationToken token)
    {
        Task owned;
        lock (gate)
        {
            if (stopping is null || stopping.IsFaulted || stopping.IsCanceled)
            {
                stopping = Task.Run(async () =>
                {
                    await lifecycle.WaitAsync().ConfigureAwait(false);
                    try { await StopCore().ConfigureAwait(false); }
                    catch (Exception ex) { lock (gate) failure = ex.GetType().Name + ": " + ex.Message; throw; }
                    finally { lifecycle.Release(); }
                });
                _ = stopping.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
            owned = stopping;
        }
        return owned.WaitAsync(token);
    }
    async Task StopCore()
    {
        cancel?.Cancel();
        // Wait for the actual GetImageBuffer/health call, not a cancelled wrapper.
        if (worker is not null) await worker.ConfigureAwait(false);
        Exception? reportingFailure = null;
        void Report(DeviceHealth state)
        {
            try { Publish(state); }
            catch (Exception ex) { reportingFailure ??= ex; }
        }
        // A failed host notification must not prevent native teardown. Native
        // errors still retain the original owner and stop at the failed step.
        Report(DeviceHealth.Stopping);
        if (api.HasFrame) { PhaseTo("retry-free-original-buffer"); await Task.Run(api.Release).ConfigureAwait(false); }
        if (grabbing) { PhaseTo("stop-grabbing"); await Task.Run(api.Stop).ConfigureAwait(false); grabbing = false; }
        if (opened) { PhaseTo("close"); await Task.Run(api.Close).ConfigureAwait(false); opened = false; }
        if (created) { PhaseTo("destroy"); await Task.Run(api.Destroy).ConfigureAwait(false); created = false; }
        if (initialized) { PhaseTo("finalize"); await Task.Run(api.Shutdown).ConfigureAwait(false); initialized = false; }
        lease?.Dispose(); lease = null; worker = null; cancel?.Dispose(); cancel = null;
        PhaseTo("stopped"); Report(DeviceHealth.Stopped); health = null;
        if (reportingFailure is not null)
            throw new IOException("MVS native teardown completed; health receiver failed: " + reportingFailure.GetType().Name, reportingFailure);
    }
    public ValueTask DisposeAsync()
    {
        lock (gate)
        {
            // Fence Start immediately, including while a prior native call is
            // blocked. A failed dispose retains ownership and can be retried.
            disposeRequested = true;
            if (disposal is null || disposal.IsFaulted || disposal.IsCanceled)
                disposal = Task.Run(async () =>
                {
                    await lifecycle.WaitAsync().ConfigureAwait(false);
                    try { await StopCore().ConfigureAwait(false); disposed = true; }
                    catch (Exception ex) { lock (gate) failure = ex.GetType().Name + ": " + ex.Message; throw; }
                    finally { lifecycle.Release(); }
                });
            return new(disposal);
        }
    }
}

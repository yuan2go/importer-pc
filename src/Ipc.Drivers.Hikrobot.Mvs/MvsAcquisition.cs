using System.Buffers;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Net;
using Ipc.Platform;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Ipc.Drivers.Hikrobot.Probe")]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Ipc.Application.Verification")]
namespace Ipc.Drivers.Hikrobot.Mvs;

public sealed class MvsAcquisitionModule : IObservationExtension
{
    public IControlledObservationSource Create(ExtensionContext context) => new MvsAcquisition(context, new MvsApi());
}

// One acquisition/resource owner. Legacy acquisition has no decoder; the current
// reader injects an actual in-process decoder and emits only decoded candidates.
internal sealed class MvsAcquisition : IControlledObservationSource
{
    readonly ExtensionContext context;
    readonly IMvsApi api;
    readonly IMonoBarcodeDecoder? decoder;
    readonly string entityMode;
    Func<Observation, bool>? receiver;
    internal Observation? Undelivered { get; private set; }
    long recognized, candidates, sequence, skippedVideoFrames, emptyFrames;
    readonly Func<string, IDisposable> acquireLease;
    readonly Action<string>? phaseChanged;
    readonly SemaphoreSlim lifecycle = new(1, 1);
    readonly object gate = new();
    readonly string ip, serial, triggerRequirement;
    readonly int nodes, maximumPayload;
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
    public string ConfirmationSource { get { lock (gate) return $"SDK:{(decoder is null ? "hikrobot-mvs" : "hikrobot-mvs-reader")}; phase={phase}; frames={frames}; bytes={bytes}; decoder={(decoder is null ? "Unavailable" : "ZXingCpp-3.1.1")}; businessReady={(decoder is null ? "false" : "ApplicationGuardsRequired")}; readResults={recognized}; candidates={candidates}; emptyFrames={emptyFrames}; skippedVideoFrames={skippedVideoFrames}; failure={failure}"; } }
    internal MvsAcquisition(ExtensionContext context, IMvsApi api, Func<string, IDisposable>? acquireLease = null, Action<string>? phaseChanged = null, IMonoBarcodeDecoder? decoder = null)
    {
        this.context = context; this.api = api; this.phaseChanged = phaseChanged; this.decoder = decoder;
        entityMode = context.Parameters.GetValueOrDefault("entity.mode", "observe-only");
        this.acquireLease = acquireLease ?? (actualSerial => new ResourceLease(MachineRuntimePolicy.ResolveLeaseRoot(null), ["device:hikrobot-gige:" + actualSerial]));
        ip = context.Parameters.GetValueOrDefault("device.ip", ""); serial = context.Parameters.GetValueOrDefault("device.serial", "");
        if (!IPAddress.TryParse(ip, out var address) || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork || address.ToString() != ip || ip is "0.0.0.0" or "255.255.255.255")
            throw new InvalidDataException("device.ip: explicit canonical IPv4 required");
        if (decoder is null && entityMode != "observe-only" || decoder is not null && entityMode is not ("read-result" or "observe-only"))
            throw new InvalidDataException("DecoderUnavailable: acquisition cannot create parcel admissions; entity.mode must be observe-only");
        triggerRequirement = context.Parameters.GetValueOrDefault("acquisition.trigger", "existing");
        if (triggerRequirement is not ("existing" or "on" or "off")) throw new InvalidDataException("Unknown trigger requirement");
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
        health?.Invoke(new(DeviceId, Epoch, state, now, state == DeviceHealth.Ready ? now.AddSeconds(3) : null, ConfirmationSource));
    }
    public async Task StartControlledAsync(string registeredEpoch, Func<Observation, bool> receive, Action<DeviceHealthConfirmation> onHealth, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registeredEpoch);
        ObjectDisposedException.ThrowIf(disposeRequested, this);
        await lifecycle.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed || disposeRequested, this);
            if (initialized || created || worker is not null || lease is not null || Undelivered is not null) throw new InvalidOperationException("Original acquisition owner retained; stop before restart");
            lock (gate) { epoch = registeredEpoch; failure = ""; frames = bytes = recognized = candidates = sequence = skippedVideoFrames = emptyFrames = 0; receiver = receive; priorFrame = null; health = onHealth; stopping = null; }
            try
            {
                Publish(DeviceHealth.Connecting);
                await Task.Run(() =>
                {
                    if (decoder is not null) { PhaseTo("initialize-recognition"); decoder.Prepare(); token.ThrowIfCancellationRequested(); }
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
                    if (api.EnumValue("PixelFormat") != MvsNative.Mono8) throw new InvalidDataException("Current target acquisition requires existing Mono8; pixel format was not changed");
                    var payload = api.PayloadSize();
                    if (payload <= 0 || payload > maximumPayload) throw new InvalidDataException("PayloadSize exceeds configured image byte budget");
                    token.ThrowIfCancellationRequested(); PhaseTo("configure-sdk-buffers"); api.ConfigureBuffers((uint)nodes);
                    token.ThrowIfCancellationRequested(); PhaseTo("start-grabbing"); grabbing = true; api.Start(); token.ThrowIfCancellationRequested();
                    cancel = new(); PhaseTo(decoder is null ? "acquiring-decoder-unavailable" : "reading"); Publish(decoder is null ? DeviceHealth.Unknown : DeviceHealth.Ready);
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
                    byte[]? pixels = null; var length = 0; var observedAt = DateTimeOffset.UtcNow;
                    try
                    {
                        ValidateFrame(frame, maximumPayload);
                        lock (gate)
                        {
                            if (priorFrame is { } prior)
                            {
                                if (frame.Number <= prior || decoder is null && frame.Number - prior != 1)
                                    throw new IOException("MVS frame sequence gap/reset; inspect original source before restart");
                                skippedVideoFrames += frame.Number - prior - 1;
                            }
                            priorFrame = frame.Number; frames++; bytes += checked((long)frame.Length);
                        }
                        if (decoder is not null)
                        {
                            length = checked((int)(frame.Width * frame.Height)); pixels = ArrayPool<byte>.Shared.Rent(length);
                            Marshal.Copy(frame.Data, pixels, 0, length);
                        }
                    }
                    catch { if (pixels is not null) { ArrayPool<byte>.Shared.Return(pixels, true); pixels = null; } throw; }
                    finally
                    {
                        PhaseTo("free-image-buffer");
                        try { api.Release(); }
                        catch { if (pixels is not null) { ArrayPool<byte>.Shared.Return(pixels, true); pixels = null; } throw; }
                    }
                    if (pixels is not null)
                    {
                        string[] codes;
                        try { PhaseTo("decode"); codes = decoder!.Decode(pixels, length, checked((int)frame.Width), checked((int)frame.Height)); }
                        finally { ArrayPool<byte>.Shared.Return(pixels, true); }
                        if (codes.Length == 0) { lock (gate) emptyFrames++; }
                        else if (!token.IsCancellationRequested)
                        {
                            var source = "mvs.frame." + frame.Number;
                            var admission = "mvs.read." + Wire.Hash(DeviceId + ":" + Epoch + ":" + source)[..40].ToLowerInvariant();
                            var observation = new Observation(Wire.Id(), DeviceId, Epoch, source, ++sequence, admission,
                                entityMode == "read-result" ? "Read" : "UncorrelatedRead", codes, null, observedAt);
                            Undelivered = observation;
                            if (receiver?.Invoke(observation) != true) throw new IOException("Application rejected decoded input; original decoded observation retained; source fenced");
                            Undelivered = null;
                            lock (gate) { recognized++; candidates += codes.Length; }
                        }
                    }
                }
                if (token.IsCancellationRequested) break;
                if (poll.Elapsed >= TimeSpan.FromSeconds(1))
                {
                    PhaseTo("connection-check");
                    if (!api.Connected() || api.Enumerate().Count(d => d.Ip == ip && d.Serial == physicalSerial) != 1)
                        throw new IOException("MVS original camera disconnected or identity changed");
                    if (triggerRequirement == "on" && api.EnumValue("TriggerMode") != 1 || triggerRequirement == "off" && api.EnumValue("TriggerMode") != 0)
                        throw new IOException("MVS device trigger setting changed");
                    PhaseTo(decoder is null ? "acquiring-decoder-unavailable" : "reading"); Publish(decoder is null ? DeviceHealth.Unknown : DeviceHealth.Ready); poll.Restart();
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
        decoder?.Release();
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

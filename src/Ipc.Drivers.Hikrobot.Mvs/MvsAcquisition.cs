using System.Buffers;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Net;
using Ipc.Platform;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Ipc.Drivers.Hikrobot.Probe")]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Ipc.Application.Verification")]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Ipc.Desktop.UiVerification")]
namespace Ipc.Drivers.Hikrobot.Mvs;

public sealed class MvsAcquisitionModule : IObservationExtension
{
    public IControlledObservationSource Create(ExtensionContext context) => new MvsAcquisition(context, new MvsApi());
}

// One acquisition/resource owner. Legacy acquisition has no decoder; the current
// reader injects an actual in-process decoder and emits only decoded candidates.
internal sealed partial class MvsAcquisition : IAutomaticReconnectObservationSource, IOriginalFrameSource, IObservationPreviewSource

{
    readonly MvsPreview preview = new();
    public ObservationPreview? ReadPreview() => disposeRequested ? null : preview.Read();
    readonly ExtensionContext context;
    readonly IMvsApi api;
    readonly IMonoBarcodeDecoder? decoder;
    readonly bool diagnosticVideoSampling;
    readonly MvsOriginalFrames originals = new();
    public void EnableOriginalFrames()
    {
        lock (gate) { if (worker is not null || decoder is null) throw new InvalidOperationException("Bind original frames before software reader startup"); originals.Enable(); }
    }
    public IOriginalFrameLease RetainOriginalFrame(AcquisitionIdentity identity) => originals.Retain(identity);
    public void ReleaseOriginalFrame(AcquisitionIdentity identity) => originals.Release(identity);
    internal int RetainedOriginalFrames => originals.Count;
    internal MvsTimings Timings { get; } = new();
    internal MvsTimingSnapshot? DecoderTimings => decoder?.TimingSnapshot;
    readonly string entityMode;
    Func<Observation, bool>? receiver;
    internal Observation? Undelivered { get; private set; }
    long recognized, candidates, sequence, skippedVideoFrames, videoGapEvents, emptyFrames;
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
    public string ConfirmationSource { get { lock (gate) return $"SDK:{(decoder is null ? "hikrobot-mvs" : "hikrobot-mvs-reader")}; phase={phase}; frames={frames}; bytes={bytes}; decoder={(decoder is null ? "Unavailable" : "ZXingCpp-3.1.1")}; businessReady={(decoder is null ? "false" : "ApplicationGuardsRequired")}; readResults={recognized}; candidates={candidates}; emptyFrames={emptyFrames}; skippedVideoFrames={skippedVideoFrames}; videoGapEvents={videoGapEvents}; failure={failure}"; } }
    internal MvsAcquisition(ExtensionContext context, IMvsApi api, Func<string, IDisposable>? acquireLease = null, Action<string>? phaseChanged = null, IMonoBarcodeDecoder? decoder = null, bool diagnosticVideoSampling = false)
    {
        this.context = context; this.api = api; this.phaseChanged = phaseChanged; this.decoder = decoder;
        this.diagnosticVideoSampling = diagnosticVideoSampling;
        if (diagnosticVideoSampling && decoder is not null) throw new ArgumentException("Raw throughput diagnostic must not contain a decoder");
        entityMode = context.Parameters.GetValueOrDefault("entity.mode", decoder is null ? "observe-only" : "read-result");
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
            Timings.Reset();
            lock (gate) { epoch = registeredEpoch; failure = ""; frames = bytes = recognized = candidates = sequence = skippedVideoFrames = videoGapEvents = emptyFrames = 0; receiver = receive; priorFrame = null; health = onHealth; stopping = null; ResetDiagnostics(); }
            try
            {
                preview.Clear();
                Publish(DeviceHealth.Connecting);
                await Task.Run(() =>
                {
                    if (decoder is not null) { PhaseTo("initialize-recognition"); decoder.Prepare(); token.ThrowIfCancellationRequested(); }
                    PhaseTo("initialize"); initialized = true; api.Initialize(); token.ThrowIfCancellationRequested();
                    PhaseTo("enumerate");
                    var matches = api.Enumerate().Where(d => d.Ip == ip && (serial.Length == 0 || serial == d.Serial)).ToArray();
                    token.ThrowIfCancellationRequested();
                    if (matches.Length != 1 || string.IsNullOrWhiteSpace(matches[0].Serial))
                        throw new ObservationConnectionException("配置的MVS相机未发现或设备身份不匹配；连接保持离线，原参数保留。",
                            new InvalidDataException("Configured MVS IP/serial not uniquely discovered"));
                    if (physicalSerial.Length != 0 && physicalSerial != matches[0].Serial)
                        throw new InvalidDataException("Original MVS physical serial changed; revise the approved device binding before replacement");
                    PhaseTo("lease"); lease = acquireLease(matches[0].Serial); physicalSerial = matches[0].Serial;
                    PhaseTo("create"); created = true; api.Create(matches[0]); token.ThrowIfCancellationRequested();
                    PhaseTo("open-exclusive");
                    try { api.Open(); }
                    catch (IOException error) { throw new ObservationConnectionException("MVS相机暂时无法取得独占连接。", error); }
                    opened = true; token.ThrowIfCancellationRequested();
                    PhaseTo("read-parameters");
                    if (decoder is not null)
                    {
                        // Current standalone station has no PLC/trigger dependency.
                        // Set only when necessary and verify actual device readback.
                        foreach (var (key, value) in new[] { ("TriggerMode", 0u), ("AcquisitionMode", 2u) })
                        {
                            if (api.EnumValue(key) != value) api.SetEnumValue(key, value);
                            if (api.EnumValue(key) != value) throw new InvalidDataException("Continuous acquisition readback mismatch: " + key);
                        }
                    }
                    var trigger = api.EnumValue("TriggerMode");
                    if (decoder is null && (triggerRequirement == "on" && trigger != 1 || triggerRequirement == "off" && trigger != 0))
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
                if (ex is ObservationConnectionException)
                {
                    // Only after successful native/decoder cleanup may this be a
                    // recoverable position dependency. Configuration and other
                    // capabilities remain active; no frame or Ready is invented.
                    PhaseTo("disconnected");
                    var now = DateTimeOffset.UtcNow;
                    onHealth(new(DeviceId, registeredEpoch, DeviceHealth.Disconnected, now, null, ConfirmationSource));
                }
                throw;
            }
        }
        finally { lifecycle.Release(); }
    }
    void Capture(CancellationToken token)
    {
        var poll = Stopwatch.StartNew();
        long previousDelivery = 0;
        try
        {
            while (!token.IsCancellationRequested)
            {
                ApplyInspectionCacheCutover();
                var inspected = BeginInspectedCycle();
                try
                {
                PhaseTo("get-image-buffer");
                MvsFrame? frame;
                DateTimeOffset observedAt;
                long deliveredTicks;
                var acquireStarted = Stopwatch.GetTimestamp();
                try
                {
                    lock (gate) acquireCalls++;
                    frame = api.Acquire();
                    // Preserve the original clock boundary immediately after
                    // SDK return, before recording any diagnostic samples.
                    observedAt = DateTimeOffset.UtcNow;
                    deliveredTicks = Stopwatch.GetTimestamp();
                }
                catch (IOException error) when (!api.Connected())
                { throw new ObservationConnectionException("MVS相机连接已断开。", error); }
                finally { Timings.Record("sdk.acquire", Stopwatch.GetElapsedTime(acquireStarted).TotalMilliseconds); }
                Timings.Record(frame is null ? "sdk.acquire.no-data" : "sdk.acquire.frame", Timings.LastMs("sdk.acquire"));
                if (frame is null) { lock (gate) noData++; }
                if (frame is not null)
                {
                    if (previousDelivery != 0) Timings.Record("host.delivery-interval", Stopwatch.GetElapsedTime(previousDelivery, deliveredTicks).TotalMilliseconds);
                    previousDelivery = deliveredTicks;
                    byte[]? pixels = null; var length = 0; long skippedBefore = 0;

                    try
                    {
                        ValidateFrame(frame, maximumPayload);
                        var allowDecode = inspected.Registration?.DecodeAllowed?.Invoke(DeviceId) != false;
                        lock (gate) decodePaused = !allowDecode;
                        if (decoder is not null && (ulong)frame.Width * frame.Height != frame.Length)
                            throw new InvalidDataException("MVS software decoding requires a verified tight Mono8 stride");
                        lock (gate)
                        {
                            if (priorFrame is { } prior)
                            {
                                if (frame.Number <= prior || decoder is null && !diagnosticVideoSampling && frame.Number - prior != 1)
                                    throw new IOException("MVS frame sequence gap/reset; inspect original source before restart");
                                skippedBefore = frame.Number - prior - 1; skippedVideoFrames += skippedBefore;
                                if (skippedBefore > 0) videoGapEvents++;

                            }
                            priorFrame = frame.Number; frames++; bytes += checked((long)frame.Length);
                            lastFrameAt = observedAt; lastFrameTicks = deliveredTicks;
                        }
                        // Preview samples the same validated native loan, independently of
                        // one-shot decoding. It never creates an observation or retains the loan.
                        preview.Offer(frame.Data, checked((int)frame.Width), checked((int)frame.Height), DeviceId, Epoch, frame.Number, observedAt);
                        if (decoder is not null && allowDecode)
                        {
                            using var copy = Timings.Measure("image.rent-copy");
                            length = checked((int)(frame.Width * frame.Height)); pixels = ArrayPool<byte>.Shared.Rent(length);
                            Marshal.Copy(frame.Data, pixels, 0, length);
                        }
                    }
                    catch { if (pixels is not null) { ArrayPool<byte>.Shared.Return(pixels, true); pixels = null; } throw; }
                    finally
                    {
                        PhaseTo("free-image-buffer");
                        try { using (Timings.Measure("sdk.release")) api.Release(); }
                        catch { if (pixels is not null) { ArrayPool<byte>.Shared.Return(pixels, true); pixels = null; } throw; }
                    }
                    if (pixels is not null)
                    {
                        MvsOriginalFrames.Frame? original = null;
                        string[] codes;
                        RecognitionSymbol[] symbols;
                        byte[]? inspectionOriginal = null;
                        try
                        {
                            using (Timings.Measure("image.original-copy"))
                                if (inspected.Registration?.Isolated != true) original = originals.Copy(pixels, length, checked((int)frame.Width), checked((int)frame.Height));
                            using (Timings.Measure("inspection.original-copy"))
                                if (inspected.Registration is { CaptureImages: true }) inspectionOriginal = pixels.AsSpan(0, length).ToArray();
                            PhaseTo("decode");
                            lock (gate) decodeStarted++;
                            using (Timings.Measure("decode")) symbols = decoder!.DecodeDetailed(pixels, length, checked((int)frame.Width), checked((int)frame.Height));
                            codes = symbols.Select(symbol => symbol.Text).ToArray();
                            lock (gate) { decodeCompleted++; decodedCandidates += codes.Length; lastDecodeAt = DateTimeOffset.UtcNow; }
                        }
                        catch { original?.Dispose(); throw; }
                        finally { using (Timings.Measure("image.clear-return")) ArrayPool<byte>.Shared.Return(pixels, true); }
                        var observationIdentity = codes.Length == 0 ? null : Wire.Id();
                        var sourceEvent = codes.Length == 0 ? null : "mvs.frame." + frame.Number;
                        var admissionIdentity = sourceEvent is null ? null : "mvs.read." + Wire.Hash(DeviceId + ":" + Epoch + ":" + sourceEvent)[..40].ToLowerInvariant();
                        var acquisitionIdentity = sourceEvent is null ? null : new AcquisitionIdentity(DeviceId, Epoch, sourceEvent, admissionIdentity!, observedAt);
                        if (inspected.Registration is { } registration && inspectionOriginal is not null)
                            PublishInspection(registration, new(DeviceId, Epoch, frame.Number, observedAt, checked((int)frame.Width), checked((int)frame.Height),
                                inspectionOriginal, symbols, Timings.LastMs("decode"), observationIdentity,
                                sourceEvent, registration.Isolated ? registration.Id : null, skippedBefore, physicalSerial, ip) { Acquisition = acquisitionIdentity });
                        if (codes.Length == 0)
                        {
                            original?.Dispose();
                            lock (gate) emptyFrames++;
                            Timings.Frame(new(frame.Number, observedAt, frame.DeviceTimestampRaw, frame.HostTimestampRaw,
                                Timings.LastMs("image.rent-copy"), Timings.LastMs("sdk.release"), Timings.LastMs("decode"), Timings.LastMs("image.clear-return"), 0, 0, 0, false) { OriginalCopyMs = Timings.LastMs("image.original-copy") });
                        }

                        else
                        {
                            var source = sourceEvent!;
                            var admission = admissionIdentity!;
                            var observation = new Observation(observationIdentity!, DeviceId, Epoch, source, ++sequence, admission,
                                entityMode == "read-result" ? "Read" : "UncorrelatedRead", codes, null, observedAt)
                            {
                                InspectionSessionId = inspected.Registration is { Isolated: true } isolated ? isolated.Id : null,
                                CaptureSessionId = inspected.Registration is { CaptureImages: true } capture ? capture.Id : null
                            };
                            try { originals.Publish(acquisitionIdentity!, original); }
                            catch { original?.Dispose(); throw; }

                            Undelivered = observation;
                            if (token.IsCancellationRequested)
                                throw new IOException("Stop requested during decoding; original decoded observation retained; source fenced");
                            Timings.Record("host.observed-to-receiver", Stopwatch.GetElapsedTime(deliveredTicks).TotalMilliseconds);
                            var delivered = false;
                            try
                            {
                                using (Timings.Measure("application.durable-handoff"))
                                    delivered = receiver?.Invoke(observation) == true;
                                if (!delivered) throw new IOException("Application rejected decoded input; original decoded observation retained; source fenced");
                            }
                            finally
                            {
                                if (!delivered) { lock (gate) rejected++; }
                                Timings.Frame(new(frame.Number, observedAt, frame.DeviceTimestampRaw, frame.HostTimestampRaw,
                                    Timings.LastMs("image.rent-copy"), Timings.LastMs("sdk.release"), Timings.LastMs("decode"), Timings.LastMs("image.clear-return"),
                                    Timings.LastMs("host.observed-to-receiver"), Timings.LastMs("application.durable-handoff"), codes.Length, delivered) { OriginalCopyMs = Timings.LastMs("image.original-copy") });
                            }
                            Timings.Record("host.observed-to-handoff-complete", Stopwatch.GetElapsedTime(deliveredTicks).TotalMilliseconds);
                            Undelivered = null;
                            lock (gate) { recognized++; candidates += codes.Length; }
                        }
                    }
                    else Timings.Frame(new(frame.Number, observedAt, frame.DeviceTimestampRaw, frame.HostTimestampRaw,
                        0, Timings.LastMs("sdk.release"), 0, 0, 0, 0, 0, false));
                    Timings.Record("host.frame-work", Stopwatch.GetElapsedTime(deliveredTicks).TotalMilliseconds);
                }
                if (token.IsCancellationRequested) break;
                if (poll.Elapsed >= TimeSpan.FromSeconds(1))
                {
                    PhaseTo("connection-check");
                    using var check = Timings.Measure("health.total");
                    bool connected;
                    using (Timings.Measure("health.connected")) connected = api.Connected();
                    var identityMatches = false;
                    if (connected)
                        using (Timings.Measure("health.enumerate")) identityMatches = api.Enumerate().Count(d => d.Ip == ip && d.Serial == physicalSerial) == 1;
                    if (!connected || !identityMatches)
                        throw new ObservationConnectionException("MVS原相机断开或当前设备身份不匹配。", new IOException("Original camera unavailable"));
                    using (Timings.Measure("health.trigger-readback"))
                    if (triggerRequirement == "on" && api.EnumValue("TriggerMode") != 1 || triggerRequirement == "off" && api.EnumValue("TriggerMode") != 0)
                        throw new IOException("MVS device trigger setting changed");
                    PhaseTo(decoder is null ? "acquiring-decoder-unavailable" : inspected.Registration?.DecodeAllowed?.Invoke(DeviceId) == false ? "read-paused" : "reading"); Publish(decoder is null ? DeviceHealth.Unknown : DeviceHealth.Ready); poll.Restart();
                }
                }
                finally { EndInspectedCycle(inspected.Generation); }
            }
        }
        catch (Exception ex)
        {
            preview.Clear();
            lock (gate) failure = ex.GetType().Name + ": " + ex.Message;
            try { Publish(ex is ObservationConnectionException ? DeviceHealth.Disconnected : DeviceHealth.Faulted); }
            catch (Exception host) { lock (gate) failure += "; health receiver=" + host.GetType().Name; }
        }
    }
    internal static void ValidateFrame(MvsFrame frame, int limit)
    {
        if (frame.Data == 0 || frame.Width == 0 || frame.Height == 0 || frame.PixelType != MvsNative.Mono8 || frame.Length == 0 || frame.Length > (ulong)limit ||
            (ulong)frame.Width * frame.Height > frame.Length || frame.LostPackets != 0 || frame.ExtraType != 0 || frame.SubImages != 0)
            throw new InvalidDataException($"MVS image missing/oversized/incomplete or incompatible with current Mono8 single-image acquisition: " +
                $"buffer={(frame.Data == 0 ? "null" : "present")}; width={frame.Width}; height={frame.Height}; pixel=0x{frame.PixelType:X8}; " +
                $"length={frame.Length}; limit={limit}; lostPackets={frame.LostPackets}; extraType={frame.ExtraType}; subImages={frame.SubImages}");
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
        preview.Clear();
        cancel?.Cancel();
        PhaseTo("stop-requested");
        // Wait for the actual GetImageBuffer/health call, not a cancelled wrapper.
        if (worker is not null) await worker.ConfigureAwait(false);
        preview.Clear();
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
                    try { await StopCore().ConfigureAwait(false); originals.Dispose(); disposed = true; }
                    catch (Exception ex) { lock (gate) failure = ex.GetType().Name + ": " + ex.Message; throw; }
                    finally { lifecycle.Release(); }
                });
            return new(disposal);
        }
    }
}

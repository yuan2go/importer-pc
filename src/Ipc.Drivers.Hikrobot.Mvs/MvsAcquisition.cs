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
internal sealed partial class MvsAcquisition : IAutomaticReconnectObservationSource, IOriginalFrameSource, IObservationPreviewSource, IObservationPreviewLeaseSource

{
    readonly MvsPreview preview = new();
    public ObservationPreview? ReadPreview() => disposeRequested ? null : preview.Read();
    public ObservationPreviewLease? ReadPreviewLease() => disposeRequested ? null : preview.ReadLease();
    readonly ExtensionContext context;
    readonly IMvsApi api;
    readonly IMonoBarcodeDecoder? decoder;
    bool Recognizes => decoder is not null || api.ReturnsDecodedFrames;
    string FramePrefix => api.ReturnsDecodedFrames ? "mvid" : "mvs";
    readonly bool diagnosticVideoSampling;
    readonly MvsOriginalFrames originals = new();
    public void EnableOriginalFrames()
    {
        lock (gate) { if (worker is not null || !Recognizes) throw new InvalidOperationException("Bind original frames before software reader startup"); originals.Enable(); }
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
    readonly int nodes, maximumPayload, stillMs;
    MvsStillness stillness = new(0, 0);
    long movingFrames, fallbackDecodes;
    IDisposable? lease;
    CancellationTokenSource? cancel;
    Task? worker, stopping, disposal;
    Action<DeviceHealthConfirmation>? health;
    bool initialized, created, opened, grabbing, disposed;
    volatile bool disposeRequested;
    string phase = "not-started", epoch = "not-started", failure = "", physicalSerial = "", teardown = "";
    long frames, bytes;
    uint? priorFrame;
    public string DeviceId => context.DeviceId;
    public string Epoch { get { lock (gate) return epoch; } }
    public string Phase { get { lock (gate) return phase; } }
    public long Frames { get { lock (gate) return frames; } }
    public string Failure { get { lock (gate) return failure; } }
    // SDK errors from StopGrabbing/CloseDevice in the last teardown that did complete; empty when clean.
    public string TeardownNotes { get { lock (gate) return teardown; } }
    public string ConfirmationSource { get { lock (gate) return $"SDK:{(api.ReturnsDecodedFrames ? "hikrobot-mvid-native" : !Recognizes ? "hikrobot-mvs" : "hikrobot-mvs-reader")}; phase={phase}; frames={frames}; bytes={bytes}; decoder={(!Recognizes ? "Unavailable" : api.ReturnsDecodedFrames ? "MVIDCodeReader-1.1.4.0" : "ZXingCpp-3.1.1")}; businessReady={(!Recognizes ? "false" : "ApplicationGuardsRequired")}; readResults={recognized}; candidates={candidates}; emptyFrames={emptyFrames}; skippedVideoFrames={skippedVideoFrames}; videoGapEvents={videoGapEvents}; rejectedFrames={rejectedFrames}; lastFrameRejection={lastFrameRejection}; stillMs={stillMs}; movingFrames={movingFrames}; fallbackDecodes={fallbackDecodes}; failure={failure}; teardown={teardown}"; } }
    internal MvsAcquisition(ExtensionContext context, IMvsApi api, Func<string, IDisposable>? acquireLease = null, Action<string>? phaseChanged = null, IMonoBarcodeDecoder? decoder = null, bool diagnosticVideoSampling = false)
    {
        this.context = context; this.api = api; this.phaseChanged = phaseChanged; this.decoder = decoder;
        if (api.ReturnsDecodedFrames && decoder is not null) throw new ArgumentException("One recognition engine must own a frame");
        this.diagnosticVideoSampling = diagnosticVideoSampling;
        if (diagnosticVideoSampling && Recognizes) throw new ArgumentException("Raw throughput diagnostic must not contain a decoder");
        entityMode = context.Parameters.GetValueOrDefault("entity.mode", !Recognizes ? "observe-only" : "read-result");
        this.acquireLease = acquireLease ?? (actualSerial => new ResourceLease(MachineRuntimePolicy.ResolveLeaseRoot(null), ["device:hikrobot-gige:" + actualSerial]));
        ip = context.Parameters.GetValueOrDefault("device.ip", ""); serial = context.Parameters.GetValueOrDefault("device.serial", "");
        if (!IPAddress.TryParse(ip, out var address) || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork || address.ToString() != ip || ip is "0.0.0.0" or "255.255.255.255")
            throw new InvalidDataException("device.ip: explicit canonical IPv4 required");
        if (!Recognizes && entityMode != "observe-only" || Recognizes && entityMode is not ("read-result" or "observe-only"))
            throw new InvalidDataException("DecoderUnavailable: acquisition cannot create parcel admissions; entity.mode must be observe-only");
        triggerRequirement = context.Parameters.GetValueOrDefault("acquisition.trigger", "existing");
        if (triggerRequirement is not ("existing" or "on" or "off")) throw new InvalidDataException("Unknown trigger requirement");
        nodes = int.Parse(context.Parameters.GetValueOrDefault("image.nodes", "3"), System.Globalization.CultureInfo.InvariantCulture);
        maximumPayload = int.Parse(context.Parameters.GetValueOrDefault("image.maximum-payload-bytes", "8388608"), System.Globalization.CultureInfo.InvariantCulture);
        if (nodes is < 1 or > 4 || maximumPayload is < 1048576 or > 33554432) throw new InvalidDataException("Image nodes must be 1..4 and payload budget 1..32 MiB");
        // Recognise only settled frames (decoder path only); 0 decodes every frame as before.
        stillMs = !Recognizes ? 0 : int.Parse(context.Parameters.GetValueOrDefault("motion.still-ms", "200"), System.Globalization.CultureInfo.InvariantCulture);
        if (stillMs is < 0 or > 2000) throw new InvalidDataException("motion.still-ms must be 0..2000");
    }
    void PhaseTo(string value) { lock (gate) phase = value; phaseChanged?.Invoke(value); }
    readonly object healthPublication = new();
    void Publish(DeviceHealth state)
    {
        lock (healthPublication)
        {
            if (state == DeviceHealth.Ready && Failure.Length != 0) return;
            // No SDK callback exists. All host work runs on lifecycle/acquisition tasks,
            // outside the metadata lock and after returning any acquired image buffer.
            var now = DateTimeOffset.UtcNow;
            health?.Invoke(new(DeviceId, Epoch, state, now, state == DeviceHealth.Ready ? now.AddSeconds(3) : null, ConfirmationSource));
        }
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
            lock (gate) { epoch = registeredEpoch; failure = ""; teardown = ""; frames = bytes = recognized = candidates = sequence = skippedVideoFrames = videoGapEvents = emptyFrames = movingFrames = fallbackDecodes = 0; receiver = receive; priorFrame = null; health = onHealth; stopping = null; ResetDiagnostics(); }
            // A new session starts from a new reference frame; nothing from the old stream is compared.
            stillness = new(stillMs, Math.Max(1500, 3 * stillMs));
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
                    if (Recognizes)
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
                    if (!Recognizes && (triggerRequirement == "on" && trigger != 1 || triggerRequirement == "off" && trigger != 0))
                        throw new InvalidDataException("Configured trigger requirement differs from device; no trigger settings were written");
                    if (api.EnumValue("PixelFormat") != MvsNative.Mono8) throw new InvalidDataException("Current target acquisition requires existing Mono8; pixel format was not changed");
                    var payload = api.PayloadSize();
                    if (payload <= 0 || payload > maximumPayload) throw new InvalidDataException("PayloadSize exceeds configured image byte budget");
                    token.ThrowIfCancellationRequested(); PhaseTo("configure-sdk-buffers"); api.ConfigureBuffers((uint)nodes, !Recognizes ? 0u : 1u);
                    token.ThrowIfCancellationRequested(); PhaseTo("start-grabbing"); grabbing = true; api.Start(); token.ThrowIfCancellationRequested();
                    cancel = new(); PhaseTo(!Recognizes ? "acquiring-decoder-unavailable" : "reading"); Publish(!Recognizes ? DeviceHealth.Unknown : DeviceHealth.Ready);
                    // GetImageBuffer and decoding form a synchronous, lifetime-long
                    // loop. Keep it off the shared pool used by Application/UI
                    // preview reads, continuations and shutdown coordination.
                    var captureToken = cancel.Token;
                    StartDecodePipeline(captureToken);
                    worker = Task.Factory.StartNew(() => Capture(captureToken), CancellationToken.None,
                        TaskCreationOptions.LongRunning, TaskScheduler.Default);
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
        var poll = Stopwatch.StartNew(); long healthFrames = 0, previousDelivery = 0;
        try
        {
            while (!token.IsCancellationRequested)
            {
                ApplyInspectionCacheCutover();
                var inspected = BeginInspectedCycle();
                try
                {
                    PhaseTo("get-image-buffer"); MvsFrame? frame; DateTimeOffset observedAt; long deliveredTicks;
                    var acquireStarted = Stopwatch.GetTimestamp();
                    try
                    {
                        lock (gate) acquireCalls++;
                        frame = api.Acquire(); observedAt = DateTimeOffset.UtcNow; deliveredTicks = Stopwatch.GetTimestamp();
                    }
                    catch (IOException error)
                    {
                        // Any SDK error from GetImageBuffer (GigE packet loss, UDP receive failure,
                        // device loss) ends this stream. Reconnect re-verifies IP and serial.
                        throw new ObservationConnectionException("MVS相机图像流中断。", error);
                    }
                    finally { Timings.Record("sdk.acquire", Stopwatch.GetElapsedTime(acquireStarted).TotalMilliseconds); }
                    Timings.Record(frame is null ? "sdk.acquire.no-data" : "sdk.acquire.frame", Timings.LastMs("sdk.acquire"));
                    if (frame is null) { lock (gate) noData++; }
                    else
                    {
                        byte[]? pixels = null; var length = 0; long skippedBefore = 0; double copyMs = 0, releaseMs; long? scene = null;
                        if (previousDelivery != 0) Timings.Record("host.delivery-interval", Stopwatch.GetElapsedTime(previousDelivery, deliveredTicks).TotalMilliseconds);
                        previousDelivery = deliveredTicks;
                        try
                        {
                            if (Recognizes && frame.LostPackets != 0)
                                throw new MvsFrameRejection("LostPackets", "Incomplete GigE frame discarded; SDK buffer will be returned");
                            ValidateFrame(frame, maximumPayload);
                            if (Recognizes && (ulong)frame.Width * frame.Height != frame.Length)
                                throw new InvalidDataException("MVS software decoding requires a verified tight Mono8 stride");
                            lock (gate)
                            {
                                if (priorFrame is { } prior)
                                {
                                    if (Recognizes && frame.Number <= prior)
                                        throw new ObservationConnectionException("MVS帧号回退；须清理原采集并建立新周期。", new IOException("MVS frame sequence reset"));
                                    if (frame.Number <= prior || !Recognizes && !diagnosticVideoSampling && frame.Number - prior != 1)
                                        throw new IOException("MVS frame sequence gap/reset; inspect original source before restart");
                                    skippedBefore = frame.Number - prior - 1; skippedVideoFrames += skippedBefore;
                                    if (skippedBefore > 0) videoGapEvents++;
                                }
                                priorFrame = frame.Number; frames++; decodeWindow.Frame(); bytes += checked((long)frame.Length);
                                lastFrameAt = observedAt; lastFrameTicks = deliveredTicks;
                            }
                            preview.Offer(frame.Data, checked((int)frame.Width), checked((int)frame.Height), DeviceId, Epoch, frame.Number, observedAt);
                            var allowDecode = inspected.Registration?.DecodeAllowed?.Invoke(DeviceId) != false;
                            lock (gate) decodePaused = !allowDecode;
                            if (Recognizes && allowDecode)
                            {
                                var sampled = Stopwatch.GetTimestamp();
                                var settled = stillness.Observe(frame.Data, checked((int)frame.Width), checked((int)frame.Height), deliveredTicks);
                                Timings.Record("motion.sample", Stopwatch.GetElapsedTime(sampled).TotalMilliseconds);
                                lock (gate) { if (settled == StillnessDecision.Moving) movingFrames++; else if (settled == StillnessDecision.Fallback) fallbackDecodes++; }
                                allowDecode = settled != StillnessDecision.Moving;
                                scene = stillness.Scene;
                            }
                            if (Recognizes && allowDecode)
                            {
                                var copyStarted = Stopwatch.GetTimestamp();
                                length = checked((int)(frame.Width * frame.Height)); pixels = ArrayPool<byte>.Shared.Rent(length);
                                Marshal.Copy(frame.Data, pixels, 0, length);
                                copyMs = Stopwatch.GetElapsedTime(copyStarted).TotalMilliseconds; Timings.Record("image.rent-copy", copyMs);
                            }
                        }
                        catch (MvsFrameRejection error)
                        {
                            if (pixels is not null) { ArrayPool<byte>.Shared.Return(pixels, true); pixels = null; }
                            RejectFrame(frame.Number, error, false);
                        }
                        catch { if (pixels is not null) { ArrayPool<byte>.Shared.Return(pixels, true); pixels = null; } throw; }
                        finally
                        {
                            PhaseTo("free-image-buffer"); var releaseStarted = Stopwatch.GetTimestamp();
                            try { api.Release(); }
                            catch { if (pixels is not null) { ArrayPool<byte>.Shared.Return(pixels, true); pixels = null; } throw; }
                            finally { releaseMs = Stopwatch.GetElapsedTime(releaseStarted).TotalMilliseconds; Timings.Record("sdk.release", releaseMs); }
                        }
                        if (pixels is not null)
                        {
                            var work = new DecodeWork(pixels, length, frame with { Data = 0 }, observedAt, deliveredTicks, copyMs, releaseMs, skippedBefore, inspected.Registration) { Scene = scene };
                            if (api.ReturnsDecodedFrames)
                            {
                                // Native recognition already happened. Do not replace a decoded result with a newer frame.
                                try { ProcessDecode(work, token); }
                                catch (MvsFrameRejection error)
                                {
                                    RejectFrame(frame.Number, error, true, scene);
                                    work.Trace = new(frame.Number, observedAt, frame.DeviceTimestampRaw, frame.HostTimestampRaw,
                                        copyMs, releaseMs, frame.NativeRecognitionMs, 0, 0, 0, 0, false)
                                    { DecodeAttempted = true, RejectedFrame = true };
                                }
                                finally { ReturnWork(work, false); }
                            }
                            else OfferDecode(work);
                        }
                        else Timings.Frame(new(frame.Number, observedAt, frame.DeviceTimestampRaw, frame.HostTimestampRaw, 0, releaseMs, 0, 0, 0, 0, 0, false));
                        Timings.Record("host.capture-work", Stopwatch.GetElapsedTime(deliveredTicks).TotalMilliseconds);
                    }
                    if (token.IsCancellationRequested) break;
                    if (poll.Elapsed >= TimeSpan.FromSeconds(1))
                    {
                        PhaseTo("connection-check"); using var check = Timings.Measure("health.total");
                        // The exclusive SDK handle was bound to the verified physical serial at startup.
                        // Fresh, validated frames on that handle prove liveness; global discovery is for idle/recovery.
                        if (!Recognizes || Frames == healthFrames)
                        {
                            bool connected; using (Timings.Measure("health.connected")) connected = api.Connected();
                            if (!connected) throw new ObservationConnectionException("MVS原相机断开。", new IOException("Original camera unavailable"));
                            bool matches; using (Timings.Measure("health.enumerate")) matches = api.Enumerate().Count(d => d.Ip == ip && d.Serial == physicalSerial) == 1;
                            if (!matches) throw new ObservationConnectionException("MVS原相机身份不匹配。", new IOException("Original camera unavailable"));
                        }
                        using (Timings.Measure("health.trigger-readback"))
                            if (!api.ReturnsDecodedFrames && (triggerRequirement == "on" && api.EnumValue("TriggerMode") != 1 || triggerRequirement == "off" && api.EnumValue("TriggerMode") != 0))
                                throw new IOException("MVS device trigger setting changed");
                        healthFrames = Frames;
                        PhaseTo(!Recognizes ? "acquiring-decoder-unavailable" : "reading"); Publish(!Recognizes ? DeviceHealth.Unknown : DeviceHealth.Ready); poll.Restart();
                    }
                }
                finally { EndInspectedCycle(inspected.Generation); }
            }
        }
        catch (Exception error) { CaptureFailed(error); }
        finally { CompleteDecodePipeline(); }
    }
    void CaptureFailed(Exception error)
    {
        cancel?.Cancel(); preview.Clear();
        lock (gate) { if (failure.Length == 0) failure = DiagnosticText.Protect(error.ToString()); }
        try { Publish(error is ObservationConnectionException ? DeviceHealth.Disconnected : DeviceHealth.Faulted); }
        catch (Exception host) { lock (gate) failure += "; health receiver=" + host.GetType().Name; }
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
            if (disposed) return Task.CompletedTask;
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
        CompleteDecodePipeline();
        List<Exception> terminatedFailures = [];
        foreach (var owned in new[] { worker, decodeWorker }.OfType<Task>())
        {
            try { await owned.ConfigureAwait(false); }
            catch (Exception error) { terminatedFailures.Add(error); }
        }
        preview.Clear();
        var healthReportFailed = false;
        void Report(DeviceHealth state)
        {
            try { Publish(state); }
            catch (Exception ex) { healthReportFailed = true; terminatedFailures.Add(ex); }
        }
        // A failed host notification must not prevent native teardown. Native
        // errors still retain the original owner and stop at the failed step.
        Report(DeviceHealth.Stopping);
        if (api.HasFrame) { PhaseTo("retry-free-original-buffer"); await Task.Run(api.Release).ConfigureAwait(false); }
        // StopGrabbing and CloseDevice are transitions, not releases. After GigE packet loss
        // they can fail (e.g. 0x80000214 UDP receive, 0x80000003 call order) on a stream that
        // is already broken; retrying them only repeats the error. Record the error and go on:
        // ownership is released only when DestroyHandle and Finalize actually succeed below.
        List<string> transitions = [];
        async Task Transition(string stage, Action call)
        {
            try { await Task.Run(call).ConfigureAwait(false); }
            catch (IOException error) { transitions.Add(stage + ": " + error.Message); }
        }
        if (grabbing) { PhaseTo("stop-grabbing"); await Transition("StopGrabbing", api.Stop).ConfigureAwait(false); grabbing = false; }
        if (opened) { PhaseTo("close"); await Transition("CloseDevice", api.Close).ConfigureAwait(false); opened = false; }
        if (transitions.Count != 0) lock (gate) teardown = string.Join("; ", transitions);
        if (created) { PhaseTo("destroy"); await Task.Run(api.Destroy).ConfigureAwait(false); created = false; }
        if (initialized) { PhaseTo("finalize"); await Task.Run(api.Shutdown).ConfigureAwait(false); initialized = false; }
        decoder?.Release();
        lease?.Dispose(); lease = null; worker = null; decodeWorker = null; cancel?.Dispose(); cancel = null;
        PhaseTo("stopped"); Report(DeviceHealth.Stopped); health = null;
        if (terminatedFailures.Count != 0)
            throw new IOException("MVS native teardown completed; " + (healthReportFailed ? "health receiver failed; " : "") +
                "original worker/health errors retained", new AggregateException(terminatedFailures));
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
                    try { await StopCore().ConfigureAwait(false); originals.Dispose(); ReleaseNativeCustody(); decodeWake.Dispose(); decodeIdle.Dispose(); disposed = true; }
                    catch (Exception ex) { lock (gate) failure = ex.GetType().Name + ": " + ex.Message; throw; }
                    finally { lifecycle.Release(); }
                });
            return new(disposal);
        }
    }
}

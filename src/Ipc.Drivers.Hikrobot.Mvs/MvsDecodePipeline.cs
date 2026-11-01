using System.Buffers;
using System.Diagnostics;
using Ipc.Platform;

namespace Ipc.Drivers.Hikrobot.Mvs;

internal sealed partial class MvsAcquisition
{
    sealed record DecodeWork(byte[] Pixels, int Length, MvsFrame Frame, DateTimeOffset ObservedAt, long DeliveredTicks,
        double CopyMs, double ReleaseMs, long SkippedBefore, InspectionRegistration? Registration)
    {
        internal MvsFrameLatency? Trace { get; set; }
    }
    readonly object decodeGate = new();
    readonly AutoResetEvent decodeWake = new(false);
    readonly ManualResetEventSlim decodeIdle = new(true);
    Task? decodeWorker;
    DecodeWork? pendingDecode;
    bool decodeComplete;
    long supersededDecodeFrames;
    void StartDecodePipeline(CancellationToken token)
    {
        lock (decodeGate) { decodeComplete = false; pendingDecode = null; supersededDecodeFrames = 0; decodeIdle.Set(); }
        if (decoder is not null) decodeWorker = Task.Factory.StartNew(() => DecodeLoop(token), CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }
    void ReturnWork(DecodeWork work, bool skipped)
    {
        if (skipped)
        {
            Interlocked.Increment(ref supersededDecodeFrames);
            work.Trace = new(work.Frame.Number, work.ObservedAt, work.Frame.DeviceTimestampRaw, work.Frame.HostTimestampRaw,
                work.CopyMs, work.ReleaseMs, 0, 0, 0, 0, 0, false) { SkippedBeforeDecode = true };
        }
        var started = Stopwatch.GetTimestamp();
        ArrayPool<byte>.Shared.Return(work.Pixels, true);
        var clearMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds; Timings.Record("image.clear-return", clearMs);
        if (work.Trace is { } trace) Timings.Frame(trace with { ClearMs = clearMs });
    }
    void OfferDecode(DecodeWork work)
    {
        DecodeWork? retired;
        lock (decodeGate)
        {
            retired = pendingDecode;
            if (decodeComplete) retired = work;
            else pendingDecode = work;
        }
        if (retired is not null) ReturnWork(retired, true);
        decodeWake.Set();
    }
    void CompleteDecodePipeline() { lock (decodeGate) decodeComplete = true; decodeWake.Set(); }
    void DrainDecodeBoundary()
    {
        DecodeWork? retired;
        lock (decodeGate) { retired = pendingDecode; pendingDecode = null; }
        if (retired is not null) ReturnWork(retired, true);
        // Capture owns this boundary and cannot enqueue a new frame while waiting.
        // The active decoder may already own a result: it must finish, never be abandoned.
        decodeIdle.Wait();
    }
    void DecodeLoop(CancellationToken token)
    {
        try
        {
            while (true)
            {
                DecodeWork? work;
                lock (decodeGate)
                {
                    work = pendingDecode; pendingDecode = null;
                    if (work is null && decodeComplete) return;
                    if (work is not null) decodeIdle.Reset();
                }
                if (work is null) { decodeWake.WaitOne(); continue; }
                try
                {
                    if (token.IsCancellationRequested || work.Registration?.DecodeAllowed?.Invoke(DeviceId) == false)
                    { Interlocked.Increment(ref supersededDecodeFrames); continue; }
                    Timings.Record("host.decode-queue-wait", Stopwatch.GetElapsedTime(work.DeliveredTicks).TotalMilliseconds);
                    ProcessDecode(work, token);
                }
                finally { ReturnWork(work, false); decodeIdle.Set(); }
            }
        }
        catch (Exception error) { CaptureFailed(error); }
        finally
        {
            DecodeWork? retired;
            lock (decodeGate) { decodeComplete = true; retired = pendingDecode; pendingDecode = null; }
            if (retired is not null) ReturnWork(retired, true);
            decodeIdle.Set();
        }
    }
    void ProcessDecode(DecodeWork work, CancellationToken token)
    {
        var pixels = work.Pixels; var length = work.Length; var frame = work.Frame;
        var observedAt = work.ObservedAt; var deliveredTicks = work.DeliveredTicks; var skippedBefore = work.SkippedBefore;
        var queueMs = Stopwatch.GetElapsedTime(deliveredTicks).TotalMilliseconds;
        var inspected = (Registration: work.Registration, Generation: 0L);
        long decodedTicks = 0;
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
            decodedTicks = Stopwatch.GetTimestamp();
            codes = symbols.Select(symbol => symbol.Text).ToArray();
            lock (gate) { decodeCompleted++; decodedCandidates += codes.Length; lastDecodeAt = DateTimeOffset.UtcNow; }
        }
        catch { original?.Dispose(); throw; }
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
            work.Trace = new(frame.Number, observedAt, frame.DeviceTimestampRaw, frame.HostTimestampRaw,
                work.CopyMs, work.ReleaseMs, Timings.LastMs("decode"), 0, 0, 0, 0, false) { OriginalCopyMs = Timings.LastMs("image.original-copy"), DecodeQueueMs = queueMs };
        }

        else
        {
            var source = sourceEvent!;
            var admission = admissionIdentity!;
            var observation = new Observation(observationIdentity!, DeviceId, Epoch, source, ++sequence, admission,
                entityMode == "read-result" ? "Read" : "UncorrelatedRead", codes, null, observedAt)
            {
                DecodedTicks = decodedTicks,
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
                work.Trace = new(frame.Number, observedAt, frame.DeviceTimestampRaw, frame.HostTimestampRaw,
                    work.CopyMs, work.ReleaseMs, Timings.LastMs("decode"), 0,
                    Timings.LastMs("host.observed-to-receiver"), Timings.LastMs("application.durable-handoff"), codes.Length, delivered) { OriginalCopyMs = Timings.LastMs("image.original-copy"), DecodeQueueMs = queueMs };
            }
            Timings.Record("host.observed-to-handoff-complete", Stopwatch.GetElapsedTime(deliveredTicks).TotalMilliseconds);
            Undelivered = null;
            lock (gate) { recognized++; candidates += codes.Length; }
        }
    }
}

using System.Collections.Concurrent;
using Ipc.Drivers.Hikrobot.Mvs;
using Ipc.Platform;

internal static partial class MvsVerification
{
    static async Task SamplingTimings()
    {
        using (var originals = new MvsOriginalFrames())
        {
            originals.Enable(); var rawPixels = new byte[] { 30, 70, 140, 210 };
            var identity = new AcquisitionIdentity("synthetic", "epoch-1", "frame-1", "admission-1", DateTimeOffset.UnixEpoch);
            originals.Publish(identity, originals.Copy(rawPixels, 4, 2, 2));
            using (var held = originals.Retain(identity))
            {
                Array.Clear(rawPixels); originals.Release(identity); originals.Release(identity);
                Check(held.Pixels.Span.SequenceEqual(new byte[] { 30, 70, 140, 210 }) && originals.Count == 1,
                    "independent raw-frame lease survives working-pixel mutation and repeated ingress release");
                try { originals.Dispose(); throw new Exception("Writer pixels freed early"); }
                catch (InvalidOperationException) { Check(held.Pixels.Length == 4, "source disposal retains an actual outstanding raw-frame writer"); }
            }
            Check(originals.Count == 0, "last raw-frame writer releases the pooled original exactly once");
            for (var i = 0; i < 32; i++) originals.Publish(identity with { SourceEventId = "quota-" + i }, originals.Copy(rawPixels, 4, 2, 2));
            try { _ = originals.Copy(rawPixels, 4, 2, 2); throw new Exception("Unbounded original frames"); }
            catch (IOException) { Check(originals.Count == 32, "32-frame / pooled-byte budget rejects overload before recognition without discarding retained originals"); }
        }
        var timings = new MvsTimings();
        for (var i = 0; i < 1025; i++) timings.Record("synthetic", i);
        var stats = timings.Snapshot().Stages["synthetic"];
        Check(stats.Count == 1025 && stats.MinimumMs == 0 && stats.MaximumMs == 1024 && stats.WindowCount == 512 && stats.P50Ms == 768,
            "timing retention is bounded; session totals/extrema and recent-window percentiles have explicit different scopes");
        for (uint i = 0; i < 1025; i++) timings.Frame(new(i, DateTimeOffset.UnixEpoch, 0, 0, 0, 0, 0, 0, 0, 0, 0, false));
        var frames = timings.Snapshot().RecentFrames;
        Check(frames.Length == 512 && frames[0].Number == 513 && frames[^1].Number == 1024, "numeric frame traces retain only the last 512 frames");
        timings.Reset(); Check(timings.Snapshot().Stages.Count == 0, "new timing session clears old samples");
        var api = new Fake(); var health = new ConcurrentQueue<DeviceHealthConfirmation>();
        await using (var source = new MvsAcquisition(new("diagnostic.mvs", "synthetic.mvs", new Dictionary<string, string> { ["device.ip"] = api.Ip, ["device.serial"] = api.Serial }),
            api, _ => new Lease(), diagnosticVideoSampling: true))
        {
            await source.StartControlledAsync("sampled", _ => throw new Exception("Raw probe invented input"), health.Enqueue, default);
            api.Frames.Enqueue(Frame(1)); api.Frames.Enqueue(Frame(8)); await Until(() => api.Freed == 2);
            Check(source.Failure.Length == 0 && source.Frames == 2 && source.ConfirmationSource.Contains("skippedVideoFrames=6;") &&
                source.ConfirmationSource.Contains("videoGapEvents=1;") && health.All(h => h.Health != DeviceHealth.Ready),
                "raw throughput diagnostic counts skipped video frames and gap events separately without barcode readiness or business input");
            api.Frames.Enqueue(Frame(8)); await Until(() => source.Failure.Length > 0);
            await source.StopAsync(default);
            Check(source.Frames == 2 && api.Freed == 3 && source.Failure.Contains("gap/reset"), "sampled diagnostic still faults duplicate/reset and returns that loan");
            await source.StartControlledAsync("sampled-restart", _ => true, _ => { }, default);
            api.Frames.Enqueue(Frame(1) with { LostPackets = 1 }); await Until(() => source.Failure.Length > 0);
            await source.StopAsync(default);
            Check(source.Frames == 0 && api.Freed == 4 && source.Timings.Snapshot().Stages["sdk.release"].Count == 1,
                "sampled diagnostic refuses real incomplete frames; restart timing/counters exclude the old epoch");
        }
        using var pixels = new PixelLoan(4);
        var software = new Fake { Acquiring = () => Thread.Sleep(25) };
        var received = new ConcurrentQueue<Observation>();
        await using (var source = new MvsAcquisition(new("synthetic.mvs", software.Ip, new Dictionary<string, string> { ["device.ip"] = software.Ip }),
            software, _ => new Lease(), decoder: new ControlledDecoder { DuringDecode = () => Thread.Sleep(60) }))
        {
            await source.StartControlledAsync("timed", o => { Check(!software.HasFrame, "timed decoder/handoff run after actual SDK loan return"); Thread.Sleep(40); received.Enqueue(o); return true; }, _ => { }, default);
            software.Frames.Enqueue(pixels.Frame);
            await Until(() => source.ConfirmationSource.Contains("readResults=1;"));
            await source.StopAsync(default);
            var stages = source.Timings.Snapshot().Stages;
            Check(stages["sdk.acquire"].MinimumMs >= 20 && stages["decode"].MinimumMs >= 55 &&
                stages["application.durable-handoff"].MinimumMs >= 35 && stages["host.observed-to-receiver"].MinimumMs >= 55 &&
                stages["host.observed-to-handoff-complete"].MinimumMs >= 90 && received.Count == 1,
                "injected acquisition/decode/durable-handoff delays are attributed separately with original observation time and one delivery");
            var frame = source.Timings.Snapshot().RecentFrames.Single();
            Check(frame.Number == 1 && frame.DeliveredAt == received.Single().ObservedAt && frame.Candidates == 1 && frame.Delivered &&
                frame.DecodeMs >= 55 && frame.HandoffMs >= 35 && frame.ObservedToReceiverMs >= frame.DecodeMs,
                "per-frame numeric trace correlates timing with the unchanged original observation and complete delivery");
        }
        var slowDiscovery = new Fake();
        await using (var source = Source(slowDiscovery, new Lease()))
        {
            await source.StartControlledAsync("slow-discovery", _ => true, _ => { }, default);
            slowDiscovery.Enumerating = () => Thread.Sleep(70);
            await Until(() => source.Timings.Snapshot().Stages.ContainsKey("health.total"));
            await source.StopAsync(default);
            var stages = source.Timings.Snapshot().Stages;
            Check(stages["health.enumerate"].MinimumMs >= 65 && stages["health.total"].MinimumMs >= 65 &&
                !stages.ContainsKey("host.observed-to-receiver"),
                "blocking identity enumeration is visible as health time outside the observed-to-queue clock; identity checks remain active");
        }
    }
}

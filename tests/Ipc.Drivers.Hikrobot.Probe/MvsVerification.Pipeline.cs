using System.Collections.Concurrent;
using Ipc.Drivers.Hikrobot.Mvs;
using Ipc.Platform;

internal static partial class MvsVerification
{
    internal static async Task VerifyPipeline()
    {
        using var pixels = new PixelLoan(4); using var release = new ManualResetEventSlim(); using var entered = new ManualResetEventSlim();
        var api = new Fake(); var lease = new Lease(); var reads = new ConcurrentQueue<Observation>();
        var decoder = new PipelinePixelGuard(entered, release);
        await using (var source = new MvsAcquisition(new("pipeline", "pipeline", new Dictionary<string, string> { ["motion.still-ms"] = "0", ["device.ip"] = api.Ip }), api, _ => lease, decoder: decoder))
        {
            try
            {
                await source.StartControlledAsync("pipeline-epoch", o => { reads.Enqueue(o); return true; }, _ => { }, default);
                source.ReadPreview(); api.Frames.Enqueue(pixels.Frame);
                await Until(() => entered.IsSet);
                // Reuse the returned SDK storage for different pixels. The blocked
                // decoder must continue to own its first frame's managed copy.
                System.Runtime.InteropServices.Marshal.Copy(new byte[] { 17, 37, 57, 77 }, 0, pixels.Frame.Data, 4);
                for (uint n = 2; n < 20; n++) api.Frames.Enqueue(pixels.Frame with { Number = n });
                await Until(() => source.Frames == 19 && api.Freed == 19);
                await Task.Delay(220); source.ReadPreview(); api.Frames.Enqueue(pixels.Frame with { Number = 20 });
                await Until(() => source.ReadPreview()?.FrameNumber == 20 && api.Freed == 20 && source.ReadDiagnostics().SupersededDecodeFrames == 18);
                Check(source.ReadDiagnostics() is { Frames: 20, DecodeStarted: 1, DecodeCompleted: 0, SupersededDecodeFrames: 18 } && api.Freed == 20,
                    "blocked decoder never blocks capture/preview; only the newest undecoded frame is retained and all 18 replacements are counted");
                release.Set(); await Until(() => reads.Count == 2);
                Check(reads.Select(o => o.SourceEventId).SequenceEqual(["mvs.frame.1", "mvs.frame.20"]) && reads.Select(o => o.Id).Distinct().Count() == 2,
                    "same-code decoded frames retain two independent original observations; intermediate undecoded video creates no fake inputs");
                Check(source.Timings.Snapshot().RecentFrames.Count(f => f.SkippedBeforeDecode) == 18 && source.Timings.Snapshot().Stages.ContainsKey("host.decode-queue-wait"),
                    "bounded numeric traces separate host decode queue age from SDK-return time and record skipped video without calling it network loss");
                Check(api.LastStrategy == 1, "software reader requests reviewed LatestImagesOnly strategy, not OneByOne backlog");
                await source.StopAsync(default);
                Check(lease.Releases == 1 && api.Freed == 20 && !api.HasFrame, "capture and decode both drain before buffers, SDK and physical lease are released");
            }
            finally { release.Set(); }
        }
        var healthApi = new Fake(); var enumerations = 0;
        healthApi.Enumerating = () => { Interlocked.Increment(ref enumerations); Thread.Sleep(80); };
        await using (var source = new MvsAcquisition(new("health", "health", new Dictionary<string, string> { ["motion.still-ms"] = "0", ["device.ip"] = healthApi.Ip }), healthApi, _ => new Lease(), decoder: new ControlledDecoder { Codes = [] }))
        {
            await source.StartControlledAsync("health-epoch", _ => true, _ => { }, default);
            uint number = 0; var deadline = DateTimeOffset.UtcNow.AddSeconds(2.3);
            while (DateTimeOffset.UtcNow < deadline) { healthApi.Frames.Enqueue(pixels.Frame with { Number = ++number }); await Task.Delay(25); }
            Check(enumerations == 1 && source.ReadDiagnostics().Frames > 30,
                "continuous verified frames avoid repeated global enumeration while exclusive startup identity verification remains required");
            await Until(() => Volatile.Read(ref enumerations) >= 2);
            await Until(() => source.Timings.Snapshot().Stages.ContainsKey("health.enumerate"));
            Check(source.Timings.Snapshot().Stages["health.enumerate"].MaximumMs >= 70,
                "idle stream falls back to measured full identity check without parallel SDK calls or relaxing bad-frame validation");
            healthApi.Online = false; await Until(() => source.Failure.Length != 0);
            Check(source.Failure.Contains("ObservationConnectionException"), "idle transport loss still faults explicitly for controlled automatic recovery");
        }
    }
    sealed class PipelinePixelGuard(ManualResetEventSlim entered, ManualResetEventSlim release) : IMonoBarcodeDecoder
    {
        public void Prepare() { }
        public string[] Decode(byte[] pixels, int length, int width, int height)
        {
            var owned = pixels.AsSpan(0, length).ToArray(); entered.Set(); release.Wait();
            if (!pixels.AsSpan(0, length).SequenceEqual(owned)) throw new InvalidDataException("Active decode pixels were cleared or reused by acquisition");
            return ["SYNTH-CODE"];
        }
        public void Release() { }
    }
}

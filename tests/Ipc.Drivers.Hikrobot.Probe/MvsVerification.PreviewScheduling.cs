using Ipc.Drivers.Hikrobot.Mvs;
using Ipc.Platform;

internal static partial class MvsVerification
{
    // Run in its own probe process: deliberately constrained worker capacity must
    // not let permanently blocking camera loops starve Application preview reads.
    internal static void VerifyPreviewScheduling()
    {
        ThreadPool.GetMinThreads(out var minimum, out var minimumIo);
        ThreadPool.GetMaxThreads(out var maximum, out var maximumIo);
        using var release = new ManualResetEventSlim();
        using var enteredLeft = new ManualResetEventSlim(); using var enteredRight = new ManualResetEventSlim();
        using var leftPixels = new PixelLoan(4); using var rightPixels = new PixelLoan(4);
        Fake Api(ManualResetEventSlim entered, PixelLoan pixels)
        {
            var api = new Fake(); var calls = 0;
            api.Frames.Enqueue(pixels.Frame);
            api.Acquiring = () => { if (Interlocked.Increment(ref calls) > 1) { entered.Set(); release.Wait(); } };
            return api;
        }
        var leftApi = Api(enteredLeft, leftPixels); var rightApi = Api(enteredRight, rightPixels);
        var leftLease = new Lease(); var rightLease = new Lease();
        MvsAcquisition Create(Fake api, Lease lease, string id) => new(new(id, id, new Dictionary<string, string> { ["motion.still-ms"] = "0", ["device.ip"] = api.Ip }),
            api, _ => lease, decoder: new ControlledDecoder { Codes = [] });
        var left = Create(leftApi, leftLease, "preview-left"); var right = Create(rightApi, rightLease, "preview-right");
        var ready = new System.Collections.Concurrent.ConcurrentDictionary<string, bool>();
        try
        {
            if (!ThreadPool.SetMinThreads(1, minimumIo) || !ThreadPool.SetMaxThreads(2, maximumIo)) throw new InvalidOperationException("Cannot constrain this isolated probe process");
            left.StartControlledAsync("left-epoch", _ => true, h => ready[h.DeviceId] = h.Health == DeviceHealth.Ready, default).GetAwaiter().GetResult();
            if (!enteredLeft.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Left acquisition never entered SDK wait");
            right.StartControlledAsync("right-epoch", _ => true, h => ready[h.DeviceId] = h.Health == DeviceHealth.Ready, default).GetAwaiter().GetResult();
            if (!enteredRight.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Right acquisition never entered SDK wait");
            Check(ready.Count == 2 && ready.Values.All(r => r) && left.ReadDiagnostics().Frames == 1 && right.ReadDiagnostics().Frames == 1,
                "both cameras are online with valid frames while their next SDK acquisitions are in flight");
            var read = Task.Run(() => { left.ReadPreview(); right.ReadPreview(); return true; });
            Check(read.Wait(TimeSpan.FromSeconds(1)), "two persistent SDK loops leave shared worker capacity available for Application preview reads");
            var stop = left.StopAsync(default);
            Check(!stop.Wait(TimeSpan.FromMilliseconds(100)) && leftLease.Releases == 0,
                "stop still waits for the real SDK acquisition and retains its physical lease");
            release.Set();
            stop.GetAwaiter().GetResult(); right.StopAsync(default).GetAwaiter().GetResult();
            Check(leftLease.Releases == 1 && rightLease.Releases == 1 && !leftApi.HasFrame && !rightApi.HasFrame,
                "native loans and both camera owners drain before their leases are released");
        }
        finally
        {
            // Restore scheduling before cleanup even on the expected negative run.
            ThreadPool.SetMaxThreads(maximum, maximumIo); ThreadPool.SetMinThreads(minimum, minimumIo); release.Set();
            left.DisposeAsync().AsTask().GetAwaiter().GetResult(); right.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
}

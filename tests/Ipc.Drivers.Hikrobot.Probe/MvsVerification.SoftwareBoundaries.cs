using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Ipc.Drivers.Hikrobot.Mvs;
using Ipc.Platform;

internal static partial class MvsVerification
{
    static async Task SoftwareBoundaries()
    {
        foreach (var mode in new string?[] { null, "observe-only" })
        {
            using var pixels = new PixelLoan(4);
            var api = new Fake(); var lease = new Lease(); var decoder = new ControlledDecoder();
            var received = new ConcurrentQueue<Observation>();
            var parameters = new Dictionary<string, string> { ["device.ip"] = api.Ip };
            if (mode is not null) parameters["entity.mode"] = mode;
            await using var source = new MvsAcquisition(new("synthetic.mvs", api.Ip, parameters), api, _ => lease, decoder: decoder);
            await source.StartControlledAsync("software-default", o => { received.Enqueue(o); return true; }, _ => { }, default);
            Check(source.ReadPreview() is null, "preview begins empty on the existing acquisition owner");
            api.Frames.Enqueue(pixels.Frame); await Until(() => received.Count == 1);
            var preview = source.ReadPreview(); var calls = api.Calls.Count;
            Check(preview is { Epoch: "software-default", Width: 2, Height: 2 } && source.ReadPreview() is not null && api.Calls.Count == calls,
                "real acquisition path produces a copied preview without opening or polling another SDK session");
            await source.StopAsync(default);
            Check(source.ReadPreview() is null, "stopped source cannot expose its old frame");
            Check(received.Single().Kind == (mode is null ? "Read" : "UncorrelatedRead"),
                mode is null ? "software reader with omitted entity.mode defaults to an admitted Read candidate" : "explicit observe-only remains an uncorrelated observation with a decoder");
            Check(decoder.Calls == 1 && api.Freed == 1 && lease.Releases == 1 && source.Undelivered is null,
                "tight Mono8 frame is handed off once after returning the native loan and releases its owner");
        }
        using (var pixels = new PixelLoan(4))
        using (var entered = new ManualResetEventSlim())
        using (var release = new ManualResetEventSlim())
        {
            var api = new Fake(); var lease = new Lease(); var received = new ConcurrentQueue<Observation>();
            var health = new ConcurrentQueue<DeviceHealthConfirmation>();
            var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var decoder = new ControlledDecoder { DuringDecode = () => { entered.Set(); release.Wait(); } };
            await using var source = new MvsAcquisition(new("synthetic.mvs", api.Ip, new Dictionary<string, string> { ["device.ip"] = api.Ip }), api,
                _ => lease, phase => { if (phase == "stop-requested") stopped.TrySetResult(); }, decoder);
            await source.StartControlledAsync("stop-while-decoding", o => { received.Enqueue(o); return true; }, health.Enqueue, default);
            api.Frames.Enqueue(pixels.Frame);
            try
            {
                Check(entered.Wait(5000), "software decoder entered with a copied frame after native loan return");
                var stopping = source.StopAsync(default);
                await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Check(!stopping.IsCompleted && lease.Releases == 0 && api.Freed == 1,
                    "stop requested during decode still waits for the actual decoder while retaining the source owner");
                release.Set(); await stopping.WaitAsync(TimeSpan.FromSeconds(5));
                var original = source.Undelivered;
                Check(received.IsEmpty && original is { DeviceId: "192.0.2.1", Epoch: "stop-while-decoding", SourceEventId: "mvs.frame.1", Sequence: 1 }
                    && original.Codes.SequenceEqual(["SYNTH-CODE"]) && original.AdmissionId.Length > 0
                    && source.Failure.Length > 0 && health.Any(h => h.Health == DeviceHealth.Faulted),
                    "decoded result arriving after stop is retained in full and faulted, never silently lost or handed off after cancellation");
                await Reject(() => source.StartControlledAsync("unsafe-restart", _ => true, _ => { }, default));
                Check(ReferenceEquals(original, source.Undelivered) && decoder.Calls == 1 && api.Freed == 1 && lease.Releases == 1,
                    "unresolved decoded observation fences restart without replacement, resend or duplicate native release");
            }
            finally { release.Set(); }
        }
        using (var pixels = new PixelLoan(5))
        {
            var api = new Fake(); var lease = new Lease(); var decoder = new ControlledDecoder();
            var received = new ConcurrentQueue<Observation>();
            await using var source = new MvsAcquisition(new("synthetic.mvs", api.Ip, new Dictionary<string, string> { ["device.ip"] = api.Ip }), api, _ => lease, decoder: decoder);
            await source.StartControlledAsync("padded-frame", o => { received.Enqueue(o); return true; }, _ => { }, default);
            api.Frames.Enqueue(pixels.Frame); await Until(() => decoder.Calls > 0 || source.Failure.Length > 0);
            await source.StopAsync(default);
            Check(decoder.Calls == 0 && received.IsEmpty && source.Failure.Contains("stride", StringComparison.OrdinalIgnoreCase)
                && source.Frames == 0 && api.Freed == 1 && lease.Releases == 1,
                "unverified padded Mono8 layout is refused before decoding/counting and the native frame is returned once");
        }
        using (var pixels = new PixelLoan(5))
        {
            var api = new Fake(); var lease = new Lease(); var health = new ConcurrentQueue<DeviceHealthConfirmation>(); var observations = 0;
            await using var source = Source(api, lease);
            await source.StartControlledAsync("acquisition-only", _ => { observations++; return true; }, health.Enqueue, default);
            api.Frames.Enqueue(pixels.Frame); await Until(() => source.Frames == 1);
            await source.StopAsync(default);
            Check(observations == 0 && health.All(h => h.Health != DeviceHealth.Ready) && source.Failure.Length == 0 && api.Freed == 1 && lease.Releases == 1,
                "acquisition-only omitted mode keeps its existing payload contract without manufacturing barcode readiness");
        }
    }
    sealed class PixelLoan : IDisposable
    {
        readonly byte[] bytes;
        GCHandle pinned;
        internal PixelLoan(int size) { bytes = Enumerable.Range(0, size).Select(i => (byte)(i * 20)).ToArray(); pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned); }
        internal MvsFrame Frame => new(pinned.AddrOfPinnedObject(), 2, 2, MvsNative.Mono8, (ulong)bytes.Length, 1, 0, 0);
        public void Dispose() { if (pinned.IsAllocated) pinned.Free(); }
    }
    sealed class ControlledDecoder : IMonoBarcodeDecoder
    {
        bool prepared;
        int calls;
        internal Action? DuringDecode;
        internal string[] Codes = ["SYNTH-CODE"];
        internal int Calls => Volatile.Read(ref calls);
        public void Prepare() => prepared = true;
        public string[] Decode(byte[] pixels, int length, int width, int height)
        {
            if (!prepared || length != width * height || length > pixels.Length) throw new InvalidDataException("Invalid synthetic decoder input");
            Interlocked.Increment(ref calls); DuringDecode?.Invoke(); return Codes;
        }
        public void Release() => prepared = false;
    }
}

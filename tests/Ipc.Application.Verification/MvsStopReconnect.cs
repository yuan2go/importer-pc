using System.Collections.Concurrent;
using Ipc.Drivers.Hikrobot.Mvs;
using Ipc.Platform;

// Field report: GigE packet loss, then StopGrabbing returns 0x80000214 (UDP receive) on the broken
// stream, and automatic reconnect stayed blocked although the camera was rediscovered. The actual
// MvsAcquisition runs here behind a synthetic SDK, driven through ObservationSession.ReconnectAsync
// (the call the reconnect loop makes). No camera, no RCS.
internal static class MvsStopReconnect
{
    static void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); Console.WriteLine("PASS MVSSTOP " + text); }

    sealed class Lease : IDisposable { public int Releases; public void Dispose() => Interlocked.Increment(ref Releases); }
    sealed class Api : IMvsApi
    {
        public const string Ip = "169.254.203.11", Serial = "SYNTH-DA8423729";
        public volatile bool Online = true;
        public int StopResult, CloseResult;
        public volatile bool FailDestroy;
        public readonly ConcurrentQueue<string> Calls = new();
        public volatile bool FailAcquire;
        public MvsDevice[]? Discovered;
        public bool FailEnumeration, FailShutdown;
        public bool HasFrame => false;
        public void Initialize() => Calls.Enqueue("initialize");
        public MvsDevice[] Enumerate() { Calls.Enqueue("enumerate"); if (FailEnumeration) throw new IOException("Synthetic enumeration failure"); return Discovered ?? (Online ? [new(Ip, Serial, "SYNTHETIC", "SYNTHETIC", [])] : []); }
        public void Create(MvsDevice device) => Calls.Enqueue("create");
        public void Open() { Calls.Enqueue("open"); if (!Online) throw new IOException("MVS OpenDevice exclusive failed: 0x80000206"); }
        public void SetEnumValue(string key, uint value) { }
        public uint EnumValue(string key) => key == "PixelFormat" ? MvsNative.Mono8 : 0;
        public long PayloadSize() => 1048576;
        public void ConfigureBuffers(uint count, uint strategy = 0) { }
        public void Start() => Calls.Enqueue("start");
        public MvsFrame? Acquire()
        {
            if (FailAcquire) throw new IOException("MVS GetImageBuffer failed: 0x80000214");
            Thread.Sleep(5); return null;
        }
        public void Release() { }
        public void ClearBuffers() { }
        public bool Connected() => Online;
        public void Stop() { Calls.Enqueue("stop"); MvsNative.Check(StopResult, "StopGrabbing"); }
        public void Close() { Calls.Enqueue("close"); MvsNative.Check(CloseResult, "CloseDevice"); }
        public void Destroy() { Calls.Enqueue("destroy"); if (FailDestroy) throw new IOException("MVS DestroyHandle failed: synthetic"); }
        public void Shutdown() { Calls.Enqueue("finalize"); if (FailShutdown) throw new IOException("Synthetic SDK release failure"); }
    }

    static async Task Until(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition()) { if (DateTime.UtcNow > deadline) throw new TimeoutException(what); await Task.Delay(20); }
    }

    internal static async Task Run()
    {
        VerifyIdentityLookup();
        var api = new Api(); var lease = new Lease();
        var parameters = new Dictionary<string, string> { ["device.ip"] = Api.Ip, ["device.serial"] = Api.Serial };
        await using var source = new MvsAcquisition(new("left-camera", "left-camera", parameters), api, _ => lease);
        var session = new ObservationSession(source);
        var states = new ConcurrentQueue<DeviceHealth>();
        await session.StartAsync(_ => true, h => states.Enqueue(h.Health), default);
        Check(api.Calls.Count(c => c == "open") == 1, "camera opens once with the configured IP and serial");

        // 1. Stream error while the SDK still says connected: lost stream, not a terminal fault.
        api.FailAcquire = true;
        await Until(() => session.Health?.Health == DeviceHealth.Disconnected, "disconnected after GetImageBuffer error");
        Check(session.Health?.Health == DeviceHealth.Disconnected && !states.Contains(DeviceHealth.Faulted),
            "a GetImageBuffer SDK error marks the camera disconnected (eligible for automatic reconnect), not faulted");
        api.FailAcquire = false;

        // 2. StopGrabbing fails with the field code; reconnect must still complete.
        api.StopResult = unchecked((int)0x80000214);
        await session.ReconnectAsync(_ => true, default);
        Check(lease.Releases == 1 && api.Calls.Count(c => c == "destroy") == 1 && api.Calls.Count(c => c == "finalize") == 1,
            "StopGrabbing 0x80000214 is recorded and the handle is still really destroyed and finalized before the device is released");
        Check(api.Calls.Count(c => c == "open") == 2 && session.Health?.Health != DeviceHealth.Disconnected, "automatic reconnect reopens the rediscovered camera");
        api.StopResult = 0;

        // 3. CloseDevice failing as well is a transition too.
        api.StopResult = unchecked((int)0x80000214); api.CloseResult = unchecked((int)0x80000206);
        await session.ReconnectAsync(_ => true, default);
        Check(lease.Releases == 2 && api.Calls.Count(c => c == "open") == 3, "StopGrabbing and CloseDevice both failing still reaches a real release and a new open");
        api.StopResult = api.CloseResult = 0;

        // 4. Camera still unreachable after release: a retryable connection failure, owner fully released.
        api.Online = false;
        try { await session.ReconnectAsync(_ => true, default); throw new Exception("offline camera reported as reconnected"); }
        catch (ObservationConnectionException) { }
        Check(lease.Releases == 3 && session.Health?.Health == DeviceHealth.Disconnected, "an unreachable camera stays disconnected and retryable, with nothing retained");
        api.Online = true;
        await session.ReconnectAsync(_ => true, default);
        Check(api.Calls.Count(c => c == "open") == 4, "once the camera answers again the next attempt reconnects");

        // 5. A real release failure still keeps the owner and is not reported as released.
        api.FailDestroy = true;
        try { await session.ReconnectAsync(_ => true, default); throw new Exception("failed DestroyHandle reported as released"); }
        catch (Exception error) when (error is not ObservationConnectionException) { }
        Check(lease.Releases == 3 && api.Calls.Count(c => c == "open") == 4,
            "a failed DestroyHandle keeps the device lease and does not reopen; this is the only case that blocks automatic reconnect");
        api.FailDestroy = false;
        await source.StopAsync(default);
        Check(lease.Releases == 4 && api.Calls.Last() == "finalize", "retrying the stop completes the original release exactly once");
    }
    static void VerifyIdentityLookup()
    {
        var api = new Api();
        Check(BundledMvs.ReadCameraSerial(Api.Ip, api) == Api.Serial && api.Calls.SequenceEqual(new[] { "initialize", "enumerate", "finalize" }),
            "IP lookup returns the actual serial without creating, opening or acquiring a camera handle");
        foreach (var devices in new MvsDevice[][] { [], [new(Api.Ip, "ONE", "", "", []), new(Api.Ip, "TWO", "", "", [])], [new(Api.Ip, "", "", "", [])] })
        {
            var rejected = new Api { Discovered = devices }; var failed = false;
            try { BundledMvs.ReadCameraSerial(Api.Ip, rejected); } catch (InvalidDataException) { failed = true; }
            Check(failed && rejected.Calls.Last() == "finalize", "missing, ambiguous or invalid camera identity is rejected and SDK ownership is released");
        }
        foreach (var enumerationFailure in new[] { true, false })
        {
            var failedApi = new Api { FailEnumeration = enumerationFailure, FailShutdown = !enumerationFailure }; var failed = false;
            try { BundledMvs.ReadCameraSerial(Api.Ip, failedApi); } catch (IOException) { failed = true; }
            Check(failed && failedApi.Calls.Last() == "finalize", "enumeration and release failures remain real failures; enumerationFailure=" + enumerationFailure);
        }
    }
}

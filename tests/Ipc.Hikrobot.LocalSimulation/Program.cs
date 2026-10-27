using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Ipc.Drivers.Hikrobot;
using Ipc.Drivers.Hikrobot.Mvs;
using Ipc.Platform;

// Local Windows simulation against the unchanged reviewed driver assemblies.
// Fake SDK metadata and barcode results only: NO real pixels, decoder, hardware,
// RCS endpoint or production configuration. Existing independent failures remain.
int passed = 0, failed = 0;
void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
async Task Until(Func<bool> condition)
{
    using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    while (!condition()) await Task.Delay(5, budget.Token);
}
async Task Check(string name, Func<Task> test)
{
    try { await test(); passed++; Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex); }
}
MvsAcquisition Acquisition(MvsPort port, LeaseCounter lease, string id = "left") => new(
    new("local.mvs." + id, id, new Dictionary<string, string> { ["device.ip"] = port.Address, ["device.serial"] = port.Serial }), port, _ => lease);
HikrobotReader Reader(ReaderPort port, LeaseCounter lease, string id = "left", int capacity = 128) => new(
    new("local.reader." + id, id, new Dictionary<string, string> { ["device.ip"] = port.Address, ["device.serial"] = port.Serial,
        ["entity.mode"] = "trigger", ["queue.capacity"] = capacity.ToString() }), port, _ => lease);
object Resources(string phase)
{
    using var p = Process.GetCurrentProcess(); p.Refresh();
    return new { Phase = phase, p.PrivateMemorySize64, p.WorkingSet64, Handles = p.HandleCount, Threads = p.Threads.Count,
        ManagedBytes = GC.GetTotalMemory(false), Note = "observed snapshot; no GC forced; not a soak or native-SDK leak test" };
}

await Check("MVS.100SequentialGenerationsReleaseEveryOwner", async () =>
{
    var port = new MvsPort(); var lease = new LeaseCounter(); await using var source = Acquisition(port, lease);
    for (var n = 1; n <= 100; n++)
    {
        port.Enqueue(1);
        await source.StartControlledAsync("cycle-" + n, _ => throw new Exception("MVS fabricated barcode"), _ => { }, default);
        await Until(() => source.Frames == 1);
        await source.StopAsync(default);
        Require(port.Freed == n && !port.HasFrame && port.Stops == n && port.Destroys == n && port.Shutdowns == n && lease.Releases == n,
            "owner or borrowed frame not reclaimed at generation " + n);
    }
    Console.WriteLine($"METRIC MVS cycles=100 starts={port.Starts} stops={port.Stops} destroys={port.Destroys} freed={port.Freed} leases={lease.Releases}");
});

await Check("Reader.100RestartsFenceOldCallbacks", async () =>
{
    var port = new ReaderPort(); var lease = new LeaseCounter(); await using var source = Reader(port, lease);
    int received = 0; Action<HikFrame>? prior = null;
    for (var n = 1; n <= 100; n++)
    {
        var generation = "reader-cycle-" + n;
        await source.StartControlledAsync(generation, o => { Require(o.Epoch == generation && o.Sequence == 1, "old epoch/sequence accepted"); Interlocked.Increment(ref received); return true; }, _ => { }, default);
        prior?.Invoke(ReaderPort.Frame(1, 1));
        port.Emit(1, 1);
        await Until(() => source.FramesDelivered == 1);
        prior = port.Callback;
        await source.StopAsync(default);
        Require(received == n && source.FramesReceived == 1 && source.FramesInFlight == 0 && lease.Releases == n && port.Destroys == n,
            "old callback counted or owner retained");
    }
    Console.WriteLine($"METRIC Reader cycles=100 observations={received} rejectedOldCallbacks=99 stops={port.Stops} destroys={port.Destroys} leases={lease.Releases}");
});

foreach (var fault in new[] { "disconnect", "address-changed", "acquire-throws" })
await Check("MVS.FaultAndExplicitRecovery." + fault, async () =>
{
    var port = new MvsPort(); var lease = new LeaseCounter(); var states = new ConcurrentQueue<DeviceHealth>();
    await using var source = Acquisition(port, lease);
    await source.StartControlledAsync("fault-one", _ => throw new Exception("fabricated observation"), h => states.Enqueue(h.Health), default);
    var timer = Stopwatch.StartNew();
    if (fault == "disconnect") port.Online = false;
    if (fault == "address-changed") port.Address = "192.0.2.99";
    if (fault == "acquire-throws") port.ThrowAcquire = true;
    await Until(() => states.Contains(DeviceHealth.Faulted));
    var detectionMs = timer.ElapsedMilliseconds;
    await Task.Delay(1100);
    Require(port.Starts == 1 && states.Last() == DeviceHealth.Faulted && !states.Contains(DeviceHealth.Ready), "auto-reconnect or false Ready after fault");
    await source.StopAsync(default);
    port.Online = true; port.Address = "192.0.2.1"; port.ThrowAcquire = false; port.Enqueue(1);
    await source.StartControlledAsync("fault-two", _ => throw new Exception("fabricated observation"), h => states.Enqueue(h.Health), default);
    await Until(() => source.Frames == 1);
    await source.StopAsync(default);
    Require(port.Starts == 2 && port.Destroys == 2 && lease.Releases == 2 && source.Failure.Length == 0, "explicit recovery failed to finish old owner");
    Console.WriteLine($"METRIC fault={fault} simulatedDetectionMs={detectionMs} starts=2 manualRecoveries=1");
});

await Check("Reader.BlockedConsumerHasExplicitOverflowAndConservedCounts", async () =>
{
    var port = new ReaderPort(); var lease = new LeaseCounter(); var states = new ConcurrentQueue<DeviceHealth>();
    using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
    await using var source = Reader(port, lease, capacity: 2);
    try
    {
        await source.StartControlledAsync("overflow", _ => { entered.Set(); if (!release.Wait(5000)) throw new TimeoutException("test consumer not released"); return true; }, h => states.Enqueue(h.Health), default);
        port.Emit(1, 1); Require(entered.Wait(5000), "consumer not entered");
        for (uint n = 2; n <= 20; n++) port.Emit(n, n);
        await Until(() => states.Contains(DeviceHealth.Faulted));
        Require(source.FramesRejected > 0 && source.LastFailure?.Contains("queue full") == true, "overflow not explicit");
    }
    finally { release.Set(); await source.StopAsync(default); }
    Require(source.FramesReceived == 20 && source.FramesReceived == source.FramesDelivered + source.FramesRejected && source.FramesInFlight == 0,
        "received inputs silently disappeared from delivered/rejected accounting");
    Require(port.Destroys == 1 && lease.Releases == 1, "blocked consumer retained owner after actual completion");
    Console.WriteLine($"METRIC overflow received={source.FramesReceived} delivered={source.FramesDelivered} rejected={source.FramesRejected} inFlight={source.FramesInFlight}");
});

await Check("DualSources.PacedMetadataAndBarcodeResults17HzFor30Seconds", async () =>
{
    var m1 = new MvsPort(); var m2 = new MvsPort { Address = "192.0.2.2", Serial = "SYNTHETIC-RIGHT" };
    var r1 = new ReaderPort(); var r2 = new ReaderPort { Address = "192.0.2.2", Serial = "SYNTHETIC-RIGHT" };
    await using var a1 = Acquisition(m1, new(), "left"); await using var a2 = Acquisition(m2, new(), "right");
    await using var b1 = Reader(r1, new(), "left"); await using var b2 = Reader(r2, new(), "right");
    var left = new ConcurrentQueue<Observation>(); var right = new ConcurrentQueue<Observation>();
    var health = new ConcurrentQueue<DeviceHealth>(); int fabricated = 0;
    await a1.StartControlledAsync("metadata-left", _ => { Interlocked.Increment(ref fabricated); return true; }, h => health.Enqueue(h.Health), default);
    await a2.StartControlledAsync("metadata-right", _ => { Interlocked.Increment(ref fabricated); return true; }, h => health.Enqueue(h.Health), default);
    await b1.StartControlledAsync("barcode-left", o => { left.Enqueue(o); return true; }, _ => { }, default);
    await b2.StartControlledAsync("barcode-right", o => { right.Enqueue(o); return true; }, _ => { }, default);
    Console.WriteLine("RESOURCE " + Wire.Encode(Resources("paced-start")));
    var timer = Stopwatch.StartNew();
    for (uint n = 1; n <= 510; n++)
    {
        var remaining = TimeSpan.FromSeconds(n / 17.0) - timer.Elapsed;
        if (remaining > TimeSpan.Zero) await Task.Delay(remaining);
        m1.Enqueue(n); m2.Enqueue(n);
        var trigger = (n + 1) / 2; // two frames for each synthetic entity; every entity has the same barcode.
        r1.Emit(trigger, n); r2.Emit(trigger, n);
        if (n % 170 == 0) Console.WriteLine($"METRIC paced seconds={timer.Elapsed.TotalSeconds:F2} perSideProduced={n} leftDelivered={b1.FramesDelivered} rightDelivered={b2.FramesDelivered}");
    }
    await Until(() => a1.Frames == 510 && a2.Frames == 510 && b1.FramesDelivered == 510 && b2.FramesDelivered == 510);
    await a1.StopAsync(default); await a2.StopAsync(default); await b1.StopAsync(default); await b2.StopAsync(default);
    Require(m1.Freed == 510 && m2.Freed == 510 && a1.Failure.Length == 0 && a2.Failure.Length == 0 && fabricated == 0 && !health.Contains(DeviceHealth.Ready),
        "MVS metadata missing, not returned, or incorrectly considered decoded");
    foreach (var pair in new[] { (Device: "left", Items: left.ToArray()), (Device: "right", Items: right.ToArray()) })
    {
        Require(pair.Items.Length == 510 && pair.Items.All(o => o.DeviceId == pair.Device && o.Codes.SequenceEqual(["SYNTHETIC-SAME"])) &&
            pair.Items.Select(o => o.Sequence).SequenceEqual(Enumerable.Range(1, 510).Select(n => (long)n)), "position, full candidates or monotonic sequence changed");
        Require(pair.Items.GroupBy(o => o.AdmissionId).Count() == 255 && pair.Items.GroupBy(o => o.AdmissionId).All(g => g.Count() == 2),
            "same trigger split into entities or same barcode merged different triggers");
    }
    Require(!left.Select(o => o.AdmissionId).Intersect(right.Select(o => o.AdmissionId)).Any() && b1.FramesRejected == 0 && b2.FramesRejected == 0, "cross-side identity overlap or lost input");
    Console.WriteLine("RESOURCE " + Wire.Encode(Resources("paced-end")));
    Console.WriteLine("METRIC paced perSide=510 returnedMetadata=1020 deliveredObservations=1020 candidateEntitiesPerSide=255; no pixels/decoder/Application/RCS exercised in this case");
});

Console.WriteLine($"LOCAL_SIMULATION_RESULT passed={passed} failed={failed} skipped=0; nativeDeviceCalls=0; realPixels=0; independentKnownFailuresNotWaived=true");
Environment.ExitCode = failed == 0 ? 0 : 1;

sealed class LeaseCounter : IDisposable { public int Releases; public void Dispose() => Interlocked.Increment(ref Releases); }
sealed class MvsPort : IMvsApi
{
    public string Address = "192.0.2.1", Serial = "SYNTHETIC-LEFT";
    public volatile bool Online = true, ThrowAcquire;
    readonly ConcurrentQueue<MvsFrame> pending = new();
    public int Starts, Stops, Destroys, Shutdowns, Freed;
    public bool HasFrame { get; private set; }
    public void Enqueue(uint n)
    {
        // Bound the test producer too; this is NOT a model of vendor overflow policy.
        if (pending.Count >= 3) throw new InvalidOperationException("bounded test producer full");
        pending.Enqueue(new(1, 3072, 2048, MvsNative.Mono8, 6291456, n, 0, 0));
    }
    public void Initialize() { }
    public MvsDevice[] Enumerate() => [new(Address, Serial, "SYNTHETIC", "SYNTHETIC", [])];
    public void Create(MvsDevice device) { }
    public void Open() { }
    public uint EnumValue(string key) => key == "PixelFormat" ? MvsNative.Mono8 : 0;
    public long PayloadSize() => 6291456;
    public void ConfigureBuffers(uint nodes) { if (nodes != 3) throw new Exception("test expects default node budget"); }
    public void Start() => Starts++;
    public MvsFrame? Acquire()
    {
        if (ThrowAcquire) throw new IOException("Synthetic acquisition error");
        if (HasFrame) throw new InvalidOperationException("previous loan still held");
        if (pending.TryDequeue(out var frame)) { HasFrame = true; return frame; }
        Thread.Sleep(2); return null;
    }
    public void Release() { if (!HasFrame) throw new InvalidOperationException("duplicate free"); HasFrame = false; Freed++; }
    public bool Connected() => Online;
    public void Stop() => Stops++;
    public void Close() { }
    public void Destroy() { if (HasFrame) throw new InvalidOperationException("destroy before free"); Destroys++; }
    public void Shutdown() => Shutdowns++;
}
sealed class ReaderPort : IHikrobotApi
{
    public string Address = "192.0.2.1", Serial = "SYNTHETIC-LEFT";
    public int Stops, Destroys;
    public Action<HikFrame>? Callback;
    public HikDevice[] Enumerate() => [new(Address, Serial, "SYNTHETIC", "SYNTHETIC", [])];
    public void Create(HikDevice device) { }
    public void Open() { }
    public uint TriggerMode() => 1;
    public void Register(Action<HikFrame> frame, Action<Exception> failure, Encoding encoding) => Callback = frame;
    public static HikFrame Frame(uint trigger, uint frame) => new(trigger, frame, 0, 0, 0, 0, ["SYNTHETIC-SAME"]);
    public void Emit(uint trigger, uint frame) => Callback!(Frame(trigger, frame));
    public void Start() { }
    public void Stop() => Stops++;
    public void Close() { }
    public void Destroy() => Destroys++;
}

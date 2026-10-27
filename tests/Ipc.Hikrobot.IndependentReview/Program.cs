using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Ipc.Drivers;
using Ipc.Drivers.Hikrobot;
using Ipc.Drivers.Hikrobot.Mvs;
using Ipc.Platform;

// Synthetic review regressions against already-built e15631fe assemblies.
// No vendor calls, real devices, production configuration or existing assertions.
int passed = 0, failed = 0;
void Require(bool condition, string detail) { if (!condition) throw new InvalidOperationException(detail); }
async Task Case(string name, Func<Task> test)
{
    try { await test(); passed++; Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex.GetType().Name + ": " + ex.Message); }
}
async Task Until(Func<bool> condition)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    while (!condition()) await Task.Delay(5, timeout.Token);
}
MvsAcquisition Mvs(MvsFake api, Lease lease, Action<string>? phase = null, string? serial = null)
{
    var p = new Dictionary<string, string> { ["device.ip"] = "192.0.2.1" };
    if (serial is not null) p["device.serial"] = serial;
    return new(new("review.mvs", "configured-left", p), api, _ => lease, phase);
}
HikrobotReader Reader(ReaderFake api, Lease lease, string? serial = null)
{
    var p = new Dictionary<string, string> { ["device.ip"] = "192.0.2.1", ["entity.mode"] = "trigger" };
    if (serial is not null) p["device.serial"] = serial;
    return new(new("review.reader", "configured-left", p), api, _ => lease);
}

await Case("MVS.ConcurrentDisposeStartMustLeaveNoNativeOwner", async () =>
{
    var api = new MvsFake(); var lease = new Lease();
    using var stopped = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
    var armed = 1;
    var source = Mvs(api, lease, phase => { if (phase == "stopped" && Interlocked.Exchange(ref armed, 0) == 1) { stopped.Set(); release.Wait(); } });
    try
    {
        await source.StartControlledAsync("dispose-one", _ => true, _ => { }, default);
        var disposal = source.DisposeAsync().AsTask();
        Require(stopped.Wait(5000), "teardown did not reach stopped boundary");
        var restarting = source.StartControlledAsync("dispose-two", _ => true, _ => { }, default);
        release.Set();
        try { await restarting.WaitAsync(TimeSpan.FromSeconds(5)); } catch (ObjectDisposedException) { }
        await disposal.WaitAsync(TimeSpan.FromSeconds(5));
        Console.WriteLine($"OBSERVE dispose completed: nativeStarts={api.Starts}; nativeStops={api.Stops}; leasesReleased={lease.Releases}; phase={source.Phase}");
        Require(api.Starts == api.Stops, "Dispose completed while a concurrent registration retained a running native acquisition");
    }
    finally { release.Set(); await source.StopAsync(default); await source.DisposeAsync(); }
});

await Case("MVS.RepeatedStartRefusedAndStopIdempotent", async () =>
{
    var api = new MvsFake(); var lease = new Lease(); await using var source = Mvs(api, lease);
    await source.StartControlledAsync("first", _ => true, _ => { }, default);
    bool refused = false;
    try { await source.StartControlledAsync("second", _ => true, _ => { }, default); } catch (InvalidOperationException) { refused = true; }
    await source.StopAsync(default); await source.StopAsync(default);
    Require(refused && api.Starts == 1 && api.Stops == 1 && lease.Releases == 1, "duplicate owner or duplicate teardown");
});

await Case("MVS.FrameReturningDuringStopIsFreedBeforeDestroy", async () =>
{
    var api = new MvsFake(); var lease = new Lease();
    using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
    api.Acquiring = () => { entered.Set(); release.Wait(); };
    api.Frames.Enqueue(MvsFake.Frame(1)); await using var source = Mvs(api, lease);
    await source.StartControlledAsync("late-frame", _ => throw new Exception("No observation permitted"), _ => { }, default);
    Require(entered.Wait(5000), "acquisition not entered");
    Task stopping;
    try { stopping = source.StopAsync(default); Require(!stopping.IsCompleted && lease.Releases == 0, "early release while acquire in flight"); }
    finally { release.Set(); }
    await stopping.WaitAsync(TimeSpan.FromSeconds(5));
    Require(api.Freed == 1 && !api.HasFrame && api.Destroyed == 1, "late loan was not returned before teardown");
});

await Case("MVS.InvalidGeometryAndPixelFormatRefused", () =>
{
    var invalid = new[] { MvsFake.Frame(1) with { Data = 0 }, MvsFake.Frame(1) with { Width = uint.MaxValue, Height = uint.MaxValue },
        MvsFake.Frame(1) with { PixelType = 0 }, MvsFake.Frame(1) with { Length = 1 }, MvsFake.Frame(1) with { SubImages = 1 },
        MvsFake.Frame(1) with { ExtraType = 1 }, MvsFake.Frame(1) with { Length = ulong.MaxValue } };
    foreach (var frame in invalid)
    {
        bool refused = false; try { MvsAcquisition.ValidateFrame(frame, 8388608); } catch (InvalidDataException) { refused = true; }
        Require(refused, "invalid frame metadata accepted");
    }
    return Task.CompletedTask;
});

await Case("MVS.ExclusiveOpenFailureRollsBackWithoutStarting", async () =>
{
    var api = new MvsFake { FailOpen = true }; var lease = new Lease(); var health = new ConcurrentQueue<DeviceHealth>();
    await using var source = Mvs(api, lease);
    bool refused = false;
    try { await source.StartControlledAsync("occupied-surrogate", _ => true, h => health.Enqueue(h.Health), default); }
    catch (IOException) { refused = true; }
    Require(refused && api.Starts == 0 && api.Destroyed == 1 && lease.Releases == 1 && !health.Contains(DeviceHealth.Ready),
        "exclusive-open failure did not roll back the original owner");
});

await Case("MVS.IdentityChangesWhileRunningFenceAcquisition", async () =>
{
    var api = new MvsFake(); await using var source = Mvs(api, new());
    await source.StartControlledAsync("identity-change", _ => true, _ => { }, default);
    api.Serial = "SYNTHETIC-RIGHT";
    await Until(() => source.Failure.Length != 0);
    Require(source.Failure.Contains("identity changed") && api.Starts == 1, "wrong live identity not fenced or automatically restarted");
});

await Case("Reader.AddressReuseMustNotRebindConfiguredLeft", async () =>
{
    var api = new ReaderFake(); var lease = new Lease(); var seen = new ConcurrentQueue<Observation>();
    await using var source = Reader(api, lease);
    await source.StartControlledAsync("physical-one", o => { seen.Enqueue(o); return true; }, _ => { }, default);
    await source.StopAsync(default); api.Serial = "SYNTHETIC-RIGHT";
    bool refused = false;
    try { await source.StartControlledAsync("physical-two", o => { seen.Enqueue(o); return true; }, _ => { }, default); }
    catch (Exception ex) when (ex is IOException or InvalidDataException or ObservationConnectionException) { refused = true; }
    if (!refused) { api.Emit(1, 1); await Until(() => !seen.IsEmpty); }
    Console.WriteLine($"OBSERVE address reuse: openedSerial={api.OpenedSerial}; deliveredDevice={seen.LastOrDefault()?.DeviceId}; refused={refused}");
    Require(refused, "a different physical serial at the old IP was accepted and labeled configured-left");
});

await Case("Reader.PinnedSerialRejectsAddressReuse", async () =>
{
    var api = new ReaderFake { Serial = "SYNTHETIC-RIGHT" }; await using var source = Reader(api, new(), "SYNTHETIC-LEFT");
    bool refused = false;
    try { await source.StartControlledAsync("pinned", _ => true, _ => { }, default); }
    catch (Exception ex) when (ex is IOException or InvalidDataException or ObservationConnectionException) { refused = true; }
    Require(refused && api.Starts == 0, "configured physical serial ignored");
});

await Case("Reader.ThrowingHealthReceiverMustNotPreventNativeTeardown", async () =>
{
    var api = new ReaderFake(); var lease = new Lease(); bool throwing = true; var source = Reader(api, lease);
    try
    {
        await source.StartControlledAsync("health-throw", _ => true, h => { if (throwing && h.Health == DeviceHealth.Stopping) throw new IOException("synthetic host notification failure"); }, default);
        try { await source.StopAsync(default); } catch (IOException) { }
        Console.WriteLine($"OBSERVE reader stop notification failed: stops={api.Stops}; destroys={api.Destroyed}; leasesReleased={lease.Releases}");
        Require(api.Stops == 1 && api.Destroyed == 1 && lease.Releases == 1, "host notification exception prevented all native cleanup");
    }
    finally { throwing = false; await source.StopAsync(default); await source.DisposeAsync(); }
});

await Case("Reader.CallbackExceptionFencesAndNeverReturnsReady", async () =>
{
    var api = new ReaderFake(); var states = new ConcurrentQueue<DeviceHealth>(); await using var source = Reader(api, new());
    await source.StartControlledAsync("consumer-throws", _ => throw new IOException("SYNTHETIC-PRIVATE-BARCODE"), h => states.Enqueue(h.Health), default);
    api.Emit(1, 1); await Until(() => states.Contains(DeviceHealth.Faulted)); await Task.Delay(1100);
    Require(states.Last() == DeviceHealth.Faulted && !source.ConfirmationSource.Contains("PRIVATE-BARCODE"), "callback failure leaked contents or recovered readiness");
});

await Case("ObservationSession.ReconnectRejectsOldEpoch", async () =>
{
    var source = new SessionFake(); var session = new ObservationSession(source); var count = 0;
    await session.StartAsync(o => { Require(session.AcceptSequence(o), "sequence not accepted"); count++; return true; }, default);
    var old = source.Receive!; var oldEpoch = session.Epoch;
    Observation O(string epoch, long n) => new(Wire.Id(), source.DeviceId, epoch, "event" + n, n, "admission", "Read", ["SYNTHETIC"], null, DateTimeOffset.UtcNow);
    Require(old(O(oldEpoch, 1)), "initial input not accepted");
    await session.ReconnectAsync(o => { Require(session.AcceptSequence(o), "sequence not accepted"); count++; return true; }, default);
    Require(session.Epoch != oldEpoch && !old(O(oldEpoch, 2)) && source.Receive!(O(session.Epoch, 1)) && count == 2, "old registration entered new generation");
});

await Case("Journal.OrdinaryParcelMetadataMustNotPersistRawBarcode", async () =>
{
    var directory = Path.Combine(Path.GetTempPath(), "ipc-hik-review-journal-" + Wire.Id());
    const string barcode = "SYNTHETIC-PRIVATE-BARCODE-ONLY";
    await using (var journal = new RuntimeJournal(directory, _ => { })) journal.Write(new { Event = "ParcelObserved", ParcelCode = barcode, Level = "信息" });
    var content = await File.ReadAllTextAsync(Path.Combine(directory, "runtime.jsonl"));
    Console.WriteLine("OBSERVE raw barcode persisted=" + content.Contains(barcode) + "; synthetic log=" + directory);
    Require(!content.Contains(barcode), "ordinary diagnostic metadata persisted ParcelCode verbatim");
});

foreach (var scenario in new[] { "split-crlf-coalesced", "empty", "control", "overlong", "invalid-utf8", "partial-eof", "silent", "callback-throw" })
await Case("TCP." + scenario, async () =>
{
    using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
    var binding = new DeviceBinding("parcel-reader", "tcp-line", "review.tcp", "1") { Parameters = new()
        { ["device.ip"] = "127.0.0.1", ["device.port"] = ((IPEndPoint)listener.LocalEndpoint).Port.ToString(), ["liveness-timeout-seconds"] = "2" } };
    var seen = new ConcurrentQueue<Observation>(); var states = new ConcurrentQueue<DeviceHealth>();
    await using var source = new TcpLineObservationSource(binding);
    await source.StartControlledAsync("tcp-review", o => { if (scenario == "callback-throw") throw new IOException("SYNTHETIC-SECRET"); seen.Enqueue(o); return true; }, h => states.Enqueue(h.Health), default);
    using var peer = await listener.AcceptTcpClientAsync(); var stream = peer.GetStream();
    if (scenario == "split-crlf-coalesced")
    {
        var bytes = Encoding.UTF8.GetBytes("合成\r\nSAME\nSAME\n");
        await stream.WriteAsync(bytes.AsMemory(0, 1)); await stream.WriteAsync(bytes.AsMemory(1));
        await Until(() => seen.Count == 3);
        var observations = seen.ToArray();
        Require(observations.Select(o => o.Codes[0]).SequenceEqual(["合成", "SAME", "SAME"]) && observations.All(o => o.Kind == "UncorrelatedRead") &&
            observations.Select(o => o.Sequence).SequenceEqual([1L, 2L, 3L]) && observations.Select(o => o.AdmissionId).Distinct().Count() == 3, "framing, identity or observe-only broken");
        return;
    }
    var payload = scenario switch
    {
        "empty" => "\nOK\n"u8.ToArray(), "control" => "BAD\0\nOK\n"u8.ToArray(), "overlong" => Encoding.UTF8.GetBytes(new string('X', 4097) + "\nOK\n"),
        "invalid-utf8" => new byte[] { 0xff, 10, 79, 75, 10 }, "partial-eof" => "PARTIAL"u8.ToArray(), "callback-throw" => "SYNTHETIC\n"u8.ToArray(), _ => Array.Empty<byte>()
    };
    if (payload.Length != 0) await stream.WriteAsync(payload);
    if (scenario == "partial-eof") peer.Close();
    await Until(() => states.Contains(DeviceHealth.Faulted));
    Require(seen.IsEmpty && states.Last() == DeviceHealth.Faulted && source.LastFailure is not null && !source.LastFailure.Contains("SECRET") && !listener.Pending(), "invalid stream continued, auto-reconnected or exposed input");
});

Console.WriteLine($"INDEPENDENT_REVIEW_RESULT passed={passed} failed={failed} skipped=0; synthetic only; real hardware NOT_RUN");
Environment.ExitCode = failed == 0 ? 0 : 1;

sealed class Lease : IDisposable { public int Releases; public void Dispose() => Releases++; }
sealed class MvsFake : IMvsApi
{
    public string Serial = "SYNTHETIC-LEFT";
    public bool FailOpen;
    public int Starts, Stops, Freed, Destroyed;
    public Action? Acquiring;
    public ConcurrentQueue<MvsFrame> Frames = new();
    public bool HasFrame { get; private set; }
    public static MvsFrame Frame(uint n) => new(1, 3072, 2048, MvsNative.Mono8, 6291456, n, 0, 0);
    public void Initialize() { }
    public MvsDevice[] Enumerate() => [new("192.0.2.1", Serial, "SYNTHETIC", "SYNTHETIC", [])];
    public void Create(MvsDevice device) { }
    public void Open() { if (FailOpen) throw new IOException("Synthetic exclusive-open failure; not a measured vendor occupancy error"); }
    public uint EnumValue(string key) => key == "PixelFormat" ? MvsNative.Mono8 : 0;
    public long PayloadSize() => 6291456;
    public void ConfigureBuffers(uint nodes) { }
    public void Start() => Starts++;
    public MvsFrame? Acquire() { Acquiring?.Invoke(); if (Frames.TryDequeue(out var frame)) { HasFrame = true; return frame; } Thread.Sleep(5); return null; }
    public void Release() { if (HasFrame) { HasFrame = false; Freed++; } }
    public bool Connected() => true;
    public void Stop() => Stops++;
    public void Close() { }
    public void Destroy() { if (HasFrame) throw new Exception("Outstanding loan"); Destroyed++; }
    public void Shutdown() { }
}
sealed class ReaderFake : IHikrobotApi
{
    public string Serial = "SYNTHETIC-LEFT", OpenedSerial = "";
    public int Starts, Stops, Destroyed;
    public Action<HikFrame>? Callback;
    public HikDevice[] Enumerate() => [new("192.0.2.1", Serial, "SYNTHETIC", "SYNTHETIC", [])];
    public void Create(HikDevice device) => OpenedSerial = device.Serial;
    public void Open() { }
    public uint TriggerMode() => 1;
    public void Register(Action<HikFrame> frame, Action<Exception> failure, Encoding encoding) => Callback = frame;
    public void Emit(uint trigger, uint frame) => Callback!(new(trigger, frame, 0, 0, 0, 0, ["SYNTHETIC-CODE"]));
    public void Start() => Starts++;
    public void Stop() => Stops++;
    public void Close() { }
    public void Destroy() => Destroyed++;
}
sealed class SessionFake : IControlledObservationSource
{
    public string DeviceId => "synthetic.session";
    public string Epoch { get; private set; } = "not-started";
    public string ConfirmationSource => "synthetic";
    public Func<Observation, bool>? Receive;
    public Task StartControlledAsync(string epoch, Func<Observation, bool> receive, Action<DeviceHealthConfirmation> health, CancellationToken token) { Epoch = epoch; Receive = receive; return Task.CompletedTask; }
    public Task StopAsync(CancellationToken token) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using Ipc.Drivers.Hikrobot;
using Ipc.Platform;

internal static class Verification
{
    static int checks;
    static void Check(bool valid, string label) { if (!valid) throw new InvalidOperationException("FAIL: " + label); checks++; Console.WriteLine("PASS: " + label); }
    static async Task Until(Func<bool> valid)
    { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)); while (!valid()) await Task.Delay(5, timeout.Token); }
    internal static async Task Run()
    {
        BufferChecks();
        await PolicyVerification.Run();
        var api = new FakeApi(); var lease = new TestLease();
        var received = new ConcurrentQueue<Observation>(); var health = new ConcurrentQueue<DeviceHealthConfirmation>();
        await using var reader = Reader(api, lease);
        await reader.StartControlledAsync("epoch-one", o => { received.Enqueue(o); return true; }, health.Enqueue, CancellationToken.None);
        api.Emit(1, 1, "SYNTHETIC-001"); api.Emit(1, 2, "SYNTHETIC-001"); api.Emit(2, 3, "SYNTHETIC-001");
        await Until(() => received.Count == 3);
        var batch = received.ToArray();
        Check(batch[0].AdmissionId == batch[1].AdmissionId && batch[1].AdmissionId != batch[2].AdmissionId, "same trigger retains entity candidate; same code on new trigger stays distinct");
        Check(batch.Select(o => o.SourceEventId).Distinct().Count() == 3 && batch.Select(o => o.Sequence).SequenceEqual([1L, 2L, 3L]), "frame identities and monotonic ingress sequence retained");
        Check(health.Any(h => h.Health == DeviceHealth.Ready && h.Source.StartsWith("SDK:") && h.Epoch == "epoch-one"), "SDK health uses registered epoch and explicit source");
        var late = api.Frame!;
        await reader.StopAsync(CancellationToken.None); late(new(3, 4, 0, 0, 0, 0, ["SYNTHETIC-LATE"]));
        Check(received.Count == 3 && lease.Releases == 1 && api.Calls.TakeLast(3).SequenceEqual(["stop", "close", "destroy"]), "stop drains before close/destroy and ignores late callbacks");
        await reader.StartControlledAsync("epoch-two", o => { received.Enqueue(o); return true; }, health.Enqueue, CancellationToken.None);
        api.Emit(1, 1, "SYNTHETIC-001"); await Until(() => received.Count == 4);
        Check(received.Last().Epoch == "epoch-two" && received.Last().AdmissionId != batch[0].AdmissionId, "reconnect uses new host epoch without reusing old admission identity");
        late(new(99, 99, 0, 0, 0, 0, ["SYNTHETIC-OLD-EPOCH"]));
        Check(received.Count == 4 && reader.LastFailure is null, "old registration callback cannot be relabeled into the new epoch");
        api.Emit(0, 2, "SYNTHETIC-COUNTER-RESET");
        Check(reader.LastFailure?.Contains("counter reversed") == true, "trigger counter reversal/wrap faults rather than merging different entities");
        await reader.StopAsync(CancellationToken.None);

        var observeApi = new FakeApi();
        await using (var observe = Reader(observeApi, new(), "observe-only"))
        {
            Observation? seen = null;
            await observe.StartControlledAsync("observe", o => { seen = o; return true; }, _ => { }, CancellationToken.None);
            observeApi.Emit(1, 1, "SYNTHETIC-OBSERVE"); await Until(() => seen is not null);
            Check(seen!.Kind == "UncorrelatedRead", "default observation cannot become business admission");
        }
        var badMode = new FakeApi { Mode = 0 }; var failedLease = new TestLease();
        await using (var bad = Reader(badMode, failedLease))
        {
            await Reject(() => bad.StartControlledAsync("bad-mode", _ => true, _ => { }, CancellationToken.None));
            Check(failedLease.Releases == 1 && !badMode.Calls.Contains("start"), "trigger mode mismatch fails without changing camera or leaking handle/lease");
        }
        var wrongIp = new FakeApi();
        await using (var missing = new HikrobotReader(new("test", "reader", new Dictionary<string, string>() { ["device.ip"] = "192.0.2.55" }), wrongIp, _ => new TestLease()))
        {
            await Reject(() => missing.StartControlledAsync("missing", _ => true, _ => { }, CancellationToken.None));
            Check(!wrongIp.Calls.Contains("create"), "IP matching never falls back to first enumerated device");
        }
        var aliasRoot = Path.Combine(Path.GetTempPath(), "ipc-hik-alias-test-" + Wire.Id());
        IDisposable Lease(string serial) => new ResourceLease(aliasRoot, [serial]);
        await using (var first = new HikrobotReader(new("first", "device-alias-a", new Dictionary<string, string> { ["device.ip"] = "192.0.2.1" }), new FakeApi(), Lease))
        await using (var second = new HikrobotReader(new("second", "device-alias-b", new Dictionary<string, string> { ["device.ip"] = "192.0.2.1" }), new FakeApi(), Lease))
        {
            await first.StartControlledAsync("first", _ => true, _ => { }, CancellationToken.None);
            await Reject(() => second.StartControlledAsync("second", _ => true, _ => { }, CancellationToken.None));
            await first.StopAsync(CancellationToken.None);
            await second.StartControlledAsync("second-retry", _ => true, _ => { }, CancellationToken.None);
            Check(true, "same physical serial rejects different business aliases, then becomes available after original teardown");
        }
        var blockedApi = new FakeApi(); var blockedLease = new TestLease();
        await using (var blocked = Reader(blockedApi, blockedLease, capacity: 1))
        {
            using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
            var states = new ConcurrentQueue<DeviceHealthConfirmation>();
            await blocked.StartControlledAsync("overflow", _ => { entered.Set(); release.Wait(); return true; }, states.Enqueue, CancellationToken.None);
            blockedApi.Emit(1, 1, "SYNTHETIC-1"); if (!entered.Wait(5000)) throw new TimeoutException();
            blockedApi.Emit(2, 2, "SYNTHETIC-2"); blockedApi.Emit(3, 3, "SYNTHETIC-3");
            Check(states.Last().Health == DeviceHealth.Faulted && blocked.LastFailure!.Contains("queue full"), "bounded overflow faults instead of silently continuing Ready");
            var stopping = blocked.StopAsync(CancellationToken.None);
            await Until(() => blockedApi.Calls.Contains("stop"));
            Check(!stopping.IsCompleted && blockedLease.Releases == 0 && !blockedApi.Calls.Contains("destroy"), "blocked consumer retains SDK and lease during stop");
            release.Set(); await stopping;
            Check(blockedLease.Releases == 1 && blocked.FramesRejected == 1, "faulted queued observation counted and teardown finishes after consumer");
        }
        var stopApi = new FakeApi { FailStop = true }; var stopLease = new TestLease();
        await using (var retry = Reader(stopApi, stopLease))
        {
            await retry.StartControlledAsync("stop-failure", _ => true, _ => { }, CancellationToken.None);
            await Reject(() => retry.StopAsync(CancellationToken.None));
            Check(stopLease.Releases == 0 && !stopApi.Calls.Contains("destroy"), "failed native stop retains resources");
            stopApi.FailStop = false; await retry.StopAsync(CancellationToken.None);
            Check(stopLease.Releases == 1 && stopApi.Calls.Count(c => c == "destroy") == 1, "explicit stop retry releases original owner once");
        }
        var healthApi = new FakeApi(); var healthStates = new ConcurrentQueue<DeviceHealthConfirmation>();
        await using (var connection = Reader(healthApi, new()))
        {
            await connection.StartControlledAsync("health", _ => true, healthStates.Enqueue, CancellationToken.None);
            healthApi.FailHealth = true; await Until(() => connection.LastFailure is not null);
            Check(healthStates.Last().Health == DeviceHealth.Faulted, "failed real health roundtrip closes readiness without reconnect/retrigger");
        }
        Console.WriteLine("SELF_TEST_PASS checks=" + checks);
    }
    static HikrobotReader Reader(FakeApi api, TestLease lease, string mode = "trigger", int capacity = 128) => new(
        new("test", "reader", new Dictionary<string, string>() { ["device.ip"] = "192.0.2.1", ["entity.mode"] = mode, ["queue.capacity"] = capacity.ToString() }), api, _ => lease);
    static async Task Reject(Func<Task> action)
    {
        try { await action(); } catch (Exception ex) when (ex is IOException or InvalidDataException) { return; }
        throw new InvalidOperationException("Expected explicit failure");
    }
    static void BufferChecks()
    {
        Check(Marshal.SizeOf<NativeMethods.FrameHeader>() == 96 && Marshal.OffsetOf<NativeMethods.FrameHeader>("Codes").ToInt32() == 72, "x64 callback prefix ABI matches SDK evidence");
        var frame = Marshal.AllocHGlobal(96); var codes = Marshal.AllocHGlobal(4 + 2 * 4628);
        try
        {
            Marshal.StructureToPtr(new NativeMethods.FrameHeader { Trigger = 7, Frame = 9 }, frame, false);
            Check(NativeMethods.CopyFrame(frame, Encoding.UTF8).Codes.Length == 0, "SDK no-code flag permits an absent optional code list");
            Marshal.StructureToPtr(new NativeMethods.FrameHeader { HasCode = 1 }, frame, false);
            bool missingCodeRejected = false;
            try { NativeMethods.CopyFrame(frame, Encoding.UTF8); } catch (InvalidDataException) { missingCodeRejected = true; }
            Check(missingCodeRejected, "reported code without its native list faults instead of inventing no-code");
            Marshal.Copy(new byte[4 + 2 * 4628], 0, codes, 4 + 2 * 4628);
            Marshal.StructureToPtr(new NativeMethods.FrameHeader { Trigger = 7, Frame = 9, Codes = codes }, frame, false);
            var empty = NativeMethods.CopyFrame(frame, new UTF8Encoding(false, true));
            Check(empty.Codes.Length == 0 && empty.Trigger == 7, "no-code frame preserves its own trigger");
            Marshal.WriteInt32(codes, 1); Marshal.WriteInt32(codes, 4 + 4100, 5); Marshal.Copy(Encoding.UTF8.GetBytes("A B \0"), 0, codes + 8, 5);
            Check(NativeMethods.CopyFrame(frame, new UTF8Encoding(false, true)).Codes.Single() == "A B ", "length-aware decode removes only terminal NUL and preserves spaces");
            Marshal.WriteInt32(codes, 2); Marshal.WriteInt32(codes, 4 + 4628 + 4100, 1); Marshal.WriteByte(codes, 8 + 4628, (byte)'C');
            Check(NativeMethods.CopyFrame(frame, new UTF8Encoding(false, true)).Codes.Length == 2, "multiple codes remain multiple without selecting a winner");
            Marshal.WriteInt32(codes, 301); bool countRejected = false;
            try { NativeMethods.CopyFrame(frame, Encoding.UTF8); } catch (InvalidDataException) { countRejected = true; }
            Check(countRejected, "out-of-ABI code count rejected before reading records");
            Marshal.WriteInt32(codes, 1); Marshal.WriteInt32(codes, 4 + 4100, 4097); bool lengthRejected = false;
            try { NativeMethods.CopyFrame(frame, Encoding.UTF8); } catch (InvalidDataException) { lengthRejected = true; }
            Check(lengthRejected, "out-of-ABI code length rejected before copy");
            Marshal.WriteInt32(codes, 4 + 4100, 1); Marshal.WriteByte(codes, 8, 0xff); bool encodingRejected = false;
            try { NativeMethods.CopyFrame(frame, new UTF8Encoding(false, true)); } catch (DecoderFallbackException) { encodingRejected = true; }
            Check(encodingRejected, "invalid configured encoding never replaces bytes into a valid code");
        }
        finally { Marshal.FreeHGlobal(frame); Marshal.FreeHGlobal(codes); }
    }
    internal sealed class TestLease : IDisposable { public int Releases; public void Dispose() => Releases++; }
    internal sealed class FakeApi : IHikrobotApi
    {
        public ConcurrentQueue<string> Calls = new(); public uint Mode = 1; public bool FailStop, FailHealth;
        public Action<HikFrame>? Frame;
        public HikDevice[] Enumerate() => [new("192.0.2.1", "synthetic-camera", "synthetic", "synthetic", [])];
        public void Create(HikDevice device) => Calls.Enqueue("create");
        public void Open() => Calls.Enqueue("open");
        public uint TriggerMode() => FailHealth ? throw new IOException("Synthetic SDK health loss") : Mode;
        public void Register(Action<HikFrame> frame, Action<Exception> failure, Encoding encoding) => Frame = frame;
        public void Emit(uint trigger, uint frame, string code) => Frame!(new(trigger, frame, 0, 0, 0, 0, [code]));
        public void Start() => Calls.Enqueue("start");
        public void Stop() { Calls.Enqueue("stop"); if (FailStop) throw new IOException("Synthetic native stop failure"); }
        public void Close() => Calls.Enqueue("close");
        public void Destroy() => Calls.Enqueue("destroy");
    }
}

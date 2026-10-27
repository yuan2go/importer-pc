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
            await Until(() => states.Any(s => s.Health == DeviceHealth.Faulted));
            Check(states.Last().Health == DeviceHealth.Faulted && blocked.LastFailure!.Contains("queue full"), "bounded overflow faults instead of silently continuing Ready");
            var stopping = blocked.StopAsync(CancellationToken.None);
            await Until(() => blockedApi.Calls.Contains("stop"));
            Check(!stopping.IsCompleted && blockedLease.Releases == 0 && !blockedApi.Calls.Contains("destroy"), "blocked consumer retains SDK and lease during stop");
            release.Set(); await stopping;
            Check(blockedLease.Releases == 1 && blocked.FramesRejected == 2 && blocked.FramesReceived == blocked.FramesDelivered + blocked.FramesRejected + blocked.FramesInFlight,
                "overflow and faulted queued frame both counted; received equals delivered plus rejected plus in-flight after teardown");
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
            healthApi.FailHealth = true; await Until(() => healthStates.Any(h => h.Health == DeviceHealth.Faulted));
            Check(healthStates.Last().Health == DeviceHealth.Faulted, "failed real health roundtrip closes readiness without reconnect/retrigger");
        }
        var hungApi = new FakeApi(); var hungLease = new TestLease();
        using (var entered = new ManualResetEventSlim())
        using (var release = new ManualResetEventSlim())
        await using (var hung = Reader(hungApi, hungLease))
        {
            await hung.StartControlledAsync("native-wait", _ => true, _ => { }, default);
            hungApi.Enumerating = () => { entered.Set(); release.Wait(); };
            try
            {
                Check(entered.Wait(4000), "native health enumeration entered");
                using var cancel = new CancellationTokenSource(50);
                try { await hung.StopAsync(cancel.Token); throw new Exception("Native call was falsely terminated"); }
                catch (OperationCanceledException) { Check(hungLease.Releases == 0 && !hungApi.Calls.Contains("destroy"), "cancelled Stop wait retains actual blocked native owner and handle"); }
                var retry = hung.StopAsync(default);
                Check(!retry.IsCompleted, "repeat Stop joins original outstanding teardown");
                release.Set(); await retry.WaitAsync(TimeSpan.FromSeconds(5));
                Check(hungLease.Releases == 1 && hungApi.Calls.Count(x => x == "destroy") == 1, "native return allows exactly one destroy and lease release");
            }
            finally { release.Set(); }
        }
        var startApi = new FakeApi(); var startLease = new TestLease();
        using (var entered = new ManualResetEventSlim())
        using (var release = new ManualResetEventSlim())
        using (var cancel = new CancellationTokenSource())
        await using (var starting = Reader(startApi, startLease))
        {
            startApi.Enumerating = () => { entered.Set(); release.Wait(); };
            var start = starting.StartControlledAsync("cancel-start", _ => true, _ => { }, cancel.Token);
            try
            {
                Check(entered.Wait(3000), "startup native enumeration entered before cancellation"); cancel.Cancel();
                Check(!start.IsCompleted, "cancellation cannot end already entered synchronous native call");
                release.Set();
                try { await start; throw new Exception("Cancelled native start became Ready"); } catch (OperationCanceledException) { }
                Check(!startApi.Calls.Contains("create") && !startApi.Calls.Contains("start"), "returned cancelled enumeration cannot acquire handle or start grabbing");
            }
            finally { release.Set(); }
        }
        var rejectedApi = new FakeApi();
        await using (var rejected = Reader(rejectedApi, new()))
        {
            await rejected.StartControlledAsync("copy-failure", _ => true, _ => { }, default);
            rejectedApi.Failure!(new HikFrameRejectedException(new InvalidDataException("synthetic malformed native frame")));
            Check(rejected.FramesReceived == 1 && rejected.FramesRejected == 1 && rejected.FramesInFlight == 0 && rejected.ConfirmationSource.Contains("malformed native frame"),
                "native buffer rejection belongs to conservation and public reason");
        }
        await CallbackIsolation();
        await ByteBudget();
        await DeadlineChecks();
        await MvsVerification.Run();
        Console.WriteLine("SELF_TEST_PASS checks=" + checks);
    }
    static async Task CallbackIsolation()
    {
        var api = new FakeApi(); var lease = new TestLease();
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        await using var reader = Reader(api, lease);
        await reader.StartControlledAsync("fault-publisher", _ => true, state =>
        { if (state.Health == DeviceHealth.Faulted) { entered.Set(); release.Wait(); } }, default);
        try
        {
            var callback = Task.Run(() => api.Failure!(new IOException("Synthetic SDK disconnect")));
            await callback.WaitAsync(TimeSpan.FromSeconds(2));
            Check(entered.Wait(2000), "fault reaches independent host publisher");
            api.Emit(1, 1, "SYNTHETIC-LATE");
            Check(reader.FramesRejected == 1 && reader.LastFailure is not null, "blocked host health cannot block native failure/frame callback or reopen ingress");
            var stopping = reader.StopAsync(default);
            Check(!stopping.IsCompleted && lease.Releases == 0, "stop retains owner while original health publication is in flight");
            release.Set(); await stopping.WaitAsync(TimeSpan.FromSeconds(5));
            Check(lease.Releases == 1 && reader.Phase == "stopped", "health drain precedes one physical owner release");
        }
        finally { release.Set(); }
    }
    static async Task ByteBudget()
    {
        var api = new FakeApi(); var lease = new TestLease();
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        await using var reader = new HikrobotReader(new("budget", "reader", new Dictionary<string, string>
        { ["device.ip"] = "192.0.2.1", ["entity.mode"] = "trigger", ["queue.maximum-code-bytes"] = "1048576" }), api, _ => lease);
        await reader.StartControlledAsync("bytes", _ => { entered.Set(); release.Wait(); return true; }, _ => { }, default);
        try
        {
            var codes = Enumerable.Repeat(new string('X', 4096), 100).ToArray();
            api.Frame!(new(1, 1, 0, 0, 0, 0, codes));
            Check(entered.Wait(2000), "representative multi-candidate batch enters consumer");
            api.Frame!(new(2, 2, 0, 0, 0, 0, codes));
            Check(reader.LastFailure?.Contains("byte budget") == true && reader.FramesReceived == 2 && reader.FramesRejected == 1,
                "byte budget includes blocked in-flight candidate strings before count capacity fills");
            release.Set(); await reader.StopAsync(default);
            Check(reader.FramesInFlight == 0 && reader.ConfirmationSource.Contains("bufferedBytes=0") && lease.Releases == 1,
                "drained byte budget and observation conservation return to zero");
        }
        finally { release.Set(); }
        var burst = new FakeApi(); var burstLease = new TestLease();
        await using var startup = Reader(burst, burstLease);
        burst.Starting = () => { burst.Emit(1, 1, "SYNTHETIC-FIRST"); throw new IOException("Synthetic native start failure after callback"); };
        await Reject(() => startup.StartControlledAsync("start-burst", _ => throw new Exception("Must not deliver failed startup"), _ => { }, default));
        Check(startup.FramesReceived == 1 && startup.FramesRejected == 1 && startup.FramesInFlight == 0 &&
            startup.ConfirmationSource.Contains("bufferedBytes=0") && burstLease.Releases == 1, "startup callback is explicitly rejected and accounted when native Start fails before consumer creation");
    }
    static async Task DeadlineChecks()
    {
        var output = new ConcurrentQueue<string>(); var phase = "native-start";
        using var deadline = new DiagnosticDeadline(TimeSpan.FromMilliseconds(30), () => phase, output.Enqueue);
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var owned = Task.Run(() => { entered.Set(); release.Wait(); });
        try
        {
            Check(entered.Wait(2000), "diagnostic owns blocked native-stage surrogate");
            await Until(() => deadline.Expired);
            Check(!owned.IsCompleted && deadline.Token.IsCancellationRequested && output.Single().Contains("phase=native-start") &&
                output.Single().Contains("ownership=retained"), "overall deadline reports active phase without claiming native completion or releasing owner");
            phase = "stop"; deadline.Stage(phase);
            release.Set(); await owned.WaitAsync(TimeSpan.FromSeconds(2));
            Check(output.Last().Contains("PHASE stop") && output.Last().Contains("deadlineExpired=True"), "teardown phase remains visible after overall budget expires");
        }
        finally { release.Set(); }
    }
    static HikrobotReader Reader(FakeApi api, TestLease lease, string mode = "trigger", int capacity = 128) => new(
        new("test", "reader", new Dictionary<string, string>() { ["device.ip"] = "192.0.2.1", ["entity.mode"] = mode, ["queue.capacity"] = capacity.ToString() }), api, _ => lease);
    static async Task Reject(Func<Task> action)
    {
        try { await action(); } catch (Exception ex) when (ex is IOException or InvalidDataException or ObservationConnectionException) { return; }
        throw new InvalidOperationException("Expected explicit failure");
    }
    static void BufferChecks()
    {
        Check(Marshal.SizeOf<NativeMethods.FrameHeader>() == 96 && Marshal.OffsetOf<NativeMethods.FrameHeader>("Codes").ToInt32() == 72, "x64 callback prefix ABI matches SDK evidence");
        var frame = Marshal.AllocHGlobal(96); var codes = Marshal.AllocHGlobal(4 + 3 * 4628);
        try
        {
            Marshal.StructureToPtr(new NativeMethods.FrameHeader { Trigger = 7, Frame = 9 }, frame, false);
            Check(NativeMethods.CopyFrame(frame, Encoding.UTF8).Codes.Length == 0, "SDK no-code flag permits an absent optional code list");
            Marshal.StructureToPtr(new NativeMethods.FrameHeader { HasCode = 1 }, frame, false);
            bool missingCodeRejected = false;
            try { NativeMethods.CopyFrame(frame, Encoding.UTF8); } catch (InvalidDataException) { missingCodeRejected = true; }
            Check(missingCodeRejected, "reported code without its native list faults instead of inventing no-code");
            Marshal.Copy(new byte[4 + 3 * 4628], 0, codes, 4 + 3 * 4628);
            Marshal.StructureToPtr(new NativeMethods.FrameHeader { Trigger = 7, Frame = 9, Codes = codes }, frame, false);
            var empty = NativeMethods.CopyFrame(frame, new UTF8Encoding(false, true));
            Check(empty.Codes.Length == 0 && empty.Trigger == 7, "no-code frame preserves its own trigger");
            Marshal.WriteInt32(codes, 1); Marshal.WriteInt32(codes, 4 + 4100, 5); Marshal.Copy(Encoding.UTF8.GetBytes("A B \0"), 0, codes + 8, 5);
            Check(NativeMethods.CopyFrame(frame, new UTF8Encoding(false, true)).Codes.Single() == "A B ", "length-aware decode removes only terminal NUL and preserves spaces");
            Marshal.WriteInt32(codes, 2); Marshal.WriteInt32(codes, 4 + 4628 + 4100, 1); Marshal.WriteByte(codes, 8 + 4628, (byte)'C');
            Check(NativeMethods.CopyFrame(frame, new UTF8Encoding(false, true)).Codes.Length == 2, "multiple codes remain multiple without selecting a winner");
            Marshal.WriteInt32(codes, 3); Marshal.WriteInt32(codes, 4 + 2 * 4628 + 4100, 8); Marshal.Copy(Encoding.UTF8.GetBytes("VALID003"), 0, codes + 8 + 2 * 4628, 8);
            Check(NativeMethods.CopyFrame(frame, new UTF8Encoding(false, true)).Codes.SequenceEqual(["A B ", "C", "VALID003"]), "third candidate is preserved in original order for business filtering");
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
        public Action<Exception>? Failure;
        public Action? Enumerating, Starting;
        public HikDevice[] Enumerate() { Enumerating?.Invoke(); return [new("192.0.2.1", "synthetic-camera", "synthetic", "synthetic", [])]; }
        public void Create(HikDevice device) => Calls.Enqueue("create");
        public void Open() => Calls.Enqueue("open");
        public uint TriggerMode() => FailHealth ? throw new IOException("Synthetic SDK health loss") : Mode;
        public void Register(Action<HikFrame> frame, Action<Exception> failure, Encoding encoding) { Frame = frame; Failure = failure; }
        public void Emit(uint trigger, uint frame, string code) => Frame!(new(trigger, frame, 0, 0, 0, 0, [code]));
        public void Start() { Calls.Enqueue("start"); Starting?.Invoke(); }
        public void Stop() { Calls.Enqueue("stop"); if (FailStop) throw new IOException("Synthetic native stop failure"); }
        public void Close() => Calls.Enqueue("close");
        public void Destroy() => Calls.Enqueue("destroy");
    }
}

using System.Diagnostics;
using System.Reflection;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Platform;
using Microsoft.Data.Sqlite;

static class TimeStorageFaultChecks
{
    internal sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Utc = DateTimeOffset.UtcNow;
        long ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override DateTimeOffset GetUtcNow() => Utc;
        public override long GetTimestamp() => Interlocked.Read(ref ticks);
        public void Elapse(TimeSpan elapsed) => Interlocked.Add(ref ticks, elapsed.Ticks);
    }
    sealed class ClockFactory(BusinessLaunch launch, ManualTime clock) : IParcelCapabilityFactory
    {
        readonly CommercialProductization.SyntheticFactory inner = new(launch);
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls;
        public ParcelConfiguration Normalize(ParcelConfiguration c) => inner.Normalize(c);
        public void Validate(ParcelConfiguration c) => inner.Validate(c);
        public void ValidateStructure(ParcelConfiguration c) => inner.ValidateStructure(c);
        public ParcelPorts Create(ParcelConfiguration c) { var p = inner.Create(c); return p with { Wcs = new ClockPort(p.Wcs!, this, clock) }; }
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
    sealed class ClockPort(IWcsPort inner, ClockFactory owner, ManualTime clock) : IWcsPort
    {
        public string InstanceId => inner.InstanceId;
        public Task StartAsync(CancellationToken t) => inner.StartAsync(t);
        public Task StopAsync(CancellationToken t) => inner.StopAsync(t);
        public Task ReconnectAsync(CancellationToken t) => inner.ReconnectAsync(t);
        public Task<CapabilityHealth> HealthAsync(CancellationToken t) => inner.HealthAsync(t);
        public ValueTask DisposeAsync() => inner.DisposeAsync();
        public Task<StageReply?> QueryAsync(FrozenRequest r, CancellationToken t) => inner.QueryAsync(r, t);
        public async Task<StageReply> ResolveSlotAsync(FrozenRequest r, CancellationToken t)
        {
            Interlocked.Increment(ref owner.Calls); owner.Entered.TrySetResult();
            await owner.Release.Task.WaitAsync(t); clock.Utc = clock.Utc.AddHours(-1); clock.Elapse(TimeSpan.FromMilliseconds(20));
            return (await inner.ResolveSlotAsync(r, t)) with { At = clock.GetUtcNow() };
        }
    }
    static async Task Until(Func<Task<bool>> condition)
    { var w = Stopwatch.StartNew(); while (!await condition()) { if (w.Elapsed > TimeSpan.FromSeconds(8)) throw new TimeoutException(); await Task.Delay(10); } }
    public static async Task Run(string workspace, string root)
    {
        var checks = new List<object>(); var failures = new List<string>();
        void Check(bool ok, string name, object data)
        { checks.Add(new { Name = name, Passed = ok, Data = data }); Console.WriteLine((ok ? "PASS " : "FAIL ") + name); if (!ok) failures.Add(name); }
        var source = Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace, "config/sportmaster/business.json"))) with { Login = null, BackupSchedule = null };
        BusinessLaunch Launch(string name)
        { var d = Path.Combine(root, name); Directory.CreateDirectory(d); ExtensionStore.AtomicWrite(Path.Combine(d, "business.json"), source); return new(d, Path.Combine(d, "data"), null, null); }
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        var capacityClock = new ManualTime(); var capacityLaunch = Launch("capacity");
        await using (var runtime = new ParcelRuntime(capacityLaunch, new SportmasterFlow(), new CommercialProductization.SyntheticFactory(capacityLaunch))
        { RuntimeIdentityPreflight = () => { }, StorageTimeForVerification = capacityClock })
        {
            await runtime.StartAsync(default);
            var gate = (SemaphoreSlim)typeof(ParcelRuntime).GetField("gate", hidden)!.GetValue(runtime)!;
            await gate.WaitAsync();
            try
            {
                typeof(ParcelRuntime).GetField("freeStorageBytes", hidden)!.SetValue(runtime, long.MaxValue);
                capacityClock.Utc = capacityClock.Utc.AddHours(-12); capacityClock.Elapse(TimeSpan.FromSeconds(2));
                typeof(ParcelRuntime).GetMethod("RefreshStorageCapacity", hidden)!.Invoke(runtime, null);
                var value = (long)typeof(ParcelRuntime).GetField("freeStorageBytes", hidden)!.GetValue(runtime)!;
                Check(value != long.MaxValue, "ST12 capacity throttle survives UTC rollback", new { UtcJumpHours = -12, MonotonicElapsedSeconds = 2, ProbeRan = value != long.MaxValue });
            }
            finally { gate.Release(); }
        }
        var receiptClock = new ManualTime(); var receiptLaunch = Launch("receipt"); var receiptFactory = new ClockFactory(receiptLaunch, receiptClock);
        await using (var runtime = new ParcelRuntime(receiptLaunch, new SportmasterFlow(), receiptFactory)
        { RuntimeIdentityPreflight = () => { }, ReceiptTimeForVerification = receiptClock })
        {
            await runtime.StartAsync(default);
            Check(runtime.EmitReferenceAt("left", "clock-admission", "SYNTHETIC", "clock-event"), "ST13 synthetic handoff is durable", new { SimulationOnly = true });
            await receiptFactory.Entered.Task.WaitAsync(TimeSpan.FromSeconds(8)); receiptFactory.Release.TrySetResult();
            await Until(async () => (await runtime.SnapshotAsync()).Operations.Any(x => x.Wcs?.Replies.Length > 0));
            var op = (await runtime.SnapshotAsync()).Operations.Single();
            Check(op.State == ParcelState.SlotResolved && op.Wcs!.State == StageState.Accepted && receiptFactory.Calls == 1,
                "ST13 in-flight UTC rollback does not manufacture Conflict", new { op.State, Stage = op.Wcs!.State, receiptFactory.Calls, Prepared = op.Wcs.Request.PreparedAt, Reply = op.Wcs.Replies.Single().At });
        }
        var ioLaunch = Launch("io");
        await using (var runtime = new ParcelRuntime(ioLaunch, new SportmasterFlow(), new CommercialProductization.SyntheticFactory(ioLaunch)) { RuntimeIdentityPreflight = () => { } })
        {
            await runtime.StartAsync(default); var attempts = 0;
            runtime.Boundary = name => { if (name == "BeforeIngressDurable" && Interlocked.Increment(ref attempts) <= 2) throw new IOException("Synthetic pre-write sharing jitter"); };
            var received = runtime.EmitReferenceAt("left", "io-admission", "SYNTHETIC", "io-event");
            Check(received && attempts == 3, "ST08 bounded pre-write I/O jitter recovers without disabling ingress", new { Received = received, Attempts = attempts });
        }
        ExtensionStore.AtomicWrite(Path.Combine(root, "TIME-STORAGE-CHECKS.json"), new { Checks = checks, Failed = failures });
        if (failures.Count != 0) throw new InvalidOperationException("Time/storage regressions: " + string.Join("; ", failures));
    }
}

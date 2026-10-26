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
        public int Queries;
        public StageReply? ProviderReply;
        public bool RollbackQuery;
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
        public Task<StageReply?> QueryAsync(FrozenRequest r, CancellationToken t)
        {
            t.ThrowIfCancellationRequested(); Interlocked.Increment(ref owner.Queries);
            if (owner.RollbackQuery) { clock.Utc = clock.Utc.AddHours(-1); clock.Elapse(TimeSpan.FromMilliseconds(20)); }
            return Task.FromResult(owner.ProviderReply);
        }
        public async Task<StageReply> ResolveSlotAsync(FrozenRequest r, CancellationToken t)
        {
            Interlocked.Increment(ref owner.Calls); owner.Entered.TrySetResult();
            await owner.Release.Task.WaitAsync(t); clock.Utc = clock.Utc.AddHours(-1); clock.Elapse(TimeSpan.FromMilliseconds(20));
            return owner.ProviderReply = (await inner.ResolveSlotAsync(r, t)) with { At = clock.GetUtcNow() };
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
                typeof(ParcelRuntime).GetField("freeStorageBytes", hidden)!.SetValue(runtime, long.MaxValue);
                capacityClock.Utc = capacityClock.Utc.AddDays(2);
                typeof(ParcelRuntime).GetMethod("RefreshStorageCapacity", hidden)!.Invoke(runtime, null);
                var skipped = (long)typeof(ParcelRuntime).GetField("freeStorageBytes", hidden)!.GetValue(runtime)! == long.MaxValue;
                Check(skipped, "ST12 UTC forward jump does not bypass interval", new { UtcJumpDays = 2, MonotonicElapsedSeconds = 0, Skipped = skipped });
                typeof(ParcelRuntime).GetField("retentionWarning", hidden)!.SetValue(runtime, "synthetic-stale-retention-probe");
                capacityClock.Utc = capacityClock.Utc.AddDays(-3); capacityClock.Elapse(TimeSpan.FromSeconds(61));
                typeof(ParcelRuntime).GetMethod("RefreshStorageCapacity", hidden)!.Invoke(runtime, null);
                var retentionRan = (string?)typeof(ParcelRuntime).GetField("retentionWarning", hidden)!.GetValue(runtime) != "synthetic-stale-retention-probe";
                Check(retentionRan, "ST12 retention notification interval also survives rollback", new { MonotonicElapsedSeconds = 61, RetentionProbeRan = retentionRan });
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
            var reply = op.Wcs.Replies.Single();
            Check(reply.LocalClockEvidence is not null && ParcelReceiptChronology.Valid(op.Wcs.Request, reply, receiptClock.GetUtcNow()),
                "ST13 rollback has request-bound durable local evidence", new { HasProof = reply.LocalClockEvidence is not null });
            Check(!ParcelReceiptChronology.Valid(op.Wcs.Request, reply with { LocalClockEvidence = null }, receiptClock.GetUtcNow()),
                "ST13 earlier reply without witnessed rollback remains invalid", new { OriginalUtcRetained = reply.At });
            Check(!ParcelReceiptChronology.Valid(op.Wcs.Request, reply with { Evidence = "tampered evidence" }, receiptClock.GetUtcNow()) &&
                  !ParcelReceiptChronology.Valid(op.Wcs.Request, reply with { At = reply.At.AddYears(-1) }, receiptClock.GetUtcNow()),
                "ST13 rollback proof cannot waive changed evidence or arbitrary old timestamps", new { NoBlanketTimeWaiver = true });
            await ManagementModeFixture.Authorize(runtime);
            var gate = (SemaphoreSlim)typeof(ParcelRuntime).GetField("gate", hidden)!.GetValue(runtime)!;
            await gate.WaitAsync();
            try
            {
                var repo = (ParcelRepository)typeof(ParcelRuntime).GetField("repository", hidden)!.GetValue(runtime)!;
                var originalHash = repo.OriginalReplyHash(op.Wcs.Request.Id, reply.Id);
                async Task<ParcelOperation> QueryOriginal(ParcelOperation current) => await (Task<ParcelOperation>)typeof(ParcelRuntime)
                    .GetMethod("ExecuteStageAsync", hidden)!.Invoke(runtime, [current, ParcelStage.Wcs, true, null, null, null])!;
                var repeated = await QueryOriginal(op);
                Check(repeated.Wcs!.State == StageState.Accepted && repeated.Wcs.Replies.Length == 1 &&
                      Wire.Encode(repeated.Wcs.Replies[0]) == Wire.Encode(reply) && receiptFactory.Calls == 1 && receiptFactory.Queries == 1 &&
                      repo.OriginalReplyHash(op.Wcs.Request.Id, reply.Id) == originalHash,
                    "ST13 identical provider query preserves first clock evidence and immutable reply identity", new { repeated.Wcs.State, receiptFactory.Calls, receiptFactory.Queries });
                // Deliberately remove only the in-memory projection. Durable original
                // custody must be the authority even when a reply is no longer cached.
                var residents = (Dictionary<string, ParcelOperation>)typeof(ParcelRuntime).GetField("operations", hidden)!.GetValue(runtime)!;
                residents[op.Id] = repeated with { Wcs = repeated.Wcs with { Replies = [] } };
                receiptFactory.RollbackQuery = true;
                var repeatedRollback = await QueryOriginal(residents[op.Id]);
                Check(repeatedRollback.Wcs!.State == StageState.Accepted && repeatedRollback.Wcs.Replies.Length == 1 &&
                      Wire.Encode(repeatedRollback.Wcs.Replies[0]) == Wire.Encode(reply) &&
                      repo.OriginalReplyHash(op.Wcs.Request.Id, reply.Id) == originalHash && receiptFactory.Calls == 1 && receiptFactory.Queries == 2,
                    "ST13 repeated rollback resolves original evidence from durable custody without redispatch", new { repeatedRollback.Wcs.State, receiptFactory.Calls, receiptFactory.Queries });
                var conflict = op with { State = ParcelState.Blocked, Wcs = op.Wcs with { State = StageState.Conflict } };
                var applied = (ParcelOperation)typeof(ParcelRuntime).GetMethod("ApplyReply", hidden)!.Invoke(runtime, [conflict, ParcelStage.Wcs, reply])!;
                Check(applied.State == ParcelState.Blocked && applied.Wcs!.State == StageState.Conflict,
                    "ST13 an existing Conflict is never automatically cleared", new { applied.State });
            }
            finally { gate.Release(); }
        }
        await using (var recovered = new ParcelRuntime(receiptLaunch, new SportmasterFlow(), new CommercialProductization.SyntheticFactory(receiptLaunch)) { RuntimeIdentityPreflight = () => { } })
        {
            await recovered.StartAsync(default); var op = (await recovered.SnapshotAsync()).Operations.Single();
            Check(op.State == ParcelState.SlotResolved && op.Wcs!.State == StageState.Accepted && !op.RecoveryRequired,
                "ST13 restart validates original rollback evidence without redispatch", new { op.State, op.RecoveryRequired });
        }
        var ioLaunch = Launch("io");
        var operatorClock = new ManualTime(); var session = new OperatorSession { TimeForVerification = operatorClock };
        session.Set(new("synthetic-operator", "Synthetic", "reference", Wire.Id(), operatorClock.GetUtcNow(), true, operatorClock.GetUtcNow().AddHours(2)));
        operatorClock.Elapse(TimeSpan.FromHours(1)); operatorClock.Utc = operatorClock.Utc.AddMinutes(48);
        Check(!session.Valid(operatorClock.GetUtcNow(), 3600000), "UTC inventory: operator maximum lifetime cannot be extended by rollback",
            new { MonotonicMinutes = 60, WallMinutes = 48 });
        var managementRoot = Path.Combine(root, "management"); ManagementModeFixture.AuthorizeRoot(managementRoot);
        var mode = LocalManagementMode.ForRoot(managementRoot);
        var expired = (CancellationTokenSource)typeof(LocalManagementMode).GetField("revoked", hidden)!.GetValue(mode)!; expired.Cancel();
        Check(!mode.Status().Authorized, "UTC inventory: management status respects its monotonic expiry token", new { ExpiredToken = true });
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

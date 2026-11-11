using System.Diagnostics;
using System.Text.Json;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Assembly;
using Ipc.Platform;
using Microsoft.Data.Sqlite;

// Synthetic fault injection against actual SQLite/runtime/reader ownership. No device qualification.
static partial class StabilityRemediation
{
    static void Check(bool condition, string message)
    { if (!condition) throw new Exception("FAIL STABILITY01: " + message); Console.WriteLine("PASS STABILITY01: " + message); }
    static async Task Until(Func<bool> predicate)
    { var until = Stopwatch.StartNew(); while (!predicate()) { if (until.Elapsed > TimeSpan.FromSeconds(8)) throw new TimeoutException(); await Task.Delay(10); } }
    static ParcelConfiguration Config(string workspace) => Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace, "tests/Ipc.Application.Verification/sportmaster.synthetic.json"))) with { QueueCapacity = 1, TimeoutMs = 30000 };
    static BusinessLaunch Launch(string root, ParcelConfiguration c)
    { Directory.CreateDirectory(root); ExtensionStore.AtomicWrite(Path.Combine(root, "business.json"), c); return new(root, Path.Combine(root, "data"), null, null); }
    static string DirectoryFor(BusinessLaunch launch, ParcelConfiguration c) => Path.Combine(launch.DataRoot, c.Mode.ToString(), c.StationId, c.ApplicationId);
    static long Scalar(string database, string sql)
    { using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()); db.Open(); using var q = db.CreateCommand(); q.CommandText = sql; return Convert.ToInt64(q.ExecuteScalar()); }
    static async Task<BusinessReceipt> Command(ParcelRuntime app, string kind, string? owner = null, string? value = null)
    {
        for (var n = 0; n < 100; n++)
        {
            var s = await app.SnapshotAsync(); var result = await app.CommandAsync(new(Wire.Id(), s.Revision, kind, owner, value));
            if (result.Status != "Conflict") return result;
        }
        throw new InvalidOperationException("Command did not acquire a current revision");
    }
    public static async Task Run(string workspace, string root)
    {
        await InputCustody(workspace, Path.Combine(root, "input"));
        await SlowMarker(workspace, Path.Combine(root, "slow-marker"));
        await DiagnosticRecovery(Path.Combine(root, "diagnostic"));
        await DiagnosticIdleRetention(Path.Combine(root, "diagnostic-idle"));
        await OwnedReads(workspace, Path.Combine(root, "reads"));
        await OwnedManagementStatus(workspace, Path.Combine(root, "management-status"));
        await Retention(workspace, Path.Combine(root, "retention"));
        Console.WriteLine("PASS STABILITY01 COMPLETE; synthetic fault verification, not a 24h qualification");
    }
    static async Task InputCustody(string workspace, string path)
    {
        var c = Config(workspace); var launch = Launch(path, c); var factory = new DelayedFactory(new(launch));
        var app = new ParcelRuntime(launch, new SportmasterFlow(), factory)
        { RuntimeIdentityPreflight = () => { if (c.Mode != RunMode.Simulation) throw new InvalidOperationException("Simulation fixture only"); }, Boundary = point => { if (point == "AfterRawDurable") throw new IOException("Synthetic T3 interruption"); } };
        await app.StartAsync(default); var database = Path.Combine(DirectoryFor(launch, c), "sportmaster.db");
        try
        {
            Check(app.EmitReferenceAt("left", "first", "001", "first-event"), "first delivery accepted durably");
            await factory.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Check(app.EmitReferenceAt("left", "second", "002", "second-event"), "second delivery accepted before first T3 fails");
            await CameraRoundHarness.Until(async () => (await app.SnapshotAsync()).Operations.Count(o => o.Wcs?.State == StageState.Dispatching) == 2 &&
                app.WorkerProgress().Single(w => w.Position == "left").Processed == 2);
            Check(Scalar(database, "SELECT count(*) FROM parcel_ingress WHERE state='Processed'") == 2 && Scalar(database, "SELECT count(*) FROM parcel_operations") == 2,
                "both input receipts and independent original requests are durable before either T3 finishes");
            factory.Release.TrySetResult();
            await app.FatalFailure.WaitAsync(TimeSpan.FromSeconds(5));
            Check(app.WorkerProgress().Single(x => x.Position == "right").State == "Waiting" && app.HasActiveWork,
                "request persistence failure remains fatal independently of the input worker's completed handoffs");
            Check(Scalar(database, "SELECT count(*) FROM parcel_ingress") == 2 && Scalar(database, "SELECT count(*) FROM parcel_operations") == 2,
                "T3 failure retains both input receipts and each parcel's original entity without deleting or merging either responsibility");
        }
        finally
        {
            factory.Release.TrySetResult();
            try { await app.DisposeAsync(); } catch (InvalidOperationException) { Check(!app.HasUnreleasedResources, "terminal failure reported after actual safe resource release"); }
        }
        var recoveryFactory = new CommercialProductization.SyntheticFactory(launch);
        await using var recovered = new ParcelRuntime(launch, new SportmasterFlow(), recoveryFactory) { RuntimeIdentityPreflight = () => { if (c.Mode != RunMode.Simulation) throw new InvalidOperationException("Simulation fixture only"); } };
        await recovered.StartAsync(default); await ManagementModeFixture.Authorize(recovered);
        var snapshot = await recovered.SnapshotAsync();
        Check(snapshot.Operations.Length == 2 && snapshot.Operations.All(o => o.Wcs is { State: StageState.Accepted, Attempts: 1 }) && recoveryFactory.Sends == 0 &&
            Scalar(database, "SELECT count(*) FROM parcel_ingress WHERE state='Interrupted'") == 0,
            "restart skips old camera inputs, reconciles original raw locally and never resends or replays queued input");
        var receipt = await Command(recovered, "IngressResponsibilities");
        using var json = JsonDocument.Parse(receipt.Reason);
        Check(json.RootElement.GetArrayLength() == 0, "old scan recovery leaves no operator input-inspection work");
        Check(Scalar(database, "SELECT count(*) FROM parcel_ingress WHERE state='Processed'") == 2 && Scalar(database, "SELECT count(*) FROM parcel_operations") == 2,
            "automatic skip clears only input barriers while preserving original request/attempt/entity ledger");
    }
    static async Task SlowMarker(string workspace, string path)
    {
        var c = Config(workspace); var launch = Launch(path, c); var factory = new DelayedFactory(new(launch));
        await using var app = new ParcelRuntime(launch, new SportmasterFlow(), factory) { RuntimeIdentityPreflight = () => { if (c.Mode != RunMode.Simulation) throw new InvalidOperationException("Simulation fixture only"); } };
        await app.StartAsync(default);
        Task<bool>? waiting = null;
        try
        {
            Check(app.EmitReferenceAt("left", "slow-first", "001", "slow-first-event"), "slow-marker first left input");
            await factory.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Check(app.EmitReferenceAt("left", "slow-second", "002", "slow-second-event"), "left second input is admitted while its predecessor awaits RCS");
            waiting = Task.Run(() => app.EmitReferenceAt("left", "backpressure", "003", "waiting-event"));
            Check(await waiting.WaitAsync(TimeSpan.FromSeconds(5)), "a held predecessor does not block the third parcel's input handoff");
            await CameraRoundHarness.Until(async () => (await app.SnapshotAsync()).Operations.Count(o => o.PositionId == "left" && o.Wcs?.State == StageState.Dispatching) == 3);
            var watch = Stopwatch.StartNew();
            Check(app.EmitReferenceAt("right", "independent", "004", "right-event"), "other position handoff does not acquire the full lane lock");
            ParcelSnapshot s;
            do { s = await app.SnapshotAsync().WaitAsync(TimeSpan.FromSeconds(2)); if (s.Operations.Any(o => o.AdmissionId == "independent" && o.State == ParcelState.SlotResolved)) break; await Task.Delay(10); }
            while (watch.Elapsed < TimeSpan.FromSeconds(5));
            Check(s.Operations.Any(o => o.AdmissionId == "independent" && o.State == ParcelState.SlotResolved) && !factory.Release.Task.IsCompleted,
                "right business reaches actual accepted raw/T3 while all three left originals independently await their responses");
        }
        finally { factory.Release.TrySetResult(); }
        await CameraRoundHarness.Until(async () => (await app.SnapshotAsync()).Operations.Count(o => o.PositionId == "left" && o.State == ParcelState.SlotResolved) == 3);
        Check(waiting is { IsCompletedSuccessfully: true } && await waiting, "all three independent left results finish without replaying an input or releasing a business queue");
    }
    static async Task DiagnosticRecovery(string path)
    {
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim(); int first = 0;
        await using var journal = new RuntimeJournal(path, _ => { });
        journal.BeforeAppend = () => { if (Interlocked.Increment(ref first) == 1) { entered.Set(); if (!release.Wait(10000)) throw new TimeoutException(); } };
        try
        {
            journal.Write(new { Event = "first" }); Check(entered.Wait(3000), "diagnostic worker delayed independently");
            for (var n = 0; n < 400; n++) journal.Write(new { Event = "load", n });
            using (var s = JsonDocument.Parse(Wire.Encode(journal.Status)))
                Check(s.RootElement.GetProperty("Dropped").GetInt64() > 0 && s.RootElement.GetProperty("QueueDepth").GetInt32() <= 256, "diagnostic overload remains bounded and counted");
        }
        finally { release.Set(); }
        await Until(() => { using var s = JsonDocument.Parse(Wire.Encode(journal.Status)); return s.RootElement.GetProperty("QueueDepth").GetInt32() == 0 && s.RootElement.GetProperty("State").GetString() == "Running"; });
        using (var held = new FileStream(Path.Combine(path, "runtime.jsonl"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            journal.Write(new { Event = "locked" });
            await Until(() => Wire.Encode(journal.Status).Contains("Degraded"));
        }
        await Until(() => { using var s = JsonDocument.Parse(Wire.Encode(journal.Status)); return s.RootElement.GetProperty("State").GetString() == "Running"; });
        using var final = JsonDocument.Parse(Wire.Encode(journal.Status));
        Check(final.RootElement.GetProperty("State").GetString() == "Running" && final.RootElement.GetProperty("Dropped").GetInt64() > 0 &&
            final.RootElement.GetProperty("LastFailure").ValueKind == JsonValueKind.String, "successful recovery probe restores diagnostics; historical failure and loss counter retained");
    }
    static async Task DiagnosticIdleRetention(string path)
    {
        await using var journal = new RuntimeJournal(path, _ => { }, 2);
        journal.Write(new { Event = "Warmup" });
        await Until(() => { using var s = JsonDocument.Parse(Wire.Encode(journal.Status)); return s.RootElement.GetProperty("Written").GetInt64() == 1; });
        var current = Path.Combine(path, "runtime.jsonl");
        var archive = current + ".1";
        File.WriteAllText(archive, "Synthetic expired diagnostic metadata\n");
        File.SetLastWriteTimeUtc(archive, DateTime.UtcNow.AddDays(-5));
        File.SetLastWriteTimeUtc(current, DateTime.UtcNow.AddDays(-5));
        // No producer call follows warmup. Only the periodic owner may rotate/expire.
        await Until(() => !File.Exists(current) && !File.Exists(archive) && !File.Exists(current + ".2"));
        using var result = JsonDocument.Parse(Wire.Encode(journal.Status));
        Check(result.RootElement.GetProperty("State").GetString() == "Running" && result.RootElement.GetProperty("Written").GetInt64() == 1,
            "healthy idle diagnostics rotate and expire on their timer without a new entry or recovery probe");
    }
    static async Task OwnedReads(string workspace, string path)
    {
        var c = Config(workspace); var launch = Launch(path, c);
        await using var app = new ParcelRuntime(launch, new SportmasterFlow(), new CommercialProductization.SyntheticFactory(launch)) { RuntimeIdentityPreflight = () => { if (c.Mode != RunMode.Simulation) throw new InvalidOperationException("Simulation fixture only"); } };
        await app.StartAsync(default);
        int entered = 0;
        app.BeforeRead = async token => { Interlocked.Increment(ref entered); await Task.Delay(Timeout.Infinite, token); };
        var selection = await app.SelectHistoryAsync(new());
        var reads = Enumerable.Range(0, 4).Select(_ => app.ReadHistoryPageAsync(selection, long.MaxValue, 50, default)).ToArray();
        await Until(() => Volatile.Read(ref entered) == 4);
        try { await app.ReadHistoryPageAsync(selection, long.MaxValue, 50, default); throw new Exception("Unbounded fifth reader"); }
        catch (InvalidOperationException ex) { Check(ex.Message.Contains("并发"), "fifth concurrent query rejected without queuing an unbounded task"); }
        await app.StopAsync(default).WaitAsync(TimeSpan.FromSeconds(5));
        foreach (var read in reads) { try { await read; throw new Exception("Stopped read completed unexpectedly"); } catch (OperationCanceledException) { } }
        using var progress = JsonDocument.Parse(Wire.Encode(app.RuntimeProgress()));
        Check(progress.RootElement.GetProperty("ReadOperations").GetInt32() == 0, "stop cancels and drains every actual reader before disposal");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { using var reader = new ParcelHistoryReader(Path.Combine(DirectoryFor(launch, c), "sportmaster.db"), cancelled.Token); throw new Exception("Cancelled SQLite session opened"); }
        catch (OperationCanceledException) { Check(true, "cancelled SQLite read never establishes a retained transaction"); }
        using var activeCancel = new CancellationTokenSource();
        using var actualReader = new ParcelHistoryReader(Path.Combine(DirectoryFor(launch, c), "sportmaster.db"), activeCancel.Token);
        var sqlite = (SqliteConnection)typeof(ParcelHistoryReader).GetField("db", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(actualReader)!;
        var begun = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var longQuery = Task.Run(() =>
        {
            using var q = sqlite.CreateCommand(); q.CommandText = "WITH RECURSIVE work(n) AS (SELECT 1 UNION ALL SELECT n+1 FROM work WHERE n<100000000) SELECT sum(n) FROM work";
            begun.TrySetResult(); return q.ExecuteScalar();
        });
        await begun.Task; await Task.Delay(50); activeCancel.Cancel();
        try { await longQuery.WaitAsync(TimeSpan.FromSeconds(3)); throw new Exception("Long SQL ignored cancellation"); }
        catch (SqliteException ex) { Check(ex.SqliteErrorCode == 9, "active SQLite engine work interrupted by the owned reader progress handler, not just cancelled wait"); }
    }
    internal static async Task OwnedManagementStatus(string workspace, string path)
    {
        var c = Config(workspace); var launch = Launch(path, c);
        await using var app = new ParcelRuntime(launch, new SportmasterFlow(), new CommercialProductization.SyntheticFactory(launch))
        { RuntimeIdentityPreflight = () => { if (c.Mode != RunMode.Simulation) throw new InvalidOperationException("Synthetic management ownership fixture only"); } };
        await app.StartAsync(default); await ManagementModeFixture.Authorize(app);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        app.BeforeManagementStatusRead = async token => { entered.TrySetResult(); await release.Task.WaitAsync(token); };
        var first = app.ManagementModeStatusAsync(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var observers = Enumerable.Range(0, 32).Select(_ => app.ManagementModeStatusAsync()).ToArray();
        Check(observers.All(t => ReferenceEquals(t, first)), "32 overlapping management observers share one actual read, not command slots or queued tasks");
        using (var progress = JsonDocument.Parse(Wire.Encode(app.RuntimeProgress())))
            Check(progress.RootElement.GetProperty("ManagementStatusOperations").GetInt32() == 1 && progress.RootElement.GetProperty("AdministrationOperations").GetInt32() == 0,
                "management observation ownership is independently visible and bounded to one");
        await app.ExitManagementModeAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Check(!first.IsCompleted, "revocation command completes while the independent observation is blocked");
        release.TrySetResult();
        Check(!(await first).Authorized && (await Task.WhenAll(observers)).All(s => !s.Authorized), "all observers receive the actual revoked authority result");
        await ManagementModeFixture.Authorize(app);
        Check((await app.ManagementModeStatusAsync()).Authorized, "completed status is never cached across the next authority change");
        entered = new(TaskCreationOptions.RunContinuationsAsynchronously); release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var stoppedRead = app.ManagementModeStatusAsync(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await app.StopAsync(default).WaitAsync(TimeSpan.FromSeconds(5));
        try { await stoppedRead; throw new Exception("Stopped management observation completed unexpectedly"); } catch (OperationCanceledException) { }
        using var stopped = JsonDocument.Parse(Wire.Encode(app.RuntimeProgress()));
        Check(stopped.RootElement.GetProperty("ManagementStatusOperations").GetInt32() == 0, "stop cancels and drains the coalesced authority observation");
    }
    sealed class DelayedFactory(CommercialProductization.SyntheticFactory inner) : IParcelCapabilityFactory
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ParcelConfiguration Normalize(ParcelConfiguration c) => inner.Normalize(c);
        public void Validate(ParcelConfiguration c) => inner.Validate(c);
        public ParcelPorts Create(ParcelConfiguration c) { var p = inner.Create(c); return p with { Wcs = new DelayedWcs(p.Wcs!, Entered, Release) }; }
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
    sealed class DelayedWcs(IWcsPort inner, TaskCompletionSource entered, TaskCompletionSource release) : IWcsPort
    {
        public string InstanceId => inner.InstanceId;
        public Task StartAsync(CancellationToken t) => inner.StartAsync(t);
        public Task<CapabilityHealth> HealthAsync(CancellationToken t) => inner.HealthAsync(t);
        public Task ReconnectAsync(CancellationToken t) => inner.ReconnectAsync(t);
        public async Task<StageReply> ResolveSlotAsync(FrozenRequest r, CancellationToken token)
        { if (Wire.Decode<ParcelPayload>(r.Payload).PositionId == "left") { entered.TrySetResult(); await release.Task.WaitAsync(TimeSpan.FromSeconds(10)); } return await inner.ResolveSlotAsync(r, token); }
        public Task<StageReply?> QueryAsync(FrozenRequest r, CancellationToken t) => inner.QueryAsync(r, t);
        public Task StopAsync(CancellationToken t) => inner.StopAsync(t);
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}

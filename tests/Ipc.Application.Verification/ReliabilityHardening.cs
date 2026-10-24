using System.Diagnostics;
using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow;
using Ipc.Application.Sportmaster;
using Ipc.Assembly;
using Ipc.Platform;
using Microsoft.Data.Sqlite;

static partial class ReliabilityHardening
{
    static void Check(bool condition, string message)
    { if (!condition) throw new Exception("FAIL H02: " + message); Console.WriteLine("PASS H02: " + message); }
    static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    static async Task Until(Func<bool> predicate)
    { var watch = Stopwatch.StartNew(); while (!predicate()) { if (watch.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException(); await Task.Delay(10); } }
    static ParcelConfiguration Config(string workspace) => Wire.Decode<ParcelConfiguration>(File.ReadAllText(Path.Combine(workspace, "config/sportmaster/business.json"))) with { TimeoutMs = 30000, QueueCapacity = 1 };
    static BusinessLaunch Launch(string root, ParcelConfiguration c)
    { Directory.CreateDirectory(root); ExtensionStore.AtomicWrite(Path.Combine(root, "business.json"), c); return new(root, Path.Combine(root, "data"), null, null); }
    static string Data(BusinessLaunch l, ParcelConfiguration c) => Path.Combine(l.DataRoot, c.Mode.ToString(), c.StationId, c.ApplicationId);
    static long Scalar(string database, string sql)
    { using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()); db.Open(); using var cmd = db.CreateCommand(); cmd.CommandText = sql; return Convert.ToInt64(cmd.ExecuteScalar()); }
    public static async Task Run(string workspace, string root)
    {
        List<Exception> failures = [];
        foreach (var test in new (string Name, Func<Task> Run)[] {
            ("startup-stop", () => StartupStop(workspace, Path.Combine(root, "startup-stop"))),
            ("old-health", OldHealth),
            ("logs", () => Logs(Path.Combine(root, "logs"))),
            ("dense-logs", () => DenseLogs(Path.Combine(root, "dense-logs"))),
            ("log-ownership", () => LogOwnership(Path.Combine(root, "log-ownership"))),
            ("storage", () => Storage(workspace, Path.Combine(root, "storage"))),
            ("handoff-stop", () => HandoffStop(workspace, Path.Combine(root, "handoff-stop"))),
            ("resource-cycles", () => ResourceCycles(workspace, Path.Combine(root, "cycles"))),
            ("time", () => JournalTime(Path.Combine(root, "time"))) })
        {
            try { await test.Run(); }
            catch (Exception ex) { Console.WriteLine("FAIL CASE " + test.Name + ": " + ex); failures.Add(ex); }
        }
        if (failures.Count != 0) throw new AggregateException(failures);
    }
    static async Task StartupStop(string workspace, string path)
    {
        var c = Config(workspace); var launch = Launch(path, c);
        var entered = Signal(); var release = Signal(); int stops = 0;
        var factory = new WrappedFactory(new(launch), p => new WrappedWcs(p)
        {
            Start = async t => { entered.TrySetResult(); await release.Task.WaitAsync(TimeSpan.FromSeconds(10)); await p.StartAsync(t); },
            Stop = t => { Interlocked.Increment(ref stops); return p.StopAsync(t); }
        });
        var app = new ParcelRuntime(launch, new SportmasterFlow(), factory);
        var start = app.StartAsync(default); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        bool retained; var watch = Stopwatch.StartNew();
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        try { await app.StopAsync(cancel.Token); retained = false; }
        catch (OperationCanceledException) { retained = !start.IsCompleted && app.HasUnreleasedResources && stops == 0; }
        finally { release.TrySetResult(); }
        try { await start; } catch (OperationCanceledException) { } catch (IOException) { /* Baseline completes with the ingress fence; preserve measured failure. */ }
        await app.DisposeAsync();
        Console.WriteLine($"MEASURE H02 startup-stop wait_ms={watch.Elapsed.TotalMilliseconds:F2}; retained={retained}; stops={stops}");
        Check(retained, "cancelled Stop waits for the real startup owner without stopping/disposal underneath it");
        Check(!app.HasUnreleasedResources && !app.HasActiveWork, "startup cancellation followed by disposal releases all application ownership");
    }
    sealed class WrappedFactory(ParcelCapabilityFactory inner, Func<IWcsPort, IWcsPort> wrap) : IParcelCapabilityFactory
    {
        public ParcelConfiguration Normalize(ParcelConfiguration c) => inner.Normalize(c);
        public void Validate(ParcelConfiguration c) => inner.Validate(c);
        public ParcelPorts Create(ParcelConfiguration c) { var p = inner.Create(c); return p with { Wcs = wrap(p.Wcs!) }; }
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
    sealed class WrappedWcs(IWcsPort inner) : IWcsPort
    {
        public Func<CancellationToken, Task>? Start, Stop;
        public Func<FrozenRequest, CancellationToken, Task<StageReply>>? Resolve { get; init; }
        public string InstanceId => inner.InstanceId;
        public Task StartAsync(CancellationToken t) => Start?.Invoke(t) ?? inner.StartAsync(t);
        public Task<CapabilityHealth> HealthAsync(CancellationToken t) => inner.HealthAsync(t);
        public Task ReconnectAsync(CancellationToken t) => inner.ReconnectAsync(t);
        public Task<StageReply> ResolveSlotAsync(FrozenRequest r, CancellationToken t) => Resolve?.Invoke(r, t) ?? inner.ResolveSlotAsync(r, t);
        public Task<StageReply?> QueryAsync(FrozenRequest r, CancellationToken t) => inner.QueryAsync(r, t);
        public Task StopAsync(CancellationToken t) => Stop?.Invoke(t) ?? inner.StopAsync(t);
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
